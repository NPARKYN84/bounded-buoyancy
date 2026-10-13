// Bounded Buoyancy for Unity: play-mode test helper. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using UnityEngine;

namespace BoundedBuoyancy.Tests
{
    /// <summary>A stand-in for another water system: a Bounded Buoyancy Water's waves, moved by `shift`.</summary>
    public class ShiftedSurface : BoundedBuoyancySurface
    {
        public BoundedBuoyancyWater from;
        public Vector3 shift;

        public override float HeightAt(Vector3 worldPoint)
        {
            return from.HeightAt(worldPoint - shift);
        }
    }
}
