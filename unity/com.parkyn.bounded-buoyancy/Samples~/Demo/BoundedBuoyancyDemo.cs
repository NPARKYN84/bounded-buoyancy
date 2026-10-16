// Bounded Buoyancy for Unity: 3D demo. Add this component to an empty GameObject in an empty scene and press
// Play (or use Tools > Bounded Buoyancy > Play 3D Demo). Everything is built in code, in any render pipeline.
// Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System.Collections.Generic;
using UnityEngine;

namespace BoundedBuoyancy.Samples
{
    public partial class BoundedBuoyancyDemo : MonoBehaviour
    {
        enum Scene { Release, Float, Tilt, Waves, Sky, Many, River, Wind, Boats, Fishing, Submarine, Battle }

        static readonly string[] Captions =
        {
            "RELEASE: light balls let go 2 m under water. Left row: the usual model, which shoots them out of the water. Right row: Bounded Buoyancy, which caps the rise at g/C, so they come up briskly and settle.",
            "FLOAT: crates of different densities, a ball, a log and a stone. Each floats as deep as its density says; the stone sinks. Drop more in.",
            "TILT: buoyancy acts at the centre of the part under water. A raft dropped at an angle settles flat, a tall block lies down, and a boat with a low centre of mass comes back upright.",
            "WAVES: two travelling sine waves with deep-water speeds. Bodies rise, fall and rock with them.",
            "SKY: the same law with air's density. Helium balloons rise until the thinning air holds them; the hot-air balloon lifts off when you light the burner (its density changes while running).",
            "MANY: 300 bodies of mixed shapes on waves, every one cut at the waterline every step.",
            "RIVER: the water flows, slower along the banks. Bodies drift with it by drag on their speed through the water.",
            "WIND: gusty wind over a lake. Boats with sails run before it and heel; balloons drift with it; crates barely move.",
            "BOATS: drive the orange motorboat (W / S throttle, A / D rudder, or the sliders): a keel, a rudder and a propeller. The dinghy, with a centreboard, a rudder and a sail that trims itself, beats up into the wind in tacks on its own, then runs back.",
            "FISHING: float fishing from a jetty. The angler casts; fish come to the bait and nibble, each nibble dipping the float, then one takes it. He strikes, the fish runs and tires against the reel's drag (the line's slip tension), and he reels it in and lands it. The line is real 0.25 mm nylon that stretches and breaks.",
            "SUBMARINE: real sea water, brackish at the top over cold salty water from 25 m down. Flooded, the submarine sinks until the water round it is as dense as it is and rests there on the salt layer; the trim tank moves it up or down a little; blown, it surfaces. The yellow floats each hang where the water is as dense as they are.",
            "BATTLE: a pirate brig against a navy brig under sail, five 12-pounders a side. Shot goes through the oak sides and holes the compartments behind; the sea comes in at the holes at and below the waterline, the ship settles and heels, and with 35 t of water in her she strikes her colours.",
        };

        Scene scene = Scene.Release;
        Transform root;
        Camera cam;
        Material baseMaterial;
        readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        readonly List<BoundedBuoyancyBody> bodies = new List<BoundedBuoyancyBody>();
        readonly List<Transform> sails = new List<Transform>();
        BoundedBuoyancyBody hotAir;
        float burner;
        Vector3 focus;
        float distance = 12f, pitch = 25f, yaw = -30f;
        bool orbit = true;
        float wrapMin = float.NegativeInfinity, wrapMax = float.PositiveInfinity;
        Vector3 wrapAxis = Vector3.right;
        System.Random rnd = new System.Random(7);
        // BOATS scene
        BoundedBuoyancyBody motorboat, dinghy;
        BoundedBuoyancyFoil boatRudder, dinghyRudder, dinghySail;
        BoundedBuoyancyThruster boatProp;
        float throttle, helm;
        bool keyW, keyS, keyA, keyD, followDinghy;
        int leg;
        float legTime;
        int pendingScene = -1;   // scenes change in Update, not in the middle of drawing the GUI

