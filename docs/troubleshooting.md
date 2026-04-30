# Troubleshooting

## PICO 找不到 PC

检查顺序：

1. PICO 和 PC 是否在同一局域网。
2. PC receiver 是否正在运行。
3. 防火墙是否允许 TCP `63901` 和 UDP `29888`。
4. PC 是否有多张网卡，导致 discovery 广播了错误 IP。

多网卡时，手动指定广播地址：

```python
PicoBridge(advertise_ip="192.168.x.x")
```

## 已连接但没有 tracking

检查：

- PC receiver 日志是否显示 device connected。
- 头显内 UI 的 `HEAD` 是否亮起。
- 当前 PICO 权限、SDK 和设备是否支持对应 tracking 源。
- 手部、身体、Motion Tracker 需要真实设备或 SDK 支持；不可用时是正常降级。

SDK 里某类 tracking 不可用时会返回固定 shape 的零数组，并设置 `active=False`。

## 摄像头预览卡顿

已观察到：PICO 通过 USB 数据线连接电脑，并从 Unity 执行 `Build & Run` 后，摄像头预览可能比普通运行更卡。

排查时把 USB 调试连接和 Unity `Build & Run` 当作独立变量。建议对比：

- 数据线连接 + Unity `Build & Run`
- 拔掉数据线后运行已安装 APK
- 只通过网络连接 PC receiver

如果只有 `Build & Run` 路径明显变卡，优先怀疑 USB 调试、logcat、安装后调试会话或 adb 连接带来的额外负载。

## WebRTC 没画面

检查：

- PC receiver 是否以视频模式启动。
- PICO 端是否请求 `StartReceivePcCamera`。
- 摄像头或 RealSense 是否被其他程序占用。
- RealSense 序列号或摄像头设备路径是否正确。
- 网络是否允许 WebRTC 相关 UDP 流量。

先用测试图验证链路：

```python
PicoBridge(video="test-pattern")
```

再切换到真实摄像头：

```python
PicoBridge(video="camera", camera_device="/dev/video0")
```

## RealSense 打不开

检查：

- `pyrealsense2` 是否安装成功。
- 设备是否能被系统识别。
- 设备是否被其他进程占用。
- `camera_device` 是否填了正确序列号。

视频源启动失败不会关闭 tracking receiver。先确认 tracking 正常，再单独排查视频。

## CLI 输出太多

默认 CLI 只输出连接、视频、告警和低频状态摘要。

逐帧 tracking 输出只在需要时打开：

```bash
python bridge.py --print-tracking
```

关闭周期状态摘要：

```bash
python bridge.py --status-interval 0
```
