// Bounded Buoyancy for Unity. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System.Collections.Generic;
using BoundedBuoyancy.Core;
using UnityEngine;

namespace BoundedBuoyancy
{
    /// <summary>
    /// A warm (or cold) place in the air: a thermal over sunlit ground, the hot air over a fire or a chimney, a cold pool
    /// in a valley at night.
    /// <para>Air there is warmer than the air around it by Warming at its middle, less toward its edge and none beyond it,
    /// so it is less dense (at the same pressure, as the gas law gives): a balloon rises faster in it. A thermal is also
    /// air on the way up: Updraft is how fast it rises at its middle, and bodies in it are carried up by it as by wind
    /// (Bounded Buoyancy Wind's TotalAt includes it), so a glider or a soaring bird that circles in it climbs. A negative
    /// Warming is cold air, and a negative Updraft air sinking.</para>
    /// <para>It is a column standing up from this object along its Y (a thermal, a plume), fading out over its top tenth,
    /// or a ball round it. It acts on the air only, for bodies in real air (not Fixed Air).</para>
    /// </summary>
    [AddComponentMenu("Physics/Bounded Buoyancy Thermal")]
    public class BoundedBuoyancyThermal : MonoBehaviour
    {
        public enum Shape
        {
            /// <summary>Upright, from this object up to its Height.</summary>
            Column,
            /// <summary>A ball of the given Radius round this object.</summary>
            Ball,
        }

        [Tooltip("Column: upright from this object up to its height. Ball: a ball round it.")]
        public Shape shape = Shape.Column;

        [Tooltip("Radius, m: a few hundred metres for a thermal over fields, a few metres over a bonfire.")]
        [Min(0.01f)] public float radius = 50f;

        [Tooltip("Height of a column, m: thermals often reach 1 to 2 km, to the base of the cloud they make.")]
        [Min(0.01f)] public float height = 1000f;

        [Tooltip("How much warmer than the air around it the air at its middle is, °C: 1 to 3 in a thermal, tens over a fire. Negative: colder.")]
        public float warming = 2f;

        [Tooltip("How fast the air at its middle rises, m/s: 1 to 5 in a thermal. Negative: sinking air.")]
        [Range(-50f, 50f)] public float updraft = 2f;

        internal static readonly List<BoundedBuoyancyThermal> active = new List<BoundedBuoyancyThermal>();

        /// <summary>Every enabled Bounded Buoyancy Thermal.</summary>
        public static IReadOnlyList<BoundedBuoyancyThermal> Active { get { return active; } }

        readonly ThermalField core = new ThermalField();
        PhysicsScene physicsScene;

        /// <summary>The engine-free thermal.</summary>
        public ThermalField Core { get { Sync(); return core; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { active.Clear(); }

        void OnEnable()
        {
            if (!active.Contains(this))
                active.Add(this);
            physicsScene = gameObject.scene.GetPhysicsScene();
        }

        void OnDisable()
        {
            active.Remove(this);
        }

        void Sync()
        {
            core.Position = Bridge.V(transform.position);
            core.Rotation = Bridge.M(transform.rotation);
            core.Ball = shape == Shape.Ball;
            core.Radius = Mathf.Max(radius, 0.01f);
            core.Height = Mathf.Max(height, 0.01f);
            core.Warming = warming;
            core.Updraft = updraft;
        }

        /// <summary>Put the thermals in `scene`, up to date, into `into`.</summary>
        internal static void Gather(PhysicsScene scene, List<ThermalField> into)
        {
            into.Clear();
            for (int i = 0; i < active.Count; i++)
                if (active[i].physicsScene == scene)
                    into.Add(active[i].Core);
        }

        /// <summary>Its rising air at p if it is in `scene`, else nothing.</summary>
        internal V3 UpdraftIn(PhysicsScene scene, V3 p)
        {
            return physicsScene == scene ? Core.UpdraftAt(p) : V3.Zero;
        }

        /// <summary>How strong it is at the world point p: 1 at its middle, falling smoothly to 0 at its edge.</summary>
        public float StrengthAt(Vector3 p) { return (float)Core.StrengthAt(Bridge.V(p)); }

        /// <summary>How much warmer the air is at p for this thermal, °C.</summary>
        public float WarmingAt(Vector3 p) { return (float)Core.WarmingAt(Bridge.V(p)); }

        /// <summary>The rising air at p (world velocity, m/s).</summary>
        public Vector3 UpdraftAt(Vector3 p) { return Bridge.U(Core.UpdraftAt(Bridge.V(p))); }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.55f, 0.2f, 0.8f);
            if (shape == Shape.Ball)
            {
                Gizmos.DrawWireSphere(transform.position, radius);
                return;
            }
            Vector3 up = transform.up * height;
            Gizmos.DrawWireSphere(transform.position, radius);
            Gizmos.DrawWireSphere(transform.position + up, radius);
            Gizmos.DrawLine(transform.position, transform.position + up);
        }
    }
}
