using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using PicoBridge.Network;
using UnityEngine;

namespace PicoBridge.Network
{
    /// <summary>
    /// Listens for UDP broadcast from PC server (CMD 0x7E on port 29888).
    /// Mirrors XRobo UIUdpReceiver discovery mechanism.
    /// </summary>
    public class UdpDiscovery : MonoBehaviour
    {
        public const int DISCOVERY_PORT = 29888;

        public event Action<string, int> OnServerFound;

        private UdpClient _udpClient;
        private Thread _listenThread;
        private volatile bool _listening;
        private readonly Queue<(string ip, int port)> _discoveredServers = new Queue<(string ip, int port)>();
        private readonly HashSet<string> _knownServers = new HashSet<string>();

        public IReadOnlyCollection<string> KnownServers => _knownServers;

        public void StartListening()
        {
            if (_listening) return;

            try
            {
                _udpClient = new UdpClient(DISCOVERY_PORT);
                _listening = true;
                _listenThread = new Thread(ListenLoop) { IsBackground = true };
                _listenThread.Start();
                Debug.Log($"[PicoBridge] UDP discovery listening on port {DISCOVERY_PORT}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[PicoBridge] UDP listen failed: {e.Message}");
            }
        }

        public void StopListening()
        {
            _listening = false;
            _udpClient?.Close();
        }

        public void ClearDiscovered()
        {
            _knownServers.Clear();
        }

        private void Update()
        {
            lock (_discoveredServers)
            {
                while (_discoveredServers.Count > 0)
                {
                    var server = _discoveredServers.Dequeue();
                    string key = $"{server.ip}:{server.port}";
                    if (_knownServers.Add(key))
                    {
                        Debug.Log($"[PicoBridge] Discovered server: {key}");
                        OnServerFound?.Invoke(server.ip, server.port);
                    }
                }
            }
        }

        private void OnDestroy()
        {
            StopListening();
        }

        private void ListenLoop()
        {
            var endPoint = new IPEndPoint(IPAddress.Any, DISCOVERY_PORT);
            var buffer = new ByteBuffer(1024);

            while (_listening)
            {
                try
                {
                    byte[] data = _udpClient.Receive(ref endPoint);
                    // Parse using our protocol — expect CMD 0x7E with IP as payload
                    buffer.Clear();
                    buffer.WriteBytes(data, 0, data.Length);
                    var pkt = PackageHandle.Unpack(buffer);
                    if (pkt != null && pkt.Cmd == NetCMD.PACKET_CMD_TCPIP && pkt.Data.Length > 0)
                    {
                        if (!TryParseDiscoveryPayload(pkt.Data, out var ip, out var port))
                            continue;

                        lock (_discoveredServers)
                        {
                            _discoveredServers.Enqueue((ip, port));
                        }
                    }
                }
                catch (SocketException)
                {
                    if (_listening) break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }

        private static bool TryParseDiscoveryPayload(byte[] payload, out string ip, out int port)
        {
            string text = System.Text.Encoding.UTF8.GetString(payload).Trim();
            if (string.IsNullOrEmpty(text))
            {
                ip = null;
                port = NetCMD.DEFAULT_TCP_PORT;
                return false;
            }

            string[] parts = text.Split('|');
            ip = parts[0].Trim();
            port = NetCMD.DEFAULT_TCP_PORT;

            if (parts.Length > 1 && !int.TryParse(parts[1], out port))
                return false;

            return !string.IsNullOrEmpty(ip);
        }
    }
}
