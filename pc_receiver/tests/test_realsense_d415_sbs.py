from __future__ import annotations

import importlib.util
from pathlib import Path

import numpy as np
import pytest


MODULE_PATH = Path(__file__).parents[1] / "examples" / "realsense_d415_sbs.py"
SPEC = importlib.util.spec_from_file_location("realsense_d415_sbs", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
EXAMPLE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(EXAMPLE)


def test_default_manual_sensor_settings() -> None:
    assert EXAMPLE.DEFAULT_EXPOSURE_US == 30_000.0
    assert EXAMPLE.DEFAULT_GAIN == 48.0


def test_infrared_pair_is_packed_as_three_channel_sbs() -> None:
    left = np.array([[1, 2], [3, 4]], dtype=np.uint8)
    right = np.array([[11, 12], [13, 14]], dtype=np.uint8)

    sbs = EXAMPLE._infrared_pair_to_rgb(left, right)

    assert sbs.shape == (2, 4, 3)
    assert sbs.dtype == np.uint8
    np.testing.assert_array_equal(sbs[:, :2, 0], left)
    np.testing.assert_array_equal(sbs[:, 2:, 0], right)
    np.testing.assert_array_equal(sbs[:, :, 0], sbs[:, :, 1])
    np.testing.assert_array_equal(sbs[:, :, 1], sbs[:, :, 2])


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
def test_infrared_pair_rejects_invalid_inputs(
    left: np.ndarray, right: np.ndarray, message: str
) -> None:
    with pytest.raises(ValueError, match=message):
        EXAMPLE._infrared_pair_to_rgb(left, right)
