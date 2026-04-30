"""PICO Bridge PC SDK."""

from __future__ import annotations

from typing import Any

__all__ = [
    "BODY_JOINT_NAMES",
    "BODY_JOINT_PARENTS",
    "HAND_JOINT_NAMES",
    "BodyFrame",
    "ControllerState",
    "ControllersFrame",
    "HandFrame",
    "PicoBridge",
    "PicoBridgeStats",
    "PicoFrame",
    "Pose",
    "cli",
    "discovery",
    "protocol",
    "tcp_server",
]


def __getattr__(name: str) -> Any:
    if name in ("PicoBridge", "PicoBridgeStats"):
        from .bridge import PicoBridge, PicoBridgeStats

        return {"PicoBridge": PicoBridge, "PicoBridgeStats": PicoBridgeStats}[name]
    if name in (
        "BODY_JOINT_NAMES",
        "BODY_JOINT_PARENTS",
        "HAND_JOINT_NAMES",
        "BodyFrame",
        "ControllerState",
        "ControllersFrame",
        "HandFrame",
        "PicoFrame",
        "Pose",
    ):
        from . import frames

        return getattr(frames, name)
    raise AttributeError(f"module {__name__!r} has no attribute {name!r}")
