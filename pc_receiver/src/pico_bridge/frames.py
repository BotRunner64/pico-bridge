"""Public PICO tracking frame types.

The public API keeps PICO/Unity data semantics intact: positions are meters,
quaternions are xyzw, and coordinates are in the Unity/PICO space sent by the
headset. Consumer projects are responsible for converting these frames into
their own coordinate systems and data models.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

import numpy as np
from numpy.typing import NDArray

ArrayF64 = NDArray[np.float64]
ArrayU64 = NDArray[np.uint64]

BODY_JOINT_NAMES: tuple[str, ...] = (
    "Pelvis",
    "Left_Hip",
    "Right_Hip",
    "Spine1",
    "Left_Knee",
    "Right_Knee",
    "Spine2",
    "Left_Ankle",
    "Right_Ankle",
    "Spine3",
    "Left_Foot",
    "Right_Foot",
    "Neck",
    "Left_Collar",
    "Right_Collar",
    "Head",
    "Left_Shoulder",
    "Right_Shoulder",
    "Left_Elbow",
    "Right_Elbow",
    "Left_Wrist",
    "Right_Wrist",
    "Left_Hand",
    "Right_Hand",
)

BODY_JOINT_PARENTS: NDArray[np.int32] = np.array(
    [
        -1,
        0,
        0,
        0,
        1,
        2,
        3,
        4,
        5,
        6,
        7,
        8,
        9,
        12,
        12,
        12,
        13,
        14,
        16,
        17,
        18,
        19,
        20,
        21,
    ],
    dtype=np.int32,
)

HAND_JOINT_NAMES: tuple[str, ...] = (
    "Palm",
    "Wrist",
    "ThumbMetacarpal",
    "ThumbProximal",
    "ThumbDistal",
    "ThumbTip",
    "IndexMetacarpal",
    "IndexProximal",
    "IndexIntermediate",
    "IndexDistal",
    "IndexTip",
    "MiddleMetacarpal",
    "MiddleProximal",
    "MiddleIntermediate",
    "MiddleDistal",
    "MiddleTip",
    "RingMetacarpal",
    "RingProximal",
    "RingIntermediate",
    "RingDistal",
    "RingTip",
    "LittleMetacarpal",
    "LittleProximal",
    "LittleIntermediate",
    "LittleDistal",
    "LittleTip",
)


@dataclass(frozen=True)
class Pose:
    """A PICO/Unity pose in meters and xyzw quaternion order."""

    position: ArrayF64
    rotation: ArrayF64

    @property
    def array(self) -> ArrayF64:
        return np.concatenate((self.position, self.rotation)).astype(np.float64, copy=False)


@dataclass(frozen=True)
class BodyFrame:
    active: bool
    joints: ArrayF64
    joint_names: tuple[str, ...] = BODY_JOINT_NAMES
    joint_parents: NDArray[np.int32] = field(default_factory=lambda: BODY_JOINT_PARENTS.copy())


@dataclass(frozen=True)
class HandFrame:
    active: bool
    joints: ArrayF64
    radii: ArrayF64
    status: ArrayU64
    joint_names: tuple[str, ...] = HAND_JOINT_NAMES
    scale: float = 1.0


@dataclass(frozen=True)
class ControllerState:
    pose: Pose | None
    axis: dict[str, float]
    buttons: dict[str, bool]
    raw: dict[str, Any]


@dataclass(frozen=True)
class ControllersFrame:
    left: ControllerState
    right: ControllerState


@dataclass(frozen=True)
class PicoFrame:
    seq: int
    timestamp_ns: int
    receive_time_s: float
    coordinate_space: str
    quat_order: str
    units: str
    head: Pose | None
    body: BodyFrame
    left_hand: HandFrame
    right_hand: HandFrame
    controllers: ControllersFrame
    raw: dict[str, Any]

    @classmethod
    def from_tracking_payload(
        cls,
        payload: dict[str, Any],
        *,
        seq: int,
        receive_time_s: float,
    ) -> "PicoFrame":
        return cls(
            seq=seq,
            timestamp_ns=_int_or_default(payload.get("timeStampNs"), 0),
            receive_time_s=float(receive_time_s),
            coordinate_space="pico_unity",
            quat_order="xyzw",
            units="meters",
            head=_parse_head(payload.get("Head", {})),
            body=_parse_body(payload.get("Body", {})),
            left_hand=_parse_hand(_dict_or_empty(payload.get("Hand", {})).get("leftHand", {})),
            right_hand=_parse_hand(_dict_or_empty(payload.get("Hand", {})).get("rightHand", {})),
            controllers=_parse_controllers(payload.get("Controller", {})),
            raw=payload,
        )

    def summary(self) -> str:
        parts: list[str] = [f"seq={self.seq}"]
        if self.head is not None:
            parts.append(f"head={_format_pose(self.head)}")
        if self.controllers.left.pose is not None:
            parts.append(f"ctrl_left={_format_pose(self.controllers.left.pose)}")
        if self.controllers.right.pose is not None:
            parts.append(f"ctrl_right={_format_pose(self.controllers.right.pose)}")
        if self.left_hand.active:
            parts.append(f"leftHand=active({_hand_joint_count(self.raw, 'leftHand', self.left_hand)}j)")
        if self.right_hand.active:
            parts.append(f"rightHand=active({_hand_joint_count(self.raw, 'rightHand', self.right_hand)}j)")
        if self.body.active:
            parts.append(f"body={_dict_int(self.raw.get('Body'), 'len', _body_joint_count(self.body))}j")
        motion_count = _dict_int(self.raw.get("Motion"), "len", 0)
        if motion_count > 0:
            parts.append(f"motion={motion_count}j")
        return " | ".join(parts)


def _parse_head(value: Any) -> Pose | None:
    if not isinstance(value, dict):
        return None
    return _parse_pose(value.get("pose"))


def _parse_body(value: Any) -> BodyFrame:
    joints = np.zeros((len(BODY_JOINT_NAMES), 7), dtype=np.float64)
    if not isinstance(value, dict):
        return BodyFrame(active=False, joints=joints)

    raw_joints = value.get("joints", [])
    if not isinstance(raw_joints, list):
        raw_joints = []

    active_count = 0
    for index, item in enumerate(raw_joints[: len(BODY_JOINT_NAMES)]):
        if not isinstance(item, dict):
            continue
        pose = _parse_pose_array(item.get("p"))
        if pose is None:
            continue
        joints[index] = pose
        active_count += 1

    declared_count = _int_or_default(value.get("len"), active_count)
    return BodyFrame(active=active_count > 0 and declared_count > 0, joints=joints)


def _parse_hand(value: Any) -> HandFrame:
    joints = np.zeros((len(HAND_JOINT_NAMES), 7), dtype=np.float64)
    radii = np.zeros((len(HAND_JOINT_NAMES),), dtype=np.float64)
    status = np.zeros((len(HAND_JOINT_NAMES),), dtype=np.uint64)
    if not isinstance(value, dict):
        return HandFrame(active=False, joints=joints, radii=radii, status=status)

    raw_joints = value.get("HandJointLocations", [])
    if not isinstance(raw_joints, list):
        raw_joints = []

    parsed_count = 0
    for index, item in enumerate(raw_joints[: len(HAND_JOINT_NAMES)]):
        if not isinstance(item, dict):
            continue
        pose = _parse_pose_array(item.get("p"))
        if pose is None:
            continue
        joints[index] = pose
        radii[index] = _float_or_default(item.get("r"), 0.0)
        status[index] = max(_int_or_default(item.get("s"), 0), 0)
        parsed_count += 1

    declared_active = _bool_or_default(value.get("isActive"), False)
    declared_count = _int_or_default(value.get("count"), parsed_count)
    return HandFrame(
        active=declared_active and parsed_count > 0 and declared_count > 0,
        joints=joints,
        radii=radii,
        status=status,
        scale=_float_or_default(value.get("scale"), 1.0),
    )


def _parse_controllers(value: Any) -> ControllersFrame:
    controller = value if isinstance(value, dict) else {}
    return ControllersFrame(
        left=_parse_controller(controller.get("left", {})),
        right=_parse_controller(controller.get("right", {})),
    )


def _parse_controller(value: Any) -> ControllerState:
    raw = value if isinstance(value, dict) else {}
    axis = {
        "x": _float_or_default(raw.get("axisX"), 0.0),
        "y": _float_or_default(raw.get("axisY"), 0.0),
        "grip": _float_or_default(raw.get("grip"), 0.0),
        "trigger": _float_or_default(raw.get("trigger"), 0.0),
    }
    buttons = {
        "axisClick": _bool_or_default(raw.get("axisClick"), False),
        "primaryButton": _bool_or_default(raw.get("primaryButton"), False),
        "secondaryButton": _bool_or_default(raw.get("secondaryButton"), False),
        "menuButton": _bool_or_default(raw.get("menuButton"), False),
    }
    return ControllerState(
        pose=_parse_pose(raw.get("pose")),
        axis=axis,
        buttons=buttons,
        raw=dict(raw),
    )


def _parse_pose(value: Any) -> Pose | None:
    pose = _parse_pose_array(value)
    if pose is None:
        return None
    return Pose(position=pose[:3].copy(), rotation=pose[3:].copy())


def _parse_pose_array(value: Any) -> ArrayF64 | None:
    if isinstance(value, str):
        parts = value.split(",")
    elif isinstance(value, (list, tuple)):
        parts = value
    else:
        return None
    if len(parts) != 7:
        return None
    try:
        pose = np.asarray([float(part) for part in parts], dtype=np.float64)
    except (TypeError, ValueError):
        return None
    if not np.all(np.isfinite(pose)):
        return None
    return pose


def _float_or_default(value: Any, default: float) -> float:
    try:
        parsed = float(value)
    except (TypeError, ValueError):
        return default
    return parsed if np.isfinite(parsed) else default


def _int_or_default(value: Any, default: int) -> int:
    try:
        if isinstance(value, float) and not np.isfinite(value):
            return default
        return int(value)
    except (TypeError, ValueError, OverflowError):
        return default


def _format_pose(pose: Pose) -> str:
    return ",".join(f"{float(value):.6g}" for value in pose.array)


def _dict_int(value: Any, key: str, default: int) -> int:
    if not isinstance(value, dict):
        return default
    return _int_or_default(value.get(key), default)


def _hand_joint_count(raw: dict[str, Any], key: str, hand: HandFrame) -> int:
    raw_hand = _dict_or_empty(_dict_or_empty(raw.get("Hand")).get(key))
    return _dict_int(raw_hand, "count", int(np.count_nonzero(np.any(hand.joints != 0, axis=1))))


def _body_joint_count(body: BodyFrame) -> int:
    return int(np.count_nonzero(np.any(body.joints != 0, axis=1)))


def _bool_or_default(value: Any, default: bool) -> bool:
    if value is None:
        return default
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float)):
        return bool(value)
    if isinstance(value, str):
        normalized = value.strip().lower()
        if normalized in ("true", "1", "yes", "on"):
            return True
        if normalized in ("false", "0", "no", "off"):
            return False
    return default


def _dict_or_empty(value: Any) -> dict[str, Any]:
    return value if isinstance(value, dict) else {}
