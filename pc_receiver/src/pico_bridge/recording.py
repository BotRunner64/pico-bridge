"""JSONL tracking recorder for the receiver CLI."""

from __future__ import annotations

import json
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, TextIO

DEFAULT_RECORD_DIR = "pico_bridge_recordings"
RECORDING_FORMAT = "pico_bridge_tracking_jsonl"
RECORDING_VERSION = 1


class TrackingRecorder:
    """Write raw tracking payloads as newline-delimited JSON."""

    def __init__(self, path: str | Path | None = None):
        self.path = _resolve_recording_path(path)
        self._file: TextIO | None = None
        self._seq = 0

    def __enter__(self) -> "TrackingRecorder":
        self.open()
        return self

    def __exit__(self, exc_type: object, exc: object, tb: object) -> None:
        self.close()

    @property
    def frame_count(self) -> int:
        return self._seq

    def open(self) -> None:
        if self._file is not None:
            return
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self._file = self.path.open("w", encoding="utf-8")
        self._write_json(
            {
                "type": "metadata",
                "format": RECORDING_FORMAT,
                "version": RECORDING_VERSION,
                "started_at": _utc_now_iso(),
                "started_at_ns": time.time_ns(),
            }
        )

    def close(self) -> None:
        file = self._file
        self._file = None
        if file is not None:
            file.close()

    def record_tracking(self, payload: dict[str, Any]) -> None:
        if self._file is None:
            raise RuntimeError("tracking recorder is not open")
        self._seq += 1
        self._write_json(
            {
                "type": "tracking",
                "seq": self._seq,
                "recorded_at_ns": time.time_ns(),
                "payload": payload,
            }
        )

    def _write_json(self, value: dict[str, Any]) -> None:
        if self._file is None:
            raise RuntimeError("tracking recorder is not open")
        json.dump(value, self._file, ensure_ascii=False, separators=(",", ":"))
        self._file.write("\n")
        self._file.flush()


def _resolve_recording_path(path: str | Path | None) -> Path:
    if path is None or path == "":
        return Path(DEFAULT_RECORD_DIR) / f"tracking_{datetime.now():%Y%m%d_%H%M%S}.jsonl"
    resolved = Path(path)
    if resolved.exists() and resolved.is_dir():
        return resolved / f"tracking_{datetime.now():%Y%m%d_%H%M%S}.jsonl"
    if str(path).endswith(("/", "\\")):
        return resolved / f"tracking_{datetime.now():%Y%m%d_%H%M%S}.jsonl"
    return resolved


def _utc_now_iso() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
