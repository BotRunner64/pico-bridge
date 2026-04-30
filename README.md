# PICO Bridge

PICO Bridge 是一套 PICO 头显到 PC 的桥接工具：

- PICO/Unity 端负责采集头显、手柄、手部、身体和 Motion Tracker tracking 数据。
- PC receiver 端负责局域网发现、TCP 接收、tracking 缓存，以及按需把 PC 摄像头或 RealSense 画面通过 WebRTC 回传到头显。

当前主线是 Unity Built-in 3D 项目，编辑器版本固定为 `2022.3.62f3`。

## 快速入口

- 第一次跑通：[`docs/quickstart.md`](docs/quickstart.md)
- PC SDK/API：[`docs/pc-receiver.md`](docs/pc-receiver.md)
- Unity/PICO 端维护：[`docs/unity-app.md`](docs/unity-app.md)
- 发布打包：[`docs/release.md`](docs/release.md)
- 常见问题：[`docs/troubleshooting.md`](docs/troubleshooting.md)
- 架构和开发约定：[`docs/architecture.md`](docs/architecture.md)、[`docs/development.md`](docs/development.md)

## 仓库结构

```text
Assets/Scripts/PicoBridge/      Unity/PICO 端运行时代码和编辑器工具
Assets/Prefabs/PicoBridge/      头显内 UI prefab
Assets/Scenes/                  Unity 场景
Packages/PICO-Unity-Integration-SDK/
pc_receiver/                    PC 端 Python receiver package
docs/                           面向使用、维护、发布的文档
```

## 最小运行路径

PC 端：

```bash
cd pc_receiver
pip install -e .
pico-bridge-receiver -v --video camera --viz
```

PICO 端：

1. 安装已发布的 APK，或用 Unity `2022.3.62f3` 构建安装。
2. 确认 PICO 和 PC 在同一个局域网。
3. 打开头显内 PicoBridge 面板并连接 PC receiver。

默认端口：

- TCP tracking/control：`63901`
- UDP discovery：`29888`

## 当前发布建议

本项目当前适合发布为内部/合作方 alpha 包：`PICO APK + PC receiver wheel + release notes`。公开商店发布前，应先完成正式签名、版本策略、安装文档和长时间真机稳定性验证。

具体流程见 [`docs/release.md`](docs/release.md)。
