using System;
using UnityEngine;

namespace kcp2k.Examples
{
    public class TestClient : MonoBehaviour
    {
        // configuration
        public ushort Port = 7777;
        [Tooltip("NoDelay is recommended to reduce latency. This also scales better without buffers getting full.")]
        public bool NoDelay = true;
        [Tooltip("KCP internal update interval. 100ms is KCP default, but a lower interval is recommended to minimize latency.")]
        public uint Interval = 40;

        // client
        readonly byte[] buffer = new byte[Kcp.MTU_DEF];
        KcpClientConnection clientConnection;
        bool connected;

        public bool Connected() => connected;

        public void Connect(string address)
        {
            if (connected)
            {
                Debug.LogWarning("KCP: client already connected!");
                return;
            }

            clientConnection = new KcpClientConnection();
            // setup events
            clientConnection.OnConnected += () =>
            {
                Debug.Log($"KCP: OnClientConnected");
                connected = true;
            };
            clientConnection.OnData += (message) =>
            {
                Debug.Log($"KCP: OnClientDataReceived({BitConverter.ToString(message.Array, message.Offset, message.Count)})");
            };
            clientConnection.OnDisconnected += () =>
            {
                Debug.Log($"KCP: OnClientDisconnected");
                connected = false;
            };

            // connect
            clientConnection.Connect(address, Port, NoDelay, Interval);
        }

        public void Send(ArraySegment<byte> segment)
        {
            if (clientConnection != null)
            {
                clientConnection.Send(segment);
            }
        }

        public void Disconnect()
        {
            clientConnection?.Disconnect();
            clientConnection = null;
        }

        // MonoBehaviour ///////////////////////////////////////////////////////
        void UpdateClient()
        {
            // tick client connection
            if (clientConnection != null)
            {
                clientConnection.Tick();
                // recv on socket
                clientConnection.RawReceive();
                // recv on kcp
                clientConnection.Receive();
            }
        }

        public void LateUpdate()
        {
            UpdateClient();
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(5, 5, 150, 400));
            GUILayout.Label("Client:");
            if (GUILayout.Button("Connect 127.0.0.1"))
            {
                Connect("127.0.0.1");
            }
            if (GUILayout.Button("Send 0x01, 0x02"))
            {
                Send(new ArraySegment<byte>(new byte[]{0x01, 0x02}));
            }
            if (GUILayout.Button("Disconnect"))
            {
                Disconnect();
            }
            GUILayout.EndArea();
        }
    }
}