// Bounded Buoyancy for Unity, 2D. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using BoundedBuoyancy.Core;
using UnityEngine;
using UnityEngine.Events;

namespace BoundedBuoyancy
{
    /// <summary>
    /// A ball in fast flight in a side view: round shot from a Bounded Buoyancy Cannon 2D, or any ball fired or thrown
    /// hard. As Bounded Buoyancy Cannonball in 3D, with the same physics: it is worked out as a ball, though the side view
    /// shows it as a circle. Give it one Circle Collider 2D; set its thickness to 4/3 of its radius so that its mass is a
    /// ball's (Bounded Buoyancy Cannon 2D does both).
    /// <para>In the air its drag changes with its speed (about 0.2 below two thirds of the speed of sound, rising to about
    /// 1 above it); it skips off the water when it comes down at less than about 18° / √σ (6.5° for iron); a step in which
    /// it meets the water is worked through in short steps (BallFlight2D). A body with a Bounded Buoyancy Hull 2D is struck
    /// by the ball's own reckoning: it goes in through the planking and out of the far side if it has the speed, holing the
    /// nearest compartment each time, and otherwise lodges; the body takes the momentum it loses. Under the water and once
    /// slow, it is an ordinary Bounded Buoyancy Body 2D: iron sinks.</para>
    /// </summary>
    [AddComponentMenu("Physics 2D/Bounded Buoyancy Cannonball 2D")]
    public class BoundedBuoyancyCannonball2D : BoundedBuoyancyBody2D
    {
        [Header("Flight")]
        [Tooltip("Skip off the water at shallow angles. Off, the water takes the ball as it does any body.")]
        public bool ricochet = true;

        [Tooltip("Speed of sound in the air, m/s: 343 at 20 °C.")]
        [Min(1f)] public float speedOfSound = 343f;

        [Tooltip("Seconds after which the ball removes itself. 0 keeps it.")]
        [Min(0f)] public float lifetime = 30f;

        [Tooltip("The ball has struck something: what, where, and the ball's velocity just before.")]
        public UnityEvent<Collider2D, Vector2, Vector2> hit = new UnityEvent<Collider2D, Vector2, Vector2>();

        readonly BallFlight2D flight = new BallFlight2D();
        System.Func<V2, WaterSample2D> waterAt;
        float age;
        bool carry;
        Vector2 chord, trueVelocity;
        Collider2D own;
        Collider2D[] passing;
        float passingUntil = -1f;
        readonly RaycastHit2D[] hits = new RaycastHit2D[8];
        static readonly ContactFilter2D NoFilter = new ContactFilter2D().NoFilter();

        /// <summary>How many times the ball has skipped off the water.</summary>
        public int Skips { get { return flight.Skips; } }
        /// <summary>The ball's velocity as it last left the water.</summary>
        public Vector2 ExitVelocity { get { return Bridge.U(flight.ExitVelocity); } }
        /// <summary>Mach number of the ball's flight through the air.</summary>
        public float Mach { get { return (float)flight.Mach; } }
        /// <summary>The flight worked out in short steps.</summary>
        public BallFlight2D Flight { get { return flight; } }

        /// <summary>Let the ball pass through these colliders (its own ship's) for `seconds`.</summary>
        public void IgnoreFor(Collider2D[] colliders, float seconds)
        {
            StopPassing();
            own = GetComponent<Collider2D>();
            if (own == null || colliders == null)
                return;
            foreach (Collider2D c in colliders)
                if (c != null && c != own)
                    Physics2D.IgnoreCollision(own, c, true);
            passing = colliders;
            passingUntil = Time.time + seconds;
        }

        void StopPassing()
        {
            if (passing == null)
                return;
            foreach (Collider2D c in passing)
                if (c != null && own != null && c != own)
                    Physics2D.IgnoreCollision(own, c, false);
            passing = null;
        }

        void Update()
        {
            age += Time.deltaTime;
            if (lifetime > 0f && age >= lifetime)
                Destroy(gameObject);
            if (passing != null && Time.time >= passingUntil)
                StopPassing();
        }

        void OnCollisionEnter2D(Collision2D collision)
        {
            if (collision.relativeVelocity.magnitude < 2f)
                return;
            Vector2 at = collision.contactCount > 0 ? collision.GetContact(0).point : (Vector2)transform.position;
            hit.Invoke(collision.collider, at, -collision.relativeVelocity);
        }

