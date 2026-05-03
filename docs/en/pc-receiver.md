# PC Receiver API

## Overview

Use the synchronous `PicoBridge` API on the PC side to read the latest tracking frames.

## Installation

Install the PC receiver from the package directory during local development:

```bash
cd pc_receiver
pip install -e .
```

## Dependency From Another Project

If another project only needs the PC SDK and does not need the Unity project, depend on the `pc_receiver` subdirectory directly:

```toml
dependencies = [
    "pico-bridge-pc-receiver @ git+ssh://git@github.com/BotRunner64/pico-bridge.git@v0.1.0#subdirectory=pc_receiver"
]
```

Replace `v0.1.0` with the tag or commit you want to use. Prefer a fixed tag or commit instead of `main`, so downstream environments are not affected by unrelated Unity project changes.

For local integration testing, install only the subdirectory:

```bash
pip install -e /path/to/pico-bridge/pc_receiver
```

## Minimal Example

Start the receiver, wait for one frame, and read common tracking fields:

```python
from pico_bridge import PicoBridge

with PicoBridge(video="camera") as pico:
    frame = pico.wait_frame(timeout=2.0)
    print(frame.head.position)
    print(frame.body.active, frame.body.joints.shape)
    print(frame.left_hand.active, frame.left_hand.joints.shape)
    print(pico.stats())
```

## Construction

Create a receiver with the following options:

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

Common parameters:

- `advertise_ip`: PC IPv4 address advertised to the headset when the PC has multiple network interfaces.
- `video`: `None`, `"test-pattern"`, `"camera"`, or `"realsense"`.
- `camera_device`: Camera device path or RealSense serial number.
- `print_tracking`: Print tracking data every frame.
- `on_raw_tracking`: Called when raw Unity JSON is received.

## Reading Frames

Read the latest frame, wait for a frame, wait for the next sequence number, or inspect receiver stats. `latest_frame()` returns `None` when no frame is available, and `wait_frame()` raises `TimeoutError` on timeout.

```python
latest = pico.latest_frame()
frame = pico.wait_frame(timeout=1.0)
next_frame = pico.wait_frame(after_seq=frame.seq)
stats = pico.stats()
```

Common fields:

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

Coordinates and data preserve native PICO/Unity semantics: coordinate space `pico_unity`, units in meters, and quaternion order `xyzw`. Downstream projects should convert coordinate systems, joint order, and robot semantics themselves.

When one tracking category is unavailable, the SDK returns a fixed-shape zero array and sets `active=False` to mark it as not consumable.

## Language

- [中文](../zh/pc-receiver.md)
- [Documentation home](README.md)
