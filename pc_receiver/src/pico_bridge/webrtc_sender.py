"""WebRTC video sender for PICO video preview."""

from __future__ import annotations

import asyncio
import logging
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


class ExternalVideoFrameSource:
    """Thread-safe latest-frame buffer for user-supplied RGB video."""

    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._frame: np.ndarray | None = None
        self._seq = 0
        self._updated_at = 0.0

    def push(self, frame: np.ndarray) -> int:
        if not isinstance(frame, np.ndarray):
            raise TypeError("video frame must be a numpy.ndarray")
        if frame.dtype != np.uint8:
            raise TypeError("video frame must use dtype uint8")
        if frame.ndim != 3 or frame.shape[2] != 3:
            raise ValueError("video frame must have shape (height, width, 3)")
        if frame.shape[0] <= 0 or frame.shape[1] <= 0:
            raise ValueError("video frame dimensions must be non-zero")
        if not frame.flags.c_contiguous:
            frame = np.ascontiguousarray(frame)
        else:
            frame = frame.copy()

        with self._lock:
            self._seq += 1
            self._frame = frame
            self._updated_at = time.monotonic()
            return self._seq

    def latest(self) -> tuple[np.ndarray | None, int, float]:
        with self._lock:
            frame = None if self._frame is None else self._frame.copy()
            return frame, self._seq, self._updated_at


class ExternalVideoTrack(_VideoStreamTrackBase):
    """aiortc-compatible video track backed by user-pushed RGB frames."""

    kind = "video"
    _NO_FRAME_WARNING_SECONDS = 2.0

    def __init__(self, source: ExternalVideoFrameSource, width: int, height: int, fps: int):
        super().__init__()
        self.source = source
        self.width = width
        self.height = height
        self.fps = max(1, fps)
        self._frame_index = 0
        self._time_base = Fraction(1, 90_000)
        self._pts_step = 90_000 // self.fps
        self._last_missing_warning_at = 0.0

    async def recv(self) -> av.VideoFrame:
        await asyncio.sleep(1 / self.fps)
        frame, _, _ = self.source.latest()
        if frame is None:
            frame = self._black_frame()
            now = time.monotonic()
            if now - self._last_missing_warning_at >= self._NO_FRAME_WARNING_SECONDS:
                self._last_missing_warning_at = now
                log.warning("external video source has no pushed frames yet; sending black frames")
        video_frame = av.VideoFrame.from_ndarray(frame, format="rgb24")
        video_frame = video_frame.reformat(width=self.width, height=self.height, format="yuv420p")
        video_frame.pts = self._frame_index * self._pts_step
        video_frame.time_base = self._time_base
        self._frame_index += 1
        return video_frame

    def _black_frame(self) -> np.ndarray:
        return np.zeros((self.height, self.width, 3), dtype=np.uint8)


def _make_rgb_test_frame(width: int, height: int, frame_index: int) -> np.ndarray:
    x = np.linspace(0, 255, width, dtype=np.uint16)[None, :]
    y = np.linspace(0, 255, height, dtype=np.uint16)[:, None]
    red_phase = (frame_index * 3) % 255
    green_phase = (frame_index * 5) % 255
    blue_phase = (frame_index * 7) % 255
    frame = np.empty((height, width, 3), dtype=np.uint8)
    frame[..., 0] = ((x + red_phase) % 255).astype(np.uint8)
    frame[..., 1] = ((y + green_phase) % 255).astype(np.uint8)
    frame[..., 2] = ((x // 2 + y // 2 + blue_phase) % 255).astype(np.uint8)
    bar_width = max(1, width // 12)
    start = (frame_index * 9) % width
    end = min(width, start + bar_width)
    frame[:, start:end, :] = np.array([255, 255, 255], dtype=np.uint8)
    return frame


class WebRtcVideoSender:
    """PC-side WebRTC peer that sends a video track to the headset."""

    _DISCONNECTED_GRACE_SECONDS = 6.0

    def __init__(
        self,
        send_signal: SignalSender,
        source: str = "test-pattern",
        frame_source: ExternalVideoFrameSource | None = None,
    ):
        self._send_signal = send_signal
        self._source = source
        self._frame_source = frame_source
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
            if self._source not in ("test-pattern", "frames"):
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
        if self._source == "frames":
            if self._frame_source is None:
                raise RuntimeError("frames video source requires an ExternalVideoFrameSource")
            return ExternalVideoTrack(self._frame_source, req.width, req.height, req.fps)
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