        protected override bool TryFastStep(float dt)
        {
            Rigidbody2D rb = Body;
            CircleCollider2D circle = GetComponent<CircleCollider2D>();
            if (circle == null || model != BoundedBuoyancyBody.Model.Bounded)
                return false;
            if (own == null)
            {
                own = circle;
                rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            }
            Vector2 v = Bridge.GetVelocity(rb);
            if (carry)
            {
                // The Rigidbody2D carried the ball along the chord of its last worked-through step; unless something has
                // struck it since, it goes on with its real velocity.
                carry = false;
                if ((v - chord).magnitude <= 1e-4f * Mathf.Max(chord.magnitude, 1f))
                    v = trueVelocity;
            }
            float g = -Physics2D.gravity.y;
            Vector3 sc = transform.lossyScale;
            flight.Radius = circle.radius * Mathf.Max(Mathf.Abs(sc.x), Mathf.Abs(sc.y));
            flight.Mass = Density * (4.0 / 3.0) * Mathf.PI * flight.Radius * flight.Radius * flight.Radius;
            flight.AddedMassCoefficient = 0.5;
            flight.DragCoefficient = 0.47;
            flight.AirDensity = airDensity;
            flight.SpeedOfSound = speedOfSound;
            flight.Ricochet = ricochet;
            if (!flight.IsFast(Bridge.V(v), g))
            {
                Bridge.SetVelocity(rb, v);
                return false;   // slow: an ordinary body
            }
            if (waterAt == null)
                waterAt = SampleAt;
            V2 start = Bridge.V(rb.position);
            V2 x = start, vel = Bridge.V(v);
            double t = 0.0;
            int n = 0;
            flight.Touches.Clear();
            while (t < dt - 1e-12 && n++ < 5000)
            {
                V2 x0 = x, v0 = vel;
                double h = flight.Advance(ref x, ref vel, waterAt, g, dt - t);
                int struck = Strike(x0, x, v0, ref x, ref vel);
                if (struck < 0)
                    return true;   // lodged
                t += h;
            }
            foreach (SurfaceTouch2D touch in flight.Touches)
            {
                if (touch.Entering)
                    waterEntered.Invoke((float)touch.Speed, Bridge.U(touch.Point));
                else
                    waterExited.Invoke((float)touch.Speed, Bridge.U(touch.Point));
            }
            chord = Bridge.U((x - start) / dt);
            trueVelocity = Bridge.U(vel);
            Bridge.SetVelocity(rb, chord);
            carry = true;
            return true;
        }

        /// <summary>Does the path from `x0` to `x1` strike planking? 0 if not, 1 if it went through (x and v set to where it
        /// goes on from), -1 if it lodged (the ball is gone).</summary>
        int Strike(V2 x0, V2 x1, V2 v, ref V2 x, ref V2 vel)
        {
            Vector2 a = Bridge.U(x0), b = Bridge.U(x1);
            Vector2 d = b - a;
            float len = d.magnitude;
            float s = (float)v.Length;
            if (len <= 0f || s < Planking.StrikeSpeed)
                return 0;
            int count = Physics2D.Raycast(a, d / len, NoFilter, hits, len);
            for (int i = 0; i < count; i++)
            {
                RaycastHit2D r = hits[i];
                if (r.collider == null || r.collider == own || r.collider.isTrigger || IsPassing(r.collider))
                    continue;
                BoundedBuoyancyHull2D hull = r.collider.GetComponentInParent<BoundedBuoyancyHull2D>();
                if (hull == null)
                    return 0;   // the engine meets it as it does any collision
                Vector2 dir = Bridge.U(v) / s;
                float cosIn = -Vector2.Dot(dir, r.normal);
                if (cosIn < Planking.Glance)
                    return 0;   // glancing: the engine's collision turns it aside
                hit.Invoke(r.collider, r.point, Bridge.U(v));
                float m = (float)flight.Mass, rad = (float)flight.Radius;
                float hole = Mathf.PI * rad * rad * hull.holeSize;
                float s1 = hull.SpeedThrough(m, rad, s, cosIn);
                hull.MakeHole(r.point, hole);
                Rigidbody2D target = r.rigidbody;
                if (s1 <= 0f)
                {
                    hull.struck.Invoke(this, r.point, false, 0f);
                    if (target != null)
                        target.AddForceAtPosition(dir * (s * m), r.point, ForceMode2D.Impulse);
                    Destroy(gameObject);
                    return -1;
                }
                // Through the ship and out of the far side, where its path leaves the body's shape.
                Vector2 outAt = r.point;
                float cosOut = cosIn;
                RaycastHit2D back = BackHit(r.collider, r.point, dir);
                if (back.collider != null)
                {
                    outAt = back.point;
                    cosOut = Mathf.Max(Vector2.Dot(dir, back.normal), (float)Planking.Glance);   // the far side faces along the path
                }
                float s2 = hull.SpeedThrough(m, rad, s1, cosOut);
                if (s2 <= 0f)
                {
                    hull.struck.Invoke(this, r.point, true, s1);
                    hull.struck.Invoke(this, outAt, false, 0f);
                    if (target != null)
                        target.AddForceAtPosition(dir * (s * m), outAt, ForceMode2D.Impulse);
                    Destroy(gameObject);
                    return -1;
                }
                if (outAt != r.point)
                    hull.MakeHole(outAt, hole);
                hull.struck.Invoke(this, r.point, true, s2);
                if (target != null)
                    target.AddForceAtPosition(dir * ((s - s2) * m), r.point, ForceMode2D.Impulse);
                IgnoreFor(target != null ? target.GetComponentsInChildren<Collider2D>() : new[] { r.collider }, 0.2f);
                x = Bridge.V(outAt + dir * (rad * 1.05f));
                vel = Bridge.V(dir * s2);
                return 1;
            }
            return 0;
        }

        bool IsPassing(Collider2D c)
        {
            if (passing == null)
                return false;
            foreach (Collider2D p in passing)
                if (p == c)
                    return true;
            return false;
        }

        /// <summary>Where the path in along `dir` from `at` leaves the collider: a ray cast back at it from beyond.</summary>
        RaycastHit2D BackHit(Collider2D c, Vector2 at, Vector2 dir)
        {
            float far = c.bounds.size.magnitude + 1f;
            Vector2 from = at + dir * far;
            int count = Physics2D.Raycast(from, -dir, NoFilter, hits, far);
            for (int i = 0; i < count; i++)
                if (hits[i].collider == c)
                    return hits[i];
            return default(RaycastHit2D);
        }
    }
}
