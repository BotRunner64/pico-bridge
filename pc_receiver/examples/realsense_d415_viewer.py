"""View RealSense D415 stereo infrared frames before PicoBridge or WebRTC."""

from __future__ import annotations

import argparse
import tempfile
import time
from pathlib import Path
from typing import Any

import numpy as np


MAIN_WINDOW = "RealSense D415 source (before PicoBridge/WebRTC)"
STATUS_HEIGHT = 150
CONTRAST_SAMPLE_STEP = 4
DEFAULT_EXPOSURE_US = 30_000.0
DEFAULT_GAIN = 48.0


def _validate_pair(left: np.ndarray, right: np.ndarray) -> None:
    if left.dtype != np.uint8 or right.dtype != np.uint8:
        raise ValueError("infrared frames must have dtype uint8")
    if left.ndim != 2 or right.ndim != 2:
        raise ValueError("infrared frames must be two-dimensional")
    if left.shape != right.shape:
        raise ValueError("left and right infrared frames must have matching shapes")


def _auto_contrast_pair(
    left: np.ndarray, right: np.ndarray
) -> tuple[np.ndarray, np.ndarray, float, float]:
    """Apply one shared percentile stretch to both eyes for display only."""

    _validate_pair(left, right)
    samples = np.concatenate(
        (
            left[::CONTRAST_SAMPLE_STEP, ::CONTRAST_SAMPLE_STEP].reshape(-1),
            right[::CONTRAST_SAMPLE_STEP, ::CONTRAST_SAMPLE_STEP].reshape(-1),
        )
    )
    low, high = (float(value) for value in np.percentile(samples, (1.0, 99.0)))
    if high <= low:
        return left.copy(), right.copy(), low, high

    values = np.arange(256, dtype=np.float32)
    lut = np.clip((values - low) * (255.0 / (high - low)), 0.0, 255.0).astype(
        np.uint8
    )
    return lut[left], lut[right], low, high


def _pair_to_bgr(left: np.ndarray, right: np.ndarray) -> np.ndarray:
    """Pack two Y8 frames into a three-channel SBS image for OpenCV."""

    _validate_pair(left, right)
    height, eye_width = left.shape
    sbs = np.empty((height, eye_width * 2, 3), dtype=np.uint8)
    sbs[:, :eye_width, :] = left[:, :, np.newaxis]
    sbs[:, eye_width:, :] = right[:, :, np.newaxis]
    return sbs


def _luma_stats(frame: np.ndarray) -> tuple[int, float, int]:
    if frame.dtype != np.uint8 or frame.ndim != 2:
        raise ValueError("infrared frame must be a two-dimensional uint8 array")
    return int(frame.min()), float(frame.mean()), int(frame.max())


def _put_text(
    image: np.ndarray,
    text: str,
    origin: tuple[int, int],
    color: tuple[int, int, int],
) -> None:
    import cv2

    cv2.putText(
        image,
        text,
        origin,
        cv2.FONT_HERSHEY_SIMPLEX,
        0.7,
        color,
        2,
        cv2.LINE_AA,
    )


def _option_value(sensor: Any, option: Any) -> float | None:
    if not sensor.supports(option):
        return None
    return float(sensor.get_option(option))


def _option_text(value: float | None, *, precision: int = 0) -> str:
    if value is None:
        return "n/a"
    return f"{value:.{precision}f}"


