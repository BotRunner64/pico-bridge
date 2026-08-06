"""Asynchronous lossless FFV1 recording for diagnostic video sources."""

from __future__ import annotations

import json
import queue
import threading
import time
from dataclasses import asdict, dataclass
from datetime import datetime
from fractions import Fraction
from pathlib import Path
from typing import Any

import av
import numpy as np


_STOP = object()


@dataclass(frozen=True, slots=True)
class RecordingResult:
    video_path: Path
    metadata_path: Path
    summary_path: Path
    enqueued_frames: int
    written_frames: int
    queue_drops: int
    duration_seconds: float
    error: str | None


class Ffv1Recorder:
    """Write full-resolution BGR frames to FFV1/Matroska off the capture thread."""

    def __init__(
        self,
        video_path: Path,
        *,
        fps: int,
        frame_size: tuple[int, int],
        queue_size: int = 60,
        source_metadata: dict[str, Any] | None = None,
    ) -> None:
        if fps <= 0:
            raise ValueError("fps must be positive")
        if frame_size[0] <= 0 or frame_size[1] <= 0:
            raise ValueError("frame_size values must be positive")
        if queue_size <= 0:
            raise ValueError("queue_size must be positive")

        self.video_path = Path(video_path)
        if self.video_path.suffix.lower() != ".mkv":
            raise ValueError("FFV1 recordings must use the .mkv extension")
        self.metadata_path = self.video_path.with_suffix(".frames.jsonl")
        self.summary_path = self.video_path.with_suffix(".summary.json")
        self.fps = fps
        self.frame_size = frame_size
        self._queue: queue.Queue[object] = queue.Queue(maxsize=queue_size)
        self._lock = threading.Lock()
        self._accepting = True
        self._enqueued_frames = 0
        self._written_frames = 0
        self._queue_drops = 0
        self._error: str | None = None
        self._result: RecordingResult | None = None
        self._started_monotonic = time.monotonic()
        self._started_at = datetime.now().astimezone().isoformat()

        self.video_path.parent.mkdir(parents=True, exist_ok=True)
        self._container = av.open(str(self.video_path), mode="w")
        self._stream = self._container.add_stream("ffv1", rate=fps)
        self._stream.width = frame_size[0]
        self._stream.height = frame_size[1]
        self._stream.pix_fmt = "bgr0"
        self._metadata_file = self.metadata_path.open("w", encoding="utf-8")
        self._write_metadata(
            {
                "type": "header",
                "codec": "FFV1",
                "container": "Matroska",
                "pixel_format": "bgr0",
                "width": frame_size[0],
                "height": frame_size[1],
                "fps": fps,
                "started_at": self._started_at,
                "source": source_metadata or {},
            }
        )
        self._metadata_file.flush()
        self._thread = threading.Thread(
            target=self._run,
            name="pico-bridge-ffv1-recorder",
            daemon=True,
        )
        self._thread.start()

    @property
    def is_recording(self) -> bool:
        with self._lock:
            return self._accepting and self._error is None

    @property
    def enqueued_frames(self) -> int:
        with self._lock:
            return self._enqueued_frames

    @property
    def written_frames(self) -> int:
        with self._lock:
            return self._written_frames

    @property
    def queue_drops(self) -> int:
        with self._lock:
            return self._queue_drops

    @property
    def pending_frames(self) -> int:
        return self._queue.qsize()

    @property
    def error(self) -> str | None:
        with self._lock:
            return self._error

    def enqueue(self, frame: np.ndarray, metadata: dict[str, Any]) -> bool:
        """Queue one immutable BGR uint8 frame without blocking capture."""
        expected_width, expected_height = self.frame_size
        if frame.dtype != np.uint8 or frame.shape != (expected_height, expected_width, 3):
            raise ValueError(
                "frame must be uint8 BGR with shape "
                f"({expected_height}, {expected_width}, 3), got {frame.dtype} {frame.shape}"
            )
        with self._lock:
            if not self._accepting or self._error is not None:
                return False
        try:
            self._queue.put_nowait((frame, dict(metadata)))
        except queue.Full:
            with self._lock:
                self._queue_drops += 1
            return False
        with self._lock:
            self._enqueued_frames += 1
        return True

    def stop(self) -> RecordingResult:
        with self._lock:
            if self._result is not None:
                return self._result
            self._accepting = False

        while self._thread.is_alive():
            try:
                self._queue.put(_STOP, timeout=0.1)
                break
            except queue.Full:
                continue
        self._thread.join()

        duration_seconds = time.monotonic() - self._started_monotonic
        result = RecordingResult(
            video_path=self.video_path,
            metadata_path=self.metadata_path,
            summary_path=self.summary_path,
            enqueued_frames=self.enqueued_frames,
            written_frames=self.written_frames,
            queue_drops=self.queue_drops,
            duration_seconds=duration_seconds,
            error=self.error,
        )
        summary = asdict(result)
        summary["video_path"] = str(result.video_path)
        summary["metadata_path"] = str(result.metadata_path)
        summary["summary_path"] = str(result.summary_path)
        summary["started_at"] = self._started_at
        summary["stopped_at"] = datetime.now().astimezone().isoformat()
        self.summary_path.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
        with self._lock:
            self._result = result
        return result

    def _write_metadata(self, row: dict[str, Any]) -> None:
        self._metadata_file.write(json.dumps(row, separators=(",", ":")) + "\n")

    def _run(self) -> None:
        try:
            while True:
                item = self._queue.get()
                if item is _STOP:
                    break
                frame, metadata = item
                video_frame = av.VideoFrame.from_ndarray(frame, format="bgr24")
                with self._lock:
                    recording_frame_index = self._written_frames
                video_frame.pts = recording_frame_index
                video_frame.time_base = Fraction(1, self.fps)
                for packet in self._stream.encode(video_frame):
                    self._container.mux(packet)
                self._write_metadata(
                    {
                        "type": "frame",
                        "recording_frame_index": recording_frame_index,
                        **metadata,
                    }
                )
                with self._lock:
                    self._written_frames += 1
                    written_frames = self._written_frames
                if written_frames % self.fps == 0:
                    self._metadata_file.flush()

            for packet in self._stream.encode():
                self._container.mux(packet)
        except Exception as exc:
            with self._lock:
                self._error = f"{type(exc).__name__}: {exc}"
                self._accepting = False
        finally:
            try:
                self._container.close()
            finally:
                self._metadata_file.flush()
                self._metadata_file.close()
