"""PICO Bridge CLI — debug wrapper around the in-process SDK."""

from __future__ import annotations

import argparse
import asyncio
import logging
from typing import Any

from .bridge import PicoBridge

_viz_push: Any = None  # set to visualiser.push_frame when --viz is active


def _visualiser_enabled(args: argparse.Namespace) -> bool:
    return args.viz or args.viz_connect


def _push_visualiser(data: dict[str, Any]) -> None:
    if _viz_push is not None:
        _viz_push(data)


async def _run(args: argparse.Namespace) -> None:
    global _viz_push
    viz_enabled = _visualiser_enabled(args)

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
        video=args.video,
        camera_device=args.camera_device,
        print_tracking=args.print_tracking,
        on_raw_tracking=_push_visualiser if viz_enabled else None,
    )

    try:
        bridge.start()

        print(f"PICO Bridge listening on 0.0.0.0:{args.tcp_port}")
        if not args.no_discovery:
            print("UDP discovery broadcasting on port 29888")
        if args.video != "disabled":
            print(f"WebRTC video sender ready (source={args.video})")
        print("Waiting for headset connection...")

        while True:
            await asyncio.sleep(1)
            if viz_enabled:
                from . import visualiser as vis_mod

                stats = bridge.stats()
                vis_mod.set_connection_state(stats.connected, stats.device_sn)
    except asyncio.CancelledError:
        pass
    finally:
        bridge.close()
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
        choices=["disabled", "test-pattern", "camera", "realsense"],
        default="disabled",
        help="Video mode: disabled, test-pattern, camera (webcam), or realsense (RGB color stream)",
    )
    parser.add_argument(
        "--camera-device",
        default=None,
        help="Camera device path/name for --video=camera, or RealSense serial for --video=realsense",
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

    logging.basicConfig(
        level=logging.DEBUG if args.verbose else logging.INFO,
        format="%(asctime)s %(name)s %(levelname)s %(message)s",
    )

    try:
        asyncio.run(_run(args))
    except KeyboardInterrupt:
        print("\nShutting down.")
