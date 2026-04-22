"""Async TCP server that speaks the PICO bridge protocol."""

from __future__ import annotations

import asyncio
import json
import logging
import time
from typing import Any, Callable

from .protocol import CMD, END_BYTE, HEAD_PC_TO_VR, Packet, PacketParser, pack

log = logging.getLogger("pico_bridge.server")

# callback type: async def handler(function_name: str, value: Any) -> None
FunctionCallback = Callable[[str, Any], Any]
TrackingCallback = Callable[[dict[str, Any]], Any]


class PicoBridgeServer:
    """Single-client TCP server for PICO headset connections."""

    def __init__(
        self,
        host: str = "0.0.0.0",
        port: int = 63901,
        on_tracking: TrackingCallback | None = None,
        on_function: FunctionCallback | None = None,
    ):
        self.host = host
        self.port = port
        self._on_tracking = on_tracking
        self._on_function = on_function
        self._server: asyncio.Server | None = None
        self._writer: asyncio.StreamWriter | None = None
        self._device_sn: str = ""
        self._connected = False

    # ── public API ─────────────────────────────────────────

    async def start(self) -> None:
        self._server = await asyncio.start_server(
            self._handle_client, self.host, self.port
        )
        log.info("listening on %s:%d", self.host, self.port)

    async def stop(self) -> None:
        if self._server:
            self._server.close()
            await self._server.wait_closed()

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
        log.info("client connected: %s", addr)
        self._writer = writer
        self._connected = True
        parser = PacketParser(accept_head=0x3F)

        try:
            while True:
                data = await reader.read(65536)
                if not data:
                    break
                for pkt in parser.feed(data):
                    await self._dispatch(pkt)
        except (ConnectionResetError, BrokenPipeError):
            log.info("client disconnected: %s", addr)
        finally:
            self._connected = False
            self._writer = None
            writer.close()
            log.info("connection closed: %s", addr)

    async def _dispatch(self, pkt: Packet) -> None:
        if pkt.cmd == CMD.CONNECT:
            self._handle_connect(pkt)
        elif pkt.cmd == CMD.SEND_VERSION:
            self._handle_version(pkt)
        elif pkt.cmd == CMD.CLIENT_HEARTBEAT:
            self._handle_heartbeat(pkt)
        elif pkt.cmd == CMD.TO_CONTROLLER_FUNCTION:
            await self._handle_function(pkt)
        elif pkt.cmd == CMD.CUSTOM_TO_PC:
            log.debug("custom_to_pc: %d bytes", len(pkt.data))
        else:
            log.debug("unknown cmd: 0x%02X (%d bytes)", pkt.cmd, len(pkt.data))

    def _handle_connect(self, pkt: Packet) -> None:
        text = pkt.data.decode("utf-8", errors="replace")
        # format: "deviceSN|-1"
        parts = text.split("|")
        self._device_sn = parts[0] if parts else text
        log.info("device connected: SN=%s", self._device_sn)

    def _handle_version(self, pkt: Packet) -> None:
        text = pkt.data.decode("utf-8", errors="replace")
        log.info("device version: %s", text)

    def _handle_heartbeat(self, pkt: Packet) -> None:
        log.debug("heartbeat from %s", self._device_sn or "unknown")

    async def _handle_function(self, pkt: Packet) -> None:
        text = pkt.data.decode("utf-8", errors="replace")
        try:
            obj = json.loads(text)
        except json.JSONDecodeError:
            log.warning("bad function JSON: %s", text[:200])
            return

        fn_name = obj.get("functionName", "")
        value = obj.get("value", obj)

        if fn_name == "Tracking":
            if self._on_tracking:
                tracking_data = value if isinstance(value, dict) else json.loads(value)
                self._on_tracking(tracking_data)
        else:
            log.info("function: %s", fn_name)
            if self._on_function:
                self._on_function(fn_name, value)
