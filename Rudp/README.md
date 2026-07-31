# RUDP：可靠 UDP 学习笔记

这个目录计划用来实现一个可靠 UDP 协议。在写代码之前，我们先把协议拆成一小步一小步来理解。每一步都应该足够小，方便解释、测试和替换。

## 1. UDP 提供了什么

UDP 发送的是一个个独立的数据报。

```text
应用层字节
  -> UDP datagram
  -> IP 网络
```

UDP 不保证：

- 一定送达
- 按顺序送达
- 不重复送达
- 自动重传
- 流量控制
- 拥塞控制
- 连接状态

可靠 UDP 的意思是：在 UDP 之上，由我们自己的应用层协议补上部分可靠性能力。

## 2. 核心思想

可靠 UDP 不是操作系统额外提供的一种新传输协议，而是放在 UDP 负载里的自定义协议。

```text
RUDP packet header + payload
  -> UDP
  -> IP
  -> 网络
```

第一个可用版本只需要理解几个概念：

- packet format
- sequence number
- ACK
- timeout
- retransmission
- duplicate filtering

## 3. 数据包格式

UDP 只帮我们传输一段字节。至于这段字节是什么意思，需要我们自己定义。

一个教学版数据包头可以长这样：

```text
[Magic][Version][Flags][ConnectionId][Sequence][Ack][Length][Payload]
```

常见字段：

- `Magic`：识别这是我们协议的数据包。
- `Version`：协议版本，方便未来升级。
- `Flags`：包类型，例如 DATA、ACK、SYN、FIN。
- `ConnectionId`：区分不同逻辑连接。
- `Sequence`：当前数据包的序列号。
- `Ack`：确认已经收到的数据。
- `Length`：负载长度。
- `Payload`：真正的业务数据。

第一版可以先用更小的格式：

```text
[Magic][Version][Flags][Sequence][Length][Payload]
```

等可靠收发跑通以后，再加入 `ConnectionId`。

## 4. 序列号

`sequence number` 用来让接收方识别数据包的身份和顺序。

例如：

```text
seq=1: hello
seq=2: world
seq=3: !
```

它可以帮助判断：

- 哪些包丢了
- 哪些包重复了
- 哪些包乱序了
- 下一个应该交付的包是谁

没有序列号，就无法实现可靠交付。

## 5. ACK

`ACK` 是 acknowledgement，也就是确认。

接收方收到 DATA 包后，需要给发送方回一个 ACK 包：

```text
sender   -> DATA seq=1 -> receiver
sender   <- ACK  seq=1 <- receiver
```

发送方收到 ACK 以后，就知道这个包已经到达，可以停止追踪它。

## 6. 超时与重传

数据包可能丢，ACK 也可能丢。发送方无法直接知道到底发生了什么，所以只能通过超时来判断。

```text
发送 DATA seq=1
等待 ACK seq=1
超时
再次发送 DATA seq=1
```

重要术语：

- `RTT`：round-trip time，往返时间。
- `RTO`：retransmission timeout，重传超时时间。
- `max retries`：最大重试次数。
- `backoff`：退避策略，连续失败后延长等待时间。

第一版可以先使用固定超时时间，例如 500 ms。后续再根据 `RTT` 动态调整 `RTO`。

## 7. 去重

重传会带来重复包。

例如：

```text
receiver 收到 DATA seq=1
ACK 丢失
sender 重传 DATA seq=1
receiver 再次收到 DATA seq=1
```

接收方遇到重复包时，应该再次回复 ACK，但不能把重复的 `Payload` 交给业务层两次。

## 8. 顺序交付

UDP 包可能乱序到达：

```text
先收到 seq=3
再收到 seq=1
再收到 seq=2
```

简单第一版：

- 只接受下一个期望的 `sequence number`
- 对合法包回复 ACK
- 对太靠后的包先忽略或拒绝

增强版：

- 缓存乱序包
- 等缺失的包到达后，再按顺序交付

