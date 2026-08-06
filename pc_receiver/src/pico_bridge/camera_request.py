"""Video preview request types for WebRTC video."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any


MIN_VIDEO_BITRATE = 250_000
MAX_VIDEO_BITRATE = 50_000_000


@dataclass
class CameraRequest:
    """Parsed StartReceivePcCamera parameters."""

    ip: str = "0.0.0.0"
    port: int = 0
    width: int = 1280
    height: int = 720
    fps: int = 30
    bitrate: int = 8 * 1024 * 1024
    codec: str = "webrtc"
    source: str = "frames"

    def __post_init__(self) -> None:
        self.bitrate = int(self.bitrate)
        if not MIN_VIDEO_BITRATE <= self.bitrate <= MAX_VIDEO_BITRATE:
            raise ValueError(
                "video bitrate must be between "
                f"{MIN_VIDEO_BITRATE} and {MAX_VIDEO_BITRATE} bits per second"
            )

    @classmethod
    def from_json(cls, obj: dict[str, Any]) -> "CameraRequest":
        codec = str(obj.get("codec", "webrtc"))
        if codec != "webrtc":
            raise ValueError(f"unsupported camera codec: {codec}")
        return cls(
            ip=str(obj.get("ip", "0.0.0.0")),
            port=int(obj.get("port", 0)),
            width=int(obj.get("width", 1280)),
            height=int(obj.get("height", 720)),
            fps=int(obj.get("fps", 30)),
            bitrate=int(obj.get("bitrate", 8 * 1024 * 1024)),
            codec=codec,
            source=str(obj.get("source", "frames")),
        )
