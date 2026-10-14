// Bounded Buoyancy for Unity. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System.Collections.Generic;
using BoundedBuoyancy.Core;
using UnityEngine;
using UnityEngine.Events;

namespace BoundedBuoyancy
{
    /// <summary>
    /// A ball in fast flight: round shot from a cannon, a musket ball, or any ball fired or thrown hard. Give it one
    /// Sphere Collider; Bounded Buoyancy Cannon makes them ready to fly.
    /// <para>Drag in the air: a fast ball's drag changes with its speed. Below about two thirds of the speed of sound a
    /// cannonball's drag coefficient is about 0.2, well under a slow ball's 0.47, as the air round it is turbulent (the
    /// drag crisis); it rises through the speed of sound to about 1 (the sphere drag of Miller and Bailey, 1979, from
    /// 18th and 19th century cannon firings; see SphereDrag). So a 24-pounder carries about a mile at 5 degrees, as the
    /// gunners' tables give.</para>
    /// <para>Skipping off the water: a ball that meets the water at a shallow angle skips off it, as round shot fired to
    /// ricochet across the sea did. The water pushes on the part of the ball under its surface that faces into its path
    /// (Birkhoff and others, 1944; see WaterImpact), and a ball skips when it comes down at less than about 18° / √σ, σ
    /// its density over the water's: 6.5° for iron, 11° for aluminium. The contact lasts much less than a physics step,
    /// so a step in which the ball meets the water is worked through in short steps (BodyCore3D.TryStepBall). Waves tilt
    /// the surface, so a ball meets the face of a wave more steeply and plunges sooner.</para>
    /// <para>Hitting things: it flies too far in a step for the usual collision tests, so continuous collision detection
    /// is on and it strikes a hull instead of passing through it; Hit is invoked. A body with a Bounded Buoyancy Hull (its
    /// planking) is struck by the ball's own reckoning instead: the ball goes through the planking if it has the speed
    /// (the wood resists it with σ + ρ v²), holing the nearest compartment, flies on through the ship and out of the far
    /// side if it still has the speed, and otherwise lodges in the wood; splinters fly, and the body takes the momentum the
    /// ball loses, less theirs. A ball that meets the sea on its way to the planking skips off it, or plunges in, first, so shot
    /// fired low holes a ship at and above its waterline. Under the water and once slow, it is an ordinary Bounded
    /// Buoyancy Body: an iron ball sinks.</para>
    /// </summary>
    [AddComponentMenu("Physics/Bounded Buoyancy Cannonball")]
    public class BoundedBuoyancyCannonball : BoundedBuoyancyBody
    {
        [Header("Flight")]
        [Tooltip("Skip off the water at shallow angles. Off, the water takes the ball as it does any body.")]
        public bool ricochet = true;

        [Tooltip("Speed of sound in the air, m/s: 343 at 20 °C, 331 at 0 °C.")]
        [Min(1f)] public float speedOfSound = 343f;

        [Tooltip("Seconds after which the ball removes itself. 0 keeps it.")]
        [Min(0f)] public float lifetime = 30f;

        [Tooltip("The ball has struck something: what, where, and the ball's velocity just before. Through a ship, it strikes it twice: going in, and coming out.")]
        public UnityEvent<Collider, Vector3, Vector3> hit = new UnityEvent<Collider, Vector3, Vector3>();

        const float HitSpeed = 2f;   // slowest strike reported

        float age;
        bool prepared;
        Vector3 flightVelocity;
        Collider[] ignoring;
        float ignoreUntil = -1f;
        Vector3 chord;           // velocity the Rigidbody was given for the last worked-through step
        Vector3 trueVelocity;    // and the ball's real velocity at its end
        bool carry;
        Collider own;
        int mask;                // the layers the ball collides with
        bool inside;             // flying through a body it has gone into:
        Collider insideCollider;
        Rigidbody insideBody;
        BoundedBuoyancyHull insideHull;
        Transform insideFrame;
        Collider[] insideColliders;
        Vector3 exitLocal, exitNormal;   // where its path leaves that body, and the far side's normal, in its frame
        float insideTime;
        Collider[] passing;      // the colliders of a body it has gone through, let pass for a moment after
        float passingUntil = -1f;
        WaterSample insideSample;

