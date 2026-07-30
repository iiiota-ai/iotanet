using System.Net;
using System.Net.Sockets;
using System.Text;

var options = new RudpOptions();
var command = DemoCommand.Parse(args);

if (string.IsNullOrEmpty(command.Mode))
{
    Console.WriteLine("usage: dotnet run -- receiver|sender");
    return;
}

if(command.Mode == "receiver")
{
    // 是否模拟丢弃ACK包
    await RunReceiver(command.DropFirstAck, options);
}
else if(command.Mode == "sender")
{
    if (command.Window)
    {
        var result = await WindowSender(command.DropWindowSequence, options);
        Console.WriteLine(result.Message);
        if (!result.Success)
        {
            Console.WriteLine($"Window sender failed.");
        }
    }
    else if (command.FireOrder)
    {
        await FireOrderSender();
    }
    else
    {
        await RunSender(command.DropFirstData, command.Reorder, options);
    }
}
else if(command.TestCodec)
{
    var bytes = TestEncode();
    TestDecode(bytes);
}

static async Task RunReceiver(bool dropFirstAck, RudpOptions options)
{
    // 创建UdpClient，绑定9000端口
    using var udp = new UdpClient(9000);    
    Console.WriteLine($"Receiver listening on 127.0.0.1:9000");

    var receiveState = new RudpReceiveState(options.ReceiveWindowSize);

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

            Console.WriteLine($"Receiver window expected={receiveState.ExpectedSequence}, size={receiveState.ReceiveWindowSize}");

            // Ack确认
            await SendAck(udp, result.RemoteEndPoint, ackSequence, receiveState.ReceiveWindowSize);
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

static async Task SendAck(UdpClient udp, IPEndPoint remoteEndPoint, uint ackSequence, uint receiveWindow)
{
    var ack = RudpPacket.Ack(ackSequence, receiveWindow);
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
        
        var packet = DemoPayload.CreateMessagePacket(newSeq);
        
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
        var packet = DemoPayload.CreateMessagePacket(seq);

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

    uint? lastDuplicateAck = null;
    int duplicateAckCount = 0;
    
    uint advertisedReceiveWindow = options.WindowSize;

    // retransmission timeout
    double currentRtoMs = options.TimeoutMs;
    // 平滑RTT
    double? srttMs = null;
    // RTT偏差
    double? rttVarMs = null;

    const double alpha = 1.0 / 8.0;
    const double beta = 1.0 / 4.0;

    // 还有未发消息
    while(!window.IsCompleted)
    {
        // 尽量填满窗口
        while(window.CanSend && window.InFlightCount < advertisedReceiveWindow)
        {
            var sequence = window.NextSequence;
            var packet = DemoPayload.CreateMessagePacket(sequence);
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
            RudpPacket? packet = await ReceiveAck(udp, (int)currentRtoMs);
            
            if(packet is null)
            {
                continue;
            }
            
            advertisedReceiveWindow = packet.ReceiveWindow;

            Console.WriteLine($"Window received ACK seq={packet.Sequence}, rwnd={packet.ReceiveWindow}, inFlight={window.InFlightCount}");

            uint ackSequence = packet.Sequence;
            uint rttSequence = window.BaseSequence;

            if(window.CanSampleRtt(rttSequence) && window.TryGetSentAt(rttSequence, out DateTimeOffset sentAt))
            {
                var rtt = DateTimeOffset.UtcNow - sentAt;
                double sampleRttMs = rtt.TotalMilliseconds;

                if(srttMs is null || rttVarMs is null)
                {
                    srttMs = sampleRttMs;
                    rttVarMs = sampleRttMs / 2;
                }
                else
                {
                    rttVarMs = (1 - beta) * rttVarMs.Value + beta * Math.Abs(srttMs.Value - sampleRttMs);
                    srttMs = (1 - alpha) * srttMs.Value + alpha * sampleRttMs;
                }

                currentRtoMs = srttMs.Value + 4 * rttVarMs.Value;
                currentRtoMs = Math.Clamp(currentRtoMs, 100, 3000);
                Console.WriteLine($"RTT seq={rttSequence}, sample={sampleRttMs:F0}, srtt={srttMs:F0}, rttvar={rttVarMs:F0}, rto={currentRtoMs:F0}");
            }
            else
            {
                Console.WriteLine($"Skip RTT sample seq={rttSequence}");
            }

            if (window.TryAck(ackSequence))
            {
                // 窗口移动则重置
                consecutiveTimeouts = 0;
                lastDuplicateAck = null;
                duplicateAckCount = 0;
            }
            else
            {
                if(lastDuplicateAck == packet.Sequence)
                {
                    duplicateAckCount++;
                }
                else
                {
                    lastDuplicateAck = packet.Sequence;
                    duplicateAckCount = 1;
                }
                Console.WriteLine($"Window duplicate ACK seq={packet.Sequence}, count={duplicateAckCount}, base={window.BaseSequence}");
                
                if(duplicateAckCount >= options.FastRetransmitDuplicateAckThreshold)
                {
                    Console.WriteLine($"Window fast retransmit from seq={window.BaseSequence}");

                    if(window.TryGetSentPacket(window.BaseSequence, out byte[]? data) && data is not null)
                    {
                        await SendEncodedData(udp, target, window.BaseSequence, data, "Window fast resend");
                        window.MarkRetransmitted(window.BaseSequence);
                    }

                    duplicateAckCount = 0;
                }
                continue;
            }
        }
        catch(OperationCanceledException)
        {
            // 超时则累加
            consecutiveTimeouts++;

            currentRtoMs = Math.Min(currentRtoMs * 2, 3000);
            Console.WriteLine($"RTO backoff, rto={currentRtoMs:F0}");

            if(consecutiveTimeouts >= options.MaxRetries)
            {
                return RudpSendResult.Fail($"Window failed after {options.MaxRetries} consecutive timeouts");
            }

            Console.WriteLine($"Window timeout, resend from seq={window.BaseSequence}");

            var retryPackets = window.GetPacketsForRetransmit();
            
            foreach(var item in retryPackets)
            {
                await SendEncodedData(udp, target, item.Key, item.Value, "Window resend");
                window.MarkRetransmitted(item.Key);
            }
        }
    }

    await Task.Delay(500);
    return RudpSendResult.Ok("Window sender completed.");
}

static byte[] TestEncode()
{
    var packet = DemoPayload.CreateDataPacket(1, "你好，rudp");

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
