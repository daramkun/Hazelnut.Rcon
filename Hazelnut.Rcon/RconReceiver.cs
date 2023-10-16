using System.Net;
using System.Net.Sockets;

namespace Hazelnut.Rcon;

public delegate void RconConnectedEventHandler(RconReceiver sender, EndPoint remoteEndPoint, bool isAuthed);
public delegate void RconDisconnectedEventHandler(RconReceiver sender, EndPoint remoteEndPoint);
public delegate string RconReceivedEventHandler(RconReceiver sender, EndPoint remoteEndPoint, string received);

public class RconReceiver : IDisposable
{
    private readonly Socket _listenSocket;
    private readonly string _password;

    private Thread? _ownerThread;
    private CancellationTokenSource? _cancellationToken;

    public event RconConnectedEventHandler? Connected;
    public event RconDisconnectedEventHandler? Disconnected;
    public event RconReceivedEventHandler? Received;

    public bool IsRunning => _cancellationToken is { IsCancellationRequested: false };
    
    public RconReceiver(EndPoint endPoint, string password)
    {
        _password = password;

        _listenSocket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        _listenSocket.Bind(endPoint);
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
        if (_cancellationToken?.IsCancellationRequested == true)
            throw new ObjectDisposedException(GetType().Name);
        _cancellationToken?.Cancel();
        
        _ownerThread?.Interrupt();

        _listenSocket.Close();
        _listenSocket.Dispose();
    }

    public void Start(int backlog = 200)
    {
        if (_cancellationToken?.IsCancellationRequested == true)
            throw new ObjectDisposedException(GetType().Name);
        if (_cancellationToken != null)
            throw new InvalidOperationException("Server already started.");

        _cancellationToken = new CancellationTokenSource();
        _listenSocket.Listen(backlog);
        _listenSocket.BeginAccept(OnSocketReceived, this);

        _ownerThread = Thread.CurrentThread;
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

    protected virtual void OnClientConnected(EndPoint remoteEndPoint, bool isAuthed)
    {
        Connected?.Invoke(this, remoteEndPoint, isAuthed);
    }

    protected virtual void OnClientDisconnected(EndPoint remoteEndPoint)
    {
        Disconnected?.Invoke(this, remoteEndPoint);
    }

    protected virtual string OnCommandReceived(EndPoint remoteEndPoint, string command)
    {
        return Received?.Invoke(this, remoteEndPoint, command)
               ?? "[RCON] Command Handler is not registered.";
    }

    private static async void OnSocketReceived(IAsyncResult result)
    {
        if (result.AsyncState is not RconReceiver receiver)
            return;

        using var acceptedSocket = receiver._listenSocket.EndAccept(result);
        var remoteEndPoint = acceptedSocket.RemoteEndPoint!;
        receiver._listenSocket.BeginAccept(OnSocketReceived, null);

        var cancellationToken = receiver._cancellationToken!.Token;

        await using var acceptedNetworkStream = new NetworkStream(acceptedSocket);
        if (!RconPacket.TryParse(acceptedNetworkStream, out var authRequest))
            return;

        if (authRequest.PacketType != RconPacketType.Login)
            return;

        var isAuthSucceed = authRequest.Payload.Equals(receiver._password);
        var authResponse = new RconPacket(
            isAuthSucceed
                ? authRequest.RequestId
                : -1,
            RconPacketType.RunCommand,
            string.Empty);

        authResponse.WriteTo(acceptedNetworkStream);

        receiver.OnClientConnected(remoteEndPoint, isAuthSucceed);

        try
        {
            if (!isAuthSucceed)
                return;

            await Task.Factory.StartNew(() => { RunServerSession(receiver, remoteEndPoint, acceptedNetworkStream); },
                cancellationToken);
        }
        catch (IOException)
        {
            // Terminated Client
        }
        finally
        {
            receiver.OnClientDisconnected(remoteEndPoint);
        }
    }

    private static void RunServerSession(RconReceiver receiver, EndPoint remoteEndPoint, Stream acceptedNetworkStream)
    {
        while (true)
        {
            if (!RconPacket.TryParse(acceptedNetworkStream, out var receivedPacket))
                break;

            var sendPacket = new RconPacket(receivedPacket.RequestId, RconPacketType.Response,
                receivedPacket.PacketType == RconPacketType.RunCommand
                    ? receiver.OnCommandReceived(remoteEndPoint, receivedPacket.Payload)
                    : "invalid packet received"
            );
            sendPacket.WriteTo(acceptedNetworkStream);
        }
    }
}