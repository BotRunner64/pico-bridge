"""H.264 video sender — streams camera or test pattern to PICO headset.

The headset's MediaDecoder opens a TCP *server* on a given port.
This module connects to that server and pushes raw Annex-B H.264 bytestream
produced by PyAV (libav bindings, no external ffmpeg needed).
"""

from __future__ import annotations

import asyncio
import logging
import signal
import threading
import time
from dataclasses import dataclass
from fractions import Fraction
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
    codec: str = "h264"  # "h264" for Android, "mjpeg" for Editor

    @classmethod
    def from_json(cls, obj: dict[str, Any]) -> "CameraRequest":
        return cls(
            ip=str(obj["ip"]),
            port=int(obj["port"]),
            width=int(obj.get("width", 2160)),
            height=int(obj.get("height", 1440)),
            fps=int(obj.get("fps", 60)),
            bitrate=int(obj.get("bitrate", 40 * 1024 * 1024)),
            codec=str(obj.get("codec", "h264")),
        )


def _open_camera(device: str | None, width: int, height: int, fps: int):
    """Open a camera device via PyAV and return the container."""
    import av
    import sys

    if sys.platform == "linux":
        dev = device or "/dev/video0"
        options = {"framerate": str(fps), "video_size": f"{width}x{height}"}
        return av.open(dev, format="v4l2", options=options)
    elif sys.platform == "darwin":
        dev = device or "0"
        options = {"framerate": str(fps), "video_size": f"{width}x{height}"}
        return av.open(f"{dev}:none", format="avfoundation", options=options)
    elif sys.platform == "win32":
        dev = device or "video=Integrated Camera"
        options = {"framerate": str(fps), "video_size": f"{width}x{height}"}
        return av.open(dev, format="dshow", options=options)
    else:
        raise RuntimeError(f"Unsupported platform: {sys.platform}")


def _make_test_frame(width: int, height: int, frame_num: int):
    """Generate a test pattern frame using PyAV."""
    import av
    frame = av.VideoFrame(width, height, "yuv420p")
    # Fill with a shifting color pattern
    for i, plane in enumerate(frame.planes):
        data = bytes([(frame_num * 3 + i + y) & 0xFF for y in range(plane.buffer_size)])
        plane.update(data)
    frame.pts = frame_num
    return frame


def _create_encoder(width: int, height: int, fps: int, bitrate: int):
    """Create an H.264 encoder codec context."""
    import av
    codec = av.CodecContext.create("libx264", "w")
    codec.width = width
    codec.height = height
    codec.pix_fmt = "yuv420p"
    codec.time_base = Fraction(1, fps)
    codec.framerate = Fraction(fps, 1)
    codec.bit_rate = bitrate
    codec.max_b_frames = 0
    codec.gop_size = fps  # keyframe every second
    codec.options = {
        "preset": "ultrafast",
        "tune": "zerolatency",
    }
    codec.open()
    return codec


