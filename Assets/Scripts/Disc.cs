using UnityEngine;

/// <summary>
/// The disc. Three states:
///  - Held:   parented to a player's hand, kinematic.
///  - Flying: free, integrated with simple aerodynamics (gravity, drag, lift).
///  - Loose:  resting on the ground waiting to be picked up.
/// All flight is script-driven; the disc has no collider, so it never fights PhysX.
/// </summary>
public class Disc : MonoBehaviour
{
    public enum State { Held, Flying, Loose }

    [Header("Flight model (tweak for feel)")]
    [Tooltip("Downward acceleration. Real gravity is 9.81; lower = floatier.")]
    public float gravity = 11f;
    [Tooltip("Quadratic drag. Higher = the disc decelerates faster.")]
    public float drag = 0.012f;
    [Tooltip("Glide lift from forward speed. Tune so a fast disc sails, then sinks.")]
    public float lift = 0.030f;
    [Tooltip("Lift is capped at this fraction of gravity, so a fast disc sails nearly level " +
             "but never CLIMBS — throwing harder lasers it farther, not higher. Below 1 keeps " +
             "a slight net descent so it always comes down.")]
    [Range(0f, 1f)] public float maxLiftRatio = 0.85f;
    [Tooltip("Curve strength. The disc carves perpendicular to its travel, so this " +
             "bends the whole flight path (a banana), not just the launch direction.")]
    public float curl = 1.0f;
    [Tooltip("How fast spin bleeds off in flight. Low = the curve sustains and breaks " +
             "late (inside-out / outside-in); high = a quick early bend then straight.")]
    public float spinDecay = 0.06f;

    [Header("Geometry")]
    public float restHeight = 0.06f;   // disc thickness / 2

    [Header("Contest")]
    [Tooltip("A player who reaches within this margin BEYOND their catch radius doesn't " +
             "catch cleanly — they tip the disc (knock it down), and it stays live.")]
    public float tipMargin = 0.9f;

    public State state = State.Loose;
    public Player Holder { get; private set; }

    /// <summary>The teammate a throw is aimed at (set by the AI). They run to
    /// <see cref="LandingSpot"/> to catch it; null for a human throw.</summary>
    public Player Receiver { get; private set; }
    /// <summary>Where the current throw is predicted to touch down — receivers and
    /// defenders home on this to make the catch / contest the pass.</summary>
    public Vector3 LandingSpot { get; private set; }

