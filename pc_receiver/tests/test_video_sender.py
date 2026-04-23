"""Tests for video_sender module and StartReceivePcCamera handling."""

from __future__ import annotations

import asyncio
import json
import sys
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from pico_bridge.protocol import CMD, HEAD_VR_TO_PC, Packet
from pico_bridge.tcp_server import PicoBridgeServer
from pico_bridge.video_sender import CameraRequest, VideoSender, _build_ffmpeg_args


# ── CameraRequest parsing ─────────────────────────────────

class TestCameraRequest:
    def test_from_json_full(self):
        obj = {"ip": "10.0.0.5", "port": 12345, "width": 1920, "height": 1080, "fps": 30, "bitrate": 8_000_000}
        req = CameraRequest.from_json(obj)
        assert req.ip == "10.0.0.5"
        assert req.port == 12345
        assert req.width == 1920
        assert req.height == 1080
        assert req.fps == 30
        assert req.bitrate == 8_000_000

    def test_from_json_defaults(self):
        obj = {"ip": "192.168.1.10", "port": 9000}
        req = CameraRequest.from_json(obj)
        assert req.width == 2160
        assert req.height == 1440
        assert req.fps == 60
        assert req.bitrate == 40 * 1024 * 1024

    def test_from_json_missing_ip_raises(self):
        with pytest.raises(KeyError):
            CameraRequest.from_json({"port": 9000})

    def test_from_json_missing_port_raises(self):
        with pytest.raises(KeyError):
            CameraRequest.from_json({"ip": "10.0.0.1"})

    def test_from_json_string_values(self):
        obj = {"ip": "10.0.0.1", "port": "8080", "width": "1280", "height": "720"}
        req = CameraRequest.from_json(obj)
        assert req.port == 8080
        assert req.width == 1280


# ── ffmpeg args builder ───────────────────────────────────

class TestBuildFfmpegArgs:
    @patch("shutil.which", return_value="/usr/bin/ffmpeg")
    def test_test_pattern_args(self, _mock_which):
        req = CameraRequest(ip="10.0.0.1", port=9000, width=1920, height=1080, fps=30, bitrate=4_000_000)
        args = _build_ffmpeg_args(req, source="test-pattern")
        assert args[0] == "/usr/bin/ffmpeg"
        assert "testsrc=size=1920x1080:rate=30" in " ".join(args)
        assert "-f" in args
        idx = args.index("pipe:1")
        assert idx == len(args) - 1
        assert "-c:v" in args
        assert args[args.index("-c:v") + 1] == "libx264"

    @patch("shutil.which", return_value="/usr/bin/ffmpeg")
    def test_camera_args_linux(self, _mock_which):
        req = CameraRequest(ip="10.0.0.1", port=9000)
        with patch.object(sys, "platform", "linux"):
            args = _build_ffmpeg_args(req, source="camera")
        assert "-f" in args
        assert "v4l2" in args

    @patch("shutil.which", return_value=None)
    def test_ffmpeg_not_found(self, _mock_which):
        req = CameraRequest(ip="10.0.0.1", port=9000)
        with pytest.raises(RuntimeError, match="ffmpeg not found"):
            _build_ffmpeg_args(req)

    @patch("shutil.which", return_value="/usr/bin/ffmpeg")
    def test_unknown_source_raises(self, _mock_which):
        req = CameraRequest(ip="10.0.0.1", port=9000)
        with pytest.raises(ValueError, match="Unknown source"):
            _build_ffmpeg_args(req, source="magic")

    @patch("shutil.which", return_value="/usr/bin/ffmpeg")
    def test_h264_annex_b_output(self, _mock_which):
        req = CameraRequest(ip="10.0.0.1", port=9000)
        args = _build_ffmpeg_args(req)
        # Must output raw h264 format
        f_indices = [i for i, a in enumerate(args) if a == "-f"]
        formats = [args[i + 1] for i in f_indices]
        assert "h264" in formats


# ── tcp_server camera dispatch ────────────────────────────

class TestServerCameraDispatch:
    def _camera_packet(self, value: object) -> Packet:
        payload = json.dumps({"functionName": "StartReceivePcCamera", "value": value}).encode()
        return Packet(head=HEAD_VR_TO_PC, cmd=CMD.TO_CONTROLLER_FUNCTION, data=payload, timestamp=0)

    def _stop_packet(self) -> Packet:
        payload = json.dumps({"functionName": "StopReceivePcCamera", "value": ""}).encode()
        return Packet(head=HEAD_VR_TO_PC, cmd=CMD.TO_CONTROLLER_FUNCTION, data=payload, timestamp=0)

    def _make_server(self, on_camera_request=None, on_camera_stop=None):
        server = PicoBridgeServer(
            on_camera_request=on_camera_request,
            on_camera_stop=on_camera_stop,
        )
        writer = _FakeWriter()
        server._writer = writer
        server._connected = True
        return server, writer

    def test_start_camera_dispatches_request(self):
        requests: list[CameraRequest] = []
        server, writer = self._make_server(on_camera_request=requests.append)
        cam_json = json.dumps({"ip": "10.0.0.5", "port": 12345, "width": 1920, "height": 1080, "fps": 30, "bitrate": 8_000_000})
        asyncio.run(server._handle_function(self._camera_packet(cam_json), writer))
        assert len(requests) == 1
        assert requests[0].ip == "10.0.0.5"
        assert requests[0].port == 12345

    def test_start_camera_with_dict_value(self):
        requests: list[CameraRequest] = []
        server, writer = self._make_server(on_camera_request=requests.append)
        cam_dict = {"ip": "10.0.0.5", "port": 12345}
        asyncio.run(server._handle_function(self._camera_packet(cam_dict), writer))
        assert len(requests) == 1
        assert requests[0].ip == "10.0.0.5"

    def test_stop_camera_dispatches(self):
        stopped = []
        server, writer = self._make_server(on_camera_stop=lambda: stopped.append(True))
        asyncio.run(server._handle_function(self._stop_packet(), writer))
        assert stopped == [True]

    def test_bad_camera_payload_does_not_crash(self):
        requests: list[CameraRequest] = []
        server, writer = self._make_server(on_camera_request=requests.append)
        bad_pkt = self._camera_packet("not-json-{{{")
        asyncio.run(server._handle_function(bad_pkt, writer))
        assert requests == []

    def test_camera_request_missing_fields_does_not_crash(self):
        requests: list[CameraRequest] = []
        server, writer = self._make_server(on_camera_request=requests.append)
        incomplete = json.dumps({"width": 1920})
        asyncio.run(server._handle_function(self._camera_packet(incomplete), writer))
        assert requests == []


# ── VideoSender lifecycle ─────────────────────────────────

class TestVideoSender:
    def test_initial_state(self):
        sender = VideoSender()
        assert not sender.is_running

    @patch("shutil.which", return_value="/usr/bin/ffmpeg")
    def test_stop_when_not_running(self, _mock_which):
        sender = VideoSender()
        asyncio.run(sender.stop())
        assert not sender.is_running


# ── helpers ───────────────────────────────────────────────

class _FakeWriter:
    def __init__(self):
        self._closed = False
        self.writes: list[bytes] = []

    def get_extra_info(self, name: str):
        return "test" if name == "peername" else None

    def is_closing(self) -> bool:
        return self._closed

    def close(self) -> None:
        self._closed = True

    async def wait_closed(self) -> None:
        pass

    def write(self, data: bytes) -> None:
        self.writes.append(data)
