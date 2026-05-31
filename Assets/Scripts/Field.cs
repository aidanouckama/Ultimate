using UnityEngine;

/// <summary>Which side a player belongs to.</summary>
public enum Team { Home, Away }

/// <summary>
/// Geometry + rules helpers for the playing field.
/// The field is centered on the world origin. The long axis is Z, the
/// sideline-to-sideline axis is X. Home attacks +Z, Away attacks -Z.
/// </summary>
public class Field : MonoBehaviour
{
    [Tooltip("Total length including both end zones (Z axis).")]
    public float length = 100f;
    [Tooltip("Sideline to sideline (X axis).")]
    public float width = 37f;
    [Tooltip("Depth of each end zone, measured in from the back line.")]
    public float endZoneDepth = 18f;

    public float HalfLength => length * 0.5f;
    public float HalfWidth  => width  * 0.5f;

    /// <summary>+1 if the team attacks +Z, -1 if it attacks -Z.</summary>
    public float AttackDir(Team t) => t == Team.Home ? 1f : -1f;

    /// <summary>Is the point between the sidelines and back lines?</summary>
    public bool InBounds(Vector3 p)
        => Mathf.Abs(p.x) <= HalfWidth && Mathf.Abs(p.z) <= HalfLength;

    /// <summary>Is the point inside the end zone the given team is attacking?</summary>
    public bool InAttackingEndZone(Vector3 p, Team attacker)
    {
        if (Mathf.Abs(p.x) > HalfWidth) return false;
        float backLine = HalfLength * AttackDir(attacker);
        return Mathf.Abs(p.z - backLine) <= endZoneDepth;
    }

    /// <summary>Closest in-bounds point to p (used when the disc sails out).</summary>
    public Vector3 ClampInBounds(Vector3 p)
    {
        p.x = Mathf.Clamp(p.x, -HalfWidth, HalfWidth);
        p.z = Mathf.Clamp(p.z, -HalfLength, HalfLength);
        return p;
    }
}
