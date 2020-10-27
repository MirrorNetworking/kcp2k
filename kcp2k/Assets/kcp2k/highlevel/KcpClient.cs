// kcp client logic abstracted into a class.
// for use in Mirror, DOTSNET, testing, etc.
using System;
using UnityEngine;

namespace kcp2k
{
    public class KcpClient
    {
        // events
        public event Action OnConnected;
        public event Action<ArraySegment<byte>> OnData;
        public event Action OnDisconnected;

        // state
        public KcpClientConnection connection;
        public bool connected;

        public KcpClient(Action OnConnected, Action<ArraySegment<byte>> OnData, Action OnDisconnected)
        {
            this.OnConnected = OnConnected;
            this.OnData = OnData;
            this.OnDisconnected = OnDisconnected;
        }

        public void Connect(string address, ushort port, bool noDelay, uint interval)
        {
            if (connected)
            {
                Debug.LogWarning("KCP: client already connected!");
                return;
            }

            connection = new KcpClientConnection();

            // setup events
            connection.OnConnected += () =>
            {
                Debug.Log($"KCP: OnClientConnected");
                connected = true;
                OnConnected.Invoke();
            };
            connection.OnData += (message) =>
            {
                //Debug.Log($"KCP: OnClientData({BitConverter.ToString(message.Array, message.Offset, message.Count)})");
                OnData.Invoke(message);
            };
            connection.OnDisconnected += () =>
            {
                Debug.Log($"KCP: OnClientDisconnected");
                connected = false;
                connection = null;
                OnDisconnected.Invoke();
            };

            // connect
            connection.Connect(address, port, noDelay, interval);
        }

        public void Send(ArraySegment<byte> segment)
        {
            if (connected)
            {
                connection.Send(segment);
            }
            else Debug.LogWarning("KCP: can't send because client not connected!");
        }

        public void Disconnect()
        {
            // only if connected
            // otherwise we end up in a deadlock because of an open Mirror bug:
            // https://github.com/vis2k/Mirror/issues/2353
            if (connected)
            {
                connection?.Disconnect();
                connection = null;
            }
        }

        public void Tick()
        {
            // tick client connection
            if (connection != null)
            {
                connection.Tick();
                // recv on socket
                connection.RawReceive();
                // recv on kcp
                connection.Receive();
            }
        }
    }
}
