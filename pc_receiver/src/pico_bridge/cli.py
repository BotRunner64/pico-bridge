"""PICO Bridge CLI — debug wrapper around the in-process SDK."""

from __future__ import annotations

import argparse
import asyncio
import logging
import threading
import time
from typing import Any

from .bridge import PicoBridge

_viz_push: Any = None  # set to visualiser.push_frame when --viz is active
_STATUS_INTERVAL_SECONDS = 5.0
log = logging.getLogger("pico_bridge.cli")


def _visualiser_enabled(args: argparse.Namespace) -> bool:
    return args.viz or args.viz_connect


def _push_visualiser(data: dict[str, Any]) -> None:
    if _viz_push is not None:
        _viz_push(data)


def _format_status(stats: Any) -> str:
    age = "n/a" if stats.latest_frame_age_s is None else f"{stats.latest_frame_age_s:.2f}s"
    video = "disabled"
    if stats.video_enabled:
        state = "running" if stats.video_running else "idle"
        video = f"{stats.video_source or 'unknown'}/{state}"
    return (
        f"status connected={int(stats.connected)} "
        f"sn={stats.device_sn or '-'} "
        f"fps={stats.fps:.1f} "
        f"seq={stats.latest_seq} "
        f"age={age} "
        f"video={video} "
        f"drops={stats.dropped_ring_frames}"
    )


async def _run(args: argparse.Namespace) -> None:
    global _viz_push
    viz_enabled = _visualiser_enabled(args)
    status_interval = max(float(args.status_interval), 0.0)
    _validate_args(args)

    sdk_video = "frames" if args.camera else None if args.video == "disabled" else args.video
    sdk_video_enabled = bool(args.camera) or args.video != "disabled"

    # Start Rerun visualiser if requested
    if viz_enabled:
        from . import visualiser

        visualiser.init(
            spawn=not args.viz_connect,
            connect=args.viz_connect,
            follow=not args.viz_no_follow,
        )
        _viz_push = visualiser.push_frame
        print("Rerun 3D viewer ready")

    bridge = PicoBridge(
        host="0.0.0.0",
        port=args.tcp_port,
        discovery=not args.no_discovery,
        advertise_ip=args.advertise_ip,
        video=sdk_video,
        video_enabled=sdk_video_enabled,
        print_tracking=args.print_tracking,
        on_raw_tracking=_push_visualiser if viz_enabled else None,
    )
    camera_worker: _CameraCaptureWorker | None = None

    try:
        bridge.start()
        if args.camera:
            camera_worker = _CameraCaptureWorker(bridge, args)
            camera_worker.start()

        print(f"PICO Bridge listening on 0.0.0.0:{args.tcp_port}")
        if not args.no_discovery:
            print("UDP discovery broadcasting on port 29888")
        if args.camera:
            print(f"WebRTC video sender ready (source={camera_worker.label})")
        elif args.video != "disabled":
            print(f"WebRTC video sender ready (source={args.video})")
        print("Waiting for headset connection...")

        last_status_time = asyncio.get_running_loop().time()
        while True:
            await asyncio.sleep(1)
            loop_time = asyncio.get_running_loop().time()
            stats = bridge.stats()
            if status_interval > 0 and loop_time - last_status_time >= status_interval:
                print(_format_status(stats), flush=True)
                last_status_time = loop_time
            if viz_enabled:
                from . import visualiser as vis_mod

                vis_mod.set_connection_state(stats.connected, stats.device_sn)
    except asyncio.CancelledError:
        pass
    finally:
        if camera_worker is not None:
            camera_worker.stop()
        bridge.close()
        if viz_enabled:
            from . import visualiser as vis_mod

            vis_mod.close()
            _viz_push = None