## 9. 滑动窗口

每发一个包就等一个 ACK，可靠但很慢。

`stop-and-wait`：

```text
send 1
wait ACK 1
send 2
wait ACK 2
```

`sliding window`：

```text
send 1, 2, 3, 4
receive ACKs
slide window forward
send 5, 6, 7, 8
```

`sliding window` 可以提高吞吐量。我们应该先把简单的 ACK/重传流程跑通，再加入窗口。

## 10. MTU 与分片

UDP 数据报不应该太大，否则容易触发 IP 分片。

一个实用的经验值是：

```text
UDP payload <= 1200 bytes
```

更大的消息应该在 RUDP 层自己拆分：

```text
message
  -> chunk seq=1
  -> chunk seq=2
  -> chunk seq=3
```

第一版先不做分片，只发送小消息。

## 11. 连接状态

UDP 本身是无连接的，但可靠 UDP 通常会模拟逻辑连接。

一个连接可以记录：

- 远端地址
- `ConnectionId`
- 下一个要发送的 `sequence number`
- 下一个期望接收的 `sequence number`
- 尚未确认的数据包
- 最后一次收到数据的时间
- 是否正在关闭

第一版可以先绑定一个发送方和一个接收方，后面再加入连接管理。

## 12. 最小实现顺序

我们后续按这个顺序实现：

1. 创建一个小型 C# 控制台项目。
2. 本地发送和接收原始 UDP 数据报。
3. 定义包类型和数据包编解码逻辑。
4. 发送一个带 `sequence number` 的 DATA 包。
5. 接收方回复 ACK 包。
6. 加入发送方超时与重传。
7. 加入接收方去重。
8. 加入连续消息的顺序交付。
9. 加入可以模拟丢包的测试或演示命令。
10. 在 `stop-and-wait` 清楚以后，再加入 `sliding window`。

## 13. 版本路线

### Version 0：原始 UDP

目标：理解 `UdpClient`、端点、发送、接收。

### Version 1：Stop-And-Wait 可靠性

目标：一个 DATA 包等待一个 ACK。

包含：

- `sequence number`
- ACK
- timeout
- retransmission
- duplicate filtering

### Version 2：有序消息流

目标：发送多条消息，并按顺序交付。

包含：

- 期望接收的 `sequence number`
- 缓存乱序包，或先采用严格顺序接收

### Version 3：Sliding Window

目标：允许多个数据包同时在路上。

包含：

- send window
- receive window
- cumulative ACK 或 selective ACK

### Version 4：会话管理

目标：在 UDP 上模拟逻辑连接。

包含：

- `ConnectionId`
- handshake
- heartbeat
- close
- idle timeout

### Version 5：生产级问题

目标：让协议更健壮。

包含：

- dynamic RTO
- congestion awareness
- packet authentication
- replay protection
- metrics
- packet loss testing

## 14. 需要熟悉的 C# API

后续会用到这些 .NET 类型：

- `System.Net.Sockets.UdpClient`
- `System.Net.Sockets.Socket`
- `System.Net.IPEndPoint`
- `System.Net.IPAddress`
- `System.Threading.CancellationToken`
- `System.Threading.Tasks.Task`
- `System.Diagnostics.Stopwatch`
- `System.Collections.Concurrent.ConcurrentDictionary<TKey, TValue>`

我们会先使用 `UdpClient`，因为它更适合学习。等需要更多控制时，再靠近底层的 `Socket`。

## 15. 一句话总结

可靠 UDP 的本质，是在 UDP 之上增加协议规则：

```text
UDP 不可靠
  -> 增加 packet header
  -> 增加 sequence number
  -> 增加 ACK
  -> 增加 timeout
  -> 增加 retransmission
  -> 过滤 duplicate packet
  -> 按顺序交付数据
```

第一个工作里程碑应该非常简单：

```text
DATA seq=N
ACK seq=N
持续重传 DATA seq=N，直到收到 ACK 或超过重试次数
```
