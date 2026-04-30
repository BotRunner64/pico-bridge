from __future__ import annotations

import asyncio
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
        "video_source": "realsense",
    }
    values.update(overrides)
    return types.SimpleNamespace(**values)


def test_visualiser_enabled_when_connecting_existing_viewer():
    args = cli.build_parser().parse_args(["--viz-connect"])

    assert cli._visualiser_enabled(args) is True


def test_format_status_includes_compact_runtime_fields():
    line = cli._format_status(_stats())

    assert line == (
        "status connected=1 sn=SN123 fps=59.8 seq=42 "
        "age=0.02s video=realsense/idle drops=0"
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
