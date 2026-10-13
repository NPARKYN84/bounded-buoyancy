// Minimal stand-ins for the parts of the High Definition Render Pipeline's Water System and of Unity.Mathematics that
// Bounded Buoyancy HDRP Water uses, transcribed from Unity's published source (github.com/Unity-Technologies/Graphics,
// branches 2022.3/staging for HDRP 14 and 6000.0/staging for HDRP 17; Unity.Mathematics 1.2). Only the members the
// package calls are here, with the same names, types and namespaces. BB_STUB_HDRP17 switches from the HDRP 14 API
// (FindWaterSurfaceHeight) to the API of HDRP 15 and newer (ProjectPointOnWaterSurface). No behaviour is modelled.
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

namespace UnityEngine.Rendering.HighDefinition
{
    using Unity.Mathematics;

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

    public partial class WaterSurface : MonoBehaviour
    {
        public WaterGeometryType geometryType = WaterGeometryType.Infinite;
        public System.Collections.Generic.List<MeshRenderer> meshRenderers = new System.Collections.Generic.List<MeshRenderer>();
        public bool scriptInteractions = false;
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

    public class WaterSurface : MonoBehaviour
    {
        public WaterGeometryType geometryType = WaterGeometryType.Infinite;
        public bool cpuSimulation = false;
        public bool FindWaterSurfaceHeight(WaterSearchParameters wsp, out WaterSearchResult wsr) { wsr = default(WaterSearchResult); return false; }
    }
#endif
}
