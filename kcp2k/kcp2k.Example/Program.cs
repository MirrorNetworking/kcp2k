// See https://aka.ms/new-console-template for more information
using kcp2k;
using System;
using System.Linq;
using System.Threading;

// greeting
Console.WriteLine("kcp example");

// setup logging
Log.Info = Console.WriteLine;
Log.Warning = Console.WriteLine;
Log.Error = Console.WriteLine;

// common config
const ushort port = 7777;
KcpConfig config = new KcpConfig(
    // force NoDelay and minimum interval.
    // this way UpdateSeveralTimes() doesn't need to wait very long and
    // tests run a lot faster.
    NoDelay: true,
    // not all platforms support DualMode.
    // run tests without it so they work on all platforms.
    DualMode: false,
    Interval: 1, // 1ms so at interval code at least runs.
    Timeout: 2000,

    // large window sizes so large messages are flushed with very few
    // update calls. otherwise tests take too long.
    SendWindowSize: Kcp.WND_SND * 1000,
    ReceiveWindowSize: Kcp.WND_RCV * 1000,

    // congestion window _heavily_ restricts send/recv window sizes
    // sending a max sized message would require thousands of updates.
    CongestionWindow: false,

    // maximum retransmit attempts until dead_link detected
    // default * 2 to check if configuration works
    MaxRetransmits: Kcp.DEADLINK * 2
);

// create server
KcpServer server = new KcpServer(
    (connectionId) => {},
    (connectionId, message, channel) => Log.Info($"[KCP] OnServerDataReceived({connectionId}, {BitConverter.ToString(message.Array, message.Offset, message.Count)} @ {channel})"),
    (connectionId) => {},
    (connectionId, error, reason) => Log.Error($"[KCP] OnServerError({connectionId}, {error}, {reason}"),
    config
);

// create client
KcpClient client = new KcpClient(
    () => {},
    (message, channel) => Log.Info($"[KCP] OnClientDataReceived({BitConverter.ToString(message.Array, message.Offset, message.Count)} @ {channel})"),
    () => {},
    (error, reason) => Log.Warning($"[KCP] OnClientError({error}, {reason}"),
    config
);

// convenience function
void UpdateSeveralTimes(int amount)
{
    // update serveral times to avoid flaky tests.
    // => need to update at 120 times for default maxed sized messages
    //    where it requires 120+ fragments.
    // => need to update even more often for 2x default max sized
    for (int i = 0; i < amount; ++i)
    {
        client.Tick();
        server.Tick();
        // update 'interval' milliseconds.
        // the lower the interval, the faster the tests will run.
        Thread.Sleep((int)config.Interval);
    }
}

// start server
server.Start(port);

// connect client
client.Connect("127.0.0.1", port);
UpdateSeveralTimes(5);

// send client to server
client.Send(new byte[]{0x01, 0x02}, KcpChannel.Reliable);
UpdateSeveralTimes(10);

// send server to client
int firstConnectionId = server.connections.Keys.First();
server.Send(firstConnectionId, new byte[]{0x03, 0x04}, KcpChannel.Reliable);
UpdateSeveralTimes(10);