        /// <summary>How many times the ball has skipped off the water.</summary>
        public int Skips { get { return Core.Skips; } }
        /// <summary>The ball's velocity as it last left the water (world).</summary>
        public Vector3 ExitVelocity { get { return Bridge.U(Core.ExitVelocity); } }
        /// <summary>How far the ball went into the wood it last lodged in, along its path (m).</summary>
        public float Penetration { get; private set; }
        /// <summary>Mach number of the ball's flight through the air.</summary>
        public float Mach { get { return (Bridge.GetVelocity(Body) - WindHere).magnitude / Mathf.Max(speedOfSound, 1f); } }

        /// <summary>Let the ball pass through these colliders (its own ship's) for `seconds`.</summary>
        public void IgnoreFor(Collider[] colliders, float seconds)
        {
            Collider own = GetComponent<Collider>();
            if (own == null || colliders == null)
                return;
            foreach (Collider c in colliders)
                if (c != null && c != own)
                    Physics.IgnoreCollision(own, c, true);
            ignoring = colliders;
            ignoreUntil = Time.time + seconds;
        }

        void Update()
        {
            age += Time.deltaTime;
            if (lifetime > 0f && age >= lifetime)
                Destroy(gameObject);
            if (ignoring != null && Time.time >= ignoreUntil)
            {
                Collider mine = GetComponent<Collider>();
                foreach (Collider c in ignoring)
                    if (c != null && mine != null && c != mine)
                        Physics.IgnoreCollision(mine, c, false);
                ignoring = null;
            }
            if (passing != null && !inside && Time.time >= passingUntil)
                StopPassing();
        }

        void OnCollisionEnter(Collision collision)
        {
            if (flightVelocity.magnitude < HitSpeed)
                return;
            Vector3 at = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            hit.Invoke(collision.collider, at, flightVelocity);
        }

        protected override Vector3 StepVelocity(Vector3 velocity)
        {
            // The Rigidbody carried the ball along the chord of its last worked-through step; unless something has
            // struck it since, it goes on with its real velocity.
            if (!carry)
                return velocity;
            carry = false;
            if ((velocity - chord).magnitude > 1e-4f * Mathf.Max(chord.magnitude, 1f))
                return velocity;
            Bridge.SetVelocity(Body, trueVelocity);
            return trueVelocity;
        }

        protected override bool TryFastStep(BodyPose pose, WaterSample sample, V3 wind, Vector3 velocity, float dt)
        {
            Rigidbody rb = Body;
            if (!prepared)
            {
                prepared = true;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                own = GetComponent<Collider>();
                mask = 0;
                for (int i = 0; i < 32; i++)
                    if (!Physics.GetIgnoreLayerCollision(gameObject.layer, i))
                        mask |= 1 << i;
            }
            flightVelocity = velocity;
            Core.FastBallDrag = true;
            Core.SpeedOfSound = speedOfSound;
            if (Core.Shape.SingleSphere)
            {
                if (inside && (insideHull == null || insideFrame == null))
                    inside = false;   // the body it was in is gone
                if (inside)
                    return InsideStep(pose, sample, velocity, dt);
                if (TryStrike(pose, sample, wind, velocity, dt))
                    return true;
            }
            if (!ricochet || model != Model.Bounded)
                return false;
            if (!Core.TryStepBall(pose, Bridge.V(velocity), sample, wind, -Physics.gravity.y, dt))
                return false;
            ReportTouches();
            HandOnFromCore(pose, velocity, dt);
            return true;
        }

