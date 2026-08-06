from __future__ import annotations

import importlib.util
from pathlib import Path

import numpy as np


MODULE_PATH = Path(__file__).parents[1] / "examples" / "zed_mini_viewer.py"
SPEC = importlib.util.spec_from_file_location("zed_mini_viewer", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
VIEWER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VIEWER)


def test_isolated_middle_frame_is_detected() -> None:
    before = np.zeros((8, 8), dtype=np.float64)
    middle = np.full((8, 8), 100.0)
    after = np.zeros((8, 8), dtype=np.float64)

    metrics = VIEWER._triplet_metrics(before, middle, after)

    assert VIEWER._is_isolated_frame(metrics)


def test_continuous_change_is_not_isolated() -> None:
    before = np.zeros((8, 8), dtype=np.float64)
    middle = np.full((8, 8), 50.0)
    after = np.full((8, 8), 100.0)

    metrics = VIEWER._triplet_metrics(before, middle, after)

    assert not VIEWER._is_isolated_frame(metrics)
