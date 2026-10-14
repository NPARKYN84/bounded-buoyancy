// Bounded Buoyancy for Unity. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System.Collections.Generic;
using BoundedBuoyancy.Core;
using UnityEngine;

namespace BoundedBuoyancy
{
    /// <summary>
    /// A space inside a Bounded Buoyancy Body that can flood: a ship's hold, a bow or stern section, a tank, the inside
    /// of a barrel or a boat.
    /// <para>Put it on an object under the body and set its size: it is a box centred on this object, inside the body's
    /// shape. Give it holes (in the inspector, or from code with AddHole, say where a cannonball hit) and water pours in
    /// through those below the waterline, faster the deeper they are, by the orifice law Q = C A √(2 g Δh); holes above
    /// the water let flood water drain out again. The water inside stays level whichever way the body leans, and its
    /// weight acts at its own centre, which moves to the low side as the body leans (the free-surface effect). So a holed
    /// ship settles lower, lists toward its flooded side, and sinks or capsizes when it has taken in more than its reserve
    /// of buoyancy will carry. The hull keeps its buoyancy: the flood water is weight it carries, as in the added-weight
    /// method of naval architecture.</para>
    /// <para>Pumps (Pump Rate) take water out again. The compartment is taken to be vented, as a ship's holds are through
    /// their hatches: air gets out as water comes in.</para>
    /// </summary>
    [AddComponentMenu("Physics/Bounded Buoyancy Compartment")]
    public class BoundedBuoyancyCompartment : MonoBehaviour
    {
        [Tooltip("Width (X), height (Y) and length (Z) of the space, in metres, centred on this object. Keep it inside the body's shape, and this object's scale at 1.")]
        public Vector3 size = Vector3.one;

        [Tooltip("The holes, each as (x, y, z, area): its position in this object's own coordinates, on or inside the box, and its area in m² (a cannonball of 15 cm makes about 0.018 m²).")]
        public List<Vector4> holes = new List<Vector4>();

        [Tooltip("Discharge coefficient of the holes: 0.6 for a sharp-edged hole, up to about 0.95 for a smooth one.")]
        [Range(0.1f, 1f)] public float dischargeCoefficient = 0.6f;

        [Tooltip("Water the pumps take out, in m³ per second, while there is water to take.")]
        [Min(0f)] public float pumpRate;

        [Tooltip("Water in the compartment now, in m³. Set it to start part full, or to fill or empty it at once.")]
        [Min(0f)] public float floodVolume;

        readonly Compartment3D core = new Compartment3D();
        BoundedBuoyancyBody body;
        float written = -1f;

        /// <summary>The engine-free compartment the body steps.</summary>
        public Compartment3D Core { get { return core; } }
        /// <summary>Volume of the space (m³).</summary>
        public float Capacity { get { return size.x * size.y * size.z; } }
        /// <summary>Fraction of the space full of water, 0 to 1.</summary>
        public float FillFraction { get { return Capacity > 0f ? Mathf.Clamp01((float)core.FloodVolume / Capacity) : 0f; } }
        /// <summary>Flow of water in through the holes over the last step, m³/s (negative when it drains out).</summary>
        public float Inflow { get { return (float)core.Inflow; } }
        /// <summary>Height of the water inside (world Y) over the last step.</summary>
        public float WaterLevel { get { return (float)core.WaterLevel; } }
        /// <summary>Centre of the water inside (world) over the last step.</summary>
        public Vector3 WaterCentre { get { return Bridge.U(core.WaterCentre); } }

        /// <summary>Make a hole of `area` m² at the world point `at` (moved onto the box if it lies outside it). Returns
        /// its index in holes.</summary>
        public int AddHole(Vector3 at, float area)
        {
            Vector3 l = Quaternion.Inverse(transform.rotation) * (at - transform.position);
            Vector3 h = 0.5f * size;
            l = new Vector3(Mathf.Clamp(l.x, -h.x, h.x), Mathf.Clamp(l.y, -h.y, h.y), Mathf.Clamp(l.z, -h.z, h.z));
            holes.Add(new Vector4(l.x, l.y, l.z, Mathf.Max(area, 0f)));
            return holes.Count - 1;
        }

        /// <summary>Close every hole.</summary>
        public void PatchHoles()
        {
            holes.Clear();
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
            size = Vector3.Max(size, new Vector3(0.001f, 0.001f, 0.001f));
            floodVolume = Mathf.Max(floodVolume, 0f);
            pumpRate = Mathf.Max(pumpRate, 0f);
        }

        /// <summary>Find the body above this object and join it (leaving any other).</summary>
        internal void Register()
        {
            BoundedBuoyancyBody found = GetComponentInParent<BoundedBuoyancyBody>();
            if (found == body)
                return;
            if (body != null)
                body.RemoveCompartment(this);
            body = found;
            if (body != null)
                body.AddCompartment(this);
        }

        /// <summary>The compartment as the core sees it, placed in the body's own coordinates: from this object's place
        /// relative to the body's Transform, so that it is right even when the Transforms are interpolated, or not yet
        /// moved after a teleport (the core then puts it where the Rigidbody is).</summary>
        internal Compartment3D Prepare(Transform bodyTransform)
        {
            Quaternion inv = Quaternion.Inverse(bodyTransform.rotation);
            core.Pose = new BodyPose(Bridge.V(inv * (transform.position - bodyTransform.position)), Bridge.M(inv * transform.rotation));
            core.Size = Bridge.V(size);
            core.DischargeCoefficient = dischargeCoefficient;
            core.PumpRate = pumpRate;
            core.Holes.Clear();
            for (int i = 0; i < holes.Count; i++)
                core.Holes.Add(new Hole(new V3(holes[i].x, holes[i].y, holes[i].z), holes[i].w));
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

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.color = new Color(0.2f, 0.5f, 0.9f, 0.8f);
            Gizmos.DrawWireCube(Vector3.zero, size);
            Gizmos.color = new Color(0.95f, 0.3f, 0.2f, 0.9f);
            foreach (Vector4 h in holes)
                Gizmos.DrawSphere(new Vector3(h.x, h.y, h.z), Mathf.Max(0.02f, 0.5f * Mathf.Sqrt(h.w)));
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
