# CLAUDE.md — Ultimate (fresh start)

Guidance for Claude Code in this repository.

## Status: blank slate

This project was reset to a bare Unity base on 2026-06-04 to redesign from
scratch. The previous prototype (an ultimate frisbee game — camera, throwing,
movement, AI) is preserved in git history at commit **b540039** and tag-able from
there; check out that commit to reference the old systems.

We are in the **design / system-layout phase** — no gameplay code exists yet.
Don't scaffold features until the design is agreed.

## What's in the project now

- **Unity 6 (6000.4.x), URP.** Render pipeline assets live in `Assets/Settings/`.
- **New Input System** package is installed (no project-wide actions asset yet).
- `Assets/Scenes/Main.unity` — an empty base scene (Main Camera + Directional Light).
- `Assets/TextMesh Pro/` — package resources.
- Packages of note (in `Packages/manifest.json`): URP, Input System, Cinemachine,
  and the Unity MCP editor bridge (`com.coplaydev.unity-mcp`).

## Working with Unity + git

- **Commit:** `Assets/` (with every `.meta`), `Packages/`, `ProjectSettings/`.
  Never delete a `.meta` without its asset, or vice versa.
- **Never commit:** `Library/`, `Temp/`, `Obj/`, `Logs/`, `UserSettings/`, build
  output, generated IDE files. These are gitignored.
- Let Unity write serialized YAML (`.unity`/`.prefab`/`.asset`) where possible;
  hand-edit only when necessary and keep the format intact.

## Conventions

- Plain C# under `Assets/Scripts/` compiles into `Assembly-CSharp`; editor code
  under `Assets/Editor/` into `Assembly-CSharp-Editor`.

> Fill in architecture, systems, and a work tracker once the new design is set.
