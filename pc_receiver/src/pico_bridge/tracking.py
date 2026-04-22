"""Tracking data parsing — mirrors XRobo JSON format."""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from typing import Any


@dataclass
class TrackingFrame:
    predict_time: float = 0.0
    app_state: dict[str, Any] = field(default_factory=dict)
    head: dict[str, Any] = field(default_factory=dict)
    controller: dict[str, Any] = field(default_factory=dict)
    hand: dict[str, Any] = field(default_factory=dict)
    body: dict[str, Any] = field(default_factory=dict)
    motion: dict[str, Any] = field(default_factory=dict)
    input_mask: int = 0
    timestamp_ns: int = 0
    raw: dict[str, Any] = field(default_factory=dict)

    @classmethod
    def from_json(cls, text: str) -> "TrackingFrame":
        d = json.loads(text)
        return cls(
            predict_time=d.get("predictTime", 0),
            app_state=d.get("appState", {}),
            head=d.get("Head", {}),
            controller=d.get("Controller", {}),
            hand=d.get("Hand", {}),
            body=d.get("Body", {}),
            motion=d.get("Motion", {}),
            input_mask=d.get("Input", 0),
            timestamp_ns=d.get("timeStampNs", 0),
            raw=d,
        )

    def summary(self) -> str:
        parts = []
        if self.head.get("pose"):
            parts.append(f"head={self.head['pose']}")
        for side in ("left", "right"):
            ctrl = self.controller.get(side, {})
            if ctrl.get("pose"):
                parts.append(f"ctrl_{side}={ctrl['pose']}")
        for side in ("leftHand", "rightHand"):
            h = self.hand.get(side, {})
            if h.get("isActive"):
                parts.append(f"{side}=active({h.get('count', 0)}j)")
        body_len = self.body.get("len", 0)
        if body_len:
            parts.append(f"body={body_len}j")
        motion_len = self.motion.get("len", 0)
        if motion_len:
            parts.append(f"motion={motion_len}j")
        return " | ".join(parts) if parts else "(empty)"