        void Start()
        {
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 500f;
            if (FindAnyLight() == null)
            {
                var light = new GameObject("Sun").AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
            GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseMaterial = probe.GetComponent<Renderer>().sharedMaterial;
            Destroy(probe);
            Load(Scene.Release);
        }

        static Light FindAnyLight()
        {
            return Object.FindAnyObjectByType<Light>();
        }

        void Load(Scene s)
        {
            scene = s;
            if (root != null)
                Destroy(root.gameObject);
            // Splinters from the BATTLE scene's planking are made outside the scene's root.
            foreach (BoundedBuoyancyBody b in Object.FindObjectsByType<BoundedBuoyancyBody>(FindObjectsSortMode.None))
                if (b.name == "Splinter")
                    Destroy(b.gameObject);
            root = new GameObject("Demo " + s).transform;
            bodies.Clear();
            sails.Clear();
            hotAir = null;
            burner = 0f;
            motorboat = null;
            dinghy = null;
            throttle = 0f;
            helm = 0f;
            leg = 0;
            legTime = 0f;
            keyW = keyS = keyA = keyD = false;
            wrapMin = float.NegativeInfinity;
            wrapMax = float.PositiveInfinity;
            orbit = true;
            ResetSea();
            switch (s)
            {
                case Scene.Release: BuildRelease(); break;
                case Scene.Float: BuildFloat(); break;
                case Scene.Tilt: BuildTilt(); break;
                case Scene.Waves: BuildWaves(); break;
                case Scene.Sky: BuildSky(); break;
                case Scene.Many: BuildMany(); break;
                case Scene.River: BuildRiver(); break;
                case Scene.Wind: BuildWind(); break;
                case Scene.Boats: BuildBoats(); break;
                case Scene.Fishing: BuildFishing(); break;
                case Scene.Submarine: BuildSubmarine(); break;
                case Scene.Battle: BuildBattle(); break;
            }
        }

        // Scenes ---------------------------------------------------------------------------------------------------

        void BuildRelease()
        {
            Pool(Vector3.zero, new Vector3(9f, 3f, 4f));
            float[] densities = { 30f, 80f, 250f, 500f, 800f };
            for (int i = 0; i < densities.Length; i++)
            {
                Ball(0.2f, densities[i], new Vector3(-3.6f + 0.7f * i, -2f, 0f), Shade(i), BoundedBuoyancyBody.Model.Usual);
                Ball(0.2f, densities[i], new Vector3(0.8f + 0.7f * i, -2f, 0f), Shade(i));
            }
            Frame(new Vector3(0f, -0.3f, 0f), 10f, 12f, 0f);
            orbit = false;
        }

        void BuildFloat()
        {
            Pool(Vector3.zero, new Vector3(8f, 3f, 8f));
            float[] densities = { 200f, 400f, 600f, 800f, 950f };
            for (int i = 0; i < densities.Length; i++)
                Crate(new Vector3(0.6f, 0.6f, 0.6f), densities[i], new Vector3(-2.4f + 1.2f * i, 1.5f + 0.3f * i, -1.5f), Shade(i));
            Ball(0.35f, 300f, new Vector3(-1.5f, 2.5f, 1.5f), new Color(0.95f, 0.45f, 0.2f));
            Log(0.2f, 2f, 600f, new Vector3(1.2f, 2f, 1.5f), new Color(0.55f, 0.36f, 0.2f));
            Ball(0.2f, 2600f, new Vector3(0f, 3f, 0f), new Color(0.45f, 0.45f, 0.48f));
            Frame(Vector3.zero, 12f, 28f, -30f);
        }

        void BuildTilt()
        {
            Pool(Vector3.zero, new Vector3(10f, 3f, 6f));
            Crate(new Vector3(1.6f, 0.35f, 1.1f), 400f, new Vector3(-3f, 1f, 0f), new Color(0.75f, 0.6f, 0.4f), 30f);
            Crate(new Vector3(0.3f, 1.2f, 0.3f), 600f, new Vector3(0f, 0.4f, 0f), new Color(0.4f, 0.65f, 0.85f), 3f);
            Boat(new Vector3(3f, 0.6f, 0f), 60f, false);
            Frame(Vector3.zero, 12f, 22f, -20f);
        }

        void BuildWaves()
        {
            BoundedBuoyancyWater sea = Pool(Vector3.zero, new Vector3(40f, 6f, 40f));
            sea.waveHeight = 0.35f;
            sea.waveLength = 7f;
            sea.waveDirection = 20f;
            Boat(new Vector3(-3f, 0.3f, -2f), 0f, false);
            Boat(new Vector3(3f, 0.3f, 2f), 0f, false);
            for (int i = 0; i < 6; i++)
                Crate(new Vector3(0.6f, 0.6f, 0.6f), 350f + 80f * i, new Vector3(-5f + 2f * i, 0.5f, 4f), Shade(i));
            for (int i = 0; i < 5; i++)
                Ball(0.3f, 300f, new Vector3(-4f + 2f * i, 0.5f, -5f), Shade(i + 2));
            Log(0.25f, 3f, 550f, new Vector3(0f, 0.5f, 0f), new Color(0.55f, 0.36f, 0.2f));
            Frame(Vector3.zero, 16f, 22f, -30f);
        }

        void BuildSky()
        {
            // The ground, and a raised tank: once a scene has water, bodies float only inside it.
            Ground(new Vector3(0f, 0f, 0f), 60f);
            Pool(new Vector3(9f, 1.2f, 0f), new Vector3(4f, 1.2f, 4f), false);
            Crate(new Vector3(0.6f, 0.6f, 0.6f), 400f, new Vector3(9f, 2.5f, 0f), new Color(0.75f, 0.6f, 0.4f));
            Color[] colors = { new Color(0.95f, 0.3f, 0.35f), new Color(0.3f, 0.6f, 0.95f), new Color(0.95f, 0.8f, 0.25f), new Color(0.5f, 0.85f, 0.45f), new Color(0.85f, 0.45f, 0.9f) };
            for (int i = 0; i < 5; i++)
            {
                // Helium balloons of slightly different fill: each rises to where the air is as dense as it is.
                BoundedBuoyancyBody b = Ball(0.3f, 0.35f + 0.12f * i, new Vector3(-4f + 1.4f * i, 0.5f, -2f), colors[i]);
                b.AirDensity = 1.2f;
                b.AirHalfHeight = 8f;
            }
            // A hot-air balloon: envelope and basket, centre of mass low in the basket.
            GameObject env = Shape(PrimitiveType.Sphere, new Vector3(0f, 2.2f, 3f), Vector3.one * 3f, new Color(0.95f, 0.55f, 0.2f));
            GameObject basket = Shape(PrimitiveType.Cube, env.transform.position + new Vector3(0f, -2.1f, 0f), new Vector3(0.6f, 0.5f, 0.6f), new Color(0.5f, 0.35f, 0.2f));
            DropCollider(basket);
            basket.transform.SetParent(env.transform, true);
            hotAir = Floater(env, 1.3f);
            hotAir.AirDensity = 1.2f;
            hotAir.AirHalfHeight = 8f;
            hotAir.angularDrag = 2f;
            hotAir.centreOfMassOffset = new Vector3(0f, -1.6f, 0f);
            Ball(0.25f, 2600f, new Vector3(-2f, 9f, 2f), new Color(0.45f, 0.45f, 0.48f)).AirDensity = 1.2f;
            Frame(new Vector3(0f, 4f, 0f), 18f, 12f, -20f);
        }

        void BuildMany()
        {
            BoundedBuoyancyWater sea = Pool(Vector3.zero, new Vector3(40f, 6f, 40f));
            sea.waveHeight = 0.3f;
            sea.waveLength = 6f;
            sea.splashes = false;
            sea.ripples = false;
            for (int i = 0; i < 300; i++)
            {
                var p = new Vector3(Rand(-17f, 17f), Rand(0.5f, 4f), Rand(-17f, 17f));
                int k = i % 3;
                if (k == 0)
                    Crate(new Vector3(0.5f, 0.4f, 0.7f), Rand(250f, 800f), p, Shade(i), Rand(-30f, 30f));
                else if (k == 1)
                    Ball(0.25f, Rand(200f, 800f), p, Shade(i));
                else
                    Log(0.15f, 1.2f, Rand(300f, 800f), p, Shade(i));
            }
            Frame(Vector3.zero, 30f, 35f, -30f);
        }

        void BuildRiver()
        {
            BoundedBuoyancyWater river = Pool(Vector3.zero, new Vector3(60f, 3f, 12f));
            river.current = new Vector3(2f, 0f, 0f);
            river.currentEdge = 3f;
            for (int i = 0; i < 12; i++)
            {
                var p = new Vector3(Rand(-28f, 10f), 0.3f, Rand(-5f, 5f));
                if (i % 3 == 0)
                    Log(0.2f, 2f, 550f, p, new Color(0.55f, 0.36f, 0.2f));
                else if (i % 3 == 1)
                    Crate(new Vector3(0.6f, 0.5f, 0.6f), 450f, p, Shade(i));
                else
                    Ball(0.3f, 400f, p, Shade(i));
            }
            wrapAxis = Vector3.right;
            wrapMin = -29f;
            wrapMax = 29f;
            Frame(new Vector3(0f, 0f, 0f), 26f, 40f, -10f);
        }

        void BuildWind()
        {
            Pool(Vector3.zero, new Vector3(50f, 4f, 30f));
            var wind = new GameObject("Wind").AddComponent<BoundedBuoyancyWind>();
            wind.transform.SetParent(root, false);
            wind.velocity = new Vector3(6f, 0f, 0f);
            wind.gustStrength = 0.35f;
            wind.gustPeriod = 6f;
            wind.directionSwing = 12f;
            for (int i = 0; i < 3; i++)
                Boat(new Vector3(-15f + 4f * i, 0.3f, -6f + 6f * i), 0f, true);
            for (int i = 0; i < 4; i++)
            {
                BoundedBuoyancyBody b = Ball(0.3f, 0.7f, new Vector3(-14f + 3f * i, 4f, 8f), Shade(i + 1));
                b.AirDensity = 1.2f;
                b.AirHalfHeight = 8f;
            }
            for (int i = 0; i < 3; i++)
                Crate(new Vector3(0.6f, 0.6f, 0.6f), 500f, new Vector3(-12f + 4f * i, 0.4f, 3f), Shade(i));
            wrapAxis = Vector3.right;
            wrapMin = -24f;
            wrapMax = 24f;
            Frame(new Vector3(-4f, 1f, 0f), 24f, 25f, -20f);
        }

        void BuildBoats()
        {
            BoundedBuoyancyWater lake = Pool(Vector3.zero, new Vector3(400f, 4f, 400f));
            lake.waveHeight = 0.05f;
            lake.waveLength = 3f;
            lake.waveDirection = 80f;
            var wind = new GameObject("Wind").AddComponent<BoundedBuoyancyWind>();
            wind.transform.SetParent(root, false);
            wind.velocity = new Vector3(0f, 0f, -5f);   // from +z: the dinghy beats towards +z
            wind.gustStrength = 0.2f;
            wind.gustPeriod = 7f;
            wind.directionSwing = 6f;
            BuildMotorboat(new Vector3(-4f, 0.1f, -8f));
            BuildDinghy(new Vector3(8f, 0.1f, -2f), 45f);
            Ball(0.35f, 300f, new Vector3(-14f, 0.3f, 40f), new Color(0.95f, 0.85f, 0.2f));
            Ball(0.35f, 300f, new Vector3(14f, 0.3f, 40f), new Color(0.95f, 0.85f, 0.2f));
            Ball(0.35f, 300f, new Vector3(0f, 0.3f, 80f), new Color(0.95f, 0.85f, 0.2f));
            orbit = false;
            Frame(motorboat.transform.position, 12f, 20f, 0f);
        }

        /// <summary>A hull for a boat, from the convex hull of its points (bow towards +z), with a convex mesh collider.</summary>
        GameObject HullObject(string name, Vector3[] pts, Vector3 at, float yaw, Color color)
        {
            var hullPoints = new List<Core.V3>();
            foreach (Vector3 p in pts)
                hullPoints.Add(new Core.V3(p.x, p.y, p.z));
            List<Core.V3> faces = Core.ConvexHull.Build(hullPoints);
            var verts = new Vector3[faces.Count];
            var tris = new int[faces.Count];
            for (int i = 0; i < faces.Count; i++)
            {
                verts[i] = new Vector3((float)faces[i].x, (float)faces[i].y, (float)faces[i].z);
                tris[i] = i;
            }
            var mesh = new Mesh { name = name };
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = at;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Paint(color);
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex = true;
            return go;
        }

        /// <summary>A foil on a boat, with a thin blade to show it: span along Y, chord along Z (aft is -Z).</summary>
        BoundedBuoyancyFoil BoatFoil(BoundedBuoyancyBody b, Vector3 at, float area, float ar, Core.Medium medium, float effort, Color color, float thickness)
        {
            var go = new GameObject(medium == Core.Medium.Air ? "Sail" : "Foil");
            go.transform.SetParent(b.transform, false);
            go.transform.localPosition = at;
            var f = go.AddComponent<BoundedBuoyancyFoil>();
            f.medium = medium;
            f.area = area;
            f.aspectRatio = ar;
            f.effortOffset = effort;
            float span = f.SpanLength, chord = f.ChordLength;
            GameObject blade = Shape(PrimitiveType.Cube, Vector3.zero, new Vector3(thickness, span, chord), color);
            DropCollider(blade);
            blade.transform.SetParent(go.transform, false);
            blade.transform.localPosition = new Vector3(0f, 0f, effort > 0f ? -0.5f * chord : 0f);
            blade.transform.localRotation = Quaternion.identity;
            return f;
        }

        void BuildMotorboat(Vector3 at)
        {
            var pts = new[]
            {
                new Vector3(-0.45f, 0.2f, 1.1f), new Vector3(0.45f, 0.2f, 1.1f), new Vector3(-0.3f, -0.2f, 0.9f), new Vector3(0.3f, -0.2f, 0.9f),
                new Vector3(-0.45f, 0.2f, -1.2f), new Vector3(0.45f, 0.2f, -1.2f), new Vector3(-0.3f, -0.2f, -1.1f), new Vector3(0.3f, -0.2f, -1.1f),
                new Vector3(0f, 0.2f, 1.6f), new Vector3(0f, -0.1f, 1.4f),
            };
            BoundedBuoyancyBody b = Floater(HullObject("Motorboat", pts, at, 0f, new Color(0.93f, 0.5f, 0.15f)), 250f);
            b.centreOfMassOffset = new Vector3(0f, -0.12f, 0f);
            b.dragCoefficient = 0.2f;              // a streamlined hull, not a sphere
            b.keepMomentumOnEntry = false;         // a hull dipping in waves is not plunging in
            Color dark = new Color(0.25f, 0.27f, 0.3f);
            BoatFoil(b, new Vector3(0f, -0.4f, 0f), 0.15f, 1.5f, Core.Medium.Water, 0f, dark, 0.03f);
            boatRudder = BoatFoil(b, new Vector3(0f, -0.3f, -1.15f), 0.06f, 2f, Core.Medium.Water, 0.04f, dark, 0.03f);
            var prop = new GameObject("Propeller");
            prop.transform.SetParent(b.transform, false);
            prop.transform.localPosition = new Vector3(0f, -0.3f, -1.1f);
            boatProp = prop.AddComponent<BoundedBuoyancyThruster>();
            boatProp.maxThrust = 400f;
            boatProp.maxSpeed = 5f;
            boatProp.diameter = 0.25f;
            motorboat = b;
        }

        void BuildDinghy(Vector3 at, float yaw)
        {
            var pts = new[]
            {
                new Vector3(-0.7f, 0.25f, 1.4f), new Vector3(0.7f, 0.25f, 1.4f), new Vector3(-0.45f, -0.15f, 1.2f), new Vector3(0.45f, -0.15f, 1.2f),
                new Vector3(-0.7f, 0.25f, -1.7f), new Vector3(0.7f, 0.25f, -1.7f), new Vector3(-0.45f, -0.15f, -1.6f), new Vector3(0.45f, -0.15f, -1.6f),
                new Vector3(0f, 0.25f, 1.9f), new Vector3(0f, -0.05f, 1.7f),
            };
            BoundedBuoyancyBody b = Floater(HullObject("Dinghy", pts, at, yaw, new Color(0.92f, 0.92f, 0.9f)), 160f);
            b.centreOfMassOffset = new Vector3(0f, -0.45f, 0f);   // a heavy centreboard and the crew sitting out
            b.dragCoefficient = 0.12f;
            b.keepMomentumOnEntry = false;
            b.AirDensity = 1.2f;
            Color dark = new Color(0.25f, 0.27f, 0.3f);
            BoatFoil(b, new Vector3(0f, -0.6f, 0.1f), 0.5f, 2f, Core.Medium.Water, 0f, dark, 0.025f);
            dinghyRudder = BoatFoil(b, new Vector3(0f, -0.35f, -1.65f), 0.12f, 2.5f, Core.Medium.Water, 0.06f, dark, 0.025f);
            dinghySail = BoatFoil(b, new Vector3(0f, 2.8f, 0.6f), 8f, 2.5f, Core.Medium.Air, 0.6f, new Color(0.97f, 0.96f, 0.92f), 0.015f);
            dinghySail.autoTrim = true;
            GameObject mast = Shape(PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.08f, 2.7f, 0.08f), new Color(0.4f, 0.42f, 0.45f));
            DropCollider(mast);
            mast.transform.SetParent(b.transform, false);
            mast.transform.localPosition = new Vector3(0f, 2.9f, 0.62f);
            mast.transform.localRotation = Quaternion.identity;
            dinghy = b;
        }

