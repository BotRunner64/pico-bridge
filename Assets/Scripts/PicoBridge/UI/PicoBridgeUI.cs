using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using PicoBridge.Camera;
using PicoBridge.Network;

namespace PicoBridge.UI
{
    /// <summary>
    /// Device builds use a world-space XR Canvas per XRI/PICO guidance.
    /// </summary>
    public class PicoBridgeUI : MonoBehaviour
    {
        [Header("Generated UI")]
        [SerializeField] private bool createGeneratedWorldSpaceUi = true;
        [SerializeField] private bool showGeneratedWorldSpaceUiInEditor;

        [Header("Scene UI Input")]
        [SerializeField] private bool configureWorldSpaceCanvasInput = true;
        [SerializeField] private bool enableMouseInputInEditor = true;
        [SerializeField] private bool enableTouchInputInEditor;

        private struct DiscoveredServer
        {
            public string Ip;
            public int Port;
            public float LastSeen;
        }

        private static readonly Color PanelColor = new Color(0.035f, 0.041f, 0.052f, 0.92f);
        private static readonly Color CardColor = new Color(0.078f, 0.089f, 0.11f, 0.94f);
        private static readonly Color CardAltColor = new Color(0.10f, 0.115f, 0.14f, 0.94f);
        private static readonly Color PrimaryColor = new Color(0.11f, 0.46f, 0.93f, 1f);
        private static readonly Color SuccessColor = new Color(0.12f, 0.78f, 0.38f, 1f);
        private static readonly Color WarningColor = new Color(1f, 0.69f, 0.18f, 1f);
        private static readonly Color ErrorColor = new Color(0.95f, 0.22f, 0.22f, 1f);
        private static readonly Color MutedTextColor = new Color(0.72f, 0.78f, 0.86f, 1f);

        private PicoBridgeManager _manager;
        private string _ipInput = "192.168.1.100";
        private string _portInput = "63901";
        private string _statusText = "Disconnected";
        private bool _cameraPreview;
        private bool _collapsed;
        private bool _discoveredServersDirty;
        private readonly List<DiscoveredServer> _discoveredServers = new List<DiscoveredServer>();
        private readonly StringBuilder _diagnosticsBuilder = new StringBuilder(256);

        private Canvas _worldCanvas;
        private RectTransform _canvasRect;
        private GameObject _expandedRoot;
        private Font _font;
        private Image _statusPillImage;
        private Text _statusPillText;
        private Text _statusLabel;
        private Text _subtitleLabel;
        private Text _serverSummaryLabel;
        private Text _emptyServerLabel;
        private InputField _ipField;
        private InputField _portField;
        private Text _connectButtonLabel;
        private Toggle _headToggle;
        private Toggle _controllersToggle;
        private Toggle _handsToggle;
        private Toggle _bodyToggle;
        private Toggle _motionToggle;
        private Button _cameraButton;
        private Text _cameraButtonLabel;
        private RawImage _previewImage;
        private Text _cameraStatusLabel;
        private LayoutElement _cameraCardElement;
        private RectTransform _serverButtonContainer;
        private Text _diagnosticsLabel;
        private Text _collapseButtonLabel;

        private bool ShouldShowGeneratedWorldSpaceUi => createGeneratedWorldSpaceUi && (!Application.isEditor || showGeneratedWorldSpaceUiInEditor);

        private void Start()
        {
            _manager = GetComponent<PicoBridgeManager>();
            if (_manager == null)
                _manager = FindObjectOfType<PicoBridgeManager>();

            if (_manager != null)
            {
                _ipInput = _manager.serverAddress;
                _portInput = _manager.serverPort.ToString();

                if (_manager.Discovery != null)
                    _manager.Discovery.OnServerFound += OnServerFound;
            }

            if (configureWorldSpaceCanvasInput)
                ConfigureWorldSpaceCanvasInput();

            if (ShouldShowGeneratedWorldSpaceUi)
                EnsureWorldSpaceUi();
        }

