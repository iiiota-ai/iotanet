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

        var result = await WindowSender(dropWindowSequence, options);
        Console.WriteLine(result.Message);
        if (!result.Success)
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

    var receiveState = new RudpReceiveState();

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

            IReadOnlyList<RudpPacket> deliveredPackets = receiveState.Accept(packet);

            if(deliveredPackets.Count == 0)
            {
                Console.WriteLine($"DATA seq={packet.Sequence} not delivered, ACK={receiveState.LastAckSequence}");
            }
            else
            {
                foreach(RudpPacket deliveredPacket in deliveredPackets)
                {
                    Deliver(result, deliveredPacket);
                }
            }
            
            // 确认已ACK的包
            uint ackSequence = receiveState.LastAckSequence;

            // 模拟首个 ACK 丢失，让 sender 超时重传
            if(dropFirstAck && !firstAckDropped)
            {
                firstAckDropped = true;
                Console.WriteLine($"Simulate lost Ack seq={ackSequence}");
                continue;
            }

            // Ack确认
            await SendAck(udp, result.RemoteEndPoint, ackSequence);
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

static async Task SendData(UdpClient udp, IPEndPoint target, RudpPacket packet)
{
    byte[] data = RudpPacketCodec.Encode(packet);
    await SendEncodedData(udp, target, packet.Sequence, data, "Sent");
}

static async Task SendEncodedData(UdpClient udp, IPEndPoint target, uint sequence, byte[] data, string prefix)
{
    await udp.SendAsync(data, data.Length, target);
    Console.WriteLine($"{prefix} DATA seq={sequence}");
}

static async Task SendAck(UdpClient udp, IPEndPoint remoteEndPoint, uint ackSequence)
{
    var ack = RudpPacket.Ack(ackSequence);
    byte[] ackBytes = RudpPacketCodec.Encode(ack);

    await udp.SendAsync(ackBytes, ackBytes.Length, remoteEndPoint);
    Console.WriteLine($"Sent ACK seq={ack.Sequence} to {remoteEndPoint}");
}

static async Task<RudpPacket?> ReceiveAck(UdpClient udp, int timeoutMs)
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
        
        var payload = Encoding.UTF8.GetBytes($"message {newSeq}");
        var packet = RudpPacket.Data(newSeq, payload);
        
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
        var payload = Encoding.UTF8.GetBytes($"message {seq}");
        var packet = RudpPacket.Data(seq, payload);

        await SendData(udp, target, packet);
        await Task.Delay(100);
    }
}

static async Task<RudpSendResult> WindowSender(uint? dropFirstSendOfSequence, RudpOptions options)
{
    using var udp = new UdpClient(0);
    var target = new IPEndPoint(IPAddress.Loopback, 9000);

    var window = new RudpSendWindow(options.TotalMessages, options.WindowSize);

    // 模拟丢包是否已发生一次
    bool windowDropConsumed = false;

    // 连续超时计数器
    int consecutiveTimeouts = 0;

    // 还有未发消息
    while(!window.IsCompleted)
    {
        // 尽量填满窗口
        while(window.CanSend)
        {
            var sequence = window.NextSequence;

            var payload = Encoding.UTF8.GetBytes($"message {sequence}");
            var packet = RudpPacket.Data(sequence, payload);

            byte[] data = RudpPacketCodec.Encode(packet);

            window.MarkSent(packet.Sequence, data);

            // 模拟丢包
            bool shouldDrop = dropFirstSendOfSequence.HasValue && !windowDropConsumed && sequence == dropFirstSendOfSequence.Value;
            if(shouldDrop)
            {
                windowDropConsumed = true;
                Console.WriteLine($"Window simulate lost DATA seq={sequence}");
            }
            else
            {
                await SendData(udp, target, packet);
            }
        }

        // 等ACK或超时
        try
        {
            RudpPacket? packet = await ReceiveAck(udp, options.TimeoutMs);
            
            if(packet is null)
            {
                continue;
            }

            Console.WriteLine($"Window received ACK seq={packet.Sequence}");

            if (window.TryAck(packet.Sequence))
            {
                // 窗口移动则重置
                consecutiveTimeouts = 0;
            }
            else
            {
                Console.WriteLine($"Window duplicate ACK seq={packet.Sequence}, base={window.BaseSequence}");
                continue;
            }
        }
        catch(OperationCanceledException)
        {
            // 超时则累加
            consecutiveTimeouts++;

            if(consecutiveTimeouts >= options.MaxRetries)
            {
                return RudpSendResult.Fail($"Window failed after {options.MaxRetries} consecutive timeouts");
            }

            Console.WriteLine($"Window timeout, resend from seq={window.BaseSequence}");

            var retryPackets = window.GetPacketsForRetransmit();
            
            foreach(var item in retryPackets)
            {
                await SendEncodedData(udp, target, item.Key, item.Value, "Window resend");
            }
        }
    }

    await Task.Delay(500);
    return RudpSendResult.Ok("Window sender completed.");
}

static byte[] TestEncode()
{
    var payload = Encoding.UTF8.GetBytes("你好，rudp");
    var packet = RudpPacket.Data(1, payload);

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
            await SendEncodedData(udp, target, packet.Sequence, data, $"Sent attempt={attempt}");
        }

        try
        {
            RudpPacket? ack = await ReceiveAck(udp, options.TimeoutMs);

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
