using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;

namespace Hazelnut.Rcon;

public class RconClient : IDisposable
{
    private readonly Socket _clientSocket;
    private readonly NetworkStream _socketStream;

    private int _requestId;

    public static async ValueTask<RconClient> ConnectAsync(EndPoint endPoint, string password)
    {
        var clientSocket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        await clientSocket.ConnectAsync(endPoint);

        var socketStream = new NetworkStream(clientSocket);
        
        var authPacket = new RconPacket(0, password, RconPacketType.Login);
        await socketStream.WriteAsync(authPacket.ToArray());

        var authReplyPacketResult = await RconPacket.TryParseAsync(socketStream);
        if (authReplyPacketResult is not { PacketType: RconPacketType.RunCommand } authReplyPacket)
            throw new IOException("Rcon Client cannot connect to server.");

        if (authReplyPacket.RequestId == -1)
            throw new AuthenticationException("Rcon Client authentication is failed.");

        return new RconClient(clientSocket, socketStream);
    }

    private RconClient(Socket socket, NetworkStream stream)
    {
        _clientSocket = socket;
        _socketStream = stream;
    }

    ~RconClient()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        _clientSocket.Dispose();
        _socketStream.Dispose();
    }

    public async ValueTask<string> SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        var requestId = Interlocked.Increment(ref _requestId);
        var sendPacket = new RconPacket(requestId, command, RconPacketType.RunCommand);

        await _socketStream.WriteAsync(sendPacket.ToArray(), cancellationToken);

        var receivedPacketResult = await RconPacket.TryParseAsync(_socketStream, cancellationToken);
        if (receivedPacketResult is not { } receivedPacket)
            throw new IOException("Rcon Client cannot received result packet.");

        if (receivedPacket.RequestId != _requestId)
            throw new InvalidDataException("Request Id is invalid.");

        if (receivedPacket.PacketType != RconPacketType.Response)
            throw new InvalidDataException("Packet type is not response.");

        return receivedPacket.PayloadString;
    }
}