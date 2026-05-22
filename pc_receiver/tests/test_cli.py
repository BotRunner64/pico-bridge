from __future__ import annotations

import asyncio
import json
import sys
import types

from pico_bridge import cli


def _stats(**overrides):
    values = {
        "connected": True,
        "device_sn": "SN123",
        "fps": 59.75,
        "latest_seq": 42,
        "latest_frame_age_s": 0.023,
        "dropped_ring_frames": 0,
        "video_enabled": True,
        "video_running": False,
        "video_source": "frames",
    }
    values.update(overrides)
    return types.SimpleNamespace(**values)


def test_visualiser_enabled_when_connecting_existing_viewer():
    args = cli.build_parser().parse_args(["--viz-connect"])

    assert cli._visualiser_enabled(args) is True


def test_camera_device_helpers_parse_webcam_device():
    assert cli._parse_webcam_device(None) == 0
    assert cli._parse_webcam_device("0") == 0
    assert cli._parse_webcam_device("/dev/video2") == "/dev/video2"


def test_camera_cannot_be_combined_with_video():
    args = cli.build_parser().parse_args(["--camera", "webcam", "--video", "test-pattern"])

    try:
        cli._validate_args(args)
    except ValueError as exc:
        assert "--camera" in str(exc)
    else:
        raise AssertionError("expected validation error")


def test_format_status_includes_compact_runtime_fields():
    line = cli._format_status(_stats())

    assert line == (
        "status connected=1 sn=SN123 fps=59.8 seq=42 "
        "age=0.02s video=frames/idle drops=0"
    )


def test_format_status_handles_missing_frames_and_disabled_video():
    line = cli._format_status(
        _stats(
            connected=False,
            device_sn="",
            latest_frame_age_s=None,
            video_enabled=False,
            video_running=False,
            video_source=None,
        )
    )

    assert "connected=0" in line
    assert "sn=-" in line
    assert "age=n/a" in line
    assert "video=disabled" in line


def test_run_starts_visualiser_for_viz_connect(monkeypatch):
    calls: list[tuple[object, ...]] = []

    fake_visualiser = types.SimpleNamespace(
        init=lambda *, spawn, connect, follow: calls.append(("init", spawn, connect, follow)),
        push_frame=lambda data: calls.append(("push", data)),
        set_connection_state=lambda connected, device_sn: calls.append(
            ("state", connected, device_sn)
        ),
        close=lambda: calls.append(("close",)),
    )

    class FakeBridge:
        def __init__(self, **kwargs):
            self.kwargs = kwargs
            self._stats = _stats(connected=False, device_sn="")

        def start(self) -> None:
            calls.append(("bridge_start", self.kwargs["on_raw_tracking"] is not None))

        def close(self) -> None:
            calls.append(("bridge_close",))

        def stats(self):
            return self._stats

    async def fake_sleep(_: float) -> None:
        raise asyncio.CancelledError

    monkeypatch.delitem(sys.modules, "pico_bridge.visualiser", raising=False)
    monkeypatch.setitem(sys.modules, "pico_bridge.visualiser", fake_visualiser)
    monkeypatch.setattr(sys.modules["pico_bridge"], "visualiser", fake_visualiser, raising=False)
    monkeypatch.setattr(cli, "PicoBridge", FakeBridge)
    monkeypatch.setattr(cli.asyncio, "sleep", fake_sleep)

    args = cli.build_parser().parse_args(
        ["--viz-connect", "--no-discovery", "--no-print-tracking"]
    )

    asyncio.run(cli._run(args))

    assert ("init", False, True, True) in calls
    assert ("bridge_start", True) in calls
    assert ("bridge_close",) in calls
    assert ("close",) in calls


