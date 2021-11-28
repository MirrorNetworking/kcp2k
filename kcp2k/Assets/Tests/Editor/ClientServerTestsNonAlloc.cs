// where-allocation version for client server tests
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
                DualMode,
                NoDelay,
                Interval,
                0,
                true,
                SendWindowSize,
                ReceiveWindowSize,
                Timeout
            );
            server.NoDelay = NoDelay;
            server.Interval = Interval;
        }

        protected override void CreateClient()
        {
            client = new KcpClientNonAlloc(
                () => {},
                ClientOnData,
                () => {}
            );
        }
    }
}