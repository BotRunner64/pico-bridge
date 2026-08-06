"""View ZED Mini stereo source frames locally before PicoBridge or WebRTC."""

from __future__ import annotations

import argparse
import tempfile
import time
from collections import deque
from pathlib import Path

import numpy as np

from pico_bridge.ffv1_recorder import Ffv1Recorder, RecordingResult


MAIN_WINDOW = "ZED Mini source (before PicoBridge/WebRTC)"
EVENT_WINDOW = "Last isolated frame: before | suspect | after"
SAMPLE_STEP = 12
OUTLIER_MIN_DIFF = 8.0
OUTLIER_RETURN_RATIO = 0.25
STATUS_HEIGHT = 112


def _sample_luma(frame: np.ndarray) -> np.ndarray:
    pixels = frame[::SAMPLE_STEP, ::SAMPLE_STEP].astype(np.int16)
    return pixels.mean(axis=2)


def _triplet_metrics(
    before: np.ndarray,
    middle: np.ndarray,
    after: np.ndarray,
) -> tuple[float, float, float]:
    before_middle = float(np.mean(np.abs(before - middle)))
    middle_after = float(np.mean(np.abs(middle - after)))
    before_after = float(np.mean(np.abs(before - after)))
    return before_middle, middle_after, before_after


def _is_isolated_frame(metrics: tuple[float, float, float]) -> bool:
    before_middle, middle_after, before_after = metrics
    minimum_change = min(before_middle, middle_after)
    return (
        minimum_change >= OUTLIER_MIN_DIFF
        and before_after <= minimum_change * OUTLIER_RETURN_RATIO
    )


def _put_text(image: np.ndarray, text: str, origin: tuple[int, int], color: tuple[int, int, int]) -> None:
    import cv2

    cv2.putText(
        image,
        text,
        origin,
        cv2.FONT_HERSHEY_SIMPLEX,
        0.7,
        color,
        2,
        cv2.LINE_AA,
    )


