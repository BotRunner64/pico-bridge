"""WebRTC video sender for PICO camera preview."""

from __future__ import annotations

import asyncio
import logging
import sys
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



class CameraVideoTrack(_VideoStreamTrackBase):
    """aiortc-compatible webcam track backed by PyAV."""

    kind = "video"

    def __init__(self, device: str | None, width: int, height: int, fps: int):
        super().__init__()
        self.width = width
        self.height = height
        self.fps = max(1, fps)
        self._container = _open_camera(device, width, height, self.fps)
        self._stream = self._container.streams.video[0]
        self._frame_index = 0
        self._time_base = Fraction(1, 90_000)
        self._pts_step = 90_000 // self.fps

    async def recv(self) -> av.VideoFrame:
        frame = await asyncio.to_thread(self._read_frame)
        frame = frame.reformat(width=self.width, height=self.height, format="yuv420p")
        frame.pts = self._frame_index * self._pts_step
        frame.time_base = self._time_base
        self._frame_index += 1
        return frame

    def stop(self) -> None:
        super().stop()
        _close_container(self._container)

    def _read_frame(self) -> av.VideoFrame:
        for packet in self._container.demux(self._stream):
            for frame in packet.decode():
                if isinstance(frame, av.VideoFrame):
                    return frame
        raise RuntimeError("camera stream ended")


class RealSenseVideoTrack(_VideoStreamTrackBase):
    """aiortc-compatible RealSense color track backed by pyrealsense2."""

    kind = "video"

    def __init__(self, device: str | None, width: int, height: int, fps: int):
        super().__init__()
        self.width = width
        self.height = height
        self.fps = max(1, fps)
        self._pipeline = _open_realsense(device, width, height, self.fps)
        self._frame_index = 0
        self._time_base = Fraction(1, 90_000)
        self._pts_step = 90_000 // self.fps

    async def recv(self) -> av.VideoFrame:
        frame = await asyncio.to_thread(self._read_frame)
        video_frame = av.VideoFrame.from_ndarray(frame, format="rgb24")
        video_frame = video_frame.reformat(width=self.width, height=self.height, format="yuv420p")
        video_frame.pts = self._frame_index * self._pts_step
        video_frame.time_base = self._time_base
        self._frame_index += 1
        return video_frame

    def stop(self) -> None:
        super().stop()
        _close_realsense(self._pipeline)

    def _read_frame(self) -> np.ndarray:
        frames = self._pipeline.wait_for_frames()
        color_frame = frames.get_color_frame()
        if not color_frame:
            raise RuntimeError("RealSense color stream produced no frame")
        return np.asanyarray(color_frame.get_data()).copy()


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

    def __init__(self, send_signal: SignalSender, source: str = "test-pattern", camera_device: str | None = None):
        self._send_signal = send_signal
        self._source = source
        self._camera_device = camera_device
        self._pc: Any | None = None
        self._track: Any | None = None
        self._running = False

    @property
    def is_running(self) -> bool:
        return self._running

    async def start(self, req: CameraRequest) -> None:
        await self.stop()
        if req.codec != "webrtc":
            raise ValueError(f"WebRtcVideoSender requires codec=webrtc, got {req.codec!r}")
        if self._source not in ("test-pattern", "camera", "realsense"):
            raise ValueError(f"unsupported WebRTC video source: {self._source!r}")

        from aiortc import RTCPeerConnection

        pc = RTCPeerConnection()
        self._pc = pc
        self._running = True

        @pc.on("icecandidate")
        async def on_icecandidate(candidate: Any) -> None:
            if candidate is None:
                return
            await self._send_signal("WebRtcIceCandidate", _candidate_to_json(candidate))

        @pc.on("connectionstatechange")
        async def on_connectionstatechange() -> None:
            log.info("WebRTC connection state: %s", pc.connectionState)
            if pc.connectionState in ("failed", "closed", "disconnected"):
                self._running = False

        track = self._create_track(req)
        self._track = track
        pc.addTrack(track)
        offer = await pc.createOffer()
        await pc.setLocalDescription(offer)
        local = pc.localDescription
        await self._send_signal("WebRtcOffer", {"type": local.type, "sdp": local.sdp})
        log.info("WebRTC offer sent (%dx%d @%dfps)", req.width, req.height, req.fps)

    def _create_track(self, req: CameraRequest) -> Any:
        if self._source == "camera":
            return CameraVideoTrack(self._camera_device, req.width, req.height, req.fps)
        if self._source == "realsense":
            return RealSenseVideoTrack(self._camera_device, req.width, req.height, req.fps)
        return TestPatternTrack(req.width, req.height, req.fps)

    async def handle_answer(self, value: Any) -> None:
        if self._pc is None:
            log.warning("WebRtcAnswer ignored; no active peer")
            return
        from aiortc import RTCSessionDescription

        desc = _session_description_from_value(value)
        await self._pc.setRemoteDescription(RTCSessionDescription(sdp=desc["sdp"], type=desc["type"]))
        log.info("WebRTC answer applied")

    async def handle_ice_candidate(self, value: Any) -> None:
        if self._pc is None:
            log.warning("WebRtcIceCandidate ignored; no active peer")
            return
        candidate = _candidate_from_value(value)
        await self._pc.addIceCandidate(candidate)

    async def stop(self) -> None:
        pc = self._pc
        track = self._track
        self._pc = None
        self._track = None
        self._running = False
        if track is not None:
            track.stop()
        if pc is not None:
            await pc.close()
            log.info("WebRTC sender stopped")


def _session_description_from_value(value: Any) -> dict[str, str]:
    if isinstance(value, str):
        import json

        value = json.loads(value)
    if not isinstance(value, dict):
        raise TypeError("session description must be a dict")
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

    if isinstance(value, str):
        import json

        value = json.loads(value)
    if not isinstance(value, dict):
        raise TypeError("ICE candidate must be a dict")
    candidate_text = str(value.get("candidate", ""))
    if candidate_text.startswith("candidate:"):
        candidate_text = candidate_text[len("candidate:") :]
    candidate = candidate_from_sdp(candidate_text)
    candidate.sdpMid = value.get("sdpMid")
    candidate.sdpMLineIndex = value.get("sdpMLineIndex")
    return candidate
