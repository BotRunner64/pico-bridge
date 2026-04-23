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

_frame_count = 0
_viz_push: Any = None  # set to visualiser.push_frame when --viz is active


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


async def _run(args: argparse.Namespace) -> None:
    global _viz_push
    viz_enabled = _visualiser_enabled(args)

    # Start Rerun visualiser if requested
    if viz_enabled:
        from . import visualiser

        visualiser.init(spawn=not args.viz_connect, connect=args.viz_connect)
        _viz_push = visualiser.push_frame
        print("Rerun 3D viewer ready")

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
        choices=["disabled"],
        default="disabled",
        help="Video mode (disabled for Phase A)",
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
