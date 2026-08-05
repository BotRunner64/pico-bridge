from __future__ import annotations

import asyncio

from pico_bridge.control import CONTROL_FUNCTION_NAME
from pico_bridge.frame_store import FrameStore
from pico_bridge.runtime import PicoBridgeRuntime
from pico_bridge.stereo import StereoCameraIntrinsics


def test_runtime_sends_video_policy_control_message():
    async def run() -> list[tuple[str, object]]:
        runtime = PicoBridgeRuntime(
            host="0.0.0.0",
            port=63901,
            discovery=False,
            advertise_ip=None,
            video="frames",
            video_enabled=False,
            video_frame_source=None,
            frame_store=FrameStore(),
            video_layout="stereo-sbs",
            stereo_intrinsics=StereoCameraIntrinsics(
                eye_width=640,
                eye_height=360,
                fx=320.0,
                fy=360.0,
                cx=320.0,
                cy=180.0,
            ),
        )
        server = _FakeServer()
        runtime._server = server

        await runtime._send_video_policy()
        return server.messages

    messages = asyncio.run(run())

    assert messages == [
        (
            CONTROL_FUNCTION_NAME,
            {
                "version": 1,
                "channel": "video",
                "type": "set_policy",
                "payload": {
                    "enabled": False,
                    "auto_preview": False,
                    "layout": "stereo-sbs",
                    "source": "frames",
                    "stereo_fx_norm": 0.5,
                    "stereo_fy_norm": 1.0,
                    "stereo_cx_norm": 0.5,
                    "stereo_cy_norm": 0.5,
                },
            },
        )
    ]


class _FakeServer:
    connected = True

    def __init__(self) -> None:
        self.messages: list[tuple[str, object]] = []

    async def send_function(self, name: str, value: object) -> None:
        self.messages.append((name, value))
