"""Stream a MuJoCo camera render to the headset through PICO Bridge."""

from __future__ import annotations

import argparse
import time

from pico_bridge import PicoBridge


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("model", help="Path to a MuJoCo XML model")
    parser.add_argument("--camera", help="MuJoCo camera name")
    parser.add_argument("--width", type=int, default=1280)
    parser.add_argument("--height", type=int, default=720)
    parser.add_argument("--fps", type=float, default=30.0)
    parser.add_argument("--tcp-port", type=int, default=63901)
    parser.add_argument("--advertise-ip")
    args = parser.parse_args()

    import mujoco

    model = mujoco.MjModel.from_xml_path(args.model)
    data = mujoco.MjData(model)
    renderer = mujoco.Renderer(model, height=args.height, width=args.width)
    frame_interval = 1.0 / max(args.fps, 1.0)

    try:
        with PicoBridge(video="frames", port=args.tcp_port, advertise_ip=args.advertise_ip) as pico:
            print("PICO Bridge ready. Connect the headset to view the MuJoCo camera.")
            while True:
                started = time.monotonic()
                mujoco.mj_step(model, data)
                renderer.update_scene(data, camera=args.camera)
                rgb = renderer.render()
                pico.push_video_frame(rgb)
                elapsed = time.monotonic() - started
                if elapsed < frame_interval:
                    time.sleep(frame_interval - elapsed)
    finally:
        renderer.close()


if __name__ == "__main__":
    main()
