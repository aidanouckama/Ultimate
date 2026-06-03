using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Third-person camera with two modes, toggled with V:
///  - Forward: auto chase cam, eases around behind the controlled player's facing.
///    Hands-off — good for running. (Player faces movement on offence, the disc on
///    defence, so you "look where you're going" / "watch the disc" for free.)
///  - Frisbee: aim cam — the mouse turns the view (yaw) so you can line up a throw,
///    orbiting behind the player at the aimed heading. The throw goes where it looks.
///
/// Either way the camera sits behind + above the player and looks ahead. The chase
/// direction is smoothed so it arcs around instead of snapping. Falls back to the disc
/// when nobody is controlled (between points).
/// </summary>
public class CameraRig : MonoBehaviour
{
    public enum Mode { Forward, Frisbee }

    [Tooltip("How far behind the player the camera sits.")]
    public float distance = 7f;
    [Tooltip("How high above the player the camera sits.")]
    public float height = 3.2f;

    [Header("Handler (holding the disc)")]
    [Tooltip("Pull back this far while the controlled player has the disc, to survey the field.")]
    public float handlerDistance = 13f;
    [Tooltip("And rise to this height while holding the disc.")]
    public float handlerHeight = 6.5f;

    [Tooltip("Look this far ahead of the player, along the chase direction.")]
    public float lookAhead = 6f;
    [Tooltip("Height of the look-at point above the player's feet (roughly head height).")]
    public float lookHeight = 1.6f;

    [Tooltip("Position smoothing. Higher = the camera stays glued tighter behind the player.")]
    public float moveLerp = 9f;
    [Tooltip("Aim smoothing for where the camera points.")]
    public float aimLerp = 11f;
    [Tooltip("Forward mode: how fast the camera swings behind the player on a turn. " +
             "Lower = lazier; higher = snaps behind them.")]
    public float turnLerp = 5f;
    [Tooltip("Frisbee mode: mouse turn speed (degrees per pixel of mouse movement).")]
    public float mouseSensitivity = 0.15f;

    public Mode mode = Mode.Forward;

    Vector3 rigForward = Vector3.forward;   // smoothed 'behind' direction (the way we chase)
    float yaw;                               // frisbee-mode aim heading, in degrees
    Transform look;                          // smoothed look-at point
    bool snapped;                            // first frame jumps straight to the pose

    void Start()
    {
        look = new GameObject("CamLook").transform;
        yaw = Mathf.Atan2(rigForward.x, rigForward.z) * Mathf.Rad2Deg;
    }

    void LateUpdate()
    {
        var mm = MatchManager.I;
        if (mm == null) return;

        // V toggles forward (auto chase) <-> frisbee (mouse aim).
        var kb = Keyboard.current;
        if (kb != null && kb.vKey.wasPressedThisFrame) ToggleMode();

        Transform target = mm.Controlled != null ? mm.Controlled.transform
                                                  : mm.disc.transform;

        if (mode == Mode.Frisbee)
        {
            // Mouse turns the aim (yaw only — loft is automatic from throw power).
            var mouse = Mouse.current;
            if (mouse != null) yaw += mouse.delta.ReadValue().x * mouseSensitivity;
            rigForward = new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f,
                                     Mathf.Cos(yaw * Mathf.Deg2Rad));
        }
        else if (mm.Controlled != null)
        {
            // Ease the chase direction toward the player's facing so the camera arcs
            // around behind them rather than whipping.
            Vector3 f = mm.Controlled.transform.forward; f.y = 0f;
            if (f.sqrMagnitude > 1e-4f)
                rigForward = Vector3.Slerp(rigForward, f.normalized,
                                           turnLerp * Time.deltaTime).normalized;
        }

        // Pull back + up while holding the disc so the handler can read the field. The
        // position lerp below eases the zoom, so it's a smooth dolly, not a snap.
        bool handler = mm.Controlled != null && mm.Controlled.HasDisc;
        float dist = handler ? handlerDistance : distance;
        float h    = handler ? handlerHeight   : height;

        Vector3 basePos   = target.position;
        Vector3 desired   = basePos + Vector3.up * h - rigForward * dist;
        Vector3 lookPoint = basePos + Vector3.up * lookHeight + rigForward * lookAhead;

        if (!snapped)   // avoid a swoop in from wherever the camera was placed
        {
            transform.position = desired;
            look.position = lookPoint;
            transform.rotation = Quaternion.LookRotation(look.position - transform.position);
            snapped = true;
            return;
        }

        transform.position = Vector3.Lerp(transform.position, desired, moveLerp * Time.deltaTime);
        look.position = Vector3.Lerp(look.position, lookPoint, aimLerp * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.LookRotation(look.position - transform.position),
            aimLerp * Time.deltaTime);
    }

    void ToggleMode()
    {
        if (mode == Mode.Forward)
        {
            mode = Mode.Frisbee;
            yaw = Mathf.Atan2(rigForward.x, rigForward.z) * Mathf.Rad2Deg;  // start where we are
            Cursor.lockState = CursorLockMode.Locked;   // smooth mouse-look (Esc frees it in-editor)
        }
        else
        {
            mode = Mode.Forward;
            Cursor.lockState = CursorLockMode.None;
        }
    }
}
