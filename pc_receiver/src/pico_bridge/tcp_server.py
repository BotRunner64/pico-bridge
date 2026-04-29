"""Async TCP server that speaks the PICO bridge protocol."""

from __future__ import annotations

import asyncio
import json
import ipaddress
import logging
from typing import Any, Callable

from .protocol import CMD, Packet, PacketParser, pack
from .camera_request import CameraRequest

log = logging.getLogger("pico_bridge.server")

# callback type: async def handler(function_name: str, value: Any) -> None
FunctionCallback = Callable[[str, Any], Any]
TrackingCallback = Callable[[dict[str, Any]], Any]
CameraRequestCallback = Callable[["CameraRequest"], Any]


def _peer_ip(writer: asyncio.StreamWriter) -> str | None:
    peer = writer.get_extra_info("peername")
    if isinstance(peer, tuple) and peer:
        return str(peer[0])
    return None


def _is_loopback_or_local_peer(writer: asyncio.StreamWriter) -> bool:
    peer_ip = _peer_ip(writer)
    sock = writer.get_extra_info("sockname")
    sock_ip = str(sock[0]) if isinstance(sock, tuple) and sock else None
    if not peer_ip:
        return False
    try:
        if ipaddress.ip_address(peer_ip).is_loopback:
            return True
    except ValueError:
        return False
    return sock_ip is not None and peer_ip == sock_ip