        /// <summary>The dinghy sails a course by itself: up into the wind in tacks 45 degrees off it, through the wind every
        /// 25 s (or near the side of the lake), until it is well upwind; then it runs back before the wind.</summary>
        void SteerDinghy(float dt)
        {
            legTime += dt;
            Vector3 p = dinghy.Body.position;
            if (leg == 2)
            {
                if (p.z < -15f)
                {
                    leg = p.x < 0f ? 0 : 1;
                    legTime = 0f;
                }
            }
            else if (p.z > 70f)
            {
                leg = 2;
                legTime = 0f;
            }
            else if (legTime > 25f || (leg == 0 && p.x > 40f) || (leg == 1 && p.x < -40f))
            {
                leg = 1 - leg;
                legTime = 0f;
            }
            float off = leg == 2 ? 180f : 45f;
            float side = leg == 0 ? 1f : -1f;
            var course = new Vector3(side * Mathf.Sin(off * Mathf.Deg2Rad), 0f, Mathf.Cos(off * Mathf.Deg2Rad));
            float error = Vector3.SignedAngle(dinghy.transform.forward, course, Vector3.up);
            dinghyRudder.Angle = Mathf.Clamp(1.5f * error - 25f * dinghy.Body.angularVelocity.y * Mathf.Rad2Deg / 60f, -30f, 30f);
        }

