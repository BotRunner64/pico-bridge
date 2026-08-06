"""Stream a RealSense D415 rectified infrared stereo pair to PICO as SBS video."""

from __future__ import annotations

import argparse
import logging

import numpy as np

from pico_bridge import PicoBridge, StereoCameraIntrinsics


LOGGER = logging.getLogger("realsense_d415_sbs")
DEFAULT_EXPOSURE_US = 30_000.0
DEFAULT_GAIN = 48.0


def _infrared_pair_to_rgb(left: np.ndarray, right: np.ndarray) -> np.ndarray:
    """Pack two Y8 infrared frames into one three-channel RGB SBS frame."""

    if left.dtype != np.uint8 or right.dtype != np.uint8:
        raise ValueError("infrared frames must have dtype uint8")
    if left.ndim != 2 or right.ndim != 2:
        raise ValueError("infrared frames must be two-dimensional")
    if left.shape != right.shape:
        raise ValueError("left and right infrared frames must have matching shapes")

    height, eye_width = left.shape
    sbs = np.empty((height, eye_width * 2, 3), dtype=np.uint8)
    sbs[:, :eye_width, :] = left[:, :, np.newaxis]
    sbs[:, eye_width:, :] = right[:, :, np.newaxis]
    return sbs


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--serial", help="Optional RealSense serial number")
    parser.add_argument("--width", type=int, default=1280, help="Per-eye width")
    parser.add_argument("--height", type=int, default=720, help="Per-eye height")
    parser.add_argument("--fps", type=int, default=30)
    parser.add_argument("--tcp-port", type=int, default=63901)
    parser.add_argument("--advertise-ip")
    parser.add_argument(
        "--enable-emitter",
        action="store_true",
        help="Enable the infrared dot projector (disabled by default)",
    )
    parser.add_argument(
        "--auto-exposure",
        action="store_true",
        help="Use sensor auto exposure instead of the default manual settings",
    )
    parser.add_argument(
        "--exposure",
        type=float,
        default=DEFAULT_EXPOSURE_US,
        help=f"Manual exposure in microseconds (default: {DEFAULT_EXPOSURE_US:.0f})",
    )
    parser.add_argument(
        "--gain",
        type=float,
        default=DEFAULT_GAIN,
        help=f"Manual sensor gain (default: {DEFAULT_GAIN:.0f})",
    )
    parser.add_argument("-v", "--verbose", action="store_true")
    args = parser.parse_args()

    logging.basicConfig(
        level=logging.INFO if args.verbose else logging.WARNING,
        format="%(asctime)s %(levelname)s %(name)s: %(message)s",
    )

    try:
        import pyrealsense2 as rs
    except ImportError as exc:
        raise RuntimeError(
            "pyrealsense2 is unavailable; install the camera extra with "
            "`pip install -e '.[camera]'` first"
        ) from exc

    pipeline = rs.pipeline()
    config = rs.config()
    if args.serial:
        config.enable_device(args.serial)
    config.enable_stream(
        rs.stream.infrared,
        1,
        args.width,
        args.height,
        rs.format.y8,
        args.fps,
    )
    config.enable_stream(
        rs.stream.infrared,
        2,
        args.width,
        args.height,
        rs.format.y8,
        args.fps,
    )

    profile = pipeline.start(config)
    try:
        device = profile.get_device()
        device_name = device.get_info(rs.camera_info.name)
        device_serial = device.get_info(rs.camera_info.serial_number)

        depth_sensor = device.first_depth_sensor()
        if depth_sensor.supports(rs.option.emitter_enabled):
            emitter_value = 1.0 if args.enable_emitter else 0.0
            depth_sensor.set_option(rs.option.emitter_enabled, emitter_value)
            LOGGER.info(
                "infrared emitter %s",
                "enabled" if args.enable_emitter else "disabled",
            )
        elif args.enable_emitter:
            LOGGER.warning("this RealSense device does not expose emitter control")

        if args.auto_exposure:
            if not depth_sensor.supports(rs.option.enable_auto_exposure):
                raise RuntimeError(
                    "this RealSense device does not expose auto-exposure control"
                )
            depth_sensor.set_option(rs.option.enable_auto_exposure, 1.0)
            sensor_settings = "auto exposure"
            LOGGER.info("sensor auto exposure enabled")
        else:
            if depth_sensor.supports(rs.option.enable_auto_exposure):
                depth_sensor.set_option(rs.option.enable_auto_exposure, 0.0)
            if not depth_sensor.supports(rs.option.exposure):
                raise RuntimeError(
                    "this RealSense device does not expose manual exposure control"
                )
            if not depth_sensor.supports(rs.option.gain):
                raise RuntimeError(
                    "this RealSense device does not expose manual gain control"
                )
            depth_sensor.set_option(rs.option.exposure, args.exposure)
            depth_sensor.set_option(rs.option.gain, args.gain)
            actual_exposure = float(depth_sensor.get_option(rs.option.exposure))
            actual_gain = float(depth_sensor.get_option(rs.option.gain))
            sensor_settings = (
                f"manual exposure {actual_exposure:.0f} us, gain {actual_gain:.0f}"
            )
            LOGGER.info(
                "manual sensor exposure %.0f us, gain %.0f",
                actual_exposure,
                actual_gain,
            )

        left_profile = profile.get_stream(
            rs.stream.infrared, 1
        ).as_video_stream_profile()
        intrinsics = left_profile.get_intrinsics()
        stereo_intrinsics = StereoCameraIntrinsics(
            eye_width=int(intrinsics.width),
            eye_height=int(intrinsics.height),
            fx=float(intrinsics.fx),
            fy=float(intrinsics.fy),
            cx=float(intrinsics.ppx),
            cy=float(intrinsics.ppy),
        )

        with PicoBridge(
            video="frames",
            video_layout="stereo-sbs",
            stereo_intrinsics=stereo_intrinsics,
            port=args.tcp_port,
            advertise_ip=args.advertise_ip,
        ) as pico:
            print(
                f"PICO Bridge ready. Streaming {device_name} ({device_serial}) "
                f"infrared SBS at {intrinsics.width * 2}x{intrinsics.height} "
                f"and {args.fps} fps with {sensor_settings}."
            )
            while True:
                frames = pipeline.wait_for_frames()
                left_frame = frames.get_infrared_frame(1)
                right_frame = frames.get_infrared_frame(2)
                if not left_frame or not right_frame:
                    continue

                left = np.asanyarray(left_frame.get_data())
                right = np.asanyarray(right_frame.get_data())
                pico.push_video_frame(_infrared_pair_to_rgb(left, right))
    finally:
        pipeline.stop()


if __name__ == "__main__":
    main()
