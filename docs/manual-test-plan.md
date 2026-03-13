# Manual Test Plan

## A. Windows ↔ Windows

### A1. 建立连接
1. 在设备 A 启动 Windows Demo，点击 **Start Host**。
2. 在设备 B 启动 Windows Demo，点击 **Discover**。
3. 选择发现到的设备并点击 **Connect**。
4. 观察双方日志，确认连接建立并收到 `hello`。

### A2. 文本收发
1. 在设备 B 发送消息。
2. 设备 A 应收到 `chat`。
3. 在设备 A 回复消息。
4. 设备 B 应收到 `chat`。

### A3. 断开
1. 在任意一端关闭应用或断开 Wi‑Fi Direct。
2. 另一端应输出断连日志。

## B. Windows ↔ Android

### B1. 权限
1. Android 首次启动时授予附近 Wi‑Fi / 位置相关权限。

### B2. 建立连接
1. Windows 端点击 **Start Host**。
2. Android 端点击 **Discover Peers**。
3. 选中 Windows 设备并连接。
4. 连接成功后 Android 应自动根据 `WifiP2pInfo` 决定是否作为客户端连接到 GO 地址。

### B3. 文本收发
1. Android 发送一条消息到 Windows。
2. Windows 回复一条消息到 Android。

## C. 失败场景

- Host 未启动广播时，Client 不应发现 peer。
- 权限被拒绝时，Android 应在日志中提示。
- 端口被占用时，应打印 socket 启动失败。
- 网卡/驱动不支持 Wi‑Fi Direct 时，应明确提示。
