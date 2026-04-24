"""UDP broadcast for server discovery — mirrors XRobo protocol.

PC server broadcasts its IP on UDP port 29888 using CMD 0x7E (TCPIP).
VR headset listens on that port and auto-discovers available servers.
"""

from __future__ import annotations

import asyncio
import ipaddress
import logging
import re
import socket
import subprocess

from .protocol import CMD, pack

log = logging.getLogger("pico_bridge.discovery")

UDP_DISCOVERY_PORT = 29888
DEFAULT_TCP_PORT = 63901
BROADCAST_INTERVAL = 2.0  # seconds
RFC1918_NETWORKS = (
    ipaddress.ip_network("10.0.0.0/8"),
    ipaddress.ip_network("172.16.0.0/12"),
    ipaddress.ip_network("192.168.0.0/16"),
)
NON_LAN_NETWORKS = (
    ipaddress.ip_network("198.18.0.0/15"),
)


def build_discovery_payload(ip: str, tcp_port: int) -> bytes:
    return f"{ip}|{tcp_port}".encode("utf-8")


def _is_advertisable_ipv4(address: ipaddress.IPv4Address) -> bool:
    return not (
        address.is_unspecified
        or address.is_loopback
        or address.is_link_local
        or address.is_multicast
        or address.is_reserved
        or any(address in network for network in NON_LAN_NETWORKS)
    )


def _is_rfc1918(address: ipaddress.IPv4Address) -> bool:
    return any(address in network for network in RFC1918_NETWORKS)


def _parse_ipv4_networks_from_ip_addr(output: str) -> list[ipaddress.IPv4Interface]:
    networks: list[ipaddress.IPv4Interface] = []
    for match in re.finditer(r"\binet\s+(\d+\.\d+\.\d+\.\d+/\d+)", output):
        try:
            interface = ipaddress.ip_interface(match.group(1))
        except ValueError:
            continue

        if interface.version == 4 and _is_advertisable_ipv4(interface.ip):
            networks.append(interface)

    return networks


def _get_local_ipv4_networks() -> list[ipaddress.IPv4Interface]:
    try:
        import psutil  # type: ignore

        networks: list[ipaddress.IPv4Interface] = []
        for addresses in psutil.net_if_addrs().values():
            for address in addresses:
                if address.family != socket.AF_INET or not address.netmask:
                    continue
                try:
                    interface = ipaddress.ip_interface(f"{address.address}/{address.netmask}")
                except ValueError:
                    continue
                if _is_advertisable_ipv4(interface.ip):
                    networks.append(interface)
        if networks:
            return networks
    except Exception:
        pass

    try:
        result = subprocess.run(
            ["ip", "-o", "-4", "addr", "show", "up"],
            check=False,
            capture_output=True,
            text=True,
            timeout=1,
        )
    except (OSError, subprocess.TimeoutExpired):
        return []

    if result.returncode != 0:
        return []

    return _parse_ipv4_networks_from_ip_addr(result.stdout)


def _broadcast_targets_for_interfaces(
    interfaces: list[ipaddress.IPv4Interface],
) -> list[tuple[str, str]]:
    targets: list[tuple[str, str]] = []
    for interface in interfaces:
        network = interface.network
        if network.prefixlen >= 31:
            continue
        targets.append((str(interface.ip), str(network.broadcast_address)))

    targets.sort(key=lambda item: (not _is_rfc1918(ipaddress.ip_address(item[0])), item[0]))
    return list(dict.fromkeys(targets))


def parse_discovery_payload(payload: bytes) -> tuple[str, int]:
    text = payload.decode("utf-8").strip()
    if not text:
        raise ValueError("empty discovery payload")

    parts = text.split("|", 1)
    ip = parts[0].strip()
    if not ip:
        raise ValueError("missing discovery IP")

    if len(parts) == 1:
        return ip, DEFAULT_TCP_PORT

    return ip, int(parts[1])


