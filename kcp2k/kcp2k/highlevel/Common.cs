using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace kcp2k
{
    public static class Common
    {
        // we need to subtract the channel and cookie bytes from every
        // MaxMessageSize calculation.
        // we also need to tell kcp to use MTU-1 to leave space for the byte.
        public const int CHANNEL_HEADER_SIZE = 1;
        public const int COOKIE_HEADER_SIZE = 4;
        public const int METADATA_SIZE = CHANNEL_HEADER_SIZE + COOKIE_HEADER_SIZE;

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
            (mtu - Kcp.OVERHEAD - METADATA_SIZE) * ((int)rcv_wnd - 1) - 1;

        // kcp encodes 'frg' as 1 byte.
        // max message size can only ever allow up to 255 fragments.
        //   WND_RCV gives 127 fragments.
        //   WND_RCV * 2 gives 255 fragments.
        // so we can limit max message size by limiting rcv_wnd parameter.
        public static int ReliableMaxMessageSize(int mtu, uint rcv_wnd) =>
            ReliableMaxMessageSize_Unconstrained(mtu, Math.Min(rcv_wnd, Kcp.FRG_MAX));

        // unreliable max message size is simply MTU - channel header size
        public static int UnreliableMaxMessageSize(int mtu) =>
            mtu - METADATA_SIZE;

        // helper function to resolve host to IPAddress
        public static bool ResolveHostname(string hostname, out IPAddress[] addresses)
        {
            try
            {
                // NOTE: dns lookup is blocking. this can take a second.
                addresses = Dns.GetHostAddresses(hostname);
                return addresses.Length >= 1;
            }
            catch (SocketException exception)
            {
                Log.Info($"Failed to resolve host: {hostname} reason: {exception}");
                addresses = null;
                return false;
            }
        }

        // if connections drop under heavy load, increase to OS limit.
        // if still not enough, increase the OS limit.
        public static void ConfigureSocketBuffers(Socket socket, int recvBufferSize, int sendBufferSize)
        {
            // log initial size for comparison.
            // remember initial size for log comparison
            int initialReceive = socket.ReceiveBufferSize;
            int initialSend    = socket.SendBufferSize;

            // set to configured size
            try
            {
                socket.ReceiveBufferSize = recvBufferSize;
                socket.SendBufferSize    = sendBufferSize;
            }
            catch (SocketException)
            {
                Log.Warning($"Kcp: failed to set Socket RecvBufSize = {recvBufferSize} SendBufSize = {sendBufferSize}");
            }


            Log.Info($"Kcp: RecvBuf = {initialReceive}=>{socket.ReceiveBufferSize} ({socket.ReceiveBufferSize/initialReceive}x) SendBuf = {initialSend}=>{socket.SendBufferSize} ({socket.SendBufferSize/initialSend}x)");
        }

        // generate a connection hash from IP+Port.
        //
        // NOTE: IPEndPoint.GetHashCode() allocates.
        //  it calls m_Address.GetHashCode().
        //  m_Address is an IPAddress.
        //  GetHashCode() allocates for IPv6:
        //  https://github.com/mono/mono/blob/bdd772531d379b4e78593587d15113c37edd4a64/mcs/class/referencesource/System/net/System/Net/IPAddress.cs#L699
        //
        // => using only newClientEP.Port wouldn't work, because
        //    different connections can have the same port.
        public static int ConnectionHash(EndPoint endPoint) =>
            endPoint.GetHashCode();

        // cookies need to be generated with a secure random generator.
        // we don't want them to be deterministic / predictable.
        // RNG is cached to avoid runtime allocations.
        static readonly RNGCryptoServiceProvider cryptoRandom = new RNGCryptoServiceProvider();
        static readonly byte[] cryptoRandomBuffer = new byte[4];
        public static uint GenerateCookie()
        {
            cryptoRandom.GetBytes(cryptoRandomBuffer);
            return BitConverter.ToUInt32(cryptoRandomBuffer, 0);
        }
    }
}
