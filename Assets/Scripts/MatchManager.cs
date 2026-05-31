using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns match state: rosters, possession, score, and all the rules events
/// (catch, drop, out-of-bounds, score). Also decides which player the human
/// controls — you always control the most relevant player on your team.
/// </summary>
public class MatchManager : MonoBehaviour
{
    public static MatchManager I { get; private set; }

    public Field field;
    public Disc disc;

    public readonly List<Player> players = new List<Player>();
    public readonly List<Player> home = new List<Player>();
    public readonly List<Player> away = new List<Player>();

    public Team possession = Team.Home;
    public Team humanTeam  = Team.Home;
    public int scoreHome, scoreAway;

    /// <summary>The player the human is steering right now.</summary>
    public Player Controlled { get; private set; }

    public float stallSeconds = 8f;   // how long a holder may keep the disc
    float stallTimer;
    bool pointLive = true;

    public string statusLine = "";

    bool started;

    void Awake() { I = this; }

    void Start()
    {
        // resolve references from the scene if they weren't wired in the Inspector
        if (field == null) field = FindFirstObjectByType<Field>();
        if (disc  == null) disc  = FindFirstObjectByType<Disc>();

        // gather every player that isn't already registered
        if (players.Count == 0)
            foreach (var p in FindObjectsByType<Player>(FindObjectsSortMode.None))
                Register(p);

        // kick off the first point (unless a bootstrap already did)
        if (!started && field != null && disc != null && players.Count > 0)
            SetupPoint(humanTeam);
    }

    public void Register(Player p)
    {
        if (players.Contains(p)) return;
        players.Add(p);
        (p.team == Team.Home ? home : away).Add(p);
    }

    public List<Player> TeamList(Team t) => t == Team.Home ? home : away;
    public Team Other(Team t) => t == Team.Home ? Team.Away : Team.Home;

    void Update()
    {
        UpdateControlledPlayer();
        UpdateHighlight();

        if (pointLive && disc.state == Disc.State.Held && disc.Holder != null)
        {
            stallTimer -= Time.deltaTime;
            if (stallTimer <= 0f)
            {
                // stall: turnover on the spot
                statusLine = "STALL — turnover!";
                Turnover(disc.Holder.transform.position);
            }
        }
    }

    // ---- Who does the human control? -------------------------------------

    void UpdateControlledPlayer()
    {
        var mine = TeamList(humanTeam);
        Player pick;

        if (disc.Holder != null && disc.Holder.team == humanTeam)
        {
            pick = disc.Holder;                       // you have it: you throw
        }
        else
        {
            // otherwise control whoever on your team is nearest the disc
            Vector3 dp = disc.transform.position;
            pick = null; float best = float.MaxValue;
            foreach (var p in mine)
            {
                float d = (p.transform.position - dp).sqrMagnitude;
                if (d < best) { best = d; pick = p; }
            }
        }
        Controlled = pick;
    }

    void UpdateHighlight()
    {
        foreach (var p in players) p.SetHighlighted(p == Controlled);
    }

    // ---- Rules events ----------------------------------------------------

    public void GiveDisc(Player p)
    {
        disc.AttachTo(p);
        possession = p.team;
        stallTimer = stallSeconds;
    }

    public void OnCatch(Player catcher, Team throwerTeam)
    {
        if (!pointLive) return;

        if (catcher.team != throwerTeam)
        {
            // interception
            statusLine = $"Intercepted by {catcher.team}!";
            GiveDisc(catcher);
            return;
        }

        // completion to a teammate
        if (field.InAttackingEndZone(catcher.transform.position, catcher.team))
        {
            Score(catcher.team);
            return;
        }
        statusLine = "Completion.";
        GiveDisc(catcher);
    }

    /// <summary>The disc hit the ground untouched — always a turnover. Where the
    /// disc is brought up depends on whether it landed in or out of bounds.</summary>
    public void OnDiscLanded(Vector3 pos)
    {
        if (!pointLive) return;
        if (field.InBounds(pos))
        {
            statusLine = "Disc down — turnover.";
            Turnover(pos);                       // play it where it lies
        }
        else
        {
            statusLine = "Out of bounds — turnover.";
            // Out the side → perpendicular to the sideline; out the back → the
            // goal line of the end zone the disc sailed past.
            Turnover(field.BringInBounds(pos));
        }
    }

    void Turnover(Vector3 pos)
    {
        Team next = Other(possession);
        possession = next;
        disc.Drop(pos);
        // nearest player of the new team picks it up
        Player nearest = null; float best = float.MaxValue;
        foreach (var p in TeamList(next))
        {
            float d = (p.transform.position - pos).sqrMagnitude;
            if (d < best) { best = d; nearest = p; }
        }
        if (nearest != null) GiveDisc(nearest);
    }

    void Score(Team t)
    {
        if (t == Team.Home) scoreHome++; else scoreAway++;
        statusLine = $"GOAL — {t}!  {scoreHome} : {scoreAway}";
        pointLive = false;
        StartCoroutine(ResetPointAfter(2.0f, Other(t)));   // scored-on team pulls
    }

    IEnumerator ResetPointAfter(float delay, Team offense)
    {
        yield return new WaitForSeconds(delay);
        SetupPoint(offense);
    }

    /// <summary>Line both teams up and hand the disc to the offense.</summary>
    public void SetupPoint(Team offense)
    {
        started = true;
        Team defense = Other(offense);
        LineUp(offense, isOffense: true);
        LineUp(defense, isOffense: false);

        // holder = the middle offensive player
        var off = TeamList(offense);
        Player holder = off[off.Count / 2];
        GiveDisc(holder);

        possession = offense;
        pointLive = true;
        statusLine = $"{offense} on offense.";
    }

    void LineUp(Team t, bool isOffense)
    {
        var list = TeamList(t);
        float dir = field.AttackDir(t);
        // offense starts ~22m back from center toward its own end, defense ~9m
        float z = -dir * (isOffense ? 22f : 9f);
        int n = list.Count;
        for (int i = 0; i < n; i++)
        {
            float t01 = n == 1 ? 0.5f : i / (float)(n - 1);
            float x = Mathf.Lerp(-field.HalfWidth * 0.7f, field.HalfWidth * 0.7f, t01);
            list[i].transform.position = new Vector3(x, 1f, z);   // capsule half-height
            list[i].FaceDir(new Vector3(0f, 0f, dir));
        }
    }
}
