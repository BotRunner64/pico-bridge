"""Stream an OpenCV camera to the headset through PICO Bridge."""

from __future__ import annotations

import argparse
import time

from pico_bridge import PicoBridge


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--device", default=0, help="OpenCV camera index or path")
    parser.add_argument("--tcp-port", type=int, default=63901)
    parser.add_argument("--advertise-ip")
    args = parser.parse_args()

    import cv2

    device: int | str
    try:
        device = int(args.device)
    except ValueError:
        device = args.device

    capture = cv2.VideoCapture(device)
    if not capture.isOpened():
        raise RuntimeError(f"failed to open OpenCV camera: {args.device}")

    try:
        with PicoBridge(video="frames", port=args.tcp_port, advertise_ip=args.advertise_ip) as pico:
            print("PICO Bridge ready. Connect the headset to view the OpenCV camera.")
            while True:
                ok, bgr = capture.read()
                if not ok:
                    time.sleep(0.01)
                    continue
                rgb = cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB)
                pico.push_video_frame(rgb)
    finally:
        capture.release()


if __name__ == "__main__":
    main()
