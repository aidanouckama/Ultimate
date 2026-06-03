using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>The three basic throws. Each sets a different launch profile (speed, loft,
/// glide) and a default curve lean the A/D input adds to.</summary>
public enum ThrowKind { Backhand, Flick, Hammer }

/// <summary>
/// Translates player input into action for whichever player MatchManager hands us.
///  - No disc : WASD / arrows move (camera-relative) to run under the disc.
///  - Has disc: you're planted (no traveling). AIM with the camera (mouse — toggle the
///    frisbee cam with V). HOLD LEFT-mouse to wind up a BACKHAND (steps out to your left)
///    or RIGHT-mouse to wind up a FLICK/forehand (steps out right); hold longer for more
///    power, release to throw. Hold A / D to curve. A short stub hints the curve.
///
/// Uses the new Input System (UnityEngine.InputSystem), so the project can stay
/// on Active Input Handling = "Input System Package (New)".
/// </summary>
public class HumanController : MonoBehaviour
{
    [Header("References")]
    public Camera cam;

    [Header("Throw tuning")]
    [Tooltip("Hold shorter than this and release = a pump FAKE; hold longer = a real throw.")]
    public float fakeTime = 0.18f;
    [Tooltip("Seconds of holding the mouse button to wind up to full power.")]
    public float chargeTime = 0.9f;
    public float minThrowSpeed = 10f;
    public float maxThrowSpeed = 27f;
    public float loftBase = 2.5f;
    public float loftScale = 4.0f;

    [Header("Step-out")]
    [Tooltip("How far the thrower pivots out to the side while winding up (metres).")]
    public float stepDistance = 1.3f;
    [Tooltip("Seconds for the step-out to reach full extension.")]
    public float stepTime = 0.15f;

    [Header("Curve")]
    [Tooltip("Hold A / D (or ← / →) while aiming to bend the throw. Max signed spin.")]
    public float maxCurveSpin = 1.4f;
    [Tooltip("How fast the curve winds up to full while the curve key is held.")]
    public float curveChargeRate = 2.2f;
    [Tooltip("Fraction of the predicted flight drawn as the curve hint. Long enough to " +
             "show the bend, short enough that the landing spot stays hidden.")]
    [Range(0.2f, 1f)] public float hintFraction = 0.6f;

    const int HintMaxSteps = 200;
    readonly Vector3[] hintBuf = new Vector3[HintMaxSteps];   // reused so the hint never allocates

    LineRenderer aim;
    Transform shadowBlob;      // soft dark shadow under the disc; grows with height
    Material shadowMat;
    bool charging;        // a mouse button is held, winding up a throw
    float charge;         // 0..1 power, fills while the button is held
    float holdTime;       // seconds the button has been held (< fakeTime on release = fake)
    float curveSpin;      // signed spin wound with A / D, on top of the throw's curve lean
    bool aimingThrow;     // true on frames we hold the disc (HUD shows the throw prompt)
    ThrowKind kind = ThrowKind.Backhand;
    Vector3 stepBase;     // body position when the windup began, to step back to
    Vector3 stepSide;     // unit lateral direction of the step-out (left=BH, right=flick)
    float stepAmt;        // 0..1 eased step-out extension

    // committed to a real throw once held past the fake window
    bool Committed => charging && holdTime >= fakeTime;

    /// <summary>Throw power 0..1 once committed to a throw (for the HUD bar), or -1.</summary>
    public float ThrowCharge => Committed ? charge : -1f;

    /// <summary>True while holding the disc, so the HUD can prompt the throw buttons.</summary>
    public bool HoldingDisc => aimingThrow;

    /// <summary>Name of the throw being wound up (for the HUD), e.g. "Backhand".</summary>
    public string ThrowTypeLabel => Spec(kind).name;

    /// <summary>Launch profile per throw type. speedMul/loftMul scale the base launch;
    /// curveLean is the default sideways spin the A/D input adds to (backhand and flick
    /// bend opposite ways); liftMul is the glide handed to the disc (low for a hammer, so
    /// it arcs over the mark and drops instead of sailing).</summary>
    struct ThrowSpec { public string name; public float speedMul, loftMul, curveLean, liftMul; }

    static ThrowSpec Spec(ThrowKind k) => k switch
    {
        ThrowKind.Flick  => new ThrowSpec { name = "Flick",   speedMul = 1.1f,  loftMul = 0.7f, curveLean =  0.25f, liftMul = 1f },
        ThrowKind.Hammer => new ThrowSpec { name = "Hammer",  speedMul = 0.85f, loftMul = 2.2f, curveLean = -0.15f, liftMul = 0.25f },
        _                => new ThrowSpec { name = "Backhand", speedMul = 1f,    loftMul = 1f,   curveLean = -0.25f, liftMul = 1f },
    };

    /// <summary>The spin actually thrown: the type's built-in lean plus the player's A/D wind.</summary>
    float EffectiveSpin() => Mathf.Clamp(Spec(kind).curveLean + curveSpin, -2f, 2f);

    void Awake()
    {
        if (cam == null) cam = Camera.main;
        BuildAimLine();
        BuildShadowBlob();
    }

    void Update()
    {
        var mm = MatchManager.I;
        if (mm == null) return;

        // Always show the disc's depth cue (it's hard to read height from the
        // eagle-eye camera), independent of who's controlling.
        UpdateDiscShadow(mm);

        // Play stopped (goal celebration / reset): no moving or throwing.
        if (!mm.PointLive)
        {
            aimingThrow = false; charging = false; stepAmt = 0f; holdTime = 0f; HideAim();
            if (mm.Controlled != null) mm.Controlled.Winding = false;
            return;
        }

        Player me = mm.Controlled;
        if (me == null) return;

        if (me.HasDisc) HandleThrowInput(mm, me);
        else            HandleMovement(me);
    }

    // --- input reads (new Input System) -----------------------------------

    static Vector2 ReadMoveAxis()
    {
        var kb = Keyboard.current;
        Vector2 mv = Vector2.zero;
        if (kb == null) return mv;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    mv.y += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  mv.y -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) mv.x += 1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  mv.x -= 1f;
        return mv;
    }

    // +1 curls right, -1 curls left (matches Disc.Aero's sign convention).
    static float ReadCurveAxis()
    {
        var kb = Keyboard.current;
        float c = 0f;
        if (kb == null) return c;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) c += 1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  c -= 1f;
        return c;
    }

    // --- running ----------------------------------------------------------

    void HandleMovement(Player me)
    {
        HideAim();
        aimingThrow = false;
        charging = false; stepAmt = 0f; holdTime = 0f;   // any interrupted wind-up ends cleanly
        me.Winding = false;

        if (me.Busy) return;   // mid-layout: no steering until they're back up

        Vector2 mv = ReadMoveAxis();
        Vector3 f = Flat(cam.transform.forward);
        Vector3 r = Flat(cam.transform.right);
        Vector3 dir = f * mv.y + r * mv.x;

        var mm = MatchManager.I;
        var kb = Keyboard.current;

        // SPACE = jump (vertical) — sky a high disc above a defender.
        if (kb != null && kb.spaceKey.wasPressedThisFrame)
        {
            me.Jump();
            return;
        }

        // SHIFT / F = lay out (horizontal dive): toward the disc if it's airborne, else
        // the way you're running. A committed full-extension bid — locked until you're up.
        if (kb != null && (kb.leftShiftKey.wasPressedThisFrame || kb.fKey.wasPressedThisFrame))
        {
            Vector3 to = (mm != null && mm.disc.state == Disc.State.Flying)
                         ? mm.disc.transform.position - me.transform.position
                         : dir;
            me.Layout(to);
            return;
        }

        // On defence, face the disc (you read as a backpedalling / shuffling defender);
        // on offence, face where you run. Movement stays camera-relative either way.
        bool defending = mm != null && mm.disc != null && mm.possession != me.team;
        me.MoveDir(dir, 1f, faceMove: !defending);
        if (defending)
            me.FaceDir(mm.disc.transform.position - me.transform.position);
    }

    // --- throwing ---------------------------------------------------------

    void HandleThrowInput(MatchManager mm, Player me)
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        aimingThrow = true;   // HUD prompts the throw buttons while we hold the disc

        // Always face where the camera aims, so the body points at the throw.
        me.FaceDir(Flat(cam.transform.forward));

        if (!charging)
        {
            // Start a wind-up: LEFT = backhand (step out left), RIGHT = flick (step right).
            if (mouse.leftButton.wasPressedThisFrame)       StartWindup(me, ThrowKind.Backhand);
            else if (mouse.rightButton.wasPressedThisFrame) StartWindup(me, ThrowKind.Flick);
            return;
        }

        // Winding up: which button owns this throw?
        var btn = kind == ThrowKind.Backhand ? mouse.leftButton : mouse.rightButton;
        if (btn.isPressed)
        {
            holdTime += Time.deltaTime;
            charge = Mathf.Clamp01(charge + Time.deltaTime / Mathf.Max(chargeTime, 0.01f));
            curveSpin = Mathf.Clamp(
                curveSpin + ReadCurveAxis() * curveChargeRate * Time.deltaTime,
                -maxCurveSpin, maxCurveSpin);

            // pivot out to the side — both a fake and a throw step out the same way (that's
            // the point: the mark can't tell yet), so the throw releases from there.
            stepAmt = Mathf.MoveTowards(stepAmt, 1f, Time.deltaTime / Mathf.Max(stepTime, 0.01f));
            me.transform.position = stepBase + stepSide * stepDistance * stepAmt;

            if (Committed) DrawCurveHint(mm, ThrowVelocity(charge), EffectiveSpin(), Spec(kind).liftMul);
            else HideAim();   // still inside the fake window — don't reveal a throw line yet
        }
        else
        {
            charging = false;
            HideAim();
            if (holdTime < fakeTime)
            {
                // quick tap = FAKE: pump and step out toward that side, then recover. The
                // marker bites; no disc is released, so it's never a turnover.
                me.Fake(stepSide);
            }
            else if (charge > 0.05f)
            {
                // held = real THROW, released from the stepped-out position
                mm.disc.Throw(ThrowVelocity(charge), me.team, EffectiveSpin(), null, Spec(kind).liftMul);
                me.PlayThrow();
            }
            me.transform.position = stepBase;   // recover the pivot
            curveSpin = 0f; stepAmt = 0f; holdTime = 0f;
            me.Winding = false;
        }
    }

    /// <summary>Begin a wind-up for one throw type, capturing the pivot foot and the side
    /// to step toward (backhand steps to the thrower's left, flick to the right). Until the
    /// button is held past <see cref="fakeTime"/> this is indistinguishable from a fake.</summary>
    void StartWindup(Player me, ThrowKind k)
    {
        charging = true; kind = k; charge = 0f; holdTime = 0f; curveSpin = 0f; stepAmt = 0f;
        stepBase = me.transform.position;
        Vector3 r = me.transform.right; r.y = 0f; r.Normalize();
        stepSide = (k == ThrowKind.Backhand) ? -r : r;
        me.Winding = true; me.WindPivot = stepBase;   // camera holds this so the step shows
    }

    /// <summary>Launch velocity for a given power level (0..1 from the wind-up): aimed
    /// where the camera looks, with speed and loft scaling off the power and the throw
    /// type's profile (a flick is flatter and faster than a backhand).</summary>
    Vector3 ThrowVelocity(float power01)
    {
        var t = Spec(kind);
        Vector3 dir = Flat(cam.transform.forward);
        float speed = Mathf.Lerp(minThrowSpeed, maxThrowSpeed, power01) * t.speedMul;
        float loft  = (loftBase + loftScale * power01) * t.loftMul;
        return dir * speed + Vector3.up * loft;
    }

    // --- curve hint -------------------------------------------------------

    /// <summary>The leading part of the predicted flight, simulated with the disc's OWN
    /// model (same Aero + spin decay) so the bend matches exactly what the throw will do.
    /// We draw only the first <see cref="hintFraction"/> of the flight — enough to read
    /// the curve as it develops, but stopping before the landing so it stays a hint, not
    /// a guide to the exact spot. (A short stub looks straight because the curl is a
    /// sideways acceleration: deflection grows with time², so the bend shows up late.)</summary>
    void DrawCurveHint(MatchManager mm, Vector3 v0, float spin, float liftMul)
    {
        const float dt = 0.04f;
        float ground = mm.disc.restHeight;

        Vector3 p = mm.disc.transform.position;
        Vector3 v = v0;
        float s = spin;
        int n = 0;
        for (; n < HintMaxSteps; n++)
        {
            hintBuf[n] = p;
            v += mm.disc.Aero(v, s, liftMul) * dt;   // semi-implicit Euler, matching the disc's flight
            p += v * dt;
            s = Mathf.MoveTowards(s, 0f, dt * mm.disc.spinDecay);
            if (p.y <= ground) { n++; break; }
        }

        int count = Mathf.Clamp(Mathf.RoundToInt(n * hintFraction), 2, n);
        aim.enabled = true;
        aim.positionCount = count;
        for (int i = 0; i < count; i++) aim.SetPosition(i, hintBuf[i]);
    }

    void HideAim()
    {
        if (aim != null) aim.enabled = false;
    }

    void BuildAimLine()
    {
        var go = new GameObject("AimLine");
        go.transform.SetParent(transform, false);
        aim = go.AddComponent<LineRenderer>();
        aim.widthMultiplier = 0.15f;
        aim.material = new Material(Shader.Find("Sprites/Default"));
        aim.startColor = new Color(1f, 1f, 1f, 0.9f);
        aim.endColor   = new Color(1f, 1f, 1f, 0.1f);
        aim.numCapVertices = 2;
        aim.enabled = false;
    }

    /// <summary>Depth cue for the top-down view: a soft dark shadow on the ground
    /// directly under the disc. It grows and fades as the disc climbs, shrinks and
    /// darkens as it nears the ground — so you can read the disc's height at a glance
    /// (and where it'll come down) while it flies.</summary>
    void UpdateDiscShadow(MatchManager mm)
    {
        bool aloft = mm.disc != null && mm.disc.state == Disc.State.Flying;
        if (shadowBlob.gameObject.activeSelf != aloft)
            shadowBlob.gameObject.SetActive(aloft);
        if (!aloft) return;

        float ground = mm.disc.restHeight;
        Vector3 p = mm.disc.transform.position;
        float h = Mathf.Max(0f, p.y - ground);

        float size  = Mathf.Clamp(0.9f + h * 0.18f, 0.9f, 3.5f);          // diameter (m)
        float alpha = Mathf.Lerp(0.6f, 0.18f, Mathf.Clamp01(h / 12f));    // fade when high

        shadowBlob.SetPositionAndRotation(
            new Vector3(p.x, ground + 0.02f, p.z),
            Quaternion.Euler(90f, 0f, 0f));      // lie flat on the ground
        shadowBlob.localScale = new Vector3(size, size, 1f);

        Color c = shadowMat.color; c.a = alpha; shadowMat.color = c;
    }

    /// <summary>A flat quad on the ground textured with a soft radial blob — the disc's
    /// shadow. No collider (players/disc are collider-free by design).</summary>
    void BuildShadowBlob()
    {
        shadowBlob = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
        shadowBlob.name = "DiscShadow";
        shadowBlob.SetParent(transform, false);

        var col = shadowBlob.GetComponent<Collider>();
        if (col != null) Destroy(col);

        shadowMat = new Material(Shader.Find("Sprites/Default"));
        shadowMat.mainTexture = BuildSoftCircleTex(64);
        shadowMat.color = new Color(0f, 0f, 0f, 0.6f);   // dark, semi-transparent
        shadowBlob.GetComponent<MeshRenderer>().material = shadowMat;

        shadowBlob.rotation = Quaternion.Euler(90f, 0f, 0f);
        shadowBlob.gameObject.SetActive(false);
    }

    /// <summary>Generate a soft-edged white circle (alpha falls off to the rim) for the
    /// shadow texture, so the blob has feathered edges instead of a hard disc.</summary>
    static Texture2D BuildSoftCircleTex(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp
        };
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c; // 0→1
                float a = Mathf.Clamp01(1f - d);
                a *= a;                                  // feather toward the edge
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return tex;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-4f ? v.normalized : v; }
}