    Rigidbody rb;
    Team throwerTeam;
    Player thrower;         // who released this throw — can't catch their own disc
    float graceTimer;       // brief window after release: nobody can catch
    float spin;             // signed spin imparted at throw, decays in flight
    float liftScale = 1f;   // per-throw glide multiplier — low for a hammer (arcs & drops)
    Vector3 curveAccel;     // fixed sideways "gravity" for an arc throw — bows the path out
                            // and back so a curved throw still lands on the aim line

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        // start parked & kinematic; the match hands it to a holder at kickoff
        rb.isKinematic = true;
        state = State.Loose;
    }

    /// <summary>Acceleration applied during flight. Shared with the aim preview
    /// so the predicted line matches real flight exactly. <paramref name="liftMul"/>
    /// scales the glide for the throw type (≈1 for flat throws, low for a hammer that
    /// arcs over and drops instead of sailing).</summary>
    public Vector3 Aero(Vector3 v, float spinNow, float liftMul = 1f, Vector3 curveAcc = default)
    {
        Vector3 a = Vector3.down * gravity;
        a += curveAcc;                                    // arc throw: sideways "gravity" (fixed axis)
        float speed = v.magnitude;
        if (speed > 0.001f)
        {
            a += -drag * speed * v;                       // drag opposes motion
            Vector3 horiz = new Vector3(v.x, 0f, v.z);
            // glide — but capped below gravity so a fast disc sails flat and never climbs
            float liftAcc = lift * liftMul * horiz.magnitude * speed;
            a += Vector3.up * Mathf.Min(liftAcc, gravity * maxLiftRatio);
            // spin curls the disc sideways (perpendicular to travel, in the plane)
            Vector3 side = Vector3.Cross(Vector3.up, horiz.normalized);
            a += side * curl * spinNow * horiz.magnitude;
        }
        return a;
    }

    public void AttachTo(Player p)
    {
        Holder = p;
        Receiver = null;
        state = State.Held;
        rb.isKinematic = true;
        // Deliberately NOT parented. The disc follows the hand point in
        // LateUpdate instead, so it never inherits the player's (0.9) scale —
        // parenting used to compound that shrink on every catch.
        SnapToHand();
    }

    void SnapToHand()
    {
        if (Holder == null || Holder.HandPoint == null) return;
        transform.position = Holder.HandPoint.position;
        transform.rotation = Holder.HandPoint.rotation;
    }

    /// <summary>While held, ride the holder's hand (after they've moved).</summary>
    void LateUpdate()
    {
        if (state == State.Held) SnapToHand();
    }

    public void Throw(Vector3 velocity, Team team, float spinAmount, Player receiver = null,
                      float liftMul = 1f, Vector3 curve = default)
    {
        thrower = Holder;           // remember who let it go, before clearing
        Holder = null;
        Receiver = receiver;
        throwerTeam = team;
        spin = spinAmount;
        liftScale = liftMul;
        curveAccel = curve;
        state = State.Flying;
        graceTimer = 0.18f;
        rb.isKinematic = false;
        rb.linearVelocity = velocity;
        LandingSpot = PredictLanding(transform.position, velocity, spinAmount);
    }

    /// <summary>Simulate the flight with the disc's own model to find where it lands.
    /// Receivers/defenders home on this so throws connect (or get contested).</summary>
    public Vector3 PredictLanding(Vector3 from, Vector3 v, float spin0)
    {
        Vector3 p = from; Vector3 vel = v; float s = spin0;
        const float dt = 0.04f;
        for (int i = 0; i < 250; i++)       // up to 10s of flight
        {
            vel += Aero(vel, s, liftScale, curveAccel) * dt;
            p += vel * dt;
            s = Mathf.MoveTowards(s, 0f, dt * spinDecay);
            if (p.y <= restHeight) break;
        }
        p.y = restHeight;
        return p;
    }

    /// <summary>Place the disc on the ground, free for anyone to pick up.</summary>
    public void Drop(Vector3 pos)
    {
        Holder = null;
        Receiver = null;
        state = State.Loose;
        spin = 0f;
        curveAccel = Vector3.zero;
        rb.isKinematic = true;
        pos.y = restHeight;
        transform.position = pos;
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
    }

    void FixedUpdate()
    {
        if (state != State.Flying) return;

        if (graceTimer > 0f) graceTimer -= Time.fixedDeltaTime;
        spin = Mathf.MoveTowards(spin, 0f, Time.fixedDeltaTime * spinDecay);

        rb.AddForce(Aero(rb.linearVelocity, spin, liftScale, curveAccel), ForceMode.Acceleration);

        // spin the mesh for visual flair
        transform.Rotate(Vector3.up, 720f * Time.fixedDeltaTime, Space.World);

        Vector3 p = rb.position;

        // The disc is only dead once it touches the ground. A disc sailing over
        // out-of-bounds territory is still live — it may curl back in or be
        // caught — so we don't fault it mid-air. MatchManager decides in-bounds
        // (place where it lies) vs out (bring to the sideline) at landing.
        if (p.y <= restHeight)
            MatchManager.I.OnDiscLanded(p);
        // someone catches it?
        else if (graceTimer <= 0f)
            TryCatch();
    }

    void TryCatch()
    {
        Player catcher = null; float catchDist = float.MaxValue;
        Player tipper  = null; float tipDist   = float.MaxValue;
        Vector3 dp = rb.position;

        foreach (var pl in MatchManager.I.players)
        {
            if (pl == thrower) continue;         // you can't catch your own throw
            float reach = pl.CatchReach;         // grows while a player is laid out
            Vector3 d = pl.transform.position - dp;
            d.y *= 0.5f;                         // jumping/extension is cheap vertically
            float dist = d.magnitude;

            if (dist <= reach)                   // clean catch range
            {
                if (dist < catchDist) { catchDist = dist; catcher = pl; }
            }
            else if (dist <= reach + tipMargin)  // just out of control — a tip
            {
                if (dist < tipDist) { tipDist = dist; tipper = pl; }
            }
        }

        if (catcher != null)
            MatchManager.I.OnCatch(catcher, throwerTeam);
        else if (tipper != null)
            Tip(tipper);
    }

    /// <summary>A player got fingers on the disc but couldn't reel it in: knock it down —
    /// kill most of its speed, pop it off their hand and toward the ground. It stays
    /// flying (now live for everyone, including the original thrower) so it can be run
    /// down, re-caught, or fall for a turnover.</summary>
    void Tip(Player p)
    {
        Vector3 away = rb.position - p.transform.position; away.y = 0f;
        away = away.sqrMagnitude > 1e-3f ? away.normalized : Vector3.forward;

        Vector3 v = rb.linearVelocity * 0.25f;   // big speed loss off the contact
        v += away * 2f;                          // deflects off the hand
        v.y = -2f;                               // knocked downward
        rb.linearVelocity = v;

        spin = 0f;
        thrower = null;          // it's been touched — anyone may now play it
        graceTimer = 0.12f;      // brief window so it doesn't re-tip on the same frame
        MatchManager.I.OnTip(p);
    }

    public Team ThrowerTeam => throwerTeam;
}
