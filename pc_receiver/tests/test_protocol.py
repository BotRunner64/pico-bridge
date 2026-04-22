"""Tests for the PICO bridge binary protocol."""

import struct
import time

import pytest

from pico_bridge.protocol import (
    CMD,
    END_BYTE,
    HEAD_PC_TO_VR,
    HEAD_VR_TO_PC,
    MAX_PAYLOAD_SIZE,
    MIN_PACKET_SIZE,
    Packet,
    PacketParser,
    pack,
)


# ── helpers ────────────────────────────────────────────────

def _make_vr_packet(cmd: int, data: bytes = b"") -> bytes:
    """Build a VR->PC packet (HEAD=0x3F)."""
    ts = int(time.time() * 1000)
    return (
        struct.pack("<BBi", HEAD_VR_TO_PC, cmd, len(data))
        + data
        + struct.pack("<qB", ts, END_BYTE)
    )


# ── pack() tests ───────────────────────────────────────────

class TestPack:
    def test_empty_packet_length(self):
        pkt = pack(CMD.CLIENT_HEARTBEAT)
        assert len(pkt) == MIN_PACKET_SIZE

    def test_pc_to_vr_head(self):
        pkt = pack(CMD.FROM_CONTROLLER_COMMON_FUNCTION, b"hello", pc_to_vr=True)
        assert pkt[0] == HEAD_PC_TO_VR

    def test_vr_to_pc_head(self):
        pkt = pack(CMD.TO_CONTROLLER_FUNCTION, b"hello", pc_to_vr=False)
        assert pkt[0] == HEAD_VR_TO_PC

    def test_end_byte(self):
        pkt = pack(CMD.CONNECT, b"test")
        assert pkt[-1] == END_BYTE

    def test_payload_length_field(self):
        data = b"SN12345|-1"
        pkt = pack(CMD.CONNECT, data)
        (length,) = struct.unpack_from("<i", pkt, 2)
        assert length == len(data)

    def test_roundtrip(self):
        data = b'{"functionName":"Tracking","value":{}}'
        wire = pack(CMD.TO_CONTROLLER_FUNCTION, data, pc_to_vr=False)
        parser = PacketParser()
        packets = list(parser.feed(wire))
        assert len(packets) == 1
        assert packets[0].cmd == CMD.TO_CONTROLLER_FUNCTION
        assert packets[0].data == data
        assert packets[0].head == HEAD_VR_TO_PC


# ── PacketParser tests ─────────────────────────────────────

