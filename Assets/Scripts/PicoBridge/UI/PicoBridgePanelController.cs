using System.Collections.Generic;
using System.Text;
using PicoBridge.Network;
using UnityEngine;
using UnityEngine.UI;

namespace PicoBridge.UI
{
    public class PicoBridgePanelController : MonoBehaviour
    {
        private struct DiscoveredServer
        {
            public string Ip;
            public int Port;
            public float LastSeen;
        }

        [SerializeField] private PicoBridgeManager manager;
        [SerializeField] private PicoBridgePanelView view;
        [SerializeField] private float refreshInterval = 0.25f;

        private readonly List<DiscoveredServer> _discoveredServers = new List<DiscoveredServer>();
        private readonly List<PicoBridgeServerListItem> _serverItems = new List<PicoBridgeServerListItem>();
        private readonly StringBuilder _diagnosticsBuilder = new StringBuilder(256);
        private float _refreshTimer;
        private bool _cameraPreviewRequested;
        private bool _serverListDirty = true;
        private bool _subscribedToDiscovery;
        private UnityEngine.Events.UnityAction<bool> _headToggleListener;
        private UnityEngine.Events.UnityAction<bool> _controllersToggleListener;
        private UnityEngine.Events.UnityAction<bool> _handsToggleListener;
        private UnityEngine.Events.UnityAction<bool> _bodyToggleListener;
        private UnityEngine.Events.UnityAction<bool> _motionToggleListener;

        private static readonly Color DisconnectedColor = new Color(0.95f, 0.22f, 0.22f, 1f);
        private static readonly Color ConnectingColor = new Color(1f, 0.69f, 0.18f, 1f);
        private static readonly Color ConnectedColor = new Color(0.12f, 0.78f, 0.38f, 1f);
        private static readonly Color ToggleOnColor = new Color(0.12f, 0.78f, 0.38f, 1f);
        private static readonly Color ToggleOffColor = new Color(1f, 1f, 1f, 0.12f);

        private void Awake()
        {
            if (view == null)
                view = GetComponent<PicoBridgePanelView>();
            if (manager == null)
                manager = FindObjectOfType<PicoBridgeManager>();
        }

        private void OnEnable()
        {
            RegisterUiEvents();
        }

        private void Start()
        {
            SyncAddressFieldsFromManager();
            TrySubscribeDiscovery();
            RefreshAll(true);
        }

        private void OnDisable()
        {
            UnregisterUiEvents();
            UnsubscribeDiscovery();
        }

        private void Update()
        {
            if (manager == null)
                manager = FindObjectOfType<PicoBridgeManager>();

            TrySubscribeDiscovery();

            _refreshTimer += Time.deltaTime;
            if (_refreshTimer < refreshInterval)
                return;

            _refreshTimer = 0f;
            RefreshAll(false);
        }

        private void RegisterUiEvents()
        {
            if (view == null)
                return;

            if (view.connectButton != null)
                view.connectButton.onClick.AddListener(HandleConnectPressed);
            if (view.refreshButton != null)
                view.refreshButton.onClick.AddListener(HandleRefreshPressed);
            if (view.cameraPreviewButton != null)
                view.cameraPreviewButton.onClick.AddListener(HandleCameraPreviewPressed);

            _headToggleListener = value => { if (manager != null) manager.sendHead = value; };
            _controllersToggleListener = value => { if (manager != null) manager.sendControllers = value; };
            _handsToggleListener = value => { if (manager != null) manager.sendHands = value; };
            _bodyToggleListener = value => { if (manager != null) manager.sendBody = value; };
            _motionToggleListener = value => { if (manager != null) manager.sendMotion = value; };

            AddToggleListener(view.headToggle, _headToggleListener);
            AddToggleListener(view.controllersToggle, _controllersToggleListener);
            AddToggleListener(view.handsToggle, _handsToggleListener);
            AddToggleListener(view.bodyToggle, _bodyToggleListener);
            AddToggleListener(view.motionToggle, _motionToggleListener);
        }

        private void UnregisterUiEvents()
        {
            if (view == null)
                return;

            if (view.connectButton != null)
                view.connectButton.onClick.RemoveListener(HandleConnectPressed);
            if (view.refreshButton != null)
                view.refreshButton.onClick.RemoveListener(HandleRefreshPressed);
            if (view.cameraPreviewButton != null)
                view.cameraPreviewButton.onClick.RemoveListener(HandleCameraPreviewPressed);

            RemoveToggleListener(view.headToggle, _headToggleListener);
            RemoveToggleListener(view.controllersToggle, _controllersToggleListener);
            RemoveToggleListener(view.handsToggle, _handsToggleListener);
            RemoveToggleListener(view.bodyToggle, _bodyToggleListener);
            RemoveToggleListener(view.motionToggle, _motionToggleListener);
        }

