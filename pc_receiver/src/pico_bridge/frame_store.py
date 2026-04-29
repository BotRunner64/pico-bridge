"""Thread-safe latest-frame store for the public PICO bridge API."""

from __future__ import annotations

import time
from collections import deque
from dataclasses import dataclass
from threading import Condition
from typing import Any

from .frames import PicoFrame


@dataclass(frozen=True)
class FrameStoreStats:
    frame_count: int
    latest_seq: int
    fps: float
    latest_frame_age_s: float | None
    latest_latency_s: float | None
    dropped_ring_frames: int


class FrameStore:
    """Stores the latest frame and a bounded recent-frame history.

    Consumers use latest-wins semantics: `wait_frame` wakes on a newer frame,
    while the ring buffer exists only for diagnostics and short interpolation
    windows in future consumers.
    """

    def __init__(self, history_size: int = 120):
        self._history_size = max(int(history_size), 1)
        self._condition = Condition()
        self._history: deque[PicoFrame] = deque(maxlen=self._history_size)
        self._latest: PicoFrame | None = None
        self._frame_count = 0
        self._next_seq = 1
        self._fps_window: deque[float] = deque(maxlen=60)
        self._dropped_ring_frames = 0

    def append_payload(self, payload: dict[str, Any]) -> PicoFrame:
        receive_time_s = time.monotonic()
        with self._condition:
            seq = self._next_seq
            self._next_seq += 1
        frame = PicoFrame.from_tracking_payload(
            payload,
            seq=seq,
            receive_time_s=receive_time_s,
        )
        self.append(frame)
        return frame

    def append(self, frame: PicoFrame) -> None:
        with self._condition:
            if len(self._history) == self._history.maxlen:
                self._dropped_ring_frames += 1
            self._history.append(frame)
            self._latest = frame
            self._frame_count += 1
            self._fps_window.append(frame.receive_time_s)
            self._condition.notify_all()

    def latest_frame(self) -> PicoFrame | None:
        with self._condition:
            return self._latest

    def wait_frame(self, timeout: float | None = None, *, after_seq: int | None = None) -> PicoFrame:
        deadline = None if timeout is None else time.monotonic() + max(float(timeout), 0.0)
        with self._condition:
            while True:
                if self._latest is not None and (after_seq is None or self._latest.seq > after_seq):
                    return self._latest
                if deadline is None:
                    self._condition.wait()
                    continue
                remaining = deadline - time.monotonic()
                if remaining <= 0:
                    raise TimeoutError("no PICO tracking frame received before timeout")
                self._condition.wait(timeout=remaining)

    def recent_frames(self) -> tuple[PicoFrame, ...]:
        with self._condition:
            return tuple(self._history)

    def stats(self) -> FrameStoreStats:
        with self._condition:
            latest = self._latest
            timestamps = tuple(self._fps_window)
            frame_count = self._frame_count
            dropped = self._dropped_ring_frames

        if len(timestamps) >= 2:
            elapsed = timestamps[-1] - timestamps[0]
            fps = (len(timestamps) - 1) / elapsed if elapsed > 0 else 0.0
        else:
            fps = 0.0

        latest_frame_age_s = None
        latency = None
        now = time.monotonic()
        if latest is not None:
            latest_frame_age_s = max(0.0, now - latest.receive_time_s)
        if latest is not None and latest.timestamp_ns > 0:
            # The headset timestamp uses its own clock; expose a best-effort
            # receive age only when future clock mapping is added. For now,
            # latest_latency_s remains None rather than implying synced clocks.
            latency = None

        return FrameStoreStats(
            frame_count=frame_count,
            latest_seq=0 if latest is None else latest.seq,
            fps=fps,
            latest_frame_age_s=latest_frame_age_s,
            latest_latency_s=latency,
            dropped_ring_frames=dropped,
        )
