using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Hazelnut.Rcon;

public delegate string RconReceivedEventHandler(RconReceiver sender, string received);

public class RconReceiver : IDisposable
{
    private readonly Socket _listenSocket;
    private readonly string _password;
    private CancellationTokenSource? _cancellationTokenSource;

    public event RconReceivedEventHandler? Received;

    public bool IsRunning => _cancellationTokenSource is { IsCancellationRequested: false };

    public RconReceiver(EndPoint endPoint, string password)
    {
        _password = password;

        _listenSocket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        _listenSocket.Bind(endPoint);
        _listenSocket.Listen(200);
    }

    ~RconReceiver()
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
        if (_cancellationTokenSource?.IsCancellationRequested == true)
            throw new ObjectDisposedException(GetType().Name);
        
        _cancellationTokenSource?.Cancel();

        _listenSocket.Close();
        _listenSocket.Dispose();
    }

    public void Start()
    {
        if (_cancellationTokenSource?.IsCancellationRequested == true)
            throw new ObjectDisposedException(GetType().Name);
        if (_cancellationTokenSource != null)
            throw new InvalidOperationException("Server already started.");
        
        _cancellationTokenSource = new CancellationTokenSource();
        _listenSocket.BeginAccept(OnSocketReceived, null);
    }

    public void Run()
    {
        Start();
        
        while (IsRunning)
        {
            try
            {
                Thread.Sleep(Timeout.InfiniteTimeSpan);
            }
            catch (ThreadInterruptedException)
            {
                // TODO:
            }
        }
    }

    protected virtual string OnCommandReceived(string command)
    {
        return Received?.Invoke(this, command)
               ?? "[RCON] Command Handler is not registered.";
    }

    private async void OnSocketReceived(IAsyncResult result)
    {
        using var acceptedSocket = _listenSocket.EndAccept(result);
        _listenSocket.BeginAccept(OnSocketReceived, null);

        var cancellationToken = _cancellationTokenSource!.Token;

        await using var acceptedSocketStream = new NetworkStream(acceptedSocket);
        var authPacketResult = await RconPacket.TryParseAsync(acceptedSocketStream, cancellationToken);

        if (authPacketResult is not { PacketType: RconPacketType.Login } authPacket)
            return;

        var isAuthSucceed = authPacket.PayloadString.Equals(_password);
        var authResultPacket = new RconPacket(isAuthSucceed ? authPacket.RequestId : -1, Array.Empty<byte>(),
            RconPacketType.RunCommand);
        await acceptedSocketStream.WriteAsync(authResultPacket.ToArray(), cancellationToken);

        if (!isAuthSucceed)
            return;
        
        await RunServerSession(acceptedSocket, acceptedSocketStream, cancellationToken);
    }

    private async ValueTask RunServerSession(Socket acceptedSocket, Stream acceptedSocketStream, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var receivedPacketResult = await RconPacket.TryParseAsync(acceptedSocketStream, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                    break;

                if (receivedPacketResult is { PacketType: RconPacketType.RunCommand } receivedPacket)
                {
                    var payload = OnCommandReceived(receivedPacket.PayloadString);
                    var sendPacket = new RconPacket(receivedPacket.RequestId, payload);
                    await acceptedSocketStream.WriteAsync(sendPacket.ToArray(), cancellationToken);
                }
                else
                {
                    var sendPacket = new RconPacket(-1, "invalid packet received.");
                    await acceptedSocketStream.WriteAsync(sendPacket.ToArray(), cancellationToken);
                }
            }
        }
        catch (IOException)
        {
            // Terminated Client
        }
    }
}