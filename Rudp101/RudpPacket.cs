using System;


/// <summary>
/// 包类型标记
/// </summary>
[Flags]
public enum PacketFlags : byte
{
    None = 0,
    Data= 1,
    Ack = 2
}

public static class RudpProtocol
{
    /// <summary>
    /// 协议标识魔数，"RU"
    /// </summary>
    public const ushort Magic = 0x5255;
    /// <summary>
    /// 协议版本号
    /// </summary>
    public const byte Version = 1;
    
    /// <summary>
    /// 协议标识魔数占用大小
    /// </summary>
    public const int MagicSize = 2;
    /// <summary>
    /// 协议版本号占用大小
    /// </summary>
    public const int VersionSize = 1;
    /// <summary>
    /// 包类型标记占用大小
    /// </summary>
    public const int FlagsSize = 1;
    /// <summary>
    /// 数据包序号占用大小
    /// </summary>
    public const int SequenceSize = 4;
    /// <summary>
    /// payload长度字段占用大小
    /// </summary>
    public const int LengthSize = 2;

    /// <summary>
    /// 协议头总大小
    /// </summary>
    public const int HeaderSize = MagicSize + VersionSize + FlagsSize + SequenceSize + LengthSize;
}

public sealed class RudpPacket
{
    /// <summary>
    /// 包类型
    /// </summary>
    public required PacketFlags Flags { get; init; }
    /// <summary>
    /// 数据包序号
    /// </summary>
    public required uint Sequence { get; init; }
    /// <summary>
    /// 载荷
    /// </summary>
    public required byte[] Payload { get; init; }
}