using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Behind-the-shoulder third-person sports camera, modelled on Cinemachine's
/// ThreePersonFollow / over-the-shoulder rig (origin → shoulder offset → camera at a
/// distance, looking at the shoulder) plus a vertical aim orbit:
///
///  - SHOULDER OFFSET: the look anchor sits off to one side of the player, so the player is
///    framed to the side ("over the shoulder") and the field you're throwing into fills the
///    rest — the classic sports/aiming framing.
///  - AIM ORBIT: mouse Y tilts the aim; the camera orbits the shoulder (lower as you aim up,
///    higher as you aim down) so you get full high-lob / low-drive freedom while the player
///    stays in frame. Mouse X turns the heading.
///  - CRITICALLY-DAMPED FOLLOW: position uses SmoothDamp (a critically-damped spring), so the
///    follow is smooth and frame-rate independent — eases in, never overshoots or jitters.
///
/// Throws read this: yaw = direction, AimPitch = loft. Pulls back while holding the disc to
/// read the field, falls back to the disc when nobody is controlled, and anchors on the pivot
/// foot during a wind-up so the step-out reads on screen.
/// </summary>
public class CameraRig : MonoBehaviour
{
    [Tooltip("How far from the player the camera orbits.")]
    public float distance = 7f;

    [Header("Handler (holding the disc)")]
    [Tooltip("Pull back to this orbit distance while the controlled player has the disc, to survey the field.")]
    public float handlerDistance = 13f;

    [Header("Over-the-shoulder framing")]
    [Tooltip("Lateral shoulder offset (metres at the base distance). Pushes the player to one " +
             "side of the frame so you look over their shoulder into the field. Positive = " +
             "player sits on the left. Scaled with zoom so the framing stays put as a handler.")]
    public float shoulderOffset = 2.6f;
    [Tooltip("Raise the shoulder/look anchor this far above the pivot (metres).")]
    public float shoulderHeight = 0.25f;

    [Header("Orbit")]
    [Tooltip("Height of the orbit pivot above the player's feet — the point kept in frame (~chest).")]
    public float pivotHeight = 1.4f;
    [Tooltip("Resting camera elevation above the pivot, in degrees (how far above the player it sits when aiming level).")]
    public float restElevation = 16f;
    [Tooltip("How many degrees the camera elevation drops per degree of aim-up. Higher = the view " +
             "swings up more as you aim a lob (camera dips lower/behind).")]
    public float aimFollow = 0.5f;
    [Tooltip("Highest the camera orbits above the pivot (degrees). Stops it going straight overhead.")]
    public float maxElevation = 35f;
    [Tooltip("Keep the camera at least this far above the ground when it dips low for a skyward aim.")]
    public float groundClearance = 0.6f;
    [Tooltip("Raise the look point this many metres per degree of aim-up, so the player drops a touch " +
             "low in frame and the arc/sky come into view on a lob.")]
    public float lookUpFollow = 0.03f;
    [Tooltip("Look this far ahead of the player (downfield), so you can read where you're throwing.")]
    public float lookForward = 2.5f;

    [Header("Feel")]
    [Tooltip("Follow smoothing time (seconds) for the critically-damped position spring. Lower = " +
             "tighter/snappier, higher = floatier. ~0.1-0.2 is a natural sports-cam feel.")]
    public float followDamping = 0.14f;
    [Tooltip("Aim smoothing rate for where the camera points (higher = snappier).")]
    public float aimLerp = 11f;
    [Tooltip("Mouse turn speed (degrees per pixel of horizontal mouse movement).")]
    public float mouseSensitivity = 0.15f;
    [Tooltip("Mouse up/down tilt speed (degrees per pixel). The aim pitch this sets becomes the " +
             "throw's loft — look up to skyball, flat/down to drive it.")]
    public float pitchSensitivity = 0.12f;
    [Tooltip("Lowest aim elevation (degrees). Negative drives the disc down for a low throw.")]
    public float minAimPitch = -15f;
    [Tooltip("Highest aim elevation (degrees) — a steep skyball / hammer arc.")]
    public float maxAimPitch = 55f;

    Vector3 rigForward = Vector3.forward;   // heading direction, from yaw
    float yaw;                               // aim heading, in degrees
    float aimPitch = 0f;                     // aim elevation, degrees (mouse Y); 0 = level
    Transform look;                          // smoothed look-at point
    Vector3 posVel, lookVel;                 // SmoothDamp velocities (critically-damped springs)
    bool snapped;                            // first frame jumps straight to the pose

