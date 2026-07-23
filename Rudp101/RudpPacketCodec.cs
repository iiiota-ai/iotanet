using System.Buffers.Binary;

/// <summary>
/// 编解码器
/// </summary>
public static class RudpPacketCodeC
{

    /// <summary>
    /// 编码，[Magic][Version][Flags][Sequence][Length][Payload]
    /// </summary>
    public static byte[] Encode(RudpPacket packet)
    {
        if(packet.Payload.Length > ushort.MaxValue)
        {
            throw new ArgumentException("Payload is too large.", nameof(packet));
        }

        int start = 0;
        int length = 0;
        int totalLenth = RudpProtocol.HeaderSize + packet.Payload.Length;
        byte[] buffer = new byte[totalLenth];

        // 写入 Magic
        length = RudpProtocol.MagicSize;
        BinaryPrimitives.WriteUInt16BigEndian(
            buffer.AsSpan(start, length),
            RudpProtocol.Magic
        );
        start += RudpProtocol.MagicSize;

        // 写入 Version
        buffer[2] = RudpProtocol.Version;
        start += RudpProtocol.VersionSize;


        // 写入 Flags
        buffer[3] = (byte)packet.Flags;
        start += RudpProtocol.FlagsSize;

        // 写入 Sequence
        length = RudpProtocol.SequenceSize;
        BinaryPrimitives.WriteUInt32BigEndian(
            buffer.AsSpan(start, length),
            (ushort)packet.Sequence
        );
        start += RudpProtocol.SequenceSize;

        // 写入 Length
        length = RudpProtocol.LengthSize;
        BinaryPrimitives.WriteUInt16BigEndian(
            buffer.AsSpan(start, length),
            (ushort)packet.Payload.Length
        );
        start += RudpProtocol.LengthSize;

        // 写入 Payload
        packet.Payload.CopyTo(buffer.AsSpan(start));

        return buffer;
    }

    /// <summary>
    /// 解码器，[Magic][Version][Flags][Sequence][Length][Payload]
    /// </summary>
    public static RudpPacket Decode(byte[] buffer)
    {
        if(buffer.Length < RudpProtocol.HeaderSize)
        {
            throw new ArgumentException("Buffer is too short.", nameof(buffer));
        }

        int start = 0;

        // 读取 Magic
        int length = RudpProtocol.MagicSize;
        ushort magic = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(start, length));
        if(magic != RudpProtocol.Magic)
        {
            throw new ArgumentException("Invalid magic.", nameof(buffer));
        }
        start += length;

        // 读取 Version
        byte version = buffer[2];
        if(version != RudpProtocol.Version)
        {
            throw new ArgumentException("Unsupported version.", nameof(buffer));
        }
        start += RudpProtocol.VersionSize;
        
        // 读取 Flags
        var flags = (PacketFlags)buffer[3];
        start += RudpProtocol.FlagsSize;

        // 读取 Sequence
        length = RudpProtocol.SequenceSize;
        uint sequence = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(start, length));
        start += length;

        // 读取 Length
        length = RudpProtocol.LengthSize;
        ushort payloadLength = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(start, length));
        start += length;
        
        // 读取 Payload
        byte[] payload = buffer.AsSpan(start, payloadLength).ToArray();

        return new RudpPacket
        {
            Flags = flags,
            Sequence = sequence,
            Payload = payload,
        };
    }
}