        void ReportTouches()
        {
            for (int i = 0; i < Core.Touches.Count; i++)
            {
                SurfaceTouch touch = Core.Touches[i];
                float speed = (float)touch.Speed;
                Vector3 at = Bridge.U(touch.Point);
                if (touch.Entering)
                    waterEntered.Invoke(speed, at);
                else
                    waterExited.Invoke(speed, at);
                if (Water != null)
                    Water.Splash(at, touch.Entering ? speed : 0.4f * speed, 2f * (float)Core.Shape.Radius);
            }
        }

        /// <summary>A step worked through by the core: the ball goes on to where it leaves it.</summary>
        void HandOnFromCore(BodyPose pose, Vector3 velocity, float dt)
        {
            ApplyMass((float)Core.InertialMass);
            ApplyAngularDamping((float)Core.AngularDamp);
            HandOn(Bridge.U(pose.Apply(Core.Shape.SphereCentre)), Bridge.U(Core.BallCentre), Bridge.U(Core.VelocityTarget), velocity, dt);
        }

        /// <summary>The Rigidbody moves the ball on by the velocity it ends the step with, so it is given the chord of the
        /// step's path, from `start` to `end`; the real velocity at the end, `vEnd`, is handed back at the start of the next
        /// step (StepVelocity). No teleport, and continuous collision detection sweeps the chord.</summary>
        void HandOn(Vector3 start, Vector3 end, Vector3 vEnd, Vector3 velocity, float dt)
        {
            chord = (end - start) / dt;
            trueVelocity = vEnd;
            carry = true;
            Body.AddForce(chord - velocity, ForceMode.VelocityChange);
        }

        /// <summary>A step in which the ball's path reaches planking (a Bounded Buoyancy Hull): it strikes it, through the
        /// water first if it is in the water on its way. False when it reaches none this step.</summary>
        bool TryStrike(BodyPose pose, WaterSample sample, V3 wind, Vector3 v, float dt)
        {
            float speed = v.magnitude;
            if (speed < Planking.StrikeSpeed)
                return false;
            float a = (float)Core.Shape.Radius;
            Vector3 c = Bridge.U(pose.Apply(Core.Shape.SphereCentre));
            Vector3 dir = v / speed;
            RaycastHit cast;
            BoundedBuoyancyHull hull;
            if (!Cast(c, a, dir, speed * dt, out cast, out hull))
                return false;
            Vector3 n = cast.normal;
            if (-Vector3.Dot(dir, n) < Planking.Glance)
                return false;   // glancing: it bounces off, as the engine makes it
            Vector3 centre = c + dir * cast.distance;
            Vector3[] flown;
            if (ricochet && model == Model.Bounded && Core.MeetsWaterBefore(Bridge.V(c), Bridge.V(centre), sample))
            {
                // In the sea on its way: it skips off it, or plunges in, before it gets to the planking.
                Core.StepBallToWall(pose, Bridge.V(v), sample, wind, -Physics.gravity.y, dt, Bridge.V(centre), Bridge.V(n));
                ReportTouches();
                if (!Core.WallStruck)
                {
                    HandOnFromCore(pose, v, dt);
                    return true;
                }
                ApplyMass((float)Core.InertialMass);
                Vector3 there = Bridge.U(Core.BallCentre);
                flown = Strike(cast.collider, hull, there - n * a, n, there, Bridge.U(Core.WallVelocity), dt - (float)Core.WallTime, sample);
            }
            else
            {
                flown = Strike(cast.collider, hull, cast.point, n, centre, v, dt - cast.distance / speed, sample);
            }
            if (flown != null)
                HandOn(c, flown[0], flown[1], v, dt);
            return true;
        }

        static readonly RaycastHit[] hits = new RaycastHit[32];

