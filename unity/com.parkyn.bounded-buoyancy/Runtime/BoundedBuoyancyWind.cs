// Bounded Buoyancy for Unity. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System.Collections.Generic;
using BoundedBuoyancy.Core;
using UnityEngine;

namespace BoundedBuoyancy
{
    /// <summary>
    /// Wind for Bounded Buoyancy Body, with gusts. It pushes on the part of each body out of the water, by drag on
    /// the body's speed through the air, and on any sail. Balloons drift with it, a boat with a sail runs before it
    /// and heels, and a crate barely moves. Gusts vary the speed and direction and travel downwind, so nearby bodies
    /// feel each gust one after another.
    /// <para>Turn the object to aim the wind: velocity is in its own frame. Turn off Everywhere to limit the wind to a
    /// box of the given size round the object, such as a gap between buildings or a fan.</para>
    /// </summary>
    [AddComponentMenu("Physics/Bounded Buoyancy Wind")]
    [DefaultExecutionOrder(-100)]
    public class BoundedBuoyancyWind : MonoBehaviour
    {
        [Tooltip("Velocity of the wind in m/s, in this object's own frame.")]
        public Vector3 velocity = new Vector3(5f, 0f, 0f);

        [Tooltip("How much gusts change the speed: 0.3 makes it vary by up to 30% either side of the mean.")]
        [Range(0f, 1f)] public float gustStrength = 0.3f;

        [Tooltip("Typical time from one gust to the next, in seconds.")]
        [Min(0.1f)] public float gustPeriod = 5f;

        [Tooltip("How far the gusts swing the direction, in degrees either side.")]
        [Range(0f, 90f)] public float directionSwing = 10f;

        [Tooltip("Blow everywhere in the scene. Turn off to limit the wind to the box below.")]
        public bool everywhere = true;

        [Tooltip("Size of the box the wind fills when Everywhere is off, centred on this object.")]
        public Vector3 size = new Vector3(10f, 10f, 10f);

        internal static readonly List<BoundedBuoyancyWind> active = new List<BoundedBuoyancyWind>();

        /// <summary>Every enabled Bounded Buoyancy Wind.</summary>
        public static IReadOnlyList<BoundedBuoyancyWind> Active { get { return active; } }

        readonly WindField core = new WindField();
        internal PhysicsScene PhysicsScene;

        /// <summary>Wind clock in seconds, advanced with the physics step.</summary>
        public float WindTime { get { return (float)core.Time; } set { core.Time = value; } }

        /// <summary>The engine-free wind field.</summary>
        public WindField Core { get { return core; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { active.Clear(); }

        void OnEnable()
        {
            if (!active.Contains(this))
                active.Add(this);
            PhysicsScene = gameObject.scene.GetPhysicsScene();
            Sync();
        }

        void OnDisable()
        {
            active.Remove(this);
        }

        void OnValidate()
        {
            gustPeriod = Mathf.Max(gustPeriod, 0.1f);
            if (isActiveAndEnabled)
                Sync();
        }

        void FixedUpdate()
        {
            if (Bridge.FixedStep3D)
                Advance(Time.fixedDeltaTime);
        }

        /// <summary>Copy the settings and advance the wind clock by one physics step.</summary>
        internal void Advance(float dt)
        {
            Sync();
            core.Time += dt;
        }

        /// <summary>Copy the transform and settings to the core (done every physics step).</summary>
        public void Sync()
        {
            core.Position = Bridge.V(transform.position);
            core.Rotation = Bridge.M(transform.rotation);
            core.Velocity = Bridge.V(velocity);
            core.GustStrength = gustStrength;
            core.GustPeriod = gustPeriod;
            core.DirectionSwingDegrees = directionSwing;
            core.Everywhere = everywhere;
            core.Size = Bridge.V(size);
        }

        /// <summary>Velocity of this wind at the world point p (world frame), gusts included.</summary>
        public Vector3 VelocityAt(Vector3 p) { return Bridge.U(core.VelocityAt(Bridge.V(p))); }

        /// <summary>Total wind at the world point p from every enabled wind in the given physics scene.</summary>
        public static Vector3 TotalAt(Vector3 p, PhysicsScene scene) { return Bridge.U(TotalAt(Bridge.V(p), scene)); }

        internal static V3 TotalAt(V3 p, PhysicsScene scene)
        {
            V3 w = V3.Zero;
            for (int i = 0; i < active.Count; i++)
                if (active[i].PhysicsScene == scene)
                    w += active[i].core.VelocityAt(p);
            return w;
        }

        void OnDrawGizmos()
        {
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.color = new Color(0.85f, 0.9f, 1f, 0.8f);
            if (!everywhere)
                Gizmos.DrawWireCube(Vector3.zero, size);
            if (velocity != Vector3.zero)
            {
                Vector3 d = velocity.normalized * 1.5f;
                Vector3 side = Vector3.Cross(d, Mathf.Abs(d.y) < 1.4f ? Vector3.up : Vector3.right).normalized * 0.35f;
                Gizmos.DrawLine(-d, d);
                Gizmos.DrawLine(d, d - 0.3f * d + side);
                Gizmos.DrawLine(d, d - 0.3f * d - side);
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
