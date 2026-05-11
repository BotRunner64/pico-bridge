"""Stream a RealSense RGB color stream to the headset through PICO Bridge."""

from __future__ import annotations

import argparse

import numpy as np

from pico_bridge import PicoBridge


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--serial", help="Optional RealSense serial number")
    parser.add_argument("--width", type=int, default=1280)
    parser.add_argument("--height", type=int, default=720)
    parser.add_argument("--fps", type=int, default=30)
    parser.add_argument("--tcp-port", type=int, default=63901)
    parser.add_argument("--advertise-ip")
    args = parser.parse_args()

    import pyrealsense2 as rs

    pipeline = rs.pipeline()
    config = rs.config()
    if args.serial:
        config.enable_device(args.serial)
    config.enable_stream(rs.stream.color, args.width, args.height, rs.format.rgb8, args.fps)
    pipeline.start(config)

    try:
        with PicoBridge(video="frames", port=args.tcp_port, advertise_ip=args.advertise_ip) as pico:
            print("PICO Bridge ready. Connect the headset to view the RealSense color stream.")
            while True:
                frames = pipeline.wait_for_frames()
                color_frame = frames.get_color_frame()
                if not color_frame:
                    continue
                rgb = np.asanyarray(color_frame.get_data())
                pico.push_video_frame(rgb)
    finally:
        pipeline.stop()


if __name__ == "__main__":
    main()
