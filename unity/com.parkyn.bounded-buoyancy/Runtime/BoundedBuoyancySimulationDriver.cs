// Bounded Buoyancy for Unity. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using UnityEngine;

namespace BoundedBuoyancy
{
    /// <summary>Steps buoyancy each frame when Unity's physics runs in Update. Made by itself when needed.</summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(-100)]
    internal sealed class BoundedBuoyancySimulationDriver : MonoBehaviour
    {
        void Update()
        {
            if (Physics.simulationMode == SimulationMode.Update)
                BoundedBuoyancySimulation.Step(Time.deltaTime);
            if (Physics2D.simulationMode == SimulationMode2D.Update)
                BoundedBuoyancySimulation.Step2D(Time.deltaTime);
        }
    }
}
