using System.Net;
using Hazelnut.Rcon;

const string HostArgumentString = "--host=";
const string PortArgumentString = "--port=";
const string PasswordArgumentString = "--password=";

var host = args.FirstOrDefault(arg => arg.StartsWith(HostArgumentString))?[HostArgumentString.Length..];
var port = args.FirstOrDefault(arg => arg.StartsWith(PortArgumentString))?[PortArgumentString.Length..];
var password = args.FirstOrDefault(arg => arg.StartsWith(PasswordArgumentString))?[PasswordArgumentString.Length..];

if (host is not null && host[0] == host[^1] && host[0] == '"')
    host = host[1..^1];
if (port is not null && port[0] == port[^1] && port[0] == '"')
    port = port[1..^1];

while (string.IsNullOrEmpty(host))
{
    Console.Write("Host> ");
    host = Console.ReadLine();
}

IPAddress? hostAddr;
while (!IPAddress.TryParse(host, out hostAddr))
{
    Console.Write("Host> ");
    host = Console.ReadLine();
}

while (string.IsNullOrEmpty(port))
{
    Console.Write("Port> ");
    port = Console.ReadLine();
}

ushort portNumber;
while (!ushort.TryParse(port, out portNumber)) {
    Console.Write("Port> ");
    port = Console.ReadLine();
}

while (string.IsNullOrEmpty(password))
{
    Console.Write("Password> ");
    password = Console.ReadLine();
}

RconClient client;
try
{
    Console.WriteLine("Connecting...");
    client = await RconClient.ConnectToAsync(new IPEndPoint(hostAddr, portNumber), password);
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync(ex.ToString());
    return 1;
}

while (true)
{
    try
    {
        Console.Write("Command> ");
        var command = Console.ReadLine();
        if (command == null)
            break;
        
        var reply = await client.SendCommandAsync(command);
        Console.WriteLine(reply);
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync(ex.ToString());
    }
}

client.Dispose();

return 0;