class _CameraCaptureWorker:
    def __init__(self, bridge: PicoBridge, args: argparse.Namespace):
        self._bridge = bridge
        self._args = args
        self._stop_event = threading.Event()
        self._ready_event = threading.Event()
        self._thread = threading.Thread(target=self._run, name="pico_bridge_camera_capture", daemon=True)
        self._error: BaseException | None = None
        self.label = args.camera

    def start(self) -> None:
        self._thread.start()
        self._ready_event.wait(timeout=5.0)
        if self._error is not None:
            raise RuntimeError(f"failed to start camera source {self.label}") from self._error
        if not self._ready_event.is_set():
            raise TimeoutError(f"camera source {self.label} did not start before timeout")

    def stop(self) -> None:
        self._stop_event.set()
        if self._thread.is_alive():
            self._thread.join(timeout=2.0)

    def _run(self) -> None:
        try:
            if self._args.camera == "realsense":
                self._run_realsense()
            else:
                self._run_uvc()
        except BaseException as exc:  # pragma: no cover - native camera failures are environment-specific
            self._error = exc
            self._ready_event.set()
            log.exception("camera capture failed")

    def _run_uvc(self) -> None:
        import cv2

        device = _parse_webcam_device(self._args.camera_device)
        capture = cv2.VideoCapture(device)
        if self._args.camera_width:
            capture.set(cv2.CAP_PROP_FRAME_WIDTH, self._args.camera_width)
        if self._args.camera_height:
            capture.set(cv2.CAP_PROP_FRAME_HEIGHT, self._args.camera_height)
        if self._args.camera_fps:
            capture.set(cv2.CAP_PROP_FPS, self._args.camera_fps)
        if not capture.isOpened():
            raise RuntimeError(f"failed to open webcam: {self._args.camera_device}")

        self._ready_event.set()
        try:
            while not self._stop_event.is_set():
                ok, bgr = capture.read()
                if not ok:
                    time.sleep(0.01)
                    continue
                rgb = cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB)
                self._bridge.push_video_frame(rgb)
        finally:
            capture.release()

    def _run_realsense(self) -> None:
        import numpy as np
        import pyrealsense2 as rs

        pipeline = rs.pipeline()
        config = rs.config()
        if self._args.camera_device is not None:
            config.enable_device(str(self._args.camera_device))
        config.enable_stream(
            rs.stream.color,
            self._args.camera_width,
            self._args.camera_height,
            rs.format.rgb8,
            self._args.camera_fps,
        )
        pipeline.start(config)

        self._ready_event.set()
        try:
            while not self._stop_event.is_set():
                frames = pipeline.wait_for_frames()
                color_frame = frames.get_color_frame()
                if not color_frame:
                    continue
                self._bridge.push_video_frame(np.asanyarray(color_frame.get_data()))
        finally:
            pipeline.stop()


def _parse_webcam_device(device: str | None) -> int | str:
    if device is None:
        return 0
    try:
        return int(device)
    except ValueError:
        return device


def _validate_args(args: argparse.Namespace) -> None:
    if args.camera and args.video != "disabled":
        raise ValueError("--camera cannot be combined with --video")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="PICO Bridge PC Receiver")
    parser.add_argument("--tcp-port", type=int, default=63901)
    parser.add_argument(
        "--print-tracking",
        action=argparse.BooleanOptionalAction,
        default=False,
        help="Print decoded tracking frames on every update",
    )
    parser.add_argument(
        "--status-interval",
        type=float,
        default=_STATUS_INTERVAL_SECONDS,
        help="Seconds between compact status lines; use 0 to disable",
    )
    parser.add_argument(
        "--advertise-ip",
        help="Override the IPv4 address announced over UDP discovery",
    )
    parser.add_argument(
        "--video",
        choices=["disabled", "test-pattern"],
        default="disabled",
        help="Video mode: disabled or test-pattern.",
    )
    parser.add_argument(
        "--camera-device",
        help="Optional webcam index/path or RealSense serial for --camera",
    )
    parser.add_argument(
        "--camera",
        choices=["webcam", "realsense"],
        help="Stream a webcam or RealSense camera through the SDK push-frame path",
    )
    parser.add_argument(
        "--camera-width",
        type=int,
        default=1280,
        help="Camera capture width for --camera",
    )
    parser.add_argument(
        "--camera-height",
        type=int,
        default=720,
        help="Camera capture height for --camera",
    )
    parser.add_argument(
        "--camera-fps",
        type=int,
        default=30,
        help="Camera capture FPS for --camera",
    )
    parser.add_argument(
        "--no-discovery",
        action="store_true",
        help="Disable UDP broadcast discovery",
    )
    parser.add_argument(
        "--viz",
        action="store_true",
        help="Launch Rerun 3D viewer for real-time tracking visualisation",
    )
    parser.add_argument(
        "--viz-connect",
        action="store_true",
        help="Connect to an already-running Rerun viewer instead of spawning one",
    )
    parser.add_argument(
        "--viz-no-follow",
        action="store_true",
        help="Disable automatic Rerun view tracking of the current tracking-signal center",
    )
    parser.add_argument("-v", "--verbose", action="store_true")
    return parser


def main() -> None:
    parser = build_parser()
    args = parser.parse_args()
    try:
        _validate_args(args)
    except ValueError as exc:
        parser.error(str(exc))

    logging.basicConfig(
        level=logging.DEBUG if args.verbose else logging.INFO,
        format="%(asctime)s %(name)s %(levelname)s %(message)s",
    )

    try:
        asyncio.run(_run(args))
    except KeyboardInterrupt:
        print("\nShutting down.")
