using PicoBridge.Network;
using PicoBridge.Tracking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PicoBridge.UI
{
    public class PicoBridgePanelController : MonoBehaviour
    {
        [SerializeField] private PicoBridgeManager manager;
        [SerializeField] private PicoBridgePanelView view;
        [SerializeField] private float refreshInterval = 0.25f;
        [SerializeField] private bool rebuildCompactLayoutOnStart = true;
        [SerializeField] private Vector2 compactCanvasSize = new Vector2(1120f, 860f);
        [SerializeField, Range(MinUiOpacity, 1f)] private float uiOpacity = 1f;

        private float _refreshTimer;
        private bool _cameraPreviewRequested;
        private bool _layoutBuilt;

        private const float MinUiOpacity = 0.05f;
        private static readonly Color PanelColor = new Color(0.035f, 0.041f, 0.052f, 0.94f);
        private static readonly Color PreviewEmptyColor = new Color(0f, 0f, 0f, 0.82f);
        private static readonly Color DisconnectedColor = new Color(0.95f, 0.22f, 0.22f, 1f);
        private static readonly Color ConnectingColor = new Color(1f, 0.69f, 0.18f, 1f);
        private static readonly Color ConnectedColor = new Color(0.12f, 0.78f, 0.38f, 1f);
        private static readonly Color SignalInactiveColor = new Color(0.24f, 0.27f, 0.31f, 1f);
        private static readonly Color MutedTextColor = new Color(0.70f, 0.76f, 0.84f, 1f);
        private static readonly TrackingSignalKind[] TrackingSignals =
        {
            TrackingSignalKind.Head,
            TrackingSignalKind.LeftController,
            TrackingSignalKind.RightController,
            TrackingSignalKind.LeftHand,
            TrackingSignalKind.RightHand,
            TrackingSignalKind.Body,
            TrackingSignalKind.Motion
        };
        private static readonly string[] TrackingSignalLabels =
        {
            "HEAD",
            "L CTRL",
            "R CTRL",
            "L HAND",
            "R HAND",
            "BODY",
            "MOTION"
        };

        private void Awake()
        {
            if (view == null)
                view = GetComponent<PicoBridgePanelView>();
            if (manager == null)
                manager = FindObjectOfType<PicoBridgeManager>();

            if (rebuildCompactLayoutOnStart || NeedsCompactLayoutRefresh())
                RebuildCompactLayout();

            ConfigureOpacityControl();
        }

        private void Start()
        {
            EnableAutomaticTrackingStreams();
            RefreshAll();
        }

        private void OnDisable()
        {
            if (_cameraPreviewRequested)
                manager?.WebRtcCamera?.StopPreview();

            _cameraPreviewRequested = false;
        }

        private void OnDestroy()
        {
            if (view != null && view.uiOpacitySlider != null)
                view.uiOpacitySlider.onValueChanged.RemoveListener(SetUiOpacity);
        }

        private void Update()
        {
            if (manager == null)
                manager = FindObjectOfType<PicoBridgeManager>();

            EnableAutomaticTrackingStreams();
            UpdateAutomaticCameraPreview();

            _refreshTimer += Time.deltaTime;
            if (_refreshTimer < refreshInterval)
                return;

            _refreshTimer = 0f;
            RefreshAll();
        }

        private void EnableAutomaticTrackingStreams()
        {
            if (manager == null)
                return;

            manager.sendHead = true;
            manager.sendControllers = true;
            manager.sendHands = true;
            manager.sendBody = true;
            manager.sendMotion = true;
        }

        private void UpdateAutomaticCameraPreview()
        {
            if (manager == null || manager.WebRtcCamera == null)
                return;

            if (!manager.IsConnected || manager.TcpClient == null)
            {
                if (_cameraPreviewRequested || manager.WebRtcCamera.IsActive)
                    manager.WebRtcCamera.StopPreview();

                _cameraPreviewRequested = false;
                return;
            }

            if (_cameraPreviewRequested)
                return;

            _cameraPreviewRequested = true;
            manager.WebRtcCamera.StartPreview(manager.TcpClient, 1280, 720, 30, 8 * 1024 * 1024);
        }

        private void RefreshAll()
        {
            if (view == null)
                return;

            RefreshConnectionStatus();
            RefreshTrackingStatus();
            RefreshCameraStatus();
        }

        private void RefreshConnectionStatus()
        {
            var state = manager != null && manager.TcpClient != null ? manager.TcpClient.State : SocketState.None;
            bool connected = manager != null && manager.IsConnected;
            bool connecting = state == SocketState.Connecting;
            string status = connected ? "Connected" : connecting ? "Connecting" : "Disconnected";
            Color color = connected ? ConnectedColor : connecting ? ConnectingColor : DisconnectedColor;

            if (view.statusPillImage != null)
                view.statusPillImage.color = color;
            if (view.statusPillText != null)
                view.statusPillText.text = status;
            if (view.endpointText != null)
            {
                view.endpointText.gameObject.SetActive(connected);
                view.endpointText.text = connected ? $"{manager.serverAddress}:{manager.serverPort}" : string.Empty;
            }
        }

        private void RefreshTrackingStatus()
        {
            if (view.trackingSignalImages == null || view.trackingSignalLabels == null)
                return;

            for (int i = 0; i < TrackingSignals.Length; i++)
            {
                bool active = manager != null && TrackingSignalStatus.HasValidSignal(TrackingSignals[i]);
                if (i < view.trackingSignalImages.Length && view.trackingSignalImages[i] != null)
                    view.trackingSignalImages[i].color = active ? ConnectedColor : SignalInactiveColor;
                if (i < view.trackingSignalLabels.Length && view.trackingSignalLabels[i] != null)
                    view.trackingSignalLabels[i].color = active ? Color.white : MutedTextColor;
            }
        }

        private void RefreshCameraStatus()
        {
            bool connected = manager != null && manager.IsConnected;
            var camera = manager != null ? manager.WebRtcCamera : null;
            var texture = connected && camera != null ? camera.Texture : null;

            if (view.cameraPreviewImage != null)
            {
                view.cameraPreviewImage.texture = texture;
                view.cameraPreviewImage.color = texture != null ? Color.white : PreviewEmptyColor;
            }

            if (view.cameraStatusText != null)
            {
                if (!connected)
                    view.cameraStatusText.text = "Camera idle";
                else if (texture != null)
                    view.cameraStatusText.text = $"Camera live  {camera.FrameCount}";
                else
                    view.cameraStatusText.text = camera != null ? camera.Status : "Camera waiting";
            }
        }

        private void RebuildCompactLayout()
        {
            if (_layoutBuilt)
                return;

            if (view == null)
                view = gameObject.AddComponent<PicoBridgePanelView>();

            ConfigureCanvas();
            ClearChildren(transform);
            BuildCompactPanel();
            _layoutBuilt = true;
        }

        private bool NeedsCompactLayoutRefresh()
        {
            return view == null ||
                   view.rootCanvasGroup == null ||
                   view.uiOpacitySlider == null ||
                   view.trackingSignalImages == null ||
                   view.trackingSignalImages.Length != TrackingSignals.Length ||
                   view.trackingSignalLabels == null ||
                   view.trackingSignalLabels.Length != TrackingSignals.Length;
        }

        private void ConfigureOpacityControl()
        {
            if (view == null)
                return;

            if (view.rootCanvasGroup == null)
                view.rootCanvasGroup = GetOrAdd<CanvasGroup>(gameObject);

            uiOpacity = Mathf.Clamp(uiOpacity, MinUiOpacity, 1f);
            view.rootCanvasGroup.alpha = uiOpacity;
            view.rootCanvasGroup.interactable = true;
            view.rootCanvasGroup.blocksRaycasts = true;

            if (view.uiOpacitySlider == null)
                return;

            view.uiOpacitySlider.minValue = MinUiOpacity;
            view.uiOpacitySlider.maxValue = 1f;
            view.uiOpacitySlider.wholeNumbers = false;
            view.uiOpacitySlider.SetValueWithoutNotify(uiOpacity);
            view.uiOpacitySlider.onValueChanged.RemoveListener(SetUiOpacity);
            view.uiOpacitySlider.onValueChanged.AddListener(SetUiOpacity);
        }

        private void SetUiOpacity(float value)
        {
            uiOpacity = Mathf.Clamp(value, MinUiOpacity, 1f);
            if (view != null && view.rootCanvasGroup != null)
                view.rootCanvasGroup.alpha = uiOpacity;
        }

        private void ConfigureCanvas()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
                return;

            if (canvas.renderMode == RenderMode.WorldSpace)
            {
                var canvasRect = canvas.GetComponent<RectTransform>();
                if (canvasRect != null)
                    canvasRect.sizeDelta = compactCanvasSize;
            }

            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
                scaler.dynamicPixelsPerUnit = Mathf.Max(scaler.dynamicPixelsPerUnit, 12f);
        }

        private void BuildCompactPanel()
        {
            var root = GetComponent<RectTransform>();
            if (root == null)
                root = gameObject.AddComponent<RectTransform>();

            Stretch(root, 20f);

            var panelImage = GetOrAdd<Image>(gameObject);
            panelImage.color = PanelColor;
            panelImage.raycastTarget = true;

            view.rootCanvasGroup = GetOrAdd<CanvasGroup>(gameObject);

            var layout = GetOrAdd<VerticalLayoutGroup>(gameObject);
            layout.padding = new RectOffset(18, 18, 18, 18);
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var header = CreateRow("Connection", transform, 66f, 14f);
            var pill = CreateRect("ConnectionStatus", header);
            view.statusPillImage = AddImage(pill.gameObject, DisconnectedColor);
            AddLayoutElement(pill.gameObject, 210f, 54f, 0f, 0f);
            view.statusPillText = CreateText("Label", pill, "Disconnected", 24, FontStyles.Bold, TextAlignmentOptions.Center, Color.white);
            Stretch(view.statusPillText.rectTransform, 0f);

            view.endpointText = CreateText("Endpoint", header, string.Empty, 30, FontStyles.Bold, TextAlignmentOptions.Left, Color.white);
            view.endpointText.enableWordWrapping = false;
            AddLayoutElement(view.endpointText.gameObject, -1f, 54f, 1f, 0f);

            var previewFrame = CreateRect("CameraPreview", transform);
            AddLayoutElement(previewFrame.gameObject, -1f, 594f, 1f, 1f);
            view.cameraPreviewImage = previewFrame.gameObject.AddComponent<RawImage>();
            view.cameraPreviewImage.color = PreviewEmptyColor;

            var footer = CreateColumn("Signals", transform, 8f);
            AddLayoutElement(footer.gameObject, -1f, 96f, 0f, 0f);

            var tracking = CreateRow("TrackingSignals", footer, 50f, 8f);
            view.trackingSignalImages = new Image[TrackingSignals.Length];
            view.trackingSignalLabels = new TMP_Text[TrackingSignals.Length];
            for (int i = 0; i < TrackingSignals.Length; i++)
                CreateSignalPill(tracking, i);

            var statusRow = CreateRow("StatusAndOpacity", footer, 30f, 12f);
            view.cameraStatusText = CreateText("CameraStatus", statusRow, "Camera idle", 18, FontStyles.Bold, TextAlignmentOptions.Left, MutedTextColor);
            view.cameraStatusText.enableWordWrapping = false;
            AddLayoutElement(view.cameraStatusText.gameObject, -1f, 30f, 1f, 0f);

            var opacityControl = CreateRow("OpacityControl", statusRow, 30f, 8f);
            AddLayoutElement(opacityControl.gameObject, 330f, 30f, 0f, 0f);
            var opacityLabel = CreateText("Label", opacityControl, "UI", 16, FontStyles.Bold, TextAlignmentOptions.Center, MutedTextColor);
            opacityLabel.enableWordWrapping = false;
            AddLayoutElement(opacityLabel.gameObject, 28f, 30f, 0f, 0f);
            view.uiOpacitySlider = CreateOpacitySlider(opacityControl);
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                if (Application.isPlaying)
                {
                    child.SetActive(false);
                    Destroy(child);
                }
                else
                    DestroyImmediate(child);
            }
        }

        private static RectTransform CreateRow(string name, Transform parent, float height, float spacing)
        {
            var rect = CreateRect(name, parent);
            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            AddLayoutElement(rect.gameObject, -1f, height, 0f, 0f);
            return rect;
        }

        private static RectTransform CreateColumn(string name, Transform parent, float spacing)
        {
            var rect = CreateRect(name, parent);
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            return rect;
        }

        private void CreateSignalPill(Transform parent, int index)
        {
            var pill = CreateRect(TrackingSignalLabels[index], parent);
            view.trackingSignalImages[index] = AddImage(pill.gameObject, SignalInactiveColor);
            AddLayoutElement(pill.gameObject, -1f, 46f, 1f, 0f);

            view.trackingSignalLabels[index] = CreateText("Label", pill, TrackingSignalLabels[index], 16, FontStyles.Bold, TextAlignmentOptions.Center, MutedTextColor);
            view.trackingSignalLabels[index].enableWordWrapping = false;
            Stretch(view.trackingSignalLabels[index].rectTransform, 0f);
        }

        private Slider CreateOpacitySlider(Transform parent)
        {
            var sliderRect = CreateRect("OpacityBar", parent);
            AddLayoutElement(sliderRect.gameObject, 294f, 30f, 1f, 0f);

            var slider = sliderRect.gameObject.AddComponent<Slider>();
            slider.minValue = MinUiOpacity;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.transition = Selectable.Transition.ColorTint;

            var background = CreateRect("Background", sliderRect);
            var backgroundImage = AddImage(background.gameObject, new Color(0.12f, 0.14f, 0.17f, 1f));
            Stretch(background, 0f);
            background.offsetMin = new Vector2(0f, 9f);
            background.offsetMax = new Vector2(0f, -9f);

            var fillArea = CreateRect("Fill Area", sliderRect);
            Stretch(fillArea, 0f);
            fillArea.offsetMin = new Vector2(3f, 9f);
            fillArea.offsetMax = new Vector2(-3f, -9f);

            var fill = CreateRect("Fill", fillArea);
            var fillImage = AddImage(fill.gameObject, new Color(0.84f, 0.88f, 0.94f, 1f));
            Stretch(fill, 0f);

            var handleArea = CreateRect("Handle Slide Area", sliderRect);
            Stretch(handleArea, 0f);
            handleArea.offsetMin = new Vector2(8f, 0f);
            handleArea.offsetMax = new Vector2(-8f, 0f);

            var handle = CreateRect("Handle", handleArea);
            var handleImage = AddImage(handle.gameObject, Color.white);
            handle.sizeDelta = new Vector2(18f, 24f);

            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            backgroundImage.raycastTarget = true;
            fillImage.raycastTarget = false;
            slider.SetValueWithoutNotify(uiOpacity);
            return slider;
        }

        private static TMP_Text CreateText(string name, Transform parent, string text, int fontSize, FontStyles style, TextAlignmentOptions alignment, Color color)
        {
            var rect = CreateRect(name, parent);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.alignment = alignment;
            label.color = color;
            label.raycastTarget = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return label;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            return gameObject.GetComponent<RectTransform>();
        }

        private static Image AddImage(GameObject target, Color color)
        {
            var image = target.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static LayoutElement AddLayoutElement(GameObject target, float width, float height, float flexibleWidth, float flexibleHeight)
        {
            var element = target.GetComponent<LayoutElement>();
            if (element == null)
                element = target.AddComponent<LayoutElement>();

            if (width > 0f)
                element.preferredWidth = width;
            if (height > 0f)
            {
                element.preferredHeight = height;
                element.minHeight = height;
            }

            element.flexibleWidth = flexibleWidth;
            element.flexibleHeight = flexibleHeight;
            return element;
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            if (component == null)
                component = target.AddComponent<T>();
            return component;
        }

        private static void Stretch(RectTransform rectTransform, float inset)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = new Vector2(inset, inset);
            rectTransform.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
