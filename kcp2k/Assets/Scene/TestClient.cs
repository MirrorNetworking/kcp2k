using System;
using UnityEngine;

namespace kcp2k.Examples
{
    public class TestClient : MonoBehaviour
    {
        // configuration
        public ushort Port = 7777;

        public void Connect(string ip)
        {
        }

        public void Send(ArraySegment<byte> segment)
        {
        }

        public void Disconnect()
        {
        }

        // MonoBehaviour ///////////////////////////////////////////////////////
        public void LateUpdate()
        {
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