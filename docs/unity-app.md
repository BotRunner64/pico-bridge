# Unity / PICO App

Unity 端负责采集 PICO tracking 数据、维护头显内 UI、连接 PC receiver，并在需要时接收 PC 端 WebRTC 视频。

## 项目基线

- Unity：`2022.3.62f3`
- Render Pipeline：Built-in 3D
- XR SDK：`Packages/PICO-Unity-Integration-SDK`
- 当前场景：`Assets/Scenes/SampleScene.unity`
- Android package：`com.picobridge.app`

## 第一次打开项目

第一次 clone 后，先在 Unity Hub 里选择 `Add` / `Add project from disk`，把仓库根目录加入 Hub，再用 Unity `2022.3.62f3` 打开项目。

进入 Editor 后需要手动打开 `Assets/Scenes/SampleScene.unity`。Unity Editor 启动时通常恢复本机上次编辑状态，不会可靠地根据 Build Settings 自动打开主场景。

## 代码位置

```text
Assets/Scripts/PicoBridge/
├── PicoBridgeManager.cs      桥接主入口
├── Network/                  TCP/UDP 协议与发现
├── Tracking/                 头显、手柄、手部、身体 tracking 采集
├── Camera/                   WebRTC 视频接收
├── UI/                       头显内调试 UI
└── Editor/                   场景 setup、校验和构建工具
```

## 编辑器菜单

### `PicoBridge > Setup Scene`

用于新场景或场景配置损坏时的一键接入。它会补齐：

- `PicoBridge` 根对象
- `PicoBridgeManager`
- `PicoBridgeUI`
- `Assets/Prefabs/PicoBridge/PicoBridgePanel.prefab` 实例

已配置好的主场景不需要反复执行。

### `PicoBridge > Rebuild Panel Prefab`

维护用命令，会从 `PicoBridgeSceneUiTemplate.cs` 重新生成 UI prefab。

它会覆盖 prefab 内的手动 UI 调整。普通 UI 调整优先直接编辑 prefab。

### `PicoBridge > Validate Project Settings`

打包前建议执行。它会检查 Android/PICO 相关设置，例如：

- IL2CPP
- Android min SDK
- Internet 权限
- application identifier 是否像模板值

## 头显内 UI 信号

底部信号项是 tracking 数据状态指示，不是开关。亮起表示当前运行环境检测到有效追踪信号。

| 标签 | 含义 |
| --- | --- |
| `HEAD` | 头显位姿，来自 PICO 主传感器 / XR HMD tracking。 |
| `L CTRL` | 左手柄位姿与输入。 |
| `R CTRL` | 右手柄位姿与输入。 |
| `L HAND` | 左手手部追踪骨骼。 |
| `R HAND` | 右手手部追踪骨骼。 |
| `BODY` | 身体追踪骨骼，用于全身或下肢 body joint 数据。 |
| `MOTION` | PICO Motion Tracker 外置追踪器信号。 |

变暗通常表示数据源未启用、设备未连接、权限/SDK 不支持，或当前没有有效 pose。

## Android/PICO 注意事项

- Android target 使用 IL2CPP。
- Android 架构使用 ARM64。
- Internet 权限必须开启。
- Unity WebRTC `3.0.0-pre.7` 的 Android 路径需要关闭 Optimized Frame Pacing；项目中 `androidUseSwappy` 应保持为 `0`。
- Android active input handling 使用 New Input System；项目中 `activeInputHandler` 应保持为 `1`。

## UI 修改规则

- 运行时代码不要创建、删除、重建或自动迁移 UI 层级。
- UI 层级通过编辑器工具或手动 prefab/scene 编辑维护。
- 添加、移动、删除 Unity asset 时保留 `.meta` 文件。
- 保存场景前注意避免无关 Unity YAML churn。