def _choose_advertise_ip(candidates: list[str]) -> str:
    lan_ips: list[str] = []
    other_ips: list[str] = []

    for candidate in candidates:
        try:
            address = ipaddress.ip_address(candidate)
        except ValueError:
            continue

        if address.version != 4 or not _is_advertisable_ipv4(address):
            continue

        if any(address in network for network in RFC1918_NETWORKS):
            lan_ips.append(candidate)
        else:
            other_ips.append(candidate)

    if lan_ips:
        return sorted(set(lan_ips))[0]
    if other_ips:
        return sorted(set(other_ips))[0]
    return "127.0.0.1"


class UdpBroadcaster:
    """Periodically broadcasts this server's IP via UDP for headset discovery."""

    def __init__(
        self,
        tcp_port: int = DEFAULT_TCP_PORT,
        udp_port: int = UDP_DISCOVERY_PORT,
        advertise_ip: str | None = None,
    ):
        self._tcp_port = tcp_port
        self._udp_port = udp_port
        self._advertise_ip = advertise_ip
        self._task: asyncio.Task | None = None
        self._running = False

    async def start(self) -> None:
        self._running = True
        self._task = asyncio.create_task(self._broadcast_loop())
        log.info("UDP discovery broadcaster started on port %d", self._udp_port)

    async def stop(self) -> None:
        self._running = False
        if self._task:
            self._task.cancel()
            try:
                await self._task
            except asyncio.CancelledError:
                pass

    def _get_local_ip(self) -> str:
        """Get a LAN-reachable IPv4 address without depending on internet access."""
        if self._advertise_ip:
            return self._advertise_ip

        interface_targets = _broadcast_targets_for_interfaces(_get_local_ipv4_networks())
        if interface_targets:
            return interface_targets[0][0]

        candidates: list[str] = []

        try:
            hostname = socket.gethostname()
            candidates.extend(
                ip for ip in socket.gethostbyname_ex(hostname)[2]
                if not ip.startswith("127.")
            )
        except OSError:
            pass

        try:
            with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
                sock.connect(("10.255.255.255", 1))
                candidates.append(sock.getsockname()[0])
        except OSError:
            pass

        return _choose_advertise_ip(candidates)

    def _get_broadcast_targets(self) -> list[tuple[str, str]]:
        if self._advertise_ip:
            address = ipaddress.ip_address(self._advertise_ip)
            if address.version == 4 and _is_rfc1918(address):
                octets = self._advertise_ip.split(".")
                return [(self._advertise_ip, f"{octets[0]}.{octets[1]}.{octets[2]}.255")]
            return [(self._advertise_ip, "255.255.255.255")]

        interface_targets = _broadcast_targets_for_interfaces(_get_local_ipv4_networks())
        if interface_targets:
            return interface_targets

        return [(self._get_local_ip(), "255.255.255.255")]

    async def _broadcast_loop(self) -> None:
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
        sock.setblocking(False)

        targets = self._get_broadcast_targets()
        packets = [
            (pack(CMD.TCPIP, build_discovery_payload(local_ip, self._tcp_port)), broadcast_ip)
            for local_ip, broadcast_ip in targets
        ]

        log.info(
            "Broadcasting discovery targets %s to UDP port %d",
            ", ".join(f"{local_ip}->{broadcast_ip}" for local_ip, broadcast_ip in targets),
            self._udp_port,
        )

        loop = asyncio.get_event_loop()
        try:
            while self._running:
                for packet, broadcast_ip in packets:
                    try:
                        await loop.run_in_executor(
                            None,
                            lambda packet=packet, broadcast_ip=broadcast_ip: sock.sendto(
                                packet, (broadcast_ip, self._udp_port)
                            )
                        )
                    except Exception as e:
                        log.debug("broadcast send error to %s: %s", broadcast_ip, e)
                await asyncio.sleep(BROADCAST_INTERVAL)
        finally:
            sock.close()
