# PC Receiver API

## Overview

Use the synchronous `PicoBridge` API on the PC side to read the latest tracking frames. The same SDK can also push user-provided RGB video frames to the headset over WebRTC.

## Installation

Install the PC receiver from the wheel attached to the GitHub Release:

```bash
pip install https://github.com/BotRunner64/pico-bridge/releases/download/v0.2.1/pico_bridge-0.2.1-py3-none-any.whl
```

For local development inside this repository, install from the PC receiver package directory:

```bash
cd pc_receiver
pip install -e .
```

The core package does not depend on OpenCV, MuJoCo, or RealSense. Install the camera extra only when you need the CLI or examples that use webcam or RealSense devices:

```bash
pip install -e ".[camera]"
```

## Supported Architectures

The PC receiver supports x86 and Arm machines when the required Python dependencies are available.

On Arm machines that need RealSense support, install `pyrealsense2` from conda-forge in the active Conda environment instead of using the pip package:

```bash
pip uninstall pyrealsense2
conda install -c conda-forge pyrealsense2
```

## As a Dependency

If another project only needs the PC SDK and not the Unity project, depend on the release wheel directly:

```toml
dependencies = [
    "pico-bridge @ https://github.com/BotRunner64/pico-bridge/releases/download/v0.2.1/pico_bridge-0.2.1-py3-none-any.whl"
]
```

Package versions match the PICO/APK release version. For example, `pico_bridge-0.2.1-py3-none-any.whl` corresponds to the `v0.2.1` APK release. Installing the wheel downloads only the PC-side Python package, not the Unity project.

For local integration testing, install only the subdirectory:

```bash
pip install -e /path/to/pico-bridge/pc_receiver
```

Use `/path/to/pico-bridge/pc_receiver[camera]` when the integration needs those optional camera dependencies.

## Tracking Example

Start the receiver, wait for one frame, and read common tracking fields:

```python
from pico_bridge import PicoBridge

with PicoBridge() as pico:
    frame = pico.wait_frame(timeout=2.0)
    print(frame.head.position)
    print(frame.body.active, frame.body.joints.shape)
    print(frame.left_hand.active, frame.left_hand.joints.shape)
    print(pico.stats())
```

## Pushed Video Frames

Use `video="frames"` to push arbitrary RGB images, such as MuJoCo renders or frames captured by OpenCV. `push_video_frame()` accepts only `numpy.ndarray` frames with dtype `uint8` and shape `(height, width, 3)` in RGB channel order.

```python
from pico_bridge import PicoBridge

with PicoBridge(video="frames") as pico:
    while True:
        rgb = render_frame_as_rgb_uint8()
        pico.push_video_frame(rgb)
```

`push_video_frame()` stores only the latest frame. It does not queue frames, so a fast simulator will not build up display latency. Frames pushed before the headset starts the WebRTC preview are cached and sent once the preview connection starts.

### Stereo SBS and ZED Mini quick start

Set `video_layout="stereo-sbs"` when one pushed frame contains the left-eye image in its left half and the right-eye image in its right half. The headset then samples the matching half for each eye instead of showing the combined frame as a normal preview.

The included ZED Mini example opens the camera in `HD720`, retrieves the SDK's rectified `sl.VIEW.SIDE_BY_SIDE` image, reads the rectified `fx`, `fy`, `cx`, and `cy` values, converts BGRA to RGB, and pushes the image and normalized intrinsics through the same bridge. Install the ZED SDK and its bundled `pyzed` Python API first, then run:

```bash
cd pc_receiver
pip install -e .
python examples/zed_mini_sbs.py --advertise-ip 192.168.1.10
```

Replace the advertised address with the PC address reachable from the headset. The headset requests one combined `1280x360` WebRTC stream at 60 fps, giving each eye `640x360`; use `--fps 30` if the camera cannot capture at 60 fps. This option changes ZED capture cadence, while the WebRTC track remains paced at the headset's 60 fps request.

The headset maps each output-eye ray through the supplied source-camera intrinsics instead of stretching each 16:9 image over the complete eye viewport. This preserves angular scale and image proportions; areas beyond the camera's calibrated field of view feather back to PICO passthrough. An SBS source without `stereo_intrinsics` uses an aspect-preserving 90-degree horizontal-FOV fallback.