        void FixedUpdate()
        {
            SeaStep(Time.fixedDeltaTime);
            if (scene != Scene.Boats || motorboat == null)
                return;
            if (keyW)
                throttle = Mathf.Min(throttle + 0.6f * Time.fixedDeltaTime, 1f);
            if (keyS)
                throttle = Mathf.Max(throttle - 0.6f * Time.fixedDeltaTime, -0.5f);
            if (keyA || keyD)
                helm = Mathf.MoveTowards(helm, keyA ? -30f : 30f, 90f * Time.fixedDeltaTime);
            boatProp.throttle = throttle;
            boatRudder.Angle = helm;
            SteerDinghy(Time.fixedDeltaTime);
            // A boat that runs off the edge of the lake comes back near the middle, heading the same way.
            foreach (BoundedBuoyancyBody b in new[] { motorboat, dinghy })
            {
                Vector3 p = b.Body.position;
                if (Mathf.Abs(p.x) > 170f || Mathf.Abs(p.z) > 170f)
                {
                    Vector3 f = b.transform.forward;
                    f.y = 0f;
                    b.Body.position = new Vector3(0f, 0.1f, 0f) - f.normalized * 60f;
                }
            }
        }

        // Building blocks ------------------------------------------------------------------------------------------

        BoundedBuoyancyWater Pool(Vector3 surface, Vector3 size, bool floor = true)
        {
            var go = new GameObject("Water");
            go.transform.SetParent(root, false);
            go.transform.position = surface;
            var w = go.AddComponent<BoundedBuoyancyWater>();
            w.size = size;
            if (floor)
                Ground(surface - new Vector3(0f, size.y, 0f), Mathf.Max(size.x, size.z) + 4f);
            return w;
        }

