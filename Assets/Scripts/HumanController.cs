using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Translates player input into action for whichever player MatchManager hands us.
///  - No disc : WASD / arrows move (camera-relative) to run under the disc.
///  - Has disc: you're planted (no traveling). Press & DRAG the mouse in the
///    direction you want to throw, then release to flick it. Drag length = power,
///    drag direction = throw direction. A dotted line previews the flight.
///
/// Uses the new Input System (UnityEngine.InputSystem), so the project can stay
/// on Active Input Handling = "Input System Package (New)".
/// </summary>
public class HumanController : MonoBehaviour
{
    [Header("References")]
    public Camera cam;

    [Header("Throw tuning")]
    [Tooltip("Drag this fraction of screen height for maximum power.")]
    public float fullPowerDragFraction = 0.35f;
    public float minThrowSpeed = 10f;
    public float maxThrowSpeed = 27f;
    public float loftBase = 2.5f;
    public float loftScale = 4.0f;

    [Header("Curve")]
    [Tooltip("Hold A / D (or ← / →) while aiming to bend the throw. Max signed spin.")]
    public float maxCurveSpin = 1.4f;
    [Tooltip("How fast the curve winds up to full while the curve key is held.")]
    public float curveChargeRate = 2.2f;

    LineRenderer aim;
    LineRenderer landMarker;
    LineRenderer dropLine;     // thin vertical line from the disc down to the ground
    LineRenderer shadowRing;   // ring on the ground directly under the disc
    bool aiming;
    Vector2 dragStart;
    float curveSpin;     // signed spin charged during the current aim

    void Awake()
    {
        if (cam == null) cam = Camera.main;
        BuildAimLine();
    }

    void Update()
    {
        var mm = MatchManager.I;
        if (mm == null) return;

        // Always show the disc's depth cue (it's hard to read height from the
        // eagle-eye camera), independent of who's controlling.
        UpdateDiscShadow(mm);

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

    static Vector2 MousePos() =>
        Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

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
        aiming = false;

        Vector2 mv = ReadMoveAxis();
        Vector3 f = Flat(cam.transform.forward);
        Vector3 r = Flat(cam.transform.right);
        Vector3 dir = f * mv.y + r * mv.x;
        me.MoveDir(dir);
    }

    // --- throwing ---------------------------------------------------------

