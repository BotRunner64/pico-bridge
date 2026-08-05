"""Public in-process PICO bridge SDK entrypoint."""

from __future__ import annotations

import asyncio
import logging
import threading
from dataclasses import dataclass
from typing import Any, Callable, Literal

import numpy as np

from .frame_store import FrameStore
from .frames import PicoFrame
from .runtime import PicoBridgeRuntime
from .stereo import StereoCameraIntrinsics
from .webrtc_sender import ExternalVideoFrameSource

VideoSource = Literal["frames", "test-pattern"]
VideoLayout = Literal["mono", "stereo-sbs"]

log = logging.getLogger("pico_bridge.bridge")


@dataclass(frozen=True)
class PicoBridgeStats:
    connected: bool
    device_sn: str
    frame_count: int
    latest_seq: int
    fps: float
    latest_frame_age_s: float | None
    latest_latency_s: float | None
    dropped_ring_frames: int
    video_enabled: bool
    video_running: bool
    video_source: str | None


class PicoBridge:
    """In-process receiver for PICO tracking and optional PC video preview."""

    def __init__(
        self,
        *,
        host: str = "0.0.0.0",
        port: int = 63901,
        discovery: bool = True,
        advertise_ip: str | None = None,
        video: VideoSource | str | None = None,
        video_enabled: bool | None = None,
        video_layout: VideoLayout | str = "mono",
        stereo_intrinsics: StereoCameraIntrinsics | None = None,
        print_tracking: bool = False,
        history_size: int = 120,
        start_timeout: float = 10.0,
        on_raw_tracking: Callable[[dict[str, Any]], None] | None = None,
    ):
        self.host = host
        self.port = int(port)
        self.discovery = bool(discovery)
        self.advertise_ip = advertise_ip
        self.video = _normalize_video_source(video)
        self.video_layout = _normalize_video_layout(video_layout)
        if stereo_intrinsics is not None and self.video_layout != "stereo-sbs":
            raise ValueError("stereo_intrinsics requires video_layout='stereo-sbs'")
        self.stereo_intrinsics = stereo_intrinsics
        self.video_enabled = self.video is not None if video_enabled is None else bool(video_enabled)
        if self.video_enabled and self.video is None:
            raise ValueError("video_enabled=True requires video='frames' or video='test-pattern'")
        self.print_tracking = print_tracking
        self.start_timeout = float(start_timeout)
        self._frame_store = FrameStore(history_size=history_size)
        self._video_frame_source = ExternalVideoFrameSource() if self.video == "frames" else None
        self._on_raw_tracking = on_raw_tracking
        self._lock = threading.Lock()
        self._thread: threading.Thread | None = None
        self._loop: asyncio.AbstractEventLoop | None = None
        self._runtime: PicoBridgeRuntime | None = None
        self._started_event = threading.Event()
        self._startup_error: BaseException | None = None

    def __enter__(self) -> "PicoBridge":
        self.start()
        return self

    def __exit__(self, exc_type: object, exc: object, tb: object) -> None:
        self.close()

    @property
    def connected(self) -> bool:
        runtime = self._runtime
        return False if runtime is None else runtime.status().connected

    def start(self) -> None:
        with self._lock:
            if self._thread is not None and self._thread.is_alive():
                return
            self._started_event = threading.Event()
            self._startup_error = None
            self._thread = threading.Thread(
                target=self._run_thread,
                name="pico_bridge_runtime",
                daemon=True,
            )
            self._thread.start()

        if not self._started_event.wait(timeout=max(self.start_timeout, 0.0)):
            self.close()
            raise TimeoutError("PICO bridge runtime did not start before timeout")
        if self._startup_error is not None:
            raise RuntimeError("PICO bridge runtime failed to start") from self._startup_error

    def close(self) -> None:
        with self._lock:
            loop = self._loop
            runtime = self._runtime
            thread = self._thread

        if runtime is not None:
            if loop is not None and loop.is_running():
                loop.call_soon_threadsafe(runtime.request_stop)
            else:
                runtime.request_stop()
        if thread is not None:
            if thread is threading.current_thread():
                log.warning("PICO bridge close requested from runtime thread; stop scheduled without join")
            else:
                thread.join(timeout=5.0)
                if thread.is_alive():
                    log.warning("PICO bridge runtime thread did not stop within timeout")

        with self._lock:
            if self._thread is thread and (thread is None or not thread.is_alive()):
                self._thread = None

    def wait_frame(self, timeout: float | None = None, *, after_seq: int | None = None) -> PicoFrame:
        return self._frame_store.wait_frame(timeout=timeout, after_seq=after_seq)

    def latest_frame(self) -> PicoFrame | None:
        return self._frame_store.latest_frame()

    def recent_frames(self) -> tuple[PicoFrame, ...]:
        return self._frame_store.recent_frames()

    def push_video_frame(self, frame: np.ndarray) -> int:
        if self._video_frame_source is None:
            raise RuntimeError('push_video_frame() requires PicoBridge(video="frames")')
        return self._video_frame_source.push(frame)

    def set_video_enabled(self, enabled: bool) -> None:
        enabled = bool(enabled)
        if enabled and self.video is None:
            raise RuntimeError("set_video_enabled(True) requires a configured video source")
        self.video_enabled = enabled

        runtime = self._runtime
        loop = self._loop
        if runtime is None or loop is None or not loop.is_running():
            return

        coro = runtime.set_video_enabled(enabled)
        if self._thread is threading.current_thread():
            loop.create_task(coro)
            return

        future = asyncio.run_coroutine_threadsafe(coro, loop)
        future.result()

    def stats(self) -> PicoBridgeStats:
        frame_stats = self._frame_store.stats()
        runtime = self._runtime
        status = None if runtime is None else runtime.status()
        return PicoBridgeStats(
            connected=False if status is None else status.connected,
            device_sn="" if status is None else status.device_sn,
            frame_count=frame_stats.frame_count,
            latest_seq=frame_stats.latest_seq,
            fps=frame_stats.fps,
            latest_frame_age_s=frame_stats.latest_frame_age_s,
            latest_latency_s=frame_stats.latest_latency_s,
            dropped_ring_frames=frame_stats.dropped_ring_frames,
            video_enabled=self.video_enabled if status is None else status.video_enabled,
            video_running=False if status is None else status.video_running,
            video_source=self.video if status is None else status.video_source,
        )

    def _run_thread(self) -> None:
        loop = asyncio.new_event_loop()
        asyncio.set_event_loop(loop)
        runtime = PicoBridgeRuntime(
            host=self.host,
            port=self.port,
            discovery=self.discovery,
            advertise_ip=self.advertise_ip,
            video=self.video,
            video_enabled=self.video_enabled,
            video_frame_source=self._video_frame_source,
            frame_store=self._frame_store,
            video_layout=self.video_layout,
            stereo_intrinsics=self.stereo_intrinsics,
            print_tracking=self.print_tracking,
            on_raw_tracking=self._on_raw_tracking,
            on_started=self._started_event.set,
        )

        with self._lock:
            self._loop = loop
            self._runtime = runtime

        try:
            loop.run_until_complete(runtime.run())
        except BaseException as exc:
            self._startup_error = exc
            self._started_event.set()
            log.exception("PICO bridge runtime failed")
        finally:
            _cancel_pending_tasks(loop)
            with self._lock:
                if self._runtime is runtime:
                    self._runtime = None
                if self._loop is loop:
                    self._loop = None
                if self._thread is threading.current_thread():
                    self._thread = None
            loop.close()


def _normalize_video_source(video: str | None) -> str | None:
    if video in (None, "", "disabled"):
        return None
    if video not in ("frames", "test-pattern"):
        raise ValueError(f"unsupported video source: {video!r}")
    return video


def _normalize_video_layout(layout: str | None) -> str:
    if layout in (None, "", "mono"):
        return "mono"
    if layout != "stereo-sbs":
        raise ValueError(f"unsupported video layout: {layout!r}")
    return layout


def _cancel_pending_tasks(loop: asyncio.AbstractEventLoop) -> None:
    pending = [task for task in asyncio.all_tasks(loop) if not task.done()]
    if pending:
        for task in pending:
            task.cancel()
        loop.run_until_complete(asyncio.gather(*pending, return_exceptions=True))
    loop.run_until_complete(loop.shutdown_asyncgens())
    loop.run_until_complete(loop.shutdown_default_executor())
