using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using PicoBridge.Camera;

namespace PicoBridge.UI
{
    /// <summary>
    /// Editor uses IMGUI for convenience.
    /// Device builds use a world-space XR Canvas per XRI/PICO guidance.
    /// </summary>
    public class PicoBridgeUI : MonoBehaviour
    {
        private struct DiscoveredServer
        {
            public string Ip;
            public int Port;
        }

        private PicoBridgeManager _manager;
        private string _ipInput = "192.168.1.100";
        private string _portInput = "63901";
        private string _statusText = "Disconnected";
        private bool _showUI = true;
        private bool _cameraPreview;
        private readonly List<DiscoveredServer> _discoveredServers = new List<DiscoveredServer>();
        private bool _discoveredServersDirty;

        private Canvas _worldCanvas;
        private Font _font;
        private Text _statusLabel;
        private InputField _ipField;
        private InputField _portField;
        private Button _connectButton;
        private Text _connectButtonLabel;
        private Toggle _headToggle;
        private Toggle _controllersToggle;
        private Toggle _handsToggle;
        private Toggle _bodyToggle;
        private Toggle _motionToggle;
        private Toggle _cameraToggle;
        private RawImage _previewImage;
        private RectTransform _serverButtonContainer;

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

            if (!Application.isEditor)
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

            if (!Application.isEditor && _worldCanvas == null)
                EnsureWorldSpaceUi();

            if (_manager.IsConnected)
                _statusText = $"Connected to {_manager.serverAddress}:{_manager.serverPort}";
            else if (_manager.TcpClient != null)
            {
                _statusText = _manager.TcpClient.State.ToString();
                if (_cameraPreview)
                {
                    _cameraPreview = false;
                    if (_manager.Camera != null)
                        _manager.Camera.StopPreview();
                }
            }
            else
                _statusText = "Disconnected";

            if (!Application.isEditor)
                SyncWorldSpaceUi();
        }

        private void OnGUI()
        {
            if (!Application.isEditor) return;
            if (!_showUI || _manager == null) return;

            float scale = Screen.dpi > 0 ? Screen.dpi / 160f : 1f;
            int w = (int)(400 * scale);
            int h = (int)(800 * scale);
            int x = (Screen.width - w) / 2;
            int y = (int)(20 * scale);

            var style = new GUIStyle(GUI.skin.label) { fontSize = (int)(16 * scale) };
            var fieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = (int)(16 * scale) };
            var btnStyle = new GUIStyle(GUI.skin.button) { fontSize = (int)(16 * scale) };

            GUILayout.BeginArea(new Rect(x, y, w, h), GUI.skin.box);

            GUILayout.Label("PICO Bridge", new GUIStyle(style) { fontStyle = FontStyle.Bold, fontSize = (int)(20 * scale) });
            GUILayout.Space(4);
            GUILayout.Label($"Status: {_statusText}", style);
            GUILayout.Space(8);

