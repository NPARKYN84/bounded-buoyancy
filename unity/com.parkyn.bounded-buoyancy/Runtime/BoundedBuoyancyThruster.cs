// Bounded Buoyancy for Unity. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using BoundedBuoyancy.Core;
using UnityEngine;

namespace BoundedBuoyancy
{
    /// <summary>
    /// A propeller (in the water) or a fan (in the air) on a Bounded Buoyancy Body.
    /// <para>Put it on a GameObject anywhere under the body, facing the way it should push the body (its +Z). At rest it
    /// pushes with Max Thrust x Throttle; the push falls in a straight line as its speed through its medium along that
    /// direction grows, to nothing at Throttle x Max Speed. So it gives a boat a top speed of its own, and a reversed
    /// throttle pushes it astern. Out of its medium (a propeller lifted clear of the water, a fan dipped in it) it
    /// pushes nothing.</para>
    /// <para>Like the buoyancy, the push is bounded: worked out with the speed at the end of the step, against the
    /// inertia the body has at the thruster, it brings the body up to its speed but never past it, however strong the
    /// thruster or light the body.</para>
    /// </summary>
    [AddComponentMenu("Physics/Bounded Buoyancy Thruster")]
    [DisallowMultipleComponent]
    public class BoundedBuoyancyThruster : MonoBehaviour
    {
        [Tooltip("Water: a propeller or a water jet, pushing while under the water. Air: a fan or an air propeller (an airboat, an airship), pushing while above it.")]
        public Medium medium = Medium.Water;

        [Tooltip("Push at full throttle from rest, N.")]
        [Min(0f)] public float maxThrust = 200f;

        [Tooltip("Speed through the medium at which full throttle pushes no more, m/s.")]
        [Min(0.01f)] public float maxSpeed = 5f;

        [Tooltip("-1 (full astern) to 1 (full ahead).")]
        [Range(-1f, 1f)] public float throttle;

        [Tooltip("Diameter, m: how deep a propeller must be to work fully.")]
        [Min(0.01f)] public float diameter = 0.3f;

        readonly Thruster3D core = new Thruster3D();
        BoundedBuoyancyBody body;

        /// <summary>Mean push over the last physics step (world, N).</summary>
        public Vector3 Force { get { return Bridge.U(core.Force); } }
        /// <summary>Fraction of the thruster in its medium this step.</summary>
        public float Immersion { get { return (float)core.Immersion; } }
        /// <summary>The engine-free thruster, for reading anything else.</summary>
        public Thruster3D Core { get { return core; } }

        void OnEnable()
        {
            body = GetComponentInParent<BoundedBuoyancyBody>();
            if (body != null)
                body.AddAppendage(this);
            else
                Debug.LogWarning("Bounded Buoyancy Thruster '" + name + "' needs a Bounded Buoyancy Body on this object or one above it.", this);
        }

        void OnDisable()
        {
            if (body != null)
                body.RemoveAppendage(this);
            body = null;
        }

        void OnValidate()
        {
            maxThrust = Mathf.Max(maxThrust, 0f);
            maxSpeed = Mathf.Max(maxSpeed, 0.01f);
            diameter = Mathf.Max(diameter, 0.01f);
            throttle = Mathf.Clamp(throttle, -1f, 1f);
        }

        /// <summary>Hand the body the thruster for this step, in world space.</summary>
        internal Thruster3D Prepare()
        {
            core.Medium = medium;
            core.MaxThrust = maxThrust;
            core.MaxSpeed = maxSpeed;
            core.Throttle = throttle;
            core.Diameter = diameter;
            core.Position = Bridge.V(transform.position);
            core.Direction = Bridge.V(transform.forward);
            return core;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.5f * diameter);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * Mathf.Max(diameter, 0.3f));
        }
    }
}
