from __future__ import annotations

import asyncio

from pico_bridge.control import CONTROL_FUNCTION_NAME
from pico_bridge.frame_store import FrameStore
from pico_bridge.runtime import PicoBridgeRuntime


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
                    "source": "frames",
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
