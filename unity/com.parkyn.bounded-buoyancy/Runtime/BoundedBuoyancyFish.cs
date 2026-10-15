// Bounded Buoyancy for Unity. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace BoundedBuoyancy
{
    /// <summary>
    /// A fish: a Bounded Buoyancy Body that swims, takes bait, and fights a line.
    /// <para>Give it a collider as for any body (a Capsule Collider along Z does), its head toward +Z. Its swim bladder
    /// keeps it as dense as the water round it, so it neither sinks nor rises, and it swims by its tail: a Bounded Buoyancy
    /// Thruster on a child object it makes for itself, whose push falls to nothing at Burst Speed, against the water's drag
    /// on its body. It turns itself to where it is going and keeps itself upright.</para>
    /// <para>It wanders near where it started, between Min Depth and Max Depth. Bait (its Bait, or else any Rigidbody with a
    /// Bounded Buoyancy Bait) within Sense Range may draw it: it comes up to it, nibbles at it (each nibble a small tug,
    /// Bite Pull, which dips a float), and then takes it and swims off with it. Any Bounded Buoyancy Line tied to the bait
    /// is then tied to the fish's mouth. Strike (pull the line harder than Strike Pull) while it has the bait and it is
    /// hooked; wait too long and it drops the bait and keeps away from bait for a while.</para>
    /// <para>Hooked, it runs away from the pull, veering and diving, with all the strength it has. Fighting tires it
    /// (Endurance is how many seconds of full effort it has in it), resting brings some strength back, and a tired fish may
    /// make one more run. It throws the hook if the angler's line goes slack for Slack Escape seconds, and escapes if a
    /// line breaks.</para>
    /// </summary>
    [AddComponentMenu("Physics/Bounded Buoyancy Fish")]
    public class BoundedBuoyancyFish : BoundedBuoyancyBody
    {
        public enum FishState { Wander, Approach, Nibble, Take, Hooked, Tired, Free }

        [Header("Swimming")]
        [Tooltip("Its usual swimming speed, m/s.")]
        [Min(0f)] public float cruiseSpeed = 0.4f;

        [Tooltip("Its fastest, in a dash, m/s.")]
        [Min(0.01f)] public float burstSpeed = 3f;

        [Tooltip("The push of its tail from rest, N.")]
        [Min(0f)] public float maxThrust = 8f;

        [Tooltip("How fast it can turn, degrees a second.")]
        [Range(1f, 2000f)] public float turnRate = 180f;

        [Tooltip("Keep it as dense as the water round it (as a swim bladder does).")]
        public bool swimBladder = true;

        [Tooltip("Shallowest it swims, m below the surface.")]
        [Min(0f)] public float minDepth = 0.5f;

        [Tooltip("Deepest it swims, m below the surface.")]
        [Min(0f)] public float maxDepth = 3f;

        [Tooltip("How far it wanders from where it started, m.")]
        [Min(0f)] public float homeRange = 8f;

        [Header("Feeding")]
        [Tooltip("Bait it goes for. Empty: any Rigidbody with a Bounded Buoyancy Bait.")]
        public Rigidbody bait;

        [Tooltip("How far it notices bait from, m.")]
        [Min(0f)] public float senseRange = 5f;

        [Tooltip("How keen it is: the chance a second that it goes for bait it has noticed.")]
        [Min(0f)] public float appetite = 0.3f;

        [Tooltip("Most nibbles before it takes the bait (it makes 1 to this many).")]
        [Range(0, 20)] public int nibbles = 3;

        [Tooltip("The tug of a nibble, N.")]
        [Min(0f)] public float bitePull = 0.04f;

        [Tooltip("Where its mouth is, in its own coordinates (from its Rigidbody's position, along its axes).")]
        public Vector3 mouth = new Vector3(0f, 0f, 0.15f);

        [Tooltip("Seconds it holds the bait before dropping it if not struck.")]
        [Min(0.1f)] public float takeTime = 2.5f;

        [Tooltip("A pull on the line this hard while it has the bait hooks it, N.")]
        [Min(0f)] public float strikePull = 2f;

        [Tooltip("Seconds it keeps away from bait after dropping it.")]
        [Min(0f)] public float waryTime = 15f;

        [Header("Fighting")]
        [Tooltip("Seconds of full effort it has in it, hooked. Resting, it gets a quarter of that back in as long again.")]
        [Min(0.1f)] public float endurance = 20f;

        [Tooltip("Seconds of slack line after which a hooked fish throws the hook. 0: never.")]
        [Min(0f)] public float slackEscape = 3f;

        [Tooltip("Seed for its choices. 0: different each run.")]
        public int randomSeed = 0;

        [Header("Fish events")]
        [Tooltip("It has noticed bait and goes for it.")]
        public UnityEvent<Rigidbody> noticedBait = new UnityEvent<Rigidbody>();
        [Tooltip("It has nibbled at the bait.")]
        public UnityEvent<Rigidbody> nibbled = new UnityEvent<Rigidbody>();
        [Tooltip("It has taken the bait.")]
        public UnityEvent<Rigidbody> tookBait = new UnityEvent<Rigidbody>();
        [Tooltip("It has dropped the bait, not struck in time.")]
        public UnityEvent<Rigidbody> droppedBait = new UnityEvent<Rigidbody>();
        [Tooltip("It is hooked.")]
        public UnityEvent hooked = new UnityEvent();
        [Tooltip("It has tired.")]
        public UnityEvent tired = new UnityEvent();
        [Tooltip("It has thrown the hook, or the line has broken.")]
        public UnityEvent escaped = new UnityEvent();

        /// <summary>What it is doing.</summary>
        public FishState State { get { return state; } }
        /// <summary>Its strength left, 1 (fresh) to 0 (spent).</summary>
        public float Stamina { get { return stamina; } }
        /// <summary>How hard it is swimming, 0 to 1.</summary>
        public float Effort { get { return effort; } }
        /// <summary>The line it is on, if hooked or holding bait.</summary>
        public BoundedBuoyancyLine Line { get { return line; } }
        /// <summary>Its tail.</summary>
        public BoundedBuoyancyThruster Tail { get { return tail; } }

        FishState state = FishState.Wander;
        float stamina = 1f;
        float effort;
        BoundedBuoyancyLine line;
        BoundedBuoyancyThruster tail;
        System.Random rng;
        bool started;
        Vector3 home;
        Vector3 goal;
        float goalTime;
        float speed;               // speed it wants to go, m/s
        float timer;
        float tug;                 // seconds left of the current tug
        int nibblesLeft;
        float wary;
        float slack;
        float veer;
        float veerTime;
        Rigidbody targetBait;
        readonly List<BoundedBuoyancyLine> lines = new List<BoundedBuoyancyLine>();   // lines tied to the bait it holds
        readonly List<Vector3> lineOffsets = new List<Vector3>();                    // and where each was tied on the bait
        float patience;            // seconds left to get the bait before it gives up
        readonly List<BoundedBuoyancyLine> chain = new List<BoundedBuoyancyLine>();   // and back from its mouth to the angler
        bool baitDetected;

        /// <summary>Its mouth, in the world.</summary>
        public Vector3 MouthPosition { get { Rigidbody rb = Body; return rb.position + rb.rotation * mouth; } }

        Vector3 Forward { get { return Body.rotation * Vector3.forward; } }

        void Begin()
        {
            started = true;
            rng = randomSeed != 0 ? new System.Random(randomSeed) : new System.Random();
            home = Body.position;
            // Its tail: its own, or the one a copy of a fish brings with it.
            Transform own = transform.Find("Tail");
            if (own != null)
                tail = own.GetComponent<BoundedBuoyancyThruster>();
            if (tail == null)
            {
                GameObject go = Visuals.Child(transform, "Tail");
                tail = go.AddComponent<BoundedBuoyancyThruster>();
                tail.medium = BoundedBuoyancy.Core.Medium.Water;
            }
            tail.throttle = 0f;
            NewGoal();
        }

        /// <summary>Density of the water round it: its water's where it is (whatever the depth, salt or warmth), or the
        /// body's Water Density.</summary>
        float BladderDensity()
        {
            Vector3 p = Body.position + Body.rotation * Bridge.U(Core.Shape.Centroid);
            if (Water != null)
                return Water.DensityAt(p);
            if (waterSurface != null)
            {
                var w = waterSurface.GetComponent<BoundedBuoyancyWater>();
                return w != null ? w.DensityAt(p) : waterDensity;
            }
            List<BoundedBuoyancyWater> all = BoundedBuoyancyWater.active;
            for (int i = 0; i < all.Count; i++)
                if (all[i].Contains(p))
                    return all[i].DensityAt(p);
            return waterDensity;
        }

        protected override void OnDisable()
        {
            // Taken out of the water (disabled or destroyed) with the bait in its mouth: it lets go.
            if (state == FishState.Take || state == FishState.Hooked || state == FishState.Tired)
            {
                LetGoOfBait();
                targetBait = null;
                state = FishState.Wander;
            }
            base.OnDisable();
        }

        protected override void BeforeStep(float dt)
        {
            if (!started)
                Begin();
            if (swimBladder)
            {
                float d = BladderDensity();
                if (Mathf.Abs(Density - d) > 1e-6f)
                    Density = d;
            }
            tail.maxThrust = maxThrust;
            tail.maxSpeed = Mathf.Max(burstSpeed, 0.01f);
            tail.diameter = Mathf.Max((float)Core.Shape.Size.y, 0.01f);
            Think(dt);
            float full = Mathf.Clamp01(speed / Mathf.Max(burstSpeed, 0.01f));
            bool fighting = state == FishState.Hooked || state == FishState.Tired || state == FishState.Free;
            effort = fighting ? Mathf.Min(full, 0.15f + 0.85f * stamina) : full;
            tail.throttle = effort;
            // Fighting tires it; out of the fight (or spent and only drifting) it gets its strength back, slowly.
            if (state == FishState.Hooked)
                stamina = Mathf.Max(stamina - effort * dt / endurance, 0f);
            else
                stamina = Mathf.Min(stamina + (1f - effort) * dt / (4f * endurance), 1f);
            Steer(dt);
        }

        float RandomRange(float a, float b)
        {
            return a + (b - a) * (float)rng.NextDouble();
        }

        void Think(float dt)
        {
            Vector3 here = Body.position;
            wary = Mathf.Max(wary - dt, 0f);
            switch (state)
            {
                case FishState.Wander:
                {
                    speed = cruiseSpeed;
                    goalTime -= dt;
                    if (goalTime <= 0f || (here - goal).magnitude < 0.5f)
                        NewGoal();
                    if (wary <= 0f)
                    {
                        Rigidbody b = FindBait();
                        if (b != null && rng.NextDouble() < appetite * dt)
                        {
                            targetBait = b;
                            nibblesLeft = nibbles > 0 ? rng.Next(1, Mathf.Max(nibbles, 1) + 1) : 0;
                            patience = 40f;
                            state = FishState.Approach;
                            noticedBait.Invoke(b);
                        }
                    }
                    break;
                }
                case FishState.Approach:
                {
                    patience -= dt;
                    if (!BaitOk() || patience <= 0f)
                    {
                        if (patience <= 0f)
                            wary = waryTime;   // it gives up, and keeps away for a while
                        BackToWander();
                        return;
                    }
                    Vector3 b = targetBait.position;
                    Vector3 off = here - b;
                    off.y = 0f;
                    off = off.magnitude > 1e-3f ? off.normalized * 0.25f : new Vector3(0.25f, 0f, 0f);
                    float to = (MouthPosition - b).magnitude;
                    goal = to < 0.4f ? b : b + off;
                    speed = Mathf.Clamp(to, 0.15f, 1f) * cruiseSpeed;
                    if (to < Reach())
                    {
                        if (nibblesLeft > 0)
                        {
                            state = FishState.Nibble;
                            tug = 0.12f;
                            timer = 0f;
                            nibbled.Invoke(targetBait);
                        }
                        else
                        {
                            Take();
                        }
                    }
                    break;
                }
                case FishState.Nibble:
                {
                    patience -= dt;
                    if (!BaitOk() || patience <= 0f)
                    {
                        if (patience <= 0f)
                            wary = waryTime;   // it gives up, and keeps away for a while
                        BackToWander();
                        return;
                    }
                    Vector3 b = targetBait.position;
                    if (tug > 0f)
                    {
                        tug -= dt;
                        Vector3 away = (here - b).normalized + Vector3.down * 0.5f;
                        targetBait.AddForce(away.normalized * bitePull, ForceMode.Force);
                        goal = b;
                        speed = 0.1f;
                        if (tug <= 0f)
                        {
                            nibblesLeft--;
                            timer = RandomRange(0.5f, 1.6f);
                        }
                    }
                    else
                    {
                        // Backs off a little between nibbles.
                        goal = b + (here - b).normalized * 0.3f;
                        speed = 0.2f;
                        timer -= dt;
                        if (timer <= 0f)
                            state = FishState.Approach;
                    }
                    break;
                }
                case FishState.Take:
                {
                    timer -= dt;
                    HoldHook();
                    speed = cruiseSpeed;
                    if ((here - goal).magnitude < 0.4f)
                        goal = here + Turned(Flat(Forward), RandomRange(-1f, 1f)) * 3f;
                    // Struck: the angler's line pulled hard (or the fish ran against a tight one).
                    for (int i = 0; i < lines.Count; i++)
                    {
                        BoundedBuoyancyLine l = lines[i];
                        if (l == null)
                            continue;
                        Chain(l, chain);
                        if (chain[chain.Count - 1].Tension > strikePull)
                        {
                            state = FishState.Hooked;
                            line = l;
                            slack = 0f;
                            hooked.Invoke();
                            return;
                        }
                    }
                    if (timer <= 0f)
                        Drop();
                    break;
                }
                case FishState.Hooked:
                case FishState.Tired:
                {
                    HoldHook();
                    if (line == null)
                    {
                        Escape();
                        return;
                    }
                    Chain(line, chain);
                    for (int i = 0; i < chain.Count; i++)
                    {
                        if (chain[i].Broken)
                        {
                            Escape();
                            return;
                        }
                    }
                    BoundedBuoyancyLine angler = chain[chain.Count - 1];
                    // A slack line from the angler and the hook works loose.
                    slack = angler.Tension <= 0f ? slack + dt : 0f;
                    if (slackEscape > 0f && slack > slackEscape)
                    {
                        Escape();
                        return;
                    }
                    if (state == FishState.Hooked && stamina < 0.12f)
                    {
                        state = FishState.Tired;
                        tired.Invoke();
                    }
                    else if (state == FishState.Tired && stamina > 0.35f)
                    {
                        state = FishState.Hooked;   // one more run
                    }
                    Vector3 pull = (angler.transform.position - MouthPosition).normalized;
                    if (state == FishState.Tired)
                    {
                        speed = 0.1f * burstSpeed;
                        goal = here + Flat(Forward) * 2f;
                    }
                    else
                    {
                        veerTime -= dt;
                        if (veerTime <= 0f)
                        {
                            veer = RandomRange(-0.7f, 0.7f);
                            veerTime = RandomRange(1.5f, 4f);
                        }
                        Vector3 run = Turned(Flat(-pull), veer);
                        goal = here + run * 4f + Vector3.down * 1.5f;
                        speed = burstSpeed;
                    }
                    break;
                }
                case FishState.Free:
                {
                    speed = timer > 0f ? burstSpeed : cruiseSpeed;
                    timer -= dt;
                    if ((here - goal).magnitude < 0.5f)
                        goal = here + Flat(Forward) * 5f;
                    if (timer < -5f)
                    {
                        home = here;
                        BackToWander();
                    }
                    break;
                }
            }
            // Stay between Min Depth and Max Depth (the goal's depth), in the water; after bait, it goes where the bait
            // is, short of the surface.
            float surface = Water != null ? Water.HeightAt(goal) : goal.y + minDepth;
            if (state == FishState.Approach || state == FishState.Nibble)
                goal.y = Mathf.Min(goal.y, surface - 0.05f);
            else
                goal.y = Mathf.Clamp(goal.y, surface - maxDepth, surface - minDepth);
        }

        /// <summary>The lines from its mouth back to the angler, through any float or weight between: the first is the one
        /// tied to it, the last the one the angler holds. Empty when it is on no line.</summary>
        public List<BoundedBuoyancyLine> LinesToAngler()
        {
            var found = new List<BoundedBuoyancyLine>();
            if (line != null)
                Chain(line, found);
            return found;
        }

        /// <summary>The lines from `first` back to the angler, into `chain`.</summary>
        void Chain(BoundedBuoyancyLine first, List<BoundedBuoyancyLine> chain)
        {
            chain.Clear();
            chain.Add(first);
            for (int i = 0; i < 8; i++)
            {
                Rigidbody holder = chain[chain.Count - 1].Holder;
                BoundedBuoyancyLine next = null;
                if (holder != null)
                {
                    List<BoundedBuoyancyLine> all = BoundedBuoyancyLine.active;
                    for (int k = 0; k < all.Count; k++)
                    {
                        if (all[k].body == holder && !chain.Contains(all[k]))
                        {
                            next = all[k];
                            break;
                        }
                    }
                }
                if (next == null)
                    break;
                chain.Add(next);
            }
        }

        /// <summary>How far from its mouth it can suck in bait: half the distance from its middle to its mouth.</summary>
        float Reach()
        {
            return Mathf.Max(0.5f * mouth.magnitude, 0.02f);
        }

        void NewGoal()
        {
            float a = (float)rng.NextDouble() * 2f * Mathf.PI;
            float r = Mathf.Sqrt((float)rng.NextDouble()) * homeRange;
            float surface = Water != null ? Water.HeightAt(home) : home.y;
            goal = home + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            goal.y = surface - RandomRange(minDepth, maxDepth);
            goalTime = RandomRange(4f, 10f);
        }

        Rigidbody FindBait()
        {
            Rigidbody best = null;
            float near = senseRange;
            Vector3 here = Body.position;
            if (bait != null)
            {
                Consider(bait, here, ref best, ref near);
            }
            else
            {
                List<BoundedBuoyancyBait> all = BoundedBuoyancyBait.active;
                for (int i = 0; i < all.Count; i++)
                    Consider(all[i].Body, here, ref best, ref near);
            }
            return best;
        }

        void Consider(Rigidbody b, Vector3 here, ref Rigidbody best, ref float near)
        {
            if (b == null || b == Body || b.isKinematic || !b.gameObject.activeInHierarchy)
                return;
            Vector3 p = b.position;
            float d = (here - p).magnitude;
            if (d < near && (Water == null || p.y < Water.HeightAt(p)))
            {
                near = d;
                best = b;
            }
        }

        bool BaitOk()
        {
            return targetBait != null && !targetBait.isKinematic && (Body.position - targetBait.position).magnitude < senseRange * 1.5f;
        }

        void BackToWander()
        {
            state = FishState.Wander;
            targetBait = null;
            NewGoal();
        }

        void Take()
        {
            state = FishState.Take;
            timer = takeTime;
            lines.Clear();
            lineOffsets.Clear();
            List<BoundedBuoyancyLine> all = BoundedBuoyancyLine.active;
            for (int i = 0; i < all.Count; i++)
            {
                BoundedBuoyancyLine l = all[i];
                if (l.body == targetBait && !l.Broken)
                {
                    lines.Add(l);
                    lineOffsets.Add(l.endOffset);
                    l.body = Body;
                    l.endOffset = mouth;
                }
            }
            targetBait.isKinematic = true;
            baitDetected = targetBait.detectCollisions;
            targetBait.detectCollisions = false;
            goal = Body.position + Flat(Forward) * 3f;
            tookBait.Invoke(targetBait);
        }

        /// <summary>The bait (the hook) stays in its mouth.</summary>
        void HoldHook()
        {
            if (targetBait != null)
                targetBait.position = MouthPosition;
        }

        void LetGoOfBait()
        {
            bool hasBait = targetBait != null;
            for (int i = 0; i < lines.Count; i++)
            {
                BoundedBuoyancyLine l = lines[i];
                if (l != null && l.body == Body)
                {
                    // Tied back to the bait as it was; the bait gone, the line hangs free.
                    l.body = hasBait ? targetBait : null;
                    l.endOffset = lineOffsets[i];
                }
            }
            lines.Clear();
            lineOffsets.Clear();
            line = null;
            if (hasBait)
            {
                targetBait.detectCollisions = baitDetected;
                targetBait.isKinematic = false;
                Bridge.SetVelocity(targetBait, Bridge.GetVelocity(Body));
            }
        }

        void Drop()
        {
            Rigidbody b = targetBait;
            LetGoOfBait();
            wary = waryTime;
            BackToWander();
            droppedBait.Invoke(b);
        }

        void Escape()
        {
            LetGoOfBait();
            targetBait = null;
            state = FishState.Free;
            timer = 4f;
            wary = waryTime * 4f;
            goal = Body.position + Flat(Forward) * 5f;
            escaped.Invoke();
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward;
        }

        /// <summary>`v` turned `radians` about the vertical.</summary>
        static Vector3 Turned(Vector3 v, float radians)
        {
            return Quaternion.AngleAxis(radians * Mathf.Rad2Deg, Vector3.up) * v;
        }

        /// <summary>Turn toward the goal and keep upright: a turn rate toward each, reached over a quarter of a second
        /// against its own inertia, never faster than Turn Rate.</summary>
        void Steer(float dt)
        {
            Rigidbody rb = Body;
            Quaternion q = rb.rotation;
            Vector3 fwd = q * Vector3.forward;
            Vector3 to = goal - rb.position;
            Vector3 want = Vector3.zero;
            if (to.magnitude > 0.05f)
            {
                Vector3 d = to.normalized;
                // Fish swim level-ish: no steeper than 40°, unless right at what they are after.
                float steep = Mathf.Tan(40f * Mathf.Deg2Rad);
                if (to.magnitude > 0.5f && Mathf.Abs(d.y) > new Vector2(d.x, d.z).magnitude * steep)
                    d = (Flat(d) + Vector3.up * Mathf.Sign(d.y) * steep).normalized;
                Vector3 axis = Vector3.Cross(fwd, d);
                float angle = Mathf.Atan2(axis.magnitude, Vector3.Dot(fwd, d));
                if (axis.magnitude > 1e-6f)
                    want += axis.normalized * angle * 3f;
            }
            if (state != FishState.Tired)
            {
                // Upright: its back toward the sky, about its own length.
                Vector3 up = Vector3.up - fwd * fwd.y;
                if (up.magnitude > 1e-3f)
                {
                    Vector3 roll = Vector3.Cross(q * Vector3.up, up.normalized);
                    want += fwd * Vector3.Dot(roll, fwd) * 4f;
                }
            }
            float limit = turnRate * Mathf.Deg2Rad;
            if (want.magnitude > limit)
                want = want.normalized * limit;
            float tau = Mathf.Max(0.25f, 2f * dt);
            // Torque I (want - ω) / τ, with its own rotational inertia (principal moments along its axes).
            Vector3 moments = rb.inertiaTensor;
            if (moments.x <= 0f || moments.y <= 0f || moments.z <= 0f)
                return;
            Quaternion axes = q * rb.inertiaTensorRotation;
            Vector3 change = Quaternion.Inverse(axes) * ((want - rb.angularVelocity) / tau);
            rb.AddTorque(axes * new Vector3(moments.x * change.x, moments.y * change.y, moments.z * change.z), ForceMode.Force);
        }
    }
}