def _status_frame(
    frame: np.ndarray,
    *,
    requested_fps: int,
    measured_fps: float,
    camera_fps: float,
    camera_drops: int,
    sdk_corrupted: int,
    isolated_frames: int,
    recording: bool,
    recorded_frames: int,
    recording_pending: int,
    recording_queue_drops: int,
    warning: str | None,
) -> np.ndarray:
    import cv2

    canvas = cv2.copyMakeBorder(
        frame,
        STATUS_HEIGHT,
        0,
        0,
        0,
        cv2.BORDER_CONSTANT,
        value=(18, 18, 18),
    )
    _put_text(
        canvas,
        "DIRECT ZED SDK SIDE_BY_SIDE - before PicoBridge / WebRTC",
        (18, 29),
        (90, 230, 90),
    )
    if recording:
        _put_text(canvas, "REC", (canvas.shape[1] - 92, 29), (0, 0, 255))
    recording_status = "R: start recording  S: snapshot  Q/Esc: quit"
    if recording or recorded_frames or recording_queue_drops:
        recording_status = (
            f"REC written {recorded_frames} pending {recording_pending} "
            f"queue-drop {recording_queue_drops}"
        )
    _put_text(
        canvas,
        (
            f"HD720@{requested_fps}  loop {measured_fps:5.1f} fps  camera {camera_fps:5.1f} fps  "
            f"SDK drops {camera_drops}  SDK corrupt {sdk_corrupted}  isolated {isolated_frames}"
        ),
        (18, 63),
        (235, 235, 235),
    )
    _put_text(
        canvas,
        recording_status,
        (18, 96),
        (0, 0, 255) if recording else (180, 180, 180),
    )
    _put_text(canvas, "LEFT", (18, STATUS_HEIGHT + 30), (255, 220, 80))
    _put_text(
        canvas,
        "RIGHT",
        (frame.shape[1] // 2 + 18, STATUS_HEIGHT + 30),
        (255, 220, 80),
    )
    if warning:
        cv2.rectangle(canvas, (3, 3), (canvas.shape[1] - 4, canvas.shape[0] - 4), (0, 0, 255), 8)
        _put_text(canvas, warning, (18, canvas.shape[0] - 22), (0, 0, 255))
    return canvas


def _event_preview(before: np.ndarray, middle: np.ndarray, after: np.ndarray) -> np.ndarray:
    import cv2

    target_width = 840
    panels = []
    for label, frame, color in (
        ("BEFORE", before, (90, 230, 90)),
        ("SUSPECT", middle, (0, 0, 255)),
        ("AFTER", after, (90, 230, 90)),
    ):
        height = max(1, round(frame.shape[0] * target_width / frame.shape[1]))
        panel = cv2.resize(frame, (target_width, height), interpolation=cv2.INTER_AREA)
        panel = cv2.copyMakeBorder(panel, 48, 6, 6, 6, cv2.BORDER_CONSTANT, value=(18, 18, 18))
        _put_text(panel, label, (14, 33), color)
        if label == "SUSPECT":
            cv2.rectangle(panel, (2, 2), (panel.shape[1] - 3, panel.shape[0] - 3), color, 6)
        panels.append(panel)
    return np.hstack(panels)


def _save_frame(path: Path, frame: np.ndarray) -> None:
    import cv2

    path.parent.mkdir(parents=True, exist_ok=True)
    if not cv2.imwrite(str(path), frame, [cv2.IMWRITE_JPEG_QUALITY, 95]):
        raise RuntimeError(f"failed to save frame: {path}")


def _save_triplet(
    capture_dir: Path,
    middle_index: int,
    before: np.ndarray,
    middle: np.ndarray,
    after: np.ndarray,
) -> Path:
    event_dir = capture_dir / f"isolated_{time.strftime('%Y%m%d_%H%M%S')}_frame{middle_index:06d}"
    _save_frame(event_dir / "before.jpg", before)
    _save_frame(event_dir / "suspect.jpg", middle)
    _save_frame(event_dir / "after.jpg", after)
    return event_dir


def _recording_finished_message(result: RecordingResult) -> str:
    status = (
        f"Recording saved: {result.video_path} ({result.written_frames} frames, "
        f"queue drops {result.queue_drops})"
    )
    if result.error:
        status += f"; writer error: {result.error}"
    print(status, flush=True)
    print(f"Frame metadata: {result.metadata_path}", flush=True)
    print(f"Recording summary: {result.summary_path}", flush=True)
    return status


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fps", type=int, choices=(15, 30, 60), default=60)
    parser.add_argument("--window-width", type=int, default=1600)
    parser.add_argument(
        "--capture-dir",
        type=Path,
        default=Path(tempfile.gettempdir()) / "zed-mini-viewer",
    )
    parser.add_argument(
        "--no-validity-check",
        action="store_true",
        help="disable the SDK image validity check while retaining the triplet detector",
    )
    parser.add_argument(
        "--record-queue-size",
        type=int,
        default=60,
        help="maximum frames buffered for the background FFV1 writer",
    )
    args = parser.parse_args()

    import cv2
    import pyzed.sl as sl

    zed = sl.Camera()
    init = sl.InitParameters()
    init.camera_resolution = sl.RESOLUTION.HD720
    init.camera_fps = args.fps
    init.depth_mode = sl.DEPTH_MODE.NONE
    init.enable_image_validity_check = 0 if args.no_validity_check else 1

    result = zed.open(init)
    if result != sl.ERROR_CODE.SUCCESS:
        raise RuntimeError(f"failed to open ZED camera: {result}")

    args.capture_dir.mkdir(parents=True, exist_ok=True)
    print("Showing direct ZED SDK SIDE_BY_SIDE frames before PicoBridge/WebRTC.")
    print(f"Automatic captures: {args.capture_dir}")
    print("Press R to start/stop lossless recording, S to save a frame, or Q/Esc to quit.")

    cv2.namedWindow(MAIN_WINDOW, cv2.WINDOW_NORMAL | cv2.WINDOW_KEEPRATIO)
    source_width = int(zed.get_camera_information().camera_configuration.resolution.width) * 2
    source_height = (
        int(zed.get_camera_information().camera_configuration.resolution.height)
        + STATUS_HEIGHT
    )
    window_height = max(320, round(source_height * args.window_width / source_width))
    cv2.resizeWindow(MAIN_WINDOW, args.window_width, window_height)

    sbs = sl.Mat()
    samples: deque[np.ndarray] = deque(maxlen=3)
    frames: deque[np.ndarray] = deque(maxlen=3)
    frame_index = 0
    sdk_corrupted = 0
    isolated_frames = 0
    measured_fps = 0.0
    previous_loop_time = time.monotonic()
    warning = None
    warning_until = 0.0
    last_event_preview = None
    corrupted_code = getattr(sl.ERROR_CODE, "CORRUPTED_FRAME", None)
    recorder: Ffv1Recorder | None = None
    last_recorded_frames = 0
    last_recording_queue_drops = 0

    try:
        while True:
            grab_result = zed.grab()
            is_sdk_corrupted = corrupted_code is not None and grab_result == corrupted_code
            if grab_result != sl.ERROR_CODE.SUCCESS and not is_sdk_corrupted:
                continue

            retrieve_result = zed.retrieve_image(sbs, sl.VIEW.SIDE_BY_SIDE)
            if retrieve_result != sl.ERROR_CODE.SUCCESS:
                continue

            frame = np.ascontiguousarray(sbs.get_data()[..., :3])
            frame_index += 1
            image_timestamp_ns = int(
                zed.get_timestamp(sl.TIME_REFERENCE.IMAGE).get_nanoseconds()
            )
            now = time.monotonic()
            interval = now - previous_loop_time
            previous_loop_time = now
            if interval > 0:
                instantaneous_fps = 1.0 / interval
                measured_fps = instantaneous_fps if measured_fps == 0 else measured_fps * 0.9 + instantaneous_fps * 0.1

            if is_sdk_corrupted:
                sdk_corrupted += 1
                warning = f"SDK CORRUPTED_FRAME at source frame {frame_index}"
                warning_until = now + 2.0
                path = args.capture_dir / f"sdk_corrupt_{time.strftime('%Y%m%d_%H%M%S')}_frame{frame_index:06d}.jpg"
                _save_frame(path, frame)
                print(f"{warning}; saved {path}", flush=True)

            samples.append(_sample_luma(frame))
            frames.append(frame)
            isolated_middle_index = None
            if len(samples) == 3:
                metrics = _triplet_metrics(samples[0], samples[1], samples[2])
                if _is_isolated_frame(metrics):
                    isolated_frames += 1
                    middle_index = frame_index - 1
                    isolated_middle_index = middle_index
                    event_dir = _save_triplet(
                        args.capture_dir,
                        middle_index,
                        frames[0],
                        frames[1],
                        frames[2],
                    )
                    last_event_preview = _event_preview(frames[0], frames[1], frames[2])
                    cv2.namedWindow(EVENT_WINDOW, cv2.WINDOW_NORMAL | cv2.WINDOW_KEEPRATIO)
                    cv2.resizeWindow(EVENT_WINDOW, args.window_width, max(300, window_height // 2))
                    before_middle, middle_after, before_after = metrics
                    warning = f"ISOLATED source frame {middle_index} saved"
                    warning_until = now + 2.0
                    print(
                        f"{warning}: before-middle={before_middle:.2f}, "
                        f"middle-after={middle_after:.2f}, before-after={before_after:.2f}; "
                        f"triplet {event_dir}",
                        flush=True,
                    )

            if recorder is not None and recorder.is_recording:
                recorder.enqueue(
                    frame,
                    {
                        "source_frame_index": frame_index,
                        "image_timestamp_ns": image_timestamp_ns,
                        "capture_monotonic_ns": time.monotonic_ns(),
                        "grab_status": str(grab_result),
                        "sdk_corrupted": is_sdk_corrupted,
                        "camera_drops": int(zed.get_frame_dropped_count()),
                        "isolated_middle_frame": isolated_middle_index,
                    },
                )
            if recorder is not None and recorder.error is not None:
                result = recorder.stop()
                last_recorded_frames = result.written_frames
                last_recording_queue_drops = result.queue_drops
                warning = _recording_finished_message(result)
                warning_until = now + 4.0
                recorder = None

            active_warning = warning if now < warning_until else None
            recording = recorder is not None and recorder.is_recording
            recorded_frames = recorder.written_frames if recorder else last_recorded_frames
            recording_pending = recorder.pending_frames if recorder else 0
            recording_queue_drops = (
                recorder.queue_drops if recorder else last_recording_queue_drops
            )
            display = _status_frame(
                frame,
                requested_fps=args.fps,
                measured_fps=measured_fps,
                camera_fps=float(zed.get_current_fps()),
                camera_drops=int(zed.get_frame_dropped_count()),
                sdk_corrupted=sdk_corrupted,
                isolated_frames=isolated_frames,
                recording=recording,
                recorded_frames=recorded_frames,
                recording_pending=recording_pending,
                recording_queue_drops=recording_queue_drops,
                warning=active_warning,
            )
            cv2.imshow(MAIN_WINDOW, display)
            if last_event_preview is not None:
                cv2.imshow(EVENT_WINDOW, last_event_preview)

            key = cv2.waitKey(1) & 0xFF
            if key in (27, ord("q"), ord("Q")):
                break
            if key in (ord("r"), ord("R")):
                if recorder is not None:
                    result = recorder.stop()
                    last_recorded_frames = result.written_frames
                    last_recording_queue_drops = result.queue_drops
                    warning = _recording_finished_message(result)
                    warning_until = time.monotonic() + 4.0
                    recorder = None
                else:
                    recording_path = args.capture_dir / (
                        f"recording_{time.strftime('%Y%m%d_%H%M%S')}_"
                        f"frame{frame_index:06d}.mkv"
                    )
                    try:
                        recorder = Ffv1Recorder(
                            recording_path,
                            fps=args.fps,
                            frame_size=(frame.shape[1], frame.shape[0]),
                            queue_size=args.record_queue_size,
                            source_metadata={
                                "camera": "ZED Mini",
                                "serial_number": int(
                                    zed.get_camera_information().serial_number
                                ),
                                "view": "SIDE_BY_SIDE",
                                "resolution": "HD720",
                                "validity_check": not args.no_validity_check,
                            },
                        )
                    except Exception as exc:
                        warning = f"Recording failed to start: {type(exc).__name__}: {exc}"
                        warning_until = time.monotonic() + 4.0
                        print(warning, flush=True)
                        recorder = None
                    else:
                        last_recorded_frames = 0
                        last_recording_queue_drops = 0
                        warning = f"Recording started: {recording_path.name}"
                        warning_until = time.monotonic() + 2.0
                        print(f"{warning}; press R again to stop", flush=True)
            if key in (ord("s"), ord("S")):
                path = args.capture_dir / f"snapshot_{time.strftime('%Y%m%d_%H%M%S')}_frame{frame_index:06d}.jpg"
                _save_frame(path, frame)
                print(f"Saved {path}", flush=True)
            try:
                if cv2.getWindowProperty(MAIN_WINDOW, cv2.WND_PROP_VISIBLE) < 1:
                    break
            except cv2.error:
                break
    finally:
        if recorder is not None:
            _recording_finished_message(recorder.stop())
        zed.close()
        cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
