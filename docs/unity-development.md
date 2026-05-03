# Unity Development

Unity 端负责采集 PICO tracking、维护头显内 UI、连接 PC receiver，并显示 PC 端 WebRTC 视频。

## 基线

- Unity：`2022.3.62f3`
- Render Pipeline：Built-in 3D
- XR SDK：`Packages/PICO-Unity-Integration-SDK`
- 主场景：`Assets/Scenes/SampleScene.unity`
- Android package：`com.picobridge.app`

PICO 4 和 PICO 4 Ultra 使用同一 APK，不按设备拆包。使用前在 PICO 开发者菜单中关闭安全边界，并在 `设置 > 交互` 中打开“手势和控制器自动切换”。

全身动捕需要先在 PICO 系统里配置 Motion Tracker，并完成校准。未配置或未校准时，`BODY` / `MOTION` 不亮是预期表现。

## 第一次打开项目

1. 在 Unity Hub 选择 `Add` / `Add project from disk`，加入仓库根目录。
2. 用 Unity `2022.3.62f3` 打开项目。
3. 手动打开 `Assets/Scenes/SampleScene.unity`。

## 结构

```text
Assets/Scripts/PicoBridge/
├── PicoBridgeManager.cs      桥接主入口
├── Network/                  TCP/UDP 协议与发现
├── Tracking/                 头显、手柄、手部、身体 tracking
├── Camera/                   WebRTC 视频接收
├── UI/                       头显内 UI
└── Editor/                   场景 setup、校验和构建工具

Assets/Prefabs/PicoBridge/    头显内 UI prefab
Assets/Plugins/Android/       Android native plugin assets
pc_receiver/                  PC 端 Python receiver
```

## 编辑器菜单

| 菜单 | 用途 |
| --- | --- |
| `PicoBridge > Setup Scene` | 补齐桥接对象和 UI prefab 实例。 |
| `PicoBridge > Rebuild Panel Prefab` | 从模板重建 UI prefab；会覆盖手动 UI 调整。 |
| `PicoBridge > Validate Project Settings` | 检查 Android/PICO 打包设置。 |

## 开发规则

- 保持 Built-in 3D 主线，不恢复 URP / Live Preview 依赖。
- 运行时代码不要创建、删除、重建或自动迁移 UI 层级。
- UI 层级通过编辑器工具或手动 prefab/scene 编辑维护。
- 添加、移动、删除 Unity asset 时保留 `.meta` 文件。
- 保存场景前避免无关 Unity YAML churn。

## 验证

- Python receiver：`cd pc_receiver && pytest tests -q`
- Unity：用 `2022.3.62f3` 打开项目，确认 Console 无编译错误。
- 真机：独立安装 APK，验证 passthrough、tracking、PC 连接和视频回传。
