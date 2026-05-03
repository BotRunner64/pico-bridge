from __future__ import annotations

import asyncio
import threading
from types import SimpleNamespace

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
            video_enabled=self.kwargs["video"] is not None,
            video_running=self._video_running,
            video_source=self.kwargs["video"],
        )


def test_package_exports_public_api_lazily():
    assert HAND_JOINT_NAMES[:2] == ("Palm", "Wrist")
    assert PicoBridge.__name__ == "PicoBridge"


def test_pico_bridge_starts_runtime_and_reports_stats(monkeypatch):
    FakeRuntime.instances = []
    monkeypatch.setattr(bridge_mod, "PicoBridgeRuntime", FakeRuntime)

    with PicoBridge(video="realsense", discovery=False, camera_device="RS123") as bridge:
        runtime = FakeRuntime.instances[0]
        assert runtime.kwargs["video"] == "realsense"
        assert runtime.kwargs["discovery"] is False
        assert runtime.kwargs["camera_device"] == "RS123"
        stats = bridge.stats()

    assert stats.connected is True
    assert stats.device_sn == "fake-sn"
    assert stats.video_enabled is True
    assert stats.video_source == "realsense"


def test_pico_bridge_wait_frame_uses_internal_store(monkeypatch):
    FakeRuntime.instances = []
    monkeypatch.setattr(bridge_mod, "PicoBridgeRuntime", FakeRuntime)

    with PicoBridge(video=None, discovery=False) as bridge:
        bridge._frame_store.append_payload({"timeStampNs": 99})
        frame = bridge.wait_frame(timeout=0.01)

    assert frame.timestamp_ns == 99
    assert frame.seq == 1


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
