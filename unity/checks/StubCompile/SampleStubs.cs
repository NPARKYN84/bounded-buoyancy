// Further stand-ins, for the demo sample only (camera, light, primitives, IMGUI, and the editor menu), transcribed
// from Unity's C# reference source like UnityStubs.cs.
#pragma warning disable CS0626, CS0824, CS0067, CS0649
using System;

namespace UnityEngine
{
    public enum FindObjectsSortMode { None = 0, InstanceID = 1 }

    public partial class Object
    {
        public static T FindAnyObjectByType<T>() where T : Object { return null; }
        public static T[] FindObjectsByType<T>(FindObjectsSortMode sortMode) where T : Object { return null; }
        public int GetInstanceID() { return 0; }
    }

    public enum CameraClearFlags { Skybox = 1, Color = 2, SolidColor = 2, Depth = 3, Nothing = 4 }

    public sealed partial class Camera : Behaviour
    {
        public static Camera main { get { return null; } }
        public float nearClipPlane { get; set; }
        public float farClipPlane { get; set; }
        public bool orthographic { get; set; }
        public float orthographicSize { get; set; }
        public CameraClearFlags clearFlags { get; set; }
        public Color backgroundColor { get; set; }
    }

    public enum LightType { Spot = 0, Directional = 1, Point = 2 }

    public sealed partial class Light : Behaviour
    {
        public LightType type { get; set; }
        public float intensity { get; set; }
        public Color color { get; set; }
        public float range { get; set; }
        public float spotAngle { get; set; }
    }

    public enum PrimitiveType { Sphere = 0, Capsule = 1, Cylinder = 2, Cube = 3, Plane = 4, Quad = 5 }
    public enum RigidbodyInterpolation { None = 0, Interpolate = 1, Extrapolate = 2 }
    public enum RigidbodyInterpolation2D { None = 0, Interpolate = 1, Extrapolate = 2 }
    public enum TextAnchor { UpperLeft = 0, UpperCenter = 1, UpperRight = 2, MiddleLeft = 3, MiddleCenter = 4, MiddleRight = 5, LowerLeft = 6, LowerCenter = 7, LowerRight = 8 }

    public struct Rect
    {
        public Rect(float x, float y, float width, float height) { }
    }

    public sealed class Screen
    {
        public static int width { get { return 0; } }
        public static int height { get { return 0; } }
    }

    public sealed class GUILayoutOption { }

    // UnityEngine.Event and its enums (Modules/IMGUI/Event.cs, EventEnums.cs; Runtime/Export/Input/KeyCode.cs).
    public enum EventType { MouseDown = 0, MouseUp = 1, MouseMove = 2, MouseDrag = 3, KeyDown = 4, KeyUp = 5 }
    public enum KeyCode { None = 0, A = 97, D = 100, S = 115, W = 119 }
    public sealed partial class Event
    {
        public static Event current { get { return null; } set { } }
        public EventType type { get { return EventType.MouseDown; } set { } }
        public KeyCode keyCode { get { return KeyCode.None; } set { } }
    }

    public sealed partial class GUIStyle
    {
        public GUIStyle(GUIStyle other) { }
        public bool wordWrap { get; set; }
        public TextAnchor alignment { get; set; }
        public int fontSize { get; set; }
    }

    public sealed partial class GUISkin : Object
    {
        public GUIStyle box { get { return null; } }
    }

    public partial class GUI
    {
        public static GUISkin skin { get { return null; } set { } }
        public static void Box(Rect position, string text, GUIStyle style) { }
    }

    public partial class GUILayout
    {
        public static void BeginArea(Rect screenRect) { }
        public static void EndArea() { }
        public static void Label(string text, params GUILayoutOption[] options) { }
        public static bool Button(string text, params GUILayoutOption[] options) { return false; }
        public static void Space(float pixels) { }
        public static bool Toggle(bool value, string text, params GUILayoutOption[] options) { return value; }
        public static float HorizontalSlider(float value, float leftValue, float rightValue, params GUILayoutOption[] options) { return value; }
        public static bool RepeatButton(string text, params GUILayoutOption[] options) { return false; }
    }

    // For the sea scenes (Runtime/Export/Graphics/RenderSettings.bindings.cs, Physics/Dynamics.bindings.cs and others).
    public enum FogMode { Linear = 1, Exponential = 2, ExponentialSquared = 3 }

    public sealed partial class RenderSettings : Object
    {
        public static bool fog { get; set; }
        public static FogMode fogMode { get; set; }
        public static float fogDensity { get; set; }
        public static Color fogColor { get; set; }
    }

    public partial class Transform
    {
        public Vector3 localEulerAngles { get; set; }
    }

    public sealed partial class GameObject
    {
        public void SetActive(bool value) { }
    }

    public partial class Rigidbody
    {
        public void MovePosition(Vector3 position) { }
    }

    public partial class Physics
    {
        public static void IgnoreCollision(Collider collider1, Collider collider2) { }
    }

    public partial struct Vector3
    {
        public static Vector3 RotateTowards(Vector3 current, Vector3 target, float maxRadiansDelta, float maxMagnitudeDelta) { return current; }
        public static Vector3 ProjectOnPlane(Vector3 vector, Vector3 planeNormal) { return vector; }
    }

    public partial struct Quaternion
    {
        public static Quaternion FromToRotation(Vector3 fromDirection, Vector3 toDirection) { return default(Quaternion); }
        public static Quaternion Euler(Vector3 euler) { return default(Quaternion); }
    }

    public partial struct Color
    {
        public static Color Lerp(Color a, Color b, float t) { return a; }
        public static Color white { get { return default(Color); } }
        public static Color gray { get { return default(Color); } }
    }

    public partial struct Mathf
    {
        public static float SmoothStep(float from, float to, float t) { return from; }
        public static float InverseLerp(float a, float b, float value) { return 0f; }
        public static float Repeat(float t, float length) { return t; }
        public static float Round(float f) { return f; }
    }

    // For the drawn ships (Runtime/Export/Graphics/Texture.bindings.cs, Mesh.cs, Material.bindings.cs).
    public enum FilterMode { Point = 0, Bilinear = 1, Trilinear = 2 }

    public partial class Texture
    {
        public FilterMode filterMode { get; set; }
        public int anisoLevel { get; set; }
    }

    public sealed partial class Texture2D
    {
        public void Apply(bool updateMipmaps, bool makeNoLongerReadable) { }
    }

    public partial class Material
    {
        public void EnableKeyword(string keyword) { }
        public void SetColor(string name, Color value) { }
    }

    public sealed partial class Mesh
    {
        public Vector2[] uv { get; set; }
    }
}

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class MenuItem : Attribute { public MenuItem(string itemName) { } }

    public sealed partial class EditorApplication
    {
        public static bool isPlaying { get; set; }
    }
}

namespace UnityEditor.SceneManagement
{
    public enum NewSceneMode { Single, Additive }
    public enum NewSceneSetup { EmptyScene, DefaultGameObjects }

    public sealed partial class EditorSceneManager
    {
        public static bool SaveCurrentModifiedScenesIfUserWantsTo() { return true; }
        public static UnityEngine.SceneManagement.Scene NewScene(NewSceneSetup setup, NewSceneMode mode) { return default(UnityEngine.SceneManagement.Scene); }
    }
}
