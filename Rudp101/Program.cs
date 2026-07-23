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
    // TODO: 创建UdpClient，绑定9000端口
    // TODO: 循环ReceiveAsync
    // TODO: 把收到的 bytes 转字符串并打印
}
else if(args[0] == "sender")
{
    // TODO: 创建 UdpClient
    // TODO: 把 "hello udp" 转成 bytes
    // TODO: SendAsync 到 127.0.0.1:9000
}