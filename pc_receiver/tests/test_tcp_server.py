from __future__ import annotations

import asyncio
import json

from pico_bridge.protocol import CMD, HEAD_VR_TO_PC, Packet, pack
from pico_bridge.tcp_server import PicoBridgeServer


def _function_packet(value: object) -> Packet:
    payload = json.dumps({"functionName": "Tracking", "value": value}).encode()
    return Packet(head=HEAD_VR_TO_PC, cmd=CMD.TO_CONTROLLER_FUNCTION, data=payload, timestamp=0)


def test_bad_tracking_payload_does_not_disconnect_handler():
    calls: list[dict[str, object]] = []
    server = PicoBridgeServer(on_tracking=calls.append)
    writer = _FakeWriter("active")
    server._writer = writer
    server._connected = True

    asyncio.run(server._handle_function(_function_packet("{bad json"), writer))

    assert calls == []


def test_tracking_payload_must_decode_to_object():
    calls: list[dict[str, object]] = []
    server = PicoBridgeServer(on_tracking=calls.append)
    writer = _FakeWriter("active")
    server._writer = writer
    server._connected = True

    asyncio.run(server._handle_function(_function_packet("[1, 2, 3]"), writer))

    assert calls == []


def test_tracking_callback_error_does_not_escape_handler():
    def raise_from_callback(_: dict[str, object]) -> None:
        raise OverflowError("bad frame")

    server = PicoBridgeServer(on_tracking=raise_from_callback)
    writer = _FakeWriter("active")
    server._writer = writer
    server._connected = True

    asyncio.run(server._handle_function(_function_packet({"timeStampNs": 1}), writer))

    assert server.connected is True


def test_new_client_replaces_stale_connection():
    async def run() -> None:
        server = PicoBridgeServer()
        first_reader = _FakeReader()
        first_writer = _FakeWriter("first")
        second_reader = _FakeReader()
        second_writer = _FakeWriter("second")
        first_task = asyncio.create_task(
            server._handle_client(first_reader, first_writer)
        )
        await _wait_until(lambda: server.connected and server._writer is first_writer)

        try:
            second_task = asyncio.create_task(
                server._handle_client(second_reader, second_writer)
            )
            await _wait_until(
                lambda: server.connected
                and server._writer is second_writer
                and first_writer.is_closing()
            )

            assert server.connected is True

            second_reader.feed_eof()
            await second_task
            await _wait_until(lambda: not server.connected)
        finally:
            first_reader.feed_eof()
            await first_task

    asyncio.run(run())


def test_stale_connection_packets_cannot_touch_active_connection():
    async def run() -> None:
        server = PicoBridgeServer()
        first_reader = _FakeReader()
        first_writer = _FakeWriter("first")
        second_reader = _FakeReader()
        second_writer = _FakeWriter("second")
        first_task = asyncio.create_task(server._handle_client(first_reader, first_writer))
        await _wait_until(lambda: server.connected and server._writer is first_writer)

        try:
            second_task = asyncio.create_task(
                server._handle_client(second_reader, second_writer)
            )
            await _wait_until(
                lambda: server.connected
                and server._writer is second_writer
                and first_writer.is_closing()
            )

            await server._dispatch(
                Packet(head=HEAD_VR_TO_PC, cmd=CMD.CONNECT, data=b"old-sn|-1", timestamp=0),
                first_writer,
            )
            assert server.device_sn == ""

            await server._dispatch(
                Packet(head=HEAD_VR_TO_PC, cmd=CMD.CLIENT_HEARTBEAT, data=b"", timestamp=0),
                first_writer,
            )
            assert second_writer.writes == []

            await server._dispatch(
                Packet(head=HEAD_VR_TO_PC, cmd=CMD.CONNECT, data=b"new-sn|-1", timestamp=0),
                second_writer,
            )
            assert server.device_sn == "new-sn"

            await server._dispatch(
                Packet(head=HEAD_VR_TO_PC, cmd=CMD.CLIENT_HEARTBEAT, data=b"", timestamp=0),
                second_writer,
            )
            assert len(second_writer.writes) == 1
        finally:
            second_reader.feed_eof()
            first_reader.feed_eof()
            await second_task
            await first_task

    asyncio.run(run())


def test_stop_closes_active_client():
    async def run() -> None:
        server = PicoBridgeServer()
        reader = _FakeReader()
        writer = _FakeWriter("active")
        task = asyncio.create_task(server._handle_client(reader, writer))
        await _wait_until(lambda: server.connected and server._writer is writer)

        await server.stop()

        assert writer.is_closing()
        assert server.connected is False
        assert server.device_sn == ""

        reader.feed_eof()
        await task

    asyncio.run(run())


def test_real_tcp_clients_can_reconnect_repeatedly():
    async def run() -> None:
        server = PicoBridgeServer(host="127.0.0.1", port=0)
        await server.start()
        assert server._server is not None
        socket = server._server.sockets[0]
        host, port = socket.getsockname()[:2]

        try:
            for index in range(4):
                reader, writer = await asyncio.open_connection(host, port)
                writer.write(pack(CMD.CONNECT, f"sn-{index}|-1".encode(), pc_to_vr=False))
                await writer.drain()

                await _wait_until(lambda: server.connected and server.device_sn == f"sn-{index}")

                writer.close()
                await writer.wait_closed()
                await _wait_until(lambda: not server.connected and server.device_sn == "")
        finally:
            await server.stop()

    asyncio.run(run())


def test_disconnect_notifies_camera_stop():
    async def run() -> None:
        stops: list[bool] = []
        server = PicoBridgeServer(on_camera_stop=lambda: stops.append(True))
        reader = _FakeReader()
        writer = _FakeWriter("active")
        task = asyncio.create_task(server._handle_client(reader, writer))
        await _wait_until(lambda: server.connected and server._writer is writer)

        reader.feed_eof()
        await task

        assert stops == [True]
        assert server.connected is False

    asyncio.run(run())


def test_heartbeat_send_failure_drops_active_client():
    async def run() -> None:
        stops: list[bool] = []
        server = PicoBridgeServer(on_camera_stop=lambda: stops.append(True))
        writer = _FakeWriter("active")
        writer.fail_drain = True
        server._writer = writer
        server._connected = True

        await server._dispatch(
            Packet(head=HEAD_VR_TO_PC, cmd=CMD.CLIENT_HEARTBEAT, data=b"", timestamp=0),
            writer,
        )

        assert server.connected is False
        assert writer.is_closing()
        assert stops == [True]

    asyncio.run(run())


class _FakeReader:
    def __init__(self):
        self._eof = asyncio.Event()

    async def read(self, _: int) -> bytes:
        await self._eof.wait()
        return b""

    def feed_eof(self) -> None:
        self._eof.set()


class _FakeWriter:
    def __init__(self, name: str):
        self._name = name
        self._closed = False
        self._closed_event = asyncio.Event()
        self.writes: list[bytes] = []
        self.fail_drain = False

    def get_extra_info(self, name: str):
        if name == "peername":
            return self._name
        return None

    def is_closing(self) -> bool:
        return self._closed

    def close(self) -> None:
        self._closed = True
        self._closed_event.set()

    async def wait_closed(self) -> None:
        await self._closed_event.wait()

    def write(self, data: bytes) -> None:
        self.writes.append(data)

    async def drain(self) -> None:
        if self.fail_drain:
            raise BrokenPipeError("closed")


async def _wait_until(predicate) -> None:
    for _ in range(20):
        if predicate():
            return
        await asyncio.sleep(0.01)
    raise AssertionError("condition was not reached")
