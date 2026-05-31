# TODO — Ultimate

Working tracker for the frisbee game. Bugs to fix next, then the longer roadmap.

## 🐞 Bugs / fixes (next session)

### 1. Disc shrinks every time it's thrown
**Symptom:** the disc gets visibly smaller with each throw/catch cycle.
**Likely cause:** `Disc.AttachTo` parents the disc to `Player.HandPoint`, a child of
the player capsule scaled `(0.9, 1, 0.9)`. Parenting with `SetParent(parent, false)`
keeps the disc's localScale but inherits the parent's scale, so world scale shrinks
by 0.9 on each attach; `Throw`'s `SetParent(null, true)` then bakes that smaller world
scale into localScale. Repeats every possession → compounding shrink.
**Fix options (`Disc.cs`):**
- Cache the disc's original `localScale` in `Awake` and reapply it after every reparent
  (attach / throw / drop), **or**
- Don't parent at all — when held, follow `holder.HandPoint.position` each frame
  (no scale inheritance). Cleanest.

### 2. Out-of-bounds / ground turnover + placement
**Desired:** when the disc lands (hits the ground) **or** goes out of bounds and is
**not curving back in**, it's a turnover, and the disc is placed:
- where it lies, if it landed in bounds, or
- on the sideline at the point across from where it crossed out (sideline crossing point).

**Current state (`Disc.FixedUpdate` / `MatchManager`):**
- Ground landing → `OnDiscGrounded` → turnover at the landing spot — even if that spot
  is out of bounds (should instead bring it to the nearest sideline point).
- Airborne out-of-bounds → `OnDiscOutOfBounds` → turnover, clamped via
  `Field.ClampInBounds` (≈ nearest sideline point).

**To do:**
- Make OOB reliably register as a turnover (a disc that *lands* out of bounds currently
  goes through the ground path, not the OOB path — verify and unify).
- Place the disc at the **sideline crossing point** (where it first crossed the line),
  not just the raw landing/exit position.
- Account for a **curving** disc that may re-enter — only call it out once it has landed
  or is clearly leaving; don't fault a disc that curves back in-bounds. Depends on #3.

### 3. No way to curve the disc (human throws)
**State:** the flight model already supports curve (`Disc.curl` + the `spin` arg to
`Disc.Throw`), and the AI passes a random spin, but `HumanController` always throws with
`spin = 0`.
**To do (`HumanController.cs`):**
- Add a curve control (e.g. A/D or Q/E held while aiming, or map horizontal mouse travel
  during the drag to spin).
- Pass that spin into `Disc.Throw(vel, team, spin)`.
- Feed the same spin into the aim preview (`DrawPreview` currently calls `Aero(v, 0f)`)
  so the predicted line curves to match the throw.

## 🗺️ Roadmap / feature ideas

- **Throw types** — distinct forehand / backhand / huck (flat vs floaty), maybe via a
  modifier key; ties into the curve work above.
- **Smarter AI** — real cutting patterns (in/out cuts), defensive force/marking, zone D.
- **Models & animation** — replace the capsules/cylinder with rigged characters +
  run / throw / catch animations.
- **Game flow** — game-to-15, halftime, timeouts, pull/kickoff, start menu, sound.
- **Multiplayer** — local 2-player or networked.
