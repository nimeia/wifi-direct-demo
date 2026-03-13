# Architecture

## 1. 总体结构

这个 Demo 分成两层：

1. **Wi‑Fi Direct 链路层**
   - 发现附近设备
   - 进行连接 / 入组
   - 获得组内可达 IP
2. **应用层 TCP 文本通道**
   - 在固定端口 `50001` 上建立 socket
   - 使用 JSON Lines 收发消息

## 2. Windows ↔ Windows

```text
Host(Windows)                         Client(Windows)
  |                                        |
  |-- Start advertiser --------------------|
  |-- Wait incoming request ---------------|
  |                                        |
  |<--------------- discover peers --------|
  |<--------------- connect by device id --|
  |                                        |
  |-- accept request / create WiFiDirectDevice
  |-- get endpoint pairs
  |-- start StreamSocketListener:50001
  |                                        |
  |------------------------- connect TCP ->|
  |<------------------------ JSON lines --->|
```

## 3. Windows ↔ Android

```text
Host(Windows)                         Client(Android)
  |                                        |
  |-- Start advertiser --------------------|
  |                                        |
  |<--------------- discoverPeers() -------|
  |<--------------- connect() -------------|
  |                                        |
  |-- accept request / keep GO role
  |-- start TCP server:50001
  |                                        |
  |---------------- groupOwnerAddress ---->|
  |<---------------- socket connect -------|
  |<---------------- JSON lines ---------->|
```

## 4. 协议

当前只定义三类消息：

- `hello`
- `chat`
- `ping`

字段：

- `type`
- `sender`
- `text`
- `timestampUtc`

## 5. 关键模块

### Windows
- `MainPage.xaml(.cs)`
  - 广播、发现、连接、日志、发送消息
- `WiFiDirectSession`
  - 当前示例内联在 `MainPage` 中，后续建议单独抽出
- `WiFiDirectDemo.Protocol`
  - 协议定义与 JSON Lines 编解码

### Android
- `MainActivity`
  - 权限、UI、发现、连接
- `WiFiDirectBroadcastReceiver`
  - Wi‑Fi P2P 广播接收
- `SocketSession`
  - TCP 收发

## 6. 为什么不把发现和聊天分离成更多模块

这是 Demo，不是正式产品。当前重点是：

- 让链路走通
- 让消息能收发
- 让项目能在 CI 里稳定构建

等你后续要扩展桌面分享或文件传输时，再拆成：

- DiscoveryManager
- LinkManager
- TransportManager
- SessionCoordinator
- SignalingChannel
