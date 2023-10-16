using System.Runtime.InteropServices;
using System.Text;

namespace Hazelnut.Rcon;

[Serializable]
[StructLayout(LayoutKind.Sequential)]
internal readonly struct RconPacket
{
    public static RconPacket Parse(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[MaximumPacketSize];
        if (stream.Read(buffer[..12]) != 12)
            throw new IOException("Cannot read header bytes.");

        var headerBytes = MemoryMarshal.Cast<byte, int>(buffer);
        var length = headerBytes[0];
        var requestId = headerBytes[1];
        var packetType = (RconPacketType)headerBytes[2];

        var payloadLength = length - (sizeof(int) + sizeof(RconPacketType) + 1);
        var payloadAndTerminateLength = payloadLength + 1;
        var payloadBytes = buffer.Slice(13, payloadAndTerminateLength);

        if (stream.Read(payloadBytes) != payloadAndTerminateLength)
            throw new IOException("Cannot read payload bytes.");

        if (buffer[12 + payloadLength] != 0 || buffer[12 + payloadLength + 1] != 0)
            throw new InvalidDataException("Packet has not Null-Terminate string.");
        
        if (packetType is not RconPacketType.Login and not RconPacketType.Response and not RconPacketType.RunCommand)
            throw new InvalidDataException("Packet type is not 0 or 2 or 3");

        var payloadString = Encoding.UTF8.GetString(payloadBytes[..^2]);
        return new RconPacket(requestId, packetType, payloadString);
    }

    public static bool TryParse(Stream stream, out RconPacket result)
    {
        try
        {
            result = Parse(stream);
            return true;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    private const int MaximumPacketSize = 4096;
    
    public readonly int RequestId;
    public readonly RconPacketType PacketType;
    public readonly string Payload;

    public RconPacket(int requestId, RconPacketType packetType, string payload)
    {
        RequestId = requestId;
        PacketType = packetType;
        Payload = payload;
    }

    public void WriteTo(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[MaximumPacketSize];

        var headerBytes = MemoryMarshal.Cast<byte, int>(buffer);
        headerBytes[1] = RequestId;
        headerBytes[2] = (int)PacketType;

        var payloadBytes = buffer[12..^2];
        var payloadBytesCount = Encoding.UTF8.GetBytes(Payload, payloadBytes);
        
        var packetLength = sizeof(int) + sizeof(RconPacketType) + payloadBytesCount + 2;
        headerBytes[0] = packetLength;
        
        stream.Write(buffer[..(packetLength + sizeof(int))]);
    }

    public override int GetHashCode() => (RequestId | (int)PacketType) ^ Payload.GetHashCode();

    public override bool Equals(object? obj)
    {
        if (obj is not RconPacket p)
            return false;
        
        return RequestId == p.RequestId && PacketType == p.PacketType && Payload == p.Payload;
    }

    public override string ToString() =>
        $"{{RequestId: {RequestId}, PacketType: {PacketType}, Payload: {Payload}, Length: {sizeof(int) + sizeof(int) + Payload.Length + 2}, TotalPacketSize: {sizeof(int) + sizeof(int) + sizeof(int) + Payload.Length + 2}}}";
}