        /// <summary>The planking the ball, its centre at `c`, would strike going `distance` along `dir`: not its own, nor
        /// what it is letting pass, and past anything lighter than the ball (a splinter), which the engine meets as it meets
        /// anything. None if something solid is in the way first.</summary>
        bool Cast(Vector3 c, float a, Vector3 dir, float distance, out RaycastHit nearest, out BoundedBuoyancyHull hull)
        {
            nearest = new RaycastHit();
            hull = null;
            int count = gameObject.scene.GetPhysicsScene().SphereCast(c, a, dir, hits, distance, mask, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = hits[i];
                Collider other = h.collider;
                if (other == null || other == own || (other.attachedRigidbody != null && other.attachedRigidbody == Body))
                    continue;
                if (h.distance <= 0f && h.point == Vector3.zero)
                    continue;   // overlapping at the start: the engine's to sort out
                if ((Listed(ignoring, other) && Time.time < ignoreUntil) || Listed(passing, other) || h.distance >= best)
                    continue;
                BoundedBuoyancyHull planking = other.GetComponentInParent<BoundedBuoyancyHull>();
                Rigidbody rb = other.attachedRigidbody;
                if ((planking == null || !planking.isActiveAndEnabled) && rb != null && !rb.isKinematic && rb.mass < OwnMass)
                    continue;   // a splinter, or anything lighter than the ball: on past it
                best = h.distance;
                nearest = h;
                hull = planking != null && planking.isActiveAndEnabled ? planking : null;
            }
            return hull != null;
        }

        static bool Listed(Collider[] list, Collider c)
        {
            if (list == null)
                return false;
            foreach (Collider x in list)
                if (x == c)
                    return true;
            return false;
        }

        static Vector3 VelocityAt(Rigidbody body, Vector3 at)
        {
            return body != null ? body.GetPointVelocity(at) : Vector3.zero;
        }

        /// <summary>Let collisions with the body the ball went through happen again.</summary>
        void StopPassing()
        {
            if (passing != null && own != null)
                foreach (Collider c in passing)
                    if (c != null && c != own)
                        Physics.IgnoreCollision(own, c, false);
            passing = null;
        }

        /// <summary>The ball, its centre at `centre` and flying at `v`, strikes the planking (`hull`) of what `target` is
        /// part of at the world point `at`, where its face has the outward normal `n`: it goes through, holing it, or lodges
        /// in it. Then on through the body for `timeAfter` seconds. Returns where it ends and its velocity then, or null if
        /// it lodged. Speeds are taken relative to the body there.</summary>
        Vector3[] Strike(Collider target, BoundedBuoyancyHull hull, Vector3 at, Vector3 n, Vector3 centre, Vector3 v, float timeAfter, WaterSample sample)
        {
            Rigidbody body = target.attachedRigidbody;
            Vector3 moving = VelocityAt(body, at);
            Vector3 u = v - moving;
            float speed = u.magnitude;
            Vector3 dir = u / speed;
            float cosIn = Mathf.Max(-Vector3.Dot(dir, n), 0f);
            float a = (float)Core.Shape.Radius;
            float m = OwnMass;
            Transform frame = body != null ? body.transform : hull.transform;
            Collider[] colliders = frame.GetComponentsInChildren<Collider>();
            float v1 = hull.SpeedThrough(m, a, speed, cosIn);
            hit.Invoke(target, at, v);
            if (v1 <= 0f)
            {
                Penetration = (float)Planking.Depth(m, Mathf.PI * a * a, speed, hull.density, hull.strength * 1e6);
                Lodge(body, hull, colliders, at, v);
                return null;
            }
            // Through the planking: a hole, and splinters off the face. The body takes the momentum the ball loses, less
            // what the splinters carry off.
            hull.MakeHole(at, Mathf.PI * a * a * hull.holeSize);
            Vector3 thrown = hull.ThrowSplinters(at, (n - dir).normalized, 0.05f * speed, a, cosIn, moving, own);
            if (body != null && !body.isKinematic)
                body.AddForceAtPosition(dir * (m * (speed - v1)) - thrown, at, ForceMode.Impulse);
            hull.struck.Invoke(this, at, true, v1);
            // On through the body, out of the engine's way: where its path leaves it, the far side.
            StopPassing();
            foreach (Collider c in colliders)
                if (c != null && own != null && c != own)
                    Physics.IgnoreCollision(own, c, true);
            inside = true;
            insideCollider = target;
            insideBody = body;
            insideHull = hull;
            insideFrame = frame;
            insideColliders = colliders;
            insideTime = 0f;
            passing = colliders;
            insideSample = sample;
            Vector3 farPoint, farNormal;
            if (!FarSide(colliders, at, dir, out farPoint, out farNormal))
            {
                // No far side to be found (a shape open on that side): it stops in the body here.
                Penetration = 0f;
                Lodge(body, hull, colliders, at, moving + dir * v1);
                return null;
            }
            exitLocal = frame.InverseTransformPoint(farPoint);
            exitNormal = frame.InverseTransformDirection(farNormal);
            return Through(centre, moving + dir * v1, timeAfter);
        }

