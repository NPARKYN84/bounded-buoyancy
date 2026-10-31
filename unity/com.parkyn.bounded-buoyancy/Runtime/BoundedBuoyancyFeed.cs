// Bounded Buoyancy for Unity. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System.Collections.Generic;
using UnityEngine;

namespace BoundedBuoyancy
{
    /// <summary>
    /// Marks a Rigidbody as loose feed (a pellet, a ball of groundbait, a pinch of maggots thrown or catapulted in):
    /// Bounded Buoyancy Fish smell it from further off than bait (their Feed Range), swim to it while it is under the
    /// water and within their depths, and eat it (the object is destroyed); fed, a fish stays round where it ate and is
    /// keener to go for bait for a while. Put it on the object with the Rigidbody. (The Godot plugin uses the group
    /// "bounded_buoyancy_feed".)
    /// </summary>
    [AddComponentMenu("Physics/Bounded Buoyancy Feed")]
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class BoundedBuoyancyFeed : MonoBehaviour
    {
        internal static readonly List<BoundedBuoyancyFeed> active = new List<BoundedBuoyancyFeed>();

        /// <summary>Every enabled Bounded Buoyancy Feed.</summary>
        public static IReadOnlyList<BoundedBuoyancyFeed> Active { get { return active; } }

        Rigidbody rb;

        /// <summary>The feed's Rigidbody.</summary>
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
