# PC Receiver API

## 概览

PC 端推荐使用同步 `PicoBridge` API 读取最新 tracking 帧。

## 安装

本地开发时，从 PC receiver 包目录安装：

```bash
cd pc_receiver
pip install -e .
```

## 作为其他项目依赖

如果其他项目只需要 PC SDK，不需要 Unity 工程，可以直接依赖 `pc_receiver` 子目录：

```toml
dependencies = [
    "pico-bridge-pc-receiver @ git+ssh://git@github.com/BotRunner64/pico-bridge.git@v0.1.0#subdirectory=pc_receiver"
]
```

把 `v0.1.0` 替换成要使用的 tag 或 commit。推荐依赖固定 tag 或 commit，不要直接依赖 `main`，避免 Unity 项目开发中的变化影响下游环境。

本地联调时也可以只安装子目录：

```bash
pip install -e /path/to/pico-bridge/pc_receiver
```

## 最小示例

启动 receiver，等待一帧，并读取常用 tracking 字段：

```python
from pico_bridge import PicoBridge

with PicoBridge(video="camera") as pico:
    frame = pico.wait_frame(timeout=2.0)
    print(frame.head.position)
    print(frame.body.active, frame.body.joints.shape)
    print(frame.left_hand.active, frame.left_hand.joints.shape)
    print(pico.stats())
```

## 创建

使用以下选项创建 receiver：

```python
PicoBridge(
    host="0.0.0.0",
    port=63901,
    discovery=True,
    advertise_ip=None,
    video=None,
    camera_device=None,
    print_tracking=False,
    history_size=120,
    start_timeout=10.0,
    on_raw_tracking=None,
)
```

常用参数：

- `advertise_ip`：多网卡时指定广播给头显的 PC IPv4。
- `video`：`None`、`"test-pattern"`、`"camera"`、`"realsense"`。
- `camera_device`：摄像头设备路径或 RealSense 序列号。
- `print_tracking`：逐帧打印 tracking。
- `on_raw_tracking`：收到原始 Unity JSON 时调用。

## 读取帧

读取最新帧、等待一帧、等待下一个序号，或查看 receiver 状态。没有可用帧时 `latest_frame()` 返回 `None`，`wait_frame()` 超时时抛 `TimeoutError`。

```python
latest = pico.latest_frame()
frame = pico.wait_frame(timeout=1.0)
next_frame = pico.wait_frame(after_seq=frame.seq)
stats = pico.stats()
```

常用字段：

```python
frame.seq
frame.timestamp_ns
frame.receive_time_s
frame.head.position            # shape (3,)
frame.head.rotation            # shape (4,), xyzw
frame.body.active
frame.body.joints              # shape (24, 7)
frame.left_hand.active
frame.left_hand.joints         # shape (26, 7)
frame.right_hand.joints
frame.controllers.left.pose
frame.controllers.left.axis
frame.controllers.left.buttons
frame.raw
```

坐标和数据保持 PICO/Unity 原生语义：坐标空间 `pico_unity`，单位 meters，四元数顺序 `xyzw`。下游项目自己转换坐标系、关节顺序和机器人语义。

某类 tracking 不可用时，SDK 返回固定 shape 零数组，并用 `active=False` 表示不可消费。

## 语言

- [English](../en/pc-receiver.md)
- [文档首页](README.md)