        private void OnDestroy()
        {
            if (_manager != null && _manager.Discovery != null)
                _manager.Discovery.OnServerFound -= OnServerFound;
        }

        private void Update()
        {
            if (_manager == null) return;

            if (ShouldShowGeneratedWorldSpaceUi && _worldCanvas == null)
                EnsureWorldSpaceUi();

            if (_manager.IsConnected)
                _statusText = $"Connected to {_manager.serverAddress}:{_manager.serverPort}";
            else if (_manager.TcpClient != null)
            {
                _statusText = _manager.TcpClient.State.ToString();
                if (_cameraPreview)
                {
                    _cameraPreview = false;
                    if (_manager.WebRtcCamera != null)
                        _manager.WebRtcCamera.StopPreview();
                }
            }
            else
                _statusText = "Disconnected";

            if (ShouldShowGeneratedWorldSpaceUi)
                SyncWorldSpaceUi();
        }

        private void OnServerFound(string ip, int port)
        {
            for (int i = 0; i < _discoveredServers.Count; i++)
            {
                if (_discoveredServers[i].Ip == ip && _discoveredServers[i].Port == port)
                {
                    _discoveredServers[i] = new DiscoveredServer { Ip = ip, Port = port, LastSeen = Time.time };
                    _discoveredServersDirty = true;
                    return;
                }
            }

            _discoveredServers.Add(new DiscoveredServer { Ip = ip, Port = port, LastSeen = Time.time });
            _discoveredServersDirty = true;
        }

        private void ConfigureWorldSpaceCanvasInput()
        {
            EnsureXrEventSystem();

            var canvases = FindObjectsOfType<Canvas>(true);
            foreach (var canvas in canvases)
            {
                if (canvas.renderMode != RenderMode.WorldSpace)
                    continue;

                var trackedRaycaster = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
                if (trackedRaycaster == null)
                    trackedRaycaster = canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
                trackedRaycaster.enabled = true;

                var graphicRaycaster = canvas.GetComponent<GraphicRaycaster>();
                if (Application.isEditor && enableMouseInputInEditor)
                {
                    if (graphicRaycaster == null)
                        graphicRaycaster = canvas.gameObject.AddComponent<GraphicRaycaster>();
                    graphicRaycaster.enabled = true;
                }
                else if (graphicRaycaster != null)
                    graphicRaycaster.enabled = false;
            }
        }

