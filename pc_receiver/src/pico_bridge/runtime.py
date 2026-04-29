"""Async runtime internals for the public PICO bridge API."""

from __future__ import annotations

import asyncio
import logging
from dataclasses import dataclass
from typing import Any, Callable

from .camera_request import CameraRequest
from .discovery import UdpBroadcaster
from .frame_store import FrameStore
from .tcp_server import PicoBridgeServer
from .webrtc_sender import WebRtcVideoSender

log = logging.getLogger("pico_bridge.runtime")

RawTrackingCallback = Callable[[dict[str, Any]], None]


@dataclass(frozen=True)
class RuntimeStatus:
    connected: bool
    device_sn: str
    video_enabled: bool
    video_running: bool
    video_source: str | None


class PicoBridgeRuntime:
    """Owns the async transport objects for one in-process bridge."""

    def __init__(
        self,
        *,
        host: str,
        port: int,
        discovery: bool,
        advertise_ip: str | None,
        video: str | None,
        camera_device: str | None,
        frame_store: FrameStore,
        print_tracking: bool = False,
        on_raw_tracking: RawTrackingCallback | None = None,
        on_started: Callable[[], None] | None = None,
    ):
        self._host = host
        self._port = port
        self._discovery_enabled = discovery
        self._advertise_ip = advertise_ip
        self._video_source = video
        self._camera_device = camera_device
        self._frame_store = frame_store
        self._print_tracking = print_tracking
        self._on_raw_tracking = on_raw_tracking
        self._on_started = on_started
        self._stop_event: asyncio.Event | None = None
        self._stop_requested = False
        self._server: PicoBridgeServer | None = None
        self._broadcaster: UdpBroadcaster | None = None
        self._webrtc_sender: WebRtcVideoSender | None = None

    async def run(self) -> None:
        self._stop_event = asyncio.Event()
        if self._stop_requested:
            self._stop_event.set()

        async def server_send_function(name: str, value: Any) -> None:
            if self._server is not None:
                await self._server.send_function(name, value)

        self._server = PicoBridgeServer(
            host=self._host,
            port=self._port,
            on_tracking=self._handle_tracking,
            on_function=self._handle_function,
            on_camera_request=self._handle_camera_request,
            on_camera_stop=self._handle_camera_stop,
        )
        if self._video_source is not None:
            self._webrtc_sender = WebRtcVideoSender(
                server_send_function,
                source=self._video_source,
                camera_device=self._camera_device,
            )

        self._broadcaster = UdpBroadcaster(
            tcp_port=self._port,
            advertise_ip=self._advertise_ip,
        )

        try:
            await self._server.start()
            if self._discovery_enabled:
                await self._broadcaster.start()
        except Exception:
            await self.stop()
            raise

        log.info("PICO bridge runtime listening on %s:%d", self._host, self._port)
        if self._on_started is not None:
            self._on_started()
        try:
            await self._stop_event.wait()
        finally:
            await self.stop()

    async def stop(self) -> None:
        webrtc_sender = self._webrtc_sender
        broadcaster = self._broadcaster
        server = self._server
        self._webrtc_sender = None
        self._broadcaster = None
        self._server = None

        if webrtc_sender is not None:
            await webrtc_sender.stop()
        if broadcaster is not None:
            await broadcaster.stop()
        if server is not None:
            await server.stop()

    def request_stop(self) -> None:
        self._stop_requested = True
        if self._stop_event is not None:
            self._stop_event.set()

    def status(self) -> RuntimeStatus:
        server = self._server
        webrtc_sender = self._webrtc_sender
        return RuntimeStatus(
            connected=False if server is None else server.connected,
            device_sn="" if server is None else server.device_sn,
            video_enabled=webrtc_sender is not None,
            video_running=False if webrtc_sender is None else webrtc_sender.is_running,
            video_source=self._video_source,
        )

    def _handle_tracking(self, data: dict[str, Any]) -> None:
        frame = self._frame_store.append_payload(data)
        if self._on_raw_tracking is not None:
            self._on_raw_tracking(data)
        if self._print_tracking:
            print(f"[{frame.seq:>6}] {frame.summary()}", flush=True)

    def _handle_function(self, name: str, value: Any) -> None:
        sender = self._webrtc_sender
        if sender is not None:
            if name == "WebRtcAnswer":
                self._schedule_sender_task(sender.handle_answer(value), "handle WebRTC answer")
                return
            if name == "WebRtcIceCandidate":
                self._schedule_sender_task(sender.handle_ice_candidate(value), "handle WebRTC ICE candidate")
                return
        log.info("function: %s = %s", name, value)

    @staticmethod
    def _schedule_sender_task(coro: Any, label: str) -> asyncio.Task:
        task = asyncio.create_task(coro)

        def log_failure(done: asyncio.Task) -> None:
            try:
                done.result()
            except asyncio.CancelledError:
                pass
            except Exception:
                log.exception("failed to %s", label)

        task.add_done_callback(log_failure)
        return task

    def _handle_camera_request(self, req: CameraRequest) -> None:
        sender = self._webrtc_sender
        if sender is None:
            log.info("WebRTC camera request ignored because video is disabled")
            return

        async def start_video() -> None:
            try:
                await sender.start(req)
            except Exception:
                log.exception("failed to start WebRTC video sender")

        self._schedule_sender_task(start_video(), "start WebRTC video sender")

    def _handle_camera_stop(self) -> asyncio.Task | None:
        sender = self._webrtc_sender
        if sender is None:
            return None

        async def stop_video() -> None:
            try:
                await sender.stop()
            except Exception:
                log.exception("failed to stop WebRTC video sender")

        return self._schedule_sender_task(stop_video(), "stop WebRTC video sender")
