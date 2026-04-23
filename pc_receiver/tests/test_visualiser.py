from __future__ import annotations

import importlib
import sys
import types

import pytest


@pytest.fixture
def visualiser_module(monkeypatch):
    monkeypatch.delitem(sys.modules, "pico_bridge.visualiser", raising=False)
    monkeypatch.setitem(sys.modules, "numpy", types.ModuleType("numpy"))
    monkeypatch.setitem(sys.modules, "rerun", types.ModuleType("rerun"))
    monkeypatch.setitem(
        sys.modules,
        "rerun.blueprint",
        types.ModuleType("rerun.blueprint"),
    )
    module = importlib.import_module("pico_bridge.visualiser")
    yield module
    sys.modules.pop("pico_bridge.visualiser", None)


def test_parse_pose_returns_none_for_non_numeric_values(visualiser_module):
    assert visualiser_module._parse_pose("1,2,3,bad,5,6,7") is None


def test_parse_pose_returns_none_for_non_string_values(visualiser_module):
    assert visualiser_module._parse_pose(["1", "2", "3"]) is None


def test_parse_pose_returns_position_and_rotation(visualiser_module):
    assert visualiser_module._parse_pose("1,2,3,0,0,0,1") == (
        [1.0, 2.0, 3.0],
        [0.0, 0.0, 0.0, 1.0],
    )


def test_log_head_clears_stale_entity_when_pose_missing(visualiser_module):
    calls: list[tuple[str, object]] = []

    class FakeRerun:
        @staticmethod
        def Clear(*, recursive: bool):
            return ("clear", recursive)

        @staticmethod
        def log(path: str, value: object) -> None:
            calls.append((path, value))

    visualiser_module.rr = FakeRerun()
    visualiser_module._log_head({})

    assert calls == [("world/head", ("clear", True))]


def test_push_frame_handles_missing_head_field(visualiser_module):
    calls: list[tuple[str, object]] = []

    class FakeRerun:
        MediaType = types.SimpleNamespace(MARKDOWN="markdown")

        @staticmethod
        def Clear(*, recursive: bool):
            return ("clear", recursive)

        @staticmethod
        def log(path: str, value: object, **_: object) -> None:
            calls.append((path, value))

        @staticmethod
        def TextDocument(text: str, media_type: object):
            return ("text", text, media_type)

    visualiser_module.rr = FakeRerun()
    visualiser_module._initialised = True

    visualiser_module.push_frame({})

    assert ("world/head", ("clear", True)) in calls


def test_log_controller_clears_stale_entity_when_pose_missing(visualiser_module):
    calls: list[tuple[str, object]] = []

    class FakeRerun:
        @staticmethod
        def Clear(*, recursive: bool):
            return ("clear", recursive)

        @staticmethod
        def log(path: str, value: object) -> None:
            calls.append((path, value))

    visualiser_module.rr = FakeRerun()
    visualiser_module._log_controllers({"left": {}, "right": {}})

    assert ("world/ctrl/left", ("clear", True)) in calls
    assert ("world/ctrl/right", ("clear", True)) in calls


def test_log_body_clears_when_all_joint_poses_are_invalid(visualiser_module):
    calls: list[tuple[str, object]] = []

    class FakeRerun:
        @staticmethod
        def Clear(*, recursive: bool):
            return ("clear", recursive)

        @staticmethod
        def log(path: str, value: object) -> None:
            calls.append((path, value))

    visualiser_module.rr = FakeRerun()
    visualiser_module._log_body({"len": 1, "joints": [{"p": "bad"}]})

    assert calls == [("world/body", ("clear", True))]
