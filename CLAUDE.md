# CLAUDE.md — Ultimate (Unity frisbee game)

Guidance for Claude Code when working in this repository.

## What this is

A playable **ultimate frisbee** game built in **Unity 6 (6000.4.x), URP**.
5-on-5, mouse flick/drag throwing with a physics-based disc, simple cutting /
marking AI, possession, turnovers, and end-zone scoring.

> Note: the on-disk project folder is historically named
> "Setup Guide In-Editor Tutorial" (it began from a Unity template). The GitHub
> repo is **Ultimate**. The folder name does not matter.

## Where the game lives

All gameplay code and assets are under `Assets/`:

| Path | What |
|------|------|
| `Assets/Scripts/` | All runtime gameplay scripts (see table below) |
| `Assets/Editor/FrisbeeBuilder.cs` | Editor tool: **Frisbee → Build Assets & Scene** |
| `Assets/Frisbee/Materials/` | Generated material assets |
| `Assets/Frisbee/Prefabs/` | `Field`, `Disc`, `PlayerHome`, `PlayerAway` |
| `Assets/Frisbee/Scenes/Frisbee.unity` | The playable scene |

### Runtime scripts
| Script | Role |
|--------|------|
| `Field.cs` | Field geometry + in-bounds / end-zone rule helpers. `Team` enum lives here. |
| `Disc.cs` | Disc states (Held/Flying/Loose) + flight model (gravity/drag/lift/curl) + catch detection. |
| `Player.cs` | One player: movement, facing, catch radius, highlight, hand point. |
| `MatchManager.cs` | Singleton (`MatchManager.I`). Possession, score, turnovers, scoring; self-gathers players and starts the point in `Start()`. |
| `AIController.cs` | Per-player AI: throw / cut / mark. |
| `HumanController.cs` | Flick-drag throw + aim preview + running. **Uses the new Input System.** |
| `CameraRig.cs` | Follow camera. |
| `Hud.cs` | IMGUI scoreboard + controls overlay. |

## Architecture notes (read before editing)

- **Self-wiring, no bootstrap.** `MatchManager.Start()` finds the `Field` and
  `Disc` and gathers every `Player` in the scene, then calls `SetupPoint()`.
  Components do not depend on a central spawner. There is no `GameBootstrap`
  anymore — it was removed in favor of the authored scene.
- **`MatchManager.I`** is the singleton entry point everything reads.
- **Players have no Rigidbody/Collider.** Movement is transform-based; catching
  is radius-based (`Player.catchRadius`, tested in `Disc.TryCatch`). This is
  deliberate — it keeps players from knocking each other or the disc around.
- **The disc is fully script-driven.** No collider; `Disc.Aero()` integrates
  flight, and the same method powers the aim-preview line so prediction matches
  reality. Ground/out-of-bounds are detected by position, not physics.
- **Input System (new).** `HumanController` reads `Keyboard.current` /
  `Mouse.current`. Keep Active Input Handling on "Input System Package (New)".
- **Regenerating assets.** Materials/prefabs/scene are produced by
  `FrisbeeBuilder`. Re-running the menu command overwrites them, so prefer
  editing the builder (or the assets directly) over hand-editing `.unity`/
  `.prefab`/`.mat` YAML.

## Working with Unity + git (important)

- **Commit:** `Assets/` (including every `.meta` file), `Packages/`,
  `ProjectSettings/`. A missing `.meta` corrupts asset references — never delete
  a `.meta` without its asset, or vice versa.
- **Never commit:** `Library/`, `Temp/`, `Obj/`, `Logs/`, `UserSettings/`,
  build output, or generated IDE files (`.csproj`, `.sln`, `.vs/`). These are in
  `.gitignore`.
- **`.meta` files are not noise** — they carry GUIDs that link assets. Always
  stage an asset and its `.meta` together.
- Unity must be **closed or idle** when checking out branches that change many
  assets, to avoid reimport thrash.
- Do not reformat or hand-edit serialized YAML assets unless necessary; let
  Unity write them.

## Conventions

- Plain C# (no asmdef) → everything compiles into `Assembly-CSharp`; editor code
  under `Assets/Editor/` compiles into `Assembly-CSharp-Editor`.
- Match the existing terse, commented style. New gameplay types generally take a
  `MatchManager.I` reference rather than singletons of their own.

## Possible next steps (not yet built)

Rigged character models + animation; distinct forehand/backhand/huck throws;
smarter cutting patterns and defensive marking/force; game-to-15 flow, menu,
sound; local or networked multiplayer.
