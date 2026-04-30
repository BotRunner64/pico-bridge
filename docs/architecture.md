# Architecture

PICO Bridge 分为两个进程：

```text
PICO headset / Unity app  <---- LAN ---->  PC receiver / Python SDK
```

## 通道

- UDP discovery：PC 广播 receiver 地址，PICO 自动发现。
- TCP tracking/control：PICO 发送 tracking JSON，PC 发送控制命令。
- WebRTC video：PC 按需把测试图、摄像头或 RealSense 画面回传到 PICO。

## PICO / Unity 端

Unity 端职责：

- 采集 PICO tracking 数据。
- 维护头显内 PicoBridge UI。
- 通过 UDP/TCP 连接 PC receiver。
- 按用户请求启动或停止 PC camera preview。
- 在头显内显示 WebRTC 视频 texture。

运行时代码位于 `Assets/Scripts/PicoBridge/`。编辑器工具位于 `Assets/Scripts/PicoBridge/Editor/`。

## PC receiver 端

PC 端职责：

- 监听 TCP receiver。
- 广播 UDP discovery。
- 把原始 tracking JSON 解析成稳定的 `PicoFrame`。
- 缓存最近帧，提供 latest-wins 消费语义。
- 按需启动 WebRTC sender 和视频源。

Package 位于 `pc_receiver/src/pico_bridge/`。

## 数据边界

`pico_bridge` 保持 PICO 原生语义：

- 坐标空间不在 SDK 内转成机器人或视觉系统坐标。
- 关节顺序不在 SDK 内转成 Teleopit、somehand、MediaPipe 或 MuJoCo。
- 不把业务 adapter 放进本仓库。

这样 PC SDK 可以保持简单、可复用、可调试。

## 视频设计

视频源只在 PICO 请求时打开。这样纯 tracking 场景不会占用摄像头或 RealSense。

PC 端视频采集使用 latest-frame 思路，避免 WebRTC `recv` 路径被同步采集阻塞。源采集慢时优先复用最近帧，并记录慢帧告警。

## 失败处理原则

- tracking receiver 和 video sender 解耦。
- 视频失败不应关闭 tracking receiver。
- 断开重连时清理旧 socket、旧 peer 和旧队列。
- 不可用 tracking 数据使用固定 shape 零数组，并用 `active=False` 表示不可消费。
