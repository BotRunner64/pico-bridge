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


def _on_tracking(data: dict[str, Any]) -> None:
    global _frame_count
    _frame_count += 1
    frame = TrackingFrame.from_json(
        json.dumps(data)
    ) if isinstance(data, dict) else TrackingFrame.from_json(data)
    print(f"[{_frame_count:>6}] {frame.summary()}", flush=True)


def _on_function(name: str, value: Any) -> None:
    print(f"  fn: {name} = {value}", flush=True)


async def _run(args: argparse.Namespace) -> None:
    server = PicoBridgeServer(
        host="0.0.0.0",
        port=args.tcp_port,
        on_tracking=_on_tracking if args.print_tracking else None,
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
    except asyncio.CancelledError:
        pass
    finally:
        await broadcaster.stop()
        await server.stop()


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
