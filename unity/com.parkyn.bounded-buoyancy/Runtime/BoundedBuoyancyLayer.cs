// Bounded Buoyancy for Unity. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using UnityEngine;

namespace BoundedBuoyancy
{
    /// <summary>
    /// A layer of a Bounded Buoyancy Water below its top water (its Layers): water under oil, brine on the bottom, the
    /// colder, saltier water under a thermocline, mud, quicksand. It runs from its depth down to the next layer's (or the
    /// bottom of the water), level whatever the waves do on the surface.
    /// <para>Bodies feel the weight of what they displace in each layer: a body less dense than a layer floats on it, one
    /// denser sinks through it, and one between the two settles on the boundary, partly in each, as an egg does on brine
    /// under fresh water.</para>
    /// </summary>
    [System.Serializable]
    public class BoundedBuoyancyLayer
    {
        [Tooltip("How far below the water's surface the layer starts, m (from the water's object, so the waves do not move it).")]
        [Min(0f)] public float depth = 1f;

        [Tooltip("Its density at its top, kg/m³: fresh water 1000, sea water 1025, a saturated brine 1200, liquid mud 1200 to 1400, quicksand 1600 to 2000.")]
        [Min(0.001f)] public float density = 1025f;

        [Tooltip("How much denser it gets for each metre deeper, kg/m³ per m (a halocline: a few). 0 for the same all through.")]
        public float densityGradient = 0f;

        [Tooltip("How much harder it is to move through than water, as a multiple of each body's drag: 1 for water, 20 to 200 for mud, more for quicksand.")]
        [Min(0f)] public float drag = 1f;

        [Tooltip("Its colour and how murky it is, when the water draws its layers.")]
        public Color color = new Color(0.1f, 0.25f, 0.35f, 0.35f);
    }
}
