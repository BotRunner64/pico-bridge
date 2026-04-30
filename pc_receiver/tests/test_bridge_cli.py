from pico_bridge.cli import build_parser


def test_print_tracking_defaults_to_false():
    assert build_parser().parse_args([]).print_tracking is False


def test_print_tracking_can_be_enabled():
    assert build_parser().parse_args(["--print-tracking"]).print_tracking is True


def test_print_tracking_can_be_disabled():
    assert build_parser().parse_args(["--no-print-tracking"]).print_tracking is False


def test_status_interval_defaults_to_five_seconds():
    assert build_parser().parse_args([]).status_interval == 5.0


def test_status_interval_can_be_disabled():
    assert build_parser().parse_args(["--status-interval", "0"]).status_interval == 0.0


def test_advertise_ip_flag_is_parsed():
    args = build_parser().parse_args(["--advertise-ip", "192.168.1.10"])
    assert args.advertise_ip == "192.168.1.10"


def test_realsense_video_source_is_parsed():
    args = build_parser().parse_args(["--video", "realsense", "--camera-device", "RS123"])
    assert args.video == "realsense"
    assert args.camera_device == "RS123"


def test_visualiser_follow_can_be_disabled():
    args = build_parser().parse_args(["--viz", "--viz-no-follow"])

    assert args.viz is True
    assert args.viz_no_follow is True
