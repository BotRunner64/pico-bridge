using System.Net;
using System.Net.Sockets;

namespace PicoBridge.Network
{
    public static class NetUtils
    {
        /// <summary>
        /// Returns the first non-loopback IPv4 address, or "127.0.0.1" as fallback.
        /// </summary>
        public static string GetLocalIPv4()
        {
            Socket socket = null;
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
                socket.Connect("8.8.8.8", 65530);
                if (socket.LocalEndPoint is IPEndPoint endPoint)
                    return endPoint.Address.ToString();
            }
            catch { }
            finally
            {
                try { socket?.Close(); } catch { }
            }
            return "127.0.0.1";
        }

        public static int GetAvailableTcpPort()
        {
            TcpListener listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Any, 0);
                listener.Start();
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            catch
            {
                return 19000;
            }
            finally
            {
                try { listener?.Stop(); } catch { }
            }
        }
    }
}
