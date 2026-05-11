from __future__ import annotations

import asyncio
import threading
from types import SimpleNamespace

import numpy as np
import pytest
import pico_bridge.bridge as bridge_mod
from pico_bridge import HAND_JOINT_NAMES, PicoBridge


class FakeRuntime:
    instances = []

    def __init__(self, **kwargs):
        self.kwargs = kwargs
        self._event = None
        self._connected = True
        self._device_sn = "fake-sn"
        self._video_running = False
        self._video_enabled = kwargs["video_enabled"]
        self.video_enabled_calls: list[bool] = []
        FakeRuntime.instances.append(self)

    async def run(self):
        self._event = asyncio.Event()
        self.kwargs["on_started"]()
        await self._event.wait()

    def request_stop(self):
        if self._event is not None:
            self._event.set()

    def status(self):
        return SimpleNamespace(
            connected=self._connected,
            device_sn=self._device_sn,
            video_enabled=self._video_enabled,
            video_running=self._video_running,
            video_source=self.kwargs["video"],
        )

    async def set_video_enabled(self, enabled: bool) -> None:
        self._video_enabled = enabled
        self.video_enabled_calls.append(enabled)


def test_package_exports_public_api_lazily():
    assert HAND_JOINT_NAMES[:2] == ("Palm", "Wrist")
    assert PicoBridge.__name__ == "PicoBridge"


def test_pico_bridge_starts_runtime_and_reports_stats(monkeypatch):
    FakeRuntime.instances = []
    monkeypatch.setattr(bridge_mod, "PicoBridgeRuntime", FakeRuntime)

    with PicoBridge(video="frames", discovery=False) as bridge:
        runtime = FakeRuntime.instances[0]
        assert runtime.kwargs["video"] == "frames"
        assert runtime.kwargs["discovery"] is False
        assert runtime.kwargs["video_enabled"] is True
        assert runtime.kwargs["video_frame_source"] is not None
        stats = bridge.stats()

    assert stats.connected is True
    assert stats.device_sn == "fake-sn"
    assert stats.video_enabled is True
    assert stats.video_source == "frames"


def test_pico_bridge_can_toggle_video_enabled_at_runtime(monkeypatch):
    FakeRuntime.instances = []
    monkeypatch.setattr(bridge_mod, "PicoBridgeRuntime", FakeRuntime)

    bridge = PicoBridge(video="frames", discovery=False)
    try:
        bridge.start()
        runtime = FakeRuntime.instances[0]

        bridge.set_video_enabled(False)
        assert runtime.video_enabled_calls == [False]
        assert bridge.stats().video_enabled is False

        bridge.set_video_enabled(True)
        assert runtime.video_enabled_calls == [False, True]
        assert bridge.stats().video_enabled is True
    finally:
        bridge.close()


def test_pico_bridge_rejects_enabling_video_without_source():
    bridge = PicoBridge(video=None, discovery=False)

    with pytest.raises(RuntimeError, match="configured video source"):
        bridge.set_video_enabled(True)


def test_pico_bridge_wait_frame_uses_internal_store(monkeypatch):
    FakeRuntime.instances = []
    monkeypatch.setattr(bridge_mod, "PicoBridgeRuntime", FakeRuntime)

    with PicoBridge(video=None, discovery=False) as bridge:
        bridge._frame_store.append_payload({"timeStampNs": 99})
        frame = bridge.wait_frame(timeout=0.01)

    assert frame.timestamp_ns == 99
    assert frame.seq == 1


def test_pico_bridge_push_video_frame_requires_frames_video():
    bridge = PicoBridge(video=None, discovery=False)

    with pytest.raises(RuntimeError, match='video="frames"'):
        bridge.push_video_frame(np.zeros((4, 8, 3), dtype=np.uint8))


def test_pico_bridge_push_video_frame_stores_latest_frame():
    bridge = PicoBridge(video="frames", discovery=False)
    frame = np.zeros((4, 8, 3), dtype=np.uint8)

    seq = bridge.push_video_frame(frame)
    frame[:, :, :] = 255
    stored, stored_seq, _ = bridge._video_frame_source.latest()

    assert seq == 1
    assert stored_seq == 1
    assert stored is not None
    assert stored.sum() == 0


def test_close_keeps_thread_reference_when_runtime_does_not_stop():
    calls = []
    bridge = PicoBridge(discovery=False)
    thread = _NonStoppingThread()
    bridge._thread = thread
    bridge._runtime = SimpleNamespace(request_stop=lambda: calls.append("stop"))

    bridge.close()

    assert calls == ["stop"]
    assert thread.join_timeout == 5.0
    assert bridge._thread is thread


def test_close_from_runtime_thread_does_not_join_current_thread():
    calls = []
    bridge = PicoBridge(discovery=False)
    bridge._thread = threading.current_thread()
    bridge._runtime = SimpleNamespace(request_stop=lambda: calls.append("stop"))

    bridge.close()

    assert calls == ["stop"]
    assert bridge._thread is threading.current_thread()


def test_cancel_pending_tasks_drains_loop():
    loop = asyncio.new_event_loop()
    cancelled = []

    async def wait_forever():
        try:
            await asyncio.Event().wait()
        finally:
            cancelled.append(True)

    try:
        task = loop.create_task(wait_forever())
        loop.run_until_complete(asyncio.sleep(0))

        bridge_mod._cancel_pending_tasks(loop)

        assert task.cancelled()
        assert cancelled == [True]
    finally:
        loop.close()


class _NonStoppingThread:
    join_timeout: float | None = None

    def join(self, timeout: float | None = None) -> None:
        self.join_timeout = timeout

    def is_alive(self) -> bool:
        return True