        void Ground(Vector3 at, float width)
        {
            GameObject g = Shape(PrimitiveType.Cube, at - new Vector3(0f, 0.25f, 0f), new Vector3(width, 0.5f, width), new Color(0.76f, 0.7f, 0.55f));
            g.name = "Ground";
        }

        GameObject Shape(PrimitiveType type, Vector3 at, Vector3 scale, Color color, float roll = 0f)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(root, false);
            go.transform.position = at;
            go.transform.rotation = Quaternion.Euler(0f, 0f, roll);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Paint(color);
            return go;
        }

        static void DropCollider(GameObject go)
        {
            Collider c = go.GetComponent<Collider>();
            if (c != null)
            {
                c.enabled = false;
                Destroy(c);
            }
        }

        BoundedBuoyancyBody Floater(GameObject go, float density, BoundedBuoyancyBody.Model model = BoundedBuoyancyBody.Model.Bounded)
        {
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var b = go.AddComponent<BoundedBuoyancyBody>();
            b.Density = density;
            b.model = model;
            bodies.Add(b);
            return b;
        }

        BoundedBuoyancyBody Crate(Vector3 size, float density, Vector3 at, Color color, float roll = 0f)
        {
            return Floater(Shape(PrimitiveType.Cube, at, size, color, roll), density);
        }

