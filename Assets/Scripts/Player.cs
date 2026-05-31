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

    Renderer rend;
    Color baseColor;

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
    }

    /// <summary>Move toward a world point this frame, staying upright.</summary>
    public void MoveToward(Vector3 worldTarget, float speedScale = 1f)
    {
        Vector3 to = worldTarget - transform.position;
        to.y = 0f;
        float step = moveSpeed * speedScale * Time.deltaTime;
        if (to.sqrMagnitude > 0.0004f)
        {
            Vector3 dir = to.normalized;
            transform.position += dir * Mathf.Min(step, to.magnitude);
            FaceDir(dir);
        }
    }

    /// <summary>Move along a direction vector this frame.</summary>
    public void MoveDir(Vector3 dir, float speedScale = 1f)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();
        transform.position += dir * moveSpeed * speedScale * Time.deltaTime;
        FaceDir(dir);
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

    /// <summary>Pivot height when standing (capsule half-height). The disc lands at
    /// ground level, so use this to place a player without sinking them into it.</summary>
    public const float StandHeight = 1f;

    /// <summary>Drop the player onto a ground spot, keeping them on their feet —
    /// the spot's Y is ignored in favor of <see cref="StandHeight"/>.</summary>
    public void PlaceAtGround(Vector3 spot)
    {
        transform.position = new Vector3(spot.x, StandHeight, spot.z);
    }

    /// <summary>Brighten the player the human is currently controlling.</summary>
    public void SetHighlighted(bool on)
    {
        if (rend == null) return;
        rend.material.color = on ? Color.Lerp(baseColor, Color.white, 0.55f) : baseColor;
    }

    public bool HasDisc => MatchManager.I.disc.Holder == this;
}