    void HandleThrowInput(MatchManager mm, Player me)
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            aiming = true;
            dragStart = MousePos();
            curveSpin = 0f;
        }

        if (aiming && mouse.leftButton.isPressed)
        {
            // wind the curve up/down while you hold A/D, clamped both ways
            curveSpin = Mathf.Clamp(
                curveSpin + ReadCurveAxis() * curveChargeRate * Time.deltaTime,
                -maxCurveSpin, maxCurveSpin);

            Vector3 vel = ThrowVelocity(MousePos() - dragStart);
            DrawPreview(mm, me, vel, curveSpin);
            me.FaceDir(Flat(vel));
        }

        if (aiming && mouse.leftButton.wasReleasedThisFrame)
        {
            aiming = false;
            HideAim();
            Vector2 drag = MousePos() - dragStart;
            if (drag.magnitude > 8f)   // ignore an accidental tap
                mm.disc.Throw(ThrowVelocity(drag), me.team, curveSpin);
        }
    }

    /// <summary>Throw by PULLING BACK: drag the mouse opposite the way you want the
    /// disc to go (like a slingshot / golf swing), then release. Drag distance = power.
    /// This takes more touch than point-and-click — you judge the launch yourself.</summary>
    Vector3 ThrowVelocity(Vector2 drag)
    {
        Vector3 f = Flat(cam.transform.forward);
        Vector3 r = Flat(cam.transform.right);

        // pull back to launch forward: the throw goes OPPOSITE the drag direction
        Vector3 dir = -(f * drag.y + r * drag.x);
        if (dir.sqrMagnitude < 0.0001f) dir = f;
        dir.Normalize();

        float power01 = Mathf.Clamp01(drag.magnitude /
                                      (Screen.height * fullPowerDragFraction));
        float speed = Mathf.Lerp(minThrowSpeed, maxThrowSpeed, power01);
        float loft  = loftBase + loftScale * power01;
        return dir * speed + Vector3.up * loft;
    }

    // --- aim preview ------------------------------------------------------

    void DrawPreview(MatchManager mm, Player me, Vector3 v0, float spin)
    {
        const int maxSteps = 80;
        const float dt = 0.045f;
        float ground = mm.disc.restHeight;
        Vector3 p = mm.disc.transform.position;
        Vector3 v = v0;

        aim.enabled = true;
        aim.positionCount = maxSteps;     // temporary; trimmed to `count` below
        int count = 0;
        aim.SetPosition(count++, p);

        for (int i = 1; i < maxSteps; i++)
        {
            v += mm.disc.Aero(v, spin) * dt;   // same spin as the throw, so the line curls to match
            p += v * dt;
            spin = Mathf.MoveTowards(spin, 0f, dt * mm.disc.spinDecay);   // mirror the in-flight spin decay

            bool landed = p.y <= ground;
            if (landed) p.y = ground;
            aim.SetPosition(count++, p);
            if (landed) break;                 // stop the line at the point it hits the ground
        }
        aim.positionCount = count;             // trim to the flight actually drawn

        DrawLandMarker(p, ground);             // small circle where it will land
    }

    /// <summary>A small flat white ring on the ground marking the predicted landing spot.</summary>
    void DrawLandMarker(Vector3 center, float ground)
    {
        const int seg = 24;
        const float radius = 0.6f;
        landMarker.enabled = true;
        landMarker.positionCount = seg;
        for (int i = 0; i < seg; i++)
        {
            float a = (i / (float)seg) * Mathf.PI * 2f;
            landMarker.SetPosition(i, new Vector3(
                center.x + Mathf.Cos(a) * radius,
                ground + 0.02f,
                center.z + Mathf.Sin(a) * radius));
        }
    }

    void HideAim()
    {
        aim.enabled = false;
        landMarker.enabled = false;
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

        var ring = new GameObject("LandMarker");
        ring.transform.SetParent(transform, false);
        landMarker = ring.AddComponent<LineRenderer>();
        landMarker.widthMultiplier = 0.07f;
        landMarker.material = new Material(Shader.Find("Sprites/Default"));
        landMarker.startColor = landMarker.endColor = Color.white;
        landMarker.loop = true;            // closed ring
        landMarker.numCornerVertices = 4;
        landMarker.enabled = false;

        var drop = new GameObject("DiscDropLine");
        drop.transform.SetParent(transform, false);
        dropLine = drop.AddComponent<LineRenderer>();
        dropLine.widthMultiplier = 0.04f;
        dropLine.material = new Material(Shader.Find("Sprites/Default"));
        dropLine.startColor = new Color(1f, 1f, 1f, 0.7f);   // bright at the disc
        dropLine.endColor   = new Color(1f, 1f, 1f, 0.25f);  // faint at the ground
        dropLine.positionCount = 2;
        dropLine.enabled = false;

        var shadow = new GameObject("DiscShadowRing");
        shadow.transform.SetParent(transform, false);
        shadowRing = shadow.AddComponent<LineRenderer>();
        shadowRing.widthMultiplier = 0.05f;
        shadowRing.material = new Material(Shader.Find("Sprites/Default"));
        shadowRing.startColor = shadowRing.endColor = new Color(0f, 0f, 0f, 0.55f);
        shadowRing.loop = true;
        shadowRing.numCornerVertices = 4;
        shadowRing.enabled = false;
    }

    /// <summary>Depth cue for the top-down view: a thin vertical line from the disc
    /// straight down to the ground, with a shadow ring where it meets the ground —
    /// so you can read how high and how far the disc actually is while it flies.</summary>
    void UpdateDiscShadow(MatchManager mm)
    {
        bool aloft = mm.disc != null && mm.disc.state == Disc.State.Flying;
        dropLine.enabled = aloft;
        shadowRing.enabled = aloft;
        if (!aloft) return;

        float ground = mm.disc.restHeight;
        Vector3 p = mm.disc.transform.position;
        Vector3 g = new Vector3(p.x, ground + 0.02f, p.z);

        dropLine.SetPosition(0, p);    // perpendicular to the ground: same x/z, top at the disc
        dropLine.SetPosition(1, g);

        const int seg = 20;
        const float radius = 0.5f;
        shadowRing.positionCount = seg;
        for (int i = 0; i < seg; i++)
        {
            float a = (i / (float)seg) * Mathf.PI * 2f;
            shadowRing.SetPosition(i, new Vector3(
                p.x + Mathf.Cos(a) * radius,
                ground + 0.02f,
                p.z + Mathf.Sin(a) * radius));
        }
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-4f ? v.normalized : v; }
}
