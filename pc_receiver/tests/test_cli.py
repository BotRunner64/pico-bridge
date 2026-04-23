from __future__ import annotations

import asyncio
import sys
import types

from pico_bridge import cli


def test_visualiser_enabled_when_connecting_existing_viewer():
    args = cli.build_parser().parse_args(["--viz-connect"])

    assert cli._visualiser_enabled(args) is True


def test_run_starts_visualiser_for_viz_connect(monkeypatch):
    calls: list[tuple[object, ...]] = []

    fake_visualiser = types.SimpleNamespace(
        init=lambda *, spawn, connect: calls.append(("init", spawn, connect)),
        push_frame=lambda data: calls.append(("push", data)),
        set_connection_state=lambda connected, device_sn: calls.append(
            ("state", connected, device_sn)
        ),
        close=lambda: calls.append(("close",)),
    )

    class FakeServer:
        def __init__(self, **kwargs):
            self.kwargs = kwargs
            self.connected = False
            self.device_sn = ""

        async def start(self) -> None:
            calls.append(("server_start", self.kwargs["on_tracking"] is not None))

        async def stop(self) -> None:
            calls.append(("server_stop",))

    class FakeBroadcaster:
        def __init__(self, **kwargs):
            self.kwargs = kwargs

        async def start(self) -> None:
            calls.append(("broadcaster_start",))

        async def stop(self) -> None:
            calls.append(("broadcaster_stop",))

    async def fake_sleep(_: float) -> None:
        raise asyncio.CancelledError

    monkeypatch.delitem(sys.modules, "pico_bridge.visualiser", raising=False)
    monkeypatch.setitem(sys.modules, "pico_bridge.visualiser", fake_visualiser)
    monkeypatch.setattr(sys.modules["pico_bridge"], "visualiser", fake_visualiser, raising=False)
    monkeypatch.setattr(cli, "PicoBridgeServer", FakeServer)
    monkeypatch.setattr(cli, "UdpBroadcaster", FakeBroadcaster)
    monkeypatch.setattr(cli.asyncio, "sleep", fake_sleep)

    args = cli.build_parser().parse_args(
        ["--viz-connect", "--no-discovery", "--no-print-tracking"]
    )

    asyncio.run(cli._run(args))

    assert ("init", False, True) in calls
    assert ("server_start", True) in calls
    assert ("close",) in calls
