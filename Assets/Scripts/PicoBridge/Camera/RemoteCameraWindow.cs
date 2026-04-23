using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using PicoBridge.Network;

namespace PicoBridge.Camera
{
    /// <summary>
    /// Manages the video preview lifecycle: creates a Texture2D, initialises
    /// MediaDecoder, starts its TCP server, and sends StartReceivePcCamera
    /// to the PC bridge so it begins streaming H.264.
    /// Optionally drives a RawImage for display.
    /// </summary>
    public class RemoteCameraWindow : MonoBehaviour
    {
        [Header("Video Settings")]
        public int resolutionWidth = 2160;
        public int resolutionHeight = 1440;
        public int videoFps = 60;
        public int bitrate = 40 * 1024 * 1024;
        public int decoderPort = 19000;

        [Header("Display")]
        [Tooltip("Optional RawImage to display the decoded video")]
        public RawImage displayImage;

        private Texture2D _texture;
        private bool _decoderActive;
        private PicoTcpClient _tcp;

        public Texture2D Texture => _texture;
        public bool IsActive => _decoderActive;

        /// <summary>
        /// Call to start the camera preview pipeline.
        /// </summary>
        public void StartPreview(PicoTcpClient tcp)
        {
            if (_decoderActive) return;
            _tcp = tcp;
            StartCoroutine(StartListenCoroutine());
        }

        /// <summary>
        /// Stop preview and release decoder resources.
        /// </summary>
        public void StopPreview()
        {
            if (!_decoderActive) return;
            _decoderActive = false;
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
            Debug.Log($"[RemoteCameraWindow] Starting preview {resolutionWidth}x{resolutionHeight} @{videoFps}fps port={decoderPort}");

            _texture = new Texture2D(resolutionWidth, resolutionHeight, TextureFormat.RGB24, false, false);
            yield return null;

            MediaDecoder.Initialize((int)_texture.GetNativeTexturePtr(), resolutionWidth, resolutionHeight);
            MediaDecoder.StartServer(decoderPort, false);
            _decoderActive = true;
            yield return null;

            // Tell the PC bridge to start sending H.264 to our decoder port
            if (_tcp != null)
            {
                string localIp = NetUtils.GetLocalIPv4();
                string cameraJson = $"{{\"ip\":\"{localIp}\",\"port\":{decoderPort},"
                    + $"\"width\":{resolutionWidth},\"height\":{resolutionHeight},"
                    + $"\"fps\":{videoFps},\"bitrate\":{bitrate}}}";
                _tcp.SendFunction("StartReceivePcCamera", cameraJson);
                Debug.Log($"[RemoteCameraWindow] Sent StartReceivePcCamera to PC");
            }
        }

        private void Update()
        {
            if (!_decoderActive || _texture == null) return;

            if (Application.platform == RuntimePlatform.Android)
            {
                if (MediaDecoder.IsUpdateFrame())
                {
                    MediaDecoder.UpdateTexture();
                    GL.InvalidateState();

                    if (displayImage != null && displayImage.texture != _texture)
                        displayImage.texture = _texture;
                }
            }
        }

        private void OnDisable()
        {
            StopPreview();
        }

        private void OnDestroy()
        {
            StopPreview();
        }
    }
}
