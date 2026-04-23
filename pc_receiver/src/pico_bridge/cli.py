"""PICO Bridge CLI — TCP server for headset tracking data."""

from __future__ import annotations

import argparse
import asyncio
import json
import logging
from typing import Any

from .discovery import UdpBroadcaster
from .tcp_server import PicoBridgeServer
from .tracking import TrackingFrame
from .video_sender import CameraRequest, VideoSender

_frame_count = 0
_viz_push: Any = None  # set to visualiser.push_frame when --viz is active
_video_sender: VideoSender | None = None
_event_loop: asyncio.AbstractEventLoop | None = None


def _visualiser_enabled(args: argparse.Namespace) -> bool:
    return args.viz or args.viz_connect


def _on_tracking(data: dict[str, Any]) -> None:
    global _frame_count
    _frame_count += 1
    frame = TrackingFrame.from_json(
        json.dumps(data)
    ) if isinstance(data, dict) else TrackingFrame.from_json(data)
    print(f"[{_frame_count:>6}] {frame.summary()}", flush=True)
    if _viz_push is not None:
        _viz_push(data)


def _on_tracking_silent(data: dict[str, Any]) -> None:
    """Tracking callback when --no-print-tracking but --viz is on."""
    if _viz_push is not None:
        _viz_push(data)


def _on_function(name: str, value: Any) -> None:
    print(f"  fn: {name} = {value}", flush=True)


def _on_camera_request(req: CameraRequest) -> None:
    if _video_sender is None:
        print("  camera request ignored (video disabled)", flush=True)
        return
    if _event_loop is None:
        return
    asyncio.run_coroutine_threadsafe(_video_sender.start(req), _event_loop)
    print(f"  video sender starting -> {req.ip}:{req.port}", flush=True)


def _on_camera_stop() -> None:
    if _video_sender is None or _event_loop is None:
        return
    asyncio.run_coroutine_threadsafe(_video_sender.stop(), _event_loop)
    print("  video sender stopping", flush=True)


async def _run(args: argparse.Namespace) -> None:
    global _viz_push, _video_sender, _event_loop
    viz_enabled = _visualiser_enabled(args)
    _event_loop = asyncio.get_running_loop()

    # Start Rerun visualiser if requested
    if viz_enabled:
        from . import visualiser

        visualiser.init(spawn=not args.viz_connect, connect=args.viz_connect)
        _viz_push = visualiser.push_frame
        print("Rerun 3D viewer ready")

    # Set up video sender if enabled
    if args.video != "disabled":
        _video_sender = VideoSender(
            source=args.video,
            camera_device=args.camera_device,
        )
        print(f"Video sender ready (source={args.video})")

    # Choose tracking callback
    if args.print_tracking:
        tracking_cb = _on_tracking
    elif viz_enabled:
        tracking_cb = _on_tracking_silent
    else:
        tracking_cb = None

    server = PicoBridgeServer(
        host="0.0.0.0",
        port=args.tcp_port,
        on_tracking=tracking_cb,
        on_function=_on_function,
        on_camera_request=_on_camera_request,
        on_camera_stop=_on_camera_stop,
    )
    await server.start()

    broadcaster = UdpBroadcaster(
        tcp_port=args.tcp_port,
        advertise_ip=args.advertise_ip,
    )
    if not args.no_discovery:
        await broadcaster.start()

    print(f"PICO Bridge listening on 0.0.0.0:{args.tcp_port}")
    if not args.no_discovery:
        print("UDP discovery broadcasting on port 29888")
    print("Waiting for headset connection...")

    try:
        while True:
            await asyncio.sleep(1)
            if viz_enabled:
                from . import visualiser as vis_mod

                vis_mod.set_connection_state(server.connected, server.device_sn)
    except asyncio.CancelledError:
        pass
    finally:
        if _video_sender is not None:
            await _video_sender.stop()
            _video_sender = None
        _event_loop = None
        await broadcaster.stop()
        await server.stop()
        if viz_enabled:
            from . import visualiser as vis_mod

            vis_mod.close()
            _viz_push = None


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="PICO Bridge PC Receiver")
    parser.add_argument("--tcp-port", type=int, default=63901)
    parser.add_argument(
        "--print-tracking",
        action=argparse.BooleanOptionalAction,
        default=True,
        help="Print decoded tracking frames",
    )
    parser.add_argument(
        "--advertise-ip",
        help="Override the IPv4 address announced over UDP discovery",
    )
    parser.add_argument(
        "--video",
        choices=["disabled", "test-pattern", "camera"],
        default="disabled",
        help="Video mode: disabled, test-pattern (ffmpeg testsrc), or camera (webcam)",
    )
    parser.add_argument(
        "--camera-device",
        default=None,
        help="Camera device path/name for --video=camera (e.g. /dev/video0)",
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
    parser.add_argument("-v", "--verbose", action="store_true")
    return parser


def main() -> None:
    parser = build_parser()
    args = parser.parse_args()

    logging.basicConfig(
        level=logging.DEBUG if args.verbose else logging.INFO,
        format="%(asctime)s %(name)s %(levelname)s %(message)s",
    )

    try:
        asyncio.run(_run(args))
    except KeyboardInterrupt:
        print("\nShutting down.")
