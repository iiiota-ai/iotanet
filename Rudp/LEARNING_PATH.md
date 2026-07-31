# RUDP 学习实现路线

这份文档记录我们后续一步一步实现可靠 UDP 的学习路线。目标不是一次性写出完整协议库，而是每一步只解决一个核心问题：先理解，再实现，再验证。

本项目选择：

- 语言：C#
- 运行时：.NET 9
- 第一阶段网络 API：`UdpClient`
- 后续性能优化 API：`Socket`
- 学习方式：先做最小可运行版本，再逐步增强

## 总体路线

```text
Step 0：创建 C# 控制台项目
Step 1：理解并跑通原始 UDP 收发
Step 2：定义 RUDP packet 格式
Step 3：实现 packet encode/decode
Step 4：发送 DATA 包
Step 5：接收方回复 ACK
Step 6：发送方 timeout + retransmission
Step 7：接收方 duplicate filtering
Step 8：多消息顺序交付
Step 9：模拟丢包、重复、乱序
Step 10：sliding window
```

## Step 0：创建 C# 控制台项目

目标：准备一个可以运行实验代码的项目。

建议项目名：

```text
Rudp.Demo
```

建议命令：

```powershell
dotnet new console -n Rudp.Demo
```

验收标准：

- 项目可以 `dotnet run`
- 能打印一行普通文本
- 暂时不写任何协议代码

## Step 1：原始 UDP 收发

目标：理解 UDP 最基础的发送和接收。

需要理解：

- `UdpClient`
- `IPEndPoint`
- IP 地址
- 端口
- `127.0.0.1`
- `ReceiveAsync`
- `SendAsync`

本阶段模型：

```text
receiver: 127.0.0.1:9000
sender:   发送 bytes 到 127.0.0.1:9000
```

要实现的效果：

```text
sender 发送 "hello"
receiver 收到 "hello"
receiver 打印 sender 的地址和端口
```

验收标准：

- receiver 能绑定本地端口
- sender 能发送 UDP 数据
- receiver 能收到数据并打印
- 暂时不做可靠性

## Step 2：定义 RUDP Packet 格式

目标：把普通 UDP bytes 变成我们自己的协议包。

第一版 packet 格式：

```text
[Magic][Version][Flags][Sequence][Length][Payload]
```

建议字段：

```text
Magic    : 2 bytes
Version  : 1 byte
Flags    : 1 byte
Sequence : 4 bytes
Length   : 2 bytes
Payload  : N bytes
```

需要理解：

- 为什么需要 `Magic`
- 为什么需要 `Version`
- `Flags` 如何区分 DATA 和 ACK
- `Sequence` 为什么是可靠性的基础
- `Length` 如何帮助解析 payload

验收标准：

- 文档中明确每个字段的字节数
- 明确整数使用大端还是小端
- 明确第一版有哪些 `Flags`

## Step 3：实现 Packet Encode/Decode

目标：把结构化 packet 和 byte array 互相转换。

需要理解：

- `byte[]`
- `Span<byte>`
- `ReadOnlySpan<byte>`
- `System.Buffers.Binary.BinaryPrimitives`
- 大端序和小端序

要实现的能力：

```text
RudpPacket -> byte[]
byte[] -> RudpPacket
```

验收标准：

- DATA packet 可以编码成 bytes
- bytes 可以解码回 DATA packet
- 非法 `Magic` 会被拒绝
- 非法 `Length` 会被拒绝

## Step 4：发送 DATA 包

目标：用 UDP 发送第一种 RUDP 包。

本阶段流程：

```text
sender 创建 DATA packet
sender encode 成 bytes
sender 通过 UDP 发出
receiver 收到 bytes
receiver decode 成 DATA packet
receiver 打印 sequence 和 payload
```

验收标准：

- receiver 不再直接打印原始字符串
- receiver 先解析 RUDP packet
- 只有合法 DATA packet 才会被处理

## Step 5：接收方回复 ACK

