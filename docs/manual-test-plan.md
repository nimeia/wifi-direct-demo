# Manual Test Plan

## A. Windows ↔ Windows

### A1. 建立连接
1. 在设备 A 启动 Windows Demo，点击 **Start Host**。
2. 在设备 B 启动 Windows Demo，点击 **Discover**。
3. 选择发现到的设备并点击 **Connect Selected Peer**。
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

## D. 端口开放（Windows Port Access）

### D1. 启动本机目标服务
1. 在 Windows 机器启动一个本机服务，例如 `python -m http.server 8080`。
2. 本机浏览器访问 `http://127.0.0.1:8080`，确认服务可用。

### D2. 启动端口开放
1. 在 Windows Demo 的 **Port Access Configuration** 设置：
2. `Ingress Port = 51080`
3. `Target Host = 127.0.0.1`
4. `Target Port = 8080`
5. `Allowed Target Ports = 8080, 8443`（至少包含 8080）
6. 点击 **Start Port Access**，确认日志出现 `Port access started: 51080 -> 127.0.0.1:8080`。

### D3. 远端验证转发
1. 让远端设备先与 Windows 建立 Wi‑Fi Direct 连接。
2. 在远端访问 `http://<Windows-WiFiDirect-IP>:51080`。
3. 预期结果：可返回本机 `8080` 服务内容。

### D4. 停止与安全校验
1. 点击 **Stop**。
2. 再次访问 `51080` 应连接失败。
3. 将 `Target Host` 设为非本机地址时，预期 UI 日志拒绝启动（仅允许 localhost）。
4. 将 `Target Port = 3306` 且白名单不包含 `3306` 时，预期 UI 日志拒绝启动。

## E. Android 端口访问测试面板

### E1. 前置连接
1. 先完成 B2，确保 Android 与 Windows 已建立 Wi‑Fi Direct 连接。
2. Android 会在连接信息回调后自动填充 `Host`（组主地址），也可手工改写。

### E2. TCP 连通性探测
1. 设置 `Port = 51080`。
2. 点击 **TCP Test**。
3. 预期日志显示 `connected in ...ms`。

### E3. HTTP 测试
1. 设置 `Path = /`。
2. 点击 **HTTP Test**。
3. 预期日志显示 `status=200`（或服务实际返回码）及响应摘要。
