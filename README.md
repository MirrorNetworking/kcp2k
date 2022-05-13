# kcp2k

C# [kcp](https://github.com/skywind3000/kcp) for Unity.

* Kcp.cs based on kcp.c v1.7, line-by-line translation to C#
* Fixed [WND_RCV bug](https://github.com/skywind3000/kcp/pull/291) from original kcp
* Optional high level C# code for client/server connection handling
* Optional high level Unreliable channel added
* Optional [where-allocation](https://github.com/vis2k/where-allocation) KcpClient/Server/Connection **NonAlloc** versions

Pull requests for bug fixes & tests welcome.

# Unity
kcp2k works perfectly in netcore.
where-allocation only works with Unity's mono sockets.
In order to run the nonalloc tests, kcp2k remains a Unity project until Unity moves to netcore.
