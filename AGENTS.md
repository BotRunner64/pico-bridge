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
- Build and update Unity UI hierarchy only through editor-only scene/template tools or manual scene edits; runtime scripts must not create, delete, rebuild, or auto-migrate UI hierarchy, so designers can adjust the scene UI by hand.
- Preserve Unity `.meta` files when adding, moving, or deleting assets.
- Prefer small, focused MonoBehaviours and utility classes.
- Keep diffs small and avoid unrelated scene churn.

## Documentation

- Keep root `README.md` and package `README.md` files in English.
- Root `README.md` is the project homepage and documentation index. Keep it concise and link to both English and Chinese docs.
- Keep English docs under `docs/en/` and Chinese docs under `docs/zh/`. Do not mix English and Chinese full documentation in the same file.
- English and Chinese documentation must stay fully equivalent: every fact, command, setup step, option, warning, and behavioral guarantee added to one language must be added to the other in the same change.
- When adding, renaming, or deleting a documentation page in one language, make the same structural change in the other language and update the index links.

## Validation

- For Python receiver changes, run `pytest tests -q` from `pc_receiver/`.
- For Unity changes, open the project with the pinned editor and check the Console for compile/import errors.
- For XR changes, verify Android settings, package resolution, and passthrough behavior on device when possible.

## Generated Files

- Do not commit `Library/`, `Temp/`, `Obj/`, `Logs/`, `Build/`, `Builds/`, or `UserSettings/`.
- Do not treat generated `*.csproj` / `*.sln` files as source.
