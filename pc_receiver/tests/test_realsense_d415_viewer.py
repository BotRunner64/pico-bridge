from __future__ import annotations

import importlib.util
from pathlib import Path

import numpy as np
import pytest


MODULE_PATH = Path(__file__).parents[1] / "examples" / "realsense_d415_viewer.py"
SPEC = importlib.util.spec_from_file_location("realsense_d415_viewer", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
VIEWER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VIEWER)


def test_default_manual_sensor_settings_match_sender() -> None:
    assert VIEWER.DEFAULT_EXPOSURE_US == 150_000.0
    assert VIEWER.DEFAULT_GAIN == 16.0


def test_pair_to_bgr_preserves_raw_grayscale_values() -> None:
    left = np.array([[1, 2], [3, 4]], dtype=np.uint8)
    right = np.array([[11, 12], [13, 14]], dtype=np.uint8)

    sbs = VIEWER._pair_to_bgr(left, right)

    assert sbs.shape == (2, 4, 3)
    np.testing.assert_array_equal(sbs[:, :2, 0], left)
    np.testing.assert_array_equal(sbs[:, 2:, 0], right)
    np.testing.assert_array_equal(sbs[:, :, 0], sbs[:, :, 1])
    np.testing.assert_array_equal(sbs[:, :, 1], sbs[:, :, 2])


def test_auto_contrast_uses_one_range_for_both_eyes() -> None:
    left = np.arange(0, 64, dtype=np.uint8).reshape(8, 8)
    right = np.arange(64, 128, dtype=np.uint8).reshape(8, 8)

    enhanced_left, enhanced_right, low, high = VIEWER._auto_contrast_pair(
        left, right
    )

    assert low < high
    assert enhanced_left.dtype == np.uint8
    assert enhanced_right.dtype == np.uint8
    assert int(enhanced_left.mean()) < int(enhanced_right.mean())
    assert int(enhanced_left.min()) == 0
    assert int(enhanced_right.max()) == 255


def test_luma_stats_report_raw_min_mean_and_max() -> None:
    frame = np.array([[0, 10], [20, 30]], dtype=np.uint8)

    assert VIEWER._luma_stats(frame) == (0, 15.0, 30)


@pytest.mark.parametrize(
    ("left", "right", "message"),
    [
        (
            np.zeros((2, 2), dtype=np.float32),
            np.zeros((2, 2), dtype=np.uint8),
            "dtype uint8",
        ),
        (
            np.zeros((2, 2, 1), dtype=np.uint8),
            np.zeros((2, 2, 1), dtype=np.uint8),
            "two-dimensional",
        ),
        (
            np.zeros((2, 2), dtype=np.uint8),
            np.zeros((2, 3), dtype=np.uint8),
            "matching shapes",
        ),
    ],
)
def test_pair_rejects_invalid_inputs(
    left: np.ndarray, right: np.ndarray, message: str
) -> None:
    with pytest.raises(ValueError, match=message):
        VIEWER._pair_to_bgr(left, right)
