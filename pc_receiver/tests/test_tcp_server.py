from __future__ import annotations

import asyncio
import json

from pico_bridge.protocol import CMD, HEAD_VR_TO_PC, Packet
from pico_bridge.tcp_server import PicoBridgeServer


def _function_packet(value: object) -> Packet:
    payload = json.dumps({"functionName": "Tracking", "value": value}).encode()
    return Packet(head=HEAD_VR_TO_PC, cmd=CMD.TO_CONTROLLER_FUNCTION, data=payload, timestamp=0)


def test_bad_tracking_payload_does_not_disconnect_handler():
    calls: list[dict[str, object]] = []
    server = PicoBridgeServer(on_tracking=calls.append)

    asyncio.run(server._handle_function(_function_packet("{bad json")))

    assert calls == []


def test_tracking_payload_must_decode_to_object():
    calls: list[dict[str, object]] = []
    server = PicoBridgeServer(on_tracking=calls.append)

    asyncio.run(server._handle_function(_function_packet("[1, 2, 3]")))

    assert calls == []


def test_rejects_second_client_without_clearing_first_connection():
    async def run() -> None:
        server = PicoBridgeServer(host="127.0.0.1", port=0)
        first_writer: asyncio.StreamWriter | None = None
        second_writer: asyncio.StreamWriter | None = None
        await server.start()
        try:
            assert server._server is not None
            assert server._server.sockets is not None
            port = server._server.sockets[0].getsockname()[1]

            _, first_writer = await asyncio.open_connection("127.0.0.1", port)
            await _wait_until(lambda: server.connected)

            second_reader, second_writer = await asyncio.open_connection("127.0.0.1", port)
            rejected = await asyncio.wait_for(second_reader.read(1), timeout=1)

            assert rejected == b""
            assert server.connected is True

            second_writer.close()
            await second_writer.wait_closed()
            first_writer.close()
            await first_writer.wait_closed()
            await _wait_until(lambda: not server.connected)
        finally:
            if second_writer and not second_writer.is_closing():
                second_writer.close()
                await second_writer.wait_closed()
            if first_writer and not first_writer.is_closing():
                first_writer.close()
                await first_writer.wait_closed()
            await server.stop()

    asyncio.run(run())


async def _wait_until(predicate) -> None:
    for _ in range(20):
        if predicate():
            return
        await asyncio.sleep(0.01)
    raise AssertionError("condition was not reached")
