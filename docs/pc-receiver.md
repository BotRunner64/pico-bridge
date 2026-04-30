# PC Receiver

`pc_receiver/` 是 PC 端 Python package。它提供两个入口：

- `PicoBridge`：推荐给下游项目集成的 SDK API。
- `pico-bridge-receiver` / `python bridge.py`：调试和手动运行用 CLI。

## 安装

```bash
cd pc_receiver
pip install -e .
```

基础安装包含 WebRTC、PyAV、RealSense 和 Rerun 可视化依赖，因为 PC 相机/RealSense 视频回传和 tracking 可视化是核心调试路径。

## CLI 常用示例

```bash
pico-bridge-receiver -v --video camera --viz
```

源码调试入口同样可以启用 PC camera 和 Rerun 可视化：

```bash
python bridge.py -v --video camera --viz
```

## SDK 最小示例

```python
from pico_bridge import PicoBridge

with PicoBridge(video="camera") as pico:
    frame = pico.wait_frame(timeout=2.0)
    print(frame.body.joints.shape)
    print(frame.left_hand.active)
    print(pico.stats())
```

坐标和数据语义保持 PICO/Unity 原生含义：

- 坐标空间：`pico_unity`
- 单位：米
- 四元数顺序：`xyzw`

下游项目应在自己的代码里转换坐标系、关节顺序、滤波或机器人语义。

## 生命周期

推荐使用 context manager：

```python
from pico_bridge import PicoBridge

with PicoBridge() as pico:
    frame = pico.wait_frame(timeout=2.0)
```

也可以手动管理：

```python
pico = PicoBridge(video="camera", camera_device="/dev/video0")
pico.start()
try:
    frame = pico.wait_frame(timeout=2.0)
finally:
    pico.close()
```

`PicoBridge` 会在后台线程中运行 asyncio runtime。普通消费者只需要同步接口。

## 构造参数

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

关键参数：

- `host` / `port`：TCP 监听地址和端口。
- `discovery`：是否启用 UDP discovery。
- `advertise_ip`：多网卡环境下手动指定广播给头显的 PC IPv4。
- `video`：`None`、`"test-pattern"`、`"camera"`、`"realsense"`。
- `camera_device`：普通摄像头设备路径或 RealSense 序列号。
- `print_tracking`：逐帧打印 tracking，调试时使用。
- `history_size`：最近帧缓存大小，消费语义仍是 latest-wins。
- `on_raw_tracking`：收到原始 tracking JSON 时调用的回调。

## 读取数据

```python
latest = pico.latest_frame()          # 没有帧时返回 None
frame = pico.wait_frame(timeout=1.0)  # 超时抛 TimeoutError
next_frame = pico.wait_frame(after_seq=frame.seq)
stats = pico.stats()
```

`PicoFrame` 主要字段：

```python
frame.seq
frame.timestamp_ns
frame.receive_time_s
frame.coordinate_space   # "pico_unity"
frame.quat_order         # "xyzw"
frame.units              # "meters"
frame.raw                # 原始 Unity JSON dict
```

头部：

```python
frame.head               # Pose | None
frame.head.position      # numpy shape (3,)
frame.head.rotation      # numpy shape (4,), xyzw
frame.head.array         # numpy shape (7,), [x,y,z,qx,qy,qz,qw]
```

身体：

```python
frame.body.active
frame.body.joints        # numpy shape (24, 7)
frame.body.joint_names
frame.body.joint_parents
```

手部：

```python
frame.left_hand.active
frame.left_hand.joints   # numpy shape (26, 7)
frame.left_hand.radii
frame.left_hand.status
frame.left_hand.scale

frame.right_hand.active
frame.right_hand.joints
```

控制器：

```python
frame.controllers.left.pose
frame.controllers.left.axis
frame.controllers.left.buttons
frame.controllers.left.raw
```

某类 tracking 不可用时，SDK 返回固定 shape 的零数组，并用 `active=False` 表示不可消费。

## 视频模式

```python
PicoBridge(video=None)
PicoBridge(video="test-pattern")
PicoBridge(video="camera")
PicoBridge(video="camera", camera_device="/dev/video0")
PicoBridge(video="realsense")
PicoBridge(video="realsense", camera_device="0123456789")
```

摄像头不会在 `PicoBridge.start()` 时立即打开。只有 PICO 端请求 `StartReceivePcCamera` 时，PC receiver 才会启动 WebRTC sender 和具体视频源。

视频启动失败会记录日志并停止视频 sender，但不会关闭 tracking receiver，也不会清空已接收的 `PicoFrame`。

## 统计信息

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

`fps` 基于 PC 接收时间估算。`latest_latency_s` 目前保留为 `None`，因为头显 timestamp 和 PC monotonic clock 没有做时钟同步。

## 边界

`pico_bridge` 只提供 PICO 原生 PC SDK，不实现业务 adapter：

- Teleopit 应在自己的 provider 中转换 `frame.body.joints`。
- somehand 应在自己的 input/source 中转换手部数据。
- 其他项目可以直接消费 `PicoFrame`，或在自己的代码中做坐标系、关节顺序、滤波和插值。

不要把 Teleopit、somehand、MediaPipe、MuJoCo 或具体机器人语义放进 `pico_bridge`。
