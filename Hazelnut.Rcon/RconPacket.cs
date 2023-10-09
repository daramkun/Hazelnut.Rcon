using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System;

namespace Hazelnut.Rcon;

internal enum RconPacketType
{
    Response = 0,
    RunCommand = 2,
    Login = 3,
}

[Serializable]
internal readonly struct RconPacket
{
    public readonly int Length;
    public readonly int RequestId;
    public readonly RconPacketType PacketType;
    public readonly ArraySegment<byte> Payload;

    public string PayloadString => Encoding.UTF8.GetString(Payload);

    public RconPacket(int requestId, string payload, RconPacketType packetType = RconPacketType.Response)
        : this(requestId, Encoding.UTF8.GetBytes(payload), packetType)
    {

    }

    public RconPacket(int requestId, ArraySegment<byte> payload, RconPacketType packetType = RconPacketType.Response)
    {
        Payload = payload;
        Length = Payload.Count + (Payload.Count > 0 && Payload[^1] == '\0' ? 0 : 1) + 1 + sizeof(int) + sizeof(RconPacketType);
        if (Length + sizeof(int) > 4096)
            throw new ArgumentOutOfRangeException(nameof(payload), "Total Packet Size must least 4096.");
        RequestId = requestId;
        PacketType = packetType;
    }

    public override string ToString()
    {
        var builder = new StringBuilder(32);
        builder.Append('{');
        builder.Append("Length=").Append(Length).Append(',');
        builder.Append("RequestId=").Append(RequestId).Append(',');
        builder.Append("PacketType=").Append((int)PacketType).Append(',');
        builder.Append("Payload=\"").Append(Encoding.UTF8.GetString(Payload)).Append('"');
        builder.Append('}');
        return builder.ToString();
    }

    public byte[] ToArray()
    {
        Span<byte> buffer = stackalloc byte[Length + sizeof(int)];
        if (BitConverter.IsLittleEndian)
        {
            var integerBuffer = MemoryMarshal.Cast<byte, int>(buffer);
            integerBuffer[0] = Length;
            integerBuffer[1] = RequestId;
            integerBuffer[2] = (int)PacketType;
        }
        else
        {
            var integerBuffer = MemoryMarshal.Cast<byte, int>(buffer);
            integerBuffer[0] = BinaryPrimitives.ReverseEndianness(Length);
            integerBuffer[1] = BinaryPrimitives.ReverseEndianness(RequestId);
            integerBuffer[2] = BinaryPrimitives.ReverseEndianness((int)PacketType);
        }
        
        Span<byte> payloadSpan = Payload;
        payloadSpan.CopyTo(buffer[12..]);
            
        buffer[^2] = 0;
        buffer[^1] = 0;

        return buffer.ToArray();
    }

    public static async ValueTask<RconPacket?> TryParseAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[4096];
        var headerBytes = buffer.AsMemory(0, 12);
        if (await stream.ReadAsync(headerBytes, cancellationToken) != 12)
            throw new IOException("Cannot read header bytes.");

        var (length, requestId, packetType) = HeaderToSegments(buffer);

        var payloadLength = length - (sizeof(int) + sizeof(RconPacketType) + 1);
        var payloadAndTerminateLength = payloadLength + 1;
        var payloadBytes = buffer.AsMemory(13, payloadAndTerminateLength);
        if (await stream.ReadAsync(payloadBytes, cancellationToken) != payloadAndTerminateLength)
            throw new IOException("Cannot read payload bytes.");
        
        if (buffer[12 + payloadLength] != 0 || buffer[12 + payloadLength + 1] != 0)
            return null;

        if (packetType is not RconPacketType.Login and not RconPacketType.Response and not RconPacketType.RunCommand)
            return null;

        return new RconPacket(requestId, new ArraySegment<byte>(buffer, 13, payloadLength - 1), packetType);
    }

    private static (int, int, RconPacketType) HeaderToSegments(byte[] headerBytes)
    {
        var header = MemoryMarshal.Cast<byte, int>(headerBytes);
        var length = header[0];
        var requestId = header[1];
        var packetType = (RconPacketType)header[2];
        return (length, requestId, packetType);
    }
}