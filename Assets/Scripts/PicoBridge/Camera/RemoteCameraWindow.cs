using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using PicoBridge.Network;

namespace PicoBridge.Camera
{
    /// <summary>
    /// Manages the video preview lifecycle.
    /// - Android: MediaDecoder JNI (H.264 over TCP)
    /// - Editor:  Built-in TCP listener receiving length-prefixed JPEG frames
    /// </summary>
    public class RemoteCameraWindow : MonoBehaviour
    {
        [Header("Video Settings")]
        public int resolutionWidth = 1280;
        public int resolutionHeight = 720;
        public int videoFps = 30;
        public int bitrate = 8 * 1024 * 1024;
        public int decoderPort = 19000;

        [Header("Display")]
        [Tooltip("Optional RawImage to display the decoded video")]
        public RawImage displayImage;

        private Texture2D _texture;
        private bool _decoderActive;
        private PicoTcpClient _tcp;

        // Editor MJPEG receiver
        private TcpListener _editorListener;
        private Thread _editorThread;
        private volatile bool _editorRunning;
        private readonly object _frameLock = new object();
        private byte[] _pendingJpeg;

        public Texture2D Texture => _texture;
        public bool IsActive => _decoderActive;

        public void StartPreview(PicoTcpClient tcp)
        {
            if (_decoderActive) return;
            _tcp = tcp;
            StartCoroutine(StartListenCoroutine());
        }

        public void StopPreview()
        {
            if (!_decoderActive) return;
            _decoderActive = false;

            StopEditorReceiver();

            if (IsAndroidDevice())
                MediaDecoder.Release();

            if (_tcp != null)
                _tcp.SendFunction("StopReceivePcCamera", "\"\"");

            if (_texture != null)
            {
                if (displayImage != null)
                    displayImage.texture = null;
                Destroy(_texture);
                _texture = null;
            }

            Debug.Log("[RemoteCameraWindow] Preview stopped");
        }

        private IEnumerator StartListenCoroutine()
        {
            Debug.Log($"[RemoteCameraWindow] Starting preview " +
                $"{resolutionWidth}x{resolutionHeight} @{videoFps}fps port={decoderPort}");

            _texture = new Texture2D(resolutionWidth, resolutionHeight,
                TextureFormat.RGB24, false, false);
            yield return null;

            if (IsAndroidDevice())
            {
                // Android: use native H.264 decoder
                MediaDecoder.Initialize(
                    (int)_texture.GetNativeTexturePtr(),
                    resolutionWidth, resolutionHeight);
                MediaDecoder.StartServer(decoderPort, false);
            }
            else
            {
                // Editor: start TCP listener for MJPEG
                StartEditorReceiver();
            }

            _decoderActive = true;
            yield return null;

            SendCameraRequest();
        }

        private void SendCameraRequest()
        {
            if (_tcp == null) return;

            string localIp = NetUtils.GetLocalIPv4();
            string codec = IsAndroidDevice() ? "h264" : "mjpeg";
            string cameraJson =
                $"{{\"ip\":\"{localIp}\",\"port\":{decoderPort}," +
                $"\"width\":{resolutionWidth},\"height\":{resolutionHeight}," +
                $"\"fps\":{videoFps},\"bitrate\":{bitrate}," +
                $"\"codec\":\"{codec}\"}}";
            _tcp.SendFunction("StartReceivePcCamera", cameraJson);
            Debug.Log($"[RemoteCameraWindow] Sent StartReceivePcCamera " +
                $"(codec={codec}) to PC");
        }

        private void Update()
        {
            if (!_decoderActive || _texture == null) return;

            if (IsAndroidDevice())
            {
                if (MediaDecoder.IsUpdateFrame())
                {
                    MediaDecoder.UpdateTexture();
                    GL.InvalidateState();
                    AssignDisplay();
                }
            }
            else
            {
                // Editor: apply pending JPEG frame
                byte[] jpeg = null;
                lock (_frameLock)
                {
                    if (_pendingJpeg != null)
                    {
                        jpeg = _pendingJpeg;
                        _pendingJpeg = null;
                    }
                }
                if (jpeg != null)
                {
                    _texture.LoadImage(jpeg);
                    AssignDisplay();
                }
            }
        }

        private void AssignDisplay()
        {
            if (displayImage != null && displayImage.texture != _texture)
                displayImage.texture = _texture;
        }

        // ── Editor MJPEG TCP receiver ────────────────────

        private void StartEditorReceiver()
        {
            _editorRunning = true;
            _editorListener = new TcpListener(IPAddress.Any, decoderPort);
            _editorListener.Start();
            _editorThread = new Thread(EditorReceiverLoop)
            {
                IsBackground = true, Name = "MjpegReceiver"
            };
            _editorThread.Start();
            Debug.Log($"[RemoteCameraWindow] Editor MJPEG listener on port {decoderPort}");
        }

        private void StopEditorReceiver()
        {
            _editorRunning = false;
            try { _editorListener?.Stop(); } catch { }
            _editorListener = null;
            _editorThread = null;
        }

        private void EditorReceiverLoop()
        {
            while (_editorRunning)
            {
                TcpClient client = null;
                try
                {
                    client = _editorListener.AcceptTcpClient();
                    Debug.Log("[RemoteCameraWindow] PC video sender connected");
                    var stream = client.GetStream();
                    var lenBuf = new byte[4];

                    while (_editorRunning && client.Connected)
                    {
                        // Read 4-byte big-endian length prefix
                        if (!ReadExact(stream, lenBuf, 4))
                            break;
                        int len = (lenBuf[0] << 24) | (lenBuf[1] << 16)
                                | (lenBuf[2] << 8) | lenBuf[3];
                        if (len <= 0 || len > 10_000_000)
                            break;

                        // Read JPEG payload
                        var jpeg = new byte[len];
                        if (!ReadExact(stream, jpeg, len))
                            break;

                        lock (_frameLock)
                            _pendingJpeg = jpeg;
                    }
                }
                catch (SocketException) { }
                catch (ObjectDisposedException) { }
                catch (System.IO.IOException) { }
                finally
                {
                    try { client?.Close(); } catch { }
                }

                if (_editorRunning)
                    Debug.Log("[RemoteCameraWindow] PC video sender disconnected, waiting...");
            }
        }

        private static bool ReadExact(NetworkStream stream, byte[] buf, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int n = stream.Read(buf, offset, count - offset);
                if (n <= 0) return false;
                offset += n;
            }
            return true;
        }

        private static bool IsAndroidDevice()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }

        private void OnDisable() => StopPreview();
        private void OnDestroy() => StopPreview();
    }
}
