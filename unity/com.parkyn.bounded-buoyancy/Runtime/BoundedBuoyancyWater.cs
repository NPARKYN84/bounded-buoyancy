// Bounded Buoyancy for Unity. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System.Collections.Generic;
using BoundedBuoyancy.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoundedBuoyancy
{
    /// <summary>
    /// A body of water for Bounded Buoyancy Body, with optional waves, a current and splashes.
    /// <para>Place this object where the middle of the water's surface should be; the water fills a box of `size`
    /// below it. Turn it about Y to turn the waves and the current; keep its scale at 1 and set the size instead.
    /// Once a scene has any Bounded Buoyancy Water, bodies float only inside one, so ponds, pools and tanks can sit
    /// at different heights. Scenes without one use each body's water level.</para>
    /// <para>Waves move the surface only: bodies rise, fall and rock with them, and nothing is carried along by
    /// them. A current makes the water flow, and bodies drift with it. Splashes and rings are for show only.</para>
    /// </summary>
    [AddComponentMenu("Physics/Bounded Buoyancy Water")]
    [DefaultExecutionOrder(-100)]   // advance the wave clock before the bodies use it
    [DisallowMultipleComponent]
    public class BoundedBuoyancyWater : MonoBehaviour
    {
        [Tooltip("Width (X), depth (Y) and length (Z) of the water, in metres. The top face is the surface.")]
        public Vector3 size = new Vector3(10f, 3f, 10f);

        [Tooltip("Density of the water, kg/m³: 1000 fresh, about 1025 sea.")]
        [Min(1f)] public float density = 1000f;

        [Header("Waves")]
        [Tooltip("Height of the largest wave above the mean surface, in metres. 0 for still water.")]
        [Min(0f)] public float waveHeight = 0f;

        [Tooltip("Distance from one crest to the next, in metres.")]
        [Min(0.05f)] public float waveLength = 4f;

        [Tooltip("Direction the waves travel, in degrees round the vertical from this object's +X towards its +Z.")]
        [Range(-180f, 180f)] public float waveDirection = 0f;

        [Header("Current")]
        [Tooltip("Velocity of the flowing water in m/s, in this object's own frame (so turning the object turns the river). Bodies are carried along by drag on their speed through the water.")]
        public Vector3 current = Vector3.zero;

        [Tooltip("Width of the slower water along each bank, in metres: across the flow, the current falls to zero at the sides over this distance. 0 keeps it the same right up to the sides.")]
        [Min(0f)] public float currentEdge = 0f;

        [Header("Splashes")]
        [Tooltip("Throw up a splash of droplets when a body hits the water fast enough.")]
        public bool splashes = true;

        [Tooltip("Leave rings spreading over the surface where bodies go in and come out.")]
        public bool ripples = true;

        [Tooltip("Slowest impact (m/s) that throws up a splash; slower ones only leave rings.")]
        [Min(0f)] public float splashMinSpeed = 1.5f;

        [Tooltip("Colour of the droplets and rings.")]
        public Color splashColor = new Color(0.95f, 0.98f, 1f, 0.85f);

        [Header("Look")]
        [Tooltip("Draw a simple translucent surface that moves with the waves. Turn off to use your own.")]
        public bool showSurface = true;

        [Tooltip("Colour and opacity of the drawn surface (with the built-in look).")]
        public Color surfaceColor = new Color(0.22f, 0.52f, 0.82f, 0.55f);

        [Tooltip("Optional material for the drawn surface, such as a transparent lit material from your render pipeline. Empty uses a built-in unlit look shaded by vertex colours.")]
        public Material surfaceMaterial;

        [Tooltip("Optional material for the droplets and rings. Empty uses a built-in soft dot.")]
        public Material effectsMaterial;

        internal static readonly List<BoundedBuoyancyWater> active = new List<BoundedBuoyancyWater>();

        /// <summary>Every enabled Bounded Buoyancy Water.</summary>
        public static IReadOnlyList<BoundedBuoyancyWater> Active { get { return active; } }

        readonly WaterVolume core = new WaterVolume();
        internal PhysicsScene PhysicsScene;

        const float RippleLife = 1.8f;
        const float RippleSpeed = 0.9f;   // m/s, how fast the rings spread
        const int MaxRipples = 48;
        const int RingPoints = 32;
        static readonly Vector3 LightDir = new Vector3(0.35f, 0.85f, 0.4f).normalized;

        MeshFilter surfaceFilter;
        MeshRenderer surfaceRenderer;
        Mesh surfaceMesh;
        int surfaceCells = -1;
        Vector3 surfaceSize;
        Vector3[] surfaceVerts;
        Vector3[] surfaceNormals;
        Color32[] surfaceColors;
        bool surfaceStill;
        Color surfaceDrawnColor;
        ParticleSystem splashSystem;
        readonly List<Ripple> ripplePool = new List<Ripple>();
        bool warnedScale;

        sealed class Ripple
        {
            public LineRenderer outer, inner;
            public Vector3[] points = new Vector3[RingPoints];
            public Vector2 centre;   // water's own x, z
            public float age, startRadius, strength;
            public bool alive;
        }

        /// <summary>Wave clock in seconds, advanced with the physics step.</summary>
        public float WaveTime { get { return (float)core.Time; } set { core.Time = value; } }

        /// <summary>The engine-free water volume the bodies ask.</summary>
        public WaterVolume Core { get { return core; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { active.Clear(); }

        void OnEnable()
        {
            if (!active.Contains(this))
                active.Add(this);
            PhysicsScene = gameObject.scene.GetPhysicsScene();
            Sync();
        }

        void OnDisable()
        {
            active.Remove(this);
            if (surfaceRenderer != null)
                surfaceRenderer.enabled = false;
            foreach (Ripple r in ripplePool)
            {
                r.alive = false;
                r.outer.enabled = false;
                r.inner.enabled = false;
            }
        }

        void OnValidate()
        {
            size = Vector3.Max(size, new Vector3(0.01f, 0.01f, 0.01f));
            waveLength = Mathf.Max(waveLength, 0.05f);
            if (isActiveAndEnabled)
                Sync();
        }

        void FixedUpdate()
        {
            if (Bridge.FixedStep3D)
                Advance(Time.fixedDeltaTime);
        }

        /// <summary>Copy the settings and advance the wave clock by one physics step.</summary>
        internal void Advance(float dt)
        {
            Sync();
            core.Time += dt;
        }

        /// <summary>Copy the transform and settings to the core. Done every physics step; call it after moving the
        /// water or changing its settings if bodies must see the change before then.</summary>
        public void Sync()
        {
            core.Position = Bridge.V(transform.position);
            core.Rotation = Bridge.M(transform.rotation);
            core.Size = Bridge.V(size);
            core.Density = density;
            core.Current = Bridge.V(current);
            core.CurrentEdge = currentEdge;
            core.SetWaves(waveHeight, waveLength, waveDirection, -Physics.gravity.y);
        }

        /// <summary>Height of the water surface (world Y) above or below the world point p.</summary>
        public float HeightAt(Vector3 p) { return (float)core.HeightAt(Bridge.V(p)); }

        /// <summary>Velocity of the water at the world point p (world frame).</summary>
        public Vector3 CurrentAt(Vector3 p) { return Bridge.U(core.CurrentAt(Bridge.V(p))); }

        /// <summary>Whether the world point p lies over or in the water: inside it across, and above its floor.</summary>
        public bool Contains(Vector3 p) { return core.Contains(Bridge.V(p)); }

        /// <summary>The most the surface rises or falls from its mean height.</summary>
        public float MaxWave { get { return (float)core.MaxWave; } }

        /// <summary>Throw up a splash and rings at the world point `at`, for a body `across` metres wide hitting the
        /// water at `speed` m/s. Bodies call this themselves; call it to make a splash of your own.</summary>
        public void Splash(Vector3 at, float speed, float across)
        {
            if (!Application.isPlaying || !isActiveAndEnabled)
                return;
            across = Mathf.Clamp(across, 0.02f, 20f);
            Vector3 l = Quaternion.Inverse(transform.rotation) * (at - transform.position);
            if (ripples && speed > 0.05f)
                AddRipple(new Vector2(l.x, l.z), across, Mathf.Clamp(speed / 3f, 0.25f, 1f));
            if (splashes && speed >= splashMinSpeed)
                AddSplash(l, speed, across);
        }

        void Update()
        {
            if (!warnedScale && (transform.lossyScale - Vector3.one).sqrMagnitude > 1e-6f)
            {
                warnedScale = true;
                Debug.LogWarning("Bounded Buoyancy Water '" + name + "': keep its scale at 1 and set its size instead; the water's size ignores scale.", this);
            }
            DrawSurface();
            AgeRipples(Time.deltaTime);
        }

        // Surface ----------------------------------------------------------------------------------------------

        void DrawSurface()
        {
            if (!showSurface)
            {
                if (surfaceRenderer != null)
                    surfaceRenderer.enabled = false;
                return;
            }
            if (surfaceFilter == null)
            {
                GameObject go = Visuals.Child(transform, "Water Surface");
                surfaceFilter = go.AddComponent<MeshFilter>();
                surfaceRenderer = go.AddComponent<MeshRenderer>();
                surfaceRenderer.shadowCastingMode = ShadowCastingMode.Off;
                surfaceRenderer.receiveShadows = false;
                surfaceMesh = new Mesh { name = "Bounded Buoyancy Surface" };
                surfaceMesh.MarkDynamic();
                surfaceFilter.sharedMesh = surfaceMesh;
            }
            surfaceRenderer.enabled = true;
            surfaceRenderer.sharedMaterial = surfaceMaterial != null ? surfaceMaterial : Visuals.Surface;
            bool flowing = current.x != 0f || current.z != 0f;
            int cells;
            if (waveHeight > 0f)
                cells = Mathf.Clamp((int)(Mathf.Max(size.x, size.z) / (waveLength / 16f)), 1, 128);
            else if (flowing)
                cells = Mathf.Clamp((int)(Mathf.Max(size.x, size.z) / 0.5f), 1, 64);
            else
                cells = 1;
            if (cells != surfaceCells || size != surfaceSize)
                BuildGrid(cells);
            else if (surfaceStill && waveHeight <= 0f && !flowing && surfaceColor == surfaceDrawnColor)
                return;   // still water: nothing moves
            UpdateGrid(flowing);
        }

        void BuildGrid(int cells)
        {
            surfaceCells = cells;
            surfaceSize = size;
            int n = cells + 1;
            surfaceVerts = new Vector3[n * n];
            surfaceNormals = new Vector3[n * n];
            surfaceColors = new Color32[n * n];
            var tris = new int[cells * cells * 6];
            int t = 0;
            for (int j = 0; j < cells; j++)
            {
                for (int i = 0; i < cells; i++)
                {
                    int a = j * n + i;
                    tris[t++] = a; tris[t++] = a + n; tris[t++] = a + 1;
                    tris[t++] = a + 1; tris[t++] = a + n; tris[t++] = a + n + 1;
                }
            }
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                    surfaceVerts[j * n + i] = new Vector3((i / (float)cells - 0.5f) * size.x, 0f, (j / (float)cells - 0.5f) * size.z);
            surfaceMesh.Clear();
            surfaceMesh.vertices = surfaceVerts;
            surfaceMesh.triangles = tris;
            surfaceStill = false;
        }

        void UpdateGrid(bool flowing)
        {
            int n = surfaceCells + 1;
            WaveSet waves = core.Waves;
            double t = core.Time;
            float speed = new Vector2(current.x, current.z).magnitude;
            Vector2 dir = speed > 0f ? new Vector2(current.x, current.z) / speed : Vector2.right;
            Color c = surfaceColor;
            for (int k = 0; k < surfaceVerts.Length; k++)
            {
                Vector3 p = surfaceVerts[k];
                double dx, dz;
                waves.Slope(p.x, p.z, t, out dx, out dz);
                p.y = (float)waves.Offset(p.x, p.z, t);
                surfaceVerts[k] = p;
                Vector3 nrm = new Vector3((float)-dx, 1f, (float)-dz).normalized;
                surfaceNormals[k] = nrm;
                float shade = 0.72f + 0.32f * Mathf.Max(0f, Vector3.Dot(nrm, LightDir));
                if (flowing)
                {
                    // Faint streaks carried downstream, so the flow can be seen.
                    float along = Vector2.Dot(new Vector2(p.x, p.z) - new Vector2(current.x, current.z) * (float)t, dir);
                    float across = Vector2.Dot(new Vector2(p.x, p.z), new Vector2(-dir.y, dir.x));
                    shade += 0.08f * Mathf.Sin(along * 2.5f + 1.5f * Mathf.Sin(across * 1.3f)) * Mathf.Sin(along * 0.6f + across * 0.8f);
                }
                surfaceColors[k] = new Color(Mathf.Clamp01(c.r * shade), Mathf.Clamp01(c.g * shade), Mathf.Clamp01(c.b * shade), c.a);
            }
            surfaceMesh.vertices = surfaceVerts;
            surfaceMesh.normals = surfaceNormals;
            surfaceMesh.colors32 = surfaceColors;
            surfaceStill = waveHeight <= 0f && !flowing;
            surfaceDrawnColor = c;
        }

        // Splashes and rings --------------------------------------------------------------------------------------

        void AddSplash(Vector3 local, float speed, float across)
        {
            if (splashSystem == null)
            {
                GameObject go = Visuals.Child(transform, "Splashes");
                splashSystem = go.AddComponent<ParticleSystem>();
                ParticleSystem.MainModule main = splashSystem.main;
                main.playOnAwake = false;
                main.loop = true;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 2000;
                main.gravityModifier = 1f;
                main.gravitySource = ParticleSystemGravitySource.Physics3D;
                ParticleSystem.EmissionModule emission = splashSystem.emission;
                emission.enabled = false;
                ParticleSystem.ShapeModule shape = splashSystem.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 28f;
                shape.rotation = new Vector3(-90f, 0f, 0f);   // the cone opens upwards
                var renderer = go.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            if (!splashSystem.isPlaying)
                splashSystem.Play();
            var r = splashSystem.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = effectsMaterial != null ? effectsMaterial : Visuals.Effects;
            float up = Mathf.Min(0.45f * speed, 7f);
            float drop = Mathf.Clamp(across, 0.1f, 3f);
            ParticleSystem.MainModule m = splashSystem.main;
            m.startLifetime = Mathf.Clamp(0.25f * Mathf.Sqrt(speed), 0.5f, 1.4f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.5f * up, up);
            m.startSize = new ParticleSystem.MinMaxCurve(0.04f + 0.04f * drop, 0.08f + 0.08f * drop);
            m.startColor = splashColor;
            ParticleSystem.ShapeModule sh = splashSystem.shape;
            sh.radius = 0.45f * across;
            sh.radiusThickness = 1f / 3f;   // a ring from 0.3 to 0.45 of the width out
            // Emitted at the point (world space), keeping the ring and the upward cone of the shape.
            var at = new ParticleSystem.EmitParams
            {
                position = transform.position + transform.rotation * new Vector3(local.x, local.y + 0.02f, local.z),
                applyShapeToPosition = true,
            };
            splashSystem.Emit(at, Mathf.Clamp((int)(14f * speed * Mathf.Sqrt(across)), 16, 120));
        }

        void AddRipple(Vector2 centre, float across, float strength)
        {
            Ripple r = null;
            foreach (Ripple q in ripplePool)
            {
                if (!q.alive)
                {
                    r = q;
                    break;
                }
            }
            if (r == null)
            {
                if (ripplePool.Count >= MaxRipples)
                {
                    r = ripplePool[0];
                    foreach (Ripple q in ripplePool)
                        if (q.age > r.age)
                            r = q;
                }
                else
                {
                    r = new Ripple { outer = Ring("Ripple"), inner = Ring("Ripple") };
                    ripplePool.Add(r);
                }
            }
            r.alive = true;
            r.age = 0f;
            r.centre = centre;
            r.startRadius = 0.5f * across;
            r.strength = strength;
            float width = Mathf.Clamp(0.06f * across, 0.02f, 0.3f);
            r.outer.widthMultiplier = width;
            r.inner.widthMultiplier = width;
            r.outer.enabled = true;
            r.inner.enabled = true;
            UpdateRipple(r);
        }

        LineRenderer Ring(string label)
        {
            GameObject go = Visuals.Child(transform, label);
            // Lines lie flat: they face this object's Z axis, which is turned to point down.
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.alignment = LineAlignment.TransformZ;
            lr.positionCount = RingPoints;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = effectsMaterial != null ? effectsMaterial : Visuals.Lines;
            lr.textureMode = LineTextureMode.Stretch;
            return lr;
        }

        void AgeRipples(float dt)
        {
            foreach (Ripple r in ripplePool)
            {
                if (!r.alive)
                    continue;
                r.age += dt;
                if (r.age >= RippleLife)
                {
                    r.alive = false;
                    r.outer.enabled = false;
                    r.inner.enabled = false;
                    continue;
                }
                UpdateRipple(r);
            }
        }

        void UpdateRipple(Ripple r)
        {
            float radius = r.startRadius + RippleSpeed * r.age;
            float fade = r.strength * (1f - r.age / RippleLife);
            WaveSet waves = core.Waves;
            double t = core.Time;
            SetRing(r, r.outer, radius, waves, t);
            Color c = splashColor;
            c.a *= fade;
            r.outer.startColor = c;
            r.outer.endColor = c;
            SetRing(r, r.inner, 0.62f * radius, waves, t);
            c.a *= 0.6f;
            r.inner.startColor = c;
            r.inner.endColor = c;
        }

        void SetRing(Ripple r, LineRenderer lr, float radius, WaveSet waves, double t)
        {
            // Points in the ring's own frame (turned 90 degrees about X): (x, z, -height) in the water's frame.
            for (int i = 0; i < RingPoints; i++)
            {
                float a = 2f * Mathf.PI * i / RingPoints;
                float x = r.centre.x + radius * Mathf.Cos(a);
                float z = r.centre.y + radius * Mathf.Sin(a);
                float h = (float)waves.Offset(x, z, t) + 0.006f;
                r.points[i] = new Vector3(x, z, -h);
            }
            lr.SetPositions(r.points);
        }

        void OnDestroy()
        {
            if (surfaceMesh != null)
                Destroy(surfaceMesh);
        }

        void OnDrawGizmos()
        {
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.color = new Color(0.2f, 0.5f, 0.9f, 0.6f);
            Gizmos.DrawWireCube(new Vector3(0f, -0.5f * size.y, 0f), size);
            if (!Application.isPlaying || !showSurface)
            {
                Gizmos.color = new Color(surfaceColor.r, surfaceColor.g, surfaceColor.b, 0.25f);
                Gizmos.DrawCube(Vector3.zero, new Vector3(size.x, 0.001f, size.z));
            }
            if (current != Vector3.zero)
            {
                Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.9f);
                Vector3 d = current.normalized * Mathf.Min(0.25f * Mathf.Min(size.x, size.z), 3f);
                Gizmos.DrawLine(-d, d);
                Gizmos.DrawLine(d, d - Quaternion.Euler(0f, 25f, 0f) * d * 0.3f);
                Gizmos.DrawLine(d, d - Quaternion.Euler(0f, -25f, 0f) * d * 0.3f);
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
