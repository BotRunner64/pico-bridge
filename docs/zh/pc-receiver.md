# PC Receiver API

## 概览

PC 端推荐使用同步 `PicoBridge` API 读取最新 tracking 帧。同一个 SDK 也可以通过 WebRTC 把用户提供的 RGB 视频帧推送到头显。

## 安装

从 GitHub Release 附带的 wheel 安装 PC receiver：

```bash
pip install https://github.com/BotRunner64/pico-bridge/releases/download/v0.2.0/pico_bridge-0.2.0-py3-none-any.whl
```

在本仓库内本地开发时，从 PC receiver 包目录安装：

```bash
cd pc_receiver
pip install -e .
```

核心包不依赖 OpenCV、MuJoCo 或 RealSense。只有需要运行使用 webcam 或 RealSense 设备的 CLI 或示例时，才安装 camera extra：

```bash
pip install -e ".[camera]"
```

## 支持的架构

PC receiver 支持 x86 和 Arm 架构的机器，前提是所需 Python 依赖可用。

在需要 RealSense 支持的 Arm 架构机器上，请在当前 Conda 环境中使用 conda-forge 安装 `pyrealsense2`，不要使用 pip 包：

```bash
pip uninstall pyrealsense2
conda install -c conda-forge pyrealsense2
```

## 作为其他项目依赖

如果其他项目只需要 PC SDK，不需要 Unity 工程，可以直接依赖 release wheel：

```toml
dependencies = [
    "pico-bridge @ https://github.com/BotRunner64/pico-bridge/releases/download/v0.2.0/pico_bridge-0.2.0-py3-none-any.whl"
]
```

包版本跟 PICO/APK release 版本保持一致。例如，`pico_bridge-0.2.0-py3-none-any.whl` 对应 `v0.2.0` APK release。通过 wheel 安装只会下载 PC 端 Python 包，不会下载 Unity 工程。

本地联调时也可以只安装子目录：

```bash
pip install -e /path/to/pico-bridge/pc_receiver
```

如果联调需要可选相机依赖，可以使用 `/path/to/pico-bridge/pc_receiver[camera]`。

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

如果希望启动时不请求 WebRTC 视频，可以把 `video_enabled=False`，之后再调用 `set_video_enabled(True)` 让头显开始请求视频：

```python
with PicoBridge(video="frames", video_enabled=False) as pico:
    pico.push_video_frame(render_frame_as_rgb_uint8())
    pico.set_video_enabled(True)
```

示例脚本：

```bash
python pc_receiver/examples/opencv_camera.py --device 0
python pc_receiver/examples/realsense_camera.py --serial RS123
python pc_receiver/examples/mujoco_camera.py path/to/model.xml --camera camera_name
```

Receiver CLI 可以通过同一套 SDK 推帧路径直接推送 UVC 摄像头或 RealSense 摄像头：

```bash
pico-bridge-receiver -v --video test-pattern --viz
pico-bridge-receiver -v --camera webcam --viz
pico-bridge-receiver -v --camera webcam --camera-device /dev/video0 --viz
pico-bridge-receiver -v --camera realsense --camera-device RS123 --viz
```

省略 `--camera-device` 时，webcam 使用索引 `0`，RealSense 使用默认设备。OpenCV（`cv2`）和 `pyrealsense2` 由 `camera` extra 安装。

## CLI 录制

使用 `--record` 捕获原始 tracking 帧，方便调试：

```bash
pico-bridge-receiver --record
pico-bridge-receiver --record recordings/session.jsonl
pico-bridge-receiver --record recordings/
```

Receiver 会写入 newline-delimited JSON。第一行是 metadata，之后每一行是一帧 tracking，包含 receiver 侧序号、接收时间戳和原始 Unity payload。`--record` 不带值时，文件会用带时间戳的名称创建在 `pico_bridge_recordings/` 下。传入目录时，也会在该目录内创建带时间戳的文件。每帧都会立即 flush，所以调试会话中断后，已经收到的数据仍会保留。

## 创建 Receiver

使用以下选项创建 receiver：

```python
PicoBridge(
    host="0.0.0.0",
    port=63901,
    discovery=True,
    advertise_ip=None,
    video=None,
    video_enabled=None,
    print_tracking=False,
    history_size=120,
    start_timeout=10.0,
    on_raw_tracking=None,
)
```

常用参数：

- `advertise_ip`：多网卡时指定广播给头显的 PC IPv4。
- `video`：`None`、`"frames"` 或 `"test-pattern"`。
- `video_enabled`：初始视频策略。`None` 跟随 `video`；`False` 会让头显在调用 `set_video_enabled(True)` 前不再请求 WebRTC 视频。
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

坐标和数据使用 PICO native tracking 约定：坐标空间 `pico_native`，单位 meters，四元数顺序 `xyzw`。Head、controller 和 hand pose 直接序列化 PICO/PXR pose 值。Body pose 使用 PICO body `localPose`，并对 body 的 Z 位置和四元数 Z/W 分量取反，以匹配 bridge 传输约定。

Unity 发送端不会把 body skeleton 强行拟合到 headset pose，也不会把 tracking 数据自动对齐到地面。如果下游需要统一的应用坐标空间、机器人坐标系或地面对齐，应使用显式校准变换。

某类 tracking 不可用时，SDK 返回固定 shape 零数组，并用 `active=False` 表示不可消费。

## 语言

- [English](../en/pc-receiver.md)
- [文档首页](README.md)
