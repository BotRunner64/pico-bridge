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
3. 菜单 `PicoBridge > Setup Scene`，或直接进入 Play 让脚本自动注入 `PicoBridge` 根对象
4. 在头显内 UI 中连接 PC receiver

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

## 项目约定

- 所有新功能都在当前 Built-in 主线继续开发
- 不再恢复 URP / Live Preview 相关依赖到主线
- 运行时代码放在 `Assets/`，编辑器代码放在 `Editor/`
- 不提交 Unity 生成目录：`Library/`、`Temp/`、`Logs/`、`Build/`、`UserSettings/`
