from pico_bridge.discovery import (
    UdpBroadcaster,
    _choose_advertise_ip,
    build_discovery_payload,
    parse_discovery_payload,
)


def test_discovery_payload_roundtrip():
    payload = build_discovery_payload("192.168.1.8", 65000)
    assert parse_discovery_payload(payload) == ("192.168.1.8", 65000)


def test_discovery_payload_without_port_uses_default():
    assert parse_discovery_payload(b"192.168.1.8") == ("192.168.1.8", 63901)


def test_choose_advertise_ip_prefers_private_ipv4():
    assert _choose_advertise_ip([
        "127.0.0.1",
        "203.0.113.7",
        "192.168.1.8",
        "0.0.0.0",
    ]) == "192.168.1.8"


def test_choose_advertise_ip_falls_back_to_loopback():
    assert _choose_advertise_ip(["127.0.0.1", "0.0.0.0", "bad-ip"]) == "127.0.0.1"


def test_udp_broadcaster_respects_explicit_advertise_ip():
    broadcaster = UdpBroadcaster(advertise_ip="192.168.50.20")
    assert broadcaster._get_local_ip() == "192.168.50.20"
