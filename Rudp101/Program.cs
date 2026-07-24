using System.Net;
using System.Net.Sockets;
using System.Text;

var options = new RudpOptions();

if(args.Length == 0)
{
    Console.WriteLine($"usage: dotnet run -- receiver|sender");
    return;
}

if(args[0] == "receiver")
{
    // 是否模拟丢弃ACK包
    bool dropFirstAck = args.Length > 1 && args[1] == "dropack";
    await RunReceiver(dropFirstAck);
}
else if(args[0] == "sender")
{
    // 是否模拟丢弃DATA包
    bool dropFirstData = args.Length > 1 && args[1] == "dropfirstdata";
    // 是否模拟乱序发送
    bool reorder = args.Length > 1 && args[1] == "reorder";
    // 是否使用不等待立即的发送方式
    bool fireOrder = args.Length > 1 && args[1] == "fireorder";
    // 是否使用滑动窗口发送
    bool window = args.Length > 1 && args[1] == "window";

    if (window)
    {
        // 模拟是否首次丢弃第二个包
        uint? dropWindowSequence = null;
        if(args.Length > 2 && args[2].StartsWith("dropseq"))
        {
            string seqText = args[2]["dropseq".Length..];
            if(uint.TryParse(seqText, out uint seq))
            {
                dropWindowSequence = seq;
            }
        }

        var ok = await WindowSender(dropWindowSequence, options);
        if (!ok)
        {
            Console.WriteLine($"Window sender failed.");
        }
    }
    else if (fireOrder)
    {
        await FireOrderSender();
    }
    else
    {
        await RunSender(dropFirstData, reorder, options);
    }
}
else if(args[0] == "testcodec")
{
    var bytes = TestEncode();
    TestDecode(bytes);
}

static async Task RunReceiver(bool dropFirstAck)
{
    // 创建UdpClient，绑定9000端口
    using var udp = new UdpClient(9000);    
    Console.WriteLine($"Receiver listening on 127.0.0.1:9000");

    // 期望接收的包序号
    uint expectedSequence = 1;
    // 缓存乱序包，表示提前到达但还不能交付
    var pendingPackets = new Dictionary<uint, RudpPacket>();
    // 模拟丢弃ACK包
    bool firstAckDropped = false;

    // 交付给业务层
    void Deliver(UdpReceiveResult result, RudpPacket packet)
    {
        Console.WriteLine($"From {result.RemoteEndPoint}: {packet.Flags} {packet.Sequence} {Encoding.UTF8.GetString(packet.Payload)}");
    }

    // 循环ReceiveAsync
    while (true)
    {
        UdpReceiveResult result;

        try
        {
            result = await udp.ReceiveAsync();
        }
        catch (SocketException ex) when (
            ex.SocketErrorCode == SocketError.ConnectionReset ||
            ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            Console.WriteLine($"Receive failed: {ex.SocketErrorCode}");
            continue;
        }

        try
        {
            var packet = RudpPacketCodec.Decode(result.Buffer);
            if(packet.Flags != PacketFlags.Data)
            {
                Console.WriteLine($"Drop non-Data packet from {result.RemoteEndPoint}: {packet.Flags}");
                continue;
            }

            // 旧包(重复包)已接收过，丢弃但仍ACK
            if(packet.Sequence < expectedSequence)
            {
                Console.WriteLine($"Duplicate DATA seq={packet.Sequence}, payload ignored");
            }
            // 只交付预期包，并且查看缓存中是否有下一seq包
            else if(packet.Sequence == expectedSequence)
            {
                Deliver(result, packet);
                expectedSequence++;

                while(pendingPackets.Remove(expectedSequence, out RudpPacket? pendingPacket))
                {
                    Deliver(result, pendingPacket);
                    expectedSequence++;
                }
            }
            // 乱序包进行缓存
            else
            {
                if(pendingPackets.TryAdd(packet.Sequence, packet))
                {
                    Console.WriteLine($"Buffered out-of-order DATA seq={packet.Sequence}, expected={expectedSequence}");
                }
                else
                {
                    Console.WriteLine($"Duplicate buffered DATA seq={packet.Sequence}, payload ignored");
                }
            }
            
            // 确认已ACK的包
            uint ackSequence = expectedSequence - 1;

            // 模拟首个 ACK 丢失，让 sender 超时重传
            if(dropFirstAck && !firstAckDropped)
            {
                firstAckDropped = true;
                Console.WriteLine($"Simulate lost Ack seq={ackSequence}");
                continue;
            }

            // Ack确认包
            var ack = new RudpPacket
            {
                Flags = PacketFlags.Ack,
                Sequence = ackSequence,
                Payload = Array.Empty<Byte>()
            };
            byte[] ackBytes = RudpPacketCodec.Encode(ack);
            await udp.SendAsync(ackBytes, ackBytes.Length, result.RemoteEndPoint);
            Console.WriteLine($"Sent ACK seq={ack.Sequence} to {result.RemoteEndPoint}");
        }
        catch(SocketException ex) when(
            ex.SocketErrorCode == SocketError.ConnectionReset ||
            ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            Console.WriteLine($"Failed to send ACK to {result.RemoteEndPoint}: {ex.SocketErrorCode}");
        }
        catch(ArgumentException ex)
        {
            Console.WriteLine($"Drop invalid packet from {result.RemoteEndPoint}: {ex.Message}");
        }
    }    
}

