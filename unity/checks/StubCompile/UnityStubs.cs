// Minimal stand-ins for the UnityEngine API that Bounded Buoyancy uses, transcribed from Unity's C# reference
// source (github.com/Unity-Technologies/UnityCsReference, tags 2022.3 and 6000.0). Only the members the package
// calls are here, with the same names, types, static-ness and namespaces, so that compiling the package against
// them catches a misspelt or missing member. UNITY_6000_0_OR_NEWER switches to the Unity 6 API, where the old
// Rigidbody members are [Obsolete] (warnings as errors catches any use of them). No behaviour is modelled.
#pragma warning disable CS0626, CS0824, CS0067, CS0649, CS0414, CS0169
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public partial class Object
    {
        public string name { get; set; }
        public HideFlags hideFlags { get; set; }
        public static void Destroy(Object obj) { }
        public static void DontDestroyOnLoad(Object target) { }
        public static bool operator ==(Object x, Object y) { return ReferenceEquals(x, y); }
        public static bool operator !=(Object x, Object y) { return !ReferenceEquals(x, y); }
        public static implicit operator bool(Object exists) { return !ReferenceEquals(exists, null); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return 0; }
    }

    [Flags]
    public enum HideFlags { None = 0, HideInHierarchy = 1, HideInInspector = 2, DontSaveInEditor = 4, NotEditable = 8, DontSaveInBuild = 16, DontUnloadUnusedAsset = 32, DontSave = 52, HideAndDontSave = 61 }

    public class Component : Object
    {
        public Transform transform { get { return null; } }
        public GameObject gameObject { get { return null; } }
        public T GetComponent<T>() { return default(T); }
        public T[] GetComponentsInChildren<T>() { return null; }
        public T GetComponentInParent<T>() { return default(T); }
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; }
        public bool isActiveAndEnabled { get { return false; } }
    }

    public class MonoBehaviour : Behaviour { }

    public sealed class GameObject : Object
    {
        public GameObject(string name) { }
        public Transform transform { get { return null; } }
        public int layer { get; set; }
        public SceneManagement.Scene scene { get { return default(SceneManagement.Scene); } }
        public string tag { get; set; }
        public static GameObject CreatePrimitive(PrimitiveType type) { return null; }
        public T AddComponent<T>() where T : Component { return null; }
        public T GetComponent<T>() { return default(T); }
    }

    public class Transform : Component
    {
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Vector3 eulerAngles { get; set; }
        public Quaternion rotation { get; set; }
        public Quaternion localRotation { get; set; }
        public Vector3 localScale { get; set; }
        public Vector3 lossyScale { get { return default(Vector3); } }
        public Transform parent { get { return null; } set { } }
        public Vector3 forward { get { return default(Vector3); } set { } }
        public Vector3 up { get { return default(Vector3); } set { } }
        public int childCount { get { return 0; } }
        public Transform Find(string n) { return null; }
        public Vector3 TransformPoint(Vector3 position) { return position; }
        public Vector3 TransformDirection(Vector3 direction) { return direction; }
        public Vector3 TransformVector(Vector3 vector) { return vector; }
        public Vector3 InverseTransformDirection(Vector3 direction) { return direction; }
        public void SetParent(Transform parent, bool worldPositionStays) { }
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero { get { return default(Vector2); } }
        public static Vector2 right { get { return new Vector2(1, 0); } }
        public float magnitude { get { return 0f; } }
        public float sqrMagnitude { get { return 0f; } }
        public Vector2 normalized { get { return this; } }
        public static float Dot(Vector2 lhs, Vector2 rhs) { return 0f; }
        public static Vector2 Max(Vector2 lhs, Vector2 rhs) { return lhs; }
        public static Vector2 operator +(Vector2 a, Vector2 b) { return a; }
        public static Vector2 operator -(Vector2 a, Vector2 b) { return a; }
        public static Vector2 operator *(Vector2 a, float d) { return a; }
        public static Vector2 operator *(float d, Vector2 a) { return a; }
        public static Vector2 operator /(Vector2 a, float d) { return a; }
        public static bool operator ==(Vector2 lhs, Vector2 rhs) { return true; }
        public static bool operator !=(Vector2 lhs, Vector2 rhs) { return false; }
        public static implicit operator Vector2(Vector3 v) { return default(Vector2); }
        public static implicit operator Vector3(Vector2 v) { return default(Vector3); }
        public override bool Equals(object other) { return false; }
        public override int GetHashCode() { return 0; }
    }

    public partial struct Vector4
    {
        public float x;
        public float y;
        public float z;
        public float w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float this[int index] { get { return 0f; } set { } }
        public static Vector3 zero { get { return default(Vector3); } }
        public static Vector3 one { get { return default(Vector3); } }
        public static Vector3 up { get { return default(Vector3); } }
        public static Vector3 right { get { return default(Vector3); } }
        public static Vector3 forward { get { return default(Vector3); } }
        public static Vector3 back { get { return default(Vector3); } }
        public static Vector3 down { get { return default(Vector3); } }
        public float magnitude { get { return 0f; } }
        public float sqrMagnitude { get { return 0f; } }
        public Vector3 normalized { get { return this; } }
        public static Vector3 Cross(Vector3 lhs, Vector3 rhs) { return lhs; }
        public static float Angle(Vector3 from, Vector3 to) { return 0f; }
        public static float SignedAngle(Vector3 from, Vector3 to, Vector3 axis) { return 0f; }
        public static float Dot(Vector3 lhs, Vector3 rhs) { return 0f; }
        public static Vector3 Max(Vector3 lhs, Vector3 rhs) { return lhs; }
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { return a; }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return a; }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return a; }
        public static Vector3 operator -(Vector3 a) { return a; }
        public static Vector3 operator *(Vector3 a, float d) { return a; }
        public static Vector3 operator *(float d, Vector3 a) { return a; }
        public static Vector3 operator /(Vector3 a, float d) { return a; }
        public static bool operator ==(Vector3 lhs, Vector3 rhs) { return true; }
        public static bool operator !=(Vector3 lhs, Vector3 rhs) { return false; }
        public override bool Equals(object other) { return false; }
        public override int GetHashCode() { return 0; }
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public static Quaternion identity { get { return default(Quaternion); } }
        public static Quaternion Euler(float x, float y, float z) { return default(Quaternion); }
        public static Quaternion Inverse(Quaternion rotation) { return rotation; }
        public static Quaternion LookRotation(Vector3 forward, Vector3 upwards) { return default(Quaternion); }
        public static Vector3 operator *(Quaternion rotation, Vector3 point) { return point; }
        public static Quaternion operator *(Quaternion lhs, Quaternion rhs) { return lhs; }
        public static Quaternion AngleAxis(float angle, Vector3 axis) { return default(Quaternion); }
    }

    // UnityEngine.Pose (Runtime/Export/XR/Pose.cs) exists in both versions; declared here so that a clash with a
    // type of the same name in the package is caught.
    [Serializable]
    public struct Pose
    {
        public Vector3 position;
        public Quaternion rotation;
        public Pose(Vector3 position, Quaternion rotation) { this.position = position; this.rotation = rotation; }
    }

    public struct Matrix4x4
    {
        public static Matrix4x4 identity { get { return default(Matrix4x4); } }
        public static Matrix4x4 TRS(Vector3 pos, Quaternion q, Vector3 s) { return default(Matrix4x4); }
    }

    public struct Bounds
    {
        public Bounds(Vector3 center, Vector3 size) { }
        public Vector3 center { get { return default(Vector3); } set { } }
        public Vector3 extents { get { return default(Vector3); } set { } }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; this.a = 1f; }
        public static Color operator *(Color a, float b) { return a; }
        public static implicit operator Color(Color32 c) { return default(Color); }
        public static bool operator ==(Color lhs, Color rhs) { return true; }
        public static bool operator !=(Color lhs, Color rhs) { return false; }
        public override bool Equals(object other) { return false; }
        public override int GetHashCode() { return 0; }
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color32(Color c) { return default(Color32); }
    }

    public struct Mathf
    {
        public const float PI = 3.14159274F;
        public const float Deg2Rad = PI * 2F / 360F;
        public static float MoveTowards(float current, float target, float maxDelta) { return current; }
        public static float LerpAngle(float a, float b, float t) { return a; }
        public static float Sin(float f) { return 0f; }
        public static float Cos(float f) { return 0f; }
        public static float Sqrt(float f) { return 0f; }
        public static float Asin(float f) { return 0f; }
        public static float Abs(float f) { return 0f; }
        public static float Pow(float f, float p) { return 0f; }
        public static float Exp(float power) { return 0f; }
        public static float Log(float f) { return 0f; }
        public static float Min(float a, float b) { return 0f; }
        public static float Max(float a, float b) { return 0f; }
        public static int Max(int a, int b) { return 0; }
        public static float Clamp(float value, float min, float max) { return 0f; }
        public static int Clamp(int value, int min, int max) { return 0; }
        public static float Clamp01(float value) { return 0f; }
        public static float DeltaAngle(float current, float target) { return 0f; }
        public const float Rad2Deg = 1F / Deg2Rad;
        public static float Atan2(float y, float x) { return 0f; }
        public static float Lerp(float a, float b, float t) { return 0f; }
        public static int RoundToInt(float f) { return 0; }
    }

    public class YieldInstruction { }
    public sealed class WaitForFixedUpdate : YieldInstruction { }

    public class Time
    {
        public static float time { get { return 0f; } }
        public static float deltaTime { get { return 0f; } }
        public static float fixedDeltaTime { get { return 0f; } set { } }
        public static float timeScale { get { return 1f; } set { } }
        public static float smoothDeltaTime { get { return 0f; } }
        public static float maximumDeltaTime { get { return 0f; } set { } }
    }

    public class Application
    {
        public static bool isPlaying { get { return false; } }
        public static event Action quitting;
    }

    public class Debug
    {
        public static void LogWarning(object message) { }
        public static void LogWarning(object message, Object context) { }
    }

    public sealed class Gizmos
    {
        public static Color color { get; set; }
        public static Matrix4x4 matrix { get; set; }
        public static void DrawLine(Vector3 from, Vector3 to) { }
        public static void DrawWireCube(Vector3 center, Vector3 size) { }
        public static void DrawCube(Vector3 center, Vector3 size) { }
        public static void DrawSphere(Vector3 center, float radius) { }
        public static void DrawWireSphere(Vector3 center, float radius) { }
    }

    // Attributes -------------------------------------------------------------------------------------------------
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class AddComponentMenu : Attribute { public AddComponentMenu(string menuName) { } }
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class RequireComponent : Attribute { public RequireComponent(Type requiredComponent) { } }
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class DisallowMultipleComponent : Attribute { }
    [AttributeUsage(AttributeTargets.Class)]
    public class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int order) { } }
    public class PropertyAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class TooltipAttribute : PropertyAttribute { public TooltipAttribute(string tooltip) { } }
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = true)]
    public class HeaderAttribute : PropertyAttribute { public HeaderAttribute(string header) { } }
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public sealed class RangeAttribute : PropertyAttribute { public RangeAttribute(float min, float max) { } }
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public sealed class MinAttribute : PropertyAttribute { public MinAttribute(float min) { } }
    public sealed class SerializeField : Attribute { }
    public class HideInInspector : Attribute { }
    public enum RuntimeInitializeLoadType { AfterSceneLoad = 0, BeforeSceneLoad = 1, AfterAssembliesLoaded = 2, BeforeSplashScreen = 3, SubsystemRegistration = 4 }
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { } }

    // 3D physics -------------------------------------------------------------------------------------------------
    public enum ForceMode { Force = 0, Acceleration = 5, Impulse = 1, VelocityChange = 2 }

    public partial class Rigidbody : Component
    {
        public Vector3 angularVelocity { get; set; }
#if UNITY_6000_0_OR_NEWER
        public Vector3 linearVelocity { get; set; }
        public float linearDamping { get; set; }
        public float angularDamping { get; set; }
        [Obsolete("Please use Rigidbody.linearDamping instead. (UnityUpgradable) -> linearDamping")]
        public float drag { get; set; }
        [Obsolete("Please use Rigidbody.angularDamping instead. (UnityUpgradable) -> angularDamping")]
        public float angularDrag { get; set; }
        [Obsolete("Please use Rigidbody.linearVelocity instead. (UnityUpgradable) -> linearVelocity")]
        public Vector3 velocity { get; set; }
#else
        public Vector3 velocity { get; set; }
        public float drag { get; set; }
        public float angularDrag { get; set; }
#endif
        public float mass { get; set; }
        public bool useGravity { get; set; }
        public bool isKinematic { get; set; }
        public bool automaticCenterOfMass { get; set; }
        public Vector3 centerOfMass { get; set; }
        public Vector3 worldCenterOfMass { get { return default(Vector3); } }
        public bool automaticInertiaTensor { get; set; }
        public Quaternion inertiaTensorRotation { get; set; }
        public void ResetCenterOfMass() { }
        public void ResetInertiaTensor() { }
        public Vector3 inertiaTensor { get; set; }
        public Vector3 position { get; set; }
        public Quaternion rotation { get; set; }
        public RigidbodyInterpolation interpolation { get; set; }
        public void AddForce(Vector3 force, ForceMode mode) { }
        public void AddTorque(Vector3 torque, ForceMode mode) { }
    }

    public partial class Collider : Component
    {
        public bool enabled { get; set; }
        public Rigidbody attachedRigidbody { get { return null; } }
        public bool isTrigger { get; set; }
    }
    public partial class BoxCollider : Collider { public Vector3 center { get; set; } public Vector3 size { get; set; } }
    public class SphereCollider : Collider { public Vector3 center { get; set; } public float radius { get; set; } }
    public class CapsuleCollider : Collider { public Vector3 center { get; set; } public float radius { get; set; } public float height { get; set; } public int direction { get; set; } }
    public partial class MeshCollider : Collider { public Mesh sharedMesh { get; set; } public bool convex { get; set; } }

    public enum SimulationMode { FixedUpdate = 0, Update = 1, Script = 2 }

    public partial class Physics
    {
        public static Vector3 gravity { get; set; }
        public static SimulationMode simulationMode { get; set; }
    }

    public partial struct PhysicsScene : IEquatable<PhysicsScene>
    {
        public static bool operator ==(PhysicsScene lhs, PhysicsScene rhs) { return true; }
        public static bool operator !=(PhysicsScene lhs, PhysicsScene rhs) { return false; }
        public override bool Equals(object other) { return false; }
        public bool Equals(PhysicsScene other) { return false; }
        public override int GetHashCode() { return 0; }
    }

    public static class PhysicsSceneExtensions
    {
        public static PhysicsScene GetPhysicsScene(this SceneManagement.Scene scene) { return default(PhysicsScene); }
    }

    // 2D physics -------------------------------------------------------------------------------------------------
    public enum ForceMode2D { Force = 0, Impulse = 1 }
    public enum RigidbodyType2D { Dynamic = 0, Kinematic = 1, Static = 2 }
    public enum CapsuleDirection2D { Vertical = 0, Horizontal = 1 }

    public sealed partial class Rigidbody2D : Component
    {
        public Vector2 position { get; set; }
        public float rotation { get; set; }
        public float angularVelocity { get; set; }
#if UNITY_6000_0_OR_NEWER
        public Vector2 linearVelocity { get; set; }
        public float linearDamping { get; set; }
        public float angularDamping { get; set; }
        [Obsolete("Please use Rigidbody2D.linearDamping instead. (UnityUpgradable) -> linearDamping", false)]
        public float drag { get; set; }
        [Obsolete("Please use Rigidbody2D.angularDamping instead. (UnityUpgradable) -> angularDamping", false)]
        public float angularDrag { get; set; }
        [Obsolete("Please use Rigidbody2D.linearVelocity instead. (UnityUpgradable) -> linearVelocity", false)]
        public Vector2 velocity { get; set; }
#else
        public Vector2 velocity { get; set; }
        public float drag { get; set; }
        public float angularDrag { get; set; }
#endif
        public bool useAutoMass { get; set; }
        public float mass { get; set; }
        public Vector2 centerOfMass { get; set; }
        public Vector2 worldCenterOfMass { get { return default(Vector2); } }
        public float inertia { get; set; }
        public float gravityScale { get; set; }
        public RigidbodyType2D bodyType { get; set; }
        public bool simulated { get; set; }
        public RigidbodyInterpolation2D interpolation { get; set; }
        public void AddForce(Vector2 force, ForceMode2D mode) { }
        public void AddTorque(float torque, ForceMode2D mode) { }
    }

    public partial class Collider2D : Behaviour
    {
        public bool isTrigger { get; set; }
        public Vector2 offset { get; set; }
        public Rigidbody2D attachedRigidbody { get { return null; } }
    }
    public sealed partial class CircleCollider2D : Collider2D { public float radius { get; set; } }
    public sealed partial class CapsuleCollider2D : Collider2D { public Vector2 size { get; set; } public CapsuleDirection2D direction { get; set; } }
    public sealed partial class BoxCollider2D : Collider2D { public Vector2 size { get; set; } public float edgeRadius { get; set; } }
    public sealed partial class PolygonCollider2D : Collider2D { public int pathCount { get; set; } public Vector2[] GetPath(int index) { return null; } public void SetPath(int index, Vector2[] points) { } }
    public sealed partial class CompositeCollider2D : Collider2D { }

    public enum SimulationMode2D { FixedUpdate = 0, Update = 1, Script = 2 }

    public partial class Physics2D
    {
        public static Vector2 gravity { get; set; }
        public static SimulationMode2D simulationMode { get; set; }
    }

    public partial struct PhysicsScene2D : IEquatable<PhysicsScene2D>
    {
        public static bool operator ==(PhysicsScene2D lhs, PhysicsScene2D rhs) { return true; }
        public static bool operator !=(PhysicsScene2D lhs, PhysicsScene2D rhs) { return false; }
        public override bool Equals(object other) { return false; }
        public bool Equals(PhysicsScene2D other) { return false; }
        public override int GetHashCode() { return 0; }
    }

    public static class PhysicsSceneExtensions2D
    {
        public static PhysicsScene2D GetPhysicsScene2D(this SceneManagement.Scene scene) { return default(PhysicsScene2D); }
    }

    // Graphics ---------------------------------------------------------------------------------------------------
    public sealed class Mesh : Object
    {
        public Mesh() { }
        public bool isReadable { get { return false; } }
        public Vector3[] vertices { get; set; }
        public Vector3[] normals { get; set; }
        public Color32[] colors32 { get; set; }
        public int[] triangles { get; set; }
        public Bounds bounds { get; set; }
        public void Clear() { }
        public void MarkDynamic() { }
        public void RecalculateNormals() { }
        public void RecalculateBounds() { }
    }

    public class Shader : Object
    {
        public static Shader Find(string name) { return null; }
    }

    public class Texture : Object
    {
        public TextureWrapMode wrapMode { get; set; }
    }

    public enum TextureWrapMode { Repeat = 0, Clamp = 1, Mirror = 2, MirrorOnce = 3 }
    public enum TextureFormat { RGBA32 = 4 }

    public sealed class Texture2D : Texture
    {
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain) { }
        public void SetPixels32(Color32[] colors) { }
        public void Apply() { }
    }

    public class Material : Object
    {
        public Material(Shader shader) { }
        public Material(Material source) { }
        public Color color { get; set; }
        public Texture mainTexture { get; set; }
        public int renderQueue { get; set; }
        public void SetInt(string name, int value) { }
    }

    public class Renderer : Component
    {
        public bool enabled { get; set; }
        public Material sharedMaterial { get; set; }
        public Rendering.ShadowCastingMode shadowCastingMode { get; set; }
        public bool receiveShadows { get; set; }
        public int sortingOrder { get; set; }
    }

    public class MeshRenderer : Renderer { }
    public sealed class MeshFilter : Component { public Mesh sharedMesh { get; set; } }

    public enum LineAlignment { View = 0, TransformZ = 1 }
    public enum LineTextureMode { Stretch = 0, Tile = 1, DistributePerSegment = 2, RepeatPerSegment = 3, Static = 4 }

    public sealed partial class LineRenderer : Renderer
    {
        public float widthMultiplier { get; set; }
        public Color startColor { get; set; }
        public Color endColor { get; set; }
        public int positionCount { get; set; }
        public bool useWorldSpace { get; set; }
        public bool loop { get; set; }
        public LineAlignment alignment { get; set; }
        public LineTextureMode textureMode { get; set; }
        public void SetPositions(Vector3[] positions) { }
    }

    // Particles --------------------------------------------------------------------------------------------------
    public enum ParticleSystemSimulationSpace { Local = 0, World = 1, Custom = 2 }
    public enum ParticleSystemGravitySource { Physics3D = 0, Physics2D = 1 }
    public enum ParticleSystemShapeType { Sphere = 0, Hemisphere = 2, Cone = 4, Box = 5, Mesh = 6, ConeVolume = 8, Circle = 10, SingleSidedEdge = 12 }
    public enum ParticleSystemRenderMode { Billboard = 0, Stretch = 1, HorizontalBillboard = 2, VerticalBillboard = 3, Mesh = 4, None = 5 }

    public sealed partial class ParticleSystem : Component
    {
        public struct MinMaxCurve
        {
            public MinMaxCurve(float constant) { }
            public MinMaxCurve(float min, float max) { }
            public static implicit operator MinMaxCurve(float constant) { return default(MinMaxCurve); }
        }

        public struct MinMaxGradient
        {
            public static implicit operator MinMaxGradient(Color color) { return default(MinMaxGradient); }
        }

        public partial struct MainModule
        {
            public bool loop { get; set; }
            public bool playOnAwake { get; set; }
            public MinMaxCurve startLifetime { get; set; }
            public MinMaxCurve startSpeed { get; set; }
            public MinMaxCurve startSize { get; set; }
            public MinMaxGradient startColor { get; set; }
            public MinMaxCurve gravityModifier { get; set; }
            public ParticleSystemGravitySource gravitySource { get; set; }
            public ParticleSystemSimulationSpace simulationSpace { get; set; }
            public int maxParticles { get; set; }
        }

        public partial struct EmissionModule
        {
            public bool enabled { get; set; }
        }

        public partial struct ShapeModule
        {
            public bool enabled { get; set; }
            public ParticleSystemShapeType shapeType { get; set; }
            public float angle { get; set; }
            public float radius { get; set; }
            public float radiusThickness { get; set; }
            public Vector3 rotation { get; set; }
        }

        public partial struct EmitParams
        {
            public Vector3 position { get; set; }
            public bool applyShapeToPosition { get; set; }
        }

        public MainModule main { get { return default(MainModule); } }
        public bool isPlaying { get { return false; } }
        public void Emit(EmitParams emitParams, int count) { }
        public EmissionModule emission { get { return default(EmissionModule); } }
        public ShapeModule shape { get { return default(ShapeModule); } }
        public void Play() { }
        public void Emit(int count) { }
    }

    public sealed partial class ParticleSystemRenderer : Renderer
    {
        public ParticleSystemRenderMode renderMode { get; set; }
    }
}

namespace UnityEngine.SceneManagement
{
    public partial struct Scene { }
}

namespace UnityEngine.Rendering
{
    public enum ShadowCastingMode { Off = 0, On = 1, TwoSided = 2, ShadowsOnly = 3 }
}

namespace UnityEngine.Events
{
    [Serializable]
    public abstract class UnityEventBase { }

    public delegate void UnityAction<T0, T1>(T0 arg0, T1 arg1);

    [Serializable]
    public class UnityEvent<T0, T1> : UnityEventBase
    {
        public UnityEvent() { }
        public void AddListener(UnityAction<T0, T1> call) { }
        public void Invoke(T0 arg0, T1 arg1) { }
    }
}

namespace UnityEditor
{
    public partial class Undo
    {
        public static void RecordObject(UnityEngine.Object objectToUndo, string name) { }
    }

    public sealed partial class PrefabUtility
    {
        public static void RecordPrefabInstancePropertyModifications(UnityEngine.Object targetObject) { }
    }
}
