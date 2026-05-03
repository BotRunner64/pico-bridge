"""Real-time 3D visualisation of tracking data using Rerun.

Shows head, controllers, hands, and body as 3D objects in space,
plus a status bar showing which signals are active.

Usage:
    python bridge.py -v --video camera --viz
"""

from __future__ import annotations

import atexit
import inspect
import time
from collections import deque
from typing import Any

import numpy as np
import rerun as rr
import rerun.blueprint as rrb

_initialised = False
_frame_count = 0
_connected = False
_device_sn = ""
_start_time = 0.0
_fps_samples: deque[float] = deque(maxlen=120)
_follow_enabled = True

# PICO HandJoint topology, indexed by Unity.XR.PXR.HandJoint:
# Palm, wrist, then thumb/index/middle/ring/little chains.
_HAND_BONES = [
    (1, 0),
    (1, 2), (2, 3), (3, 4), (4, 5),
    (1, 6), (6, 7), (7, 8), (8, 9), (9, 10),
    (1, 11), (11, 12), (12, 13), (13, 14), (14, 15),
    (1, 16), (16, 17), (17, 18), (18, 19), (19, 20),
    (1, 21), (21, 22), (22, 23), (23, 24), (24, 25),
    (6, 11), (11, 16), (16, 21),
]

# PICO BodyTrackerRole topology, indexed by BodyTrackerRole enum:
# Pelvis, hips, spine chain, legs, neck/head, collars, arms, hands.
_BODY_BONES = [
    (0, 1), (1, 4), (4, 7), (7, 10),
    (0, 2), (2, 5), (5, 8), (8, 11),
    (0, 3), (3, 6), (6, 9), (9, 12), (12, 15),
    (12, 13), (13, 16), (16, 18), (18, 20), (20, 22),
    (12, 14), (14, 17), (17, 19), (19, 21), (21, 23),
]

# Track which signals are active in the current frame
_signals: dict[str, bool] = {}

# Colors
_HEAD_COLOR = [0, 200, 255, 255]
_CTRL_L_COLOR = [255, 160, 0, 255]
_CTRL_R_COLOR = [0, 230, 120, 255]
_HAND_L_COLOR = [255, 220, 50, 255]
_HAND_R_COLOR = [50, 180, 255, 255]
_BODY_COLOR = [0, 230, 180, 255]
_MOTION_COLOR = [255, 90, 170, 255]
_FOCUS_COLOR = [255, 255, 255, 96]
_GRID_COLOR = [255, 255, 255, 15]
_FOCUS_ENTITY_PATH = "world/focus"


def _clear_path(path: str) -> None:
    rr.log(path, rr.Clear(recursive=True))


def init(spawn: bool = True, connect: bool = False, follow: bool = True) -> None:
    global _initialised, _start_time, _follow_enabled
    if _initialised:
        return

    _follow_enabled = follow
    rr.init("pico_bridge")

    eye_controls = None
    if follow:
        eye_controls = rrb.EyeControls3D(
            kind=rrb.Eye3DKind.Orbital,
            tracking_entity=_FOCUS_ENTITY_PATH,
        )

    blueprint = rrb.Blueprint(
        rrb.Vertical(
            rrb.Spatial3DView(
                name="Tracking",
                origin="world",
                background=rrb.Background(color=[18, 18, 28]),
                eye_controls=eye_controls,
            ),
            rrb.TextDocumentView(
                name="Status",
                origin="status",
            ),
            row_shares=[10, 1],
        ),
        rrb.BlueprintPanel(state=rrb.PanelState.Collapsed),
        rrb.SelectionPanel(state=rrb.PanelState.Collapsed),
        rrb.TimePanel(state=rrb.PanelState.Collapsed),
    )

    if connect:
        _connect_viewer()
    elif spawn:
        _spawn_viewer()

    rr.send_blueprint(blueprint)
    rr.log("world", rr.ViewCoordinates.RIGHT_HAND_Y_UP, static=True)

    # Subtle ground grid
    lines = []
    for i in range(-4, 5):
        lines.append([[i, 0, -4], [i, 0, 4]])
        lines.append([[-4, 0, i], [4, 0, i]])
    rr.log("world/ground", rr.LineStrips3D(
        lines, colors=[_GRID_COLOR],
    ), static=True)

    # Origin axes
    rr.log("world/origin", rr.Arrows3D(
        origins=[[0, 0, 0], [0, 0, 0], [0, 0, 0]],
        vectors=[[0.3, 0, 0], [0, 0.3, 0], [0, 0, 0.3]],
        colors=[[255, 60, 60, 180], [60, 255, 60, 180], [60, 60, 255, 180]],
    ), static=True)

    _initialised = True
    _start_time = time.monotonic()
    atexit.register(close)


def close() -> None:
    global _initialised, _connected, _device_sn
    if not _initialised:
        return

    disconnect = getattr(rr, "disconnect", None)
    if disconnect is not None:
        disconnect()

    _initialised = False
    _connected = False
    _device_sn = ""


def _connect_viewer() -> None:
    connect = getattr(rr, "connect", None) or getattr(rr, "connect_grpc", None)
    if connect is None:
        raise RuntimeError("installed rerun-sdk does not provide a viewer connect API")
    connect()


