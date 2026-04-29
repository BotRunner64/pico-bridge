from __future__ import annotations

import numpy as np

from pico_bridge.frames import BODY_JOINT_NAMES, HAND_JOINT_NAMES, PicoFrame


def test_pico_frame_parses_tracking_payload_shapes_and_metadata():
    payload = {
        "Head": {"pose": "1,2,3,0,0,0,1"},
        "Controller": {
            "left": {
                "pose": "0,0,0,0,0,0,1",
                "axisX": 0.25,
                "axisY": -0.5,
                "trigger": 0.75,
                "primaryButton": True,
            },
            "right": {},
        },
        "Body": {
            "len": 2,
            "joints": [
                {"p": "0,1,2,0,0,0,1"},
                {"p": "3,4,5,0.1,0.2,0.3,0.9"},
            ],
        },
        "Hand": {
            "leftHand": {
                "isActive": True,
                "count": 2,
                "scale": 1.2,
                "HandJointLocations": [
                    {"p": "1,0,0,0,0,0,1", "r": 0.01, "s": 7},
                    {"p": "2,0,0,0,0,0,1", "r": 0.02, "s": 5},
                ],
            },
            "rightHand": {"isActive": False, "count": 0, "HandJointLocations": []},
        },
        "Motion": {"len": 3},
        "timeStampNs": 123,
    }

    frame = PicoFrame.from_tracking_payload(payload, seq=9, receive_time_s=10.0)

    assert frame.seq == 9
    assert frame.timestamp_ns == 123
    assert frame.coordinate_space == "pico_unity"
    assert frame.quat_order == "xyzw"
    assert frame.units == "meters"
    np.testing.assert_allclose(frame.head.position, [1, 2, 3])
    assert frame.body.active is True
    assert frame.body.joints.shape == (len(BODY_JOINT_NAMES), 7)
    np.testing.assert_allclose(frame.body.joints[1], [3, 4, 5, 0.1, 0.2, 0.3, 0.9])
    assert frame.left_hand.active is True
    assert frame.left_hand.joints.shape == (len(HAND_JOINT_NAMES), 7)
    np.testing.assert_allclose(frame.left_hand.radii[:2], [0.01, 0.02])
    np.testing.assert_array_equal(frame.left_hand.status[:2], [7, 5])
    assert frame.right_hand.active is False
    assert frame.controllers.left.buttons["primaryButton"] is True
    assert frame.controllers.left.axis["trigger"] == 0.75
    assert frame.raw is payload
    summary = frame.summary()
    assert "head=1,2,3,0,0,0,1" in summary
    assert "ctrl_left=0,0,0,0,0,0,1" in summary
    assert "leftHand=active(2j)" in summary
    assert "body=2j" in summary
    assert "motion=3j" in summary


def test_pico_frame_uses_fixed_empty_shapes_for_missing_data():
    frame = PicoFrame.from_tracking_payload({}, seq=1, receive_time_s=2.0)

    assert frame.head is None
    assert frame.body.active is False
    assert frame.body.joints.shape == (24, 7)
    assert frame.left_hand.active is False
    assert frame.left_hand.joints.shape == (26, 7)
    assert frame.right_hand.radii.shape == (26,)


def test_pico_frame_defaults_non_finite_integer_fields():
    frame = PicoFrame.from_tracking_payload({"timeStampNs": float("inf")}, seq=1, receive_time_s=2.0)

    assert frame.timestamp_ns == 0