        private static void AddToggleListener(Toggle toggle, UnityEngine.Events.UnityAction<bool> action)
        {
            if (toggle != null)
                toggle.onValueChanged.AddListener(action);
        }

        private static void RemoveToggleListener(Toggle toggle, UnityEngine.Events.UnityAction<bool> action)
        {
            if (toggle != null && action != null)
                toggle.onValueChanged.RemoveListener(action);
        }

        private void TrySubscribeDiscovery()
        {
            if (_subscribedToDiscovery || manager == null || manager.Discovery == null)
                return;

            manager.Discovery.OnServerFound += HandleServerFound;
            _subscribedToDiscovery = true;
        }

        private void UnsubscribeDiscovery()
        {
            if (!_subscribedToDiscovery || manager == null || manager.Discovery == null)
                return;

            manager.Discovery.OnServerFound -= HandleServerFound;
            _subscribedToDiscovery = false;
        }

        private void HandleServerFound(string ip, int port)
        {
            for (int i = 0; i < _discoveredServers.Count; i++)
            {
                if (_discoveredServers[i].Ip == ip && _discoveredServers[i].Port == port)
                {
                    _discoveredServers[i] = new DiscoveredServer { Ip = ip, Port = port, LastSeen = Time.time };
                    _serverListDirty = true;
                    return;
                }
            }

            _discoveredServers.Add(new DiscoveredServer { Ip = ip, Port = port, LastSeen = Time.time });
            _serverListDirty = true;
        }

        private void HandleConnectPressed()
        {
            if (manager == null)
                return;

            if (manager.IsConnected)
            {
                manager.TcpClient?.Disconnect();
                _cameraPreviewRequested = false;
                manager.WebRtcCamera?.StopPreview();
                return;
            }

            string ip = view != null && view.ipInput != null ? view.ipInput.text.Trim() : manager.serverAddress;
            string portText = view != null && view.portInput != null ? view.portInput.text.Trim() : manager.serverPort.ToString();
            if (!int.TryParse(portText, out var port))
                port = manager.serverPort;

            manager.SetServer(ip, port);
        }

        private void HandleRefreshPressed()
        {
            _serverListDirty = true;
            if (manager != null && manager.Discovery != null)
                manager.Discovery.StartListening();
            RefreshServerList();
        }

        private void HandleCameraPreviewPressed()
        {
            if (manager == null || !manager.IsConnected || manager.WebRtcCamera == null || manager.TcpClient == null)
                return;

            _cameraPreviewRequested = !_cameraPreviewRequested;
            if (_cameraPreviewRequested)
                manager.WebRtcCamera.StartPreview(manager.TcpClient, 1280, 720, 30, 8 * 1024 * 1024);
            else
                manager.WebRtcCamera.StopPreview();
        }

        private void HandleServerSelected(string ip, int port)
        {
            if (view != null)
            {
                if (view.ipInput != null)
                    view.ipInput.text = ip;
                if (view.portInput != null)
                    view.portInput.text = port.ToString();
            }

            manager?.SetServer(ip, port);
        }

        private void RefreshAll(bool force)
        {
            if (view == null)
                return;

            RefreshStatus();
            RefreshTrackingToggles();
            RefreshCamera();
            RefreshDiagnostics();

            if (force || _serverListDirty)
                RefreshServerList();
        }

        private void RefreshStatus()
        {
            var state = manager != null && manager.TcpClient != null ? manager.TcpClient.State : SocketState.None;
            bool connected = manager != null && manager.IsConnected;
            string status = connected ? "Connected" : state == SocketState.Connecting ? "Connecting" : state == SocketState.Error ? "Error" : "Disconnected";
            Color color = connected ? ConnectedColor : state == SocketState.Connecting ? ConnectingColor : DisconnectedColor;

            if (view.statusPillImage != null)
                view.statusPillImage.color = color;
            if (view.statusPillText != null)
                view.statusPillText.text = status;
            if (view.subtitleText != null)
                view.subtitleText.text = connected ? "Streaming tracking data to PC receiver" : "Connect to a PC receiver on the same network";
            if (view.statusText != null)
                view.statusText.text = manager == null ? "PicoBridgeManager not found" : status;
            if (view.serverSummaryText != null)
                view.serverSummaryText.text = connected
                    ? $"Active PC: {manager.serverAddress}:{manager.serverPort}"
                    : _discoveredServers.Count > 0
                        ? $"Found {_discoveredServers.Count} receiver{(_discoveredServers.Count == 1 ? "" : "s")}"
                        : "No PC receiver discovered yet";

            if (view.connectButtonLabel != null)
                view.connectButtonLabel.text = connected ? "Disconnect" : "Connect";
        }

