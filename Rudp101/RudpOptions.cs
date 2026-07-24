public sealed class RudpOptions
{
    public int TimeoutMs { get; init; } = 500;

    public int MaxRetries { get; init; } = 5;

    public uint WindowSize { get; init; } = 3;

    public uint TotalMessages { get; init; } = 6;
}