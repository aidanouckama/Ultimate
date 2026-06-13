# Ultimate

A **Unity 6 (URP)** project, reset to a bare base on 2026-06-04 to redesign from
scratch — currently a blank slate in the design phase.

> The previous prototype (an ultimate frisbee game) is preserved in git history
> at commit `b540039`. Check out that commit to browse the old camera, throwing,
> movement, and AI systems.

## Requirements

- **Unity 6 LTS** (6000.4.x) with the **Universal Render Pipeline**
- The **new Input System** package is installed

## Getting started

1. Clone the repo and open the folder in **Unity Hub → Add → project from disk**.
2. Let Unity import (it regenerates `Library/` on first open).
3. Open `Assets/Scenes/Main.unity` (an empty base scene) and press **Play**.

## Project layout

```
Assets/
  Scenes/        Main.unity (empty base scene)
  Settings/      URP render-pipeline assets
  TextMesh Pro/  package resources
```

See `CLAUDE.md` for the Unity-on-git conventions. Game design and systems are TBD.

## License

MIT — see `LICENSE`.