The display is still head-locked: it does not reproject the camera image using a timestamped camera pose, depth-warp it, or align it geometrically with the physical world. If the eyes are reversed or the decoded texture is upside down, use `Swap Eyes` or `Flip Y` on the scene's `StereoVideoScreen` component.

Use `video_enabled=False` to keep the WebRTC preview disabled at startup, then toggle it later with `set_video_enabled(True)` when you want the headset to request video:

```python
with PicoBridge(video="frames", video_enabled=False) as pico:
    pico.push_video_frame(render_frame_as_rgb_uint8())
    pico.set_video_enabled(True)
```

Example scripts:

```bash
python pc_receiver/examples/opencv_camera.py --device 0
python pc_receiver/examples/realsense_camera.py --serial RS123
python pc_receiver/examples/mujoco_camera.py path/to/model.xml --camera camera_name
python pc_receiver/examples/zed_mini_sbs.py --advertise-ip 192.168.1.10
```

The receiver CLI can stream a UVC webcam or RealSense camera through the same SDK push-frame path:

```bash
pico-bridge-receiver -v --video test-pattern --viz
pico-bridge-receiver -v --camera webcam --viz
pico-bridge-receiver -v --camera webcam --camera-device /dev/video0 --viz
pico-bridge-receiver -v --camera realsense --camera-device RS123 --viz
```

Omit `--camera-device` to use webcam index `0` or the default RealSense device. Install the `camera` extra for OpenCV (`cv2`) and `pyrealsense2`.

## CLI Recording

Use `--record` to capture raw tracking frames for debugging:

```bash
pico-bridge-receiver --record
pico-bridge-receiver --record recordings/session.jsonl
pico-bridge-receiver --record recordings/
```

The receiver writes newline-delimited JSON. The first line is metadata, and each following line is one tracking frame with a receiver-side sequence number, receive timestamp, and the raw Unity payload. When `--record` has no value, files are created under `pico_bridge_recordings/` with a timestamped name. Passing a directory also creates a timestamped file inside that directory. Each frame is flushed immediately so interrupted debug sessions keep the data already received.

## Creating a Receiver

Create a receiver with the following options:

```python
PicoBridge(
    host="0.0.0.0",
    port=63901,
    discovery=True,
    advertise_ip=None,
    video=None,
    video_enabled=None,
    video_layout="mono",
    stereo_intrinsics=None,
    print_tracking=False,
    history_size=120,
    start_timeout=10.0,
    on_raw_tracking=None,
)
```

Common parameters:

- `advertise_ip`: PC IPv4 address advertised to the headset when multiple network interfaces are present.
- `video`: `None`, `"frames"`, or `"test-pattern"`.
- `video_enabled`: Initial video policy. `None` follows `video`; `False` keeps the headset from requesting WebRTC video until you call `set_video_enabled(True)`.
- `video_layout`: `"mono"` for a normal full-frame preview or `"stereo-sbs"` for left/right side-by-side frames.
- `stereo_intrinsics`: Optional rectified `StereoCameraIntrinsics` for one eye of an SBS source. Supplying it lets the headset preserve the camera's calibrated projection even when WebRTC resizes the frame. It requires `video_layout="stereo-sbs"`.
- `print_tracking`: Print tracking data every frame.
- `on_raw_tracking`: Called with the raw Unity JSON when a tracking frame arrives.

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

Coordinates and data use the PICO native tracking convention: coordinate space `pico_native`, units meters, quaternion order `xyzw`. Head, controller, and hand poses are serialized from the PICO/PXR pose values. Body poses use PICO body `localPose` values with the body Z position and Z/W quaternion components inverted to match the bridge transport convention.

The Unity sender does not force-fit the body skeleton to the headset pose or auto-align tracking data to a ground plane. Consumers that need a shared application space, robot coordinate frame, or floor alignment should apply an explicit calibration transform.

When a tracking family is unavailable, the SDK returns fixed-shape zero arrays and marks the family with `active=False`.

## Language

- [中文](../zh/pc-receiver.md)
- [Docs Home](README.md)