        private void EnsureWorldSpaceUi()
        {
            if (_manager == null || _worldCanvas != null)
                return;

            var mainCamera = UnityEngine.Camera.main;
            if (mainCamera == null)
                return;

            EnsureXrEventSystem();
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasObject = new GameObject("PicoBridge XR UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster));
            canvasObject.transform.SetParent(mainCamera.transform, false);
            canvasObject.transform.localPosition = new Vector3(0f, -0.08f, 1.12f);
            canvasObject.transform.localRotation = Quaternion.identity;
            canvasObject.transform.localScale = Vector3.one * 0.001f;

            _worldCanvas = canvasObject.GetComponent<Canvas>();
            _worldCanvas.renderMode = RenderMode.WorldSpace;
            _worldCanvas.worldCamera = mainCamera;

            _canvasRect = canvasObject.GetComponent<RectTransform>();
            _canvasRect.sizeDelta = new Vector2(860f, 1030f);

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 12f;

            var panel = CreateUIObject("Panel", canvasObject.transform);
            StretchRect(panel.GetComponent<RectTransform>());
            AddImage(panel, PanelColor);
            var panelLayout = AddVerticalLayout(panel, 22, 22, 22, 22, 14f);
            panelLayout.childControlHeight = true;
            panelLayout.childControlWidth = true;
            panelLayout.childForceExpandHeight = false;
            panelLayout.childForceExpandWidth = true;

            BuildHeader(panel.transform);

            _expandedRoot = CreateUIObject("Expanded", panel.transform);
            var expandedLayout = AddVerticalLayout(_expandedRoot, 0, 0, 0, 0, 14f);
            expandedLayout.childControlHeight = true;
            expandedLayout.childControlWidth = true;
            expandedLayout.childForceExpandHeight = false;
            expandedLayout.childForceExpandWidth = true;
            var expandedElement = _expandedRoot.AddComponent<LayoutElement>();
            expandedElement.preferredHeight = 890f;
            expandedElement.minHeight = 890f;

            BuildStatusCard(_expandedRoot.transform);
            BuildServerCard(_expandedRoot.transform);
            BuildManualConnectCard(_expandedRoot.transform);
            BuildTrackingCard(_expandedRoot.transform);
            BuildCameraCard(_expandedRoot.transform);
            BuildDiagnosticsCard(_expandedRoot.transform);

            _discoveredServersDirty = true;
            SyncWorldSpaceUi();
        }

        private void BuildHeader(Transform parent)
        {
            var header = CreateHorizontalRow(parent, 76f, 16f);

            var titleGroup = CreateUIObject("TitleGroup", header.transform);
            var titleLayout = AddVerticalLayout(titleGroup, 0, 0, 0, 0, 0f);
            titleLayout.childControlHeight = false;
            titleLayout.childControlWidth = true;
            titleLayout.childForceExpandHeight = false;
            titleGroup.AddComponent<LayoutElement>().flexibleWidth = 1f;
            CreateText(titleGroup.transform, "PICO Bridge", 40, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white, 42f);
            _subtitleLabel = CreateText(titleGroup.transform, "Searching for your PC receiver", 20, FontStyle.Normal, TextAnchor.MiddleLeft, MutedTextColor, 28f);

            var pill = CreateUIObject("StatusPill", header.transform);
            _statusPillImage = AddImage(pill, WarningColor);
            var pillElement = pill.AddComponent<LayoutElement>();
            pillElement.preferredWidth = 190f;
            pillElement.preferredHeight = 46f;
            pillElement.minHeight = 46f;
            _statusPillText = CreateText(pill.transform, "Searching", 22, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white, 46f);
            StretchRect(_statusPillText.GetComponent<RectTransform>());

            CreateButton(header.transform, "Hide", 20, 46f, ToggleCollapsed, out _collapseButtonLabel, 98f, new Color(1f, 1f, 1f, 0.13f));
        }

        private void BuildStatusCard(Transform parent)
        {
            var card = CreateCard(parent, "StatusCard", 122f);
            _statusLabel = CreateText(card.transform, _statusText, 28, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white, 40f);
            _serverSummaryLabel = CreateText(card.transform, "No PC connected yet", 22, FontStyle.Normal, TextAnchor.MiddleLeft, MutedTextColor, 34f);
            CreateText(card.transform, "Tip: start pc_receiver first, then keep this headset on the same Wi-Fi.", 18, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.62f, 0.69f, 0.78f, 1f), 28f);
        }

