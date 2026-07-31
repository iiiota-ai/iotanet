using System.Text;

public static class DemoPayload
{
    public static byte[] CreatePayload(string text)
    {
        return Encoding.UTF8.GetBytes(text);
    }

    public static RudpPacket CreateDataPacket(uint sequence, string text)
    {
        return RudpPacket.Data(sequence, CreatePayload(text));
    }

    public static RudpPacket CreateMessagePacket(uint sequence)
    {
        return CreateDataPacket(sequence, $"message {sequence}");
    }

}