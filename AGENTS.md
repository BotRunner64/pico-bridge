# AGENTS.md

## Project Overview

- This repository is a Unity project for PICO/VR development.
- Unity editor version is pinned by `ProjectSettings/ProjectVersion.txt`.
- `Packages/PICO-Unity-Integration-SDK` is a git submodule and should be used as an embedded Unity Package Manager package, not copied into `Assets/`.

## PICO Documentation

- Use the official PICO Unity documentation as the primary reference for SDK setup, APIs, and platform-specific behavior:
  - `https://developer.picoxr.com/zh/document/unity/`
- When implementing PICO SDK features or looking up related interfaces, check the official PICO Unity documentation first.
- Prefer documented PICO APIs and Unity Package Manager integration patterns over inferred or copied examples.

## Local Plugins

- This repository also contains a local Unity package plugin at `Packages/Unity-Live-Preview-Plugin/`.
- The package name is `com.unity.pico.livepreview`; prefer keeping it as an embedded Unity Package Manager package under `Packages/` instead of copying files into `Assets/`.
- For Live Preview related APIs and integration points, inspect this plugin first:
  - `Packages/Unity-Live-Preview-Plugin/package.json`
  - `Packages/Unity-Live-Preview-Plugin/Runtime/Scripts/`
  - `Packages/Unity-Live-Preview-Plugin/Runtime/UnitySubsystemsManifest.json`
- The main runtime namespace exposed by this plugin is `Unity.XR.PICO.LivePreview`.
- When looking for Live Preview loaders, settings, or subsystem entry points, start from:
  - `Packages/Unity-Live-Preview-Plugin/Runtime/Scripts/PXR_PTLoader.cs`
  - `Packages/Unity-Live-Preview-Plugin/Runtime/Scripts/PXR_PTSettings.cs`

## Submodules

- Initialize SDK submodules before working on PICO integration:
  - `git submodule sync --recursive`
  - `git submodule update --init --recursive`
- The PICO SDK submodule lives at `Packages/PICO-Unity-Integration-SDK`.
- Do not vendor or duplicate the PICO SDK files under `Assets/`.

## Unity Package Guidance

- Prefer editing `Packages/manifest.json` for package dependencies.
- Embedded packages stored directly under `Packages/` are preferred for local plugins in this repository.
- `Packages/PICO-Unity-Integration-SDK` exposes the package `com.unity.xr.picoxr`.
- `Packages/Unity-Live-Preview-Plugin` exposes the package `com.unity.pico.livepreview`.
- Keep `Packages/packages-lock.json` in sync with Unity Package Manager changes.

## Generated Files

- Do not commit Unity generated folders such as `Library/`, `Temp/`, `Obj/`, `Logs/`, `Build/`, `Builds/`, or `UserSettings/`.
- Do not treat generated project files such as `*.csproj`, `*.sln`, or IDE metadata as source.

## Coding Conventions

- Keep runtime scripts under `Assets/` and editor-only scripts under an `Editor/` folder.
- Prefer small, focused MonoBehaviours and ScriptableObjects.
- Avoid adding new packages unless they are required for the requested feature.
- Preserve Unity `.meta` files when adding, moving, or deleting assets.

## Verification

- For code changes, open the project with the pinned Unity version and check the Console for compile errors.
- For package or XR changes, verify Unity Package Manager resolves successfully.
- For PICO features, validate Android XR settings and test on device when possible.
