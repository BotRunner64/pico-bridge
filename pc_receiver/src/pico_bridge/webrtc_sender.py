"""WebRTC video sender for PICO camera preview."""

from __future__ import annotations

import asyncio
import logging
import sys
import threading
import time
from fractions import Fraction
from typing import Any, Awaitable, Callable

import av
import numpy as np

from .camera_request import CameraRequest

log = logging.getLogger("pico_bridge.webrtc")

SignalSender = Callable[[str, Any], Awaitable[None]]

try:  # Keep unit tests importable before optional runtime deps are installed.
    from aiortc import VideoStreamTrack as _VideoStreamTrackBase
except Exception:  # pragma: no cover - exercised only in environments without aiortc
    _VideoStreamTrackBase = object

try:  # aiortc treats this as a normal media stream end.
    from aiortc.mediastreams import MediaStreamError as _MediaStreamError
except Exception:  # pragma: no cover - exercised only in environments without aiortc
    class _MediaStreamError(Exception):
        pass


class TestPatternTrack(_VideoStreamTrackBase):
    """aiortc-compatible synthetic video track."""

    kind = "video"

    def __init__(self, width: int, height: int, fps: int):
        super().__init__()
        self.width = width
        self.height = height
        self.fps = max(1, fps)
        self._frame_index = 0
        self._time_base = Fraction(1, 90_000)
        self._pts_step = 90_000 // self.fps

    async def recv(self) -> av.VideoFrame:
        await asyncio.sleep(1 / self.fps)
        frame = _make_rgb_test_frame(self.width, self.height, self._frame_index)
        video_frame = av.VideoFrame.from_ndarray(frame, format="rgb24")
        video_frame.pts = self._frame_index * self._pts_step
        video_frame.time_base = self._time_base
        self._frame_index += 1
        return video_frame



class _LatestFrameTrack(_VideoStreamTrackBase):
    """Video track that sends the most recent background-captured frame."""

    kind = "video"
    _SLOW_CAPTURE_SECONDS = 0.12
    _STALE_FRAME_SECONDS = 2.0

    def __init__(self, width: int, height: int, fps: int):
        super().__init__()
        self.width = width
        self.height = height
        self.fps = max(1, fps)
        self._frame_index = 0
        self._time_base = Fraction(1, 90_000)
        self._pts_step = 90_000 // self.fps
        self._lock = threading.Lock()
        self._latest_frame: av.VideoFrame | None = None
        self._latest_time = 0.0
        self._last_error: Exception | None = None
        self._stopped = threading.Event()
        self._capture_thread = threading.Thread(target=self._capture_loop, daemon=True)
        self._capture_thread.start()

    async def recv(self) -> av.VideoFrame:
        await asyncio.sleep(1 / self.fps)
        frame = await self._latest_frame_or_raise()
        video_frame = frame.reformat(width=self.width, height=self.height, format="yuv420p")
        video_frame.pts = self._frame_index * self._pts_step
        video_frame.time_base = self._time_base
        self._frame_index += 1
        return video_frame

    def stop(self) -> None:
        try:
            super().stop()
        except AttributeError:
            pass
        self._stopped.set()
        if self._capture_thread.is_alive():
            self._capture_thread.join(timeout=1.0)

    async def _latest_frame_or_raise(self) -> av.VideoFrame:
        deadline = time.monotonic() + self._STALE_FRAME_SECONDS
        while not self._stopped.is_set():
            with self._lock:
                frame = self._latest_frame
                latest_time = self._latest_time
                last_error = self._last_error
            if frame is not None:
                if time.monotonic() - latest_time <= self._STALE_FRAME_SECONDS or last_error is None:
                    return frame
            if last_error is not None and time.monotonic() >= deadline:
                raise RuntimeError("video source stopped producing frames") from last_error
            if time.monotonic() >= deadline and frame is None:
                raise RuntimeError("video source produced no frames")
            await asyncio.sleep(0.01)
        raise _MediaStreamError

    def _capture_loop(self) -> None:
        while not self._stopped.is_set():
            started = time.monotonic()
            try:
                frame = self._read_source_frame()
            except Exception as exc:  # pragma: no cover - source hardware failures are environment-specific
                with self._lock:
                    self._last_error = exc
                time.sleep(0.02)
                continue

            elapsed = time.monotonic() - started
            if elapsed >= self._SLOW_CAPTURE_SECONDS:
                log.warning("slow video source capture: %.1f ms", elapsed * 1000)
            with self._lock:
                self._latest_frame = frame
                self._latest_time = time.monotonic()
                self._last_error = None

    def _read_source_frame(self) -> av.VideoFrame:
        raise NotImplementedError


class CameraVideoTrack(_LatestFrameTrack):
    """aiortc-compatible webcam track backed by PyAV."""

    def __init__(self, device: str | None, width: int, height: int, fps: int):
        self._container = _open_camera(device, width, height, max(1, fps))
        self._stream = self._container.streams.video[0]
        super().__init__(width, height, fps)

    def stop(self) -> None:
        super().stop()
        _close_container(self._container)

    def _read_source_frame(self) -> av.VideoFrame:
        for packet in self._container.demux(self._stream):
            for frame in packet.decode():
                if isinstance(frame, av.VideoFrame):
                    return frame
        raise RuntimeError("camera stream ended")