        BoundedBuoyancyBody Ball(float radius, float density, Vector3 at, Color color, BoundedBuoyancyBody.Model model = BoundedBuoyancyBody.Model.Bounded)
        {
            return Floater(Shape(PrimitiveType.Sphere, at, Vector3.one * (2f * radius), color), density, model);
        }

        BoundedBuoyancyBody Log(float radius, float length, float density, Vector3 at, Color color)
        {
            // Unity's capsule is 2 high and 1 across, along Y: lay it along X.
            BoundedBuoyancyBody b = Floater(Shape(PrimitiveType.Capsule, at, new Vector3(2f * radius, 0.5f * length, 2f * radius), color, 90f), density);
            b.addedMassCoefficient = 1f;   // a long cylinder moving side-on
            return b;
        }

        void Boat(Vector3 at, float roll, bool withSail)
        {
            // A small hull, 1.8 m long, as a convex mesh.
            var pts = new[]
            {
                new Vector3(-0.9f, 0.25f, -0.5f), new Vector3(0.9f, 0.25f, -0.5f), new Vector3(-0.7f, -0.25f, -0.3f), new Vector3(0.7f, -0.25f, -0.3f),
                new Vector3(-0.9f, 0.25f, 0.5f), new Vector3(0.9f, 0.25f, 0.5f), new Vector3(-0.7f, -0.25f, 0.3f), new Vector3(0.7f, -0.25f, 0.3f),
                new Vector3(1.25f, 0.25f, 0f), new Vector3(1.0f, -0.2f, 0f),
            };
            // Its faces: the convex hull of the points, wound to face outwards (flat-shaded).
            var hullPoints = new List<Core.V3>();
            foreach (Vector3 p in pts)
                hullPoints.Add(new Core.V3(p.x, p.y, p.z));
            List<Core.V3> faces = Core.ConvexHull.Build(hullPoints);
            var verts = new Vector3[faces.Count];
            var tris = new int[faces.Count];
            for (int i = 0; i < faces.Count; i++)
            {
                verts[i] = new Vector3((float)faces[i].x, (float)faces[i].y, (float)faces[i].z);
                tris[i] = i;
            }
            var mesh = new Mesh { name = "Hull" };
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("Boat");
            go.transform.SetParent(root, false);
            go.transform.position = at;
            go.transform.rotation = Quaternion.Euler(0f, 0f, roll);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Paint(new Color(0.85f, 0.85f, 0.9f));
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex = true;
            BoundedBuoyancyBody b = Floater(go, 280f);
            b.centreOfMassOffset = new Vector3(0f, -0.15f, 0f);   // ballast
            b.AirDensity = 1.2f;
            if (withSail)
            {
                GameObject mast = Shape(PrimitiveType.Cube, at + new Vector3(0f, 1.2f, 0f), new Vector3(0.06f, 2f, 0.06f), new Color(0.4f, 0.3f, 0.2f));
                DropCollider(mast);
                mast.transform.SetParent(go.transform, true);
                var pivot = new GameObject("Sail");
                pivot.transform.SetParent(go.transform, false);
                pivot.transform.localPosition = new Vector3(0f, 1.2f, 0f);
                GameObject cloth = Shape(PrimitiveType.Cube, Vector3.zero, new Vector3(0.02f, 1.6f, 1.1f), new Color(0.97f, 0.97f, 0.95f));
                DropCollider(cloth);
                cloth.transform.SetParent(pivot.transform, false);
                cloth.transform.localPosition = Vector3.zero;
                b.sailArea = 1.6f;
                b.sailCentre = new Vector3(0f, 1.2f, 0f);
                sails.Add(pivot.transform);
            }
        }

