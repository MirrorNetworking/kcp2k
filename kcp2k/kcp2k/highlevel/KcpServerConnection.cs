// server needs to store a separate KcpPeer for each connection.
// as well as remoteEndPoint so we know where to send data to.
using System;
using System.Net;

namespace kcp2k
{
    public class KcpServerConnection : KcpPeer
    {
        public readonly EndPoint remoteEndPoint;

        // callbacks
        // even for errors, to allow liraries to show popups etc.
        // instead of logging directly.
        // (string instead of Exception for ease of use and to avoid user panic)
        //
        // events are readonly, set in constructor.
        // this ensures they are always initialized when used.
        // fixes https://github.com/MirrorNetworking/Mirror/issues/3337 and more
        protected readonly Action<KcpServerConnection> OnConnectedCallback;
        protected readonly Action<ArraySegment<byte>, KcpChannel> OnDataCallback;
        protected readonly Action OnDisconnectedCallback;
        protected readonly Action<ErrorCode, string> OnErrorCallback;
        protected readonly Action<ArraySegment<byte>> RawSendCallback;

        public KcpServerConnection(
            Action<KcpServerConnection> OnConnected,
            Action<ArraySegment<byte>, KcpChannel> OnData,
            Action OnDisconnected,
            Action<ErrorCode, string> OnError,
            Action<ArraySegment<byte>> OnRawSend,
            KcpConfig config,
            uint cookie,
            EndPoint remoteEndPoint)
                : base(config, cookie)
        {
            OnConnectedCallback = OnConnected;
            OnDataCallback = OnData;
            OnDisconnectedCallback = OnDisconnected;
            OnErrorCallback = OnError;
            RawSendCallback = OnRawSend;

            this.remoteEndPoint = remoteEndPoint;
        }

        // callbacks ///////////////////////////////////////////////////////////
        protected override void OnAuthenticated()
        {
            // only send handshake to client AFTER we received his
            // handshake in OnAuthenticated.
            // we don't want to reply to random internet messages
            // with handshakes each time.
            SendHandshake();

            OnConnectedCallback(this);
        }

        protected override void OnData(ArraySegment<byte> message, KcpChannel channel) =>
            OnDataCallback(message, channel);

        protected override void OnDisconnected() =>
            OnDisconnectedCallback();

        protected override void OnError(ErrorCode error, string message) =>
            OnErrorCallback(error, message);

        protected override void RawSend(ArraySegment<byte> data) =>
            RawSendCallback(data);
        ////////////////////////////////////////////////////////////////////////
    }
}
