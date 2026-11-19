// Bounded Buoyancy for Unity, 2D. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using BoundedBuoyancy.Core;
using UnityEngine;
using UnityEngine.Events;

namespace BoundedBuoyancy
{
    /// <summary>
    /// The planking of a ship in a side view (or of anything shot can strike), as Bounded Buoyancy Hull in 3D: how thick
    /// it is and what it is made of. Put it on the body (the object with the Rigidbody2D).
    /// <para>Shot (Bounded Buoyancy Cannonball 2D) that strikes the body is resisted by the wood with a pressure σ + ρ v²
    /// on its cross-section (Poncelet's law; see Planking). Shot with the speed goes in through the planking where it
    /// strikes and out through the planking on the far side, slower, punching a hole each time; through a hole below the
    /// waterline the sea comes in (the hole goes in the nearest Bounded Buoyancy Compartment 2D), so a ship holed there
    /// floods and sinks. Shot without the speed lodges in the wood. The body takes the momentum the shot loses. Shot
    /// meeting the planking more than about 78° off square glances off.</para>
    /// </summary>
    [AddComponentMenu("Physics 2D/Bounded Buoyancy Hull 2D")]
    public class BoundedBuoyancyHull2D : MonoBehaviour
    {
        [Tooltip("Thickness of the planking, m. A frigate's side was about 0.4 m of oak at the waterline; a small boat's a few centimetres.")]
        [Min(0.001f)] public float thickness = 0.2f;

        [Tooltip("Density of the wood, kg/m³: oak 750. It resists fast shot (ρ v²).")]
        [Min(1f)] public float density = 750f;

        [Tooltip("Strength of the wood against shot, MPa: oak 15. It stops slow shot.")]
        [Min(0f)] public float strength = 15f;

        [Tooltip("Size of a shot hole, as a multiple of the shot's own cross-section.")]
        [Min(0f)] public float holeSize = 1f;

        [Tooltip("Shot has struck the planking: the shot, where (world), whether it went through, and its speed out (m/s; 0 if it lodged).")]
        public UnityEvent<BoundedBuoyancyCannonball2D, Vector2, bool, float> struck = new UnityEvent<BoundedBuoyancyCannonball2D, Vector2, bool, float>();

        [Tooltip("A hole has been made: where (world), its area (m²), and the compartment it is in (none if the body has none).")]
        public UnityEvent<Vector2, float, BoundedBuoyancyCompartment2D> holed = new UnityEvent<Vector2, float, BoundedBuoyancyCompartment2D>();

        /// <summary>The speed of shot of `mass` (kg) and `radius` (m) after going through this planking, met at `cosIn`
        /// (the cosine of its angle off square) at `speed`. 0 if it lodges.</summary>
        public float SpeedThrough(float mass, float radius, float speed, float cosIn)
        {
            double path = thickness / Mathf.Max(cosIn, (float)Planking.Glance);
            return (float)Planking.SpeedAfter(mass, Mathf.PI * radius * radius, speed, path, density, strength * 1e6);
        }

        /// <summary>Make a hole of `area` m² at `at` (world) in the body's nearest compartment, if it has any; invokes
        /// holed. Returns the compartment.</summary>
        public BoundedBuoyancyCompartment2D MakeHole(Vector2 at, float area)
        {
            BoundedBuoyancyBody2D body = GetComponentInParent<BoundedBuoyancyBody2D>();
            BoundedBuoyancyCompartment2D best = null;
            float bestD = float.PositiveInfinity;
            if (body != null)
            {
                foreach (BoundedBuoyancyCompartment2D c in body.Compartments)
                {
                    if (c == null || !c.isActiveAndEnabled)
                        continue;
                    float d = c.DistanceTo(at);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = c;
                    }
                }
            }
            if (best != null)
                best.AddHole(at, area);
            holed.Invoke(at, area, best);
            return best;
        }
    }
}
