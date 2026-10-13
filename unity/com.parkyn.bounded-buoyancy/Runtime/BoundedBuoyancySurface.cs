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
    internal sealed class SurfaceLink : ISurfaceHeights
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
    }
}
