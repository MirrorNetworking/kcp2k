// kcp server logic abstracted into a class.
// for use in Mirror, DOTSNET, testing, etc.
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace kcp2k
{
    public class KcpServer
    {
        // events
        public event Action<int> OnConnected;
        public event Action<int, ArraySegment<byte>> OnData;
        public event Action<int> OnDisconnected;

        // configuration
        // NoDelay is recommended to reduce latency. This also scales better
        // without buffers getting full.
        public bool NoDelay = true;
        // KCP internal update interval. 100ms is KCP default, but a lower
        // interval is recommended to minimize latency and to scale to more
        // networked entities.
        public uint Interval = 10;

        // state
        Socket socket;
        EndPoint newClientEP = new IPEndPoint(IPAddress.IPv6Any, 0);
        readonly byte[] buffer = new byte[Kcp.MTU_DEF];

        // connections <connectionId, connection> where connectionId is EndPoint.GetHashCode
        public Dictionary<int, KcpServerConnection> connections = new Dictionary<int, KcpServerConnection>();

        public KcpServer(Action<int> OnConnected,
                         Action<int, ArraySegment<byte>> OnData,
                         Action<int> OnDisconnected,
                         bool NoDelay,
                         uint Interval)
        {
            this.OnConnected = OnConnected;
            this.OnData = OnData;
            this.OnDisconnected = OnDisconnected;
            this.NoDelay = NoDelay;
            this.Interval = Interval;
        }

        public bool IsActive() => socket != null;

        public void Start(ushort port)
        {
            // only start once
            if (socket != null)
            {
                Debug.LogWarning("KCP: server already started!");
            }

            // listen
            socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
            socket.DualMode = true;
            socket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
        }

        public void Send(int connectionId, ArraySegment<byte> segment)
        {
            if (connections.TryGetValue(connectionId, out KcpServerConnection connection))
            {
                connection.Send(segment);
            }
        }
        public void Disconnect(int connectionId)
        {
            if (connections.TryGetValue(connectionId, out KcpServerConnection connection))
            {
                connection.Disconnect();
            }
        }

        public string GetClientAddress(int connectionId)
        {
            if (connections.TryGetValue(connectionId, out KcpServerConnection connection))
            {
                return (connection.GetRemoteEndPoint() as IPEndPoint).Address.ToString();
            }
            return "";
        }

        HashSet<int> connectionsToRemove = new HashSet<int>();
        public void Tick()
        {
            while (socket != null && socket.Poll(0, SelectMode.SelectRead))
            {
                int msgLength = socket.ReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref newClientEP);
                //Debug.Log($"KCP: server raw recv {msgLength} bytes = {BitConverter.ToString(buffer, 0, msgLength)}");

                // calculate connectionId from endpoint
                int connectionId = newClientEP.GetHashCode();

                // is this a new connection?
                if (!connections.TryGetValue(connectionId, out KcpServerConnection connection))
                {
                    // create a new KcpConnection
                    connection = new KcpServerConnection(socket, newClientEP, NoDelay, Interval);

                    // DO NOT add to connections yet. only if the first message
                    // is actually the kcp handshake. otherwise it's either:
                    // * random data from the internet
                    // * or from a client connection that we just disconnected
                    //   but that hasn't realized it yet, still sending data
                    //   from last session that we should absolutely ignore.
                    //
                    //
                    // TODO this allocates a new KcpConnection for each new
                    // internet connection. not ideal, but C# UDP Receive
                    // already allocated anyway.
                    //
                    // expecting a MAGIC byte[] would work, but sending the raw
                    // UDP message without kcp's reliability will have low
                    // probability of being received.
                    //
                    // for now, this is fine.

                    // setup connected event that also adds to connections
                    connection.OnConnected += () =>
                    {
                        // OnConnected is only called after handshake.
                        // that's when we add to connections, not earlier.
                        connections.Add(connectionId, connection);
                        Debug.Log($"KCP: server added connection {newClientEP}");

                        // setup Data + Disconnected events only AFTER the
                        // handshake. we don't want to fire OnServerDisconnected
                        // every time we receive invalid random data from the
                        // internet.

                        // setup data event
                        connection.OnData += (message) =>
                        {
                            // call mirror event
                            //Debug.Log($"KCP: OnServerDataReceived({connectionId}, {BitConverter.ToString(message.Array, message.Offset, message.Count)})");
                            OnData.Invoke(connectionId, message);
                        };

                        // setup disconnected event
                        connection.OnDisconnected += () =>
                        {
                            // flag for removal
                            // (can't remove directly because connection is updated
                            //  and event is called while iterating all connections)
                            connectionsToRemove.Add(connectionId);

                            // call mirror event
                            Debug.Log($"KCP: OnServerDisconnected({connectionId})");
                            OnDisconnected.Invoke(connectionId);
                        };

                        // finally, call mirror OnConnected event
                        Debug.Log($"KCP: OnServerConnected({connectionId})");
                        OnConnected.Invoke(connectionId);
                    };

                    // now input the message & tick
                    // connected event was set up.
                    // tick will process the first message and adds the
                    // connection if it was the handshake.
                    connection.RawInput(buffer, msgLength);
                    connection.Tick();

                    // again, do not add to connections.
                    // if the first message wasn't the kcp handshake then
                    // connection will simply be garbage collected.
                }
                // existing connection: simply input the message into kcp
                else
                {
                    connection.RawInput(buffer, msgLength);
                }
            }

            // tick all server connections
            foreach (KcpServerConnection connection in connections.Values)
            {
                connection.Tick();
            }

            // remove disconnected connections
            // (can't do it in connection.OnDisconnected because Tick is called
            //  while iterating connections)
            foreach (int connectionId in connectionsToRemove)
            {
                connections.Remove(connectionId);
            }
            connectionsToRemove.Clear();
        }

        public void Stop()
        {
            socket?.Close();
            socket = null;
        }
    }
}
