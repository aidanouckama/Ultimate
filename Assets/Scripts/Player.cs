using UnityEngine;

/// <summary>
/// A single player on the field. Movement is transform-based (no physics body),
/// so players never knock each other or the disc around. Catching is handled by
/// the disc via <see cref="catchRadius"/>.
/// </summary>
public class Player : MonoBehaviour
{
    public Team team;
    public float moveSpeed = 7.5f;
    public float catchRadius = 2.2f;

    [Tooltip("Where a held disc rides. Authored as a child in the prefab; " +
             "created automatically at runtime if left empty.")]
    public Transform HandPoint;

    [HideInInspector] public Vector3 aiTarget;   // where the AI currently wants to go

    [HideInInspector] public bool Winding;       // winding up a throw — the camera holds the
    [HideInInspector] public Vector3 WindPivot;  // pivot foot so the step-out is visible

    [Tooltip("Animator on the character-model child. Drives idle / run / throw. " +
             "Wired by the character setup; leave empty for the old capsule look.")]
    public Animator animator;

    [Header("Jump (vertical catch — high disc)")]
    [Tooltip("Peak height of the hop. The raised body lifts the catch point so you can " +
             "sky a disc above a defender.")]
    public float jumpHeight = 1.7f;
    [Tooltip("Time to reach the peak (the rise). Launch is instant, then gravity takes over.")]
    public float jumpRise = 0.38f;
    [Tooltip("Time to fall back down. Shorter than the rise — asymmetric gravity (slower up, " +
             "faster down) is the weighty, snappy arc players expect.")]
    public float jumpFall = 0.28f;
    [Tooltip("A little extra reach while airborne, on top of the higher catch point.")]
    public float jumpReachBonus = 0.7f;
    [Tooltip("Fraction of your running speed carried into a jump, so you can sky a disc on " +
             "the move instead of rooting to the spot.")]
    [Range(0f, 1f)] public float jumpMomentum = 0.9f;

    [Header("Fake (pump fake)")]
    [Tooltip("How long a pump fake keeps the marker biting toward the fake side.")]
    public float fakeHold = 0.5f;

    [Header("Layout (diving catch — low / wide disc)")]
    [Tooltip("Forward lunge speed of a layout dive.")]
    public float diveSpeed = 11f;
    [Tooltip("Upward pop at launch so the body arcs out and lays flat near the turf, instead " +
             "of sliding along upright.")]
    public float diveLift = 2.2f;
    [Tooltip("Gravity pulling the dive back down onto the ground.")]
    public float diveGravity = 16f;
    [Tooltip("How low the body drops while fully laid out (pivot height along the ground).")]
    public float layoutHeight = 0.45f;
    [Tooltip("How long the full-extension reach lasts (the dive itself).")]
    public float diveExtend = 0.5f;
    [Tooltip("Lockout after landing while the player slides out and gets back up.")]
    public float diveRecover = 0.7f;
    [Tooltip("How fast the lunge/slide bleeds off (friction). Acts through the whole dive so " +
             "it's an explosive launch that decelerates, landing ~4-5 m out rather than flying.")]
    public float diveFriction = 16f;
    [Tooltip("Fraction of running speed added into the dive launch (carry your momentum).")]
    [Range(0f, 1f)] public float diveMomentum = 0.3f;
    [Tooltip("Catch radius while laid out — the extra reach is the whole point of a layout.")]
    public float diveCatchRadius = 4.2f;

    Renderer rend;
    Color baseColor;
    LineRenderer controlRing;   // white ring on the ground marking the human's player
    Vector3 lastAnimPos;        // previous position, to derive velocity for the animator
    bool lastAnimPosInit;
    Vector3 groundVelocity;     // last frame's running velocity (flat), carried into jumps / dives
    float extendTimer;          // >0: laid out, lunging with extended reach
    float recoverTimer;         // >0: down on the ground, sliding out / getting back up (locked)
    Vector3 diveDir;            // the committed dive line
    bool airborne;              // mid-jump (vertical hop)
    float vertVel;              // vertical velocity for the jump / dive arc
    Vector3 airVel;             // horizontal velocity carried through a jump or dive
    float groundY;              // standing ground height to land back on
    Vector3 fakeDir;           // direction of the last pump fake (world, flat)
    float fakeTimer;           // >0: a fake is still selling, marker biting

    /// <summary>The current fake direction while a pump fake sells, else zero. The
    /// marking defender reads this and bites toward it, opening the other side.</summary>
    public Vector3 CurrentFake => fakeTimer > 0f ? fakeDir : Vector3.zero;

    /// <summary>True while jumping, diving, or getting back up — controllers stop
    /// steering so the move plays out without input fighting it.</summary>
    public bool Busy => extendTimer > 0f || recoverTimer > 0f || airborne;