class RealSenseVideoTrack(_LatestFrameTrack):
    """aiortc-compatible RealSense color track backed by pyrealsense2."""

    def __init__(self, device: str | None, width: int, height: int, fps: int):
        self._pipeline = _open_realsense(device, width, height, max(1, fps))
        super().__init__(width, height, fps)

    def stop(self) -> None:
        super().stop()
        _close_realsense(self._pipeline)

    def _read_source_frame(self) -> av.VideoFrame:
        frames = self._pipeline.wait_for_frames()
        color_frame = frames.get_color_frame()
        if not color_frame:
            raise RuntimeError("RealSense color stream produced no frame")
        frame = np.asanyarray(color_frame.get_data()).copy()
        return av.VideoFrame.from_ndarray(frame, format="rgb24")


def _open_camera(device: str | None, width: int, height: int, fps: int):
    if sys.platform.startswith("linux"):
        camera_device = device or "/dev/video0"
        options = {"framerate": str(fps), "video_size": f"{width}x{height}"}
        log.info("Opening v4l2 camera %s (%dx%d @%dfps)", camera_device, width, height, fps)
        return av.open(camera_device, format="v4l2", options=options)
    if sys.platform == "darwin":
        camera_device = device or "0"
        options = {"framerate": str(fps), "video_size": f"{width}x{height}"}
        log.info("Opening avfoundation camera %s (%dx%d @%dfps)", camera_device, width, height, fps)
        return av.open(f"{camera_device}:none", format="avfoundation", options=options)
    if sys.platform == "win32":
        camera_device = device or "video=Integrated Camera"
        options = {"framerate": str(fps), "video_size": f"{width}x{height}"}
        log.info("Opening dshow camera %s (%dx%d @%dfps)", camera_device, width, height, fps)
        return av.open(camera_device, format="dshow", options=options)
    raise RuntimeError(f"Unsupported camera platform: {sys.platform}")


def _open_realsense(device: str | None, width: int, height: int, fps: int):
    try:
        import pyrealsense2 as rs
    except ImportError as exc:
        raise RuntimeError(
            "RealSense video source requires pyrealsense2. "
            "Install project dependencies with `pip install -e .` or install `pyrealsense2` directly."
        ) from exc

    pipeline = rs.pipeline()
    config = rs.config()
    if device:
        config.enable_device(device)
    config.enable_stream(rs.stream.color, width, height, rs.format.rgb8, fps)
    log.info("Opening RealSense color stream %s (%dx%d @%dfps)", device or "default", width, height, fps)
    pipeline.start(config)
    return pipeline


def _close_container(container: Any) -> None:
    try:
        container.close()
    except Exception as exc:  # pragma: no cover - defensive around native camera libs
        log.debug("Ignoring camera close error: %s", exc)


def _close_realsense(pipeline: Any) -> None:
    try:
        pipeline.stop()
    except Exception as exc:  # pragma: no cover - defensive around native camera libs
        log.debug("Ignoring RealSense close error: %s", exc)