        private void RefreshTrackingToggles()
        {
            if (manager == null)
                return;

            SetToggle(view.headToggle, manager.sendHead);
            SetToggle(view.controllersToggle, manager.sendControllers);
            SetToggle(view.handsToggle, manager.sendHands);
            SetToggle(view.bodyToggle, manager.sendBody);
            SetToggle(view.motionToggle, manager.sendMotion);
        }

        private static void SetToggle(Toggle toggle, bool value)
        {
            if (toggle == null)
                return;

            toggle.SetIsOnWithoutNotify(value);
            if (toggle.targetGraphic != null)
                toggle.targetGraphic.color = value ? ToggleOnColor : ToggleOffColor;
        }

        private void RefreshCamera()
        {
            bool connected = manager != null && manager.IsConnected;
            bool available = connected && manager.WebRtcCamera != null;
            if (!available)
            {
                if (_cameraPreviewRequested || (manager != null && manager.WebRtcCamera != null && manager.WebRtcCamera.IsActive))
                    manager?.WebRtcCamera?.StopPreview();

                _cameraPreviewRequested = false;
            }

            if (view.cameraPreviewButton != null)
                view.cameraPreviewButton.interactable = available;
            if (view.cameraPreviewButtonLabel != null)
                view.cameraPreviewButtonLabel.text = _cameraPreviewRequested ? "Stop" : "Preview";

            var texture = available && manager.WebRtcCamera.IsActive ? manager.WebRtcCamera.Texture : null;
            if (view.cameraPreviewImage != null)
            {
                view.cameraPreviewImage.texture = texture;
                view.cameraPreviewImage.color = texture != null ? Color.white : new Color(0f, 0f, 0f, 0.72f);
            }

            if (view.cameraStatusText != null)
            {
                view.cameraStatusText.text = available
                    ? manager.WebRtcCamera.IsActive ? manager.WebRtcCamera.Status : "Preview idle"
                    : "Connect to PC before preview";
            }
        }

        private void RefreshDiagnostics()
        {
            if (view.diagnosticsText == null)
                return;

            _diagnosticsBuilder.Clear();
            _diagnosticsBuilder.Append("UDP: ").Append(manager != null && manager.Discovery != null ? "available" : "not available");
            _diagnosticsBuilder.Append('\n').Append("TCP: ").Append(manager != null && manager.TcpClient != null ? manager.TcpClient.State.ToString() : "None");
            _diagnosticsBuilder.Append('\n').Append("Server: ").Append(manager != null ? $"{manager.serverAddress}:{manager.serverPort}" : "none");
            _diagnosticsBuilder.Append('\n').Append("Discovered: ").Append(_discoveredServers.Count);
            _diagnosticsBuilder.Append('\n').Append("Camera: ").Append(manager != null && manager.WebRtcCamera != null ? manager.WebRtcCamera.Status : "idle");
            view.diagnosticsText.text = _diagnosticsBuilder.ToString();
        }

        private void RefreshServerList()
        {
            if (view.serverListContent == null || view.serverListItemTemplate == null)
                return;

            foreach (var item in _serverItems)
            {
                if (item != null)
                    Destroy(item.gameObject);
            }
            _serverItems.Clear();

            _discoveredServers.Sort((left, right) => right.LastSeen.CompareTo(left.LastSeen));
            for (int i = 0; i < _discoveredServers.Count; i++)
            {
                var server = _discoveredServers[i];
                var item = Instantiate(view.serverListItemTemplate, view.serverListContent);
                item.gameObject.SetActive(true);
                item.Configure(server.Ip, server.Port, Time.time - server.LastSeen, HandleServerSelected);
                _serverItems.Add(item);
            }

            if (view.emptyServerMessage != null)
                view.emptyServerMessage.SetActive(_discoveredServers.Count == 0);

            _serverListDirty = false;
        }

        private void SyncAddressFieldsFromManager()
        {
            if (view == null || manager == null)
                return;

            if (view.ipInput != null)
                view.ipInput.text = manager.serverAddress;
            if (view.portInput != null)
                view.portInput.text = manager.serverPort.ToString();
        }
    }
}
