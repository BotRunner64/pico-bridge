# PC Receiver API

## 概览

PC 端推荐使用同步 `PicoBridge` API 读取最新 tracking 帧。同一个 SDK 也可以通过 WebRTC 把用户提供的 RGB 视频帧推送到头显。

## 安装

从 GitHub Release 附带的 wheel 安装 PC receiver：

```bash
pip install https://github.com/BotRunner64/pico-bridge/releases/download/v0.1.0/pico_bridge-0.1.0-py3-none-any.whl
```

在本仓库内本地开发时，从 PC receiver 包目录安装：

```bash
cd pc_receiver
pip install -e .
```

核心包不依赖 OpenCV、MuJoCo 或 RealSense。只在运行对应示例时安装这些依赖。

## 作为其他项目依赖

如果其他项目只需要 PC SDK，不需要 Unity 工程，可以直接依赖 release wheel：

```toml
dependencies = [
    "pico-bridge @ https://github.com/BotRunner64/pico-bridge/releases/download/v0.1.0/pico_bridge-0.1.0-py3-none-any.whl"
]
```

包版本跟 PICO/APK release 版本保持一致。例如，`pico_bridge-0.1.0-py3-none-any.whl` 对应 `v0.1.0` APK release。通过 wheel 安装只会下载 PC 端 Python 包，不会下载 Unity 工程。

本地联调时也可以只安装子目录：

```bash
pip install -e /path/to/pico-bridge/pc_receiver
```

## Tracking 示例

启动 receiver，等待一帧，并读取常用 tracking 字段：

```python
from pico_bridge import PicoBridge

with PicoBridge() as pico:
    frame = pico.wait_frame(timeout=2.0)
    print(frame.head.position)
    print(frame.body.active, frame.body.joints.shape)
    print(frame.left_hand.active, frame.left_hand.joints.shape)
    print(pico.stats())
```

## 推送视频帧

使用 `video="frames"` 推送任意 RGB 图像，例如 MuJoCo 渲染画面或 OpenCV 捕获帧。`push_video_frame()` 只接受 dtype 为 `uint8`、shape 为 `(height, width, 3)`、通道顺序为 RGB 的 `numpy.ndarray`。

```python
from pico_bridge import PicoBridge

with PicoBridge(video="frames") as pico:
    while True:
        rgb = render_frame_as_rgb_uint8()
        pico.push_video_frame(rgb)
```

`push_video_frame()` 只保存最新帧，不排队，所以高频仿真不会累积显示延迟。头显启动 WebRTC 预览前推入的帧会被缓存，并在预览连接启动后发送。

示例脚本：

```bash
python pc_receiver/examples/opencv_camera.py --device 0
python pc_receiver/examples/realsense_camera.py --serial RS123
python pc_receiver/examples/mujoco_camera.py path/to/model.xml --camera camera_name
```

Receiver CLI 仍可用于 tracking 和视频链路自测：

```bash
pico-bridge-receiver -v --video test-pattern --viz
```

## 创建 Receiver

使用以下选项创建 receiver：

```python
PicoBridge(
    host="0.0.0.0",
    port=63901,
    discovery=True,
    advertise_ip=None,
    video=None,
    print_tracking=False,
    history_size=120,
    start_timeout=10.0,
    on_raw_tracking=None,
)
```

常用参数：

- `advertise_ip`：多网卡时指定广播给头显的 PC IPv4。
- `video`：`None`、`"frames"` 或 `"test-pattern"`。
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
