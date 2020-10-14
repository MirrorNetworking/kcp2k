using System;
using UnityEngine;

namespace kcp2k.Examples
{
    public class TestServer : MonoBehaviour
    {
        // configuration
        public ushort Port = 7777;

        public void StartServer()
        {
        }

        public void Send(int connectionId, ArraySegment<byte> segment)
        {
        }

        public bool Disconnect(int connectionId)
        {
            return false;
        }

        public string GetAddress(int connectionId)
        {
            return "";
        }

        public void StopServer()
        {
        }

        // MonoBehaviour ///////////////////////////////////////////////////////
        public void LateUpdate()
        {
        }

        void OnGUI()
        {
            //int firstclient = connections.Count > 0 ? connections.First().Key : -1;

            GUILayout.BeginArea(new Rect(160, 5, 250, 400));
            GUILayout.Label("Server:");
            if (GUILayout.Button("Start"))
            {
                StartServer();
            }
            /*if (GUILayout.Button("Send 0x01, 0x02 to " + firstclient))
            {
                Send(firstclient, new ArraySegment<byte>(new byte[]{0x01, 0x02}));
            }
            if (GUILayout.Button("Disconnect connection " + firstclient))
            {
                Disconnect(firstclient);
            }*/
            if (GUILayout.Button("Stop"))
            {
                StopServer();
            }
            GUILayout.EndArea();
        }
    }
}
