// where-allocation version for client server tests
using UnityEngine;

namespace kcp2k.Tests
{
    public class ClientServerTestsNonAlloc : ClientServerTests
    {
        protected override void CreateServer()
        {
            server = new KcpServerNonAlloc(
                (connectionId) => {},
                ServerOnData,
                (connectionId) => {},
                (connectionId, error, reason) => Debug.LogWarning($"connId={connectionId}: {error}, {reason}"),
                DualMode,
                NoDelay,
                Interval,
                0,
                true,
                SendWindowSize,
                ReceiveWindowSize,
                Timeout,
                MaxRetransmits
            );
            server.NoDelay = NoDelay;
            server.Interval = Interval;
        }

        protected override void CreateClient()
        {
            client = new KcpClientNonAlloc(
                () => {},
                ClientOnData,
                () => {},
                (error, reason) => Debug.LogWarning($"{error}, {reason}")
            );
        }
    }
}