# PICO Bridge

Built-in 渲染管线下的 PICO MR / tracking bridge 主线工程。

## 当前主线

- Unity: `2022.3.62f3`
- Render Pipeline: Built-in 3D
- XR SDK: `Packages/PICO-Unity-Integration-SDK`
- 主要目标: 保持彩透可用，并继续开发 UI、通讯与 `pc_receiver`

旧的 URP 工程已从主线迁出，作为仓库外参考目录保留，不再继续承载新功能开发。

## 仓库结构

```text
Assets/Scripts/PicoBridge/
├── PicoBridgeManager.cs      # 桥接主入口
├── Network/                  # TCP/UDP 协议与发现
├── Tracking/                 # 头显 / 手柄 / 手部 tracking 采集
├── UI/                       # 头显内调试 UI
├── Camera/                   # 预留的视频预览链路
└── Editor/                   # 自动搭场景与工程校验

pc_receiver/
├── bridge.py                 # 本地开发入口
├── src/pico_bridge/          # Python 接收端实现
└── tests/                    # 单元测试
```

## 快速开始

### Unity 端

1. 用 Unity `2022.3.62f3` 打开本项目
2. 确认 Android 平台启用 PICO Loader
3. 如当前场景未配置桥接对象，执行菜单 `PicoBridge > Setup Scene`
4. 在头显内 UI 中连接 PC receiver

### 头显内 UI tracking 信号

头显内 UI 底部的信号项是 tracking 数据状态指示，不是开关。亮起表示当前运行环境检测到对应数据源有有效追踪信号；变暗表示该数据源未启用、设备未连接、权限/SDK 不支持，或当前没有有效 pose。

| UI 标签 | 含义 |
| --- | --- |
| `HEAD` | 头显位姿，来自 PICO 主传感器 / XR HMD tracking。 |
| `L CTRL` | 左手柄位姿与输入，来自左侧 controller tracking。 |
| `R CTRL` | 右手柄位姿与输入，来自右侧 controller tracking。 |
| `L HAND` | 左手手部追踪骨骼，来自 PICO hand tracking。 |
| `R HAND` | 右手手部追踪骨骼，来自 PICO hand tracking。 |
| `BODY` | 身体追踪骨骼，来自 PICO body tracking，用于全身 / 下肢等 body joint 数据。 |
| `MOTION` | PICO Motion Tracker 外置追踪器信号，不是普通移动/运动状态。当前 UI 会检测已连接 tracker 的有效位姿；真实设备路径中的 `Motion` 数据字段仍是占位输出，Editor Play mock 会填充测试点。 |

### PicoBridge 菜单

这些菜单是编辑器维护工具，不是每次开发前都必须执行。正常拉取仓库后，开发者可以直接打开项目继续开发。

- `PicoBridge > Setup Scene`
  - 用于新场景或场景配置损坏时的一键接入。
  - 会创建或补齐 `PicoBridge` 根对象、`PicoBridgeManager`、`PicoBridgeUI`，并把 `Assets/Prefabs/PicoBridge/PicoBridgePanel.prefab` 安装到当前场景的 Controller Canvas。
  - 已配置好的主场景不需要重复执行。
- `PicoBridge > Rebuild Panel Prefab`
  - 开发维护用，从 `PicoBridgeSceneUiTemplate.cs` 重新生成 `PicoBridgePanel.prefab`。
  - 会覆盖 Prefab 内的手动 UI 调整；普通 UI 调整优先直接编辑 Prefab，不要随手执行这个菜单。
- `PicoBridge > Validate Project Settings`
  - 检查 Android / PICO 相关工程设置，例如 IL2CPP、最低 SDK、Internet 权限和包名。
  - 换机器、升级 SDK、准备真机打包前建议执行；日常编码不需要。

要保持“拉下仓库即可开发”，需要提交 UI Prefab 及其 `.meta`。如果主场景已经安装 Prefab 实例，也需要提交对应场景改动；保存场景前注意避免无关 Unity YAML churn。

### PC 端

```bash
cd pc_receiver
python bridge.py -v
```

默认行为：

- TCP 监听 `63901`
- UDP 广播发现端口 `29888`
- 打印 tracking 数据

## 验证

### Python

```bash
cd pc_receiver
pytest tests -q
```

### Unity

- 打开项目后确认 Console 无编译错误
- Play Mode 下确认可自动搭建 `PicoBridge` 对象
- 连接 PC receiver 后确认 tracking 持续到达
- Android 真机确认彩透仍可用

## Troubleshooting

### 摄像头预览在 `Build & Run` 时更卡

已观察到：PICO 通过 USB 数据线连接电脑，并从 Unity 执行 `Build & Run` 后，摄像头预览画面可能比普通运行更卡。

排查摄像头 / WebRTC 预览卡顿时，把 USB 调试连接和 Unity `Build & Run` 当作独立变量记录。建议对比：

- 数据线连接 + Unity `Build & Run`
- 拔掉数据线后运行已安装 APK
- 只通过网络连接 PC receiver 的运行状态

如果只有 `Build & Run` 路径明显变卡，优先怀疑 USB 调试、logcat、安装后调试会话或电脑端 adb 连接带来的额外负载，而不是直接归因到摄像头编码或 WebRTC 参数。

## 项目约定

- 所有新功能都在当前 Built-in 主线继续开发
- 不再恢复 URP / Live Preview 相关依赖到主线
- 运行时代码放在 `Assets/`，编辑器代码放在 `Editor/`
- 不提交 Unity 生成目录：`Library/`、`Temp/`、`Logs/`、`Build/`、`UserSettings/`
