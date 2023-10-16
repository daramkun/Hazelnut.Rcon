using System.Net;
using Hazelnut.Rcon;

var server = new RconReceiver(new IPEndPoint(IPAddress.Any, 25575), "example");

server.Connected += (sender, remoteEndPoint, authed) =>
{
    Console.WriteLine("Connected from {0} ({1})", remoteEndPoint, authed);
};
server.Received += (receiver, remoteEndPoint, command) =>
{
    Console.WriteLine("{0}> {1}", remoteEndPoint, command);
    return command;
};
server.Disconnected += (receiver, remoteEndPoint) =>
{
    Console.WriteLine("Disconnected from {0}", remoteEndPoint);
};

server.Run();