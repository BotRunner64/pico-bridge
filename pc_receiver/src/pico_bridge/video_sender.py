"""H.264 video sender — streams camera or test pattern to PICO headset.

The headset's MediaDecoder opens a TCP *server* on a given port.
This module connects to that server and pushes raw Annex-B H.264 bytestream
produced by an ffmpeg subprocess.
"""

from __future__ import annotations

import asyncio
import logging
import shutil
import signal
from dataclasses import dataclass
from typing import Any

log = logging.getLogger("pico_bridge.video")


@dataclass
class CameraRequest:
    """Parsed StartReceivePcCamera parameters."""
    ip: str
    port: int
    width: int = 2160
    height: int = 1440
    fps: int = 60
    bitrate: int = 40 * 1024 * 1024

    @classmethod
    def from_json(cls, obj: dict[str, Any]) -> "CameraRequest":
        return cls(
            ip=str(obj["ip"]),
            port=int(obj["port"]),
            width=int(obj.get("width", 2160)),
            height=int(obj.get("height", 1440)),
            fps=int(obj.get("fps", 60)),
            bitrate=int(obj.get("bitrate", 40 * 1024 * 1024)),
        )


def _find_ffmpeg() -> str:
    path = shutil.which("ffmpeg")
    if not path:
        raise RuntimeError("ffmpeg not found in PATH")
    return path


def _build_ffmpeg_args(
    req: CameraRequest,
    *,
    source: str = "test-pattern",
    camera_device: str | None = None,
) -> list[str]:
    """Build ffmpeg command line for H.264 Annex-B output to stdout."""
    ffmpeg = _find_ffmpeg()
    args = [ffmpeg, "-hide_banner", "-loglevel", "warning"]

    if source == "test-pattern":
        args += [
            "-f", "lavfi",
            "-i", f"testsrc=size={req.width}x{req.height}:rate={req.fps}",
        ]
    elif source == "camera":
        import sys
        if sys.platform == "linux":
            dev = camera_device or "/dev/video0"
            args += ["-f", "v4l2", "-framerate", str(req.fps),
                     "-video_size", f"{req.width}x{req.height}", "-i", dev]
        elif sys.platform == "darwin":
            dev = camera_device or "0"
            args += ["-f", "avfoundation", "-framerate", str(req.fps),
                     "-video_size", f"{req.width}x{req.height}", "-i", f"{dev}:none"]
        elif sys.platform == "win32":
            dev = camera_device or "video=Integrated Camera"
            args += ["-f", "dshow", "-framerate", str(req.fps),
                     "-video_size", f"{req.width}x{req.height}", "-i", dev]
        else:
            raise RuntimeError(f"Unsupported platform: {sys.platform}")
    else:
        raise ValueError(f"Unknown source: {source}")

    # H.264 Annex-B output to stdout
    args += [
        "-c:v", "libx264",
        "-preset", "ultrafast",
        "-tune", "zerolatency",
        "-b:v", str(req.bitrate),
        "-maxrate", str(req.bitrate),
        "-bufsize", str(req.bitrate // 2),
        "-g", str(req.fps),  # keyframe every second
        "-f", "h264",        # raw Annex-B
        "-an",               # no audio
        "pipe:1",
    ]
    return args


class VideoSender:
    """Manages ffmpeg subprocess and TCP connection to headset decoder."""

    def __init__(
        self,
        source: str = "test-pattern",
        camera_device: str | None = None,
    ):
        self._source = source
        self._camera_device = camera_device
        self._proc: asyncio.subprocess.Process | None = None
        self._send_task: asyncio.Task[None] | None = None
        self._running = False

    @property
    def is_running(self) -> bool:
        return self._running

    async def start(self, req: CameraRequest) -> None:
        """Start streaming to the headset's MediaDecoder TCP server."""
        if self._running:
            await self.stop()

        log.info("Connecting to headset decoder at %s:%d", req.ip, req.port)
        log.info("Video: %dx%d @%dfps %dkbps source=%s",
                 req.width, req.height, req.fps, req.bitrate // 1024, self._source)

        args = _build_ffmpeg_args(
            req, source=self._source, camera_device=self._camera_device,
        )
        log.debug("ffmpeg command: %s", " ".join(args))

        self._proc = await asyncio.create_subprocess_exec(
            *args,
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.PIPE,
        )
        self._running = True
        self._send_task = asyncio.create_task(self._stream_loop(req))

    async def _stream_loop(self, req: CameraRequest) -> None:
        """Read from ffmpeg stdout, push to headset TCP server."""
        assert self._proc is not None and self._proc.stdout is not None

        reader: asyncio.StreamReader | None = None
        writer: asyncio.StreamWriter | None = None

        try:
            # Connect to headset's MediaDecoder TCP server
            reader_unused, writer = await asyncio.wait_for(
                asyncio.open_connection(req.ip, req.port),
                timeout=10.0,
            )
            reader = reader_unused  # noqa: F841 — we don't read from headset
            log.info("Connected to headset decoder")

            while self._running:
                chunk = await self._proc.stdout.read(65536)
                if not chunk:
                    log.info("ffmpeg stream ended")
                    break
                writer.write(chunk)
                await writer.drain()

        except asyncio.TimeoutError:
            log.error("Timeout connecting to headset decoder at %s:%d", req.ip, req.port)
        except (ConnectionRefusedError, OSError) as e:
            log.error("Connection to headset decoder failed: %s", e)
        except (ConnectionResetError, BrokenPipeError):
            log.warning("Headset decoder connection lost")
        finally:
            self._running = False
            if writer:
                writer.close()
                try:
                    await writer.wait_closed()
                except Exception:
                    pass
            await self._kill_ffmpeg()

    async def stop(self) -> None:
        """Stop streaming and clean up."""
        self._running = False
        if self._send_task and not self._send_task.done():
            self._send_task.cancel()
            try:
                await self._send_task
            except asyncio.CancelledError:
                pass
        self._send_task = None
        await self._kill_ffmpeg()
        log.info("Video sender stopped")

    async def _kill_ffmpeg(self) -> None:
        if self._proc is None:
            return
        if self._proc.returncode is None:
            try:
                self._proc.send_signal(signal.SIGTERM)
                try:
                    await asyncio.wait_for(self._proc.wait(), timeout=3.0)
                except asyncio.TimeoutError:
                    self._proc.kill()
                    await self._proc.wait()
            except ProcessLookupError:
                pass
        # Drain stderr for diagnostics
        if self._proc.stderr:
            err = await self._proc.stderr.read()
            if err:
                log.debug("ffmpeg stderr: %s", err.decode(errors="replace").strip())
        self._proc = None
