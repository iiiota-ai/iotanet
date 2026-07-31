using System.Net.Sockets;
using System.Text;

public static class RudpReceiver
{
    public static async Task RunAsync(bool dropFirstAck, RudpOptions options)
    {
        using var udp = new UdpClient(9000);
        Console.WriteLine($"Receiver listening on 127.0.0.1:9000");

        var receiveState = new RudpReceiveState(options.ReceiveWindowSize);
        bool firstAckDropped = false;

        void Deliver(UdpReceiveResult result, RudpPacket packet)
        {
            Console.WriteLine($"From {result.RemoteEndPoint}: {packet.Flags} {packet.Sequence} {Encoding.UTF8.GetString(packet.Payload)}");
        }

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

                uint ackSequence = receiveState.LastAckSequence;

                if(dropFirstAck && !firstAckDropped)
                {
                    firstAckDropped = true;
                    Console.WriteLine($"Simulate lost Ack seq={ackSequence}");
                    continue;
                }

                Console.WriteLine($"Receiver window expected={receiveState.ExpectedSequence}, size={receiveState.ReceiveWindowSize}");

                await RudpUdpIo.SendAckAsync(udp, result.RemoteEndPoint, ackSequence, receiveState.ReceiveWindowSize);
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
}