class VideoSender:
    """Manages PyAV encoding and TCP connection to headset decoder."""

    def __init__(
        self,
        source: str = "test-pattern",
        camera_device: str | None = None,
    ):
        self._source = source
        self._camera_device = camera_device
        self._send_task: asyncio.Task[None] | None = None
        self._running = False
        self._encode_thread: threading.Thread | None = None

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

        self._running = True
        self._send_task = asyncio.create_task(self._stream_loop(req))

    async def _stream_loop(self, req: CameraRequest) -> None:
        """Encode frames and push to headset TCP server."""
        if req.codec == "mjpeg":
            await self._stream_mjpeg(req)
        else:
            await self._stream_h264(req)

    async def _stream_mjpeg(self, req: CameraRequest) -> None:
        """Send length-prefixed JPEG frames for Editor preview."""
        import av
        import io
        import struct

        writer: asyncio.StreamWriter | None = None
        input_container = None

        try:
            _, writer = await asyncio.wait_for(
                asyncio.open_connection(req.ip, req.port),
                timeout=10.0,
            )
            log.info("Connected to Editor MJPEG receiver")

            frame_interval = 1.0 / req.fps
            loop = asyncio.get_running_loop()

            def _encode_jpeg(frame: av.VideoFrame) -> bytes:
                """Convert a video frame to JPEG bytes via PIL."""
                img = frame.to_image()
                if img.size != (req.width, req.height):
                    img = img.resize((req.width, req.height))
                buf = io.BytesIO()
                img.save(buf, format="JPEG", quality=80)
                return buf.getvalue()

            if self._source == "camera":
                input_container = _open_camera(
                    self._camera_device, req.width, req.height, req.fps,
                )
                stream = input_container.streams.video[0]

                frame_queue: asyncio.Queue[bytes | None] = asyncio.Queue(maxsize=2)

                def _decode_camera():
                    try:
                        for raw_frame in input_container.decode(stream):
                            if not self._running:
                                break
                            jpeg_data = _encode_jpeg(raw_frame)
                            header = struct.pack(">I", len(jpeg_data))
                            asyncio.run_coroutine_threadsafe(
                                frame_queue.put(header + jpeg_data), loop
                            ).result(timeout=5)
                    except Exception as e:
                        log.error("Camera decode error: %s", e)
                    finally:
                        asyncio.run_coroutine_threadsafe(frame_queue.put(None), loop)

                decode_thread = threading.Thread(target=_decode_camera, daemon=True)
                decode_thread.start()

                while self._running:
                    data = await frame_queue.get()
                    if data is None:
                        break
                    writer.write(data)
                    await writer.drain()

            elif self._source == "test-pattern":
                frame_num = 0
                while self._running:
                    t_start = time.monotonic()
                    frame = _make_test_frame(req.width, req.height, frame_num)
                    jpeg_data = _encode_jpeg(frame)
                    header = struct.pack(">I", len(jpeg_data))
                    writer.write(header + jpeg_data)
                    await writer.drain()
                    frame_num += 1
                    elapsed = time.monotonic() - t_start
                    if elapsed < frame_interval:
                        await asyncio.sleep(frame_interval - elapsed)

        except asyncio.TimeoutError:
            log.error("Timeout connecting to Editor at %s:%d", req.ip, req.port)
        except (ConnectionRefusedError, OSError) as e:
            log.error("Connection to Editor failed: %s", e)
        except (ConnectionResetError, BrokenPipeError):
            log.warning("Editor connection lost")
        finally:
            self._running = False
            if input_container is not None:
                input_container.close()
            if writer:
                writer.close()
                try:
                    await writer.wait_closed()
                except Exception:
                    pass

    async def _stream_h264(self, req: CameraRequest) -> None:
        """Encode frames and push H.264 packets to headset TCP server."""
        import av

        writer: asyncio.StreamWriter | None = None
        input_container = None

        try:
            # Connect to headset's MediaDecoder TCP server
            _, writer = await asyncio.wait_for(
                asyncio.open_connection(req.ip, req.port),
                timeout=10.0,
            )
            log.info("Connected to headset decoder")

            encoder = _create_encoder(req.width, req.height, req.fps, req.bitrate)
            frame_num = 0
            frame_interval = 1.0 / req.fps

            if self._source == "camera":
                input_container = _open_camera(
                    self._camera_device, req.width, req.height, req.fps,
                )
                stream = input_container.streams.video[0]
                loop = asyncio.get_running_loop()
                frame_queue: asyncio.Queue[bytes | None] = asyncio.Queue(maxsize=2)

                def _decode_h264():
                    try:
                        nonlocal frame_num
                        for raw_frame in input_container.decode(stream):
                            if not self._running:
                                break
                            if raw_frame.format.name != "yuv420p" or \
                               raw_frame.width != req.width or raw_frame.height != req.height:
                                raw_frame = raw_frame.reformat(
                                    width=req.width, height=req.height, format="yuv420p",
                                )
                            raw_frame.pts = frame_num
                            for packet in encoder.encode(raw_frame):
                                asyncio.run_coroutine_threadsafe(
                                    frame_queue.put(bytes(packet)), loop
                                ).result(timeout=5)
                            frame_num += 1
                    except Exception as e:
                        log.error("Camera decode error: %s", e)
                    finally:
                        asyncio.run_coroutine_threadsafe(frame_queue.put(None), loop)

                decode_thread = threading.Thread(target=_decode_h264, daemon=True)
                decode_thread.start()

                while self._running:
                    data = await frame_queue.get()
                    if data is None:
                        break
                    writer.write(data)
                    await writer.drain()

            elif self._source == "test-pattern":
                while self._running:
                    t_start = time.monotonic()
                    frame = _make_test_frame(req.width, req.height, frame_num)
                    for packet in encoder.encode(frame):
                        writer.write(bytes(packet))
                        await writer.drain()
                    frame_num += 1
                    # Pace to target fps
                    elapsed = time.monotonic() - t_start
                    if elapsed < frame_interval:
                        await asyncio.sleep(frame_interval - elapsed)
            else:
                raise ValueError(f"Unknown source: {self._source}")

            # Flush encoder
            for packet in encoder.encode():
                writer.write(bytes(packet))
                await writer.drain()

        except asyncio.TimeoutError:
            log.error("Timeout connecting to headset decoder at %s:%d",
                      req.ip, req.port)
        except (ConnectionRefusedError, OSError) as e:
            log.error("Connection to headset decoder failed: %s", e)
        except (ConnectionResetError, BrokenPipeError):
            log.warning("Headset decoder connection lost")
        finally:
            self._running = False
            if input_container is not None:
                input_container.close()
            if writer:
                writer.close()
                try:
                    await writer.wait_closed()
                except Exception:
                    pass

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
        log.info("Video sender stopped")
