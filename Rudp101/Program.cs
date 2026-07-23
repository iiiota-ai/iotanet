using System.Net;
using System.Net.Sockets;
using System.Text;

const int timeoutMs = 500;
const int maxRetries = 5;

if(args.Length == 0)
{
    Console.WriteLine($"usage: dotnet run -- receiver|sender");
    return;
}

if(args[0] == "receiver")
{
    await RunReceiver();
}
else if(args[0] == "sender")
{
    await RunSender();
}
else if(args[0] == "testcodec")
{
    var bytes = TestEncode();
    TestDecode(bytes);
}

static async Task RunReceiver()
{
    // 创建UdpClient，绑定9000端口
    using var udp = new UdpClient(9000);    
    Console.WriteLine($"Receiver listening on 127.0.0.1:9000");

    // 期望接收的包序号
    uint expectedSequence = 1;
    // 循环ReceiveAsync
    while (true)
    {
        UdpReceiveResult result = await udp.ReceiveAsync();
        // string text= Encoding.UTF8.GetString(result.Buffer);

        try
        {
            var packet = RudpPacketCodec.Decode(result.Buffer);
            if(packet.Flags != PacketFlags.Data)
            {
                Console.WriteLine($"Drop non-Data packet from {result.RemoteEndPoint}: {packet.Flags}");
                continue;
            }

            // 旧包(重复包)已接收过
            if(packet.Sequence < expectedSequence)
            {
                Console.WriteLine($"Duplicate DATA seq={packet.Sequence}, payload ignored");
            }
            // 只交付预期包，其他丢弃但仍然ACK
            else if(packet.Sequence == expectedSequence)
            {
                Console.WriteLine($"From {result.RemoteEndPoint}: {packet.Flags} {packet.Sequence} {Encoding.UTF8.GetString(packet.Payload)}");
                expectedSequence++;
            }
            else
            {
                Console.WriteLine($"Out-of-order DATA seq={packet.Sequence}, expected={expectedSequence} payload={Encoding.UTF8.GetString(packet.Payload)}");
            }

            // Ack确认包
            var ack = new RudpPacket
            {
                Flags = PacketFlags.Ack,
                Sequence = packet.Sequence,
                Payload = Array.Empty<Byte>()
            };
            byte[] ackBytes = RudpPacketCodec.Encode(ack);
            await udp.SendAsync(ackBytes, ackBytes.Length, result.RemoteEndPoint);
            Console.WriteLine($"Sent ACK seq={ack.Sequence} to {result.RemoteEndPoint}");
        }
        catch(ArgumentException ex)
        {
            Console.WriteLine($"Drop invalid packet from {result.RemoteEndPoint}: {ex.Message}");
        }        
    }    
}

static async Task RunSender()
{
    // 创建 UdpClient
    using var udp = new UdpClient();

    // 把 "hello udp" 转成 bytes
    // byte[] data = Encoding.UTF8.GetBytes("hello udp");
    var packet = new RudpPacket
    {
        Flags = PacketFlags.Data,
        Sequence = 1,
        Payload = Encoding.UTF8.GetBytes("hello rudp")
    };
    byte[] data = RudpPacketCodec.Encode(packet);
    var target = new IPEndPoint(IPAddress.Loopback, 9000);

    for(int attempt = 1; attempt <= maxRetries; attempt++)
    {
        // SendAsync 到 127.0.0.1:9000
        await udp.SendAsync(data, data.Length, target);
        Console.WriteLine($"Sent DATA seq={packet.Sequence}, attempt={attempt}");
        
        try
        {
            using var timeoutCts = new CancellationTokenSource(timeoutMs);
            UdpReceiveResult result = await udp.ReceiveAsync(timeoutCts.Token);
            RudpPacket ack = RudpPacketCodec.Decode(result.Buffer);

            if(ack.Flags == PacketFlags.Ack && ack.Sequence == packet.Sequence)
            {
                Console.WriteLine($"Received ACK seq={ack.Sequence} from {result.RemoteEndPoint}");
                return;
            }

            Console.WriteLine($"Received unexpected packet: {ack.Flags} seq={ack.Sequence}");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"Timeout waiting ACK seq={packet.Sequence}");
        }
        catch (SocketException ex) when (
            ex.SocketErrorCode == SocketError.ConnectionReset ||
            ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            Console.WriteLine($"No ACK endpoint available for seq={packet.Sequence}: {ex.SocketErrorCode}");
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"Drop invalid packet while waiting ACK: {ex.Message}");
        }
    }

    Console.WriteLine($"Failed to receive ACK seq={packet.Sequence} after {maxRetries} attempts");
}

static byte[] TestEncode()
{
    var packet = new RudpPacket
    {
        Flags = PacketFlags.Data,
        Sequence = 1,
        Payload = Encoding.UTF8.GetBytes("你好，rudp")
    };
    Console.WriteLine($"TestEncode Flags:{packet.Flags} Sequence:{packet.Sequence} Payload:{Encoding.UTF8.GetString(packet.Payload)}");

    byte[] bytes = RudpPacketCodec.Encode(packet);
    return bytes;
}

static void TestDecode(byte[] bytes)
{
    RudpPacket decoded = RudpPacketCodec.Decode(bytes);
    Console.WriteLine($"TestDecode Flags:{decoded.Flags} Sequence:{decoded.Sequence} Payload:{Encoding.UTF8.GetString(decoded.Payload)}");
}
