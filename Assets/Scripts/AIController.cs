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

        float spin = Random.Range(-0.4f, 0.4f);
        mm.disc.Throw(vel, me.team, spin);
    }

    /// <summary>Pick the most open teammate, returning a lead point.</summary>
    Player BestReceiver(MatchManager mm, out Vector3 lead)
    {
        lead = Vector3.zero;
        Player best = null;
        float bestScore = 0.2f;   // minimum openness to bother throwing
        float dir = mm.field.AttackDir(me.team);

        foreach (var mate in mm.TeamList(me.team))
        {
            if (mate == me) continue;
            Vector3 predicted = mate.transform.position +
                                new Vector3(0f, 0f, dir) * 4f;     // lead downfield
            float openness = Openness(mm, predicted, me.team);
            float progress = (predicted.z - me.transform.position.z) * dir; // reward forward
            float score = openness + Mathf.Clamp(progress, -8f, 14f) * 0.05f;

            if (score > bestScore)
            {
                bestScore = score;
                best = mate;
                lead = predicted;
            }
        }
        return best;
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