def _spawn_viewer() -> None:
    kwargs: dict[str, Any] = {}
    if "detach_process" in inspect.signature(rr.spawn).parameters:
        kwargs["detach_process"] = False
    rr.spawn(**kwargs)


def push_frame(data: dict[str, Any]) -> None:
    global _frame_count
    if not _initialised:
        return
    _frame_count += 1
    now = time.monotonic()
    _fps_samples.append(now)
    _set_time("frame", sequence=_frame_count)
    _set_time("time", duration=now - _start_time)

    _signals.clear()
    _log_tracking_focus(data)
    _log_head(data.get("Head", {}))
    _log_controllers(data.get("Controller", {}))
    _log_hands(data.get("Hand", {}))
    _log_body(data.get("Body", {}))
    _log_motion(data.get("Motion", {}))
    _log_status()


def set_connection_state(connected: bool, device_sn: str = "") -> None:
    global _connected, _device_sn
    _connected = connected
    _device_sn = device_sn


def _parse_pose(s: object) -> tuple[list[float], list[float]] | None:
    if not isinstance(s, str):
        return None
    parts = s.split(",")
    if len(parts) < 7:
        return None
    try:
        v = [float(p) for p in parts[:7]]
    except ValueError:
        return None
    return v[:3], v[3:]


def _log_tracking_focus(data: dict[str, Any]) -> None:
    if not _follow_enabled:
        return

    center = _compute_tracking_center(data)
    if center is None:
        _clear_path(_FOCUS_ENTITY_PATH)
        return

    rr.log(_FOCUS_ENTITY_PATH, rr.Transform3D(translation=center))
    rr.log(_FOCUS_ENTITY_PATH, rr.Points3D(
        [[0.0, 0.0, 0.0]], colors=[_FOCUS_COLOR], radii=[0.03],
    ))


def _compute_tracking_center(data: dict[str, Any]) -> list[float] | None:
    body = data.get("Body", {})
    if _body_is_active(body):
        body_points = _pose_points_from_joints(body.get("joints", []))
        if body_points:
            return _bounds_center(body_points)

    points: list[list[float]] = []
    _append_pose(points, data.get("Head", {}).get("pose"))

    controller = data.get("Controller", {})
    for side in ("left", "right"):
        _append_pose(points, controller.get(side, {}).get("pose"))

    hand = data.get("Hand", {})
    for side in ("leftHand", "rightHand"):
        if hand.get(side, {}).get("isActive"):
            points.extend(_pose_points_from_joints(hand.get(side, {}).get("HandJointLocations", [])))

    points.extend(_pose_points_from_joints(data.get("Motion", {}).get("joints", [])))
    return _bounds_center(points) if points else None


def _pose_points_from_joints(joints: object) -> list[list[float]]:
    if not isinstance(joints, list):
        return []

    points = []
    for joint in joints:
        if not isinstance(joint, dict):
            continue
        parsed = _parse_pose(joint.get("p", ""))
        if parsed:
            points.append(parsed[0])
    return points


def _append_pose(points: list[list[float]], pose: object) -> None:
    parsed = _parse_pose(pose)
    if parsed:
        points.append(parsed[0])


def _bounds_center(points: list[list[float]]) -> list[float]:
    return [
        (min(point[axis] for point in points) + max(point[axis] for point in points)) * 0.5
        for axis in range(3)
    ]


def _compute_fps() -> float:
    samples = list(_fps_samples)
    if len(samples) < 2:
        return 0.0
    dt = samples[-1] - samples[0]
    return (len(samples) - 1) / dt if dt > 0 else 0.0


def _set_time(timeline: str, **kwargs: Any) -> None:
    set_time = getattr(rr, "set_time", None)
    if set_time is not None:
        set_time(timeline, **kwargs)
        return

    sequence = kwargs.get("sequence")
    if sequence is not None and hasattr(rr, "set_time_sequence"):
        rr.set_time_sequence(timeline, sequence)
        return

    duration = kwargs.get("duration")
    if duration is not None and hasattr(rr, "set_time_seconds"):
        rr.set_time_seconds(timeline, duration)


# ── 3D logging ────────────────────────────────────────────

def _log_head(head: dict) -> None:
    p = head.get("pose")
    if not p:
        _signals["Head"] = False
        _clear_path("world/head")
        return
    parsed = _parse_pose(p)
    if not parsed:
        _signals["Head"] = False
        _clear_path("world/head")
        return
    _signals["Head"] = True
    pos, q = parsed
    rr.log("world/head", rr.Transform3D(translation=pos, rotation=rr.Quaternion(xyzw=q)))
    # Sphere for head
    rr.log("world/head/shape", rr.Ellipsoids3D(
        half_sizes=[[0.10, 0.08, 0.12]],
        colors=[_HEAD_COLOR],
    ))
    # Gaze direction — thick bright arrow
    rr.log("world/head/gaze", rr.Arrows3D(
        origins=[[0, 0, 0]], vectors=[[0, 0, -0.35]],
        colors=[_HEAD_COLOR], radii=[0.008],
    ))


