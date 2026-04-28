"""Tests for WebRTC camera preview handling."""

from __future__ import annotations

import asyncio
import json
import sys
import types

import pytest

from pico_bridge.camera_request import CameraRequest
from pico_bridge.protocol import CMD, HEAD_VR_TO_PC, Packet
from pico_bridge.tcp_server import PicoBridgeServer
from pico_bridge.webrtc_sender import (
    CameraVideoTrack,
    RealSenseVideoTrack,
    WebRtcVideoSender,
    _make_rgb_test_frame,
    _open_realsense,
)


class TestCameraRequest:
    def test_from_json_webrtc_defaults(self):
        req = CameraRequest.from_json({"codec": "webrtc"})
        assert req.codec == "webrtc"
        assert req.ip == "0.0.0.0"
        assert req.port == 0
        assert req.width == 1280
        assert req.height == 720
        assert req.fps == 30

    def test_from_json_webrtc_values(self):
        req = CameraRequest.from_json({
            "codec": "webrtc",
            "source": "test-pattern",
            "width": "640",
            "height": "360",
            "fps": "24",
            "bitrate": "1000000",
        })
        assert req.source == "test-pattern"
        assert req.width == 640
        assert req.height == 360
        assert req.fps == 24
        assert req.bitrate == 1_000_000

    def test_rejects_legacy_codec(self):
        with pytest.raises(ValueError):
            CameraRequest.from_json({"codec": "h264", "ip": "10.0.0.1", "port": 1234})


class TestWebRtcPattern:
    def test_make_rgb_test_frame(self):
        frame = _make_rgb_test_frame(32, 16, 2)
        assert frame.shape == (16, 32, 3)
        assert frame.dtype.name == "uint8"

    def test_sender_initial_state(self):
        async def send_signal(name, value):
            pass

        sender = WebRtcVideoSender(send_signal)
        assert not sender.is_running

    def test_sender_rejects_non_webrtc_request(self):
        async def send_signal(name, value):
            pass

        sender = WebRtcVideoSender(send_signal)
        req = CameraRequest(codec="h264")
        with pytest.raises(ValueError):
            asyncio.run(sender.start(req))

    def test_camera_source_creates_camera_track(self, monkeypatch):
        async def send_signal(name, value):
            pass

        opened = []

        class FakeContainer:
            streams = type("Streams", (), {"video": [object()]})()

            def close(self):
                pass

        def fake_open_camera(device, width, height, fps):
            opened.append((device, width, height, fps))
            return FakeContainer()

        monkeypatch.setattr("pico_bridge.webrtc_sender._open_camera", fake_open_camera)
        sender = WebRtcVideoSender(send_signal, source="camera", camera_device="/dev/video9")
        track = sender._create_track(CameraRequest(width=640, height=360, fps=24))
        assert isinstance(track, CameraVideoTrack)
        assert opened == [("/dev/video9", 640, 360, 24)]
        track.stop()

    def test_realsense_source_creates_realsense_track(self, monkeypatch):
        async def send_signal(name, value):
            pass

        opened = []

        class FakePipeline:
            def stop(self):
                pass

        def fake_open_realsense(device, width, height, fps):
            opened.append((device, width, height, fps))
            return FakePipeline()

        monkeypatch.setattr("pico_bridge.webrtc_sender._open_realsense", fake_open_realsense)
        sender = WebRtcVideoSender(send_signal, source="realsense", camera_device="RS123")
        track = sender._create_track(CameraRequest(width=640, height=480, fps=30))
        assert isinstance(track, RealSenseVideoTrack)
        assert opened == [("RS123", 640, 480, 30)]
        track.stop()

    def test_open_realsense_enables_rgb_color_stream(self, monkeypatch):
        calls = []

        class FakePipeline:
            def start(self, config):
                calls.append(("start", config))

        class FakeConfig:
            def enable_device(self, serial):
                calls.append(("device", serial))

            def enable_stream(self, stream, width, height, fmt, fps):
                calls.append(("stream", stream, width, height, fmt, fps))

        fake_rs = types.SimpleNamespace(
            pipeline=FakePipeline,
            config=FakeConfig,
            stream=types.SimpleNamespace(color="color"),
            format=types.SimpleNamespace(rgb8="rgb8"),
        )
        monkeypatch.setitem(sys.modules, "pyrealsense2", fake_rs)

        pipeline = _open_realsense("RS123", 848, 480, 60)

        assert isinstance(pipeline, FakePipeline)
        assert calls[:2] == [
            ("device", "RS123"),
            ("stream", "color", 848, 480, "rgb8", 60),
        ]
        assert calls[2][0] == "start"
        assert isinstance(calls[2][1], FakeConfig)


class TestServerCameraDispatch:
    def _camera_packet(self, value: object) -> Packet:
        payload = json.dumps({"functionName": "StartReceivePcCamera", "value": value}).encode()
        return Packet(head=HEAD_VR_TO_PC, cmd=CMD.TO_CONTROLLER_FUNCTION, data=payload, timestamp=0)

    def _stop_packet(self) -> Packet:
        payload = json.dumps({"functionName": "StopReceivePcCamera", "value": ""}).encode()
        return Packet(head=HEAD_VR_TO_PC, cmd=CMD.TO_CONTROLLER_FUNCTION, data=payload, timestamp=0)

    def _make_server(self, on_camera_request=None, on_camera_stop=None):
        server = PicoBridgeServer(on_camera_request=on_camera_request, on_camera_stop=on_camera_stop)
        writer = _FakeWriter()
        server._writer = writer
        server._connected = True
        return server, writer

    def test_start_camera_dispatches_webrtc_request(self):
        requests: list[CameraRequest] = []
        server, writer = self._make_server(on_camera_request=requests.append)
        cam_dict = {"codec": "webrtc", "width": 1280, "height": 720, "fps": 30}
        asyncio.run(server._handle_function(self._camera_packet(cam_dict), writer))
        assert len(requests) == 1
        assert requests[0].codec == "webrtc"
        assert requests[0].width == 1280

    def test_stop_camera_dispatches(self):
        stopped = []
        server, writer = self._make_server(on_camera_stop=lambda: stopped.append(True))
        asyncio.run(server._handle_function(self._stop_packet(), writer))
        assert stopped == [True]

    def test_bad_camera_payload_does_not_crash(self):
        requests: list[CameraRequest] = []
        server, writer = self._make_server(on_camera_request=requests.append)
        asyncio.run(server._handle_function(self._camera_packet("not-json-{{{"), writer))
        assert requests == []


class _FakeWriter:
    def __init__(self):
        self._closed = False
        self.writes: list[bytes] = []

    def get_extra_info(self, name: str):
        if name == "peername":
            return getattr(self, "peername", None)
        if name == "sockname":
            return getattr(self, "sockname", None)
        return None

    def is_closing(self) -> bool:
        return self._closed

    def close(self) -> None:
        self._closed = True

    async def wait_closed(self) -> None:
        pass

    def write(self, data: bytes) -> None:
        self.writes.append(data)