def _status_frame(
    frame: np.ndarray,
    *,
    width: int,
    height: int,
    requested_fps: int,
    measured_fps: float,
    left_frame_number: int,
    right_frame_number: int,
    left_stats: tuple[int, float, int],
    right_stats: tuple[int, float, int],
    auto_contrast: bool,
    contrast_range: tuple[float, float] | None,
    auto_exposure: float | None,
    exposure: float | None,
    gain: float | None,
    emitter: float | None,
    warning: str | None,
) -> np.ndarray:
    import cv2

    canvas = cv2.copyMakeBorder(
        frame,
        STATUS_HEIGHT,
        0,
        0,
        0,
        cv2.BORDER_CONSTANT,
        value=(18, 18, 18),
    )
    _put_text(
        canvas,
        "DIRECT REALSENSE IR1 | IR2 - before PicoBridge / WebRTC",
        (18, 28),
        (90, 230, 90),
    )
    _put_text(
        canvas,
        (
            f"per eye {width}x{height}@{requested_fps}  loop {measured_fps:5.1f} fps  "
            f"frame L/R {left_frame_number}/{right_frame_number}"
        ),
        (18, 59),
        (235, 235, 235),
    )
    _put_text(
        canvas,
        (
            f"RAW brightness  L min/mean/max {left_stats[0]}/{left_stats[1]:.1f}/{left_stats[2]}  "
            f"R {right_stats[0]}/{right_stats[1]:.1f}/{right_stats[2]}"
        ),
        (18, 90),
        (235, 235, 235),
    )
    display_mode = "AUTO-CONTRAST (display only)" if auto_contrast else "RAW"
    if auto_contrast and contrast_range is not None:
        display_mode += f" [{contrast_range[0]:.0f}, {contrast_range[1]:.0f}]"
    _put_text(
        canvas,
        (
            f"sensor AE {'ON' if auto_exposure else 'OFF'}  "
            f"exposure {_option_text(exposure)} us  gain {_option_text(gain)}  "
            f"emitter {'ON' if emitter else 'OFF'}  display {display_mode}"
        ),
        (18, 121),
        (255, 220, 80),
    )
    _put_text(
        canvas,
        "C: contrast  E: emitter  A: auto exposure  [ / ]: exposure  S: raw PNG  Q/Esc: quit",
        (18, 145),
        (180, 180, 180),
    )
    _put_text(canvas, "LEFT IR1", (18, STATUS_HEIGHT + 30), (255, 220, 80))
    _put_text(
        canvas,
        "RIGHT IR2",
        (frame.shape[1] // 2 + 18, STATUS_HEIGHT + 30),
        (255, 220, 80),
    )
    if warning:
        cv2.rectangle(
            canvas,
            (3, 3),
            (canvas.shape[1] - 4, canvas.shape[0] - 4),
            (0, 0, 255),
            7,
        )
        _put_text(canvas, warning, (18, canvas.shape[0] - 20), (0, 0, 255))
    return canvas


def _set_manual_exposure(sensor: Any, rs: Any, multiplier: float) -> float:
    if not sensor.supports(rs.option.exposure):
        raise RuntimeError("this device does not expose manual exposure control")
    if sensor.supports(rs.option.enable_auto_exposure):
        sensor.set_option(rs.option.enable_auto_exposure, 0.0)

    current = float(sensor.get_option(rs.option.exposure))
    option_range = sensor.get_option_range(rs.option.exposure)
    target = min(max(current * multiplier, option_range.min), option_range.max)
    if option_range.step > 0:
        target = option_range.min + round(
            (target - option_range.min) / option_range.step
        ) * option_range.step
    sensor.set_option(rs.option.exposure, float(target))
    return float(target)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--serial", help="Optional RealSense serial number")
    parser.add_argument("--width", type=int, default=1280, help="Per-eye width")
    parser.add_argument("--height", type=int, default=720, help="Per-eye height")
    parser.add_argument("--fps", type=int, default=30)
    parser.add_argument("--window-width", type=int, default=1600)
    parser.add_argument(
        "--capture-dir",
        type=Path,
        default=Path(tempfile.gettempdir()) / "realsense-d415-viewer",
    )
    parser.add_argument(
        "--enable-emitter",
        action="store_true",
        help="start with the infrared dot projector enabled",
    )
    parser.add_argument(
        "--auto-contrast",
        action="store_true",
        help="start with display-only automatic contrast enabled",
    )
    parser.add_argument(
        "--auto-exposure",
        action="store_true",
        help="start with sensor auto exposure instead of manual exposure",
    )
    parser.add_argument(
        "--exposure",
        type=float,
        default=DEFAULT_EXPOSURE_US,
        help=f"manual exposure in microseconds (default: {DEFAULT_EXPOSURE_US:.0f})",
    )
    parser.add_argument(
        "--gain",
        type=float,
        default=DEFAULT_GAIN,
        help=f"manual sensor gain (default: {DEFAULT_GAIN:.0f})",
    )
    args = parser.parse_args()

    try:
        import cv2
        import pyrealsense2 as rs
    except ImportError as exc:
        raise RuntimeError(
            "OpenCV and pyrealsense2 are required; install them with "
            "`pip install -e '.[camera]'` first"
        ) from exc

    pipeline = rs.pipeline()
    config = rs.config()
    if args.serial:
        config.enable_device(args.serial)
    config.enable_stream(
        rs.stream.infrared,
        1,
        args.width,
        args.height,
        rs.format.y8,
        args.fps,
    )
    config.enable_stream(
        rs.stream.infrared,
        2,
        args.width,
        args.height,
        rs.format.y8,
        args.fps,
    )

    profile = pipeline.start(config)
    try:
        device = profile.get_device()
        device_name = device.get_info(rs.camera_info.name)
        device_serial = device.get_info(rs.camera_info.serial_number)
        sensor = device.first_depth_sensor()

        if sensor.supports(rs.option.emitter_enabled):
            sensor.set_option(
                rs.option.emitter_enabled, 1.0 if args.enable_emitter else 0.0
            )
        elif args.enable_emitter:
            print("Warning: this device does not expose emitter control.", flush=True)

        if sensor.supports(rs.option.enable_auto_exposure):
            sensor.set_option(
                rs.option.enable_auto_exposure,
                1.0 if args.auto_exposure else 0.0,
            )
        elif args.auto_exposure:
            raise RuntimeError(
                "this RealSense device does not expose auto-exposure control"
            )
        if not args.auto_exposure:
            if not sensor.supports(rs.option.exposure):
                raise RuntimeError("this device does not expose exposure control")
            sensor.set_option(rs.option.exposure, args.exposure)
            if not sensor.supports(rs.option.gain):
                raise RuntimeError("this device does not expose gain control")
            sensor.set_option(rs.option.gain, args.gain)

        left_profile = profile.get_stream(
            rs.stream.infrared, 1
        ).as_video_stream_profile()
        intrinsics = left_profile.get_intrinsics()
        source_width = int(intrinsics.width) * 2
        source_height = int(intrinsics.height)

        args.capture_dir.mkdir(parents=True, exist_ok=True)
        print(
            f"Showing direct {device_name} ({device_serial}) IR1 | IR2 at "
            f"{source_width}x{source_height}@{args.fps} before PicoBridge/WebRTC."
        )
        if args.auto_exposure:
            emitter_text = "on" if args.enable_emitter else "off"
            print(f"Sensor settings: auto exposure, emitter {emitter_text}.")
        else:
            actual_exposure = _option_value(sensor, rs.option.exposure)
            actual_gain = _option_value(sensor, rs.option.gain)
            print(
                f"Sensor settings: manual exposure {_option_text(actual_exposure)} us, "
                f"gain {_option_text(actual_gain)}, emitter "
                f"{'on' if args.enable_emitter else 'off'}."
            )
        print(f"Raw snapshots: {args.capture_dir}")
        print(
            "Press C for display-only contrast, E for emitter, A for auto exposure, "
            "[ or ] for exposure, S for raw PNG, or Q/Esc to quit."
        )

        cv2.namedWindow(MAIN_WINDOW, cv2.WINDOW_NORMAL | cv2.WINDOW_KEEPRATIO)
        window_height = max(
            320,
            round(
                (source_height + STATUS_HEIGHT) * args.window_width / source_width
            ),
        )
        cv2.resizeWindow(MAIN_WINDOW, args.window_width, window_height)

        auto_contrast = args.auto_contrast
        measured_fps = 0.0
        previous_loop_time = time.monotonic()
        warning: str | None = None
        warning_until = 0.0
        frame_index = 0

        while True:
            frames = pipeline.wait_for_frames()
            left_frame = frames.get_infrared_frame(1)
            right_frame = frames.get_infrared_frame(2)
            if not left_frame or not right_frame:
                continue

            left = np.asanyarray(left_frame.get_data())
            right = np.asanyarray(right_frame.get_data())
            _validate_pair(left, right)
            frame_index += 1

            now = time.monotonic()
            interval = now - previous_loop_time
            previous_loop_time = now
            if interval > 0:
                instantaneous_fps = 1.0 / interval
                measured_fps = (
                    instantaneous_fps
                    if measured_fps == 0
                    else measured_fps * 0.9 + instantaneous_fps * 0.1
                )

            contrast_range = None
            display_left = left
            display_right = right
            if auto_contrast:
                display_left, display_right, low, high = _auto_contrast_pair(
                    left, right
                )
                contrast_range = (low, high)

            left_frame_number = int(left_frame.get_frame_number())
            right_frame_number = int(right_frame.get_frame_number())
            if left_frame_number != right_frame_number:
                warning = (
                    f"IR frame numbers differ: {left_frame_number} / "
                    f"{right_frame_number}"
                )
                warning_until = now + 2.0

            display = _status_frame(
                _pair_to_bgr(display_left, display_right),
                width=left.shape[1],
                height=left.shape[0],
                requested_fps=args.fps,
                measured_fps=measured_fps,
                left_frame_number=left_frame_number,
                right_frame_number=right_frame_number,
                left_stats=_luma_stats(left),
                right_stats=_luma_stats(right),
                auto_contrast=auto_contrast,
                contrast_range=contrast_range,
                auto_exposure=_option_value(sensor, rs.option.enable_auto_exposure),
                exposure=_option_value(sensor, rs.option.exposure),
                gain=_option_value(sensor, rs.option.gain),
                emitter=_option_value(sensor, rs.option.emitter_enabled),
                warning=warning if now < warning_until else None,
            )
            cv2.imshow(MAIN_WINDOW, display)

            key = cv2.waitKey(1) & 0xFF
            if key in (27, ord("q"), ord("Q")):
                break
            if key in (ord("c"), ord("C")):
                auto_contrast = not auto_contrast
                print(
                    "Display auto-contrast "
                    f"{'enabled' if auto_contrast else 'disabled'}; raw pixels unchanged.",
                    flush=True,
                )
            if key in (ord("e"), ord("E")):
                try:
                    current = _option_value(sensor, rs.option.emitter_enabled)
                    if current is None:
                        raise RuntimeError("this device does not expose emitter control")
                    sensor.set_option(
                        rs.option.emitter_enabled, 0.0 if current else 1.0
                    )
                except RuntimeError as exc:
                    warning = f"Emitter change failed: {exc}"
                    warning_until = time.monotonic() + 3.0
                    print(warning, flush=True)
            if key in (ord("a"), ord("A")):
                try:
                    current = _option_value(sensor, rs.option.enable_auto_exposure)
                    if current is None:
                        raise RuntimeError(
                            "this device does not expose auto-exposure control"
                        )
                    sensor.set_option(
                        rs.option.enable_auto_exposure, 0.0 if current else 1.0
                    )
                except RuntimeError as exc:
                    warning = f"Auto-exposure change failed: {exc}"
                    warning_until = time.monotonic() + 3.0
                    print(warning, flush=True)
            if key in (ord("["), ord("]")):
                try:
                    target = _set_manual_exposure(
                        sensor, rs, 0.8 if key == ord("[") else 1.25
                    )
                    print(f"Manual exposure set to {target:.0f} us.", flush=True)
                except RuntimeError as exc:
                    warning = f"Exposure change failed: {exc}"
                    warning_until = time.monotonic() + 3.0
                    print(warning, flush=True)
            if key in (ord("s"), ord("S")):
                path = args.capture_dir / (
                    f"raw_ir_sbs_{time.strftime('%Y%m%d_%H%M%S')}_"
                    f"frame{frame_index:06d}.png"
                )
                raw_sbs = np.hstack((left, right))
                if not cv2.imwrite(str(path), raw_sbs):
                    warning = f"Snapshot failed: {path}"
                    warning_until = time.monotonic() + 3.0
                    print(warning, flush=True)
                else:
                    print(f"Saved raw Y8 SBS snapshot: {path}", flush=True)
            try:
                if cv2.getWindowProperty(MAIN_WINDOW, cv2.WND_PROP_VISIBLE) < 1:
                    break
            except cv2.error:
                break
    finally:
        pipeline.stop()
        try:
            cv2.destroyAllWindows()
        except UnboundLocalError:
            pass


if __name__ == "__main__":
    main()
