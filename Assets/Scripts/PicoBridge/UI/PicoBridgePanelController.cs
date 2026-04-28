using PicoBridge.Network;
using PicoBridge.Tracking;
using UnityEngine;

namespace PicoBridge.UI
{
    public class PicoBridgePanelController : MonoBehaviour
    {
        [SerializeField] private PicoBridgeManager manager;
        [SerializeField] private PicoBridgePanelView view;
        [SerializeField] private float refreshInterval = 0.25f;
        [SerializeField, Range(MinUiOpacity, 1f)] private float uiOpacity = 1f;
        [SerializeField] private bool uiCollapsed;

        private float _refreshTimer;
        private bool _cameraPreviewRequested;
        private bool _hasExpandedRect;
        private Vector2 _expandedAnchorMin;
        private Vector2 _expandedAnchorMax;
        private Vector2 _expandedPivot;
        private Vector2 _expandedSizeDelta;
        private Vector2 _expandedAnchoredPosition;

        private const float MinUiOpacity = 0.05f;
        private const string CollapseExpandedIcon = "▼";
        private const string CollapseCollapsedIcon = "▲";
        private static readonly Vector2 CollapsedAnchor = new Vector2(0.5f, 0f);
        private static readonly Vector2 CollapsedSize = new Vector2(64f, 44f);
        private static readonly Color PreviewEmptyColor = new Color(0.018f, 0.023f, 0.026f, 0.72f);
        private static readonly Color DisconnectedColor = new Color(0.88f, 0.22f, 0.29f, 1f);
        private static readonly Color ConnectingColor = new Color(0.96f, 0.63f, 0.16f, 1f);
        private static readonly Color ConnectedColor = new Color(0.16f, 0.74f, 0.43f, 1f);
        private static readonly Color SignalInactiveColor = new Color(0.16f, 0.19f, 0.205f, 1f);
        private static readonly Color TextColor = new Color(0.94f, 0.975f, 0.985f, 1f);
        private static readonly Color MutedTextColor = new Color(0.66f, 0.72f, 0.75f, 1f);
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
        private void Awake()
        {
            if (view == null)
                view = GetComponent<PicoBridgePanelView>();
            if (manager == null)
                manager = FindObjectOfType<PicoBridgeManager>();

            CaptureExpandedRect();
            ConfigureOpacityControl();
            ConfigureCollapseControl();
            ApplyCollapseState();
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
            if (view != null && view.collapseButton != null)
                view.collapseButton.onClick.RemoveListener(ToggleCollapsed);
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
                view.endpointText.gameObject.SetActive(true);
                view.endpointText.text = manager != null ? $"{manager.serverAddress}:{manager.serverPort}" : "Endpoint waiting";
                view.endpointText.color = connected ? TextColor : MutedTextColor;
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
                    view.trackingSignalLabels[i].color = active ? TextColor : MutedTextColor;
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

        private void ConfigureOpacityControl()
        {
            if (view == null)
                return;

            if (view.rootCanvasGroup == null)
                view.rootCanvasGroup = GetComponent<CanvasGroup>();

            if (view.rootCanvasGroup != null)
            {
                uiOpacity = Mathf.Clamp(uiOpacity, MinUiOpacity, 1f);
                view.rootCanvasGroup.alpha = uiOpacity;
                view.rootCanvasGroup.interactable = true;
                view.rootCanvasGroup.blocksRaycasts = true;
            }

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

        private void ConfigureCollapseControl()
        {
            if (view == null || view.collapseButton == null)
                return;

            view.collapseButton.onClick.RemoveListener(ToggleCollapsed);
            view.collapseButton.onClick.AddListener(ToggleCollapsed);
        }

        private void ToggleCollapsed()
        {
            uiCollapsed = !uiCollapsed;
            ApplyCollapseState();
        }

        private void ApplyCollapseState()
        {
            if (view == null)
                return;

            if (view.panelContentRoot != null)
                view.panelContentRoot.gameObject.SetActive(!uiCollapsed);
            if (view.panelImage != null)
                view.panelImage.enabled = !uiCollapsed;
            if (view.collapseButtonText != null)
                view.collapseButtonText.text = uiCollapsed ? CollapseCollapsedIcon : CollapseExpandedIcon;

            var root = GetComponent<RectTransform>();
            if (root == null)
                return;

            if (uiCollapsed)
                SetCollapsedRect(root);
            else
                RestoreExpandedRect(root);
        }

        private void CaptureExpandedRect()
        {
            var rectTransform = GetComponent<RectTransform>();
            if (rectTransform == null)
                return;

            _expandedAnchorMin = rectTransform.anchorMin;
            _expandedAnchorMax = rectTransform.anchorMax;
            _expandedPivot = rectTransform.pivot;
            _expandedSizeDelta = rectTransform.sizeDelta;
            _expandedAnchoredPosition = rectTransform.anchoredPosition;
            _hasExpandedRect = true;
        }

        private void RestoreExpandedRect(RectTransform rectTransform)
        {
            if (!_hasExpandedRect)
                return;

            rectTransform.anchorMin = _expandedAnchorMin;
            rectTransform.anchorMax = _expandedAnchorMax;
            rectTransform.pivot = _expandedPivot;
            rectTransform.sizeDelta = _expandedSizeDelta;
            rectTransform.anchoredPosition = _expandedAnchoredPosition;
        }

        private static void SetCollapsedRect(RectTransform rectTransform)
        {
            rectTransform.anchorMin = CollapsedAnchor;
            rectTransform.anchorMax = CollapsedAnchor;
            rectTransform.pivot = CollapsedAnchor;
            rectTransform.anchoredPosition = new Vector2(0f, 20f);
            rectTransform.sizeDelta = CollapsedSize;
        }
    }
}
