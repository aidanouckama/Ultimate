using UnityEngine;

/// <summary>
/// Drives a single player when the human is NOT controlling it.
/// Behavior depends on whether the player's team has the disc:
///  - Offense, holder : look for an open teammate and throw a lead pass.
///  - Offense, cutter : run to open space toward the attacking end zone.
///  - Defense         : mark the nearest attacker / chase a loose disc.
/// </summary>
[RequireComponent(typeof(Player))]
public class AIController : MonoBehaviour
{
    [Header("Throwing judgment")]
    [Tooltip("Don't throw if a defender is within this distance of the passing lane.")]
    public float laneBlockRadius = 2.8f;
    [Tooltip("The receiver must have at least this much separation from defenders.")]
    public float minReceiverOpenness = 2.6f;

    Player me;
    float decisionTimer;
    float holdThinkTimer;

    void Awake() { me = GetComponent<Player>(); }

    void Update()
    {
        var mm = MatchManager.I;
        if (mm == null) return;
        if (mm.Controlled == me) return;   // human is driving this one

        bool myTeamHasIt = mm.disc.Holder != null && mm.disc.Holder.team == me.team;

        if (me.HasDisc)            HandleHolder(mm);
        else if (myTeamHasIt)      HandleCutter(mm);
        else                       HandleDefense(mm);
    }

    // --- with the disc ----------------------------------------------------

    void HandleHolder(MatchManager mm)
    {
        me.FaceDir(new Vector3(0f, 0f, mm.field.AttackDir(me.team)));
        holdThinkTimer -= Time.deltaTime;
        if (holdThinkTimer > 0f) return;
        holdThinkTimer = 0.35f;

        Player target = BestReceiver(mm, out Vector3 lead);
        if (target == null) return;

        // throw a lead pass to where the receiver is heading
        Vector3 from = mm.disc.transform.position;
        Vector3 flat = lead - from; flat.y = 0f;
        float dist = flat.magnitude;
        if (dist < 1f) return;

        Vector3 dir = flat.normalized;
        float speed = Mathf.Clamp(dist * 1.15f, 12f, 26f);
        float loft = Mathf.Clamp(dist * 0.18f, 2.5f, 6.5f);
        Vector3 vel = dir * speed + Vector3.up * loft;

        // throw straight — the AI aims for a clear lane, so a random curve would
        // only bend the disc off its intended (lane-checked) target.
        mm.disc.Throw(vel, me.team, 0f);
    }

    /// <summary>Pick the best teammate to throw to: open at the catch point AND
    /// reachable through a clear lane. Returns null if nothing is safe (hold the
    /// disc rather than turf it into a defender).</summary>
    Player BestReceiver(MatchManager mm, out Vector3 lead)
    {
        lead = Vector3.zero;
        Player best = null;
        float bestScore = 0f;
        float dir = mm.field.AttackDir(me.team);
        Vector3 from = mm.disc.transform.position;

        foreach (var mate in mm.TeamList(me.team))
        {
            if (mate == me) continue;

            // lead the pass to where the receiver is cutting (their aiTarget),
            // capped a few metres ahead; fall back to a step downfield if idle
            Vector3 toCut = mate.aiTarget - mate.transform.position;
            Vector3 predicted = toCut.sqrMagnitude > 1f
                ? mate.transform.position + Vector3.ClampMagnitude(toCut, 6f)
                : mate.transform.position + new Vector3(0f, 0f, dir) * 4f;
            predicted = mm.field.ClampInBounds(predicted);

            // GATE 1: a defender sitting in the passing lane would pick it off
            float laneClear = LaneClearance(mm, from, predicted, me.team);
            if (laneClear < laneBlockRadius) continue;

            // GATE 2: the receiver has to actually be open where they'll catch it
            float openness = Openness(mm, predicted, me.team);
            if (openness < minReceiverOpenness) continue;

            // among safe options, prefer open + downfield + a wide-open lane
            float progress = (predicted.z - me.transform.position.z) * dir;
            float score = openness
                        + Mathf.Clamp(progress, -8f, 18f) * 0.08f
                        + laneClear * 0.3f;

            if (score > bestScore)
            {
                bestScore = score;
                best = mate;
                lead = predicted;
            }
        }
        return best;
    }

    /// <summary>How close the nearest defender gets to the straight passing lane
    /// from `from` to `to` (measured on the ground plane, since a flat-ish disc
    /// can be picked off from below). Small = the lane is contested.</summary>
    static float LaneClearance(MatchManager mm, Vector3 from, Vector3 to, Team team)
    {
        Vector3 a = from; a.y = 0f;
        Vector3 b = to;   b.y = 0f;
        float nearest = float.MaxValue;
        foreach (var d in mm.TeamList(mm.Other(team)))
        {
            Vector3 p = d.transform.position; p.y = 0f;
            float dist = DistPointToSegment(p, a, b);
            if (dist < nearest) nearest = dist;
        }
        return nearest;
    }

    static float DistPointToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-4f) return (p - a).magnitude;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
        return (p - (a + ab * t)).magnitude;
    }

    // --- offense without the disc ----------------------------------------

    void HandleCutter(MatchManager mm)
    {
        decisionTimer -= Time.deltaTime;
        if (decisionTimer <= 0f || ReachedTarget())
        {
            decisionTimer = Random.Range(0.8f, 1.6f);
            me.aiTarget = PickCut(mm);
        }
        me.MoveToward(me.aiTarget);
    }

    Vector3 PickCut(MatchManager mm)
    {
        float dir = mm.field.AttackDir(me.team);
        Vector3 p = me.transform.position;

        // bias downfield, with a lateral juke, sampling a few spots for openness
        Vector3 best = p; float bestOpen = -1f;
        for (int i = 0; i < 6; i++)
        {
            Vector3 cand = p + new Vector3(
                Random.Range(-14f, 14f),
                0f,
                dir * Random.Range(4f, 16f));
            cand = mm.field.ClampInBounds(cand);
            float open = Openness(mm, cand, me.team);
            if (open > bestOpen) { bestOpen = open; best = cand; }
        }
        return best;
    }

    // --- defense ----------------------------------------------------------

    void HandleDefense(MatchManager mm)
    {
        // chase a loose disc directly
        if (mm.disc.state == Disc.State.Loose)
        {
            me.MoveToward(mm.disc.transform.position, 1.05f);
            return;
        }

        Player mark = NearestOpponent(mm);
        if (mark == null) return;

        // stand between your mark and the end zone they're attacking
        float oppDir = mm.field.AttackDir(mark.team);
        Vector3 goalSide = mark.transform.position + new Vector3(0f, 0f, oppDir * 2.5f);
        me.MoveToward(goalSide, 1.0f);
    }

    Player NearestOpponent(MatchManager mm)
    {
        Player best = null; float bd = float.MaxValue;
        foreach (var o in mm.TeamList(mm.Other(me.team)))
        {
            float d = (o.transform.position - me.transform.position).sqrMagnitude;
            if (d < bd) { bd = d; best = o; }
        }
        return best;
    }

    // --- shared helpers ---------------------------------------------------

    /// <summary>How open is a spot? Distance to the nearest defender of `team`.
    /// Bigger = more open.</summary>
    static float Openness(MatchManager mm, Vector3 spot, Team team)
    {
        float nearest = float.MaxValue;
        foreach (var d in mm.TeamList(mm.Other(team)))
        {
            float dist = (d.transform.position - spot).magnitude;
            if (dist < nearest) nearest = dist;
        }
        return nearest;
    }

    bool ReachedTarget()
        => (me.transform.position - me.aiTarget).sqrMagnitude < 1.5f;
}
