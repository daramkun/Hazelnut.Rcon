using System.Net;
using Hazelnut.Rcon;

var server = new RconReceiver(new IPEndPoint(IPAddress.Any, 25575), "example");

server.Received += (receiver, command) =>
{
    Console.WriteLine(command);
    return command;
};

server.Run();