def _make_rgb_test_frame(width: int, height: int, frame_index: int) -> np.ndarray:
    x = np.linspace(0, 255, width, dtype=np.uint8)[None, :]
    y = np.linspace(0, 255, height, dtype=np.uint8)[:, None]
    frame = np.empty((height, width, 3), dtype=np.uint8)
    frame[..., 0] = (x + frame_index * 3) % 255
    frame[..., 1] = (y + frame_index * 5) % 255
    frame[..., 2] = ((x // 2 + y // 2 + frame_index * 7) % 255).astype(np.uint8)
    bar_width = max(1, width // 12)
    start = (frame_index * 9) % width
    end = min(width, start + bar_width)
    frame[:, start:end, :] = np.array([255, 255, 255], dtype=np.uint8)
    return frame


class WebRtcVideoSender:
    """PC-side WebRTC peer that sends a video track to the headset."""

    _DISCONNECTED_GRACE_SECONDS = 6.0

    def __init__(self, send_signal: SignalSender, source: str = "test-pattern", camera_device: str | None = None):
        self._send_signal = send_signal
        self._source = source
        self._camera_device = camera_device
        self._pc: Any | None = None
        self._track: Any | None = None
        self._running = False
        self._lock = asyncio.Lock()
        self._generation = 0

    @property
    def is_running(self) -> bool:
        return self._running

    async def start(self, req: CameraRequest) -> None:
        async with self._lock:
            await self._stop_locked()
            if req.codec != "webrtc":
                raise ValueError(f"WebRtcVideoSender requires codec=webrtc, got {req.codec!r}")
            if self._source not in ("test-pattern", "camera", "realsense"):
                raise ValueError(f"unsupported WebRTC video source: {self._source!r}")

            from aiortc import RTCPeerConnection

            self._generation += 1
            generation = self._generation
            pc = RTCPeerConnection()
            self._pc = pc
            self._running = True

            @pc.on("icecandidate")
            async def on_icecandidate(candidate: Any) -> None:
                if candidate is None:
                    return
                if self._pc is not pc or self._generation != generation:
                    return
                try:
                    await self._send_signal("WebRtcIceCandidate", _candidate_to_json(candidate))
                except Exception:
                    log.exception("failed to send WebRTC ICE candidate")
                    asyncio.create_task(self._stop_if_current(pc, generation))

            @pc.on("connectionstatechange")
            async def on_connectionstatechange() -> None:
                log.info("WebRTC connection state: %s", pc.connectionState)
                if pc.connectionState in ("failed", "closed"):
                    asyncio.create_task(self._stop_if_current(pc, generation))
                elif pc.connectionState == "disconnected":
                    asyncio.create_task(self._stop_if_still_disconnected(pc, generation))

            try:
                track = self._create_track(req)
                self._track = track
                pc.addTrack(track)
                offer = await pc.createOffer()
                await pc.setLocalDescription(offer)
                local = pc.localDescription
                await self._send_signal("WebRtcOffer", {"type": local.type, "sdp": local.sdp})
            except Exception:
                await self._stop_locked()
                raise

            log.info("WebRTC offer sent (%dx%d @%dfps)", req.width, req.height, req.fps)

    def _create_track(self, req: CameraRequest) -> Any:
        if self._source == "camera":
            return CameraVideoTrack(self._camera_device, req.width, req.height, req.fps)
        if self._source == "realsense":
            return RealSenseVideoTrack(self._camera_device, req.width, req.height, req.fps)
        return TestPatternTrack(req.width, req.height, req.fps)

    async def handle_answer(self, value: Any) -> None:
        async with self._lock:
            if self._pc is None:
                log.warning("WebRtcAnswer ignored; no active peer")
                return
            from aiortc import RTCSessionDescription

            desc = _session_description_from_value(value)
            try:
                await self._pc.setRemoteDescription(RTCSessionDescription(sdp=desc["sdp"], type=desc["type"]))
            except Exception:
                await self._stop_locked()
                raise
            log.info("WebRTC answer applied")

    async def handle_ice_candidate(self, value: Any) -> None:
        async with self._lock:
            if self._pc is None:
                log.warning("WebRtcIceCandidate ignored; no active peer")
                return
            try:
                candidate = _candidate_from_value(value)
                await self._pc.addIceCandidate(candidate)
            except Exception as exc:
                log.warning("WebRtcIceCandidate ignored: %s", exc)

    async def stop(self) -> None:
        async with self._lock:
            self._generation += 1
            await self._stop_locked()

    async def _stop_if_current(self, pc: Any, generation: int) -> None:
        async with self._lock:
            if self._pc is not pc or self._generation != generation:
                return
            self._generation += 1
            await self._stop_locked()

    async def _stop_if_still_disconnected(self, pc: Any, generation: int) -> None:
        await asyncio.sleep(self._DISCONNECTED_GRACE_SECONDS)
        async with self._lock:
            if self._pc is not pc or self._generation != generation:
                return
            if pc.connectionState != "disconnected":
                return
            self._generation += 1
            await self._stop_locked()

    async def _stop_locked(self) -> None:
        pc = self._pc
        track = self._track
        self._pc = None
        self._track = None
        self._running = False
        if track is not None:
            try:
                track.stop()
            except Exception as exc:  # pragma: no cover - defensive around native media tracks
                log.debug("Ignoring WebRTC track stop error: %s", exc)
        if pc is not None:
            try:
                await pc.close()
            except Exception as exc:  # pragma: no cover - defensive around native aiortc internals
                log.debug("Ignoring WebRTC peer close error: %s", exc)
            log.info("WebRTC sender stopped")


def _session_description_from_value(value: Any) -> dict[str, str]:
    value = _json_object_from_value(value, "session description")
    sdp = str(value["sdp"])
    desc_type = str(value.get("type", "answer"))
    return {"sdp": sdp, "type": desc_type}


def _candidate_to_json(candidate: Any) -> dict[str, Any]:
    return {
        "candidate": candidate.to_sdp() if hasattr(candidate, "to_sdp") else str(candidate),
        "sdpMid": getattr(candidate, "sdpMid", None),
        "sdpMLineIndex": getattr(candidate, "sdpMLineIndex", None),
    }


def _candidate_from_value(value: Any) -> Any:
    from aiortc.sdp import candidate_from_sdp

    value = _json_object_from_value(value, "ICE candidate")
    candidate_text = str(value.get("candidate", ""))
    if candidate_text.startswith("candidate:"):
        candidate_text = candidate_text[len("candidate:") :]
    candidate = candidate_from_sdp(candidate_text)
    candidate.sdpMid = value.get("sdpMid")
    candidate.sdpMLineIndex = value.get("sdpMLineIndex")
    return candidate


def _json_object_from_value(value: Any, label: str) -> dict[str, Any]:
    if isinstance(value, str):
        import json

        value = json.loads(value)
    if not isinstance(value, dict):
        raise TypeError(f"{label} must be a dict")
    return value
