from __future__ import annotations

import threading
import time

from pico_bridge.frame_store import FrameStore


def test_frame_store_waits_for_new_payload():
    store = FrameStore(history_size=2)
    results = []

    def wait_for_frame() -> None:
        results.append(store.wait_frame(timeout=1.0))

    thread = threading.Thread(target=wait_for_frame)
    thread.start()
    time.sleep(0.01)
    store.append_payload({"timeStampNs": 42})
    thread.join(timeout=1.0)

    assert len(results) == 1
    assert results[0].timestamp_ns == 42
    assert store.latest_frame() is results[0]


def test_frame_store_wait_after_seq_requires_newer_frame():
    store = FrameStore(history_size=2)
    first = store.append_payload({"timeStampNs": 1})

    try:
        store.wait_frame(timeout=0.01, after_seq=first.seq)
    except TimeoutError:
        pass
    else:
        raise AssertionError("wait_frame should time out without a newer frame")

    second = store.append_payload({"timeStampNs": 2})

    assert store.wait_frame(timeout=0.01, after_seq=first.seq) is second


def test_frame_store_counts_ring_overwrites():
    store = FrameStore(history_size=1)
    store.append_payload({"timeStampNs": 1})
    store.append_payload({"timeStampNs": 2})

    stats = store.stats()

    assert stats.frame_count == 2
    assert stats.latest_seq == 2
    assert stats.dropped_ring_frames == 1
