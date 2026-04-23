using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace PicoBridge.Network
{
    public enum SocketState
    {
        None, Connecting, Working, Closed, Error
    }

    public class PicoTcpClient : MonoBehaviour
    {
        [Header("Connection")]
        public string serverAddress = "192.168.1.100";
        public int serverPort = 63901;
        public bool autoReconnect = true;
        public float reconnectInterval = 2f;
        public float heartbeatInterval = 10f;

        public SocketState State { get; private set; } = SocketState.None;
        public string DeviceSN { get; set; } = "";
        public bool SendTrackingData { get; set; } = true;

        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string, string> OnFunctionReceived; // functionName, json

        private Socket _socket;
        private Thread _receiveThread;
        private Thread _sendThread;
        private readonly ByteBuffer _recvBuffer = new ByteBuffer();
        private readonly ConcurrentQueue<NetPacket> _recvQueue = new ConcurrentQueue<NetPacket>();
        private readonly ConcurrentQueue<byte[]> _sendQueue = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<string> _trackingQueue = new ConcurrentQueue<string>();
        private volatile bool _running;
        private float _lastHeartbeat;
        private float _reconnectTimer;

        // ── public API ────────────────────────────────────

        public void Connect(string address)
        {
            serverAddress = address;
            Connect();
        }

        public void Connect()
        {
            if (State == SocketState.Connecting || State == SocketState.Working)
                return;

            State = SocketState.Connecting;
            _running = true;

            try
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _socket.SendTimeout = 15000;
                _socket.NoDelay = true;
                _socket.BeginConnect(serverAddress, serverPort, OnConnectCallback, null);
            }
            catch (Exception e)
            {
                Debug.LogError($"[PicoBridge] Connect error: {e.Message}");
                State = SocketState.Error;
            }
        }

        public void Disconnect()
        {
            autoReconnect = false;
            CloseSocket();
        }

        public void EnqueueTracking(string json)
        {
            if (_trackingQueue.Count < 2)
                _trackingQueue.Enqueue(json);
        }

        public void SendFunction(string functionName, string valueJson)
        {
            var json = $"{{\"functionName\":\"{functionName}\",\"value\":{valueJson}}}";
            var data = Encoding.UTF8.GetBytes(json);
            _sendQueue.Enqueue(PackageHandle.Pack(NetCMD.PACKET_CCMD_TO_CONTROLLER_FUNCTION, data));
        }

        // ── lifecycle ─────────────────────────────────────

        private void Update()
        {
            // Process received packets on main thread
            while (_recvQueue.TryDequeue(out var pkt))
                DispatchPacket(pkt);

            // Heartbeat
            if (State == SocketState.Working)
            {
                _lastHeartbeat += Time.deltaTime;
                if (_lastHeartbeat >= heartbeatInterval)
                {
                    _lastHeartbeat = 0;
                    var snBytes = Encoding.UTF8.GetBytes(DeviceSN);
                    _sendQueue.Enqueue(PackageHandle.Pack(NetCMD.PACKET_CCMD_CLIENT_HEARTBEAT, snBytes));
                }
            }

            // Auto-reconnect
            if (autoReconnect && State == SocketState.Closed)
            {
                _reconnectTimer += Time.deltaTime;
                if (_reconnectTimer >= reconnectInterval)
                {
                    _reconnectTimer = 0;
                    Connect();
                }
            }
        }

        private void OnDestroy()
        {
            _running = false;
            CloseSocket();
        }

        // ── connection ────────────────────────────────────

        private void OnConnectCallback(IAsyncResult ar)
        {
            try
            {
                _socket.EndConnect(ar);
                State = SocketState.Working;
                _lastHeartbeat = 0;

                _receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
                _receiveThread.Start();

                _sendThread = new Thread(SendLoop) { IsBackground = true };
                _sendThread.Start();

                SendConnectInit();

                // Fire event on next Update
                _recvQueue.Enqueue(new NetPacket { Cmd = 0xFF }); // sentinel for connected
            }
            catch (Exception e)
            {
                Debug.LogError($"[PicoBridge] Connect failed: {e.Message}");
                State = SocketState.Error;
                CloseSocket();
            }
        }

        private void SendConnectInit()
        {
            // Send CONNECT with device SN
            var connectData = Encoding.UTF8.GetBytes($"{DeviceSN}|-1");
            _sendQueue.Enqueue(PackageHandle.Pack(NetCMD.PACKET_CCMD_CONNECT, connectData));

            // Send VERSION
            var versionData = Encoding.UTF8.GetBytes("PicoBridge|1.0.0");
            _sendQueue.Enqueue(PackageHandle.Pack(NetCMD.PACKET_CCMD_SEND_VERSION, versionData));
        }

        // ── threads ───────────────────────────────────────

        private void ReceiveLoop()
        {
            var buf = new byte[65536];
            while (_running && _socket != null && _socket.Connected)
            {
                try
                {
                    int n = _socket.Receive(buf);
                    if (n <= 0) break;

                    lock (_recvBuffer)
                    {
                        _recvBuffer.WriteBytes(buf, 0, n);
                        NetPacket pkt;
                        while ((pkt = PackageHandle.Unpack(_recvBuffer)) != null)
                            _recvQueue.Enqueue(pkt);
                    }
                }
                catch (SocketException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }

            if (_running)
            {
                State = SocketState.Closed;
            }
        }

        private void SendLoop()
        {
            while (_running && _socket != null && _socket.Connected)
            {
                try
                {
                    // Priority: tracking data
                    if (SendTrackingData && _trackingQueue.TryDequeue(out var trackingJson))
                    {
                        var json = $"{{\"functionName\":\"Tracking\",\"value\":{trackingJson}}}";
                        var data = Encoding.UTF8.GetBytes(json);
                        var packet = PackageHandle.Pack(NetCMD.PACKET_CCMD_TO_CONTROLLER_FUNCTION, data);
                        if (!SendAll(packet))
                            break;
                    }

                    // Then: queued commands
                    if (_sendQueue.TryDequeue(out var raw))
                    {
                        if (!SendAll(raw))
                            break;
                    }
                    else
                    {
                        Thread.Sleep(1);
                    }
                }
                catch (SocketException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }

        private bool SendAll(byte[] packet)
        {
            int sent = 0;
            while (_running && _socket != null && _socket.Connected && sent < packet.Length)
            {
                int written = _socket.Send(packet, sent, packet.Length - sent, SocketFlags.None);
                if (written <= 0)
                    return false;
                sent += written;
            }

            return sent == packet.Length;
        }

        // ── dispatch ──────────────────────────────────────

        private void DispatchPacket(NetPacket pkt)
        {
            if (pkt.Cmd == 0xFF) // connected sentinel
            {
                Debug.Log($"[PicoBridge] Connected to {serverAddress}:{serverPort}");
                OnConnected?.Invoke();
                return;
            }

            switch (pkt.Cmd)
            {
                case NetCMD.PACKET_CMD_FROM_CONTROLLER_COMMON_FUNCTION:
                    HandleFunction(pkt);
                    break;
                case NetCMD.PACKET_CMD_CUSTOM_TO_VR:
                    Debug.Log($"[PicoBridge] Custom data: {pkt.Data.Length} bytes");
                    break;
            }
        }

        private void HandleFunction(NetPacket pkt)
        {
            var json = Encoding.UTF8.GetString(pkt.Data);
            // Quick parse for functionName
            int fnIdx = json.IndexOf("\"functionName\"", StringComparison.Ordinal);
            if (fnIdx < 0) return;
            int colonIdx = json.IndexOf(':', fnIdx);
            int quoteStart = json.IndexOf('"', colonIdx + 1);
            int quoteEnd = json.IndexOf('"', quoteStart + 1);
            if (quoteStart < 0 || quoteEnd < 0) return;
            string fnName = json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
            OnFunctionReceived?.Invoke(fnName, json);
        }

        private void CloseSocket()
        {
            _running = false;
            try { _socket?.Shutdown(SocketShutdown.Both); } catch { }
            try { _socket?.Close(); } catch { }
            _socket = null;
            State = SocketState.Closed;
            OnDisconnected?.Invoke();
        }
    }
}
