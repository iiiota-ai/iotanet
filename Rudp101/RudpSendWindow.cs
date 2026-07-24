/// <summary>
/// sender 滑动窗口，窗口范围：[baseSequence, baseSequence + windowSize)
/// </summary>
public sealed class RudpSendWindow
{
    private readonly uint _totalMessages;

    private readonly uint _windowSize;
    
    /// <summary>
    /// 缓存已发送但可能需要重传的包
    /// </summary>
    private readonly Dictionary<uint, byte[]> _sentPackets = new ();
    
    /// <summary>
    /// 当前窗口做左边，最早未确认交付的包
    /// </summary>
    public uint BaseSequence { get; private set; } = 1;

    /// <summary>
    /// 下一个可发送的包
    /// </summary>
    public uint NextSequence { get; private set; } = 1;

    public bool IsCompleted => BaseSequence > _totalMessages;

    public bool CanSend => NextSequence <= _totalMessages && NextSequence < BaseSequence + _windowSize;

    public RudpSendWindow(uint totalMessages, uint windowSize)
    {
        _totalMessages = totalMessages;
        _windowSize = windowSize;
    }

    public void MarkSent(uint sequence, byte[] data)
    {
        _sentPackets[sequence] = data;
        NextSequence = sequence + 1;
    }

    public bool TryAck(uint ackSequence)
    {
        if(ackSequence < BaseSequence)
        {
            return false;
        }
        
        // 移动窗口左侧，已确认交付指针
        uint oldBase = BaseSequence;
        BaseSequence = ackSequence + 1;

        for(uint seq = oldBase; seq < BaseSequence; seq++)
        {
            _sentPackets.Remove(seq);
        }

        return true;
    }

    public IReadOnlyList<KeyValuePair<uint, byte[]>> GetPacketsForRetransmit()
    {
        return _sentPackets.OrderBy(x => x.Key).ToList();
    }
}