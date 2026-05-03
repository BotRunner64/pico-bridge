# PC Receiver API

PC 端推荐用 `PicoBridge` 同步接口读取最新 tracking 帧。

## 安装

```bash
cd pc_receiver
pip install -e .
```

## 最小示例

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

## 读取

```python
latest = pico.latest_frame()          # 没有帧时返回 None
frame = pico.wait_frame(timeout=1.0)  # 超时抛 TimeoutError
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
frame.raw                      # 原始 Unity JSON dict
```

坐标和数据保持 PICO/Unity 原生语义：坐标空间 `pico_unity`，单位 meters，四元数顺序 `xyzw`。下游项目自己转换坐标系、关节顺序和机器人语义。

某类 tracking 不可用时，SDK 返回固定 shape 零数组，并用 `active=False` 表示不可消费。