    /// <summary>Current aim elevation in degrees (set by mouse Y). The thrower reads this
    /// for loft: up = skyball, flat/down = a drive.</summary>
    public float AimPitch => aimPitch;

    void Start()
    {
        look = new GameObject("CamLook").transform;
        yaw = Mathf.Atan2(rigForward.x, rigForward.z) * Mathf.Rad2Deg;
        Cursor.lockState = CursorLockMode.Locked;   // smooth mouse-look (Esc frees it in-editor)
    }

    void LateUpdate()
    {
        var mm = MatchManager.I;
        if (mm == null) return;

        Transform target = mm.Controlled != null ? mm.Controlled.transform
                                                  : mm.disc.transform;

        // Mouse turns the aim: X = heading (yaw), Y = elevation (pitch → throw loft).
        var mouse = Mouse.current;
        if (mouse != null)
        {
            Vector2 d = mouse.delta.ReadValue();
            yaw += d.x * mouseSensitivity;
            aimPitch = Mathf.Clamp(aimPitch + d.y * pitchSensitivity, minAimPitch, maxAimPitch);
        }
        rigForward = new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f,
                                 Mathf.Cos(yaw * Mathf.Deg2Rad));
        Vector3 right = new Vector3(Mathf.Cos(yaw * Mathf.Deg2Rad), 0f, -Mathf.Sin(yaw * Mathf.Deg2Rad));

        // Pull back while holding the disc so the handler can read the field (eased by the spring).
        bool handler = mm.Controlled != null && mm.Controlled.HasDisc;
        float dist = handler ? handlerDistance : distance;

        // While winding up a throw, orbit the pivot foot (not the body) so the step-out reads
        // on screen instead of the camera tracking it away.
        Vector3 basePos = (mm.Controlled != null && mm.Controlled.Winding)
            ? mm.Controlled.WindPivot
            : target.position;
        Vector3 pivot = basePos + Vector3.up * pivotHeight;

        // Over-the-shoulder anchor: shift the orbit/look centre to one side (and up a touch),
        // so the player is framed off-shoulder and the field fills the rest. The camera orbits
        // and looks from THIS point, which puts the player to the opposite side of the screen.
        // Scale the offset with zoom (dist) so the player holds the same screen position whether
        // running in close or pulled back as the handler.
        float shoulderLateral = shoulderOffset * dist / Mathf.Max(distance, 0.1f);
        Vector3 shoulder = pivot + right * shoulderLateral + Vector3.up * shoulderHeight;

        // Orbit elevation: drop the camera as you aim up (view tilts up), raise it as you aim
        // down. Clamp the bottom so the camera dips for a skyward look but never sinks into the
        // ground, and the top so it never swings overhead. Looking from the shoulder keeps the
        // player framed off to the side through the whole high-to-low range.
        float minElevation = Mathf.Asin(Mathf.Clamp(-(pivotHeight - groundClearance) / dist, -1f, 1f))
                           * Mathf.Rad2Deg;
        float elevation = Mathf.Clamp(restElevation - aimPitch * aimFollow, minElevation, maxElevation);
        Vector3 dirToCam = Quaternion.AngleAxis(elevation, right) * -rigForward;   // shoulder → camera

        Vector3 desired   = shoulder + dirToCam * dist;
        Vector3 lookPoint = shoulder + Vector3.up * (aimPitch * lookUpFollow) + rigForward * lookForward;

        if (!snapped)   // avoid a swoop in from wherever the camera was placed
        {
            transform.position = desired;
            look.position = lookPoint;
            transform.rotation = Quaternion.LookRotation(look.position - transform.position);
            posVel = lookVel = Vector3.zero;
            snapped = true;
            return;
        }

        // Critically-damped spring follow (SmoothDamp) — smooth, frame-rate independent, no
        // overshoot. Rotation eases with an exponential (also frame-rate independent).
        transform.position = Vector3.SmoothDamp(transform.position, desired, ref posVel, followDamping);
        look.position      = Vector3.SmoothDamp(look.position, lookPoint, ref lookVel, followDamping * 0.6f);
        float t = 1f - Mathf.Exp(-aimLerp * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.LookRotation(look.position - transform.position),
            t);
    }
}
