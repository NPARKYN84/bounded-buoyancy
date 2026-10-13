// Further stand-ins, for the demo sample only (camera, light, primitives, IMGUI, and the editor menu), transcribed
// from Unity's C# reference source like UnityStubs.cs.
#pragma warning disable CS0626, CS0824, CS0067, CS0649
using System;

namespace UnityEngine
{
    public partial class Object
    {
        public static T FindAnyObjectByType<T>() where T : Object { return null; }
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
