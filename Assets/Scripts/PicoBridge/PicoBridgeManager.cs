using UnityEngine;
using PicoBridge.Camera;
using PicoBridge.Network;
using PicoBridge.Tracking;
using Unity.XR.PXR;
using System.Collections;
using UnityEngine.XR.Interaction.Toolkit;

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

        [Header("Scene Visuals")]
        public bool hideTrackingVisualsWithoutSignal = true;

        private PicoTcpClient _tcp;
        private UdpDiscovery _discovery;
        private PicoTrackingCollector _collector;
        private WebRtcCameraReceiver _webRtcCamera;
        private float _trackingInterval;
        private float _trackingTimer;
        private bool _autoConnected;
#if UNITY_ANDROID && !UNITY_EDITOR
        private Coroutine _videoSeeThroughCoroutine;
#endif

        public PicoTcpClient TcpClient => _tcp;
        public UdpDiscovery Discovery => _discovery;
        public WebRtcCameraReceiver WebRtcCamera => _webRtcCamera;
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

            // Camera preview
            _webRtcCamera = gameObject.AddComponent<WebRtcCameraReceiver>();

            #if UNITY_EDITOR
            _collector = null; // Use mock in Editor
            #else
            _collector = new PicoTrackingCollector();
            #endif
            _trackingInterval = 1f / trackingFps;
        }

        private void Start()
        {
            ConfigurePassthroughRendering();
            ConfigureTrackingVisualGuards();
            StartVideoSeeThroughBootstrap();

#if UNITY_EDITOR
            // Keep Editor Play from auto-claiming the single PC receiver while a headset is testing.
            if (!autoDiscovery)
                _tcp.Connect();
#else
            if (autoDiscovery)
                _discovery.StartListening();

            // Don't auto-connect to hardcoded IP if discovery is on
            if (!autoDiscovery)
                _tcp.Connect();
#endif
        }

        private void OnEnable()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            PXR_Plugin.System.SessionStateChanged += OnSessionStateChanged;
#endif
        }

        private void OnDisable()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            PXR_Plugin.System.SessionStateChanged -= OnSessionStateChanged;
#endif
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (!pauseStatus)
                StartVideoSeeThroughBootstrap();
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

        private void ConfigureTrackingVisualGuards()
        {
            if (!hideTrackingVisualsWithoutSignal)
                return;

            foreach (var bodyBlock in FindObjectsOfType<global::PXR_BodyTrackingBlock>(true))
            {
                var target = bodyBlock.skeletonJoints != null ? bodyBlock.skeletonJoints.gameObject : bodyBlock.gameObject;
                AddTrackingVisualGuard(target, TrackingVisualSignalSource.Body);
            }

#if !PICO_OPENXR_SDK
            foreach (var hand in FindObjectsOfType<global::PXR_Hand>(true))
            {
                var source = hand.handType == HandType.HandLeft
                    ? TrackingVisualSignalSource.LeftHand
                    : TrackingVisualSignalSource.RightHand;
                AddTrackingVisualGuard(hand.gameObject, source);
            }
#endif

            foreach (var controller in FindObjectsOfType<ActionBasedController>(true))
            {
                var objectName = controller.gameObject.name.ToLowerInvariant();
                if (objectName.Contains("left"))
                    AddTrackingVisualGuard(controller.gameObject, TrackingVisualSignalSource.LeftController);
                else if (objectName.Contains("right"))
                    AddTrackingVisualGuard(controller.gameObject, TrackingVisualSignalSource.RightController);
            }
        }

        private static void AddTrackingVisualGuard(GameObject target, TrackingVisualSignalSource source)
        {
            if (target == null)
                return;

            var guard = target.GetComponent<TrackingVisualSignalGate>();
            if (guard == null)
                guard = target.AddComponent<TrackingVisualSignalGate>();

            guard.Configure(source);
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
            if (functionName == "WebRtcOffer" || functionName == "WebRtcIceCandidate")
                _webRtcCamera?.HandleFunction(functionName, json);
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

        private static void EnableVideoSeeThrough()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            PXR_Manager.EnableVideoSeeThrough = true;
#endif
        }

        private static void ConfigurePassthroughRendering()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            PXR_Plugin.Render.UPxr_EnablePremultipliedAlpha(true);
#endif
        }

        private void StartVideoSeeThroughBootstrap()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_videoSeeThroughCoroutine != null)
                StopCoroutine(_videoSeeThroughCoroutine);

            _videoSeeThroughCoroutine = StartCoroutine(EnableVideoSeeThroughWithRetry());
#endif
        }

        private IEnumerator EnableVideoSeeThroughWithRetry()
        {
            const int maxAttempts = 12;
            const float retryDelay = 0.5f;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                ConfigurePassthroughRendering();
                EnableVideoSeeThrough();
                yield return new WaitForSeconds(retryDelay);
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            _videoSeeThroughCoroutine = null;
#endif
        }

        private void OnSessionStateChanged(XrSessionState state)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (state == XrSessionState.Ready ||
                state == XrSessionState.Synchronized ||
                state == XrSessionState.Visible ||
                state == XrSessionState.Focused)
            {
                StartVideoSeeThroughBootstrap();
            }
#endif
        }
    }
}
