from bridge import build_parser


def test_print_tracking_defaults_to_true():
    assert build_parser().parse_args([]).print_tracking is True


def test_print_tracking_can_be_disabled():
    assert build_parser().parse_args(["--no-print-tracking"]).print_tracking is False


def test_advertise_ip_flag_is_parsed():
    args = build_parser().parse_args(["--advertise-ip", "192.168.1.10"])
    assert args.advertise_ip == "192.168.1.10"
