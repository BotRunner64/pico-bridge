"""Stream rectified ZED Mini stereo video to PICO as one side-by-side frame."""

from __future__ import annotations

import argparse
import logging

import numpy as np

from pico_bridge import PicoBridge, StereoCameraIntrinsics


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tcp-port", type=int, default=63901)
    parser.add_argument("--advertise-ip")
    parser.add_argument("--fps", type=int, choices=(15, 30, 60), default=60)
    parser.add_argument("-v", "--verbose", action="store_true")
    args = parser.parse_args()

    logging.basicConfig(
        level=logging.INFO if args.verbose else logging.WARNING,
        format="%(asctime)s %(levelname)s %(name)s: %(message)s",
    )

    try:
        import pyzed.sl as sl
    except ImportError as exc:
        raise RuntimeError(
            "pyzed is unavailable; install the ZED SDK and its bundled Python API first"
        ) from exc

    zed = sl.Camera()
    init = sl.InitParameters()
    init.camera_resolution = sl.RESOLUTION.HD720
    init.camera_fps = args.fps
    init.depth_mode = sl.DEPTH_MODE.NONE
    init.enable_image_validity_check = 1

    error = zed.open(init)
    if error != sl.ERROR_CODE.SUCCESS:
        raise RuntimeError(f"failed to open ZED camera: {error}")

    camera_configuration = zed.get_camera_information().camera_configuration
    left_camera = camera_configuration.calibration_parameters.left_cam
    stereo_intrinsics = StereoCameraIntrinsics(
        eye_width=int(camera_configuration.resolution.width),
        eye_height=int(camera_configuration.resolution.height),
        fx=float(left_camera.fx),
        fy=float(left_camera.fy),
        cx=float(left_camera.cx),
        cy=float(left_camera.cy),
    )

    sbs = sl.Mat()
    try:
        with PicoBridge(
            video="frames",
            video_layout="stereo-sbs",
            stereo_intrinsics=stereo_intrinsics,
            port=args.tcp_port,
            advertise_ip=args.advertise_ip,
        ) as pico:
            print("PICO Bridge ready. Connect the headset to view the ZED stereo stream.")
            while True:
                if zed.grab() != sl.ERROR_CODE.SUCCESS:
                    continue

                zed.retrieve_image(sbs, sl.VIEW.SIDE_BY_SIDE)
                bgra = sbs.get_data()
                rgb = np.ascontiguousarray(bgra[..., [2, 1, 0]])
                pico.push_video_frame(rgb)
    finally:
        zed.close()


if __name__ == "__main__":
    main()
