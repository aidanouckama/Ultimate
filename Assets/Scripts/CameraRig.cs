using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Third-person mouse-look camera. The mouse turns the view — X = heading (yaw),
/// Y = elevation (pitch) — and the camera orbits behind + above the controlled player,
/// looking ahead along that aim. You move with WASD relative to the view, and throws go
/// where the camera looks (yaw = direction, pitch = loft). Pulls back while holding the
/// disc so the handler can read the field, and falls back to the disc when nobody is
/// controlled (between points). Motion is smoothed so it eases rather than snaps.
/// </summary>
public class CameraRig : MonoBehaviour
{
    [Tooltip("How far behind the player the camera sits.")]
    public float distance = 7f;
    [Tooltip("How high above the player the camera sits.")]
    public float height = 3.2f;

    [Header("Handler (holding the disc)")]
    [Tooltip("Pull back this far while the controlled player has the disc, to survey the field.")]
    public float handlerDistance = 13f;
    [Tooltip("And rise to this height while holding the disc.")]
    public float handlerHeight = 6.5f;

    [Tooltip("Look this far ahead of the player, along the aim direction.")]
    public float lookAhead = 6f;
    [Tooltip("Height of the look-at point above the player's feet (roughly head height).")]
    public float lookHeight = 1.6f;

    [Tooltip("Position smoothing. Higher = the camera stays glued tighter behind the player.")]
    public float moveLerp = 9f;
    [Tooltip("Aim smoothing for where the camera points.")]
    public float aimLerp = 11f;
    [Tooltip("Mouse turn speed (degrees per pixel of horizontal mouse movement).")]
    public float mouseSensitivity = 0.15f;
    [Tooltip("Mouse up/down tilt speed (degrees per pixel). The aim pitch this sets becomes " +
             "the throw's loft — look up to skyball, flat/down to zing it.")]
    public float pitchSensitivity = 0.12f;
    [Tooltip("Lowest aim elevation (degrees). Slightly negative lets you drive a flat zinger.")]
    public float minAimPitch = -8f;
    [Tooltip("Highest aim elevation (degrees) — a steep skyball / hammer arc.")]
    public float maxAimPitch = 55f;

    Vector3 rigForward = Vector3.forward;   // 'behind' direction (the way we chase), from yaw
    float yaw;                               // aim heading, in degrees
    float aimPitch = 0f;                     // aim elevation, degrees (mouse Y); 0 = level laser
    Transform look;                          // smoothed look-at point
    bool snapped;                            // first frame jumps straight to the pose

    /// <summary>Current aim elevation in degrees (set by mouse Y). The thrower reads this
    /// for loft: up = skyball, flat/down = a zinger.</summary>
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

        // Pull back + up while holding the disc so the handler can read the field. The
        // position lerp below eases the zoom, so it's a smooth dolly, not a snap.
        bool handler = mm.Controlled != null && mm.Controlled.HasDisc;
        float dist = handler ? handlerDistance : distance;
        float h    = handler ? handlerHeight   : height;

        // While winding up a throw, anchor on the pivot foot (not the body) so the
        // step-out actually reads on screen instead of the camera tracking it away.
        Vector3 basePos = (mm.Controlled != null && mm.Controlled.Winding)
            ? mm.Controlled.WindPivot
            : target.position;
        Vector3 desired   = basePos + Vector3.up * h - rigForward * dist;
        Vector3 lookPoint = basePos + Vector3.up * lookHeight + rigForward * lookAhead
                          + Vector3.up * Mathf.Tan(aimPitch * Mathf.Deg2Rad) * lookAhead;

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
}
