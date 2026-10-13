// Bounded Buoyancy for Unity: link to the Water System of the High Definition Render Pipeline.
// Copyright (c) 2026 Nicholas Parkyn. MIT licence.
// This assembly is compiled only when HDRP 14 (Unity 2022.3) or newer is installed.
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace BoundedBuoyancy
{
    /// <summary>
    /// Floats Bounded Buoyancy bodies on the waves of an HDRP Water Surface: HDRP draws the ocean, river or pool, and
    /// Bounded Buoyancy does the physics (the part of each body under the waves, added mass, righting, drag).
    /// <para>Add this to an unscaled object at the height of the water (a Bounded Buoyancy Water is added with it), and
    /// pick the Water Surface. HDRP sizes some water surfaces by their object's scale, so keep this off a scaled one.
    /// Turn on Script Interactions on the Water Surface (in Unity 2022.3, in the Water section of the HDRP asset too), so
    /// HDRP can answer height requests. The Bounded Buoyancy Water still sets where bodies float (its size), the water's
    /// density and any current.</para>
    /// </summary>
    [AddComponentMenu("Physics/Bounded Buoyancy HDRP Water")]
    [RequireComponent(typeof(BoundedBuoyancyWater))]
    public class BoundedBuoyancyHDRPWater : BoundedBuoyancySurface
    {
        [Tooltip("The HDRP Water Surface whose waves the bodies float on. Turn on its Script Interactions.")]
        public WaterSurface waterSurface;

        [Tooltip("How close, in metres across, each height search must get. Smaller is more exact and costs more.")]
        [Min(0.0001f)] public float searchError = 0.01f;

        [Tooltip("Most steps each height search may take.")]
        [Range(1, 64)] public int searchSteps = 8;

#if BB_HDRP_15_OR_NEWER
        [Tooltip("Include the water's deformation (its deformers, or its water decals in Unity 6) in the heights.")]
        public bool includeDeformation = true;
#endif

        float missingSince = -1f;
        bool warned;

        protected override void Reset()
        {
            maxWave = 3f;
            waterSurface = GetComponent<WaterSurface>();
            if (waterSurface == null)
                waterSurface = GetComponentInParent<WaterSurface>();
            if (waterSurface == null)
                waterSurface = FindAnyObjectByType<WaterSurface>();
            var water = GetComponent<BoundedBuoyancyWater>();
            if (water != null)
            {
                RecordChange(water);
                water.surfaceSource = this;
                if (waterSurface != null && water.size == new Vector3(10f, 3f, 10f))
                {
                    if (waterSurface.geometryType == WaterGeometryType.Infinite)
                        water.size = new Vector3(1000f, 100f, 1000f);   // an ocean: bodies float within 500 m of here
                    else if (waterSurface.geometryType == WaterGeometryType.Quad)
                    {
                        Vector3 s = waterSurface.transform.lossyScale;   // HDRP sizes a quad surface by its scale
                        water.size = new Vector3(Mathf.Abs(s.x), water.size.y, Mathf.Abs(s.z));
                    }
                }
                ChangeRecorded(water);
            }
            if ((transform.lossyScale - Vector3.one).sqrMagnitude > 1e-6f)
                Debug.LogWarning("Bounded Buoyancy HDRP Water '" + name + "': this object is scaled. Put Bounded Buoyancy HDRP Water on a separate, unscaled object at the water's height, and set the Bounded Buoyancy Water's Size.", this);
        }

        /// <summary>Height (world Y) of the HDRP water surface above or below the world point `worldPoint`. Until HDRP
        /// can answer (for a frame or two after the start, or with Script Interactions off), the height of the Water
        /// Surface object.</summary>
        public override float HeightAt(Vector3 worldPoint)
        {
            if (waterSurface == null || !waterSurface.isActiveAndEnabled)
                return transform.position.y;
            var at = new float3(worldPoint.x, worldPoint.y, worldPoint.z);
#if BB_HDRP_15_OR_NEWER
            var search = new WaterSearchParameters
            {
                targetPositionWS = at,
                startPositionWS = at,
                error = searchError,
                maxIterations = searchSteps,
                includeDeformation = includeDeformation,
            };
            WaterSearchResult result;
            if (waterSurface.ProjectPointOnWaterSurface(search, out result))
            {
                missingSince = -1f;
                // A custom-mesh surface answers in heights above its own level (as Unity's water samples allow for);
                // HDRP draws one with no mesh renderers as a quad, which answers in world heights.
                if (waterSurface.geometryType == WaterGeometryType.Custom && waterSurface.meshRenderers != null && waterSurface.meshRenderers.Count != 0)
                    return result.projectedPositionWS.y + waterSurface.transform.position.y;
                return result.projectedPositionWS.y;
            }
#else
            var search = new WaterSearchParameters
            {
                targetPosition = at,
                startPosition = at,
                error = searchError,
                maxIterations = searchSteps,
            };
            WaterSearchResult result;
            if (waterSurface.FindWaterSurfaceHeight(search, out result))
            {
                missingSince = -1f;
                return result.height;
            }
#endif
            Missed();
            return waterSurface.transform.position.y;
        }

        void Missed()
        {
            // HDRP has no heights for the first frame or two; for longer, Script Interactions is off.
            if (warned)
                return;
            if (missingSince < 0f)
                missingSince = Time.time;
            if (Time.time - missingSince < 2f)
                return;
            warned = true;
            Debug.LogWarning("Bounded Buoyancy HDRP Water '" + name + "': the Water Surface '" + waterSurface.name
                + "' gives no heights, so bodies float on a flat surface at its height. Turn on Script Interactions on the Water Surface (in Unity 2022.3, in the Water section of the HDRP asset too).", this);
        }
    }
}
