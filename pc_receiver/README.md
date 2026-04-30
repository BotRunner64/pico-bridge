# PC Receiver

PICO Bridge 的 PC 端接收子工程，单独隔离在 `pc_receiver/` 下。

## 快速开始

推荐在 Python 程序内直接使用 `PicoBridge`。它会在当前进程内启动 TCP receiver、UDP discovery、tracking 缓存，以及按需启用的 WebRTC/RealSense 视频链路：

```python
from pico_bridge import PicoBridge

with PicoBridge(video="realsense") as pico:
    frame = pico.wait_frame(timeout=2.0)
    print(frame.body.joints.shape)
    print(frame.left_hand.active)
    print(pico.stats())
```

`frame` 保留 PICO/Unity 原生语义：坐标为 PICO Unity 空间、单位为米、四元数顺序为 `xyzw`。Teleopit、somehand 或其他项目应在各自代码中把 `PicoFrame` 转换成自己的输入格式。

## PC SDK 开发者指南

### 安装

在开发环境中安装：

```bash
cd pc_receiver
pip install -e .
```

如果需要 Rerun 可视化调试：

```bash
pip install -e ".[viz]"
```

基础安装包含 WebRTC、PyAV、RealSense 依赖，因为 PC 相机/RealSense 视频回传是 pico-bridge 的核心能力。

### 生命周期

`PicoBridge` 是 PC SDK 的主入口。推荐使用 context manager，退出时会关闭后台 receiver、UDP discovery 和 WebRTC sender：

```python
from pico_bridge import PicoBridge

with PicoBridge() as pico:
    frame = pico.wait_frame(timeout=2.0)
```

也可以手动管理生命周期：

```python
pico = PicoBridge(video="camera", camera_device="/dev/video0")
pico.start()
try:
    frame = pico.wait_frame(timeout=2.0)
finally:
    pico.close()
```

`PicoBridge` 在后台线程中运行 asyncio runtime。消费者线程通常只需要调用同步接口：

```python
latest = pico.latest_frame()          # 没有帧时返回 None
frame = pico.wait_frame(timeout=1.0)  # 超时抛 TimeoutError
next_frame = pico.wait_frame(after_seq=frame.seq)
stats = pico.stats()
```

### 构造参数

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

- `host` / `port`：PC 端 TCP 监听地址和端口。
- `discovery`：是否启用 UDP discovery 广播，让 PICO 自动发现 PC。
- `advertise_ip`：手动指定广播给头显的 PC IPv4 地址，适合多网卡环境。
- `video`：`None`、`"test-pattern"`、`"camera"`、`"realsense"`。设置后由 WebRTC 把 PC 端视频送到 PICO。
- `camera_device`：普通摄像头设备路径或 RealSense 序列号。
- `print_tracking`：是否打印每帧 summary，主要用于调试。
- `history_size`：保留最近多少个 `PicoFrame`，主消费语义仍是 latest-wins。
- `start_timeout`：receiver 启动超时时间。
- `on_raw_tracking`：可选回调，收到原始 tracking JSON 时调用，适合可视化或临时调试。

### 帧数据结构

`wait_frame()` 和 `latest_frame()` 返回 `PicoFrame`：

```python
frame.seq
frame.timestamp_ns
frame.receive_time_s
frame.coordinate_space   # "pico_unity"
frame.quat_order         # "xyzw"
frame.units              # "meters"
frame.raw                # 原始 Unity JSON dict
```

头部姿态：

```python
frame.head               # Pose | None
frame.head.position      # numpy shape (3,)
frame.head.rotation      # numpy shape (4,), xyzw
frame.head.array         # numpy shape (7,), [x,y,z,qx,qy,qz,qw]
```

身体数据：

```python
frame.body.active
frame.body.joints        # numpy shape (24, 7), [x,y,z,qx,qy,qz,qw]
frame.body.joint_names   # 24 个 PICO body joint 名称
frame.body.joint_parents # SMPL-X 风格父节点索引
```

手部数据：

```python
frame.left_hand.active
frame.left_hand.joints       # numpy shape (26, 7), [x,y,z,qx,qy,qz,qw]
frame.left_hand.radii        # numpy shape (26,)
frame.left_hand.status       # numpy shape (26,)
frame.left_hand.scale
frame.left_hand.joint_names

frame.right_hand.active
frame.right_hand.joints
```

控制器数据：

