// Bounded Buoyancy for Unity. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
namespace BoundedBuoyancy
{
    /// <summary>How rough the sea is, by the sea state code sailors and weather services use (WMO), from calm to
    /// phenomenal. Pick one on a water's Sea State and its waves are set to a real sea's. Custom: set them yourself.</summary>
    public enum BoundedBuoyancySeaState
    {
        Custom, CalmGlassy, CalmRippled, Smooth, Slight, Moderate, Rough, VeryRough, High, VeryHigh, Phenomenal,
    }

    /// <summary>
    /// Sea states. Each is a band of significant wave height (the mean height, trough to crest, of the highest third of
    /// the waves), and the waves are given the middle of it: the highest of them, the main wave with the second on top,
    /// is that tall, trough to crest. Their length is that of a sea the wind has fully raised (Pierson and Moskowitz,
    /// 1964), about 39 times the height: a rough sea's waves are 3 m high and over 100 m long. As in the Godot plugin.
    /// </summary>
    public static class BoundedBuoyancySea
    {
        static readonly float[] Heights = { 0f, 0f, 0.05f, 0.3f, 0.875f, 1.875f, 3.25f, 5f, 7.5f, 11.5f, 16f };
        /// <summary>Waves a fully raised sea has, crest to crest, for each metre of significant height.</summary>
        public const float LengthPerHeight = 39f;
        /// <summary>The highest of a water's waves, trough to crest, over its Wave Height: 2 (1 + 0.4).</summary>
        public const float TroughToCrest = 2.8f;

        /// <summary>Significant wave height of `state` (m).</summary>
        public static float SignificantHeight(BoundedBuoyancySeaState state) { return Heights[(int)state]; }
        /// <summary>Wave Height for `state`: the main wave's height above the mean surface (m).</summary>
        public static float WaveHeightOf(BoundedBuoyancySeaState state) { return Heights[(int)state] / TroughToCrest; }
        /// <summary>Wave Length for `state` (m), or 0 for a calm sea (keep the length you have).</summary>
        public static float WaveLengthOf(BoundedBuoyancySeaState state) { return LengthPerHeight * Heights[(int)state]; }
    }
}
