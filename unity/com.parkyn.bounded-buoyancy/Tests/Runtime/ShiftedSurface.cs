// Bounded Buoyancy for Unity: play-mode test helper. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using UnityEngine;

namespace BoundedBuoyancy.Tests
{
    /// <summary>A stand-in for another water system: a Bounded Buoyancy Water's waves, moved by `shift`, with how its
    /// water moves in them.</summary>
    public class ShiftedSurface : BoundedBuoyancySurface
    {
        public BoundedBuoyancyWater from;
        public Vector3 shift;

        public override float HeightAt(Vector3 worldPoint)
        {
            return from.HeightAt(worldPoint - shift);
        }

        public override bool VerticalAccelerationAt(Vector3 worldPoint, out float acceleration)
        {
            acceleration = from.VerticalAccelerationAt(worldPoint - shift);
            return true;
        }

        public override bool VerticalAccelerationOver(Vector3 worldPoint, float across, float along, out float acceleration)
        {
            acceleration = from.VerticalAccelerationAt(worldPoint - shift, across, along);
            return true;
        }
    }
}
