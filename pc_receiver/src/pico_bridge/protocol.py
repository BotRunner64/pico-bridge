"""
PICO Bridge binary protocol.

Packet format:
    [HEAD:1][CMD:1][LEN:4 LE][DATA:N][TIMESTAMP:8 LE][END:1]

DEFAULT_PACKAGE_SIZE = 15 (empty packet, no payload).

Directions:
    VR -> PC:  HEAD=0x3F, END=0xA5
    PC -> VR:  HEAD=0xCF, END=0xA5
"""

from __future__ import annotations

import struct
import time
from dataclasses import dataclass
from enum import IntEnum
from typing import Iterator

# ── constants ──────────────────────────────────────────────

HEAD_VR_TO_PC = 0x3F
HEAD_PC_TO_VR = 0xCF
END_BYTE = 0xA5
HEADER_SIZE = 6          # HEAD(1) + CMD(1) + LEN(4)
TRAILER_SIZE = 9          # TIMESTAMP(8) + END(1)
MIN_PACKET_SIZE = 15      # HEADER_SIZE + TRAILER_SIZE
MAX_PAYLOAD_SIZE = 4 * 1024 * 1024


class CMD(IntEnum):
    CONNECT = 0x19
    SEND_VERSION = 0x6C
    TO_CONTROLLER_FUNCTION = 0x6D
    CLIENT_HEARTBEAT = 0x23
    FROM_CONTROLLER_COMMON_FUNCTION = 0x5F
    CUSTOM_TO_VR = 0x71
    CUSTOM_TO_PC = 0x72
    TCPIP = 0x7E
    MEDIAIP = 0x7F


# ── data types ─────────────────────────────────────────────

@dataclass(slots=True)
class Packet:
    head: int
    cmd: int
    data: bytes
    timestamp: int

    @property
    def direction(self) -> str:
        return "VR->PC" if self.head == HEAD_VR_TO_PC else "PC->VR"


# ── pack ───────────────────────────────────────────────────

def pack(cmd: int, data: bytes = b"", *, pc_to_vr: bool = True) -> bytes:
    """Build a wire-format packet."""
    head = HEAD_PC_TO_VR if pc_to_vr else HEAD_VR_TO_PC
    ts = int(time.time() * 1000)
    return struct.pack("<BBi", head, cmd, len(data)) + data + struct.pack("<qB", ts, END_BYTE)


# ── stream parser ──────────────────────────────────────────

class PacketParser:
    """Incremental TCP stream parser.  Feed arbitrary chunks, yield complete Packets."""

    def __init__(self, *, accept_head: int | None = None):
        self._buf = bytearray()
        self._accept_head = accept_head   # None = accept both directions

    def feed(self, data: bytes) -> Iterator[Packet]:
        self._buf.extend(data)
        while True:
            pkt = self._try_parse()
            if pkt is None:
                break
            yield pkt

    # ── internals ──────────────────────────────────────────

    def _try_parse(self) -> Packet | None:
        # scan for a valid HEAD byte
        while self._buf:
            if self._buf[0] in (HEAD_VR_TO_PC, HEAD_PC_TO_VR):
                if self._accept_head is None or self._buf[0] == self._accept_head:
                    break
            # discard garbage byte
            self._buf.pop(0)

        if len(self._buf) < HEADER_SIZE:
            return None

        head = self._buf[0]
        cmd = self._buf[1]
        (data_len,) = struct.unpack_from("<i", self._buf, 2)
        if data_len < 0 or data_len > MAX_PAYLOAD_SIZE:
            self._buf.pop(0)
            return self._try_parse()

        total = HEADER_SIZE + data_len + TRAILER_SIZE
        if total < MIN_PACKET_SIZE:
            self._buf.pop(0)
            return self._try_parse()
        if len(self._buf) < total:
            return None

        # validate END byte
        if self._buf[total - 1] != END_BYTE:
            # bad packet — discard HEAD and rescan
            self._buf.pop(0)
            return self._try_parse()

        payload = bytes(self._buf[HEADER_SIZE : HEADER_SIZE + data_len])
        (timestamp,) = struct.unpack_from("<q", self._buf, HEADER_SIZE + data_len)

        del self._buf[:total]
        return Packet(head=head, cmd=cmd, data=payload, timestamp=timestamp)
