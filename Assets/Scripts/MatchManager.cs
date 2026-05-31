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

    [Tooltip("The single player the human always controls (the one with the white " +
             "ring). Auto-picked from the human team if left unset. Control never " +
             "switches — no more flicker when two players tie for nearest.")]
    public Player humanPlayer;

    public float stallSeconds = 8f;   // how long a holder may keep the disc
    float stallTimer;
    bool pointLive = true;

    [Tooltip("How close a player must get to a loose disc on the ground to pick it up.")]
    public float pickupRadius = 1.6f;

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

        // pick the one player the human will always control (a central handler)
        if (humanPlayer == null)
        {
            var mine = TeamList(humanTeam);
            if (mine.Count > 0) humanPlayer = mine[mine.Count / 2];
        }

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

        // a loose disc on the ground gets picked up by the team in possession
        if (pointLive && disc.state == Disc.State.Loose)
            TryPickUpLoose();
    }

    /// <summary>If a player on the possessing team is standing over the loose disc,
    /// they pick it up. Only the team that earned possession can claim it.</summary>
    void TryPickUpLoose()
    {
        Vector3 dp = disc.transform.position; dp.y = 0f;
        Player best = null; float bd = pickupRadius * pickupRadius;
        foreach (var p in TeamList(possession))
        {
            Vector3 pp = p.transform.position; pp.y = 0f;
            float d = (pp - dp).sqrMagnitude;
            if (d <= bd) { bd = d; best = p; }
        }
        if (best != null) GiveDisc(best);
    }

    // ---- Who does the human control? -------------------------------------

    void UpdateControlledPlayer()
    {
        // The human always steers one fixed player (the white-ringed one). Control
        // never switches to "nearest the disc" — that flickered when two players
        // tied. You run your own player to cut, catch, and throw.
        if (humanPlayer == null)
        {
            var mine = TeamList(humanTeam);
            if (mine.Count > 0) humanPlayer = mine[mine.Count / 2];
        }
        Controlled = humanPlayer;
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

    /// <summary>The disc hit the ground untouched — always a turnover. TurnoverSpot
    /// places it: where it lies in the field, on the sideline if out the side, or on
    /// the goal line if it landed in / past an end zone.</summary>
    public void OnDiscLanded(Vector3 pos)
    {
        if (!pointLive) return;
        statusLine = field.InBounds(pos) ? "Disc down — turnover." : "Out of bounds — turnover.";
        Turnover(field.TurnoverSpot(pos));
    }

    void Turnover(Vector3 pos)
    {
        possession = Other(possession);
        // The disc lies on the ground at the spot; the new offense has to run over
        // and pick it up (see TryPickUpLoose). No teleport — possession just flips.
        disc.Drop(pos);
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

        // On offense the human starts with the disc (their fixed player handles);
        // otherwise the middle offensive player (an AI) does.
        var off = TeamList(offense);
        Player holder = (offense == humanTeam && humanPlayer != null && off.Contains(humanPlayer))
            ? humanPlayer
            : off[off.Count / 2];
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
            list[i].PlaceAtGround(new Vector3(x, 0f, z));
            list[i].FaceDir(new Vector3(0f, 0f, dir));
        }
    }
}