static async Task RunSender(bool dropFirstData, bool reorder, RudpOptions options)
{    
    // 创建 UdpClient，并绑定一个系统分配的本地临时端口。
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
        var packet = new RudpPacket
        {
            Flags = PacketFlags.Data,
            Sequence = newSeq,
            Payload = Encoding.UTF8.GetBytes($"message {newSeq}")
        };
        // 模拟丢弃第一个DATA包
        var shouldDropFirstData = dropFirstData && !firstDataDropped;
        if (shouldDropFirstData)
        {
            firstDataDropped = true;
        }

        bool ok = await SendWithRetry(udp, target, packet, shouldDropFirstData, options);
        if (!ok)
        {
            Console.WriteLine($"Stop sending because seq={packet.Sequence} failed");
            return;
        }
    }
}

static async Task FireOrderSender()
{
    using var udp = new UdpClient(0);
    var target = new IPEndPoint(IPAddress.Loopback, 9000);

    uint[] order = [1, 3, 2];
    foreach(uint seq in order)
    {
        var packet = new RudpPacket
        {
            Flags = PacketFlags.Data,
            Sequence = seq,
            Payload = Encoding.UTF8.GetBytes($"message {seq}")
        };

        byte[] data = RudpPacketCodec.Encode(packet);

        await udp.SendAsync(data, data.Length, target);

        Console.WriteLine($"Fire DATA seq={seq}");

        await Task.Delay(100);
    }
}

static async Task<bool> WindowSender(uint? dropFirstSendOfSequence, RudpOptions options)
{
    using var udp = new UdpClient(0);
    var target = new IPEndPoint(IPAddress.Loopback, 9000);
    
    // 窗口范围：[baseSequence, baseSequence + windowSize)

    // 总消息数量
    const int totalMessages = 6;
    // 固定窗口大小
    const int windowSize = 3;

    // 当前窗口做左边，最早未确认交付的包
    uint baseSequence = 1;
    // 下一个可发送的包
    uint nextSequence = 1;

    // 缓存已发送但可能需要重传的包
    var sentPackets = new Dictionary<uint, byte[]>();

    // 模拟丢包是否已发生一次
    bool windowDropConsumed = false;

    // 连续超时计数器
    int consecutiveTimeouts = 0;

    // 还有未读消息
    while(baseSequence <= totalMessages)
    {
        // 尽量填满窗口
        while(nextSequence <= totalMessages && nextSequence < baseSequence + windowSize)
        {
            var packet = new RudpPacket
            {
                Flags = PacketFlags.Data,
                Sequence = nextSequence,
                Payload = Encoding.UTF8.GetBytes($"message {nextSequence}")
            };

            byte[] data = RudpPacketCodec.Encode(packet);

            // 缓存已发送的包
            sentPackets[nextSequence] = data;

            // 模拟丢包
            bool shouldDrop = dropFirstSendOfSequence.HasValue && !windowDropConsumed && nextSequence == dropFirstSendOfSequence.Value;
            if(shouldDrop)
            {
                windowDropConsumed = true;
                Console.WriteLine($"Window simulate lost DATA seq={nextSequence}");
            }
            else
            {
                await udp.SendAsync(data, data.Length, target);
                Console.WriteLine($"window send DATA seq={nextSequence}");
            }
            nextSequence++;
        }

        // 等ACK或超时
        try
        {
            using var timeoutCts = new CancellationTokenSource(options.TimeoutMs);

            UdpReceiveResult result = await udp.ReceiveAsync(timeoutCts.Token);
            RudpPacket ack = RudpPacketCodec.Decode(result.Buffer);

            if(ack.Flags != PacketFlags.Ack)
            {
                Console.WriteLine($"window received non-ACK: {ack.Flags}");
                continue;
            }

            Console.WriteLine($"Window received ACK seq={ack.Sequence}");

            if(ack.Sequence < baseSequence)
            {
                Console.WriteLine($"Window duplicate ACK seq={ack.Sequence}, base={baseSequence}");
                continue;
            }

            // 移动窗口左侧，已确认交付指针
            uint oldBase = baseSequence;
            baseSequence = ack.Sequence + 1;
            // 窗口移动则重置
            consecutiveTimeouts = 0;
            
            for(uint seq = oldBase; seq < baseSequence; seq++)
            {
                sentPackets.Remove(seq);
            }
        }
        catch(OperationCanceledException)
        {
            // 超时则累加
            consecutiveTimeouts++;

            if(consecutiveTimeouts >= options.MaxRetries)
            {
                Console.WriteLine($"Window failed after {options.MaxRetries} consecutive timeouts");
                return false;
            }

            Console.WriteLine($"Window timeout, resend from seq={baseSequence}");
            
            foreach(var item in sentPackets.OrderBy(x => x.Key))
            {
                await udp.SendAsync(item.Value, item.Value.Length, target);
                Console.WriteLine($"Window resend DATA seq={item.Key}");
            }
        }
    }
    
    Console.WriteLine($"Window sender completed.");
    await Task.Delay(500);
    return true;
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

static async Task<bool> SendWithRetry(UdpClient udp, IPEndPoint target, RudpPacket packet, bool dropFirstData, RudpOptions options)
{
    byte[] data = RudpPacketCodec.Encode(packet);
    // 模拟丢弃DATA包
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
            await udp.SendAsync(data, data.Length, target);
            Console.WriteLine($"Sent DATA seq={packet.Sequence}, attempt={attempt}");
        }

        try
        {
            using var timeoutCts = new CancellationTokenSource(options.TimeoutMs);

            UdpReceiveResult result = await udp.ReceiveAsync(timeoutCts.Token);
            RudpPacket ack = RudpPacketCodec.Decode(result.Buffer);

            if(ack.Flags == PacketFlags.Ack && ack.Sequence == packet.Sequence)
            {
                Console.WriteLine($"Received ACK seq={ack.Sequence} from {result.RemoteEndPoint}");
                return true;
            }

            Console.WriteLine($"Received unexpected packet: {ack.Flags} seq={ack.Sequence}");
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
