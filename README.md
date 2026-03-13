# Wi-Fi Direct Demo

这是一个最小可读的跨端演示仓库，用来说明 **Wi-Fi Direct 在 Windows ↔ Windows、Windows ↔ Android** 下的基本使用方式。

目标不是做一个可上线产品，而是提供一套：

- 能读懂的最小架构
- 能在 GitHub Actions 中完成构建的工程骨架
- 能在实体设备上手工验证的 Demo 主流程
- 能继续扩展到文件传输、桌面分享、控制通道的基础协议

## 仓库结构

```text
.
├─ .github/workflows/
│  ├─ windows-demo.yml
│  └─ android-demo.yml
├─ docs/
│  ├─ architecture.md
│  ├─ manual-test-plan.md
│  └─ status.md
├─ windows/
│  ├─ WiFiDirectDemo.sln
│  ├─ WiFiDirectDemo.Protocol/
│  ├─ WiFiDirectDemo.Protocol.Tests/
│  └─ WiFiDirectDemo.Windows/
└─ android/
   └─ app/
```

## Demo 设计取舍

这版 Demo 采用 **“Host / Client”** 模型，而不是做全自动、全对等组网：

- **Host**
  - Windows：使用 `WiFiDirectAdvertisementPublisher` 发布 Wi‑Fi Direct 广播，并开启 TCP 文本服务器。
  - Android：在连上后，如果自己成为 GO，则开启 TCP 文本服务器。
- **Client**
  - Windows：发现附近 Wi‑Fi Direct 设备并连接到选中的 Host。
  - Android：发现 peer 并连接到选中的 Host。

TCP 文本通道使用一个固定端口：`50001`。

## 端口开放演示（Windows）

Windows Demo 页面新增了 **Port Access Configuration** 卡片，可用于把 Wi‑Fi Direct 侧的一个入口端口转发到本机服务端口：

- Ingress Port：远端设备访问的端口（例如 `51080`）
- Target Host：仅允许 `localhost / 127.0.0.1 / ::1`
- Target Port：Windows 本机服务端口（例如 `8080`）
- Allowed Target Ports：可配置白名单（逗号分隔），目标端口必须在白名单中

示例：将 `51080 -> 127.0.0.1:8080`，远端通过 Wi‑Fi Direct 连到 Windows 后，访问 `Windows_IP:51080` 即可转发到本机 `8080` 服务。

## Android 端口测试面板

Android 页面新增了 **Port Access Test** 区域，用于直接验证 Windows 端口开放是否可达：

- `Host`：Windows 的 Wi‑Fi Direct IP
- `Port`：Windows 配置的 Ingress Port（例如 `51080`）
- `Path`：HTTP 测试路径（例如 `/`）
- `HTTP Test`：发起 HTTP GET 并显示状态码和响应摘要
- `TCP Test`：只做 TCP connect 连通性探测

## 协议

文本协议使用 **newline-delimited JSON**，例如：

```json
{"type":"hello","sender":"Windows-Host","text":"ready","timestampUtc":"2026-03-13T00:00:00Z"}
{"type":"chat","sender":"Android-Client","text":"hello from android","timestampUtc":"2026-03-13T00:00:01Z"}
```

这一层故意做得很简单，方便后面改成：

- 文件传输
- WebRTC 本地信令
- 远控指令
- 设备能力交换

## 运行前提

### Windows
- Windows 10/11
- Wi‑Fi 网卡与驱动支持 Wi‑Fi Direct
- Visual Studio 2022 / MSBuild / UWP 构建支持
- 需要实体设备，GitHub Actions **只能编译，不能真实发起 Wi‑Fi Direct 会话**

### Android
- Android 10+
- 支持 Wi‑Fi P2P
- Android 13+ 需要 `NEARBY_WIFI_DEVICES` 运行时权限
- 旧版本需要位置权限

## GitHub Actions

- `windows-demo.yml`
  - 构建 Windows 协议库测试
  - 构建 UWP Demo
  - 上传 Windows 构建产物
- `android-demo.yml`
  - 构建 Android Debug APK
  - 运行 JVM 单测
  - 上传 APK

## 手工验证建议

参见：

- `docs/manual-test-plan.md`
- `docs/architecture.md`
- `docs/status.md`

## 已知限制

1. GitHub Actions 无法提供 Wi‑Fi Direct 硬件环境，因此 **CI 只负责编译与纯逻辑测试**。
2. Windows 侧采用 UWP/WinRT Wi‑Fi Direct API，目的是贴近微软官方 sample 的路径。
3. Windows ↔ Android 互通依赖底层驱动与设备兼容性；这版 Demo 更适合做“能力验证”而不是“产品质量承诺”。
4. 当前协议是纯文本 JSON，不包含加密、认证和重传机制。
