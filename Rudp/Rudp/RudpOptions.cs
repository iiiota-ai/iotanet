public sealed class RudpOptions
{
    public int TimeoutMs { get; init; } = 500;

    public int MaxRetries { get; init; } = 5;

    public uint WindowSize { get; init; } = 8;

    public uint TotalMessages { get; init; } = 10;

    public int FastRetransmitDuplicateAckThreshold { get; init; } = 2;
    
    public uint ReceiveWindowSize { get; init; } = 4;
}