// Bounded Buoyancy for Unity. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using BoundedBuoyancy.Core;
using UnityEngine;

namespace BoundedBuoyancy
{
    /// <summary>
    /// The surface of another water system, for a Bounded Buoyancy Water to float bodies on: the other system draws the
    /// water, and Bounded Buoyancy does the physics.
    /// <para>To connect an ocean or water asset, derive from this, override HeightAt (and HeightsAt if the asset can
    /// answer many points at once), add the component to the same object as a Bounded Buoyancy Water and set the water's
    /// Surface Source to it. Bounded Buoyancy HDRP Water does this for the Water System of the High Definition Render
    /// Pipeline.</para>
    /// </summary>
    public abstract class BoundedBuoyancySurface : MonoBehaviour
    {
        [Tooltip("The most the surface rises or falls from its average height, in metres. Set it at least as high as the highest crest: bodies further than twice this from the surface are not measured.")]
        [Min(0f)] public float maxWave = 2f;

        /// <summary>Height (world Y) of the surface above or below the world point `worldPoint`.</summary>
        public abstract float HeightAt(Vector3 worldPoint);

        /// <summary>Heights for many points at once: heights[i] for points[i], for i below count. Each body asks for all
        /// its vertices in one call. Override this if your water can answer a batch faster than one point at a time.</summary>
        public virtual void HeightsAt(Vector3[] points, float[] heights, int count)
        {
            for (int i = 0; i < count; i++)
                heights[i] = HeightAt(points[i]);
        }

        /// <summary>The most the surface rises or falls from its average height (metres).</summary>
        public virtual float MaxWave { get { return maxWave; } }

        /// <summary>The water's velocity (world frame) at the world point `worldPoint`, if this water system knows
        /// how its water flows (a river's current): return true and set `flow`. False (the default) carries bodies
        /// with the Bounded Buoyancy Water's own Current instead.</summary>
        public virtual bool FlowAt(Vector3 worldPoint, out Vector3 flow)
        {
            flow = Vector3.zero;
            return false;
        }

        /// <summary>The water's upward acceleration (m/s²) at the world point `worldPoint` in the water, if this water
        /// system knows how its water moves in its waves: return true and set `acceleration`. Bodies are then pushed by
        /// the water's motion, rising and falling with the waves as far as the water does. False (the default) buoys them
        /// on the surface alone, so a small raft rises and falls a little further than the water (about 105% on waves 20
        /// times its length). For waves made of sines, each wave's part is −height × ω² × sin(phase) × e^(k z), ω its
        /// frequency, k its wave number and z the point's height above the water's average level (no more than 0).</summary>
        public virtual bool VerticalAccelerationAt(Vector3 worldPoint, out float acceleration)
        {
            acceleration = 0f;
            return false;
        }

        /// <summary>The same averaged over a footprint `across` (world X) by `along` (world Z) metres centred on
        /// `worldPoint`, as bodies ask for it. By default the average at four points (two-point Gauss–Legendre each way);
        /// override it if your water can average a footprint itself.</summary>
        public virtual bool VerticalAccelerationOver(Vector3 worldPoint, float across, float along, out float acceleration)
        {
            float hx = across / (2f * Mathf.Sqrt(3f)), hz = along / (2f * Mathf.Sqrt(3f)), sum = 0f;
            acceleration = 0f;
            for (int i = 0; i < 4; i++)
            {
                float a;
                if (!VerticalAccelerationAt(worldPoint + new Vector3((i & 1) != 0 ? hx : -hx, 0f, (i & 2) != 0 ? hz : -hz), out a))
                    return false;
                sum += a;
            }
            acceleration = 0.25f * sum;
            return true;
        }

        /// <summary>Called when the component is added: links it to the Bounded Buoyancy Water on the same object.</summary>
        protected virtual void Reset()
        {
            var water = GetComponent<BoundedBuoyancyWater>();
            if (water != null && water.surfaceSource == null)
            {
                RecordChange(water);
                water.surfaceSource = this;
                ChangeRecorded(water);
            }
        }

        /// <summary>In the editor, before changing another component from Reset: makes the change undoable.</summary>
        protected static void RecordChange(Object target)
        {
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(target, "Link Bounded Buoyancy Surface");
#endif
        }

        /// <summary>In the editor, after the change: keeps it on prefab instances.</summary>
        protected static void ChangeRecorded(Object target)
        {
#if UNITY_EDITOR
            UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(target);
#endif
        }
    }

    /// <summary>A BoundedBuoyancySurface as the engine-free core sees it.</summary>
    internal sealed class SurfaceLink : ISurfaceHeights, ISurfaceFlow, ISurfaceMotion
    {
        internal BoundedBuoyancySurface Target;
        Vector3[] points = new Vector3[0];
        float[] heights = new float[0];

        public void HeightsAt(V3[] at, double[] result, int count)
        {
            if (points.Length < count)
            {
                points = new Vector3[count];
                heights = new float[count];
            }
            for (int i = 0; i < count; i++)
                points[i] = Bridge.U(at[i]);
            Target.HeightsAt(points, heights, count);
            for (int i = 0; i < count; i++)
                result[i] = heights[i];
        }

        public double MaxWave { get { return Target.MaxWave; } }

        public bool FlowAt(V3 p, out V3 flow)
        {
            Vector3 f;
            if (Target.FlowAt(Bridge.U(p), out f))
            {
                flow = Bridge.V(f);
                return true;
            }
            flow = V3.Zero;
            return false;
        }

        public bool VerticalAccelerationAt(V3 p, double across, double along, out double acceleration)
        {
            float a;
            bool known = across == 0.0 && along == 0.0 ? Target.VerticalAccelerationAt(Bridge.U(p), out a)
                : Target.VerticalAccelerationOver(Bridge.U(p), (float)across, (float)along, out a);
            acceleration = known ? a : 0.0;
            return known;
        }
    }
}