        /// <summary>A step of flying through the body the ball has gone into.</summary>
        bool InsideStep(BodyPose pose, WaterSample sample, Vector3 velocity, float dt)
        {
            insideTime += dt;
            insideSample = sample;
            Vector3 c = Bridge.U(pose.Apply(Core.Shape.SphereCentre));
            if (insideTime > 2f)
            {
                Penetration = 0f;
                Lodge(insideBody, insideHull, insideColliders, c, velocity);
                return true;
            }
            Vector3[] flown = Through(c, velocity, dt);
            if (flown != null)
                HandOn(c, flown[0], flown[1], velocity, dt);
            return true;
        }

        /// <summary>The ball flies on inside the body it has gone into, from `p` at `v`, for `time`: out of the far side if
        /// it gets there with the speed to go through that planking, and otherwise lodged in it. Inside the hull it is out of
        /// the sea and the wind, under gravity alone. Returns where it ends and its velocity then, or null if it lodged.
        /// Speeds are taken relative to the body.</summary>
        Vector3[] Through(Vector3 p, Vector3 v, float time)
        {
            Vector3 exit = insideFrame.TransformPoint(exitLocal);
            Vector3 normal = insideFrame.TransformDirection(exitNormal).normalized;
            float g = -Physics.gravity.y;
            Vector3 moving = VelocityAt(insideBody, p);
            Vector3 u = v - moving;
            float speed = u.magnitude;
            Vector3 dir = speed > 1e-6f ? u / speed : Vector3.zero;
            float cosOut = Vector3.Dot(dir, normal);
            if (speed < 1e-3f || cosOut < Planking.Glance)
            {
                Penetration = 0f;
                Lodge(insideBody, insideHull, insideColliders, p, v);
                return null;
            }
            float s = Vector3.Dot(exit - p, normal) / cosOut;   // how far on its path, through the body, to the far side
            if (s > speed * time)
                return new[] { p + v * time + Vector3.down * (0.5f * g * time * time), v + Vector3.down * (g * time) };
            float toFar = Mathf.Max(s, 0f) / speed;
            Vector3 at = p + v * toFar + Vector3.down * (0.5f * g * toFar * toFar);
            float m = OwnMass;
            float a = (float)Core.Shape.Radius;
            BoundedBuoyancyHull hull = insideHull;
            float v2 = hull.SpeedThrough(m, a, speed, cosOut);
            hit.Invoke(insideCollider, at, v);
            if (v2 <= 0f)
            {
                Penetration = (float)Planking.Depth(m, Mathf.PI * a * a, speed, hull.density, hull.strength * 1e6);
                Lodge(insideBody, hull, insideColliders, at, v);
                return null;
            }
            hull.MakeHole(at, Mathf.PI * a * a * hull.holeSize);
            moving = VelocityAt(insideBody, at);
            Vector3 thrown = hull.ThrowSplinters(at, dir, 0.3f * v2, a, cosOut, moving, own);
            if (insideBody != null && !insideBody.isKinematic)
                insideBody.AddForceAtPosition(dir * (m * (speed - v2)) - thrown, at, ForceMode.Impulse);
            hull.struck.Invoke(this, at, true, v2);
            inside = false;
            passingUntil = Time.time + 0.3f;
            float rest = time - toFar;
            Vector3 vOut = moving + dir * v2;
            WaterSample sea = insideSample;
            if (sea.HasWater && sea.Surface.HeightAt(Bridge.V(at)) > at.y)
            {
                // Out into the sea: the water's drag slows it from there, as v / (1 + k v t) (net weight aside), and it
                // is in the water already, so it makes no splash.
                Core.InTheWaterAlready();
                float rho = (float)sea.Density;
                float vol = (float)Core.Shape.Volume;
                float taken = m + addedMassCoefficient * rho * vol;
                float k = 0.5f * dragCoefficient * Mathf.PI * a * a * rho / taken;
                float net = g * (m - rho * vol) / taken;
                float gone = k > 1e-9f ? Mathf.Log(1f + k * v2 * rest) / k : v2 * rest;
                Vector3 vEnd = moving + dir * (v2 / (1f + k * v2 * rest));
                return new[] { at + moving * rest + dir * gone + Vector3.down * (0.5f * net * rest * rest), vEnd + Vector3.down * (net * rest) };
            }
            return new[] { at + vOut * rest + Vector3.down * (0.5f * g * rest * rest), vOut + Vector3.down * (g * rest) };
        }

