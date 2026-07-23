using System.Net;
using System.Net.Sockets;
using System.Text;

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

    // 循环ReceiveAsync
    while (true)
    {
        UdpReceiveResult result = await udp.ReceiveAsync();        
        string text= Encoding.UTF8.GetString(result.Buffer);
        
        // 把收到的 bytes 转字符串并打印
        Console.WriteLine($"From {result.RemoteEndPoint}: {text}");
    }
    
}

static async Task RunSender()
{
    // 创建 UdpClient
    using var udp = new UdpClient();

    // 把 "hello udp" 转成 bytes
    byte[] data = Encoding.UTF8.GetBytes("hello udp");
    var target = new IPEndPoint(IPAddress.Loopback, 9000);

    // SendAsync 到 127.0.0.1:9000
    await udp.SendAsync(data, data.Length, target);
    
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