            // Discovered servers
            if (_discoveredServers.Count > 0 && !_manager.IsConnected)
            {
                GUILayout.Label("Discovered servers:", style);
                foreach (var server in _discoveredServers)
                {
                    string label = $"{server.Ip}:{server.Port}";
                    if (GUILayout.Button(label, btnStyle))
                    {
                        _ipInput = server.Ip;
                        _portInput = server.Port.ToString();
                        _manager.SetServer(server.Ip, server.Port);
                    }
                }
                GUILayout.Space(4);
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("IP:", style, GUILayout.Width(30 * scale));
            _ipInput = GUILayout.TextField(_ipInput, fieldStyle);
            GUILayout.Label(":", style, GUILayout.Width(10 * scale));
            _portInput = GUILayout.TextField(_portInput, fieldStyle, GUILayout.Width(70 * scale));
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_manager.IsConnected ? "Disconnect" : "Connect", btnStyle))
            {
                if (_manager.IsConnected)
                    _manager.TcpClient.Disconnect();
                else if (int.TryParse(_portInput, out int port))
                    _manager.SetServer(_ipInput, port);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            // Tracking toggles
            _manager.sendHead = GUILayout.Toggle(_manager.sendHead, "Head", style);
            _manager.sendControllers = GUILayout.Toggle(_manager.sendControllers, "Controllers", style);
            _manager.sendHands = GUILayout.Toggle(_manager.sendHands, "Hands", style);
            _manager.sendBody = GUILayout.Toggle(_manager.sendBody, "Body", style);
            _manager.sendMotion = GUILayout.Toggle(_manager.sendMotion, "Motion Trackers", style);

            GUILayout.Space(8);

            // Camera preview toggle
            bool canCamera = _manager.IsConnected && _manager.Camera != null;
            GUI.enabled = canCamera;
            bool newCameraPreview = GUILayout.Toggle(_cameraPreview, "Camera Preview", style);
            GUI.enabled = true;

            if (newCameraPreview != _cameraPreview && canCamera)
            {
                _cameraPreview = newCameraPreview;
                if (_cameraPreview)
                    _manager.Camera.StartPreview(_manager.TcpClient);
                else
                    _manager.Camera.StopPreview();
            }

            if (_manager.Camera != null && _manager.Camera.IsActive)
            {
                GUILayout.Label("  Camera: streaming", style);

                // Draw the camera texture
                var tex = _manager.Camera.Texture;
                if (tex != null)
                {
                    float aspect = (float)tex.width / tex.height;
                    float previewW = w - 20 * scale;
                    float previewH = previewW / aspect;
                    var rect = GUILayoutUtility.GetRect(previewW, previewH);
                    GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit);
                }
            }

            GUILayout.EndArea();
        }

        private void OnServerFound(string ip, int port)
        {
            for (int i = 0; i < _discoveredServers.Count; i++)
            {
                if (_discoveredServers[i].Ip == ip && _discoveredServers[i].Port == port)
                    return;
            }

            _discoveredServers.Add(new DiscoveredServer { Ip = ip, Port = port });
            _discoveredServersDirty = true;
        }

        private void EnsureWorldSpaceUi()
        {
            if (_manager == null || _worldCanvas != null)
                return;

            var mainCamera = UnityEngine.Camera.main;
            if (mainCamera == null)
                return;

            EnsureXrEventSystem();

            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            var canvasObject = new GameObject("PicoBridge XR UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster));
            canvasObject.transform.SetParent(mainCamera.transform, false);
            canvasObject.transform.localPosition = new Vector3(0f, -0.05f, 1.0f);
            canvasObject.transform.localRotation = Quaternion.identity;
            canvasObject.transform.localScale = Vector3.one * 0.001f;

            _worldCanvas = canvasObject.GetComponent<Canvas>();
            _worldCanvas.renderMode = RenderMode.WorldSpace;
            _worldCanvas.worldCamera = mainCamera;

            var canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(900f, 1200f);

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;

            var panel = CreateUIObject("Panel", canvasObject.transform);
            var panelRect = panel.GetComponent<RectTransform>();
            StretchRect(panelRect);

            var panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0.05f, 0.05f, 0.05f, 0.88f);

            var panelLayout = panel.AddComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(24, 24, 24, 24);
            panelLayout.spacing = 12f;
            panelLayout.childControlHeight = false;
            panelLayout.childControlWidth = true;
            panelLayout.childForceExpandHeight = false;
            panelLayout.childForceExpandWidth = true;

            CreateText(panel.transform, "PICO Bridge", 42, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white, 64f);
            _statusLabel = CreateText(panel.transform, _statusText, 28, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white, 48f);

            CreateText(panel.transform, "Discovered Servers", 28, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white, 42f);
            var serverContainer = CreateUIObject("ServerButtons", panel.transform);
            _serverButtonContainer = serverContainer.GetComponent<RectTransform>();
            var serverLayout = serverContainer.AddComponent<VerticalLayoutGroup>();
            serverLayout.spacing = 8f;
            serverLayout.childControlHeight = false;
            serverLayout.childControlWidth = true;
            serverLayout.childForceExpandHeight = false;
            serverLayout.childForceExpandWidth = true;
            serverContainer.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var addressRow = CreateHorizontalRow(panel.transform, 44f);
            CreateText(addressRow.transform, "IP", 24, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white, 44f, 80f);
            _ipField = CreateInputField(addressRow.transform, _ipInput, 24, 1f);
            CreateText(addressRow.transform, "Port", 24, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white, 44f, 90f);
            _portField = CreateInputField(addressRow.transform, _portInput, 24, 0f, 180f);

            _connectButton = CreateButton(panel.transform, _manager.IsConnected ? "Disconnect" : "Connect", 30, 56f, OnConnectButtonPressed, out _connectButtonLabel);

            CreateText(panel.transform, "Tracking", 28, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white, 42f);
            _headToggle = CreateToggle(panel.transform, "Head", _manager.sendHead, value => _manager.sendHead = value);
            _controllersToggle = CreateToggle(panel.transform, "Controllers", _manager.sendControllers, value => _manager.sendControllers = value);
            _handsToggle = CreateToggle(panel.transform, "Hands", _manager.sendHands, value => _manager.sendHands = value);
            _bodyToggle = CreateToggle(panel.transform, "Body", _manager.sendBody, value => _manager.sendBody = value);
            _motionToggle = CreateToggle(panel.transform, "Motion Trackers", _manager.sendMotion, value => _manager.sendMotion = value);

            _cameraToggle = CreateToggle(panel.transform, "Camera Preview", _cameraPreview, OnCameraPreviewChanged);

            _previewImage = CreateRawImage(panel.transform, 520f, 292.5f);
            _previewImage.gameObject.SetActive(false);

            _discoveredServersDirty = true;
            SyncWorldSpaceUi();
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
            xrUiInputModule.enableMouseInput = false;
            xrUiInputModule.enableTouchInput = false;
            xrUiInputModule.enableBuiltinActionsAsFallback = true;
        }

        private void SyncWorldSpaceUi()
        {
            if (_worldCanvas == null)
                return;

            if (_statusLabel != null)
                _statusLabel.text = $"Status: {_statusText}";

            if (_ipField != null && !_ipField.isFocused && _ipField.text != _ipInput)
                _ipField.text = _ipInput;

            if (_portField != null && !_portField.isFocused && _portField.text != _portInput)
                _portField.text = _portInput;

            if (_connectButtonLabel != null)
                _connectButtonLabel.text = _manager.IsConnected ? "Disconnect" : "Connect";

            if (_headToggle != null)
                _headToggle.SetIsOnWithoutNotify(_manager.sendHead);
            if (_controllersToggle != null)
                _controllersToggle.SetIsOnWithoutNotify(_manager.sendControllers);
            if (_handsToggle != null)
                _handsToggle.SetIsOnWithoutNotify(_manager.sendHands);
            if (_bodyToggle != null)
                _bodyToggle.SetIsOnWithoutNotify(_manager.sendBody);
            if (_motionToggle != null)
                _motionToggle.SetIsOnWithoutNotify(_manager.sendMotion);
            if (_cameraToggle != null)
                _cameraToggle.SetIsOnWithoutNotify(_cameraPreview);

            if (_previewImage != null)
            {
                var texture = _manager.Camera != null && _manager.Camera.IsActive ? _manager.Camera.Texture : null;
                _previewImage.texture = texture;
                _previewImage.gameObject.SetActive(texture != null);
            }

            if (_discoveredServersDirty)
                RebuildDiscoveredServerButtons();
        }

        private void RebuildDiscoveredServerButtons()
        {
            if (_serverButtonContainer == null)
                return;

            for (int i = _serverButtonContainer.childCount - 1; i >= 0; i--)
                Destroy(_serverButtonContainer.GetChild(i).gameObject);

            for (int i = 0; i < _discoveredServers.Count; i++)
            {
                var server = _discoveredServers[i];
                string label = $"{server.Ip}:{server.Port}";
                CreateButton(_serverButtonContainer, label, 24, 48f, () =>
                {
                    _ipInput = server.Ip;
                    _portInput = server.Port.ToString();
                    if (_ipField != null) _ipField.text = _ipInput;
                    if (_portField != null) _portField.text = _portInput;
                    _manager.SetServer(server.Ip, server.Port);
                }, out _);
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

        private void OnCameraPreviewChanged(bool value)
        {
            if (_manager == null || !_manager.IsConnected || _manager.Camera == null)
            {
                _cameraPreview = false;
                if (_cameraToggle != null)
                    _cameraToggle.SetIsOnWithoutNotify(false);
                return;
            }

            _cameraPreview = value;
            if (_cameraPreview)
                _manager.Camera.StartPreview(_manager.TcpClient);
            else
                _manager.Camera.StopPreview();
        }

        private GameObject CreateHorizontalRow(Transform parent, float minHeight)
        {
            var row = CreateUIObject("Row", parent);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var layoutElement = row.AddComponent<LayoutElement>();
            layoutElement.minHeight = minHeight;
            layoutElement.preferredHeight = minHeight;
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

            var layoutElement = textObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = preferredHeight;
            if (preferredWidth > 0f)
                layoutElement.preferredWidth = preferredWidth;

            return textComponent;
        }

        private InputField CreateInputField(Transform parent, string value, int fontSize, float flexibleWidth, float preferredWidth = -1f)
        {
            var fieldObject = CreateUIObject("InputField", parent);
            var image = fieldObject.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.12f);

            var layoutElement = fieldObject.AddComponent<LayoutElement>();
            layoutElement.minHeight = 44f;
            layoutElement.preferredHeight = 44f;
            layoutElement.flexibleWidth = flexibleWidth;
            if (preferredWidth > 0f)
                layoutElement.preferredWidth = preferredWidth;

            var inputField = fieldObject.AddComponent<InputField>();
            inputField.targetGraphic = image;

            var textViewport = CreateUIObject("Text", fieldObject.transform);
            var textRect = textViewport.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 6f);
            textRect.offsetMax = new Vector2(-12f, -6f);

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
            placeholderRect.offsetMin = new Vector2(12f, 6f);
            placeholderRect.offsetMax = new Vector2(-12f, -6f);

            var placeholderText = placeholderObject.AddComponent<Text>();
            placeholderText.font = _font;
            placeholderText.fontSize = fontSize;
            placeholderText.alignment = TextAnchor.MiddleLeft;
            placeholderText.color = new Color(1f, 1f, 1f, 0.35f);
            placeholderText.text = value;
            placeholderText.supportRichText = false;

            inputField.textComponent = textComponent;
            inputField.placeholder = placeholderText;
            inputField.text = value;

            return inputField;
        }

        private Button CreateButton(Transform parent, string label, int fontSize, float preferredHeight, UnityEngine.Events.UnityAction onClick, out Text labelText)
        {
            var buttonObject = CreateUIObject("Button", parent);
            var image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.16f, 0.45f, 0.85f, 0.95f);

            var layoutElement = buttonObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = preferredHeight;
            layoutElement.minHeight = preferredHeight;

            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            labelText = CreateText(buttonObject.transform, label, fontSize, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white, preferredHeight);
            var labelRect = labelText.GetComponent<RectTransform>();
            StretchRect(labelRect);

            return button;
        }

        private Toggle CreateToggle(Transform parent, string label, bool value, UnityEngine.Events.UnityAction<bool> onChanged)
        {
            var row = CreateHorizontalRow(parent, 42f);

            var toggleObject = CreateUIObject("Toggle", row.transform);
            var toggleLayout = toggleObject.AddComponent<LayoutElement>();
            toggleLayout.preferredWidth = 42f;
            toggleLayout.preferredHeight = 42f;

            var backgroundObject = CreateUIObject("Background", toggleObject.transform);
            var backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0f, 0.5f);
            backgroundRect.anchorMax = new Vector2(0f, 0.5f);
            backgroundRect.sizeDelta = new Vector2(32f, 32f);
            backgroundRect.anchoredPosition = new Vector2(16f, 0f);

            var backgroundImage = backgroundObject.AddComponent<Image>();
            backgroundImage.color = new Color(1f, 1f, 1f, 0.2f);

            var checkmarkObject = CreateUIObject("Checkmark", backgroundObject.transform);
            var checkmarkRect = checkmarkObject.GetComponent<RectTransform>();
            StretchRect(checkmarkRect, 6f);

            var checkmarkImage = checkmarkObject.AddComponent<Image>();
            checkmarkImage.color = new Color(0.2f, 0.8f, 0.35f, 1f);

            var toggle = toggleObject.AddComponent<Toggle>();
            toggle.targetGraphic = backgroundImage;
            toggle.graphic = checkmarkImage;
            toggle.isOn = value;
            toggle.onValueChanged.AddListener(onChanged);

            CreateText(row.transform, label, 24, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white, 42f);
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

            return image;
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