        Material Paint(Color c)
        {
            Material m;
            if (!materials.TryGetValue(c, out m))
            {
                m = new Material(baseMaterial) { color = c };
                materials[c] = m;
            }
            return m;
        }

        static Color Shade(int i)
        {
            Color[] palette =
            {
                new Color(0.93f, 0.42f, 0.36f), new Color(0.98f, 0.73f, 0.3f), new Color(0.55f, 0.78f, 0.42f),
                new Color(0.36f, 0.64f, 0.9f), new Color(0.66f, 0.5f, 0.86f), new Color(0.9f, 0.9f, 0.86f),
            };
            return palette[((i % palette.Length) + palette.Length) % palette.Length];
        }

        float Rand(float lo, float hi) { return lo + (float)rnd.NextDouble() * (hi - lo); }

        void Frame(Vector3 target, float dist, float pitchDegrees, float yawDegrees)
        {
            focus = target;
            distance = dist;
            pitch = pitchDegrees;
            yaw = yawDegrees;
        }

        // Running --------------------------------------------------------------------------------------------------

        void OnApplicationFocus(bool focused)
        {
            if (!focused)
                keyW = keyS = keyA = keyD = false;
        }

        void Update()
        {
            if (pendingScene >= 0)
            {
                Load((Scene)pendingScene);
                pendingScene = -1;
            }
            if (scene == Scene.Boats && motorboat != null)
            {
                // Follow the motorboat (or the dinghy) from behind.
                BoundedBuoyancyBody target = followDinghy ? dinghy : motorboat;
                Vector3 f = target.transform.forward;
                float heading = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                focus = Vector3.Lerp(focus, target.transform.position + Vector3.up, 1f - Mathf.Exp(-4f * Time.deltaTime));
                yaw = Mathf.LerpAngle(yaw, heading, 1f - Mathf.Exp(-2f * Time.deltaTime));
                distance = followDinghy ? 16f : 12f;
            }
            if (!SeaCamera())
            {
                if (orbit)
                    yaw += 4f * Time.deltaTime;
                Quaternion q = Quaternion.Euler(pitch, yaw, 0f);
                cam.transform.position = focus - q * Vector3.forward * distance;
                cam.transform.rotation = q;
            }

            // Sails turn to face the wind they feel, like a sail let out to run before the wind.
            foreach (Transform sail in sails)
            {
                if (sail == null)
                    continue;
                var body = sail.GetComponentInParent<BoundedBuoyancyBody>();
                Vector3 felt = body.WindHere - Velocity(body.Body);
                Vector3 local = body.transform.InverseTransformDirection(felt);
                if (new Vector2(local.x, local.z).sqrMagnitude > 1e-4f)
                    sail.localRotation = Quaternion.Euler(0f, Mathf.Atan2(-local.z, local.x) * Mathf.Rad2Deg, 0f);
            }

            if (hotAir != null)
                hotAir.Density = Mathf.Lerp(1.3f, 0.9f, burner);   // heating the air in the envelope

            // Bodies carried off the end come back upstream (or upwind).
            foreach (BoundedBuoyancyBody b in bodies)
            {
                if (b == null)
                    continue;
                float s = Vector3.Dot(b.Body.position, wrapAxis);
                if (s > wrapMax)
                    b.Body.position += wrapAxis * (wrapMin - s);
            }
        }

