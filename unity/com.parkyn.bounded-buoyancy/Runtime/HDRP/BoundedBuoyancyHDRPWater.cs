// Bounded Buoyancy for Unity: link to the Water System of the High Definition Render Pipeline.
// Copyright (c) 2026 Nicholas Parkyn. MIT licence.
// This assembly is compiled only when HDRP 14 (Unity 2022.3) or newer is installed.
using Unity.Collections;
using Unity.Jobs;
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
    /// HDRP can answer height requests. The Bounded Buoyancy Water still sets where bodies float (its size) and the
    /// water's density; the current comes from the Water Surface (Use HDRP Current) or from the Bounded Buoyancy
    /// Water.</para>
    /// <para>Each body asks for the heights over all its vertices at once, and they are found in one Burst job on the
    /// worker threads, the way HDRP's own water samples do, so a hull of hundreds of vertices costs little more than a
    /// sphere; the height over a body's centre is a single search.</para>
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

        [Tooltip("Carry bodies along with the Water Surface's own current (its Current speed, in km/h, along its orientation: the large current of an ocean or river, the ripples current of a pool). Off uses the Bounded Buoyancy Water's Current.")]
        public bool useHdrpCurrent = true;

        float missingSince = -1f;
        bool warned;
        NativeArray<float3> targets, starts, candidates;
        NativeArray<float> errors;
        NativeArray<int> steps;
#if BB_HDRP_15_OR_NEWER
        NativeArray<float3> projected, directions;
#endif
#if BB_HDRP_17_OR_NEWER
        NativeArray<float3> normals;
#endif
#if !BB_HDRP_15_OR_NEWER
        NativeArray<float> heights;
#endif
        int capacity;

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

        void OnDisable()
        {
            Release();
        }

        void OnDestroy()
        {
            Release();
        }

        void Release()
        {
            if (capacity == 0)
                return;
            targets.Dispose();
            starts.Dispose();
            candidates.Dispose();
            errors.Dispose();
            steps.Dispose();
#if BB_HDRP_15_OR_NEWER
            projected.Dispose();
            directions.Dispose();
#endif
#if BB_HDRP_17_OR_NEWER
            normals.Dispose();
#endif
#if !BB_HDRP_15_OR_NEWER
            heights.Dispose();
#endif
            capacity = 0;
        }

        void Reserve(int n)
        {
            if (n <= capacity)
                return;
            Release();
            capacity = Mathf.Max(n, 64);
            targets = new NativeArray<float3>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            starts = new NativeArray<float3>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            candidates = new NativeArray<float3>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            errors = new NativeArray<float>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            steps = new NativeArray<int>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
#if BB_HDRP_15_OR_NEWER
            projected = new NativeArray<float3>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            directions = new NativeArray<float3>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
#endif
#if BB_HDRP_17_OR_NEWER
            normals = new NativeArray<float3>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
#endif
#if !BB_HDRP_15_OR_NEWER
            heights = new NativeArray<float>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
#endif
        }

        /// <summary>Heights (world Y) of the HDRP water surface above or below many world points at once: one Burst
        /// job over all of them, with HDRP's own search. Each body asks for all its vertices this way. Until HDRP can
        /// answer, the height of the Water Surface object for every point.</summary>
        public override void HeightsAt(Vector3[] points, float[] heights, int count)
        {
            if (count <= 0)
                return;
            float level = transform.position.y;
            if (waterSurface != null && waterSurface.isActiveAndEnabled)
            {
                level = waterSurface.transform.position.y;
                var data = new WaterSimSearchData();
                if (waterSurface.FillWaterSearchData(ref data))
                {
                    missingSince = -1f;
                    Reserve(count);
                    for (int i = 0; i < count; i++)
                    {
                        Vector3 p = points[i];
                        var at = new float3(p.x, p.y, p.z);
                        targets[i] = at;
                        starts[i] = at;
                    }
                    var job = new WaterSimulationSearchJob
                    {
                        simSearchData = data,
                        error = searchError,
                        maxIterations = searchSteps,
#if BB_HDRP_15_OR_NEWER
                        includeDeformation = includeDeformation,
                        excludeSimulation = false,
                        targetPositionWSBuffer = targets,
                        startPositionWSBuffer = starts,
                        projectedPositionWSBuffer = projected,
                        errorBuffer = errors,
                        candidateLocationWSBuffer = candidates,
                        directionBuffer = directions,
                        stepCountBuffer = steps,
#else
                        targetPositionBuffer = targets,
                        startPositionBuffer = starts,
                        heightBuffer = this.heights,
                        errorBuffer = errors,
                        candidateLocationBuffer = candidates,
                        stepCountBuffer = steps,
#endif
                    };
#if BB_HDRP_17_OR_NEWER
                    job.outputNormal = false;
                    job.normalWSBuffer = normals;
#endif
                    job.Schedule(count, 32).Complete();
#if BB_HDRP_15_OR_NEWER
                    // A custom-mesh surface answers in heights above its own level (see HeightAt).
                    float lift = waterSurface.geometryType == WaterGeometryType.Custom && waterSurface.meshRenderers != null && waterSurface.meshRenderers.Count != 0 ? level : 0f;
                    for (int i = 0; i < count; i++)
                        heights[i] = projected[i].y + lift;
#else
                    for (int i = 0; i < count; i++)
                        heights[i] = this.heights[i];
#endif
                    return;
                }
                Missed();
            }
            for (int i = 0; i < count; i++)
                heights[i] = level;
        }

        /// <summary>Speed of the Water Surface's current in m/s (HDRP sets it in km/h): the large current of an
        /// ocean or river, the ripples current of a pool.</summary>
        public float CurrentSpeed
        {
            get
            {
                if (waterSurface == null)
                    return 0f;
                return (waterSurface.surfaceType == WaterSurfaceType.Pool ? waterSurface.ripplesCurrentSpeedValue : waterSurface.largeCurrentSpeedValue) / 3.6f;
            }
        }

        /// <summary>The Water Surface's current (world frame) at `worldPoint`: its speed along its orientation, turned
        /// with the surface's object. With Use HDRP Current off, false: the Bounded Buoyancy Water's Current is used.</summary>
        public override bool FlowAt(Vector3 worldPoint, out Vector3 flow)
        {
            flow = Vector3.zero;
            if (!useHdrpCurrent || waterSurface == null)
                return false;
            float speed = CurrentSpeed;
            if (speed == 0f)
                return true;
#if BB_HDRP_15_OR_NEWER
            float degrees = waterSurface.surfaceType == WaterSurfaceType.Pool ? waterSurface.ripplesOrientationValue : waterSurface.largeOrientationValue;
#else
            float degrees = waterSurface.surfaceType == WaterSurfaceType.Pool ? waterSurface.ripplesCurrentOrientationValue : waterSurface.largeCurrentOrientationValue;
#endif
            float a = degrees * Mathf.Deg2Rad;
            flow = waterSurface.transform.rotation * new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * speed;
            return true;
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