        private void BuildServerCard(Transform parent)
        {
            var card = CreateCard(parent, "ServersCard", 210f);
            var titleRow = CreateHorizontalRow(card.transform, 38f, 10f);
            CreateText(titleRow.transform, "Discovered PCs", 26, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white, 38f).GetComponent<LayoutElement>().flexibleWidth = 1f;
            CreateButton(titleRow.transform, "Refresh", 18, 36f, () => _discoveredServersDirty = true, out _, 116f, new Color(1f, 1f, 1f, 0.10f));

            _emptyServerLabel = CreateText(card.transform, "Listening for UDP discovery...", 22, FontStyle.Normal, TextAnchor.MiddleCenter, MutedTextColor, 48f);
            var serverContainer = CreateUIObject("ServerButtons", card.transform);
            _serverButtonContainer = serverContainer.GetComponent<RectTransform>();
            var serverLayout = AddVerticalLayout(serverContainer, 0, 0, 0, 0, 8f);
            serverLayout.childControlHeight = false;
            serverLayout.childControlWidth = true;
            serverLayout.childForceExpandHeight = false;
            serverLayout.childForceExpandWidth = true;
            serverContainer.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void BuildManualConnectCard(Transform parent)
        {
            var card = CreateCard(parent, "ManualConnectCard", 150f);
            CreateText(card.transform, "Manual Connect", 24, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white, 34f);
            var addressRow = CreateHorizontalRow(card.transform, 48f, 10f);
            CreateText(addressRow.transform, "IP", 22, FontStyle.Bold, TextAnchor.MiddleLeft, MutedTextColor, 46f, 54f);
            _ipField = CreateInputField(addressRow.transform, _ipInput, 22, 1f);
            CreateText(addressRow.transform, "Port", 22, FontStyle.Bold, TextAnchor.MiddleLeft, MutedTextColor, 46f, 74f);
            _portField = CreateInputField(addressRow.transform, _portInput, 22, 0f, 142f);
            CreateButton(card.transform, _manager.IsConnected ? "Disconnect" : "Connect", 24, 48f, OnConnectButtonPressed, out _connectButtonLabel, -1f, PrimaryColor);
        }

        private void BuildTrackingCard(Transform parent)
        {
            var card = CreateCard(parent, "TrackingCard", 166f);
            CreateText(card.transform, "Tracking Streams", 24, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white, 34f);
            var rowA = CreateHorizontalRow(card.transform, 46f, 12f);
            _headToggle = CreateTogglePill(rowA.transform, "Head", _manager.sendHead, value => _manager.sendHead = value);
            _controllersToggle = CreateTogglePill(rowA.transform, "Controllers", _manager.sendControllers, value => _manager.sendControllers = value);
            _handsToggle = CreateTogglePill(rowA.transform, "Hands", _manager.sendHands, value => _manager.sendHands = value);
            var rowB = CreateHorizontalRow(card.transform, 46f, 12f);
            _bodyToggle = CreateTogglePill(rowB.transform, "Body", _manager.sendBody, value => _manager.sendBody = value);
            _motionToggle = CreateTogglePill(rowB.transform, "Motion", _manager.sendMotion, value => _manager.sendMotion = value);
        }

        private void BuildCameraCard(Transform parent)
        {
            var card = CreateCard(parent, "CameraCard", 412f);
            _cameraCardElement = card.GetComponent<LayoutElement>();
            var row = CreateHorizontalRow(card.transform, 48f, 12f);
            CreateText(row.transform, "Camera Preview", 24, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white, 46f).GetComponent<LayoutElement>().flexibleWidth = 1f;
            _cameraButton = CreateButton(row.transform, "Preview", 20, 46f, ToggleCameraPreview, out _cameraButtonLabel, 150f, PrimaryColor);
            _previewImage = CreateRawImage(card.transform, 760f, 292.5f);
            _previewImage.gameObject.SetActive(true);
            _cameraStatusLabel = CreateText(card.transform, "Preview idle", 18, FontStyle.Normal, TextAnchor.MiddleLeft, MutedTextColor, 26f);
        }

        private void BuildDiagnosticsCard(Transform parent)
        {
            var card = CreateCard(parent, "DiagnosticsCard", 156f, CardAltColor);
            CreateText(card.transform, "Diagnostics", 24, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white, 34f);
            _diagnosticsLabel = CreateText(card.transform, "Waiting for runtime state...", 18, FontStyle.Normal, TextAnchor.UpperLeft, MutedTextColor, 88f);
        }

        private void EnsureXrEventSystem()
        {
            var eventSystem = FindObjectOfType<EventSystem>();
            if (eventSystem == null)
            {
                var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
                eventSystem = eventSystemObject.GetComponent<EventSystem>();
            }

            foreach (var module in eventSystem.GetComponents<BaseInputModule>())
            {
                if (module is XRUIInputModule)
                    continue;

                Destroy(module);
            }

            var xrUiInputModule = eventSystem.GetComponent<XRUIInputModule>();
            if (xrUiInputModule == null)
                xrUiInputModule = eventSystem.gameObject.AddComponent<XRUIInputModule>();

            xrUiInputModule.activeInputMode = XRUIInputModule.ActiveInputMode.InputSystemActions;
            xrUiInputModule.enableXRInput = true;
            xrUiInputModule.enableMouseInput = Application.isEditor && enableMouseInputInEditor;
            xrUiInputModule.enableTouchInput = Application.isEditor && enableTouchInputInEditor;
            xrUiInputModule.enableBuiltinActionsAsFallback = true;
        }

        private void SyncWorldSpaceUi()
        {
            if (_worldCanvas == null)
                return;

            var state = _manager.TcpClient != null ? _manager.TcpClient.State : SocketState.None;
            bool connected = _manager.IsConnected;
            bool connecting = state == SocketState.Connecting;
            Color statusColor = connected ? SuccessColor : connecting ? WarningColor : state == SocketState.Error ? ErrorColor : WarningColor;
            string pillText = connected ? "Connected" : connecting ? "Connecting" : state == SocketState.Error ? "Error" : "Searching";

            if (_statusPillImage != null)
                _statusPillImage.color = statusColor;
            if (_statusPillText != null)
                _statusPillText.text = pillText;
            if (_statusLabel != null)
                _statusLabel.text = _statusText;
            if (_subtitleLabel != null)
                _subtitleLabel.text = connected ? "Streaming tracking data to your receiver" : "Searching for your PC receiver";
            if (_serverSummaryLabel != null)
                _serverSummaryLabel.text = connected
                    ? $"Active PC: {_manager.serverAddress}:{_manager.serverPort}"
                    : _discoveredServers.Count > 0
                        ? $"Found {_discoveredServers.Count} receiver{(_discoveredServers.Count == 1 ? "" : "s")}. Tap one to reconnect."
                        : "No PC found yet. Check Wi-Fi and pc_receiver logs.";

            if (_ipField != null && !_ipField.isFocused && _ipField.text != _ipInput)
                _ipField.text = _ipInput;
            if (_portField != null && !_portField.isFocused && _portField.text != _portInput)
                _portField.text = _portInput;
            if (_connectButtonLabel != null)
                _connectButtonLabel.text = connected ? "Disconnect" : "Connect";

            SyncToggle(_headToggle, _manager.sendHead);
            SyncToggle(_controllersToggle, _manager.sendControllers);
            SyncToggle(_handsToggle, _manager.sendHands);
            SyncToggle(_bodyToggle, _manager.sendBody);
            SyncToggle(_motionToggle, _manager.sendMotion);
            SyncCameraButton();

            SyncCameraPreview();
            SyncDiagnostics();
            ApplyCollapsedState();

            if (_discoveredServersDirty)
                RebuildDiscoveredServerButtons();
        }

        private void SyncCameraButton()
        {
            if (_cameraButtonLabel != null)
                _cameraButtonLabel.text = _cameraPreview ? "Stop" : "Preview";
            if (_cameraButton != null)
                _cameraButton.interactable = _manager != null && _manager.IsConnected && _manager.WebRtcCamera != null;
        }

        private void SyncCameraPreview()
        {
            if (_previewImage == null)
                return;

            bool active = _manager.WebRtcCamera != null && _manager.WebRtcCamera.IsActive;
            var texture = active ? _manager.WebRtcCamera.Texture : null;
            bool showPreview = !_collapsed;
            _previewImage.texture = texture;
            _previewImage.color = texture != null ? Color.white : new Color(0f, 0f, 0f, 0.72f);
            _previewImage.gameObject.SetActive(showPreview);
            if (_cameraStatusLabel != null)
                _cameraStatusLabel.text = active
                    ? texture != null ? $"Preview active: {texture.width}x{texture.height}, WebRTC frames {_manager.WebRtcCamera.FrameCount}" : $"WebRTC: {_manager.WebRtcCamera.Status}"
                    : _manager.IsConnected ? "Preview idle" : "Connect to PC before preview";
            SetPreferredHeight(_cameraCardElement, 412f);
        }

        private void SyncDiagnostics()
        {
            if (_diagnosticsLabel == null)
                return;

            _diagnosticsBuilder.Clear();
            _diagnosticsBuilder.Append("UDP: ").Append(_manager.Discovery != null ? "listening on 29888" : "not available");
            _diagnosticsBuilder.Append('\n').Append("TCP: ").Append(_manager.TcpClient != null ? _manager.TcpClient.State.ToString() : "None");
            _diagnosticsBuilder.Append('\n').Append("Server: ").Append(_manager.serverAddress).Append(':').Append(_manager.serverPort);
            _diagnosticsBuilder.Append('\n').Append("Discovered: ").Append(_discoveredServers.Count);
            _diagnosticsBuilder.Append('\n').Append("Camera: ").Append(_manager.WebRtcCamera != null && _manager.WebRtcCamera.IsActive ? _manager.WebRtcCamera.Status : "idle");
            _diagnosticsLabel.text = _diagnosticsBuilder.ToString();
        }

        private void ApplyCollapsedState()
        {
            if (_expandedRoot != null)
                _expandedRoot.SetActive(!_collapsed);
            if (_canvasRect != null)
            {
                bool previewVisible = _previewImage != null && _previewImage.gameObject.activeSelf;
                _canvasRect.sizeDelta = _collapsed
                    ? new Vector2(760f, 118f)
                    : new Vector2(860f, 1360f);
            }
            if (_collapseButtonLabel != null)
                _collapseButtonLabel.text = _collapsed ? "Show" : "Hide";
        }

        private static void SyncToggle(Toggle toggle, bool value)
        {
            if (toggle != null)
                toggle.SetIsOnWithoutNotify(value);
        }

        private void RebuildDiscoveredServerButtons()
        {
            if (_serverButtonContainer == null)
                return;

            for (int i = _serverButtonContainer.childCount - 1; i >= 0; i--)
                Destroy(_serverButtonContainer.GetChild(i).gameObject);

            _emptyServerLabel.gameObject.SetActive(_discoveredServers.Count == 0);
            _discoveredServers.Sort((left, right) => right.LastSeen.CompareTo(left.LastSeen));

            for (int i = 0; i < _discoveredServers.Count; i++)
            {
                var server = _discoveredServers[i];
                string label = $"{server.Ip}:{server.Port}";
                CreateServerButton(_serverButtonContainer, label, Time.time - server.LastSeen, () =>
                {
                    _ipInput = server.Ip;
                    _portInput = server.Port.ToString();
                    if (_ipField != null) _ipField.text = _ipInput;
                    if (_portField != null) _portField.text = _portInput;
                    _manager.SetServer(server.Ip, server.Port);
                });
            }

            _discoveredServersDirty = false;
        }

        private void OnConnectButtonPressed()
        {
            if (_manager.IsConnected)
            {
                _manager.TcpClient.Disconnect();
                return;
            }

            _ipInput = _ipField != null ? _ipField.text : _ipInput;
            _portInput = _portField != null ? _portField.text : _portInput;

            if (int.TryParse(_portInput, out int port))
                _manager.SetServer(_ipInput, port);
        }

        private void ToggleCameraPreview()
        {
            if (_manager == null || !_manager.IsConnected || _manager.WebRtcCamera == null)
            {
                _cameraPreview = false;
                SyncCameraButton();
                return;
            }

            _cameraPreview = !_cameraPreview;
            Debug.Log($"[PicoBridgeUI] Camera preview toggled: {_cameraPreview}");
            if (_cameraPreview)
                _manager.WebRtcCamera.StartPreview(_manager.TcpClient, 1280, 720, 30, 8 * 1024 * 1024);
            else
                _manager.WebRtcCamera.StopPreview();
            SyncCameraButton();
        }

        private void ToggleCollapsed()
        {
            _collapsed = !_collapsed;
            ApplyCollapsedState();
        }

        private GameObject CreateCard(Transform parent, string name, float preferredHeight, Color? color = null)
        {
            var card = CreateUIObject(name, parent);
            AddImage(card, color ?? CardColor);
            var layout = AddVerticalLayout(card, 18, 18, 16, 16, 8f);
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            var element = card.AddComponent<LayoutElement>();
            element.preferredHeight = preferredHeight;
            element.minHeight = preferredHeight;
            element.flexibleHeight = 0f;
            return card;
        }

        private static void SetPreferredHeight(LayoutElement element, float height)
        {
            if (element == null)
                return;
            element.preferredHeight = height;
            element.minHeight = height;
        }

        private GameObject CreateHorizontalRow(Transform parent, float minHeight, float spacing)
        {
            var row = CreateUIObject("Row", parent);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var layoutElement = row.AddComponent<LayoutElement>();
            layoutElement.minHeight = minHeight;
            layoutElement.preferredHeight = minHeight;
            layoutElement.flexibleHeight = 0f;
            return row;
        }

        private Text CreateText(Transform parent, string text, int fontSize, FontStyle fontStyle, TextAnchor alignment, Color color, float preferredHeight, float preferredWidth = -1f)
        {
            var textObject = CreateUIObject("Text", parent);
            var textComponent = textObject.AddComponent<Text>();
            textComponent.font = _font;
            textComponent.fontSize = fontSize;
            textComponent.fontStyle = fontStyle;
            textComponent.alignment = alignment;
            textComponent.color = color;
            textComponent.text = text;
            textComponent.raycastTarget = false;

            var layoutElement = textObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = preferredHeight;
            layoutElement.minHeight = preferredHeight;
            layoutElement.flexibleHeight = 0f;
            if (preferredWidth > 0f)
                layoutElement.preferredWidth = preferredWidth;

            return textComponent;
        }

        private InputField CreateInputField(Transform parent, string value, int fontSize, float flexibleWidth, float preferredWidth = -1f)
        {
            var fieldObject = CreateUIObject("InputField", parent);
            var image = AddImage(fieldObject, new Color(1f, 1f, 1f, 0.11f));

            var layoutElement = fieldObject.AddComponent<LayoutElement>();
            layoutElement.minHeight = 46f;
            layoutElement.preferredHeight = 46f;
            layoutElement.flexibleWidth = flexibleWidth;
            if (preferredWidth > 0f)
                layoutElement.preferredWidth = preferredWidth;

            var inputField = fieldObject.AddComponent<InputField>();
            inputField.targetGraphic = image;

            var textViewport = CreateUIObject("Text", fieldObject.transform);
            var textRect = textViewport.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(14f, 6f);
            textRect.offsetMax = new Vector2(-14f, -6f);

            var textComponent = textViewport.AddComponent<Text>();
            textComponent.font = _font;
            textComponent.fontSize = fontSize;
            textComponent.alignment = TextAnchor.MiddleLeft;
            textComponent.color = Color.white;
            textComponent.supportRichText = false;

            var placeholderObject = CreateUIObject("Placeholder", fieldObject.transform);
            var placeholderRect = placeholderObject.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(14f, 6f);
            placeholderRect.offsetMax = new Vector2(-14f, -6f);

            var placeholderText = placeholderObject.AddComponent<Text>();
            placeholderText.font = _font;
            placeholderText.fontSize = fontSize;
            placeholderText.alignment = TextAnchor.MiddleLeft;
            placeholderText.color = new Color(1f, 1f, 1f, 0.38f);
            placeholderText.text = value;
            placeholderText.supportRichText = false;

            inputField.textComponent = textComponent;
            inputField.placeholder = placeholderText;
            inputField.text = value;

            return inputField;
        }

        private Button CreateButton(Transform parent, string label, int fontSize, float preferredHeight, UnityEngine.Events.UnityAction onClick, out Text labelText, float preferredWidth = -1f, Color? color = null)
        {
            var buttonObject = CreateUIObject("Button", parent);
            var image = AddImage(buttonObject, color ?? PrimaryColor);

            var layoutElement = buttonObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = preferredHeight;
            layoutElement.minHeight = preferredHeight;
            layoutElement.flexibleHeight = 0f;
            if (preferredWidth > 0f)
                layoutElement.preferredWidth = preferredWidth;
            else
                layoutElement.flexibleWidth = 1f;

            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            labelText = CreateText(buttonObject.transform, label, fontSize, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white, preferredHeight);
            StretchRect(labelText.GetComponent<RectTransform>());
            return button;
        }

        private void CreateServerButton(Transform parent, string label, float ageSeconds, UnityEngine.Events.UnityAction onClick)
        {
            var buttonObject = CreateUIObject("ServerButton", parent);
            AddImage(buttonObject, new Color(0.12f, 0.17f, 0.22f, 0.96f));
            var element = buttonObject.AddComponent<LayoutElement>();
            element.preferredHeight = 54f;
            element.minHeight = 54f;
            element.flexibleHeight = 0f;
            var button = buttonObject.AddComponent<Button>();
            button.onClick.AddListener(onClick);

            var row = CreateHorizontalRow(buttonObject.transform, 54f, 8f);
            StretchRect(row.GetComponent<RectTransform>(), 8f);
            CreateText(row.transform, label, 22, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white, 52f).GetComponent<LayoutElement>().flexibleWidth = 1f;
            CreateText(row.transform, ageSeconds < 1f ? "now" : $"{Mathf.FloorToInt(ageSeconds)}s ago", 18, FontStyle.Normal, TextAnchor.MiddleRight, MutedTextColor, 52f, 100f);
        }

        private Toggle CreateTogglePill(Transform parent, string label, bool value, UnityEngine.Events.UnityAction<bool> onChanged, float preferredWidth = -1f)
        {
            var toggleObject = CreateUIObject("TogglePill", parent);
            var image = AddImage(toggleObject, value ? SuccessColor : new Color(1f, 1f, 1f, 0.13f));
            var element = toggleObject.AddComponent<LayoutElement>();
            element.preferredHeight = 46f;
            element.minHeight = 46f;
            element.flexibleHeight = 0f;
            if (preferredWidth > 0f)
                element.preferredWidth = preferredWidth;
            else
                element.flexibleWidth = 1f;

            var labelText = CreateText(toggleObject.transform, label, 20, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white, 46f);
            StretchRect(labelText.GetComponent<RectTransform>());

            var toggle = toggleObject.AddComponent<Toggle>();
            toggle.targetGraphic = image;
            toggle.isOn = value;
            toggle.onValueChanged.AddListener(isOn =>
            {
                image.color = isOn ? SuccessColor : new Color(1f, 1f, 1f, 0.13f);
                onChanged.Invoke(isOn);
            });

            return toggle;
        }

        private RawImage CreateRawImage(Transform parent, float preferredWidth, float preferredHeight)
        {
            var rawImageObject = CreateUIObject("Preview", parent);
            var image = rawImageObject.AddComponent<RawImage>();
            image.color = Color.white;

            var layoutElement = rawImageObject.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = preferredWidth;
            layoutElement.preferredHeight = preferredHeight;
            layoutElement.minHeight = preferredHeight;
            layoutElement.flexibleHeight = 0f;

            return image;
        }

        private static Image AddImage(GameObject gameObject, Color color)
        {
            var image = gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static VerticalLayoutGroup AddVerticalLayout(GameObject gameObject, int left, int right, int top, int bottom, float spacing)
        {
            var layout = gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(left, right, top, bottom);
            layout.spacing = spacing;
            return layout;
        }

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private static void StretchRect(RectTransform rectTransform, float inset = 0f)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = new Vector2(inset, inset);
            rectTransform.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
