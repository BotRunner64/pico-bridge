# Unity 结构和开发

## 概览

Unity 端负责采集 PICO tracking、维护头显内 UI、连接 PC receiver，并显示 PC 端 WebRTC 视频。

## 基线

- Unity：`2022.3.62f3`
- Render Pipeline：Built-in 3D
- XR SDK：`Packages/PICO-Unity-Integration-SDK`
- 主场景：`Assets/Scenes/SampleScene.unity`
- Android package：`com.picobridge.app`

PICO 4 和 PICO 4 Ultra 使用同一 APK，不按设备拆包。使用前在 PICO 开发者菜单中关闭安全边界，并在 `设置 > 交互` 中打开“手势和控制器自动切换”。

全身动捕需要先在 PICO 系统里配置 Motion Tracker，并完成校准。未配置或未校准时，`BODY` / `MOTION` 不亮是预期表现。

Tip: 如果要在 Unity 里直接 `Build & Run` 到头显，请先用支持数据传输的 USB 数据线连接 PC 和 PICO；充电线可能不会被 Unity 识别为可部署设备。

## 第一次打开项目

1. 在 Unity Hub 选择 `Add` / `Add project from disk`，加入仓库根目录。
2. 用 Unity `2022.3.62f3` 打开项目。
3. 手动打开 `Assets/Scenes/SampleScene.unity`。

## 结构

运行时桥接代码、UI prefab、Android native plugin assets 和 PC receiver 按以下结构组织：

```text
Assets/Scripts/PicoBridge/
├── PicoBridgeManager.cs      桥接主入口
├── Network/                  TCP/UDP 协议与发现
├── Tracking/                 头显、手柄、手部、身体 tracking
├── Camera/                   WebRTC 接收与 stereo SBS 显示
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
| `PicoBridge > Install Stereo SBS Screen` | 在 `Main Camera` 下安装或刷新头锁定的逐眼视频屏幕。 |
| `PicoBridge > Rebuild Panel Prefab` | 从模板重建 UI prefab；会覆盖手动 UI 调整。 |
| `PicoBridge > Validate Project Settings` | 检查 Android/PICO 打包设置。 |

## Stereo SBS 显示

`SampleScene` 的 `Main Camera` 下包含一个 `StereoVideoScreen` 子对象。普通 `mono` 视频时它的 renderer 保持禁用；只有 PC 广播 `video_layout="stereo-sbs"` 且 WebRTC 解码纹理已经到达后才会启用。`PicoBridge/StereoSBS` shader 会通过 Unity 的 stereo eye index 选择对应的 SBS 半幅画面，使用投影矩阵恢复 PICO 输出眼视线，再用归一化源相机内参映射这条视线，从而保持相机画面的比例和角度尺度。超出源相机标定 FOV 的像素会渐隐回 passthrough。设备侧需要修正时，可以在 `StereoSbsDisplay` 组件上使用 `Swap Eyes` 和 `Flip Y`。

屏幕层级由编辑器工具安装；运行时代码只更新其纹理、可见性、标定数据和当前逐眼投影属性。显示仍然采用头锁定方式，不执行带时间戳的相机姿态重投影、深度扭曲或世界配准。

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

## 语言

- [English](../en/unity-development.md)
- [文档首页](README.md)
