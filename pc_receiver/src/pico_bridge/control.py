"""Logical control messages sent over the existing TCP function channel."""

from __future__ import annotations

from typing import Any

from .stereo import StereoCameraIntrinsics

CONTROL_FUNCTION_NAME = "BridgeControl"
CONTROL_VERSION = 1
CONTROL_CHANNEL_VIDEO = "video"
CONTROL_TYPE_SET_POLICY = "set_policy"


def build_control_message(channel: str, message_type: str, payload: dict[str, Any]) -> dict[str, Any]:
    return {
        "version": CONTROL_VERSION,
        "channel": channel,
        "type": message_type,
        "payload": payload,
    }


def build_video_policy_message(
    *,
    enabled: bool,
    source: str | None,
    layout: str = "mono",
    stereo_intrinsics: StereoCameraIntrinsics | None = None,
) -> dict[str, Any]:
    payload: dict[str, Any] = {
        "enabled": bool(enabled),
        "auto_preview": bool(enabled),
        "layout": layout,
    }
    if source is not None:
        payload["source"] = source
    if stereo_intrinsics is not None:
        payload.update(stereo_intrinsics.normalized())
    return build_control_message(CONTROL_CHANNEL_VIDEO, CONTROL_TYPE_SET_POLICY, payload)
