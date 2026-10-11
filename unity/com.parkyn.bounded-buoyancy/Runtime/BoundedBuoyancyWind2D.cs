// Bounded Buoyancy for Unity, 2D. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System.Collections.Generic;
using BoundedBuoyancy.Core;
using UnityEngine;

namespace BoundedBuoyancy
{
    /// <summary>Wind for Bounded Buoyancy Body 2D, with gusts. It pushes on the part of each body out of the water
    /// and on any sail.</summary>
    [AddComponentMenu("Physics 2D/Bounded Buoyancy Wind 2D")]
    [DefaultExecutionOrder(-100)]
    public class BoundedBuoyancyWind2D : MonoBehaviour
    {
        [Tooltip("Velocity of the wind, m/s.")]
        public Vector2 velocity = new Vector2(4f, 0f);

        [Tooltip("How much gusts change the speed: 0.3 makes it vary by up to 30% either side.")]
        [Range(0f, 1f)] public float gustStrength = 0.3f;

        [Tooltip("Typical time from one gust to the next, in seconds.")]
        [Min(0.1f)] public float gustPeriod = 5f;

        [Tooltip("Blow everywhere. Turn off to limit the wind to a rectangle of the size below, centred on this object.")]
        public bool everywhere = true;

        [Tooltip("Size of the rectangle the wind fills when Everywhere is off, in metres.")]
        public Vector2 size = new Vector2(10f, 10f);

        internal static readonly List<BoundedBuoyancyWind2D> active = new List<BoundedBuoyancyWind2D>();

        public static IReadOnlyList<BoundedBuoyancyWind2D> Active { get { return active; } }

        readonly WindField2D core = new WindField2D();
        internal PhysicsScene2D PhysicsScene;

        public float WindTime { get { return (float)core.Time; } set { core.Time = value; } }

        public WindField2D Core { get { return core; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { active.Clear(); }

        void OnEnable()
        {
            if (!active.Contains(this))
                active.Add(this);
            PhysicsScene = gameObject.scene.GetPhysicsScene2D();
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
            if (Bridge.FixedStep2D)
                Advance(Time.fixedDeltaTime);
        }

        /// <summary>Copy the settings and advance the wind clock by one physics step.</summary>
        internal void Advance(float dt)
        {
            Sync();
            core.Time += dt;
        }

        public void Sync()
        {
            core.Position = Bridge.V((Vector2)transform.position);
            core.Velocity = Bridge.V(velocity);
            core.GustStrength = gustStrength;
            core.GustPeriod = gustPeriod;
            core.Everywhere = everywhere;
            core.Size = Bridge.V(size);
        }

        public Vector2 VelocityAt(Vector2 p) { return Bridge.U(core.VelocityAt(Bridge.V(p))); }

        public static Vector2 TotalAt(Vector2 p, PhysicsScene2D scene) { return Bridge.U(TotalAt(Bridge.V(p), scene)); }

        internal static V2 TotalAt(V2 p, PhysicsScene2D scene)
        {
            V2 w = V2.Zero;
            for (int i = 0; i < active.Count; i++)
                if (active[i].PhysicsScene == scene)
                    w += active[i].core.VelocityAt(p);
            return w;
        }

        void OnDrawGizmos()
        {
            Vector3 c = transform.position;
            Gizmos.color = new Color(0.85f, 0.9f, 1f, 0.8f);
            if (!everywhere)
                Gizmos.DrawWireCube(c, new Vector3(size.x, size.y, 0f));
            if (velocity != Vector2.zero)
            {
                Vector3 d = (Vector3)velocity.normalized * 1.2f;
                Vector3 side = new Vector3(-d.y, d.x, 0f) * 0.25f;
                Gizmos.DrawLine(c - d, c + d);
                Gizmos.DrawLine(c + d, c + 0.7f * d + side);
                Gizmos.DrawLine(c + d, c + 0.7f * d - side);
            }
        }
    }
}
