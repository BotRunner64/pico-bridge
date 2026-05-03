# Unity Development

## Overview

The Unity side collects PICO tracking data, maintains the in-headset UI, connects to the PC receiver, and displays PC-side WebRTC video.

## Baseline

- Unity: `2022.3.62f3`
- Render pipeline: Built-in 3D
- XR SDK: `Packages/PICO-Unity-Integration-SDK`
- Main scene: `Assets/Scenes/SampleScene.unity`
- Android package: `com.picobridge.app`

PICO 4 and PICO 4 Ultra use the same APK. The project does not split packages by device. Before use, disable the safety boundary in the PICO developer menu and enable `Settings > Interaction > Automatic switching between gestures and controllers`.

Full-body motion capture requires Motion Tracker setup and calibration in the PICO system. If Motion Tracker is not configured or calibrated, `BODY` / `MOTION` being inactive is expected.

Tip: To use Unity `Build & Run` directly to the headset, connect the PC and PICO with a USB cable that supports data transfer. A charge-only cable may not be recognized by Unity as a deployable device.

## First Open

1. In Unity Hub, choose `Add` / `Add project from disk`, then select the repository root.
2. Open the project with Unity `2022.3.62f3`.
3. Manually open `Assets/Scenes/SampleScene.unity`.

## Structure

Runtime bridge code, UI prefabs, Android native plugin assets, and the PC receiver are organized as follows:

```text
Assets/Scripts/PicoBridge/
├── PicoBridgeManager.cs      bridge entrypoint
├── Network/                  TCP/UDP protocol and discovery
├── Tracking/                 headset, controller, hand, and body tracking
├── Camera/                   WebRTC video receiver
├── UI/                       in-headset UI
└── Editor/                   scene setup, validation, and build tools

Assets/Prefabs/PicoBridge/    in-headset UI prefab
Assets/Plugins/Android/       Android native plugin assets
pc_receiver/                  PC-side Python receiver
```

## Editor Menu

| Menu | Purpose |
| --- | --- |
| `PicoBridge > Setup Scene` | Completes bridge objects and UI prefab instances. |
| `PicoBridge > Rebuild Panel Prefab` | Rebuilds the UI prefab from the template; this overwrites manual UI changes. |
| `PicoBridge > Validate Project Settings` | Checks Android/PICO build settings. |

## Development Rules

- Keep the Built-in 3D mainline. Do not restore URP / Live Preview dependencies.
- Runtime code must not create, delete, rebuild, or automatically migrate UI hierarchy.
- Maintain UI hierarchy through editor tools or manual prefab/scene edits.
- Preserve `.meta` files when adding, moving, or deleting Unity assets.
- Avoid unrelated Unity YAML churn before saving scenes.

## Validation

- Python receiver: `cd pc_receiver && pytest tests -q`
- Unity: open the project with `2022.3.62f3` and confirm the Console has no compile errors.
- Device: install the APK separately and verify passthrough, tracking, PC connection, and video return.

## Language

- [中文](../zh/unity-development.md)
- [Documentation home](README.md)
