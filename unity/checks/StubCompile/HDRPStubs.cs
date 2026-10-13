// Minimal stand-ins for the parts of the High Definition Render Pipeline's Water System, of Unity.Mathematics and of
// the collections and job system that Bounded Buoyancy HDRP Water uses, transcribed from Unity's published source
// (github.com/Unity-Technologies/Graphics, branches 2022.3/staging for HDRP 14 and 6000.0/staging for HDRP 17, with
// the search job checked against 2023.1 and 2023.2 for HDRP 15 and 16; UnityCsReference for NativeArray and the jobs;
// Unity.Mathematics 1.2). Only the members the package calls are here, with the same names, types and namespaces.
// BB_STUB_HDRP17 switches from the HDRP 14 API (FindWaterSurfaceHeight) to the API of HDRP 15 and newer
// (ProjectPointOnWaterSurface, with the normal outputs of 17). No behaviour is modelled.
#pragma warning disable CS0626, CS0824, CS0067, CS0649, CS0414, CS0169

namespace Unity.Mathematics
{
    public partial struct float3
    {
        public float x;
        public float y;
        public float z;
        public float3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
}

namespace Unity.Collections
{
    public enum Allocator { Invalid = 0, None = 1, Temp = 2, TempJob = 3, Persistent = 4 }
    public enum NativeArrayOptions { UninitializedMemory = 0, ClearMemory = 1 }

    public struct NativeArray<T> : System.IDisposable where T : struct
    {
        public NativeArray(int length, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory) { }
        public T this[int index] { get { return default(T); } set { } }
        public int Length { get { return 0; } }
        public bool IsCreated { get { return false; } }
        public void Dispose() { }
    }

    public sealed class ReadOnlyAttribute : System.Attribute { }
    public sealed class WriteOnlyAttribute : System.Attribute { }
    public sealed class NativeDisableParallelForRestrictionAttribute : System.Attribute { }
}

namespace Unity.Jobs
{
    public interface IJobParallelFor { void Execute(int index); }

    public struct JobHandle
    {
        public void Complete() { }
    }

    public static class IJobParallelForExtensions
    {
        public static JobHandle Schedule<T>(this T jobData, int arrayLength, int innerloopBatchCount, JobHandle dependsOn = default(JobHandle)) where T : struct, IJobParallelFor { return default(JobHandle); }
    }
}

namespace Unity.Burst
{
    public class BurstCompileAttribute : System.Attribute { }
}

namespace UnityEngine.Rendering.HighDefinition
{
    using Unity.Mathematics;
    using Unity.Collections;

    public enum WaterSurfaceType { OceanSeaLake, River, Pool }

    public struct WaterSimSearchData { }

    public enum WaterGeometryType
    {
        Quad,
#if BB_STUB_HDRP17
        Custom,
        InstancedQuads,
#else
        CustomMesh,
#endif
        Infinite
    }

#if BB_STUB_HDRP17
    public struct WaterSearchParameters
    {
        public float3 targetPositionWS;
        public float3 startPositionWS;
        public float error;
        public int maxIterations;
        public bool includeDeformation;
        public bool excludeSimulation;
        public bool outputNormal;
    }

    public struct WaterSearchResult
    {
        public float3 projectedPositionWS;
        public float3 normalWS;
        public float3 candidateLocationWS;
        public float3 currentDirectionWS;
        public int numIterations;
        public float error;
    }

    [Unity.Burst.BurstCompile]
    public struct WaterSimulationSearchJob : Unity.Jobs.IJobParallelFor
    {
        public WaterSimSearchData simSearchData;
        [ReadOnly] [NativeDisableParallelForRestriction] public NativeArray<float3> targetPositionWSBuffer;
        [ReadOnly] [NativeDisableParallelForRestriction] public NativeArray<float3> startPositionWSBuffer;
        public float error;
        public int maxIterations;
        public bool includeDeformation;
        public bool excludeSimulation;
        public bool outputNormal;
        [WriteOnly] [NativeDisableParallelForRestriction] public NativeArray<float3> projectedPositionWSBuffer;
        [WriteOnly] [NativeDisableParallelForRestriction] public NativeArray<float3> normalWSBuffer;
        [WriteOnly] [NativeDisableParallelForRestriction] public NativeArray<float> errorBuffer;
        [WriteOnly] [NativeDisableParallelForRestriction] public NativeArray<float3> candidateLocationWSBuffer;
        [WriteOnly] [NativeDisableParallelForRestriction] public NativeArray<float3> directionBuffer;
        [WriteOnly] [NativeDisableParallelForRestriction] public NativeArray<int> stepCountBuffer;
        public void Execute(int index) { }
    }

    public partial class WaterSurface : MonoBehaviour
    {
        public WaterSurfaceType surfaceType = WaterSurfaceType.OceanSeaLake;
        public WaterGeometryType geometryType = WaterGeometryType.Infinite;
        public System.Collections.Generic.List<MeshRenderer> meshRenderers = new System.Collections.Generic.List<MeshRenderer>();
        public bool scriptInteractions = false;
        public float largeOrientationValue = 0.0f;
        public float ripplesOrientationValue = 0.0f;
        public float largeCurrentSpeedValue = 0.0f;
        public float ripplesCurrentSpeedValue = 0.0f;
        public bool FillWaterSearchData(ref WaterSimSearchData wsd) { return false; }
        public bool ProjectPointOnWaterSurface(WaterSearchParameters wsp, out WaterSearchResult wsr) { wsr = default(WaterSearchResult); return false; }
    }
#else
    public struct WaterSearchParameters
    {
        public float3 targetPosition;
        public float3 startPosition;
        public float error;
        public int maxIterations;
    }

    public struct WaterSearchResult
    {
        public float height;
        public float error;
        public float3 candidateLocation;
        public int numIterations;
    }

    [Unity.Burst.BurstCompile]
    public struct WaterSimulationSearchJob : Unity.Jobs.IJobParallelFor
    {
        public WaterSimSearchData simSearchData;
        [ReadOnly] [NativeDisableParallelForRestriction] public NativeArray<float3> targetPositionBuffer;
        [ReadOnly] [NativeDisableParallelForRestriction] public NativeArray<float3> startPositionBuffer;
        public float error;
        public int maxIterations;
        [WriteOnly] [NativeDisableParallelForRestriction] public NativeArray<float> heightBuffer;
        [WriteOnly] [NativeDisableParallelForRestriction] public NativeArray<float> errorBuffer;
        [WriteOnly] [NativeDisableParallelForRestriction] public NativeArray<float3> candidateLocationBuffer;
        [WriteOnly] [NativeDisableParallelForRestriction] public NativeArray<int> stepCountBuffer;
        public void Execute(int index) { }
    }

    public class WaterSurface : MonoBehaviour
    {
        public WaterSurfaceType surfaceType = WaterSurfaceType.OceanSeaLake;
        public WaterGeometryType geometryType = WaterGeometryType.Infinite;
        public bool cpuSimulation = false;
        public float largeCurrentSpeedValue = 0.0f;
        public float largeCurrentOrientationValue = 0.0f;
        public float ripplesCurrentSpeedValue = 0.0f;
        public float ripplesCurrentOrientationValue = 0.0f;
        public bool FillWaterSearchData(ref WaterSimSearchData wsd) { return false; }
        public bool FindWaterSurfaceHeight(WaterSearchParameters wsp, out WaterSearchResult wsr) { wsr = default(WaterSearchResult); return false; }
    }
#endif
}
