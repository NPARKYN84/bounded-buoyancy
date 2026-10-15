// Bounded Buoyancy for Unity. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System.Collections.Generic;
using UnityEngine;

namespace BoundedBuoyancy
{
    /// <summary>
    /// Marks a Rigidbody as bait (a baited hook, a lure): Bounded Buoyancy Fish without a Bait of their own go for any
    /// enabled one within their Sense Range that is under the water and not kinematic. Put it on the object with the
    /// Rigidbody. (The Godot plugin uses the group "bounded_buoyancy_bait"; tags would have to be added to each project.)
    /// </summary>
    [AddComponentMenu("Physics/Bounded Buoyancy Bait")]
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class BoundedBuoyancyBait : MonoBehaviour
    {
        internal static readonly List<BoundedBuoyancyBait> active = new List<BoundedBuoyancyBait>();

        /// <summary>Every enabled Bounded Buoyancy Bait.</summary>
        public static IReadOnlyList<BoundedBuoyancyBait> Active { get { return active; } }

        Rigidbody rb;

        /// <summary>The bait's Rigidbody.</summary>
        public Rigidbody Body { get { if (rb == null) rb = GetComponent<Rigidbody>(); return rb; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { active.Clear(); }

        void OnEnable()
        {
            if (!active.Contains(this))
                active.Add(this);
        }

        void OnDisable()
        {
            active.Remove(this);
        }
    }
}