目标：收到 DATA 后，接收方回复确认。

本阶段流程：

```text
sender   -> DATA seq=1 -> receiver
sender   <- ACK  seq=1 <- receiver
```

需要理解：

- ACK 是接收方给发送方的确认
- ACK 本身也可能丢失
- ACK packet 可以没有 payload

验收标准：

- receiver 收到 DATA 后会发送 ACK
- sender 能收到 ACK
- sender 能判断 ACK 对应哪个 sequence

## Step 6：Timeout + Retransmission

目标：发送方在没有收到 ACK 时自动重传。

本阶段流程：

```text
send DATA seq=1
wait ACK seq=1
timeout
send DATA seq=1 again
```

需要理解：

- `timeout`
- `RTT`
- `RTO`
- `max retries`
- `CancellationToken`

第一版建议：

```text
timeout = 500 ms
max retries = 5
```

验收标准：

- 正常情况下只发送一次
- ACK 丢失时会重传
- 超过最大重试次数后返回失败

## Step 7：Duplicate Filtering

目标：接收方不能把重复 DATA 交给业务层多次。

重复包出现的典型原因：

```text
receiver 收到 DATA seq=1
receiver 回复 ACK seq=1
ACK 丢失
sender 重传 DATA seq=1
receiver 再次收到 DATA seq=1
```

正确行为：

```text
重复 DATA：继续回复 ACK
重复 payload：不再交给业务层
```

验收标准：

- 同一个 sequence 的 DATA 多次到达，只交付一次
- 重复 DATA 仍然会触发 ACK

## Step 8：多消息顺序交付

目标：连续发送多条消息，接收方按顺序交付。

需要理解：

- expected sequence
- out-of-order packet
- ordered delivery

第一版策略：

```text
只接受 expected sequence
其他 sequence 先不交付
```

增强策略：

```text
缓存乱序包
缺失包到达后，连续交付缓存中的后续包
```

验收标准：

- seq=1,2,3 按顺序到达时能正常交付
- 重复 seq 不重复交付
- 乱序情况下不会错误交付

## Step 9：模拟丢包、重复、乱序

目标：主动制造网络问题，验证可靠性逻辑。

可以模拟：

- DATA 丢失
- ACK 丢失
- DATA 重复
- DATA 乱序
- 延迟

建议做一个简单的 `LossSimulator` 或开关参数。

验收标准：

- 可以指定丢包概率
- 可以让 ACK 偶尔丢失
- 可以观察到重传日志

## Step 10：Sliding Window

目标：提升吞吐量，允许多个包同时等待 ACK。

`stop-and-wait` 的问题：

```text
send 1
wait ACK 1
send 2
wait ACK 2
```

`sliding window` 的目标：

```text
send 1, 2, 3, 4
receive ACKs
slide window
send 5, 6, 7, 8
```

需要理解：

- send window
- receive window
- cumulative ACK
- selective ACK
- in-flight packets

第一版建议使用固定窗口大小：

```text
window size = 4
```

验收标准：

- 同时存在多个未确认 DATA
- ACK 到达后窗口前进
- 超时后只重传未确认包

## 后续优化方向

当以上步骤都完成以后，再考虑这些问题：

- 从 `UdpClient` 切换到 `Socket`
- 使用 `ArrayPool<byte>` 减少分配
- 使用 `Span<T>` 和 `Memory<T>` 优化编解码
- 使用动态 `RTO`
- 加入 `ConnectionId`
- 加入 handshake
- 加入 heartbeat
- 加入 close 流程
- 加入 packet authentication
- 加入性能指标和压测

## 学习节奏

每一步都按这个节奏推进：

```text
1. 先解释原理
2. 再明确本步要写什么
3. 写最小代码
4. 运行验证
5. 总结这一小步解决了什么问题
```

不要急着把所有功能一次性做完。可靠 UDP 的难点不是某一行代码，而是状态、时序和异常网络条件叠在一起以后还能保持正确。
