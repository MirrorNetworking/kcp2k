# kcp2k

C# [kcp](https://github.com/skywind3000/kcp) for Unity.

* Kcp.cs based on kcp.c v1.7, line-by-line translation to C#
* Fixed [WND_RCV bug](https://github.com/skywind3000/kcp/pull/291) from original kcp
* Optional high level C# code for client/server connection handling
* Optional high level Unreliable channel added

Pull requests for bug fixes & tests welcome.

# Unity
kcp2k works perfectly in netcore.
where-allocation only works with Unity's mono sockets.
In order to run the nonalloc tests, kcp2k remains a Unity project until Unity moves to netcore.

# Allocations
The client is allocation free.
The server's SendTo/ReceiveFrom still allocate.

Previously, [where-allocation](https://github.com/vis2k/where-allocation) for a 25x reduction in server allocations. However:
- It only worked with Unity's old Mono version.
- It didn't work in Unity's IL2CPP builds, which are still faster than Mono + NonAlloc
- It didn't work in regular C# projects.
- Overall, the extra complexity is not worth it. Use IL2CPP instead.
- Microsoft is considering to [remove the allocation](https://github.com/dotnet/runtime/issues/30797#issuecomment-1308599410).
