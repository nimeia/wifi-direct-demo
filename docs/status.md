# Status

## 已完成
- Windows UWP Wi‑Fi Direct Demo 骨架
- Android Wi‑Fi P2P Demo 骨架
- 统一 JSON Lines 文本协议
- Windows 协议单元测试
- Android JVM 单元测试
- GitHub Actions 构建脚本
- 运行文档与手工测试计划

## 当前范围内未做
- 文件传输
- 自动重连
- 服务发现元数据互通
- 认证 / 加密
- 正式产品级 UI

## 风险
- Windows 与 Android 间的实际互通依赖具体网卡驱动
- UWP 工程在不同 CI 镜像上的可用性取决于对应 Visual Studio / UWP 组件
- Android 真机上的权限行为在不同 ROM 上可能存在差异