    /// <summary>Catch reach right now. A layout extends it a lot (wide horizontal grab);
    /// a jump adds a little (the real gain there is the raised catch point). The disc
    /// reads this when testing catches.</summary>
    public float CatchReach =>
        extendTimer > 0f ? diveCatchRadius :
        airborne        ? catchRadius + jumpReachBonus : catchRadius;

    void Awake()
    {
        if (HandPoint == null)
        {
            // no authored hand point — make one in front of the body
            var hp = new GameObject("Hand").transform;
            hp.SetParent(transform, false);
            hp.localPosition = new Vector3(0.6f, 0.9f, 0.5f);
            HandPoint = hp;
        }

        rend = GetComponentInChildren<Renderer>();
        if (rend != null) baseColor = rend.material.color;

        BuildControlRing();
    }

    /// <summary>A flat white ring at the player's feet, shown only on the player the
    /// human controls. Local-space so it rides the body (players only yaw, so it
    /// stays flat on the ground).</summary>
    void BuildControlRing()
    {
        var go = new GameObject("ControlRing");
        go.transform.SetParent(transform, false);
        controlRing = go.AddComponent<LineRenderer>();
        controlRing.useWorldSpace = false;
        controlRing.loop = true;
        controlRing.widthMultiplier = 0.1f;
        controlRing.material = new Material(Shader.Find("Sprites/Default"));
        controlRing.startColor = controlRing.endColor = Color.white;

        const int seg = 32;
        const float radius = 1.1f;
        float y = -StandHeight + 0.05f;   // pivot sits at StandHeight, so this is ~ground
        controlRing.positionCount = seg;
        for (int i = 0; i < seg; i++)
        {
            float a = (i / (float)seg) * Mathf.PI * 2f;
            controlRing.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius));
        }
        controlRing.enabled = false;
    }

    /// <summary>Move toward a world point this frame, staying upright. Pass
    /// <paramref name="faceMove"/> = false to keep moving without turning to face the
    /// travel direction — the caller then sets facing itself (e.g. a defender who
    /// watches the disc while backpedalling), which the 2D blend tree reads as a
    /// backpedal / strafe instead of a forward run.</summary>
    public void MoveToward(Vector3 worldTarget, float speedScale = 1f, bool faceMove = true)
    {
        Vector3 to = worldTarget - transform.position;
        to.y = 0f;
        float step = moveSpeed * speedScale * Time.deltaTime;
        if (to.sqrMagnitude > 0.0004f)
        {
            Vector3 dir = to.normalized;
            transform.position += dir * Mathf.Min(step, to.magnitude);
            if (faceMove) FaceDir(dir);
        }
    }

    /// <summary>Move along a direction vector this frame. See <see cref="MoveToward"/>
    /// for <paramref name="faceMove"/>.</summary>
    public void MoveDir(Vector3 dir, float speedScale = 1f, bool faceMove = true)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();
        transform.position += dir * moveSpeed * speedScale * Time.deltaTime;
        if (faceMove) FaceDir(dir);
    }

    public void FaceDir(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(dir),
                12f * Time.deltaTime);
    }

    /// <summary>Feed the animator a run speed derived from how far the (script-driven)
    /// transform actually moved this frame — works the same whether a human or the AI
    /// is steering, since both just move the transform.</summary>
    void LateUpdate()
    {
        if (!lastAnimPosInit) { lastAnimPos = transform.position; lastAnimPosInit = true; }
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);

        // Velocity of the (script-driven) transform, expressed in the body's OWN frame.
        // local.z is how fast we move the way we face (forward/back), local.x is sideways.
        // The 2D directional blend tree reads these, so a player who faces the disc while
        // moving away from it reads as a backpedal, and one shuffling sideways strafes.
        Vector3 worldVel = (transform.position - lastAnimPos) / dt;
        lastAnimPos = transform.position;

        // Capture the running velocity (flat) so jumps and dives can carry momentum. Only
        // while grounded/steering — not mid-action, or we'd record the action's own motion
        // as "run speed".
        if (!Busy) groundVelocity = new Vector3(worldVel.x, 0f, worldVel.z);

        if (animator == null) return;
        Vector3 local = transform.InverseTransformDirection(worldVel);

        // Damped so the blend eases between clips instead of snapping.
        animator.SetFloat("MoveX", local.x, 0.1f, dt);
        animator.SetFloat("MoveZ", local.z, 0.1f, dt);
    }

    /// <summary>Fire the throw animation. Called the instant a throw is released.</summary>
    public void PlayThrow()
    {
        if (animator != null) animator.SetTrigger("Throw");
    }

    /// <summary>Drive the jump and layout arcs each frame. Both are transform-only (no
    /// physics body) but use real ballistics for feel: the jump is a parabola with
    /// asymmetric gravity and carried momentum; the layout pops out, arcs down to lay flat,
    /// then slides to a stop and gets up.</summary>
    void Update()
    {
        float dt = Time.deltaTime;

        // Layout: an arcing dive. Pop out and down to lay flat near the turf carrying forward
        // momentum, then a ground recovery that slides to a stop and stands back up. The
        // extended reach (CatchReach) is live for the whole lunge.
        if (extendTimer > 0f)
        {
            extendTimer -= dt;
            airVel = Vector3.MoveTowards(airVel, Vector3.zero, diveFriction * dt);   // decelerating lunge
            vertVel -= diveGravity * dt;
            Vector3 p = transform.position + airVel * dt;
            p.y = Mathf.Max(p.y + vertVel * dt, layoutHeight);   // arc down, lay flat on the ground
            transform.position = p;
            if (extendTimer <= 0f) recoverTimer = diveRecover;   // landed → slide out / get up
        }
        else if (recoverTimer > 0f)
        {
            recoverTimer -= dt;
            airVel = Vector3.MoveTowards(airVel, Vector3.zero, diveFriction * dt);   // slide out
            Vector3 p = transform.position + airVel * dt;
            p.y = Mathf.MoveTowards(p.y, groundY,
                                    (groundY - layoutHeight) / Mathf.Max(diveRecover, 0.01f) * dt);
            transform.position = p;
            if (recoverTimer <= 0f) { p = transform.position; p.y = groundY; transform.position = p; }
        }

        if (fakeTimer > 0f) fakeTimer -= dt;   // the bite wears off

        // Jump: a real parabola with asymmetric gravity (slower rise, faster fall) and the
        // running momentum carried in, so the raised body skies a high disc — and you can do
        // it on the move instead of from a standstill.
        if (airborne)
        {
            float g = vertVel > 0f
                ? 2f * jumpHeight / (jumpRise * jumpRise)    // rise: gentle
                : 2f * jumpHeight / (jumpFall * jumpFall);   // fall: faster
            vertVel -= g * dt;
            Vector3 p = transform.position + airVel * dt;    // drift with momentum
            p.y += vertVel * dt;
            if (p.y <= groundY) { p.y = groundY; airborne = false; vertVel = 0f; airVel = Vector3.zero; }
            transform.position = p;
        }
    }

    /// <summary>Jump straight up to catch a high disc — the raised body lifts the catch
    /// point above defenders. No-op if already busy; steering locks until you land.</summary>
    public void Jump()
    {
        if (Busy) return;
        groundY = transform.position.y;
        vertVel = 2f * jumpHeight / Mathf.Max(jumpRise, 0.01f);   // instant launch velocity
        airVel  = groundVelocity * jumpMomentum;                  // carry the run into the air
        airborne = true;
        if (animator != null) animator.SetTrigger("Jump");
    }

    /// <summary>Sell a pump fake toward a direction (the step-out side): the marker bites
    /// that way for <see cref="fakeHold"/> seconds, opening the opposite side for the real
    /// throw. Plays the throw motion as the pump, but no disc is released.</summary>
    public void Fake(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return;
        fakeDir = dir.normalized;
        fakeTimer = fakeHold;
        if (animator != null) animator.SetTrigger("Throw");   // reuse the throw as the pump
    }

    /// <summary>Lay out (dive) in a direction for a full-extension catch: a committed
    /// lunge with a much longer <see cref="CatchReach"/>, then a recovery on the ground.
    /// No-op if already busy. Steering is locked (<see cref="Busy"/>) until recovered.</summary>
    public void Layout(Vector3 dir)
    {
        if (Busy) return;
        dir.y = 0f;
        diveDir = dir.sqrMagnitude > 1e-4f ? dir.normalized : transform.forward;
        transform.rotation = Quaternion.LookRotation(diveDir);   // commit to the dive line
        groundY = transform.position.y;
        // Launch along the dive line at full lunge speed, plus the part of your run that's
        // already heading that way (carry momentum), with an upward pop so it arcs out flat.
        float along = Mathf.Max(0f, Vector3.Dot(groundVelocity, diveDir));
        airVel  = diveDir * (diveSpeed + along * diveMomentum);
        vertVel = diveLift;
        extendTimer = diveExtend;
        if (animator != null) animator.SetTrigger("Dive");
    }

    /// <summary>Pivot height when standing (capsule half-height). The disc lands at
    /// ground level, so use this to place a player without sinking them into it.</summary>
    public const float StandHeight = 1f;

    /// <summary>Drop the player onto a ground spot, keeping them on their feet —
    /// the spot's Y is ignored in favor of <see cref="StandHeight"/>.</summary>
    public void PlaceAtGround(Vector3 spot)
    {
        transform.position = new Vector3(spot.x, StandHeight, spot.z);
    }

    /// <summary>Mark the player the human controls: a white ground ring, plus a
    /// subtle brighten so they read clearly.</summary>
    public void SetHighlighted(bool on)
    {
        if (controlRing != null) controlRing.enabled = on;
        if (rend != null)
            rend.material.color = on ? Color.Lerp(baseColor, Color.white, 0.35f) : baseColor;
    }

    public bool HasDisc => MatchManager.I.disc.Holder == this;
}
