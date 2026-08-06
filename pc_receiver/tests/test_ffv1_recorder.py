from __future__ import annotations

import json

import av
import numpy as np

from pico_bridge.ffv1_recorder import Ffv1Recorder


def test_ffv1_recorder_writes_lossless_video_and_metadata(tmp_path) -> None:
    video_path = tmp_path / "capture.mkv"
    recorder = Ffv1Recorder(
        video_path,
        fps=30,
        frame_size=(64, 32),
        queue_size=8,
        source_metadata={"camera": "test"},
    )
    frames = [
        np.random.default_rng(seed).integers(0, 256, (32, 64, 3), dtype=np.uint8)
        for seed in range(3)
    ]
    for source_frame_index, frame in enumerate(frames, 10):
        assert recorder.enqueue(frame, {"source_frame_index": source_frame_index})

    result = recorder.stop()

    assert result.error is None
    assert result.enqueued_frames == 3
    assert result.written_frames == 3
    assert result.queue_drops == 0
    with av.open(str(video_path)) as container:
        decoded = [frame.to_ndarray(format="bgr24") for frame in container.decode(video=0)]
    assert len(decoded) == len(frames)
    for expected, actual in zip(frames, decoded, strict=True):
        np.testing.assert_array_equal(actual, expected)

    rows = [json.loads(line) for line in result.metadata_path.read_text().splitlines()]
    assert rows[0]["codec"] == "FFV1"
    assert rows[0]["source"] == {"camera": "test"}
    assert [row["source_frame_index"] for row in rows[1:]] == [10, 11, 12]
    summary = json.loads(result.summary_path.read_text())
    assert summary["written_frames"] == 3
    assert summary["queue_drops"] == 0


def test_ffv1_recorder_rejects_incompatible_frames(tmp_path) -> None:
    recorder = Ffv1Recorder(tmp_path / "capture.mkv", fps=30, frame_size=(64, 32))

    try:
        bad_frame = np.zeros((32, 63, 3), dtype=np.uint8)
        try:
            recorder.enqueue(bad_frame, {})
        except ValueError as exc:
            assert "frame must be uint8 BGR" in str(exc)
        else:
            raise AssertionError("incompatible frame was accepted")
    finally:
        recorder.stop()
