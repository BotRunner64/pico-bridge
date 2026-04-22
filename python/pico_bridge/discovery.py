"""UDP broadcast for server discovery — mirrors XRobo protocol.

PC server broadcasts its IP on UDP port 29888 using CMD 0x7E (TCPIP).
VR headset listens on that port and auto-discovers available servers.
"""

from __future__ import annotations

import asyncio
import ipaddress
import logging
import socket

from .protocol import CMD, pack

log = logging.getLogger("pico_bridge.discovery")

UDP_DISCOVERY_PORT = 29888
DEFAULT_TCP_PORT = 63901
BROADCAST_INTERVAL = 2.0  # seconds


def build_discovery_payload(ip: str, tcp_port: int) -> bytes:
    return f"{ip}|{tcp_port}".encode("utf-8")


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
    private_ips: list[str] = []
    other_ips: list[str] = []

    for candidate in candidates:
        try:
            address = ipaddress.ip_address(candidate)
        except ValueError:
            continue

        if address.version != 4 or address.is_unspecified or address.is_loopback:
            continue

        if address.is_private:
            private_ips.append(candidate)
        else:
            other_ips.append(candidate)

    if private_ips:
        return sorted(set(private_ips))[0]
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

        candidates: list[str] = []

        try:
            with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
                sock.connect(("10.255.255.255", 1))
                candidates.append(sock.getsockname()[0])
        except OSError:
            pass

        for hostname in (socket.gethostname(), socket.getfqdn()):
            try:
                _, _, resolved = socket.gethostbyname_ex(hostname)
            except OSError:
                continue
            candidates.extend(resolved)

        return _choose_advertise_ip(candidates)

    async def _broadcast_loop(self) -> None:
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
        sock.setblocking(False)

        local_ip = self._get_local_ip()
        packet = pack(CMD.TCPIP, build_discovery_payload(local_ip, self._tcp_port))

        log.info("Broadcasting IP %s to UDP port %d", local_ip, self._udp_port)

        loop = asyncio.get_event_loop()
        try:
            while self._running:
                try:
                    await loop.run_in_executor(
                        None,
                        lambda: sock.sendto(packet, ("255.255.255.255", self._udp_port))
                    )
                except Exception as e:
                    log.debug("broadcast send error: %s", e)
                await asyncio.sleep(BROADCAST_INTERVAL)
        finally:
            sock.close()
