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
    [Tooltip("Sideways curve from spin (flick disc). 0 = straight.")]
    public float curl = 0.6f;

    [Header("Geometry")]
    public float restHeight = 0.06f;   // disc thickness / 2

    public State state = State.Loose;
    public Player Holder { get; private set; }

    Rigidbody rb;
    Team throwerTeam;
    float graceTimer;       // brief window after release: nobody can catch
    float spin;             // signed spin imparted at throw, decays in flight

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
    /// so the predicted line matches real flight exactly.</summary>
    public Vector3 Aero(Vector3 v, float spinNow)
    {
        Vector3 a = Vector3.down * gravity;
        float speed = v.magnitude;
        if (speed > 0.001f)
        {
            a += -drag * speed * v;                       // drag opposes motion
            Vector3 horiz = new Vector3(v.x, 0f, v.z);
            a += Vector3.up * lift * horiz.magnitude * speed;   // glide
            // spin curls the disc sideways (perpendicular to travel, in the plane)
            Vector3 side = Vector3.Cross(Vector3.up, horiz.normalized);
            a += side * curl * spinNow * horiz.magnitude;
        }
        return a;
    }

    public void AttachTo(Player p)
    {
        Holder = p;
        state = State.Held;
        rb.isKinematic = true;
        transform.SetParent(p.HandPoint, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
    }

    public void Throw(Vector3 velocity, Team team, float spinAmount)
    {
        transform.SetParent(null, true);
        Holder = null;
        throwerTeam = team;
        spin = spinAmount;
        state = State.Flying;
        graceTimer = 0.18f;
        rb.isKinematic = false;
        rb.linearVelocity = velocity;
    }

    /// <summary>Place the disc on the ground, free for anyone to pick up.</summary>
    public void Drop(Vector3 pos)
    {
        transform.SetParent(null, true);
        Holder = null;
        state = State.Loose;
        spin = 0f;
        rb.isKinematic = true;
        pos.y = restHeight;
        transform.position = pos;
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
    }

    void FixedUpdate()
    {
        if (state != State.Flying) return;

        if (graceTimer > 0f) graceTimer -= Time.fixedDeltaTime;
        spin = Mathf.MoveTowards(spin, 0f, Time.fixedDeltaTime * 0.4f);

        rb.AddForce(Aero(rb.linearVelocity, spin), ForceMode.Acceleration);

        // spin the mesh for visual flair
        transform.Rotate(Vector3.up, 720f * Time.fixedDeltaTime, Space.World);

        Vector3 p = rb.position;

        // landed?
        if (p.y <= restHeight)
            MatchManager.I.OnDiscGrounded(p);
        // left the field?
        else if (!MatchManager.I.field.InBounds(p))
            MatchManager.I.OnDiscOutOfBounds(p);
        // someone catches it?
        else if (graceTimer <= 0f)
            TryCatch();
    }

    void TryCatch()
    {
        Player best = null;
        float bestDist = float.MaxValue;
        Vector3 dp = rb.position;

        foreach (var pl in MatchManager.I.players)
        {
            float reach = pl.catchRadius;
            Vector3 d = pl.transform.position - dp;
            d.y *= 0.5f;                         // jumping/extension is cheap vertically
            float dist = d.magnitude;
            if (dist <= reach && dist < bestDist)
            {
                bestDist = dist;
                best = pl;
            }
        }

        if (best != null)
            MatchManager.I.OnCatch(best, throwerTeam);
    }

    public Team ThrowerTeam => throwerTeam;
}
