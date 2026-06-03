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

    [Tooltip("Animator on the character-model child. Drives idle / run / throw. " +
             "Wired by the character setup; leave empty for the old capsule look.")]
    public Animator animator;

    [Header("Jump (vertical catch — high disc)")]
    [Tooltip("Peak height of the hop. The raised body lifts the catch point so you can " +
             "sky a disc above a defender.")]
    public float jumpHeight = 1.6f;
    [Tooltip("Time from takeoff to landing.")]
    public float jumpDuration = 0.65f;
    [Tooltip("A little extra reach while airborne, on top of the higher catch point.")]
    public float jumpReachBonus = 0.7f;

    [Header("Layout (diving catch — low / wide disc)")]
    [Tooltip("Horizontal lunge speed during a layout dive.")]
    public float diveSpeed = 12f;
    [Tooltip("How long the full-extension reach lasts (the dive itself).")]
    public float diveExtend = 0.5f;
    [Tooltip("Lockout after landing while the player gets back up (can't move or dive).")]
    public float diveRecover = 0.7f;
    [Tooltip("Catch radius while laid out — the extra reach is the whole point of a layout.")]
    public float diveCatchRadius = 4.2f;

    Renderer rend;
    Color baseColor;
    LineRenderer controlRing;   // white ring on the ground marking the human's player
    Vector3 lastAnimPos;        // previous position, to derive velocity for the animator
    bool lastAnimPosInit;
    float extendTimer;          // >0: laid out, lunging with extended reach
    float recoverTimer;         // >0: down on the ground, getting back up (locked out)
    Vector3 diveDir;            // the committed dive line
    float jumpTimer;            // >0: airborne on a vertical jump
    float jumpBaseY;            // ground height to return to on landing

    /// <summary>True while jumping, diving, or getting back up — controllers stop
    /// steering so the move plays out without input fighting it.</summary>
    public bool Busy => extendTimer > 0f || recoverTimer > 0f || jumpTimer > 0f;

    /// <summary>Catch reach right now. A layout extends it a lot (wide horizontal grab);
    /// a jump adds a little (the real gain there is the raised catch point). The disc
    /// reads this when testing catches.</summary>
    public float CatchReach =>
        extendTimer > 0f ? diveCatchRadius :
        jumpTimer   > 0f ? catchRadius + jumpReachBonus : catchRadius;

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
        if (animator == null) return;
        if (!lastAnimPosInit) { lastAnimPos = transform.position; lastAnimPosInit = true; }
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);

        // Velocity of the (script-driven) transform, expressed in the body's OWN frame.
        // local.z is how fast we move the way we face (forward/back), local.x is sideways.
        // The 2D directional blend tree reads these, so a player who faces the disc while
        // moving away from it reads as a backpedal, and one shuffling sideways strafes.
        Vector3 worldVel = (transform.position - lastAnimPos) / dt;
        lastAnimPos = transform.position;
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

    /// <summary>Drive the dive: a quick committed lunge with extended reach, then a
    /// brief lockout while getting up. Movement is transform-only (no physics), like the
    /// rest of the game, so the diver stays at standing height and just slides along the
    /// dive line — the animation sells the layout.</summary>
    void Update()
    {
        // Layout: lunge along the dive line, then a ground recovery.
        if (extendTimer > 0f)
        {
            extendTimer -= Time.deltaTime;
            transform.position += diveDir * diveSpeed * Time.deltaTime;
            if (extendTimer <= 0f) recoverTimer = diveRecover;   // hit the ground → get up
        }
        else if (recoverTimer > 0f)
        {
            recoverTimer -= Time.deltaTime;
        }

        // Jump: a sine arc up and back down. Pure vertical (steering is locked), so the
        // raised transform lifts the catch point to sky a high disc.
        if (jumpTimer > 0f)
        {
            jumpTimer -= Time.deltaTime;
            float t = Mathf.Clamp01(1f - jumpTimer / jumpDuration);   // 0 → 1 over the hop
            var p = transform.position;
            p.y = jumpBaseY + jumpHeight * Mathf.Sin(Mathf.PI * t);
            if (jumpTimer <= 0f) p.y = jumpBaseY;                     // settle exactly on land
            transform.position = p;
        }
    }

    /// <summary>Jump straight up to catch a high disc — the raised body lifts the catch
    /// point above defenders. No-op if already busy; steering locks until you land.</summary>
    public void Jump()
    {
        if (Busy) return;
        jumpBaseY = transform.position.y;
        jumpTimer = jumpDuration;
        if (animator != null) animator.SetTrigger("Jump");
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
