using UnityEngine;

/// <summary>
/// A high, angled sports-broadcast camera that follows the action (the disc and
/// the controlled player) from behind the human team's end.
/// </summary>
public class CameraRig : MonoBehaviour
{
    public Vector3 offset = new Vector3(0f, 30f, -26f);
    public float followLerp = 4f;
    public float lookLerp = 6f;

    Transform look;   // a smoothed look-at point

    void Start()
    {
        look = new GameObject("CamLook").transform;
        look.position = Vector3.zero;
    }

    void LateUpdate()
    {
        var mm = MatchManager.I;
        if (mm == null) return;

        // focus = midpoint between the disc and the controlled player
        Vector3 focus = mm.disc.transform.position;
        if (mm.Controlled != null)
            focus = (focus + mm.Controlled.transform.position) * 0.5f;
        focus.y = 0f;

        Vector3 desired = focus + offset;
        transform.position = Vector3.Lerp(transform.position, desired,
                                          followLerp * Time.deltaTime);

        look.position = Vector3.Lerp(look.position, focus, lookLerp * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.LookRotation(look.position - transform.position),
            lookLerp * Time.deltaTime);
    }
}