def test_run_records_raw_tracking_frames(tmp_path, monkeypatch):
    path = tmp_path / "tracking.jsonl"
    calls: list[tuple[object, ...]] = []

    class FakeBridge:
        def __init__(self, **kwargs):
            self.kwargs = kwargs

        def start(self) -> None:
            calls.append(("raw_callback", self.kwargs["on_raw_tracking"] is not None))
            self.kwargs["on_raw_tracking"]({"timeStampNs": 42})

        def close(self) -> None:
            calls.append(("bridge_close",))

        def stats(self):
            return _stats()

    async def fake_sleep(_: float) -> None:
        raise asyncio.CancelledError

    monkeypatch.setattr(cli, "PicoBridge", FakeBridge)
    monkeypatch.setattr(cli.asyncio, "sleep", fake_sleep)

    args = cli.build_parser().parse_args(["--no-discovery", "--record", str(path)])
    asyncio.run(cli._run(args))

    lines = [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines()]
    assert ("raw_callback", True) in calls
    assert ("bridge_close",) in calls
    assert lines[0]["type"] == "metadata"
    assert lines[1]["type"] == "tracking"
    assert lines[1]["seq"] == 1
    assert lines[1]["payload"] == {"timeStampNs": 42}


def test_run_passes_quiet_tracking_default(monkeypatch):
    calls: list[tuple[object, ...]] = []

    class FakeBridge:
        def __init__(self, **kwargs):
            self.kwargs = kwargs

        def start(self) -> None:
            calls.append(("print_tracking", self.kwargs["print_tracking"]))

        def close(self) -> None:
            calls.append(("bridge_close",))

        def stats(self):
            return _stats()

    async def fake_sleep(_: float) -> None:
        raise asyncio.CancelledError

    monkeypatch.setattr(cli, "PicoBridge", FakeBridge)
    monkeypatch.setattr(cli.asyncio, "sleep", fake_sleep)

    args = cli.build_parser().parse_args(["--no-discovery"])

    asyncio.run(cli._run(args))

    assert ("print_tracking", False) in calls
    assert ("bridge_close",) in calls


def test_run_uses_sdk_video_policy(monkeypatch):
    calls: list[tuple[object, ...]] = []

    class FakeBridge:
        def __init__(self, **kwargs):
            self.kwargs = kwargs

        def start(self) -> None:
            calls.append(("video", self.kwargs["video"], self.kwargs["video_enabled"]))

        def close(self) -> None:
            calls.append(("bridge_close",))

        def stats(self):
            return _stats(video_source=self.kwargs["video"], video_enabled=self.kwargs["video_enabled"])

    async def fake_sleep(_: float) -> None:
        raise asyncio.CancelledError

    monkeypatch.setattr(cli, "PicoBridge", FakeBridge)
    monkeypatch.setattr(cli.asyncio, "sleep", fake_sleep)

    asyncio.run(cli._run(cli.build_parser().parse_args(["--no-discovery"])))
    asyncio.run(cli._run(cli.build_parser().parse_args(["--no-discovery", "--video", "test-pattern"])))

    assert ("video", None, False) in calls
    assert ("video", "test-pattern", True) in calls


def test_run_uses_sdk_frames_for_camera(monkeypatch):
    calls: list[tuple[object, ...]] = []

    class FakeBridge:
        def __init__(self, **kwargs):
            self.kwargs = kwargs

        def start(self) -> None:
            calls.append(("video", self.kwargs["video"], self.kwargs["video_enabled"]))

        def close(self) -> None:
            calls.append(("bridge_close",))

        def stats(self):
            return _stats(video_source=self.kwargs["video"], video_enabled=self.kwargs["video_enabled"])

    class FakeCameraWorker:
        label = "webcam"

        def __init__(self, bridge, args):
            calls.append(("camera_init", bridge.kwargs["video"], args.camera, args.camera_device))

        def start(self):
            calls.append(("camera_start",))

        def stop(self):
            calls.append(("camera_stop",))

    async def fake_sleep(_: float) -> None:
        raise asyncio.CancelledError

    monkeypatch.setattr(cli, "PicoBridge", FakeBridge)
    monkeypatch.setattr(cli, "_CameraCaptureWorker", FakeCameraWorker)
    monkeypatch.setattr(cli.asyncio, "sleep", fake_sleep)

    args = cli.build_parser().parse_args(["--no-discovery", "--camera", "webcam"])
    asyncio.run(cli._run(args))

    assert ("video", "frames", True) in calls
    assert ("camera_init", "frames", "webcam", None) in calls
    assert ("camera_start",) in calls
    assert ("camera_stop",) in calls
