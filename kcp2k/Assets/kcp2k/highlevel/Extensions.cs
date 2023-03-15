using System;
using System.Net;
using System.Net.Sockets;

namespace kcp2k
{
    public static class Extensions
    {
        // non-blocking UDP send.
        // => wrapped with Poll to avoid WouldBlock allocating new SocketException.
        // => wrapped with try-catch to ignore WouldBlock exception.
        // make sure to set socket.Blocking = false before using this!
        public static bool SendToNonBlocking(this Socket socket, ArraySegment<byte> data, EndPoint remoteEP)
        {
            try
            {
                // when using non-blocking sockets, SendTo may return WouldBlock.
                // in C#, WouldBlock throws a SocketException, which is expected.
                // unfortunately, creating the SocketException allocates in C#.
                // let's poll first to avoid the WouldBlock allocation.
                // note that this entirely to avoid allocations.
                // non-blocking UDP doesn't need Poll in other languages.
                // and the code still works without the Poll call.
                if (!socket.Poll(0, SelectMode.SelectWrite)) return false;

                // send to the the endpoint.
                // do not send to 'newClientEP', as that's always reused.
                // fixes https://github.com/MirrorNetworking/Mirror/issues/3296
                socket.SendTo(data.Array, data.Offset, data.Count, SocketFlags.None, remoteEP);
                return true;
            }
            catch (SocketException e)
            {
                // for non-blocking sockets, SendTo may throw WouldBlock.
                // in that case, simply drop the message. it's UDP, it's fine.
                if (e.SocketErrorCode == SocketError.WouldBlock) return false;

                // otherwise it's a real socket error. throw it.
                throw;
            }
        }

        // non-blocking UDP send.
        // => wrapped with Poll to avoid WouldBlock allocating new SocketException.
        // => wrapped with try-catch to ignore WouldBlock exception.
        // make sure to set socket.Blocking = false before using this!
        public static bool SendNonBlocking(this Socket socket, ArraySegment<byte> data)
        {
            try
            {
                // when using non-blocking sockets, SendTo may return WouldBlock.
                // in C#, WouldBlock throws a SocketException, which is expected.
                // unfortunately, creating the SocketException allocates in C#.
                // let's poll first to avoid the WouldBlock allocation.
                // note that this entirely to avoid allocations.
                // non-blocking UDP doesn't need Poll in other languages.
                // and the code still works without the Poll call.
                if (!socket.Poll(0, SelectMode.SelectWrite)) return false;

                // send to the the endpoint.
                // do not send to 'newClientEP', as that's always reused.
                // fixes https://github.com/MirrorNetworking/Mirror/issues/3296
                socket.Send(data.Array, data.Offset, data.Count, SocketFlags.None);
                return true;
            }
            catch (SocketException e)
            {
                // for non-blocking sockets, SendTo may throw WouldBlock.
                // in that case, simply drop the message. it's UDP, it's fine.
                if (e.SocketErrorCode == SocketError.WouldBlock) return false;

                // otherwise it's a real socket error. throw it.
                throw;
            }
        }
    }
}