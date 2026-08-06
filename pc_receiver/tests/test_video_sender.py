"""Tests for WebRTC camera preview handling."""

from __future__ import annotations

import asyncio
import json
import sys
import types

import numpy as np
import pytest

import pico_bridge.webrtc_sender as webrtc_sender_module
from pico_bridge.camera_request import CameraRequest
from pico_bridge.protocol import CMD, HEAD_VR_TO_PC, Packet
from pico_bridge.tcp_server import PicoBridgeServer
from pico_bridge.webrtc_sender import (
    ExternalVideoFrameSource,
    ExternalVideoTrack,
    WebRtcVideoSender,
    _apply_codec_bitrate_override,
    _make_rgb_test_frame,
    _restore_codec_bitrate_override,
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

    @pytest.mark.parametrize("bitrate", [249_999, 50_000_001])
    def test_rejects_out_of_range_bitrate(self, bitrate):
        with pytest.raises(ValueError, match="video bitrate"):
            CameraRequest.from_json({"codec": "webrtc", "bitrate": bitrate})


class TestWebRtcPattern:
    def test_make_rgb_test_frame(self):
        frame = _make_rgb_test_frame(32, 16, 2)
        assert frame.shape == (16, 32, 3)
        assert frame.dtype.name == "uint8"

    def test_make_rgb_test_frame_handles_large_frame_index(self):
        frame = _make_rgb_test_frame(32, 16, 10_000)
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

    def test_requested_bitrate_configures_aiortc_encoders(self):
        from aiortc.codecs import h264, vpx

        overrides = _apply_codec_bitrate_override(8_000_000)
        try:
            assert vpx.DEFAULT_BITRATE == 8_000_000
            assert vpx.MAX_BITRATE == 8_000_000
            assert vpx.Vp8Encoder().target_bitrate == 8_000_000
            assert h264.DEFAULT_BITRATE == 8_000_000
            assert h264.MAX_BITRATE == 8_000_000
            assert h264.H264Encoder().target_bitrate == 8_000_000
        finally:
            _restore_codec_bitrate_override(overrides)

    def test_sender_cleans_up_when_offer_signal_fails(self, monkeypatch):
        peers = []

        class FakeDescription:
            type = "offer"
            sdp = "fake-sdp"

        class FakePeer:
            def __init__(self):
                self.localDescription = FakeDescription()
                self.closed = False
                peers.append(self)

            def on(self, _event):
                def register(handler):
                    return handler

                return register

            def addTrack(self, _track):
                pass

            async def createOffer(self):
                return FakeDescription()

            async def setLocalDescription(self, description):
                self.localDescription = description

            async def close(self):
                self.closed = True

        fake_aiortc = types.SimpleNamespace(RTCPeerConnection=FakePeer)
        monkeypatch.setitem(sys.modules, "aiortc", fake_aiortc)

        async def send_signal(_name, _value):
            raise BrokenPipeError("control connection dropped")

        sender = WebRtcVideoSender(send_signal)

        with pytest.raises(BrokenPipeError):
            asyncio.run(sender.start(CameraRequest(codec="webrtc")))

        assert sender.is_running is False
        assert len(peers) == 1
        assert peers[0].closed is True

    def test_sender_stats_logs_rtcp_loss_fraction(self, monkeypatch, caplog):
        async def send_signal(_name, _value):
            pass

        sender = WebRtcVideoSender(send_signal)
        peer = object()
        generation = 3
        sender._pc = peer
        sender._generation = generation
        monkeypatch.setattr(webrtc_sender_module, "_SENDER_STATS_INTERVAL_SECONDS", 0)

        class FakeRtpSender:
            async def getStats(self):
                sender._pc = None
                return {
                    "outbound": types.SimpleNamespace(
                        type="outbound-rtp",
                        kind="video",
                        bytesSent=10_000,
                        packetsSent=100,
                    ),
                    "remote": types.SimpleNamespace(
                        type="remote-inbound-rtp",
                        kind="video",
                        packetsLost=1,
                        fractionLost=1,
                        roundTripTime=0.012,
                    ),
                }

        with caplog.at_level("INFO", logger="pico_bridge.webrtc"):
            asyncio.run(sender._log_stats_loop(peer, FakeRtpSender(), generation))

        assert "recent=0.39%" in caplog.text
        assert "lost=1 rtt=12.0 ms" in caplog.text

    def test_external_frame_source_validates_rgb_uint8(self):
        source = ExternalVideoFrameSource()
        frame = np.zeros((8, 16, 3), dtype=np.uint8)

        seq = source.push(frame)
        frame[:, :, :] = 255
        stored, stored_seq, _ = source.latest()

        assert seq == 1
        assert stored_seq == 1
        assert stored is not None
        assert stored.shape == (8, 16, 3)
        assert stored.sum() == 0

        with pytest.raises(TypeError):
            source.push(np.zeros((8, 16, 3), dtype=np.float32))
        with pytest.raises(ValueError):
            source.push(np.zeros((8, 16), dtype=np.uint8))

    def test_external_video_track_uses_black_frame_before_push(self):
        async def run():
            source = ExternalVideoFrameSource()
            track = ExternalVideoTrack(source, 16, 8, 30)
            frame = await track.recv()
            track.stop()
            return frame

        frame = asyncio.run(run())

        assert frame.width == 16
        assert frame.height == 8
        assert frame.pts == 0

    def test_frames_source_creates_external_track(self):
        async def send_signal(name, value):
            pass

        source = ExternalVideoFrameSource()
        sender = WebRtcVideoSender(send_signal, source="frames", frame_source=source)
        track = sender._create_track(CameraRequest(width=640, height=360, fps=24))
        assert isinstance(track, ExternalVideoTrack)
        assert track.source is source
        track.stop()

    def test_frames_source_requires_frame_source(self):
        async def send_signal(name, value):
            pass

        sender = WebRtcVideoSender(send_signal, source="frames")

        with pytest.raises(RuntimeError, match="ExternalVideoFrameSource"):
            sender._create_track(CameraRequest(width=640, height=480, fps=30))


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

    async def drain(self) -> None:
        pass
