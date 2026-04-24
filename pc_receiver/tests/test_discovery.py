from pico_bridge.discovery import (
    UdpBroadcaster,
    _broadcast_targets_for_interfaces,
    _choose_advertise_ip,
    _parse_ipv4_networks_from_ip_addr,
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


def test_choose_advertise_ip_ignores_benchmark_and_link_local_ranges():
    assert _choose_advertise_ip([
        "198.18.0.1",
        "169.254.12.34",
        "192.168.50.20",
    ]) == "192.168.50.20"


def test_choose_advertise_ip_falls_back_to_loopback():
    assert _choose_advertise_ip(["127.0.0.1", "0.0.0.0", "bad-ip"]) == "127.0.0.1"


def test_udp_broadcaster_respects_explicit_advertise_ip():
    broadcaster = UdpBroadcaster(advertise_ip="192.168.50.20")
    assert broadcaster._get_local_ip() == "192.168.50.20"


def test_parse_ipv4_networks_from_ip_addr_skips_non_lan_addresses():
    output = """
1: lo    inet 127.0.0.1/8 scope host lo
2: eth0  inet 198.18.0.1/15 brd 198.19.255.255 scope global eth0
3: wlan0 inet 192.168.50.20/24 brd 192.168.50.255 scope global wlan0
4: usb0  inet 169.254.12.34/16 brd 169.254.255.255 scope link usb0
"""

    assert [str(interface) for interface in _parse_ipv4_networks_from_ip_addr(output)] == [
        "192.168.50.20/24",
    ]


def test_broadcast_targets_for_interfaces_sends_each_subnet():
    interfaces = _parse_ipv4_networks_from_ip_addr("""
2: eth0  inet 10.1.2.3/16 brd 10.1.255.255 scope global eth0
3: wlan0 inet 192.168.50.20/24 brd 192.168.50.255 scope global wlan0
""")

    assert _broadcast_targets_for_interfaces(interfaces) == [
        ("10.1.2.3", "10.1.255.255"),
        ("192.168.50.20", "192.168.50.255"),
    ]
