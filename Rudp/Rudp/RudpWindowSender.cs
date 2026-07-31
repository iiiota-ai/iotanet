using System.Net;
using System.Net.Sockets;

public static class RudpWindowSender
{
    public static async Task<RudpSendResult> RunAsync(uint? dropFirstSendOfSequence, RudpOptions options)
    {
        using var udp = new UdpClient(0);
        var target = new IPEndPoint(IPAddress.Loopback, 9000);

        var window = new RudpSendWindow(options.TotalMessages, options.WindowSize);

        bool windowDropConsumed = false;
        int consecutiveTimeouts = 0;
        uint? lastDuplicateAck = null;
        int duplicateAckCount = 0;
        uint advertisedReceiveWindow = options.WindowSize;
        double currentRtoMs = options.TimeoutMs;
        double? srttMs = null;
        double? rttVarMs = null;
        uint congestionWindow = 1;

        const double alpha = 1.0 / 8.0;
        const double beta = 1.0 / 4.0;

        while(!window.IsCompleted)
        {
            uint effectiveWindow = Math.Min(advertisedReceiveWindow, congestionWindow);

            while(window.CanSend && window.InFlightCount < effectiveWindow)
            {
                var sequence = window.NextSequence;
                var packet = DemoPayload.CreateMessagePacket(sequence);
                byte[] data = RudpPacketCodec.Encode(packet);

                window.MarkSent(packet.Sequence, data);

                bool shouldDrop = dropFirstSendOfSequence.HasValue &&
                    !windowDropConsumed &&
                    sequence == dropFirstSendOfSequence.Value;

                if(shouldDrop)
                {
                    windowDropConsumed = true;
                    Console.WriteLine($"Window simulate lost DATA seq={sequence}");
                }
                else
                {
                    await RudpUdpIo.SendDataAsync(udp, target, packet);
                }
            }

            try
            {
                RudpPacket? packet = await RudpUdpIo.ReceiveAckAsync(udp, (int)currentRtoMs);

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
                    consecutiveTimeouts = 0;
                    lastDuplicateAck = null;
                    duplicateAckCount = 0;

                    congestionWindow++;
                    Console.WriteLine($"Congestion cwnd={congestionWindow}");
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
                            await RudpUdpIo.SendEncodedDataAsync(udp, target, window.BaseSequence, data, "Window fast resend");
                            window.MarkRetransmitted(window.BaseSequence);
                        }

                        duplicateAckCount = 0;
                    }
                    continue;
                }
            }
            catch(OperationCanceledException)
            {
                consecutiveTimeouts++;

                congestionWindow = 1;
                Console.WriteLine($"Congestion timeout, cwnd={congestionWindow}");

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
                    await RudpUdpIo.SendEncodedDataAsync(udp, target, item.Key, item.Value, "Window resend");
                    window.MarkRetransmitted(item.Key);
                }
            }
        }

        await Task.Delay(500);
        return RudpSendResult.Ok("Window sender completed.");
    }
}
