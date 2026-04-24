# AGENTS.md

## Project Overview

- This repository is the active Built-in 3D mainline for the PICO bridge project.
- Unity editor version is pinned by `ProjectSettings/ProjectVersion.txt`.
- The primary product goal is to keep PICO passthrough working while continuing UI, networking, tracking, and `pc_receiver` development.
- A legacy URP project exists outside this repo as reference only; do not treat it as the main development target.

## Packages and SDKs

- `Packages/PICO-Unity-Integration-SDK` is the embedded PICO SDK package and should remain under `Packages/`.
- Prefer documented PICO SDK APIs over copied or inferred behavior.
- Do not reintroduce `Unity-Live-Preview-Plugin` or URP-only package/config dependencies unless explicitly requested.

## Project Structure

- Unity runtime bridge code lives under `Assets/Scripts/PicoBridge/`.
- Android native plugin assets live under `Assets/Plugins/Android/`.
- The Python receiver lives under `pc_receiver/`.

## Coding Conventions

- Keep runtime scripts under `Assets/` and editor-only scripts inside an `Editor/` folder.
- Preserve Unity `.meta` files when adding, moving, or deleting assets.
- Prefer small, focused MonoBehaviours and utility classes.
- Keep diffs small and avoid unrelated scene churn.

## Validation

- For Python receiver changes, run `pytest tests -q` from `pc_receiver/`.
- For Unity changes, open the project with the pinned editor and check the Console for compile/import errors.
- For XR changes, verify Android settings, package resolution, and passthrough behavior on device when possible.

## Generated Files

- Do not commit `Library/`, `Temp/`, `Obj/`, `Logs/`, `Build/`, `Builds/`, or `UserSettings/`.
- Do not treat generated `*.csproj` / `*.sln` files as source.
