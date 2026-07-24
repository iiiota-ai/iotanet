public sealed class DemoCommand
{
    public string Mode { get; init; } = "";

    public bool DropFirstAck { get; init; }

    public bool DropFirstData { get; init; }

    public bool Reorder { get; init; }

    public bool FireOrder { get; init; }

    public bool Window { get; init; }

    public uint? DropWindowSequence { get; init; }

    public bool TestCodec { get; init; }
}