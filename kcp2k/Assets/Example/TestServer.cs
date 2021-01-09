using System;
using System.Linq;
using UnityEngine;

namespace kcp2k.Examples
{
    public class TestServer : MonoBehaviour
    {
        // configuration
        public ushort Port = 7777;

        // server
        public KcpServer server = new KcpServer(
            (connectionId) => {},
            (connectionId, message) => Debug.Log($"KCP: OnServerDataReceived({connectionId}, {BitConverter.ToString(message.Array, message.Offset, message.Count)})"),
            (connectionId) => {},
            true,
            10
        );

        // MonoBehaviour ///////////////////////////////////////////////////////
        void Awake()
        {
            // logging
            Log.Info = Debug.Log;
            Log.Warning = Debug.LogWarning;
            Log.Error = Debug.LogError;
        }

        public void LateUpdate() => server.Tick();

        void OnGUI()
        {
            int firstclient = server.connections.Count > 0 ? server.connections.First().Key : -1;

            GUILayout.BeginArea(new Rect(160, 5, 250, 400));
            GUILayout.Label("Server:");
            if (GUILayout.Button("Start"))
            {
                server.Start(Port);
            }
            if (GUILayout.Button("Send 0x01, 0x02 to " + firstclient))
            {
                server.Send(firstclient, new ArraySegment<byte>(new byte[]{0x01, 0x02}), KcpChannel.Reliable);
            }
            if (GUILayout.Button("Send 0x03, 0x04 to " + firstclient + " unreliable"))
            {
                server.Send(firstclient, new ArraySegment<byte>(new byte[]{0x03, 0x04}), KcpChannel.Unreliable);
            }
            if (GUILayout.Button("Disconnect connection " + firstclient))
            {
                server.Disconnect(firstclient);
            }
            if (GUILayout.Button("Stop"))
            {
                server.Stop();
            }
            GUILayout.EndArea();
        }
    }
}
