using UnityEngine;
using PicoBridge.Network;
using PicoBridge.Tracking;

namespace PicoBridge
{
    /// <summary>
    /// Main entry point. Manages TCP connection, UDP discovery, and tracking data flow.
    /// Attach to a GameObject in the scene.
    /// </summary>
    public class PicoBridgeManager : MonoBehaviour
    {
        [Header("Server")]
        public string serverAddress = "192.168.1.100";
        public int serverPort = 63901;
        public bool autoDiscovery = true;

        [Header("Tracking")]
        public bool sendHead = true;
        public bool sendControllers = true;
        public bool sendHands = true;
        public bool sendBody = false;
        public bool sendMotion = false;

        [Header("Timing")]
        [Range(30, 120)]
        public int trackingFps = 72;

        private PicoTcpClient _tcp;
        private UdpDiscovery _discovery;
        private PicoTrackingCollector _collector;
        private float _trackingInterval;
        private float _trackingTimer;
        private bool _autoConnected;

        public PicoTcpClient TcpClient => _tcp;
        public UdpDiscovery Discovery => _discovery;
        public bool IsConnected => _tcp != null && _tcp.State == SocketState.Working;

        private void Awake()
        {
            _tcp = gameObject.AddComponent<PicoTcpClient>();
            _tcp.serverAddress = serverAddress;
            _tcp.serverPort = serverPort;
            _tcp.DeviceSN = SystemInfo.deviceUniqueIdentifier;

            _tcp.OnConnected += () => Debug.Log("[PicoBridge] Connected");
            _tcp.OnDisconnected += () =>
            {
                Debug.Log("[PicoBridge] Disconnected");
                _autoConnected = false;
            };
            _tcp.OnFunctionReceived += OnFunction;

            // UDP discovery
            _discovery = gameObject.AddComponent<UdpDiscovery>();
            _discovery.OnServerFound += OnServerDiscovered;

            #if UNITY_EDITOR
            _collector = null; // Use mock in Editor
            #else
            _collector = new PicoTrackingCollector();
            #endif
            _trackingInterval = 1f / trackingFps;
        }

        private void Start()
        {
            if (autoDiscovery)
                _discovery.StartListening();

            // Don't auto-connect to hardcoded IP if discovery is on
            if (!autoDiscovery)
                _tcp.Connect();
        }

        private void Update()
        {
            // Rate-limited tracking send
            _trackingTimer += Time.deltaTime;
            if (_trackingTimer >= _trackingInterval && IsConnected)
            {
                _trackingTimer = 0;
                string json;
                #if UNITY_EDITOR
                json = MockTrackingData.GenerateJson(Time.time);
                #else
                if (_collector == null) return;
                _collector.HeadEnabled = sendHead;
                _collector.ControllerEnabled = sendControllers;
                _collector.HandTrackingEnabled = sendHands;
                _collector.BodyTrackingEnabled = sendBody;
                _collector.MotionTrackerEnabled = sendMotion;
                json = _collector.CollectJson();
                #endif
                _tcp.EnqueueTracking(json);
            }
        }

        private void OnServerDiscovered(string ip, int port)
        {
            Debug.Log($"[PicoBridge] Server discovered: {ip}:{port}");
            // Auto-connect to first discovered server if not already connected
            if (!IsConnected && !_autoConnected)
            {
                _autoConnected = true;
                SetServer(ip, port);
            }
        }

        private void OnFunction(string functionName, string json)
        {
            Debug.Log($"[PicoBridge] Function: {functionName}");
        }

        /// <summary>
        /// Change server address at runtime (e.g. from UI input or discovery).
        /// </summary>
        public void SetServer(string address, int port = NetCMD.DEFAULT_TCP_PORT)
        {
            serverAddress = address;
            serverPort = port;
            if (_tcp != null)
            {
                _tcp.Disconnect();
                _tcp.serverAddress = address;
                _tcp.serverPort = port;
                _tcp.autoReconnect = true;
                _tcp.Connect();
            }
        }
    }
}
