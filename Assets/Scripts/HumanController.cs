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

    LineRenderer aim;
    bool aiming;
    Vector2 dragStart;

    void Awake()
    {
        if (cam == null) cam = Camera.main;
        BuildAimLine();
    }

    void Update()
    {
        var mm = MatchManager.I;
        Player me = mm != null ? mm.Controlled : null;
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

    // --- running ----------------------------------------------------------

    void HandleMovement(Player me)
    {
        aim.enabled = false;
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
        }

        if (aiming && mouse.leftButton.isPressed)
        {
            Vector3 vel = ThrowVelocity(MousePos() - dragStart);
            DrawPreview(mm, me, vel);
            me.FaceDir(Flat(vel));
        }

        if (aiming && mouse.leftButton.wasReleasedThisFrame)
        {
            aiming = false;
            aim.enabled = false;
            Vector2 drag = MousePos() - dragStart;
            if (drag.magnitude > 8f)   // ignore an accidental tap
                mm.disc.Throw(ThrowVelocity(drag), me.team, 0f);
        }
    }

    Vector3 ThrowVelocity(Vector2 drag)
    {
        Vector3 f = Flat(cam.transform.forward);
        Vector3 r = Flat(cam.transform.right);

        // drag up the screen => throw away from camera (downfield)
        Vector3 dir = (f * drag.y + r * drag.x);
        if (dir.sqrMagnitude < 0.0001f) dir = f;
        dir.Normalize();

        float power01 = Mathf.Clamp01(drag.magnitude /
                                      (Screen.height * fullPowerDragFraction));
        float speed = Mathf.Lerp(minThrowSpeed, maxThrowSpeed, power01);
        float loft  = loftBase + loftScale * power01;
        return dir * speed + Vector3.up * loft;
    }

    // --- aim preview ------------------------------------------------------

    void DrawPreview(MatchManager mm, Player me, Vector3 v0)
    {
        const int steps = 45;
        const float dt = 0.045f;
        Vector3 p = mm.disc.transform.position;
        Vector3 v = v0;
        aim.enabled = true;
        aim.positionCount = steps;
        for (int i = 0; i < steps; i++)
        {
            aim.SetPosition(i, p);
            v += mm.disc.Aero(v, 0f) * dt;
            p += v * dt;
            if (p.y < mm.disc.restHeight) { p.y = mm.disc.restHeight; }
        }
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

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-4f ? v.normalized : v; }
}
