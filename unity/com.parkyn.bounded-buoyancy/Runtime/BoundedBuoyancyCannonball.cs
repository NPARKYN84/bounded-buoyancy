// Bounded Buoyancy for Unity. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
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
    /// is on and it strikes a hull instead of passing through it; Hit is invoked. Under the water and once slow, it is an
    /// ordinary Bounded Buoyancy Body: an iron ball sinks.</para>
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

        [Tooltip("The ball has struck something: what, where, and the ball's velocity just before.")]
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

        /// <summary>How many times the ball has skipped off the water.</summary>
        public int Skips { get { return Core.Skips; } }
        /// <summary>The ball's velocity as it last left the water (world).</summary>
        public Vector3 ExitVelocity { get { return Bridge.U(Core.ExitVelocity); } }
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
                Collider own = GetComponent<Collider>();
                foreach (Collider c in ignoring)
                    if (c != null && own != null && c != own)
                        Physics.IgnoreCollision(own, c, false);
                ignoring = null;
            }
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
            }
            flightVelocity = velocity;
            Core.FastBallDrag = true;
            Core.SpeedOfSound = speedOfSound;
            if (!ricochet || model != Model.Bounded)
                return false;
            if (!Core.TryStepBall(pose, Bridge.V(velocity), sample, wind, -Physics.gravity.y, dt))
                return false;
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
            ApplyMass((float)Core.InertialMass);
            ApplyAngularDamping((float)Core.AngularDamp);
            // The Rigidbody moves the ball on by the velocity it ends the step with, so it is given the chord of the
            // step's path, from where the ball was to where the short steps leave it; the real velocity at the end is
            // handed back at the start of the next step (StepVelocity). No teleport, and continuous collision
            // detection sweeps the chord.
            Vector3 start = Bridge.U(pose.Apply(Core.Shape.SphereCentre));
            chord = (Bridge.U(Core.BallCentre) - start) / dt;
            trueVelocity = Bridge.U(Core.VelocityTarget);
            carry = true;
            rb.AddForce(chord - velocity, ForceMode.VelocityChange);
            return true;
        }
    }
}
