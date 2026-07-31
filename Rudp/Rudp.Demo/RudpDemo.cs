using System.Net;
using System.Net.Sockets;
using System.Text;

public static class RudpDemo
{
    public static async Task RunStopAndWaitSenderAsync(bool dropFirstData, bool reorder, RudpOptions options)
    {
        using var udp = new UdpClient(0);
        var target = new IPEndPoint(IPAddress.Loopback, 9000);

        bool firstDataDropped = false;

        for(uint seq = 1; seq <= 3; seq++)
        {
            uint newSeq = seq;
            if (reorder && seq != 1)
            {
                newSeq = seq == 2 ? 3u : 2u;
            }

            var packet = DemoPayload.CreateMessagePacket(newSeq);

            var shouldDropFirstData = dropFirstData && !firstDataDropped;
            if (shouldDropFirstData)
            {
                firstDataDropped = true;
            }

            bool ok = await SendWithRetryAsync(udp, target, packet, shouldDropFirstData, options);
            if (!ok)
            {
                Console.WriteLine($"Stop sending because seq={packet.Sequence} failed");
                return;
            }
        }
    }

    public static async Task RunFireOrderSenderAsync()
    {
        using var udp = new UdpClient(0);
        var target = new IPEndPoint(IPAddress.Loopback, 9000);

        uint[] order = [1, 3, 2];
        foreach(uint seq in order)
        {
            var packet = DemoPayload.CreateMessagePacket(seq);

            await RudpUdpIo.SendDataAsync(udp, target, packet);
            await Task.Delay(100);
        }
    }

    public static void RunCodecTest()
    {
        var bytes = TestEncode();
        TestDecode(bytes);
    }

    private static byte[] TestEncode()
    {
        var packet = DemoPayload.CreateDataPacket(1, "你好，rudp");

        Console.WriteLine($"TestEncode Flags:{packet.Flags} Sequence:{packet.Sequence} Payload:{Encoding.UTF8.GetString(packet.Payload)}");

        byte[] bytes = RudpPacketCodec.Encode(packet);
        return bytes;
    }

    private static void TestDecode(byte[] bytes)
    {
        RudpPacket decoded = RudpPacketCodec.Decode(bytes);
        Console.WriteLine($"TestDecode Flags:{decoded.Flags} Sequence:{decoded.Sequence} Payload:{Encoding.UTF8.GetString(decoded.Payload)}");
    }

    private static async Task<bool> SendWithRetryAsync(
        UdpClient udp,
        IPEndPoint target,
        RudpPacket packet,
        bool dropFirstData,
        RudpOptions options)
    {
        byte[] data = RudpPacketCodec.Encode(packet);
        bool dropDataDropped = false;

        for(int attempt = 1; attempt <= options.MaxRetries; attempt++)
        {
            if(dropFirstData && !dropDataDropped)
            {
                Console.WriteLine($"Simulate lost DATA seq={packet.Sequence}, attempt={attempt}");
                dropDataDropped = true;
            }
            else
            {
                await RudpUdpIo.SendEncodedDataAsync(udp, target, packet.Sequence, data, $"Sent attempt={attempt}");
            }

            try
            {
                RudpPacket? ack = await RudpUdpIo.ReceiveAckAsync(udp, options.TimeoutMs);

                if(ack is null)
                {
                    continue;
                }

                if(ack.Sequence == packet.Sequence)
                {
                    Console.WriteLine($"Received ACK seq={ack.Sequence}");
                    return true;
                }

                Console.WriteLine($"Received unexpected ACK seq={ack.Sequence}, expected={packet.Sequence}");
            }
            catch(OperationCanceledException)
            {
                Console.WriteLine($"Timeout waiting ACK seq={packet.Sequence}");
            }
            catch(SocketException ex) when (
                ex.SocketErrorCode == SocketError.ConnectionReset ||
                ex.SocketErrorCode == SocketError.ConnectionRefused)
            {
                Console.WriteLine($"No ACK endpoint available for seq={packet.Sequence}: {ex.SocketErrorCode}");
            }
            catch(ArgumentException ex)
            {
                Console.WriteLine($"Drop invalid packet while waiting ACK: {ex.Message}");
            }
        }

        Console.WriteLine($"Failed to receive ACK seq={packet.Sequence} after {options.MaxRetries} attempts");
        return false;
    }
}
