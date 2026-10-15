// Bounded Buoyancy for Unity. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System.Collections.Generic;
using UnityEngine;

namespace BoundedBuoyancy
{
    /// <summary>
    /// Steps every buoyant body, water and wind at once. You need it only if you change Unity's physics simulation
    /// mode. With the default (Physics.simulationMode = FixedUpdate) each component steps itself in FixedUpdate. With
    /// Update it is called for you each frame. With Script, call Step(dt) (and Step2D(dt) for 2D) just before your own
    /// Physics.Simulate(dt) (or Physics2D.Simulate(dt)).
    /// </summary>
    public static class BoundedBuoyancySimulation
    {
        internal static readonly List<BoundedBuoyancyBody> bodies = new List<BoundedBuoyancyBody>();
        internal static readonly List<BoundedBuoyancyBody2D> bodies2D = new List<BoundedBuoyancyBody2D>();
        static BoundedBuoyancySimulationDriver s_Driver;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            bodies.Clear();
            bodies2D.Clear();
            s_Driver = null;
        }

        /// <summary>One step of dt seconds for every 3D water, wind, body and line: clocks first, then bodies, then lines.</summary>
        public static void Step(float dt)
        {
            for (int i = 0; i < BoundedBuoyancyWater.active.Count; i++)
                BoundedBuoyancyWater.active[i].Advance(dt);
            for (int i = 0; i < BoundedBuoyancyWind.active.Count; i++)
                BoundedBuoyancyWind.active[i].Advance(dt);
            for (int i = 0; i < bodies.Count; i++)
                bodies[i].Step(dt);
            for (int i = 0; i < BoundedBuoyancyLine.active.Count; i++)
                BoundedBuoyancyLine.active[i].Step(dt);
        }

        /// <summary>One step of dt seconds for every 2D water, wind and body.</summary>
        public static void Step2D(float dt)
        {
            for (int i = 0; i < BoundedBuoyancyWater2D.active.Count; i++)
                BoundedBuoyancyWater2D.active[i].Advance(dt);
            for (int i = 0; i < BoundedBuoyancyWind2D.active.Count; i++)
                BoundedBuoyancyWind2D.active[i].Advance(dt);
            for (int i = 0; i < bodies2D.Count; i++)
                bodies2D[i].Step(dt);
        }

        /// <summary>Makes sure something steps the bodies each frame when physics runs in Update.</summary>
        internal static void EnsureDriver()
        {
            if (s_Driver != null || !Application.isPlaying)
                return;
            var go = new GameObject("Bounded Buoyancy Simulation");
            Object.DontDestroyOnLoad(go);
            s_Driver = go.AddComponent<BoundedBuoyancySimulationDriver>();
        }
    }
}
