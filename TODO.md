# TODO — Ultimate

Working tracker for the frisbee game. Bugs to fix next, then the longer roadmap.

## ✅ Fixed (2026-05-31)

### 1. Disc shrinks every time it's thrown — FIXED
The disc is no longer parented to the hand. `Disc.AttachTo` just records the holder and
`Disc.LateUpdate` snaps the disc onto `holder.HandPoint` each frame (`SnapToHand`). With
no parenting there's no scale inheritance, so the disc keeps its prefab scale forever.
`Throw` / `Drop` dropped their `SetParent(null, true)` calls.

### 2. Out-of-bounds / ground turnover + placement — FIXED
The disc is now judged only when it **lands** (`Disc.FixedUpdate` → `OnDiscLanded`); the
old mid-air OOB fault is gone, so a disc that curves out over the sideline and back in
stays live. `MatchManager.OnDiscLanded` branches: landed in bounds → turnover where it
lies; landed out → turnover at `Field.ClampInBounds(pos)` (the sideline point across from
where it fell). Replaced the old `OnDiscGrounded` / `OnDiscOutOfBounds` pair.

### 3. Curve the disc (human throws) — FIXED
`HumanController` now charges a signed `curveSpin` while you hold **A / D** (or ← / →)
during the aim (clamped to `maxCurveSpin`, ramped at `curveChargeRate`), passes it into
`Disc.Throw(vel, team, curveSpin)`, and feeds the same spin (with matching in-flight
decay) into `DrawPreview`, so the dotted line curls exactly like the throw will.

**OOB placement rule** (`Field.BringInBounds`): out the **side** → nearest sideline at the
same depth (perpendicular); out the **back**, past an end zone → that end zone's **goal
line** (`GoalLineZ`); in bounds → where it lies. Also: the thrower can no longer catch
their own throw (`Disc.thrower` is skipped in `TryCatch`).

> Follow-ups if you want them: tune `maxCurveSpin` / `curveChargeRate` for feel; add an
> on-HUD curve indicator.

## 🗺️ Roadmap / feature ideas

- **Throw types** — distinct forehand / backhand / huck (flat vs floaty), maybe via a
  modifier key; ties into the curve work above.
- **Smarter AI** — real cutting patterns (in/out cuts), defensive force/marking, zone D.
- **Models & animation** — replace the capsules/cylinder with rigged characters +
  run / throw / catch animations.
- **Game flow** — game-to-15, halftime, timeouts, pull/kickoff, start menu, sound.
- **Multiplayer** — local 2-player or networked.
