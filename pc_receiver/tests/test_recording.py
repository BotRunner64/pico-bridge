from __future__ import annotations

import json

from pico_bridge.recording import RECORDING_FORMAT, RECORDING_VERSION, TrackingRecorder


def test_tracking_recorder_writes_metadata_and_frames(tmp_path):
    path = tmp_path / "tracking.jsonl"

    with TrackingRecorder(path) as recorder:
        recorder.record_tracking({"timeStampNs": 42, "Head": {"pose": [1, 2, 3, 0, 0, 0, 1]}})

    lines = [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines()]

    assert lines[0]["type"] == "metadata"
    assert lines[0]["format"] == RECORDING_FORMAT
    assert lines[0]["version"] == RECORDING_VERSION
    assert lines[1] == {
        "type": "tracking",
        "seq": 1,
        "recorded_at_ns": lines[1]["recorded_at_ns"],
        "payload": {"timeStampNs": 42, "Head": {"pose": [1, 2, 3, 0, 0, 0, 1]}},
    }
    assert recorder.frame_count == 1


def test_tracking_recorder_accepts_directory_path(tmp_path):
    with TrackingRecorder(tmp_path) as recorder:
        path = recorder.path

    assert path.parent == tmp_path
    assert path.name.startswith("tracking_")
    assert path.suffix == ".jsonl"
