// Bounded Buoyancy for Unity, 2D. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System.Collections.Generic;
using BoundedBuoyancy.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoundedBuoyancy
{
    /// <summary>
    /// A body of water for Bounded Buoyancy Body 2D, with optional waves, a current and splashes. Place this object
    /// where the middle of the surface should be; the water fills a rectangle of `size` below it. Keep it unrotated
    /// and unscaled, and set the size instead. Once a scene has any Bounded Buoyancy Water 2D, bodies float only
    /// inside one.
    /// </summary>
    [AddComponentMenu("Physics 2D/Bounded Buoyancy Water 2D")]
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class BoundedBuoyancyWater2D : MonoBehaviour
    {
        [Tooltip("Width and depth of the water, in metres.")]
        public Vector2 size = new Vector2(12f, 3f);

        [Tooltip("Density of the water, kg/m³: 1000 fresh, about 1025 sea.")]
        [Min(1f)] public float density = 1000f;

        [Header("Waves")]
        [Tooltip("Height of the largest wave above the mean surface, in metres. 0 for still water.")]
        [Min(0f)] public float waveHeight = 0f;

        [Tooltip("Distance from one crest to the next, in metres.")]
        [Min(0.05f)] public float waveLength = 4f;

        [Header("Current")]
        [Tooltip("Velocity of the flowing water, m/s (positive x flows right).")]
        public Vector2 current = Vector2.zero;

        [Header("Splashes")]
        [Tooltip("Throw up droplets when a body hits the water fast enough.")]
        public bool splashes = true;

        [Tooltip("Slowest impact (m/s) that throws up a splash.")]
        [Min(0f)] public float splashMinSpeed = 1.5f;

        [Tooltip("Colour of the droplets.")]
        public Color splashColor = new Color(0.95f, 0.98f, 1f, 0.9f);

        [Header("Look")]
        [Tooltip("Draw the water. Turn off to draw your own.")]
        public bool showWater = true;

        [Tooltip("Colour and opacity of the water.")]
        public Color waterColor = new Color(0.22f, 0.52f, 0.82f, 0.55f);

        [Tooltip("Sorting order of the drawn water (higher draws in front of sprites, so bodies look under water).")]
        public int sortingOrder = 10;

        [Tooltip("Optional material for the water. Empty uses a built-in unlit look.")]
        public Material waterMaterial;

        [Tooltip("Optional material for the droplets. Empty uses a built-in soft dot.")]
        public Material effectsMaterial;

        internal static readonly List<BoundedBuoyancyWater2D> active = new List<BoundedBuoyancyWater2D>();

        /// <summary>Every enabled Bounded Buoyancy Water 2D.</summary>
        public static IReadOnlyList<BoundedBuoyancyWater2D> Active { get { return active; } }

        readonly WaterVolume2D core = new WaterVolume2D();
        internal PhysicsScene2D PhysicsScene;

        MeshFilter fillFilter;
        MeshRenderer fillRenderer;
        Mesh fillMesh;
        LineRenderer outline;
        Vector3[] fillVerts;
        Color32[] fillColors;
        Vector3[] outlinePoints;
        int fillSteps = -1;
        Vector2 fillSize;
        bool fillStill;
        Color fillDrawnColor;
        ParticleSystem splashSystem;

        /// <summary>Wave clock in seconds, advanced with the physics step.</summary>
        public float WaveTime { get { return (float)core.Time; } set { core.Time = value; } }

        public WaterVolume2D Core { get { return core; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { active.Clear(); }

        void OnEnable()
        {
            if (!active.Contains(this))
                active.Add(this);
            PhysicsScene = gameObject.scene.GetPhysicsScene2D();
            Sync();
        }

        void OnDisable()
        {
            active.Remove(this);
            if (fillRenderer != null)
            {
                fillRenderer.enabled = false;
                outline.enabled = false;
            }
        }

        void OnValidate()
        {
            size = Vector2.Max(size, new Vector2(0.01f, 0.01f));
            waveLength = Mathf.Max(waveLength, 0.05f);
            if (isActiveAndEnabled)
                Sync();
        }

        void FixedUpdate()
        {
            if (Bridge.FixedStep2D)
                Advance(Time.fixedDeltaTime);
        }

        /// <summary>Copy the settings and advance the wave clock by one physics step.</summary>
        internal void Advance(float dt)
        {
            Sync();
            core.Time += dt;
        }

        /// <summary>Copy the position and settings to the core (done every physics step).</summary>
        public void Sync()
        {
            core.Position = Bridge.V((Vector2)transform.position);
            core.Size = Bridge.V(size);
            core.Density = density;
            core.Current = Bridge.V(current);
            core.SetWaves(waveHeight, waveLength, -Physics2D.gravity.y);
        }

        /// <summary>Height (y) of the surface above the position x.</summary>
        public float HeightAt(float x) { return (float)core.HeightAt(x); }

        public bool Contains(Vector2 p) { return core.Contains(Bridge.V(p)); }

        public float MaxWave { get { return (float)core.MaxWave; } }

        /// <summary>Throw up a splash at `at` for a body `across` metres wide hitting the water at `speed` m/s.</summary>
        public void Splash(Vector2 at, float speed, float across)
        {
            if (!Application.isPlaying || !isActiveAndEnabled || !splashes || speed < splashMinSpeed)
                return;
            across = Mathf.Clamp(across, 0.02f, 20f);
            if (splashSystem == null)
            {
                GameObject go = Visuals.Child(transform, "Splashes");
                splashSystem = go.AddComponent<ParticleSystem>();
                ParticleSystem.MainModule main = splashSystem.main;
                main.playOnAwake = false;
                main.loop = true;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 1000;
                main.gravityModifier = 1f;
                main.gravitySource = ParticleSystemGravitySource.Physics2D;
                ParticleSystem.EmissionModule emission = splashSystem.emission;
                emission.enabled = false;
                ParticleSystem.ShapeModule shape = splashSystem.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 30f;
                shape.radiusThickness = 1f;
                shape.rotation = new Vector3(-90f, 0f, 0f);
                var renderer = go.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            if (!splashSystem.isPlaying)
                splashSystem.Play();
            var r = splashSystem.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = effectsMaterial != null ? effectsMaterial : Visuals.Effects;
            r.sortingOrder = sortingOrder + 1;
            float s = Mathf.Min(speed, 15f);
            ParticleSystem.MainModule m = splashSystem.main;
            m.startLifetime = Mathf.Clamp(0.25f * speed, 0.5f, 1.4f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.25f * s, 0.5f * s);
            m.startSize = new ParticleSystem.MinMaxCurve(0.05f + 0.04f * across, 0.1f + 0.08f * across);
            m.startColor = splashColor;
            ParticleSystem.ShapeModule sh = splashSystem.shape;
            sh.radius = 0.4f * across;
            var emit = new ParticleSystem.EmitParams
            {
                position = new Vector3(at.x, at.y + 0.02f, transform.position.z),
                applyShapeToPosition = true,
            };
            splashSystem.Emit(emit, Mathf.Clamp((int)(8f * speed * Mathf.Sqrt(2f * across)), 12, 80));
        }

        void Update()
        {
            if (!showWater)
            {
                if (fillRenderer != null)
                {
                    fillRenderer.enabled = false;
                    outline.enabled = false;
                }
                return;
            }
            if (fillFilter == null)
            {
                GameObject go = Visuals.Child(transform, "Water");
                fillFilter = go.AddComponent<MeshFilter>();
                fillRenderer = go.AddComponent<MeshRenderer>();
                fillRenderer.shadowCastingMode = ShadowCastingMode.Off;
                fillRenderer.receiveShadows = false;
                fillMesh = new Mesh { name = "Bounded Buoyancy Water 2D" };
                fillMesh.MarkDynamic();
                fillFilter.sharedMesh = fillMesh;
                GameObject line = Visuals.Child(transform, "Surface Line");
                outline = line.AddComponent<LineRenderer>();
                outline.useWorldSpace = false;
                outline.alignment = LineAlignment.View;
                outline.widthMultiplier = 0.04f;
                outline.shadowCastingMode = ShadowCastingMode.Off;
                outline.receiveShadows = false;
            }
            fillRenderer.enabled = true;
            outline.enabled = true;
            Material mat = waterMaterial != null ? waterMaterial : Visuals.Surface;
            fillRenderer.sharedMaterial = mat;
            fillRenderer.sortingOrder = sortingOrder;
            outline.sharedMaterial = Visuals.Lines;
            outline.sortingOrder = sortingOrder;
            Color dark = new Color(waterColor.r * 0.75f, waterColor.g * 0.75f, waterColor.b * 0.75f, Mathf.Min(1f, waterColor.a + 0.25f));
            outline.startColor = dark;
            outline.endColor = dark;
            int steps = waveHeight > 0f ? Mathf.Clamp((int)(size.x / 0.08f), 2, 1000) : 1;
            if (steps != fillSteps || size != fillSize)
                Build(steps);
            else if (fillStill && waveHeight <= 0f && waterColor == fillDrawnColor)
                return;
            Refresh();
        }

        void Build(int steps)
        {
            fillSteps = steps;
            fillSize = size;
            fillVerts = new Vector3[2 * (steps + 1)];
            fillColors = new Color32[fillVerts.Length];
            outlinePoints = new Vector3[steps + 1];
            var tris = new int[steps * 6];
            for (int i = 0; i < steps; i++)
            {
                int a = 2 * i;   // top at a, bottom at a + 1
                tris[6 * i] = a; tris[6 * i + 1] = a + 2; tris[6 * i + 2] = a + 1;
                tris[6 * i + 3] = a + 2; tris[6 * i + 4] = a + 3; tris[6 * i + 5] = a + 1;
            }
            fillMesh.Clear();
            fillMesh.vertices = fillVerts;
            fillMesh.triangles = tris;
            outline.positionCount = steps + 1;
            fillStill = false;
        }

        void Refresh()
        {
            WaveSet2D waves = core.Waves;
            double t = core.Time;
            Color32 top = waterColor;
            Color32 bottom = new Color(waterColor.r * 0.8f, waterColor.g * 0.8f, waterColor.b * 0.8f, Mathf.Min(1f, waterColor.a + 0.15f));
            for (int i = 0; i <= fillSteps; i++)
            {
                float x = -0.5f * size.x + size.x * i / fillSteps;
                float y = (float)waves.Offset(x, t);
                fillVerts[2 * i] = new Vector3(x, y, 0f);
                fillVerts[2 * i + 1] = new Vector3(x, -size.y, 0f);
                fillColors[2 * i] = top;
                fillColors[2 * i + 1] = bottom;
                outlinePoints[i] = new Vector3(x, y, -0.001f);
            }
            fillMesh.vertices = fillVerts;
            fillMesh.colors32 = fillColors;
            outline.SetPositions(outlinePoints);
            fillStill = waveHeight <= 0f;
            fillDrawnColor = waterColor;
        }

        void OnDestroy()
        {
            if (fillMesh != null)
                Destroy(fillMesh);
        }

        void OnDrawGizmos()
        {
            Vector3 c = transform.position;
            Gizmos.color = new Color(0.2f, 0.5f, 0.9f, 0.7f);
            Gizmos.DrawWireCube(c + new Vector3(0f, -0.5f * size.y, 0f), new Vector3(size.x, size.y, 0f));
            if (!Application.isPlaying || !showWater)
            {
                Gizmos.color = new Color(waterColor.r, waterColor.g, waterColor.b, 0.2f);
                Gizmos.DrawCube(c + new Vector3(0f, -0.5f * size.y, 0f), new Vector3(size.x, size.y, 0f));
            }
        }
    }
}
