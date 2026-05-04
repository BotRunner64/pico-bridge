# PICO Bridge Documentation

PICO Bridge streams headset, controller, hand, body, and Motion Tracker data from PICO 4 / PICO 4 Ultra to a PC, and can optionally stream PC camera video back to the headset.

## Documentation Index

| Topic | Description |
| --- | --- |
| [PC Receiver API](pc-receiver.md) | Python package installation, downstream dependency usage, `PicoBridge` API, frame fields, and coordinate semantics. |
| [Unity Development](unity-development.md) | Unity version, project structure, editor menu, development rules, and validation steps. |

## Quick Start

1. Connect the PICO headset and PC to the same local network.
2. Disable the safety boundary in the PICO developer menu before use.
3. Enable `Settings > Interaction > Automatic switching between gestures and controllers` on the headset.
4. Download the APK from [GitHub Releases](https://github.com/BotRunner64/pico-bridge/releases), or build and install it with Unity `2022.3.62f3`.
5. Start the PICO Bridge app in the headset.

```bash
pip install https://github.com/BotRunner64/pico-bridge/releases/download/v0.1.0/pico_bridge-0.1.0-py3-none-any.whl
pico-bridge-receiver -v --video camera --viz
```

6. Connect to the PC receiver from the PicoBridge panel in the headset.

Manual APK installation:

```bash
sudo apt update
sudo apt install android-tools-adb
adb devices
adb install -r path/to/pico-bridge.apk
```

Full-body motion capture requires Motion Tracker setup and calibration in the PICO system before use.

## Language

- [中文文档](../zh/README.md)
- [Repository README](../../README.md)
