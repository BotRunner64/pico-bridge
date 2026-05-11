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

        [Header("PC Control")]
        [SerializeField] private bool allowPcVideoPreview;
        [SerializeField] private bool autoRequestPcVideoPreview;

        [Header("Scene Visuals")]
        public bool hideTrackingVisualsWithoutSignal = true;

        private PicoTcpClient _tcp;
        private UdpDiscovery _discovery;
#if !UNITY_EDITOR
        private PicoTrackingCollector _collector;
#endif
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
        public bool AllowPcVideoPreview => allowPcVideoPreview;
        public bool AutoRequestPcVideoPreview => autoRequestPcVideoPreview;

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

#if !UNITY_EDITOR
            _collector = new PicoTrackingCollector();
#endif
            _trackingInterval = 1f / trackingFps;
        }

        private void Start()
        {
            ConfigurePassthroughRendering();
            ConfigureTrackingVisualGuards();
#if UNITY_EDITOR
            SuppressEditorOnlyControllerRenderers();
#endif
            StartVideoSeeThroughBootstrap();

            if (autoDiscovery)
                _discovery.StartListening();

            // Don't auto-connect to hardcoded IP if discovery is on
            if (!autoDiscovery)
                _tcp.Connect();
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
#if UNITY_EDITOR
            SuppressEditorOnlyControllerRenderers();
#endif

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

#if UNITY_EDITOR
        private void SuppressEditorOnlyControllerRenderers()
        {
            if (!hideTrackingVisualsWithoutSignal)
                return;

            foreach (var controller in FindObjectsOfType<ActionBasedController>(true))
            {
                if (controller == null)
                    continue;

                foreach (var rendererComponent in controller.GetComponentsInChildren<Renderer>(true))
                {
                    if (rendererComponent != null)
                        rendererComponent.enabled = false;
                }
            }
        }
#endif

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
            if (functionName == "BridgeControl")
            {
                HandleBridgeControl(json);
                return;
            }
            if (functionName == "WebRtcOffer" || functionName == "WebRtcIceCandidate")
                _webRtcCamera?.HandleFunction(functionName, json);
        }

        private void HandleBridgeControl(string json)
        {
            string channel = ExtractString(json, "channel");
            string type = ExtractString(json, "type");
            if (channel != "video" || type != "set_policy")
                return;

            bool enabled = ExtractBool(json, "enabled") ?? false;
            bool autoPreview = ExtractBool(json, "auto_preview") ?? enabled;
            ApplyVideoPolicy(enabled, autoPreview);
        }

        private void ApplyVideoPolicy(bool enabled, bool autoPreview)
        {
            allowPcVideoPreview = enabled;
            autoRequestPcVideoPreview = enabled && autoPreview;
            if (!allowPcVideoPreview || !autoRequestPcVideoPreview)
                _webRtcCamera?.StopPreview();
        }

        private static string ExtractString(string json, string key)
        {
            string needle = $"\"{key}\"";
            int keyIndex = json.IndexOf(needle, System.StringComparison.Ordinal);
            if (keyIndex < 0) return string.Empty;
            int colon = json.IndexOf(':', keyIndex + needle.Length);
            if (colon < 0) return string.Empty;
            int start = json.IndexOf('"', colon + 1);
            if (start < 0) return string.Empty;
            var result = new System.Text.StringBuilder();
            bool escape = false;
            for (int i = start + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (escape)
                {
                    switch (c)
                    {
                        case 'n': result.Append('\n'); break;
                        case 'r': result.Append('\r'); break;
                        case 't': result.Append('\t'); break;
                        case '\\': result.Append('\\'); break;
                        case '"': result.Append('"'); break;
                        default: result.Append(c); break;
                    }
                    escape = false;
                }
                else if (c == '\\')
                    escape = true;
                else if (c == '"')
                    return result.ToString();
                else
                    result.Append(c);
            }
            return string.Empty;
        }

        private static bool? ExtractBool(string json, string key)
        {
            string needle = $"\"{key}\"";
            int keyIndex = json.IndexOf(needle, System.StringComparison.Ordinal);
            if (keyIndex < 0) return null;
            int colon = json.IndexOf(':', keyIndex + needle.Length);
            if (colon < 0) return null;
            int start = colon + 1;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            if (start + 4 <= json.Length && string.Compare(json, start, "true", 0, 4, System.StringComparison.Ordinal) == 0)
                return true;
            if (start + 5 <= json.Length && string.Compare(json, start, "false", 0, 5, System.StringComparison.Ordinal) == 0)
                return false;
            return null;
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
