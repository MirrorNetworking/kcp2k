// kcp server logic abstracted into a class.
// for use in Mirror, DOTSNET, testing, etc.
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace kcp2k
{
    public class KcpServer
    {
        // we need to subtract the channel and cookie bytes from every
        // MaxMessageSize calculation.
        // we also need to tell kcp to use MTU-1 to leave space for the byte.
        public const int CHANNEL_HEADER_SIZE = 1;
        public const int COOKIE_HEADER_SIZE = 4;
        public const int METADATA_SIZE_RELIABLE = CHANNEL_HEADER_SIZE + COOKIE_HEADER_SIZE;
        public const int METADATA_SIZE_UNRELIABLE = CHANNEL_HEADER_SIZE + COOKIE_HEADER_SIZE;

        // reliable channel (= kcp) MaxMessageSize so the outside knows largest
        // allowed message to send. the calculation in Send() is not obvious at
        // all, so let's provide the helper here.
        //
        // kcp does fragmentation, so max message is way larger than MTU.
        //
        // -> runtime MTU changes are disabled: mss is always MTU_DEF-OVERHEAD
        // -> Send() checks if fragment count < rcv_wnd, so we use rcv_wnd - 1.
        //    NOTE that original kcp has a bug where WND_RCV default is used
        //    instead of configured rcv_wnd, limiting max message size to 144 KB
        //    https://github.com/skywind3000/kcp/pull/291
        //    we fixed this in kcp2k.
        // -> we add 1 byte KcpHeader enum to each message, so -1
        //
        // IMPORTANT: max message is MTU * rcv_wnd, in other words it completely
        //            fills the receive window! due to head of line blocking,
        //            all other messages have to wait while a maxed size message
        //            is being delivered.
        //            => in other words, DO NOT use max size all the time like
        //               for batching.
        //            => sending UNRELIABLE max message size most of the time is
        //               best for performance (use that one for batching!)
        static int ReliableMaxMessageSize_Unconstrained(int mtu, uint rcv_wnd) =>
            (mtu - Kcp.OVERHEAD - METADATA_SIZE_RELIABLE) * ((int)rcv_wnd - 1) - 1;

        // kcp encodes 'frg' as 1 byte.
        // max message size can only ever allow up to 255 fragments.
        //   WND_RCV gives 127 fragments.
        //   WND_RCV * 2 gives 255 fragments.
        // so we can limit max message size by limiting rcv_wnd parameter.
        public static int ReliableMaxMessageSize(int mtu, uint rcv_wnd) =>
            ReliableMaxMessageSize_Unconstrained(mtu, Math.Min(rcv_wnd, Kcp.FRG_MAX));

        // unreliable max message size is simply MTU - channel header - kcp header
        public static int UnreliableMaxMessageSize(int mtu) =>
            mtu - METADATA_SIZE_UNRELIABLE - 1;
        
        // buffer to receive kcp's processed messages (avoids allocations).
        // IMPORTANT: this is for KCP messages. so it needs to be of size:
        //            1 byte header + MaxMessageSize content
        readonly byte[] kcpMessageBuffer;// = new byte[1 + ReliableMaxMessageSize];

        // send buffer for handing user messages to kcp for processing.
        // (avoids allocations).
        // IMPORTANT: needs to be of size:
        //            1 byte header + MaxMessageSize content
        readonly byte[] kcpSendBuffer;// = new byte[1 + ReliableMaxMessageSize];

        // raw send buffer is exactly MTU.
        readonly byte[] rawSendBuffer;
        
        // callbacks
        // even for errors, to allow liraries to show popups etc.
        // instead of logging directly.
        // (string instead of Exception for ease of use and to avoid user panic)
        //
        // events are readonly, set in constructor.
        // this ensures they are always initialized when used.
        // fixes https://github.com/MirrorNetworking/Mirror/issues/3337 and more
        protected readonly Action<int> OnConnected;
        protected readonly Action<int, ArraySegment<byte>, KcpChannel> OnData;
        protected readonly Action<int> OnDisconnected;
        protected readonly Action<int, ErrorCode, string> OnError;

        // configuration
        protected readonly KcpConfig config;

        // state
        protected Socket socket;
        EndPoint newClientEP;

        // expose local endpoint for users / relays / nat traversal etc.
        public EndPoint LocalEndPoint => socket?.LocalEndPoint;

        // raw receive buffer always needs to be of 'MTU' size, even if
        // MaxMessageSize is larger. kcp always sends in MTU segments and having
        // a buffer smaller than MTU would silently drop excess data.
        // => we need the mtu to fit channel + message!
        protected readonly byte[] rawReceiveBuffer;

        // connections <connectionId, connection> where connectionId is EndPoint.GetHashCode
        public Dictionary<int, KcpServerConnection> connections =
            new Dictionary<int, KcpServerConnection>();

        public KcpServer(Action<int> OnConnected,
                         Action<int, ArraySegment<byte>, KcpChannel> OnData,
                         Action<int> OnDisconnected,
                         Action<int, ErrorCode, string> OnError,
                         KcpConfig config)
        {
            // initialize callbacks first to ensure they can be used safely.
            this.OnConnected = OnConnected;
            this.OnData = OnData;
            this.OnDisconnected = OnDisconnected;
            this.OnError = OnError;
            this.config = config;

            // create mtu sized receive buffer
            rawReceiveBuffer = new byte[config.Mtu];

            // create newClientEP either IPv4 or IPv6
            newClientEP = config.DualMode
                          ? new IPEndPoint(IPAddress.IPv6Any, 0)
                          : new IPEndPoint(IPAddress.Any,     0);
            
            // create mtu sized send buffer
            rawSendBuffer = new byte[config.Mtu];

            // calculate max message sizes once
            // unreliableMax = UnreliableMaxMessageSize(config.Mtu);
            var reliableMax = ReliableMaxMessageSize(config.Mtu, config.ReceiveWindowSize);

            // create message buffers AFTER window size is set
            // see comments on buffer definition for the "+1" part
            kcpMessageBuffer = new byte[1 + reliableMax];
            kcpSendBuffer    = new byte[1 + reliableMax];
        }

        public virtual bool IsActive() => socket != null;

        static Socket CreateServerSocket(bool DualMode, ushort port)
        {
            if (DualMode)
            {
                // IPv6 socket with DualMode @ "::" : port
                Socket socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);

                // enabling DualMode may throw:
                // https://learn.microsoft.com/en-us/dotnet/api/System.Net.Sockets.Socket.DualMode?view=net-7.0
                // attempt it, otherwise log but continue
                // fixes: https://github.com/MirrorNetworking/Mirror/issues/3358
                try
                {
                    socket.DualMode = true;
                }
                catch (NotSupportedException e)
                {
                    Log.Warning($"[KCP] Failed to set Dual Mode, continuing with IPv6 without Dual Mode. Error: {e}");
                }

                // for windows sockets, there's a rare issue where when using
                // a server socket with multiple clients, if one of the clients
                // is closed, the single server socket throws exceptions when
                // sending/receiving. even if the socket is made for N clients.
                //
                // this actually happened to one of our users:
                // https://github.com/MirrorNetworking/Mirror/issues/3611
                //
                // here's the in-depth explanation & solution:
                //
                // "As you may be aware, if a host receives a packet for a UDP
                // port that is not currently bound, it may send back an ICMP
                // "Port Unreachable" message. Whether or not it does this is
                // dependent on the firewall, private/public settings, etc.
                // On localhost, however, it will pretty much always send this
                // packet back.
                //
                // Now, on Windows (and only on Windows), by default, a received
                // ICMP Port Unreachable message will close the UDP socket that
                // sent it; hence, the next time you try to receive on the
                // socket, it will throw an exception because the socket has
                // been closed by the OS.
                //
                // Obviously, this causes a headache in the multi-client,
                // single-server socket set-up you have here, but luckily there
                // is a fix:
                //
                // You need to utilise the not-often-required SIO_UDP_CONNRESET
                // Winsock control code, which turns off this built-in behaviour
                // of automatically closing the socket.
                //
                // Note that this ioctl code is only supported on Windows
                // (XP and later), not on Linux, since it is provided by the
                // Winsock extensions. Of course, since the described behavior
                // is only the default behavior on Windows, this omission is not
                // a major loss. If you are attempting to create a
                // cross-platform library, you should cordon this off as
                // Windows-specific code."
                // https://stackoverflow.com/questions/74327225/why-does-sending-via-a-udpclient-cause-subsequent-receiving-to-fail
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    const uint IOC_IN = 0x80000000U;
                    const uint IOC_VENDOR = 0x18000000U;
                    const int SIO_UDP_CONNRESET = unchecked((int)(IOC_IN | IOC_VENDOR | 12));
                    socket.IOControl(SIO_UDP_CONNRESET, new byte[] { 0x00 }, null);
                }

                socket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
                return socket;
            }
            else
            {
                // IPv4 socket @ "0.0.0.0" : port
                Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Bind(new IPEndPoint(IPAddress.Any, port));
                return socket;
            }
        }

        public virtual void Start(ushort port)
        {
            // only start once
            if (socket != null)
            {
                Log.Warning("[KCP] Server: already started!");
                return;
            }

            // listen
            socket = CreateServerSocket(config.DualMode, port);

            // recv & send are called from main thread.
            // need to ensure this never blocks.
            // even a 1ms block per connection would stop us from scaling.
            socket.Blocking = false;

            // configure buffer sizes
            Common.ConfigureSocketBuffers(socket, config.RecvBufferSize, config.SendBufferSize);
        }

        public void Send(int connectionId, ArraySegment<byte> segment, KcpChannel channel)
        {
            if (connections.TryGetValue(connectionId, out KcpServerConnection connection))
            {
                connection.SendData(segment, channel);
            }
        }

        public void Disconnect(int connectionId)
        {
            if (connections.TryGetValue(connectionId, out KcpServerConnection connection))
            {
                connection.Disconnect();
            }
        }

        // expose the whole IPEndPoint, not just the IP address. some need it.
        public IPEndPoint GetClientEndPoint(int connectionId)
        {
            if (connections.TryGetValue(connectionId, out KcpServerConnection connection))
            {
                return connection.remoteEndPoint as IPEndPoint;
            }
            return null;
        }

        // io - input.
        // virtual so it may be modified for relays, nonalloc workaround, etc.
        // https://github.com/vis2k/where-allocation
        // bool return because not all receives may be valid.
        // for example, relay may expect a certain header.
        protected virtual bool RawReceiveFrom(out ArraySegment<byte> segment, out int connectionId)
        {
            segment = default;
            connectionId = 0;
            if (socket == null) return false;

            try
            {
                if (socket.ReceiveFromNonBlocking(rawReceiveBuffer, out segment, ref newClientEP))
                {
                    // set connectionId to hash from endpoint
                    connectionId = Common.ConnectionHash(newClientEP);
                    return true;
                }
            }
            catch (SocketException e)
            {
                // NOTE: SocketException is not a subclass of IOException.
                // the other end closing the connection is not an 'error'.
                // but connections should never just end silently.
                // at least log a message for easier debugging.
                Log.Info($"[KCP] Server: ReceiveFrom failed: {e}");
            }

            return false;
        }

        // io - out.
        // virtual so it may be modified for relays, nonalloc workaround, etc.
        // relays may need to prefix connId (and remoteEndPoint would be same for all)
        protected virtual void RawSend(int connectionId, ArraySegment<byte> data)
        {
            // get the connection's endpoint
            if (!connections.TryGetValue(connectionId, out KcpServerConnection connection))
            {
                Log.Warning($"[KCP] Server: RawSend invalid connectionId={connectionId}");
                return;
            }

            try
            {
                socket.SendToNonBlocking(data, connection.remoteEndPoint);
            }
            catch (SocketException e)
            {
                Log.Error($"[KCP] Server: SendTo failed: {e}");
            }
        }

        protected virtual KcpServerConnection CreateConnection(int connectionId)
        {
            // generate a random cookie for this connection to avoid UDP spoofing.
            // needs to be random, but without allocations to avoid GC.
            uint cookie = Common.GenerateCookie();

            // create empty connection without peer first.
            // we need it to set up peer callbacks.
            // afterwards we assign the peer.
            // events need to be wrapped with connectionIds
            KcpServerConnection connection = new KcpServerConnection(
                OnConnectedCallback,
                (message,  channel) => OnData(connectionId, message, channel),
                OnDisconnectedCallback,
                (error, reason) => OnError(connectionId, error, reason),
                (data) => RawSend(connectionId, data),
                config,
                cookie,
                newClientEP, 
                rawSendBuffer, 
                kcpMessageBuffer, 
                kcpSendBuffer);

            return connection;

            // setup authenticated event that also adds to connections
            void OnConnectedCallback(KcpServerConnection conn)
            {
                // add to connections dict after being authenticated.
                connections.Add(connectionId, conn);
                Log.Info($"[KCP] Server: added connection({connectionId})");

                // setup Data + Disconnected events only AFTER the
                // handshake. we don't want to fire OnServerDisconnected
                // every time we receive invalid random data from the
                // internet.

                // setup data event

                // finally, call mirror OnConnected event
                Log.Info($"[KCP] Server: OnConnected({connectionId})");
                OnConnected(connectionId);
            }

            void OnDisconnectedCallback()
            {
                // flag for removal
                // (can't remove directly because connection is updated
                //  and event is called while iterating all connections)
                connectionsToRemove.Add(connectionId);

                // call mirror event
                Log.Info($"[KCP] Server: OnDisconnected({connectionId})");
                OnDisconnected(connectionId);
            }
        }

        // receive + add + process once.
        // best to call this as long as there is more data to receive.
        void ProcessMessage(ArraySegment<byte> segment, int connectionId)
        {
            //Log.Info($"[KCP] server raw recv {msgLength} bytes = {BitConverter.ToString(buffer, 0, msgLength)}");

            // is this a new connection?
            if (!connections.TryGetValue(connectionId, out KcpServerConnection connection))
            {
                // create a new KcpConnection based on last received
                // EndPoint. can be overwritten for where-allocation.
                connection = CreateConnection(connectionId);

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


                // now input the message & process received ones
                // connected event was set up.
                // tick will process the first message and adds the
                // connection if it was the handshake.
                connection.RawInput(segment);
                connection.TickIncoming();

                // again, do not add to connections.
                // if the first message wasn't the kcp handshake then
                // connection will simply be garbage collected.
            }
            // existing connection: simply input the message into kcp
            else
            {
                connection.RawInput(segment);
            }
        }

        // process incoming messages. should be called before updating the world.
        // virtual because relay may need to inject their own ping or similar.
        readonly HashSet<int> connectionsToRemove = new HashSet<int>();
        public virtual void TickIncoming()
        {
            // input all received messages into kcp
            while (RawReceiveFrom(out ArraySegment<byte> segment, out int connectionId))
            {
                ProcessMessage(segment, connectionId);
            }

            // process inputs for all server connections
            // (even if we didn't receive anything. need to tick ping etc.)
            foreach (KcpServerConnection connection in connections.Values)
            {
                connection.TickIncoming();
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

        // process outgoing messages. should be called after updating the world.
        // virtual because relay may need to inject their own ping or similar.
        public virtual void TickOutgoing()
        {
            // flush all server connections
            foreach (KcpServerConnection connection in connections.Values)
            {
                connection.TickOutgoing();
            }
        }

        // process incoming and outgoing for convenience.
        // => ideally call ProcessIncoming() before updating the world and
        //    ProcessOutgoing() after updating the world for minimum latency
        public virtual void Tick()
        {
            TickIncoming();
            TickOutgoing();
        }

        public virtual void Stop()
        {
            // need to clear connections, otherwise they are in next session.
            // fixes https://github.com/vis2k/kcp2k/pull/47
            connections.Clear();
            socket?.Close();
            socket = null;
        }
    }
}
