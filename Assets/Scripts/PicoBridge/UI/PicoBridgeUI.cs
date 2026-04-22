using System.Collections.Generic;
using UnityEngine;

namespace PicoBridge.UI
{
    /// <summary>
    /// Minimal IMGUI for connection control and status.
    /// No Canvas/prefab dependencies — works immediately.
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
        private readonly List<DiscoveredServer> _discoveredServers = new List<DiscoveredServer>();

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
                    _manager.Discovery.OnServerFound += (ip, port) =>
                    {
                        for (int i = 0; i < _discoveredServers.Count; i++)
                        {
                            if (_discoveredServers[i].Ip == ip && _discoveredServers[i].Port == port)
                                return;
                        }

                        _discoveredServers.Add(new DiscoveredServer { Ip = ip, Port = port });
                    };
            }
        }

        private void Update()
        {
            if (_manager == null) return;

            if (_manager.IsConnected)
                _statusText = $"Connected to {_manager.serverAddress}:{_manager.serverPort}";
            else if (_manager.TcpClient != null)
                _statusText = _manager.TcpClient.State.ToString();
            else
                _statusText = "Disconnected";
        }

        private void OnGUI()
        {
            if (!_showUI || _manager == null) return;

            float scale = Screen.dpi > 0 ? Screen.dpi / 160f : 1f;
            int w = (int)(400 * scale);
            int h = (int)(380 * scale);
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

            GUILayout.EndArea();
        }
    }
}
