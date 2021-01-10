using System;
using UnityEngine;

namespace kcp2k.Examples
{
    public class TestClient : MonoBehaviour
    {
        // configuration
        public ushort Port = 7777;

        // client
        public KcpClient client = new KcpClient(
            () => {},
            (message) => Debug.Log($"KCP: OnClientDataReceived({BitConverter.ToString(message.Array, message.Offset, message.Count)})"),
            () => {}
        );

        // MonoBehaviour ///////////////////////////////////////////////////////
        void Awake()
        {
            // logging
            Log.Info = Debug.Log;
            Log.Warning = Debug.LogWarning;
            Log.Error = Debug.LogError;
        }

        public void LateUpdate() => client.Tick();

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(5, 5, 150, 400));
            GUILayout.Label("Client:");
            if (GUILayout.Button("Connect 127.0.0.1"))
            {
                client.Connect("127.0.0.1", Port, true, 10);
            }
            if (GUILayout.Button("Send 0x01, 0x02 reliable"))
            {
                client.Send(new ArraySegment<byte>(new byte[]{0x01, 0x02}), KcpChannel.Reliable);
            }
            if (GUILayout.Button("Send 0x03, 0x04 unreliable"))
            {
                client.Send(new ArraySegment<byte>(new byte[]{0x03, 0x04}), KcpChannel.Unreliable);
            }
            if (GUILayout.Button("Disconnect"))
            {
                client.Disconnect();
            }
            GUILayout.EndArea();
        }
    }
}