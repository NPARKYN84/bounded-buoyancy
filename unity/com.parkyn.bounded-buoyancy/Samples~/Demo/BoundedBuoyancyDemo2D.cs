// Bounded Buoyancy for Unity: 2D demo (side view, metres, y up). Add this component to an empty GameObject in an
// empty scene and press Play (or use Tools > Bounded Buoyancy > Play 2D Demo). Everything is built in code.
// Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System.Collections.Generic;
using UnityEngine;

namespace BoundedBuoyancy.Samples
{
    public class BoundedBuoyancyDemo2D : MonoBehaviour
    {
        Transform root;
        Material material;
        BoundedBuoyancyWater2D tank;
        BoundedBuoyancyWind2D wind;
        readonly List<BoundedBuoyancyBody2D> bodies = new List<BoundedBuoyancyBody2D>();
        readonly System.Random rnd = new System.Random(3);
        GUIStyle captionStyle;

        void Start()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            cam.orthographic = true;
            cam.orthographicSize = 6f;
            cam.transform.position = new Vector3(0f, 0.8f, -10f);
            cam.transform.rotation = Quaternion.identity;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.78f, 0.88f, 0.97f);
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                Debug.LogWarning("Bounded Buoyancy 2D demo: no built-in sprite shader found, so the bodies are not drawn.");
                enabled = false;
                return;
            }
            material = new Material(shader);
            Build();
        }

        void Build()
        {
            if (root != null)
                Destroy(root.gameObject);
            root = new GameObject("Demo 2D").transform;
            bodies.Clear();

            var tgo = new GameObject("Tank");
            tgo.transform.SetParent(root, false);
            tank = tgo.AddComponent<BoundedBuoyancyWater2D>();
            tank.size = new Vector2(18f, 4f);
            tank.waveHeight = 0.12f;
            tank.waveLength = 5f;
            Wall(new Vector2(0f, -4.25f), new Vector2(19f, 0.5f));
            Wall(new Vector2(-9.25f, -1.5f), new Vector2(0.5f, 6f));
            Wall(new Vector2(9.25f, -1.5f), new Vector2(0.5f, 6f));

            var wgo = new GameObject("Wind");
            wgo.transform.SetParent(root, false);
            wind = wgo.AddComponent<BoundedBuoyancyWind2D>();
            wind.velocity = new Vector2(2.5f, 0f);
            wind.gustStrength = 0.4f;

            float[] densities = { 250f, 450f, 650f, 850f };
            for (int i = 0; i < densities.Length; i++)
                Crate(new Vector2(0.8f, 0.8f), densities[i], new Vector2(-7f + 1.3f * i, 1.5f + 0.4f * i), Shade(i), 15f * i);
            Plank();
            Boat();
            for (int i = 0; i < 3; i++)
                Ball(0.35f, 300f + 200f * i, new Vector2(4.5f + 1.1f * i, 2f + 0.5f * i), Shade(i + 3));
            for (int i = 0; i < 3; i++)
            {
                BoundedBuoyancyBody2D b = Ball(0.3f, 0.5f + 0.15f * i, new Vector2(-2f + 1.2f * i, 0.8f), new Color(0.95f, 0.35f + 0.2f * i, 0.4f));
                b.airDensity = 1.2f;
                b.airHalfHeight = 4f;
                b.angularDrag = 1f;
            }
        }

        void Wall(Vector2 at, Vector2 size)
        {
            var go = new GameObject("Wall");
            go.transform.SetParent(root, false);
            go.transform.position = at;
            go.AddComponent<BoxCollider2D>().size = size;
            Draw(go, Box(size), new Color(0.5f, 0.45f, 0.38f), -1);
        }

        BoundedBuoyancyBody2D Floater(GameObject go, float density)
        {
            var rb = go.AddComponent<Rigidbody2D>();
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            var b = go.AddComponent<BoundedBuoyancyBody2D>();
            b.Density = density;
            b.thickness = 0.5f;
            bodies.Add(b);
            return b;
        }

        GameObject Make(string name, Vector2 at, float angle)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = at;
            go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            return go;
        }

        BoundedBuoyancyBody2D Crate(Vector2 size, float density, Vector2 at, Color color, float angle = 0f)
        {
            GameObject go = Make("Crate", at, angle);
            go.AddComponent<BoxCollider2D>().size = size;
            Draw(go, Box(size), color, 0);
            return Floater(go, density);
        }

        BoundedBuoyancyBody2D Ball(float r, float density, Vector2 at, Color color)
        {
            GameObject go = Make("Ball", at, 0f);
            go.AddComponent<CircleCollider2D>().radius = r;
            var pts = new Vector2[32];
            for (int i = 0; i < 32; i++)
                pts[i] = r * new Vector2(Mathf.Cos(2f * Mathf.PI * i / 32f), Mathf.Sin(2f * Mathf.PI * i / 32f));
            Draw(go, pts, color, 0);
            // A spoke, so that rolling shows.
            Draw(go, new[] { new Vector2(-0.06f * r, 0f), new Vector2(0.06f * r, 0f), new Vector2(0.06f * r, 0.9f * r), new Vector2(-0.06f * r, 0.9f * r) }, color * 0.7f, 1);
            return Floater(go, density);
        }

        void Plank()
        {
            BoundedBuoyancyBody2D p = Crate(new Vector2(2.4f, 0.3f), 500f, new Vector2(-1f, 2.5f), new Color(0.75f, 0.6f, 0.4f), 25f);
            p.addedMassCoefficient = 1.5f;   // a flat plate moving face-on takes more water along
        }

        void Boat()
        {
            GameObject go = Make("Boat", new Vector2(2.5f, 1.2f), 35f);
            var hull = new[] { new Vector2(-1.1f, 0.25f), new Vector2(1.1f, 0.25f), new Vector2(0.75f, -0.25f), new Vector2(-0.75f, -0.25f) };
            go.AddComponent<PolygonCollider2D>().SetPath(0, hull);
            Draw(go, hull, new Color(0.88f, 0.88f, 0.92f), 0);
            Draw(go, new[] { new Vector2(-0.03f, 0.25f), new Vector2(0.03f, 0.25f), new Vector2(0.03f, 1.9f), new Vector2(-0.03f, 1.9f) }, new Color(0.4f, 0.3f, 0.2f), 1);
            Draw(go, new[] { new Vector2(0.08f, 0.4f), new Vector2(0.9f, 0.4f), new Vector2(0.08f, 1.8f) }, new Color(0.98f, 0.98f, 0.95f), 1);
            BoundedBuoyancyBody2D b = Floater(go, 300f);
            b.centreOfMassOffset = new Vector2(0f, -0.15f);   // ballast
            b.sailSize = 1.4f;
            b.sailCentre = new Vector2(0.35f, 0.9f);
        }

        static Vector2[] Box(Vector2 size)
        {
            Vector2 h = 0.5f * size;
            return new[] { new Vector2(-h.x, -h.y), new Vector2(h.x, -h.y), new Vector2(h.x, h.y), new Vector2(-h.x, h.y) };
        }

        void Draw(GameObject go, Vector2[] outline, Color color, int order)
        {
            var child = new GameObject("Look");
            child.transform.SetParent(go.transform, false);
            var verts = new Vector3[outline.Length];
            var cols = new Color32[outline.Length];
            for (int i = 0; i < outline.Length; i++)
            {
                verts[i] = outline[i];
                cols[i] = color;
            }
            var tris = new List<int>();
            for (int i = 1; i + 1 < outline.Length; i++)
            {
                tris.Add(0);
                tris.Add(i + 1);
                tris.Add(i);
            }
            var mesh = new Mesh();
            mesh.vertices = verts;
            mesh.colors32 = cols;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateBounds();
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = child.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.sortingOrder = order;
        }

        static Color Shade(int i)
        {
            Color[] palette =
            {
                new Color(0.93f, 0.42f, 0.36f), new Color(0.98f, 0.73f, 0.3f), new Color(0.55f, 0.78f, 0.42f),
                new Color(0.36f, 0.64f, 0.9f), new Color(0.66f, 0.5f, 0.86f), new Color(0.9f, 0.9f, 0.86f),
            };
            return palette[i % palette.Length];
        }

        float Rand(float lo, float hi) { return lo + (float)rnd.NextDouble() * (hi - lo); }

        void Update()
        {
            // Bodies blown off the right come back on the left.
            foreach (BoundedBuoyancyBody2D b in bodies)
            {
                if (b == null)
                    continue;
                Vector2 p = b.Body.position;
                if (p.x > 9.5f && p.y > 0.5f)
                    b.Body.position = new Vector2(-8.5f, p.y);
            }
        }

        void OnGUI()
        {
            if (captionStyle == null)
                captionStyle = new GUIStyle(GUI.skin.box) { wordWrap = true, alignment = TextAnchor.UpperLeft, fontSize = 14 };
            GUILayout.BeginArea(new Rect(10f, 10f, 180f, 400f));
            GUILayout.Label("Bounded Buoyancy 2D");
            if (GUILayout.Button("Drop a crate"))
                Crate(new Vector2(0.7f, 0.7f), Rand(250f, 900f), new Vector2(Rand(-6f, 6f), 5f), Shade(rnd.Next(6)), Rand(-40f, 40f));
            if (GUILayout.Button("Drop a ball"))
                Ball(0.3f, Rand(200f, 800f), new Vector2(Rand(-6f, 6f), 5f), Shade(rnd.Next(6)));
            if (GUILayout.Button("Drop a stone"))
                Ball(0.25f, 2600f, new Vector2(Rand(-6f, 6f), 5f), new Color(0.45f, 0.45f, 0.48f));
            tank.waveHeight = GUILayout.Toggle(tank.waveHeight > 0f, " Waves") ? 0.12f : 0f;
            wind.enabled = GUILayout.Toggle(wind.enabled, " Wind");
            if (GUILayout.Button("Start again"))
                Build();
            GUILayout.EndArea();
            GUI.Box(new Rect(200f, Screen.height - 80f, Mathf.Min(900f, Screen.width - 210f), 70f),
                "Crates float as deep as their density says, the plank lies flat, the boat with a low centre of mass rights itself and its sail drives it, and the balloons rise until the thinning air holds them. Every splash is thrown up where a body hits the water.", captionStyle);
        }
    }
}