```python
frame.controllers.left.pose
frame.controllers.left.axis      # x, y, grip, trigger
frame.controllers.left.buttons   # axisClick, primaryButton, secondaryButton, menuButton
frame.controllers.left.raw
```

如果某类 tracking 当前不可用，SDK 会返回固定 shape 的零数组，并用 `active=False` 表示不可用。这样下游项目可以稳定处理数组形状，同时根据 `active` 决定是否消费该数据。

### 状态和诊断

`pico.stats()` 返回 `PicoBridgeStats`：

```python
stats.connected
stats.device_sn
stats.frame_count
stats.latest_seq
stats.fps
stats.latest_frame_age_s
stats.latest_latency_s
stats.dropped_ring_frames
stats.video_enabled
stats.video_running
stats.video_source
```

`fps` 基于 PC 接收时间估算。`latest_latency_s` 目前保留为 `None`，因为头显 timestamp 和 PC monotonic clock 还没有做时钟同步；需要判断接收新鲜度时使用 `latest_frame_age_s`。

### 视频和 RealSense

视频能力由 `video` 参数控制：

```python
PicoBridge(video=None)                  # 只接收 tracking/control
PicoBridge(video="test-pattern")        # WebRTC 测试图
PicoBridge(video="camera")              # 默认摄像头
PicoBridge(video="camera", camera_device="/dev/video0")
PicoBridge(video="realsense")           # 默认 RealSense
PicoBridge(video="realsense", camera_device="0123456789")
```

摄像头不会在 `PicoBridge.start()` 时立即打开；只有 PICO 端请求 `StartReceivePcCamera` 时才会启动 WebRTC sender 和具体视频源。视频启动失败会记录日志并停止视频 sender，但不会关闭 tracking receiver，也不会清空已接收的 `PicoFrame`。

### 下游项目接入边界

`pico_bridge` 只提供 PICO 原生 PC SDK，不实现业务 adapter：

- Teleopit 应在自己的 provider 中把 `frame.body.joints` 转成 `HumanFrame`。
- somehand 应在自己的 input/source 中把 `frame.left_hand` / `frame.right_hand` 转成自己的 21 landmarks 或 `HandDetection`。
- 其他项目可以直接消费 `PicoFrame`，或在自己的代码中做坐标系、关节顺序、滤波和插值。

不要把 Teleopit、somehand、MediaPipe、MuJoCo 或具体机器人语义放进 `pico_bridge`。这样 PC SDK 才能保持简单、可复用、可调试。

CLI 入口仍可用于调试、日志和可视化：

```bash
cd pc_receiver
python bridge.py -v
```

默认 CLI 只输出连接、视频、告警和低频状态摘要，避免每帧 tracking 刷屏。需要逐帧排查 tracking 内容时再显式开启：

```bash
python bridge.py --print-tracking
python bridge.py --status-interval 0   # 关闭周期状态摘要
```

如果希望按标准 Python 包方式使用：

```bash
cd pc_receiver
pip install -e .
pico-bridge-receiver -v
```

如需实时 3D 可视化，安装可选依赖后使用 `--viz` 启动 Rerun，或用 `--viz-connect` 连接已运行的 viewer：

```bash
cd pc_receiver
pip install -e ".[viz]"
pico-bridge-receiver --viz
pico-bridge-receiver --viz-connect
```

## 目录结构

```text
pc_receiver/
├── bridge.py              # 本地开发入口包装器
├── pyproject.toml         # Python 包元数据
├── src/pico_bridge/       # 接收端实现
└── tests/                 # Python 单元测试
```

## 运行测试

```bash
cd pc_receiver
python -m pytest tests -v
```

## CLI 视频调试

CLI 可用于验证 WebRTC 视频链路。tracking/control 仍走现有 TCP 协议，视频走 WebRTC。

```bash
cd pc_receiver
pip install -e .
python bridge.py --video test-pattern
```

真实摄像头使用 `camera` 源，Linux 默认读取 `/dev/video0`，也可以手动指定设备：

```bash
python bridge.py --video camera --camera-device /dev/video0
```

Intel RealSense 摄像头使用 `realsense` 源，只读取 RGB color stream。安装项目依赖后启动：

```bash
pip install -e .
python bridge.py --video realsense
```

如果有多台 RealSense，可用序列号指定设备：

```bash
python bridge.py --video realsense --camera-device 0123456789
```
