using System.Net;
using System.Net.Sockets;

public static class RudpUdpIo
{
    public static async Task SendDataAsync(UdpClient udp, IPEndPoint target, RudpPacket packet)
    {
        byte[] data = RudpPacketCodec.Encode(packet);
        await SendEncodedDataAsync(udp, target, packet.Sequence, data, "Sent");
    }

    public static async Task SendEncodedDataAsync(
        UdpClient udp,
        IPEndPoint target,
        uint sequence,
        byte[] data,
        string prefix)
    {
        await udp.SendAsync(data, data.Length, target);
        Console.WriteLine($"{prefix} DATA seq={sequence}");
    }

    public static async Task SendAckAsync(
        UdpClient udp,
        IPEndPoint remoteEndPoint,
        uint ackSequence,
        uint receiveWindow)
    {
        var ack = RudpPacket.Ack(ackSequence, receiveWindow);
        byte[] ackBytes = RudpPacketCodec.Encode(ack);

        await udp.SendAsync(ackBytes, ackBytes.Length, remoteEndPoint);
        Console.WriteLine($"Sent ACK seq={ack.Sequence} to {remoteEndPoint}");
    }

    public static async Task<RudpPacket?> ReceiveAckAsync(UdpClient udp, int timeoutMs)
    {
        using var timeoutCts = new CancellationTokenSource(timeoutMs);

        UdpReceiveResult result = await udp.ReceiveAsync(timeoutCts.Token);
        RudpPacket packet = RudpPacketCodec.Decode(result.Buffer);

        if(packet.Flags != PacketFlags.Ack)
        {
            Console.WriteLine($"Received non-ACK: {packet.Flags}");
            return null;
        }

        return packet;
    }
}
