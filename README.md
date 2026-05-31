# Ultimate

A playable **ultimate frisbee** game built in **Unity 6 (URP)**. 5-on-5, with
mouse flick/drag throwing on a physics-based disc, simple cutting/marking AI,
possession, turnovers, and end-zone scoring.

https://github.com — public repo. Made with Claude Code.

## Requirements

- **Unity 6 LTS** (6000.4.x) with the **Universal Render Pipeline**
- The project uses the **new Input System** (already configured)

## Getting started

1. Clone the repo and open the folder in **Unity Hub → Add → project from disk**.
2. Let Unity import (it regenerates `Library/` on first open — this takes a bit).
3. Run the menu command **Frisbee → Build Assets & Scene**. This generates the
   materials, prefabs, and the playable scene under `Assets/Frisbee/`.
4. Open `Assets/Frisbee/Scenes/Frisbee.unity` and press **Play**.

> The scene/prefabs/materials are produced by an editor script
> (`Assets/Editor/FrisbeeBuilder.cs`) so the repo stays small and the setup is
> reproducible. Re-running the command rebuilds everything.

## Controls

| Action | Input |
|--------|-------|
| Move (without the disc) | **WASD / Arrow keys** (camera-relative) |
| Throw | **Hold left mouse**, **drag** toward your target, **release** |
| Power | how **far** you drag (a dotted line previews the flight) |
| Direction | the **direction** you drag (up = downfield) |

You control the **highlighted** player — the disc-holder when your team has it
(you're planted, like real ultimate: pass to advance), otherwise whoever's
nearest the disc. Catch a pass in the far end zone to score.

## Rules implemented

Completion → keep possession · interception / drop / out-of-bounds / 8-second
stall → turnover · catch in the attacking end zone → goal, then the conceded-to
team restarts.

## Project layout

```
Assets/
  Scripts/    gameplay (Field, Disc, Player, MatchManager, AIController,
              HumanController, CameraRig, Hud)
  Editor/     FrisbeeBuilder — generates assets + scene
  Frisbee/    generated Materials / Prefabs / Scenes
  Settings/   URP render-pipeline assets
```

See `CLAUDE.md` for an architecture overview and the Unity-on-git conventions.

## Roadmap

Rigged character models + animation · forehand/backhand/huck throw types ·
smarter cutting & defensive marking · game-to-15 flow, menu, sound · multiplayer.

## License

MIT — see `LICENSE`.
