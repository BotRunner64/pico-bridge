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
        public float connectTimeout = 5f;
        public float heartbeatInterval = 10f;

        private int _state = (int)SocketState.None;
        public SocketState State
        {
            get => (SocketState)Volatile.Read(ref _state);
            private set => Volatile.Write(ref _state, (int)value);
        }

        public string DeviceSN { get; set; } = "";
        public bool SendTrackingData { get; set; } = true;

        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string, string> OnFunctionReceived; // functionName, json

        private const int ConnectedEvent = 1;
        private const int DisconnectedEvent = 2;

        private Socket _socket;
        private readonly object _socketLock = new object();
        private Thread _receiveThread;
        private Thread _sendThread;
        private readonly ByteBuffer _recvBuffer = new ByteBuffer();
        private readonly ConcurrentQueue<NetPacket> _recvQueue = new ConcurrentQueue<NetPacket>();
        private readonly ConcurrentQueue<ConnectionEvent> _connectionEvents = new ConcurrentQueue<ConnectionEvent>();
        private readonly ConcurrentQueue<byte[]> _sendQueue = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<string> _trackingQueue = new ConcurrentQueue<string>();
        private volatile bool _running;
        private int _connectGeneration;
        private float _connectStartedAt;
        private float _lastHeartbeat;
        private float _reconnectTimer;
        private string _lastReportedConnectFailure;

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

            Socket socket = null;
            int generation = 0;
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.ReceiveTimeout = 2000;
                socket.SendTimeout = 5000;
                socket.NoDelay = true;
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                lock (_socketLock)
                {
                    if (State == SocketState.Connecting || State == SocketState.Working)
                    {
                        try { socket.Close(); } catch { }
                        return;
                    }

                    DrainConnectionQueues();
                    generation = Interlocked.Increment(ref _connectGeneration);
                    _socket = socket;
                    _running = true;
                    _connectStartedAt = Time.realtimeSinceStartup;
                    _reconnectTimer = 0;
                    State = SocketState.Connecting;
                }

                socket.BeginConnect(serverAddress, serverPort, OnConnectCallback, new ConnectAttempt(socket, generation));
            }
            catch (Exception e)
            {
                try { socket?.Close(); } catch { }
                ReportConnectFailure("Connect error", e);
                CloseSocket(notify: false);
            }
        }

        public void Disconnect()
        {
            autoReconnect = false;
            CloseSocket(notify: true);
        }

        public void EnqueueTracking(string json)
        {
            if (_trackingQueue.Count < 2)
                _trackingQueue.Enqueue(json);
        }

        public void SendFunction(string functionName, string valueJson)
        {
            if (State != SocketState.Working)
                return;

            var json = $"{{\"functionName\":\"{functionName}\",\"value\":{valueJson}}}";
            var data = Encoding.UTF8.GetBytes(json);
            _sendQueue.Enqueue(PackageHandle.Pack(NetCMD.PACKET_CCMD_TO_CONTROLLER_FUNCTION, data));
        }

        // ── lifecycle ─────────────────────────────────────

        private void Update()
        {
            while (_connectionEvents.TryDequeue(out var connectionEvent))
                DispatchConnectionEvent(connectionEvent);

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

            if (State == SocketState.Connecting && connectTimeout > 0f)
            {
                float elapsed = Time.realtimeSinceStartup - _connectStartedAt;
                if (elapsed >= connectTimeout)
                {
                    ReportConnectFailure("Connect timed out", new TimeoutException($"{connectTimeout:0.0}s elapsed"));
                    CloseSocket(notify: true);
                }
            }

            // Auto-reconnect
            if (autoReconnect && (State == SocketState.Closed || State == SocketState.Error))
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
            CloseSocket(notify: false);
        }

        // ── connection ────────────────────────────────────

        private void OnConnectCallback(IAsyncResult ar)
        {
            var attempt = (ConnectAttempt)ar.AsyncState;
            try
            {
                attempt.Socket.EndConnect(ar);
                lock (_socketLock)
                {
                    if (attempt.Generation != Volatile.Read(ref _connectGeneration) || _socket != attempt.Socket)
                    {
                        try { attempt.Socket.Close(); } catch { }
                        return;
                    }

                    State = SocketState.Working;
                    _lastHeartbeat = 0;
                    _lastReportedConnectFailure = null;
                }

                _receiveThread = new Thread(() => ReceiveLoop(attempt.Socket, attempt.Generation)) { IsBackground = true };
                _receiveThread.Start();

                _sendThread = new Thread(() => SendLoop(attempt.Socket, attempt.Generation)) { IsBackground = true };
                _sendThread.Start();

                SendConnectInit();

                _connectionEvents.Enqueue(new ConnectionEvent(ConnectedEvent, attempt.Generation));
            }
            catch (Exception e)
            {
                if (attempt.Generation != Volatile.Read(ref _connectGeneration))
                {
                    try { attempt.Socket.Close(); } catch { }
                    return;
                }

                ReportConnectFailure("Connect failed", e);
                CloseActiveSocket(attempt.Socket, attempt.Generation, notify: true);
            }
        }

        private void ReportConnectFailure(string prefix, Exception exception)
        {
            if (autoReconnect)
            {
                string failureKey = $"{serverAddress}:{serverPort}|{exception.GetType().FullName}|{exception.Message}";
                if (_lastReportedConnectFailure == failureKey)
                    return;

                _lastReportedConnectFailure = failureKey;
                Debug.LogWarning($"[PicoBridge] {prefix}: {exception.Message}; retrying automatically");
                return;
            }

            _lastReportedConnectFailure = null;
            Debug.LogError($"[PicoBridge] {prefix}: {exception.Message}");
        }

        private readonly struct ConnectAttempt
        {
            public readonly Socket Socket;
            public readonly int Generation;

            public ConnectAttempt(Socket socket, int generation)
            {
                Socket = socket;
                Generation = generation;
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

        private void ReceiveLoop(Socket socket, int generation)
        {
            var buf = new byte[65536];
            while (IsActiveSocket(socket, generation))
            {
                try
                {
                    int n = socket.Receive(buf);
                    if (n <= 0) break;

                    lock (_recvBuffer)
                    {
                        _recvBuffer.WriteBytes(buf, 0, n);
                        NetPacket pkt;
                        while ((pkt = PackageHandle.Unpack(_recvBuffer)) != null)
                            _recvQueue.Enqueue(pkt);
                    }
                }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.TimedOut)
                {
                    continue;
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

            CloseActiveSocket(socket, generation, notify: true);
        }

        private void SendLoop(Socket socket, int generation)
        {
            while (IsActiveSocket(socket, generation))
            {
                try
                {
                    // Priority: tracking data
                    if (SendTrackingData && _trackingQueue.TryDequeue(out var trackingJson))
                    {
                        var json = $"{{\"functionName\":\"Tracking\",\"value\":{trackingJson}}}";
                        var data = Encoding.UTF8.GetBytes(json);
                        var packet = PackageHandle.Pack(NetCMD.PACKET_CCMD_TO_CONTROLLER_FUNCTION, data);
                        if (!SendAll(socket, generation, packet))
                            break;
                    }

                    // Then: queued commands
                    if (_sendQueue.TryDequeue(out var raw))
                    {
                        if (!SendAll(socket, generation, raw))
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

            CloseActiveSocket(socket, generation, notify: true);
        }

        private bool SendAll(Socket socket, int generation, byte[] packet)
        {
            int sent = 0;
            while (IsActiveSocket(socket, generation) && sent < packet.Length)
            {
                int written = socket.Send(packet, sent, packet.Length - sent, SocketFlags.None);
                if (written <= 0)
                    return false;
                sent += written;
            }

            return sent == packet.Length;
        }

        // ── dispatch ──────────────────────────────────────

        private void DispatchConnectionEvent(ConnectionEvent connectionEvent)
        {
            if (connectionEvent.Type == ConnectedEvent)
            {
                if (connectionEvent.Generation != Volatile.Read(ref _connectGeneration) || State != SocketState.Working)
                    return;

                Debug.Log($"[PicoBridge] Connected to {serverAddress}:{serverPort}");
                OnConnected?.Invoke();
                return;
            }

            if (connectionEvent.Type == DisconnectedEvent)
            {
                if (State == SocketState.Working || State == SocketState.Connecting)
                    return;

                Debug.Log("[PicoBridge] Disconnected");
                OnDisconnected?.Invoke();
            }
        }

        private void DispatchPacket(NetPacket pkt)
        {
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
            CloseSocket(notify: true);
        }

        private void CloseSocket(bool notify)
        {
            Socket socketToClose = null;
            int generation;
            bool shouldNotify;

            lock (_socketLock)
            {
                var previousState = State;
                generation = Interlocked.Increment(ref _connectGeneration);
                _running = false;
                socketToClose = _socket;
                _socket = null;
                DrainConnectionQueues();
                State = SocketState.Closed;
                shouldNotify = notify && (previousState == SocketState.Connecting || previousState == SocketState.Working || previousState == SocketState.Error);
            }

            CloseSocketQuietly(socketToClose);

            if (shouldNotify)
                _connectionEvents.Enqueue(new ConnectionEvent(DisconnectedEvent, generation));
        }

        private void CloseActiveSocket(Socket socket, int generation, bool notify)
        {
            Socket socketToClose = null;
            int closedGeneration = generation;
            bool shouldNotify = false;

            lock (_socketLock)
            {
                if (generation != Volatile.Read(ref _connectGeneration) || _socket != socket)
                    return;

                var previousState = State;
                closedGeneration = Interlocked.Increment(ref _connectGeneration);
                _running = false;
                socketToClose = _socket;
                _socket = null;
                DrainConnectionQueues();
                State = SocketState.Closed;
                shouldNotify = notify && (previousState == SocketState.Connecting || previousState == SocketState.Working || previousState == SocketState.Error);
            }

            CloseSocketQuietly(socketToClose);

            if (shouldNotify)
                _connectionEvents.Enqueue(new ConnectionEvent(DisconnectedEvent, closedGeneration));
        }

        private bool IsActiveSocket(Socket socket, int generation)
        {
            return _running
                && generation == Volatile.Read(ref _connectGeneration)
                && State == SocketState.Working
                && ReferenceEquals(_socket, socket)
                && socket != null
                && socket.Connected;
        }

        private void CloseSocketQuietly(Socket socket)
        {
            try { socket?.Shutdown(SocketShutdown.Both); } catch { }
            try { socket?.Close(); } catch { }
        }

        private void DrainConnectionQueues()
        {
            while (_sendQueue.TryDequeue(out _)) { }
            while (_trackingQueue.TryDequeue(out _)) { }
            while (_recvQueue.TryDequeue(out _)) { }
        }

        private readonly struct ConnectionEvent
        {
            public readonly int Type;
            public readonly int Generation;

            public ConnectionEvent(int type, int generation)
            {
                Type = type;
                Generation = generation;
            }
        }
    }
}
