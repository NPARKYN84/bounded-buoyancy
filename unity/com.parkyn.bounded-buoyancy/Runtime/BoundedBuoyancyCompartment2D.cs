// Bounded Buoyancy for Unity, 2D. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System.Collections.Generic;
using BoundedBuoyancy.Core;
using UnityEngine;

namespace BoundedBuoyancy
{
    /// <summary>
    /// A space inside a Bounded Buoyancy Body 2D that can flood, in a side view: a ship's hold, a tank, the inside of a
    /// boat. As Bounded Buoyancy Compartment in 3D.
    /// <para>Put it on an object under the body and set its size: a rectangle centred on this object, inside the body's
    /// shape, as deep into the screen as the body's thickness. Give it holes (in the inspector, or from code with AddHole,
    /// say where a cannonball hit) and water pours in through those below the waterline by the orifice law
    /// Q = C A √(2 g Δh); holes above the water let flood water drain out again. The water lies level, and its weight
    /// acts at its own centre, so a holed boat settles, trims toward its flooded end, and sinks when it has taken in more
    /// than its reserve of buoyancy will carry.</para>
    /// </summary>
    [AddComponentMenu("Physics 2D/Bounded Buoyancy Compartment 2D")]
    public class BoundedBuoyancyCompartment2D : MonoBehaviour
    {
        [Tooltip("Width and height of the space, in metres, centred on this object. Keep it inside the body's shape, and this object's scale at 1.")]
        public Vector2 size = Vector2.one;

        [Tooltip("The holes, each as (x, y, area): its position in this object's own coordinates, on or inside the rectangle, and its area in m².")]
        public List<Vector3> holes = new List<Vector3>();

        [Tooltip("Discharge coefficient of the holes: 0.6 for a sharp-edged hole, up to about 0.95 for a smooth one.")]
        [Range(0.1f, 1f)] public float dischargeCoefficient = 0.6f;

        [Tooltip("Water the pumps take out, in m³ per second, while there is water to take.")]
        [Min(0f)] public float pumpRate;

        [Tooltip("Water in the compartment now, in m³. Set it to start part full, or to fill or empty it at once.")]
        [Min(0f)] public float floodVolume;

        readonly Compartment2D core = new Compartment2D();
        BoundedBuoyancyBody2D body;
        float written = -1f;

        /// <summary>The engine-free compartment the body steps.</summary>
        public Compartment2D Core { get { return core; } }
        /// <summary>The body it is in.</summary>
        public BoundedBuoyancyBody2D Body { get { return body; } }
        /// <summary>Volume of the space (m³).</summary>
        public float Capacity { get { return size.x * size.y * (body != null ? body.thickness : 0.2f); } }
        /// <summary>Fraction of the space full of water, 0 to 1.</summary>
        public float FillFraction { get { return Capacity > 0f ? Mathf.Clamp01((float)core.FloodVolume / Capacity) : 0f; } }
        /// <summary>Flow of water in through the holes over the last step, m³/s (negative when it drains out).</summary>
        public float Inflow { get { return (float)core.Inflow; } }
        /// <summary>Height of the water inside (world y) over the last step.</summary>
        public float WaterLevel { get { return (float)core.WaterLevel; } }
        /// <summary>Centre of the water inside (world) over the last step.</summary>
        public Vector2 WaterCentre { get { return Bridge.U(core.WaterCentre); } }

        /// <summary>Make a hole of `area` m² at the world point `at` (moved onto the rectangle if it lies outside it).
        /// Returns its index in holes.</summary>
        public int AddHole(Vector2 at, float area)
        {
            Vector2 l = transform.InverseTransformPoint(at);
            Vector2 h = 0.5f * size;
            l = new Vector2(Mathf.Clamp(l.x, -h.x, h.x), Mathf.Clamp(l.y, -h.y, h.y));
            holes.Add(new Vector3(l.x, l.y, Mathf.Max(area, 0f)));
            return holes.Count - 1;
        }

        /// <summary>Close every hole.</summary>
        public void PatchHoles()
        {
            holes.Clear();
        }

        /// <summary>Distance from the world point `at` to the rectangle (0 inside it).</summary>
        public float DistanceTo(Vector2 at)
        {
            Vector2 l = transform.InverseTransformPoint(at);
            Vector2 h = 0.5f * size;
            return (l - new Vector2(Mathf.Clamp(l.x, -h.x, h.x), Mathf.Clamp(l.y, -h.y, h.y))).magnitude;
        }

        void OnEnable()
        {
            Register();
        }

        void OnDisable()
        {
            if (body != null)
                body.RemoveCompartment(this);
            body = null;
        }

        void OnTransformParentChanged()
        {
            if (isActiveAndEnabled)
                Register();
        }

        void OnValidate()
        {
            size = Vector2.Max(size, new Vector2(0.001f, 0.001f));
            floodVolume = Mathf.Max(floodVolume, 0f);
            pumpRate = Mathf.Max(pumpRate, 0f);
        }

        /// <summary>Find the body above this object and join it (leaving any other).</summary>
        internal void Register()
        {
            BoundedBuoyancyBody2D found = GetComponentInParent<BoundedBuoyancyBody2D>();
            if (found == body)
                return;
            if (body != null)
                body.RemoveCompartment(this);
            body = found;
            if (body != null)
                body.AddCompartment(this);
        }

        /// <summary>The compartment as the core sees it, placed in the body's own coordinates.</summary>
        internal Compartment2D Prepare(Transform bodyTransform)
        {
            Vector2 local = Quaternion.Inverse(bodyTransform.rotation) * (transform.position - bodyTransform.position);
            core.Position = Bridge.V(local);
            core.Angle = (transform.eulerAngles.z - bodyTransform.eulerAngles.z) * Mathf.Deg2Rad;
            core.Size = Bridge.V(size);
            core.DischargeCoefficient = dischargeCoefficient;
            core.PumpRate = pumpRate;
            core.Holes.Clear();
            for (int i = 0; i < holes.Count; i++)
                core.Holes.Add(new Hole2D(new V2(holes[i].x, holes[i].y), holes[i].z));
            if (floodVolume != written)
                core.FloodVolume = floodVolume;   // set from outside since the last step
            return core;
        }

        /// <summary>After the body's step: the water inside, back where scripts and the inspector see it.</summary>
        internal void Finish()
        {
            floodVolume = (float)core.FloodVolume;
            written = floodVolume;
        }

        void OnDrawGizmos()
        {
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.color = new Color(0.2f, 0.5f, 0.9f, 0.8f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, size.y, 0f));
            Gizmos.color = new Color(0.95f, 0.3f, 0.2f, 0.9f);
            foreach (Vector3 h in holes)
                Gizmos.DrawSphere(new Vector3(h.x, h.y, 0f), Mathf.Max(0.02f, 0.5f * Mathf.Sqrt(h.z)));
            Gizmos.matrix = Matrix4x4.identity;
            if (Application.isPlaying && core.FloodVolume > 0.0)
            {
                // The water inside: the rectangle below its level.
                Gizmos.color = new Color(0.2f, 0.45f, 0.75f, 0.9f);
                Gizmos.DrawLine(new Vector3(transform.position.x - 0.5f * size.x, WaterLevel, 0f), new Vector3(transform.position.x + 0.5f * size.x, WaterLevel, 0f));
            }
        }
    }
}
