#if MIRROR
using System;
using System.Linq;
using UnityEngine;
using kcp2k;

namespace Mirror.KCP
{
    public class KcpTransport : Transport
    {
        // common
        [Header("Transport Configuration")]
        public ushort Port = 7777;
        [Tooltip("NoDelay is recommended to reduce latency. This also scales better without buffers getting full.")]
        public bool NoDelay = true;
        [Tooltip("KCP internal update interval. 100ms is KCP default, but a lower interval is recommended to minimize latency and to scale to more networked entities.")]
        public uint Interval = 10;
        [Header("Advanced")]
        [Tooltip("KCP window size can be modified to support higher loads. For example, Mirror Benchmark requires 128 for 4k monsters, 512 for 10k monsters")]
        public uint SendWindowSize = 128; //Kcp.WND_SND; 32 by default. 128 is better for 4k Benchmark etc.
        [Tooltip("KCP window size can be modified to support higher loads. For example, Mirror Benchmark requires 128 for 4k monsters, 512 for 10k monsters")]
        public uint ReceiveWindowSize = Kcp.WND_RCV;

        // server & client
        KcpServer server;
        KcpClient client;

        // debugging
        [Header("Debug")]
        public bool debugGUI;

        void Awake()
        {
            // TODO simplify after converting Mirror Transport events to Action
            client = new KcpClient(
                () => OnClientConnected.Invoke(),
                (message) => OnClientDataReceived.Invoke(message),
                () => OnClientDisconnected.Invoke()
            );
            // TODO simplify after converting Mirror Transport events to Action
            server = new KcpServer(
                (connectionId) => OnServerConnected.Invoke(connectionId),
                (connectionId, message) => OnServerDataReceived.Invoke(connectionId, message),
                (connectionId) => OnServerDisconnected.Invoke(connectionId),
                NoDelay,
                Interval,
                SendWindowSize,
                ReceiveWindowSize
            );
            Debug.Log("KcpTransport initialized!");
        }

        // all except WebGL
        public override bool Available() =>
            Application.platform != RuntimePlatform.WebGLPlayer;

        // client
        public override bool ClientConnected() => client.connected;
        public override void ClientConnect(string address)
        {
            client.Connect(address, Port, NoDelay, Interval, SendWindowSize, ReceiveWindowSize);
        }
        public override bool ClientSend(int channelId, ArraySegment<byte> segment)
        {
            client.Send(segment);
            return true;
        }
        public override void ClientDisconnect() => client.Disconnect();

        // IMPORTANT: set script execution order to >1000 to call Transport's
        //            LateUpdate after all others. Fixes race condition where
        //            e.g. in uSurvival Transport would apply Cmds before
        //            ShoulderRotation.LateUpdate, resulting in projectile
        //            spawns at the point before shoulder rotation.
        public void LateUpdate()
        {
            // note: we need to check enabled in case we set it to false
            // when LateUpdate already started.
            // (https://github.com/vis2k/Mirror/pull/379)
            if (!enabled)
                return;

            server.Tick();
            client.Tick();
        }

        // server
        public override bool ServerActive() => server.IsActive();
        public override void ServerStart() => server.Start(Port);
        public override bool ServerSend(int connectionId, int channelId, ArraySegment<byte> segment)
        {
            server.Send(connectionId, segment);
            return true;
        }
        public override bool ServerDisconnect(int connectionId)
        {
            server.Disconnect(connectionId);
            return true;
        }
        public override string ServerGetClientAddress(int connectionId) => server.GetClientAddress(connectionId);
        public override void ServerStop() => server.Stop();

        // common
        public override void Shutdown() {}

        // MTU
        public override ushort GetMaxPacketSize() => Kcp.MTU_DEF;

        public override string ToString()
        {
            return "KCP";
        }

        int GetTotalSendQueue() =>
            server.connections.Values.Sum(conn => conn.kcp.snd_queue.Count);
        int GetTotalReceiveQueue() =>
            server.connections.Values.Sum(conn => conn.kcp.rcv_queue.Count);
        int GetTotalSendBuffer() =>
            server.connections.Values.Sum(conn => conn.kcp.snd_buf.Count);
        int GetTotalReceiveBuffer() =>
            server.connections.Values.Sum(conn => conn.kcp.rcv_buf.Count);

        void OnGUI()
        {
            if (!debugGUI) return;

            GUILayout.BeginArea(new Rect(5, 100, 300, 300));

            if (ServerActive())
            {
                GUILayout.BeginVertical("Box");
                GUILayout.Label("SERVER");
                GUILayout.Label("  connections: " + server.connections.Count);
                GUILayout.Label("  SendQueue: " + GetTotalSendQueue());
                GUILayout.Label("  ReceiveQueue: " + GetTotalReceiveQueue());
                GUILayout.Label("  SendBuffer: " + GetTotalSendBuffer());
                GUILayout.Label("  ReceiveBuffer: " + GetTotalReceiveBuffer());
                GUILayout.EndVertical();
            }

            if (ClientConnected())
            {
                GUILayout.BeginVertical("Box");
                GUILayout.Label("CLIENT");
                GUILayout.Label("  SendQueue: " + client.connection.kcp.snd_queue.Count);
                GUILayout.Label("  ReceiveQueue: " + client.connection.kcp.rcv_queue.Count);
                GUILayout.Label("  SendBuffer: " + client.connection.kcp.snd_buf.Count);
                GUILayout.Label("  ReceiveBuffer: " + client.connection.kcp.rcv_buf.Count);
                GUILayout.EndVertical();
            }

            GUILayout.EndArea();
        }
    }
}
#endif