def _log_controllers(ctrl: dict) -> None:
    configs = [("left", _CTRL_L_COLOR), ("right", _CTRL_R_COLOR)]
    for side, color in configs:
        c = ctrl.get(side, {})
        p = c.get("pose")
        key = f"Ctrl-{side[0].upper()}"
        path = f"world/ctrl/{side}"
        if not p:
            _signals[key] = False
            _clear_path(path)
            continue
        parsed = _parse_pose(p)
        if not parsed:
            _signals[key] = False
            _clear_path(path)
            continue
        _signals[key] = True
        pos, q = parsed
        rr.log(path, rr.Transform3D(translation=pos, rotation=rr.Quaternion(xyzw=q)))
        # Rounded controller shape
        rr.log(f"{path}/shape", rr.Ellipsoids3D(
            half_sizes=[[0.035, 0.025, 0.07]],
            colors=[color],
        ))
        # Pointer ray
        rr.log(f"{path}/ray", rr.Arrows3D(
            origins=[[0, 0, 0]], vectors=[[0, 0, -0.3]],
            colors=[color], radii=[0.004],
        ))


def _log_hands(hand: dict) -> None:
    configs = [("leftHand", "Hand-L", _HAND_L_COLOR), ("rightHand", "Hand-R", _HAND_R_COLOR)]
    for side, key, color in configs:
        h = hand.get(side, {})
        path = f"world/hand/{side}"
        active = bool(h.get("isActive"))
        _signals[key] = active
        if not active:
            _clear_path(path)
            continue
        joints = h.get("HandJointLocations", [])
        if not joints:
            _clear_path(path)
            continue
        pts = []
        for j in joints:
            parsed = _parse_pose(j.get("p", ""))
            pts.append(parsed[0] if parsed else [0, 0, 0])
        pts = np.array(pts, dtype=np.float32)
        rr.log(f"{path}/pts", rr.Points3D(pts, colors=[color], radii=[0.006]))
        bones = [
            [pts[a].tolist(), pts[b].tolist()]
            for a, b in _HAND_BONES
            if a < len(pts) and b < len(pts)
        ]
        if bones:
            rr.log(f"{path}/bones", rr.LineStrips3D(bones, colors=[color], radii=[0.003]))
        else:
            rr.log(f"{path}/bones", rr.Clear(recursive=False))


def _log_body(body: dict) -> None:
    joints = body.get("joints", [])
    active = _body_is_active(body)
    _signals["Body"] = active
    if not active:
        _clear_path("world/body")
        return

    pts_by_index: list[list[float] | None] = []
    for j in joints:
        parsed = _parse_pose(j.get("p", ""))
        pts_by_index.append(parsed[0] if parsed else None)

    pts = [p for p in pts_by_index if p is not None]
    if pts:
        rr.log("world/body/pts", rr.Points3D(
            np.array(pts, dtype=np.float32), colors=[_BODY_COLOR], radii=[0.025],
        ))
        bones = [
            [pts_by_index[a], pts_by_index[b]]
            for a, b in _BODY_BONES
            if a < len(pts_by_index)
            and b < len(pts_by_index)
            and pts_by_index[a] is not None
            and pts_by_index[b] is not None
        ]
        if bones:
            rr.log("world/body/bones", rr.LineStrips3D(
                bones, colors=[_BODY_COLOR], radii=[0.006],
            ))
        else:
            rr.log("world/body/bones", rr.Clear(recursive=False))
    else:
        _clear_path("world/body")


def _body_is_active(body: object) -> bool:
    if not isinstance(body, dict):
        return False
    joints = body.get("joints", [])
    if not isinstance(joints, list) or not joints:
        return False
    try:
        count = int(body.get("len", len(joints)))
    except (TypeError, ValueError):
        count = len(joints)
    return count > 0


def _log_motion(motion: dict) -> None:
    joints = motion.get("joints", [])
    count = motion.get("len", len(joints))
    _signals["Motion"] = count > 0
    if not joints:
        _clear_path("world/motion")
        return
    pts = []
    for j in joints:
        parsed = _parse_pose(j.get("p", ""))
        if parsed:
            pts.append(parsed[0])
    if pts:
        rr.log("world/motion/pts", rr.Points3D(
            np.array(pts, dtype=np.float32), colors=[_MOTION_COLOR], radii=[0.035],
        ))
    else:
        _clear_path("world/motion")


# ── Status bar (TextDocument, not draggable) ──────────────

_SIGNAL_NAMES = ["Head", "Ctrl-L", "Ctrl-R", "Hand-L", "Hand-R", "Body", "Motion"]


def _log_status() -> None:
    fps = _compute_fps()

    conn = "[x] CONNECTED" if _connected else "[ ] DISCONNECTED"

    badges = []
    for name in _SIGNAL_NAMES:
        active = _signals.get(name, False)
        badges.append(f"[x] {name}" if active else f"[ ] {name}")

    text = f"## {conn}    {'    '.join(badges)}    `{_frame_count}f  {fps:.0f}fps`"
    rr.log("status", rr.TextDocument(text, media_type=rr.MediaType.MARKDOWN))