class PicoBridgeServer:
    """Single-client TCP server for PICO headset connections."""

    def __init__(
        self,
        host: str = "0.0.0.0",
        port: int = 63901,
        on_tracking: TrackingCallback | None = None,
        on_function: FunctionCallback | None = None,
        on_camera_request: CameraRequestCallback | None = None,
        on_camera_stop: Callable[[], Any] | None = None,
        allow_local_clients: bool = False,
    ):
        self.host = host
        self.port = port
        self._on_tracking = on_tracking
        self._on_function = on_function
        self._on_camera_request = on_camera_request
        self._on_camera_stop = on_camera_stop
        self._allow_local_clients = allow_local_clients
        self._server: asyncio.Server | None = None
        self._writer: asyncio.StreamWriter | None = None
        self._device_sn: str = ""
        self._connected = False
        self._client_lock = asyncio.Lock()

    # ── public API ─────────────────────────────────────────

    async def start(self) -> None:
        self._server = await asyncio.start_server(
            self._handle_client, self.host, self.port
        )
        log.info("listening on %s:%d", self.host, self.port)

    async def stop(self) -> None:
        server = self._server
        self._server = None
        if server:
            server.close()
            await server.wait_closed()

        writer: asyncio.StreamWriter | None = None
        async with self._client_lock:
            writer = self._writer
            self._writer = None
            self._connected = False
            self._device_sn = ""

        if writer is not None:
            await self._close_writer(writer)

    async def send_function(self, name: str, value: Any) -> None:
        """Send a PC->VR function command."""
        if not self._writer:
            log.warning("send_function: no client connected")
            return
        payload = json.dumps({"functionName": name, "value": value}).encode()
        self._writer.write(pack(CMD.FROM_CONTROLLER_COMMON_FUNCTION, payload))
        await self._writer.drain()

    async def send_custom(self, data: bytes) -> None:
        """Send a PC->VR custom binary packet."""
        if not self._writer:
            return
        self._writer.write(pack(CMD.CUSTOM_TO_VR, data))
        await self._writer.drain()

    @property
    def connected(self) -> bool:
        return self._connected

    @property
    def device_sn(self) -> str:
        return self._device_sn

    # ── connection handler ─────────────────────────────────

    async def _handle_client(
        self, reader: asyncio.StreamReader, writer: asyncio.StreamWriter
    ) -> None:
        addr = writer.get_extra_info("peername")
        async with self._client_lock:
            if self._writer is not None and not self._writer.is_closing():
                old_addr = self._writer.get_extra_info("peername")
                if _is_loopback_or_local_peer(self._writer) and not _is_loopback_or_local_peer(writer):
                    log.info("replacing local/editor connection %s with headset client: %s", old_addr, addr)
                else:
                    log.info("replacing stale connection %s with new client: %s", old_addr, addr)
                try:
                    self._writer.close()
                except Exception:
                    pass
                self._writer = None
                self._connected = False

            self._writer = writer
            self._connected = True
            self._device_sn = ""

        log.info("client connected: %s", addr)
        parser = PacketParser(accept_head=0x3F)

        try:
            while True:
                data = await reader.read(65536)
                if not data:
                    break
                for pkt in parser.feed(data):
                    await self._dispatch(pkt, writer)
        except (ConnectionResetError, BrokenPipeError):
            log.info("client disconnected: %s", addr)
        finally:
            async with self._client_lock:
                if self._writer is writer:
                    self._connected = False
                    self._writer = None
                    self._device_sn = ""
            await self._close_writer(writer)
            log.info("connection closed: %s", addr)

    async def _close_writer(self, writer: asyncio.StreamWriter) -> None:
        try:
            writer.close()
            await writer.wait_closed()
        except (ConnectionResetError, BrokenPipeError, OSError) as exc:
            log.debug("Ignoring client close error: %s", exc)

    def _is_active_writer(self, writer: asyncio.StreamWriter) -> bool:
        return self._writer is writer and self._connected and not writer.is_closing()

    async def _dispatch(self, pkt: Packet, writer: asyncio.StreamWriter) -> None:
        if pkt.cmd == CMD.CONNECT:
            self._handle_connect(pkt, writer)
        elif pkt.cmd == CMD.SEND_VERSION:
            self._handle_version(pkt, writer)
        elif pkt.cmd == CMD.CLIENT_HEARTBEAT:
            self._handle_heartbeat(pkt, writer)
        elif pkt.cmd == CMD.TO_CONTROLLER_FUNCTION:
            await self._handle_function(pkt, writer)
        elif pkt.cmd == CMD.CUSTOM_TO_PC:
            log.debug("custom_to_pc: %d bytes", len(pkt.data))
        else:
            log.debug("unknown cmd: 0x%02X (%d bytes)", pkt.cmd, len(pkt.data))

    def _handle_connect(self, pkt: Packet, writer: asyncio.StreamWriter) -> None:
        if not self._is_active_writer(writer):
            return
        text = pkt.data.decode("utf-8", errors="replace")
        parts = text.split("|")
        self._device_sn = parts[0] if parts else text
        log.info("device connected: SN=%s", self._device_sn)

    def _handle_version(self, pkt: Packet, writer: asyncio.StreamWriter) -> None:
        if not self._is_active_writer(writer):
            return
        text = pkt.data.decode("utf-8", errors="replace")
        log.info("device version: %s", text)

    def _handle_heartbeat(self, pkt: Packet, writer: asyncio.StreamWriter) -> None:
        if not self._is_active_writer(writer):
            return
        log.debug("heartbeat from %s", self._device_sn or "unknown")
        writer.write(pack(CMD.CLIENT_HEARTBEAT))

    async def _handle_function(self, pkt: Packet, writer: asyncio.StreamWriter) -> None:
        if not self._is_active_writer(writer):
            return
        text = pkt.data.decode("utf-8", errors="replace")
        try:
            obj = json.loads(text)
        except json.JSONDecodeError as e:
            start = max(e.pos - 80, 0)
            end = min(e.pos + 80, len(text))
            log.warning(
                "bad function JSON at char %d/%d: %s; near=%r; prefix=%r",
                e.pos,
                len(text),
                e.msg,
                text[start:end],
                text[:200],
            )
            return

        fn_name = obj.get("functionName", "")
        value = obj.get("value", obj)

        if fn_name == "Tracking":
            if self._on_tracking:
                try:
                    tracking_data = self._decode_tracking_value(value)
                except (json.JSONDecodeError, TypeError, UnicodeDecodeError) as e:
                    log.warning("bad tracking payload: %s", e)
                    return
                try:
                    self._on_tracking(tracking_data)
                except Exception:
                    log.exception("tracking callback failed; dropping frame")
        elif fn_name == "StartReceivePcCamera":
            self._handle_camera_start(value, writer)
        elif fn_name == "StopReceivePcCamera":
            if self._on_camera_stop:
                self._on_camera_stop()
        else:
            log.info("function: %s", fn_name)
            if self._on_function:
                self._on_function(fn_name, value)

    def _handle_camera_start(self, value: Any, writer: asyncio.StreamWriter | None = None) -> None:
        try:
            obj = value if isinstance(value, dict) else json.loads(value)
            req = CameraRequest.from_json(obj)
        except (json.JSONDecodeError, KeyError, TypeError, ValueError) as e:
            log.warning("bad StartReceivePcCamera payload: %s", e)
            return
        peer_ip = _peer_ip(writer) if writer is not None else None
        if peer_ip and req.ip in ("127.0.0.1", "0.0.0.0", "localhost"):
            log.info("camera request loopback IP overridden by TCP peer: %s -> %s", req.ip, peer_ip)
            req.ip = peer_ip
        elif peer_ip and req.ip != peer_ip:
            log.info("camera request keeps advertised decoder IP %s (control peer=%s)", req.ip, peer_ip)
        log.info("camera request: %s:%d %dx%d @%dfps codec=%s",
                 req.ip, req.port, req.width, req.height, req.fps, req.codec)
        if self._on_camera_request:
            self._on_camera_request(req)

    @staticmethod
    def _decode_tracking_value(value: Any) -> dict[str, Any]:
        if isinstance(value, dict):
            return value
        if isinstance(value, bytes | bytearray):
            value = value.decode("utf-8")
        if isinstance(value, str):
            tracking_data = json.loads(value)
            if isinstance(tracking_data, dict):
                return tracking_data
        raise TypeError(f"expected tracking object or JSON object string, got {type(value).__name__}")
