/// <summary>
/// receiver 状态机
/// </summary>
public sealed class RudpReceiveState
{
    /// <summary>
    /// 期望接收的包序号
    /// </summary>
    private uint _expectedSequence = 1;

    /// <summary>
    /// 缓存乱序包，表示提前到达但还不能交付
    /// </summary>
    private readonly Dictionary<uint, RudpPacket> _pendingPackets = new ();

    public uint LastAckSequence => _expectedSequence - 1;

    private readonly uint _receiveWindowSize;

    public RudpReceiveState(uint receiveWindowSize)
    {
        _receiveWindowSize = receiveWindowSize;
    }

    public IReadOnlyList<RudpPacket> Accept(RudpPacket packet)
    {
        var delivered = new List<RudpPacket>();

        if(packet.Sequence >= _expectedSequence + _receiveWindowSize)
        {
            Console.WriteLine($"Drop out-of-window DATA seq={packet.Sequence}, expected={_expectedSequence}");
            return delivered;
        }

        // 旧包(重复包)已接收过，丢弃但仍ACK
        if(packet.Sequence < _expectedSequence)
        {
            return delivered;
        }

        // 只交付预期包，并且查看缓存中是否有下一seq包
        if(packet.Sequence == _expectedSequence)
        {
            delivered.Add(packet);
            _expectedSequence++;

            while(_pendingPackets.Remove(_expectedSequence, out RudpPacket? pendingPacket))
            {
                delivered.Add(pendingPacket);
                _expectedSequence++;
            }

            return delivered;
        }

        if(_pendingPackets.TryAdd(packet.Sequence, packet))
        {
            Console.WriteLine($"Buffered out-of-order DATA seq={packet.Sequence}, expected={_expectedSequence}");
        }
        else
        {
            Console.WriteLine($"Duplicate buffered DATA seq={packet.Sequence}, payload ignored");
        }
        return delivered;
    }
}