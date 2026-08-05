"""Stereo video calibration shared by the public SDK and control protocol."""

from __future__ import annotations

import math
from dataclasses import dataclass


@dataclass(frozen=True)
class StereoCameraIntrinsics:
    """Rectified pinhole intrinsics for one eye of an SBS video frame.

    ``cx`` and ``cy`` use the conventional image coordinate system whose
    origin is the top-left corner. The values are normalized before they are
    sent to the headset, so later video resizing does not change the
    projection.
    """

    eye_width: int
    eye_height: int
    fx: float
    fy: float
    cx: float
    cy: float

    def __post_init__(self) -> None:
        if self.eye_width <= 0 or self.eye_height <= 0:
            raise ValueError("stereo eye dimensions must be positive")

        values = (self.fx, self.fy, self.cx, self.cy)
        if not all(math.isfinite(value) for value in values):
            raise ValueError("stereo intrinsics must be finite")
        if self.fx <= 0 or self.fy <= 0:
            raise ValueError("stereo focal lengths must be positive")

    def normalized(self) -> dict[str, float]:
        """Return resolution-independent pinhole intrinsics."""

        return {
            "stereo_fx_norm": self.fx / self.eye_width,
            "stereo_fy_norm": self.fy / self.eye_height,
            "stereo_cx_norm": self.cx / self.eye_width,
            "stereo_cy_norm": self.cy / self.eye_height,
        }