        /// <summary>The ball stops in the wood at `at`: the body takes all its momentum (relative to the body there), and
        /// the ball is gone.</summary>
        void Lodge(Rigidbody body, BoundedBuoyancyHull hull, Collider[] colliders, Vector3 at, Vector3 v)
        {
            if (body != null && !body.isKinematic)
                body.AddForceAtPosition((v - VelocityAt(body, at)) * OwnMass, at, ForceMode.Impulse);
            if (colliders != null)
                foreach (Collider c in colliders)
                    if (c != null && own != null && c != own)
                        Physics.IgnoreCollision(own, c, true);
            if (hull != null)
                hull.struck.Invoke(this, at, false, 0f);
            inside = false;
            Rigidbody rb = Body;
            Bridge.SetVelocity(rb, Vector3.zero);
            rb.isKinematic = true;
            rb.detectCollisions = false;
            Destroy(gameObject);
        }

        /// <summary>Where a path along `dir` from `at`, inside the body made of `colliders`, comes out of it. Each collider
        /// is taken as solid from where the path first meets it to where it last leaves it, and colliders that touch or
        /// overlap along the path (a ship of several pieces) as one: the path leaves the body where the first of these
        /// solids ends.</summary>
        static bool FarSide(Collider[] colliders, Vector3 at, Vector3 dir, out Vector3 point, out Vector3 normal)
        {
            point = Vector3.zero;
            normal = Vector3.zero;
            const float far = 1000f, touching = 0.1f;
            var spans = new List<Span>();
            foreach (Collider c in colliders)
            {
                RaycastHit into, outOf;
                if (c == null || c.isTrigger)
                    continue;
                if (!c.Raycast(new Ray(at - dir * far, dir), out into, 2f * far) || !c.Raycast(new Ray(at + dir * far, -dir), out outOf, 2f * far))
                    continue;
                float s0 = into.distance - far, s1 = far - outOf.distance;
                if (s1 > s0)
                    spans.Add(new Span { From = s0, To = s1, Normal = outOf.normal });
            }
            spans.Sort((x, y) => x.From.CompareTo(y.From));
            bool found = false;
            float end = 0f;
            Vector3 n = Vector3.zero;
            foreach (Span sp in spans)
            {
                if (found && sp.From > end + touching)
                    break;   // a separate part further on
                if (!found && sp.To <= 0.01f)
                    continue;   // behind the point it went in at
                if (!found || sp.To > end)
                {
                    end = sp.To;
                    n = sp.Normal;
                }
                found = true;
            }
            if (!found)
                return false;
            point = at + dir * end;
            normal = n;
            return true;
        }

        struct Span
        {
            public float From, To;
            public Vector3 Normal;
        }
    }
}
