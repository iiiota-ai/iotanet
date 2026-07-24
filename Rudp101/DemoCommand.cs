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
 
    public static DemoCommand Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new DemoCommand();
        }

        string mode = args[0];
        string? option = args.Length > 1 ? args[1] : null;

        uint? dropWindowSequence = null;

        if (args.Length > 2 && args[2].StartsWith("dropseq"))
        {
            string seqText = args[2]["dropseq".Length..];

            if (uint.TryParse(seqText, out uint seq))
            {
                dropWindowSequence = seq;
            }
        }

        return new DemoCommand
        {
            Mode = mode,
            DropFirstAck = mode == "receiver" && option == "dropack",
            DropFirstData = mode == "sender" && option == "dropfirstdata",
            Reorder = mode == "sender" && option == "reorder",
            FireOrder = mode == "sender" && option == "fireorder",
            Window = mode == "sender" && option == "window",
            DropWindowSequence = dropWindowSequence,
            TestCodec = mode == "testcodec"
        };
    }
}