class TestPacketParser:
    def test_single_packet(self):
        wire = _make_vr_packet(CMD.CONNECT, b"SN123|-1")
        parser = PacketParser()
        packets = list(parser.feed(wire))
        assert len(packets) == 1
        assert packets[0].cmd == CMD.CONNECT
        assert packets[0].data == b"SN123|-1"

    def test_multiple_packets_in_one_chunk(self):
        wire = (
            _make_vr_packet(CMD.CONNECT, b"SN1|-1")
            + _make_vr_packet(CMD.SEND_VERSION, b"1.0")
            + _make_vr_packet(CMD.CLIENT_HEARTBEAT)
        )
        parser = PacketParser()
        packets = list(parser.feed(wire))
        assert len(packets) == 3
        assert packets[0].cmd == CMD.CONNECT
        assert packets[1].cmd == CMD.SEND_VERSION
        assert packets[2].cmd == CMD.CLIENT_HEARTBEAT

    def test_split_across_chunks(self):
        """Simulate TCP fragmentation — packet split at arbitrary point."""
        wire = _make_vr_packet(CMD.TO_CONTROLLER_FUNCTION, b'{"test":1}')
        mid = len(wire) // 2
        parser = PacketParser()
        packets1 = list(parser.feed(wire[:mid]))
        assert len(packets1) == 0  # not enough data yet
        packets2 = list(parser.feed(wire[mid:]))
        assert len(packets2) == 1
        assert packets2[0].data == b'{"test":1}'

    def test_byte_by_byte(self):
        """Feed one byte at a time."""
        wire = _make_vr_packet(CMD.CLIENT_HEARTBEAT)
        parser = PacketParser()
        all_packets = []
        for b in wire:
            all_packets.extend(parser.feed(bytes([b])))
        assert len(all_packets) == 1
        assert all_packets[0].cmd == CMD.CLIENT_HEARTBEAT

    def test_garbage_before_packet(self):
        """Parser should skip garbage bytes before a valid HEAD."""
        garbage = bytes([0x00, 0xFF, 0x42, 0x99])
        wire = garbage + _make_vr_packet(CMD.CONNECT, b"SN|-1")
        parser = PacketParser()
        packets = list(parser.feed(wire))
        assert len(packets) == 1
        assert packets[0].cmd == CMD.CONNECT

    def test_bad_end_byte_recovery(self):
        """If END byte is wrong, parser should skip and find next packet."""
        good = _make_vr_packet(CMD.CONNECT, b"SN|-1")
        # corrupt the END byte of a copy
        bad = bytearray(_make_vr_packet(CMD.CLIENT_HEARTBEAT))
        bad[-1] = 0x00  # wrong END
        wire = bytes(bad) + good
        parser = PacketParser()
        packets = list(parser.feed(wire))
        # should recover and parse the good packet
        assert any(p.cmd == CMD.CONNECT for p in packets)

    def test_negative_length_recovery(self):
        bad = struct.pack("<BBi", HEAD_VR_TO_PC, CMD.CLIENT_HEARTBEAT, -100) + struct.pack("<qB", 0, END_BYTE)
        good = _make_vr_packet(CMD.CONNECT, b"SN|-1")
        parser = PacketParser()
        packets = list(parser.feed(bad + good))
        assert len(packets) == 1
        assert packets[0].cmd == CMD.CONNECT

    def test_oversized_length_recovery(self):
        bad = struct.pack("<BBi", HEAD_VR_TO_PC, CMD.CLIENT_HEARTBEAT, MAX_PAYLOAD_SIZE + 1) + struct.pack("<qB", 0, END_BYTE)
        good = _make_vr_packet(CMD.CONNECT, b"SN|-1")
        parser = PacketParser()
        packets = list(parser.feed(bad + good))
        assert len(packets) == 1
        assert packets[0].cmd == CMD.CONNECT

    def test_accept_head_filter(self):
        """Parser with accept_head=0x3F should ignore PC->VR packets."""
        vr_pkt = _make_vr_packet(CMD.CONNECT, b"SN|-1")
        pc_pkt = pack(CMD.FROM_CONTROLLER_COMMON_FUNCTION, b"cmd")
        parser = PacketParser(accept_head=HEAD_VR_TO_PC)
        packets = list(parser.feed(pc_pkt + vr_pkt))
        assert len(packets) == 1
        assert packets[0].head == HEAD_VR_TO_PC

    def test_large_payload(self):
        """Handle a realistic tracking JSON payload."""
        import json
        tracking = json.dumps({
            "functionName": "Tracking",
            "value": {
                "predictTime": 16000,
                "Head": {"pose": "0.1,0.2,0.3,0,0,0,1", "status": 3},
                "Controller": {
                    "left": {"pose": "0,0,0,0,0,0,1", "trigger": 0.5},
                    "right": {"pose": "0,0,0,0,0,0,1", "trigger": 0.0},
                },
                "Hand": {},
                "Body": {},
                "Motion": {},
                "Input": 0,
                "timeStampNs": 123456789,
            }
        }).encode()
        wire = _make_vr_packet(CMD.TO_CONTROLLER_FUNCTION, tracking)
        parser = PacketParser()
        packets = list(parser.feed(wire))
        assert len(packets) == 1
        assert b"Tracking" in packets[0].data


# ── Packet dataclass tests ─────────────────────────────────

class TestPacketDataclass:
    def test_direction_vr_to_pc(self):
        p = Packet(head=HEAD_VR_TO_PC, cmd=CMD.CONNECT, data=b"", timestamp=0)
        assert p.direction == "VR->PC"

    def test_direction_pc_to_vr(self):
        p = Packet(head=HEAD_PC_TO_VR, cmd=CMD.CONNECT, data=b"", timestamp=0)
        assert p.direction == "PC->VR"