        static Vector3 Velocity(Rigidbody rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }

        GUIStyle captionStyle;

        void OnGUI()
        {
            if (captionStyle == null)
                captionStyle = new GUIStyle(GUI.skin.box) { wordWrap = true, alignment = TextAnchor.UpperLeft, fontSize = 14 };
            GUILayout.BeginArea(new Rect(10f, 10f, 190f, Screen.height - 20f));
            GUILayout.Label("Bounded Buoyancy");
            string[] names = System.Enum.GetNames(typeof(Scene));
            for (int i = 0; i < names.Length; i++)
                if (GUILayout.Button((i + 1) + "  " + names[i]))
                    pendingScene = i;
            GUILayout.Space(8f);
            if (!eyeSet)
                orbit = GUILayout.Toggle(orbit, " Turn the camera");
            SeaGUI();
            if (scene == Scene.Release && GUILayout.Button("Release again"))
                pendingScene = (int)Scene.Release;
            if ((scene == Scene.Float || scene == Scene.Waves || scene == Scene.Tilt) && GUILayout.Button("Drop a crate"))
                Crate(new Vector3(0.6f, 0.6f, 0.6f), Rand(250f, 900f), focus + new Vector3(Rand(-1.5f, 1.5f), 4f, Rand(-1.5f, 1.5f)), Shade(rnd.Next(6)), Rand(-40f, 40f));
            if ((scene == Scene.Float || scene == Scene.Waves) && GUILayout.Button("Drop a stone"))
                Ball(0.2f, 2600f, focus + new Vector3(Rand(-1.5f, 1.5f), 5f, Rand(-1.5f, 1.5f)), new Color(0.45f, 0.45f, 0.48f));
            if (scene == Scene.Sky)
            {
                GUILayout.Label("Burner: " + Mathf.RoundToInt(100f * burner) + "%");
                burner = GUILayout.HorizontalSlider(burner, 0f, 1f);
            }
            if (scene == Scene.Many)
                GUILayout.Label(bodies.Count + " bodies, " + Mathf.RoundToInt(1f / Mathf.Max(Time.smoothDeltaTime, 1e-4f)) + " fps");
            if (scene == Scene.Boats && motorboat != null)
            {
                // W / S and A / D through the GUI's own key events, which work with either input system.
                Event e = Event.current;
                if (e.type == EventType.KeyDown || e.type == EventType.KeyUp)
                {
                    bool down = e.type == EventType.KeyDown;
                    if (e.keyCode == KeyCode.W) keyW = down;
                    if (e.keyCode == KeyCode.S) keyS = down;
                    if (e.keyCode == KeyCode.A) keyA = down;
                    if (e.keyCode == KeyCode.D) keyD = down;
                }
                GUILayout.Label("Throttle " + Mathf.RoundToInt(100f * throttle) + "%");
                throttle = GUILayout.HorizontalSlider(throttle, -0.5f, 1f);
                GUILayout.Label("Rudder " + Mathf.RoundToInt(helm) + "°");
                helm = GUILayout.HorizontalSlider(helm, -30f, 30f);
                if (GUILayout.Button("Centre the rudder"))
                    helm = 0f;
                Vector3 v = Velocity(motorboat.Body);
                GUILayout.Label("Motorboat " + (new Vector2(v.x, v.z).magnitude * 1.943844f).ToString("0.0") + " knots");
                Vector3 dv = Velocity(dinghy.Body);
                GUILayout.Label("Dinghy " + (new Vector2(dv.x, dv.z).magnitude * 1.943844f).ToString("0.0") + " knots, sail out "
                    + Mathf.RoundToInt(Mathf.Abs(dinghySail.Angle)) + "°, " + (leg == 2 ? "running" : "beating"));
                followDinghy = GUILayout.Toggle(followDinghy, " Follow the dinghy");
            }
            GUILayout.EndArea();
            GUI.Box(new Rect(210f, Screen.height - 80f, Mathf.Min(900f, Screen.width - 220f), 70f), Captions[(int)scene], captionStyle);
        }
    }
}
