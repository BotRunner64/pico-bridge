using PicoBridge.Network;
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
        [SerializeField] private Vector2 compactCanvasSize = new Vector2(1120f, 820f);

        private float _refreshTimer;
        private bool _cameraPreviewRequested;
        private bool _layoutBuilt;

        private static readonly Color PanelColor = new Color(0.035f, 0.041f, 0.052f, 0.94f);
        private static readonly Color PreviewEmptyColor = new Color(0f, 0f, 0f, 0.82f);
        private static readonly Color DisconnectedColor = new Color(0.95f, 0.22f, 0.22f, 1f);
        private static readonly Color ConnectingColor = new Color(1f, 0.69f, 0.18f, 1f);
        private static readonly Color ConnectedColor = new Color(0.12f, 0.78f, 0.38f, 1f);
        private static readonly Color MutedTextColor = new Color(0.70f, 0.76f, 0.84f, 1f);

        private void Awake()
        {
            if (view == null)
                view = GetComponent<PicoBridgePanelView>();
            if (manager == null)
                manager = FindObjectOfType<PicoBridgeManager>();

            if (rebuildCompactLayoutOnStart)
                RebuildCompactLayout();
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
            bool streaming = manager != null && manager.IsConnected;
            if (view.trackingStatusImage != null)
                view.trackingStatusImage.color = streaming ? ConnectedColor : DisconnectedColor;
            if (view.trackingStatusText != null)
                view.trackingStatusText.text = streaming ? $"Tracking {manager.trackingFps} FPS" : "Tracking idle";
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
            AddLayoutElement(previewFrame.gameObject, -1f, 588f, 1f, 1f);
            view.cameraPreviewImage = previewFrame.gameObject.AddComponent<RawImage>();
            view.cameraPreviewImage.color = PreviewEmptyColor;

            var footer = CreateRow("Signals", transform, 58f, 18f);
            var tracking = CreateRow("Tracking", footer, 52f, 10f);
            AddLayoutElement(tracking.gameObject, -1f, 52f, 1f, 0f);
            view.trackingStatusImage = CreateSignalDot("Dot", tracking, DisconnectedColor);
            view.trackingStatusText = CreateText("TrackingStatus", tracking, "Tracking idle", 22, FontStyles.Bold, TextAlignmentOptions.Left, Color.white);
            AddLayoutElement(view.trackingStatusText.gameObject, -1f, 44f, 1f, 0f);

            view.cameraStatusText = CreateText("CameraStatus", footer, "Camera idle", 22, FontStyles.Bold, TextAlignmentOptions.Right, MutedTextColor);
            view.cameraStatusText.enableWordWrapping = false;
            AddLayoutElement(view.cameraStatusText.gameObject, 300f, 52f, 0f, 0f);
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

        private static Image CreateSignalDot(string name, Transform parent, Color color)
        {
            var dot = CreateRect(name, parent);
            var image = AddImage(dot.gameObject, color);
            AddLayoutElement(dot.gameObject, 18f, 18f, 0f, 0f);
            return image;
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
