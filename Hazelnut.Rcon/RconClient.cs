using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading.Channels;

namespace Hazelnut.Rcon;

public class RconClient : IDisposable
{
    public static async ValueTask<RconClient> ConnectToAsync(EndPoint endPoint, string password)
    {
        var clientSocket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        await clientSocket.ConnectAsync(endPoint);

        var networkStream = new NetworkStream(clientSocket);

        var authPacket = new RconPacket(0, RconPacketType.Login, password);
        authPacket.WriteTo(networkStream);

        if (!RconPacket.TryParse(networkStream, out var authReply))
            throw new IOException("Rcon Client cannot connect to server.");

        if (authReply.RequestId == -1)
            throw new AuthenticationException("Rcon Client authentication is failed");

        return new RconClient(networkStream);
    }
    
    private readonly NetworkStream _networkStream;
    private readonly Channel<RconPacket> _queuedReceivedPackets;

    private readonly Thread _clientBodyThread;
    private readonly CancellationTokenSource _cancellationToken = new();

    private int _requestId;
    
    public RconClient(NetworkStream networkStream)
    {
        _networkStream = networkStream;
        _queuedReceivedPackets = Channel.CreateUnbounded<RconPacket>();

        _clientBodyThread = new Thread(ClientBody)
        {
            IsBackground = true
        };
        _clientBodyThread.Start(this);
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
        _queuedReceivedPackets.Writer.Complete();
        _cancellationToken.Cancel();
        _networkStream.Dispose();
    }

    public async ValueTask<string> SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        var requestId = Interlocked.Increment(ref _requestId);
        var sendPacket = new RconPacket(requestId, RconPacketType.RunCommand, command);
        
        sendPacket.WriteTo(_networkStream);

        while (!cancellationToken.IsCancellationRequested &&
               await _queuedReceivedPackets.Reader.WaitToReadAsync(cancellationToken))
        {
            if (!_queuedReceivedPackets.Reader.TryPeek(out var receivedPacket) ||
                receivedPacket.RequestId != requestId)
                continue;
            
            await _queuedReceivedPackets.Reader.ReadAsync(cancellationToken);

            if (receivedPacket.PacketType != RconPacketType.Response)
                throw new InvalidDataException("Packet type is not respond.");

            return receivedPacket.Payload;
        }

        throw new OperationCanceledException();
    }

    private static async void ClientBody(object? state)
    {
        if (state is not RconClient client)
            return;

        var networkStream = client._networkStream;
        var cancellationToken = client._cancellationToken.Token;

        var writer = client._queuedReceivedPackets.Writer;
        while (!cancellationToken.IsCancellationRequested)
        {
            RconPacket packet;
            try
            {
                if (!RconPacket.TryParse(networkStream, out packet))
                    continue;
            }
            catch
            {
                continue;
            }

            await writer.WriteAsync(packet, cancellationToken);
        }
    }
}