// Bounded Buoyancy for Unity. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using BoundedBuoyancy.Core;
using UnityEngine;

namespace BoundedBuoyancy
{
    /// <summary>Conversions between Unity's types and the core's, and the few Rigidbody calls that were renamed
    /// in Unity 6 (velocity, drag and angularDrag became linearVelocity, linearDamping and angularDamping).</summary>
    internal static class Bridge
    {
        internal static V3 V(Vector3 v) { return new V3(v.x, v.y, v.z); }
        internal static V2 V(Vector2 v) { return new V2(v.x, v.y); }
        internal static Vector3 U(V3 v) { return new Vector3((float)v.x, (float)v.y, (float)v.z); }
        internal static Vector2 U(V2 v) { return new Vector2((float)v.x, (float)v.y); }
        internal static M3 M(Quaternion q) { return M3.FromQuaternion(q.x, q.y, q.z, q.w); }

        /// <summary>A rotation whose axes are the columns of `a` (a right-handed rotation matrix).</summary>
        internal static Quaternion Q(M3 a) { return Quaternion.LookRotation(U(a.Z), U(a.Y)); }

        /// <summary>Whether buoyancy should step now: in FixedUpdate when physics runs there (the default), and
        /// otherwise from BoundedBuoyancySimulation.</summary>
        internal static bool FixedStep3D { get { return Physics.simulationMode == SimulationMode.FixedUpdate; } }
        internal static bool FixedStep2D { get { return Physics2D.simulationMode == SimulationMode2D.FixedUpdate; } }

        internal static Vector3 GetVelocity(Rigidbody rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }

        internal static void SetVelocity(Rigidbody rb, Vector3 v)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = v;
#else
            rb.velocity = v;
#endif
        }

        internal static float GetLinearDamping(Rigidbody rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearDamping;
#else
            return rb.drag;
#endif
        }

        internal static void SetLinearDamping(Rigidbody rb, float value)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearDamping = value;
#else
            rb.drag = value;
#endif
        }

        internal static float GetAngularDamping(Rigidbody rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.angularDamping;
#else
            return rb.angularDrag;
#endif
        }

        internal static void SetAngularDamping(Rigidbody rb, float value)
        {
#if UNITY_6000_0_OR_NEWER
            rb.angularDamping = value;
#else
            rb.angularDrag = value;
#endif
        }

        internal static Vector2 GetVelocity(Rigidbody2D rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }

        internal static float GetLinearDamping(Rigidbody2D rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearDamping;
#else
            return rb.drag;
#endif
        }

        internal static void SetLinearDamping(Rigidbody2D rb, float value)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearDamping = value;
#else
            rb.drag = value;
#endif
        }

        internal static float GetAngularDamping(Rigidbody2D rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.angularDamping;
#else
            return rb.angularDrag;
#endif
        }

        internal static void SetAngularDamping(Rigidbody2D rb, float value)
        {
#if UNITY_6000_0_OR_NEWER
            rb.angularDamping = value;
#else
            rb.angularDrag = value;
#endif
        }
    }

    /// <summary>Materials and textures for the drawn water and effects, made once. They use a shader present in
    /// every render pipeline's player builds (Sprites/Default: unlit, transparent, vertex colours).</summary>
    internal static class Visuals
    {
        static Material s_Surface;
        static Material s_Effects;
        static Material s_Lines;
        static Texture2D s_Dot;
        static Texture2D s_Band;
        static bool s_Warned;

        static Shader FindShader()
        {
            Shader s = Shader.Find("Sprites/Default");
            if (s == null)
                s = Shader.Find("Hidden/Internal-Colored");
            if (s == null && !s_Warned)
            {
                s_Warned = true;
                Debug.LogWarning("Bounded Buoyancy: no built-in transparent shader found; set the surface and effects materials on the water.");
            }
            return s;
        }

        static bool s_Hooked;

        static void Release()
        {
            Object.Destroy(s_Surface);
            Object.Destroy(s_Effects);
            Object.Destroy(s_Lines);
            Object.Destroy(s_Dot);
            Object.Destroy(s_Band);
        }

        static Material Make(string name)
        {
            if (!s_Hooked)
            {
                s_Hooked = true;
                Application.quitting += Release;   // also when leaving play mode in the editor
            }
            Shader shader = FindShader();
            if (shader == null)
                return null;
            var m = new Material(shader) { name = name, hideFlags = HideFlags.DontSave };
            if (shader.name == "Hidden/Internal-Colored")
            {
                m.SetInt("_SrcBlend", 5);    // SrcAlpha
                m.SetInt("_DstBlend", 10);   // OneMinusSrcAlpha
                m.SetInt("_Cull", 0);
                m.SetInt("_ZWrite", 0);
            }
            m.renderQueue = 3000;
            return m;
        }

        /// <summary>For water surfaces: colour comes from the vertices.</summary>
        internal static Material Surface
        {
            get
            {
                if (s_Surface == null)
                    s_Surface = Make("Bounded Buoyancy Surface");
                return s_Surface;
            }
        }

        /// <summary>For droplets and rings: a soft round dot, coloured by the particles and lines.</summary>
        internal static Material Effects
        {
            get
            {
                if (s_Effects == null)
                {
                    s_Effects = Make("Bounded Buoyancy Effects");
                    if (s_Effects != null)
                        s_Effects.mainTexture = Dot;
                }
                return s_Effects;
            }
        }

        /// <summary>For rings drawn as lines: soft along both edges of the line.</summary>
        internal static Material Lines
        {
            get
            {
                if (s_Lines == null)
                {
                    s_Lines = Make("Bounded Buoyancy Lines");
                    if (s_Lines != null)
                        s_Lines.mainTexture = Band;
                }
                return s_Lines;
            }
        }

        static Texture2D Band
        {
            get
            {
                if (s_Band == null)
                {
                    const int n = 16;
                    s_Band = new Texture2D(4, n, TextureFormat.RGBA32, false)
                    {
                        name = "Bounded Buoyancy Band",
                        wrapMode = TextureWrapMode.Clamp,
                        hideFlags = HideFlags.DontSave,
                    };
                    var px = new Color32[4 * n];
                    for (int y = 0; y < n; y++)
                    {
                        float v = (y + 0.5f) / n * 2f - 1f;
                        var c = new Color32(255, 255, 255, (byte)(255f * Mathf.Clamp01((1f - Mathf.Abs(v)) * 2f)));
                        for (int x = 0; x < 4; x++)
                            px[y * 4 + x] = c;
                    }
                    s_Band.SetPixels32(px);
                    s_Band.Apply();
                }
                return s_Band;
            }
        }

        static Texture2D Dot
        {
            get
            {
                if (s_Dot == null)
                {
                    const int n = 32;
                    s_Dot = new Texture2D(n, n, TextureFormat.RGBA32, false)
                    {
                        name = "Bounded Buoyancy Dot",
                        wrapMode = TextureWrapMode.Clamp,
                        hideFlags = HideFlags.DontSave,
                    };
                    var px = new Color32[n * n];
                    for (int y = 0; y < n; y++)
                    {
                        for (int x = 0; x < n; x++)
                        {
                            float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                            float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                            px[y * n + x] = new Color32(255, 255, 255, (byte)(255f * Mathf.Clamp01(a * 2.5f)));
                        }
                    }
                    s_Dot.SetPixels32(px);
                    s_Dot.Apply();
                }
                return s_Dot;
            }
        }

        /// <summary>A child object that only draws (no physics). Made in play mode only.</summary>
        internal static GameObject Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            return go;
        }
    }
}
