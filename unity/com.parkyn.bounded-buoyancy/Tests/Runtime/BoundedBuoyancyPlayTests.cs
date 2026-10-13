// Play-mode checks of Bounded Buoyancy in the real Unity physics engine: the same benchmarks the Godot plugin
// and the engine-free core pass (B1-B13). Run them from Window > General > Test Runner > PlayMode.
// (In your project's Packages/manifest.json, add "testables": ["com.parkyn.bounded-buoyancy"] to list them.)
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BoundedBuoyancy.Tests
{
    public class BoundedBuoyancyPlayTests
    {
        const float G = 9.80665f;
        const float R = 0.02f;
        const float R_TT = 0.0805721899f;   // table-tennis ball, 2.7 g, 40 mm
        const float Dt = 1f / 60f;

        readonly List<GameObject> made = new List<GameObject>();
        Vector3 savedGravity;
        Vector2 savedGravity2D;
        float savedFixed, savedScale, savedMaxDelta;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            savedGravity = Physics.gravity;
            savedGravity2D = Physics2D.gravity;
            savedFixed = Time.fixedDeltaTime;
            savedScale = Time.timeScale;
            savedMaxDelta = Time.maximumDeltaTime;
            Physics.gravity = new Vector3(0f, -G, 0f);
            Physics2D.gravity = new Vector2(0f, -G);
            Time.fixedDeltaTime = Dt;
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject go in made)
                if (go != null)
                    Object.Destroy(go);
            made.Clear();
            Physics.gravity = savedGravity;
            Physics2D.gravity = savedGravity2D;
            Time.fixedDeltaTime = savedFixed;
            Time.timeScale = savedScale;
            Time.maximumDeltaTime = savedMaxDelta;
            yield return null;
        }

        // Long runs go faster than real time; each physics step is still 1/60 s.
        void Fast()
        {
            Time.maximumDeltaTime = 1f;
            Time.timeScale = 20f;
        }

        GameObject Make(string name, Vector3 at, float rollDegrees = 0f)
        {
            var go = new GameObject(name);
            go.transform.position = at;
            go.transform.rotation = Quaternion.Euler(0f, 0f, rollDegrees);
            made.Add(go);
            return go;
        }

        BoundedBuoyancyBody Float(GameObject go, float density, float air = 0f, BoundedBuoyancyBody.Model model = BoundedBuoyancyBody.Model.Bounded)
        {
            go.AddComponent<Rigidbody>();
            var b = go.AddComponent<BoundedBuoyancyBody>();
            b.Density = density;
            b.airDensity = air;
            b.model = model;
            return b;
        }

        BoundedBuoyancyBody Ball(float radius, float density, Vector3 at, float air = 0f, BoundedBuoyancyBody.Model model = BoundedBuoyancyBody.Model.Bounded)
        {
            GameObject go = Make("Ball", at);
            go.AddComponent<SphereCollider>().radius = radius;
            return Float(go, density, air, model);
        }

        BoundedBuoyancyBody Box(Vector3 size, float density, Vector3 at, float roll = 0f, float air = 0f)
        {
            GameObject go = Make("Box", at, roll);
            go.AddComponent<BoxCollider>().size = size;
            return Float(go, density, air);
        }

        BoundedBuoyancyBody Hull(Vector3[] points, float density, Vector3 at, float roll, Vector3 comOffset)
        {
            GameObject go = Make("Hull", at, roll);
            var mesh = new Mesh();
            mesh.vertices = points;
            // Any triangles will do: a convex mesh collider uses the hull of the points.
            var tris = new List<int>();
            for (int i = 1; i + 1 < points.Length; i++)
            {
                tris.Add(0);
                tris.Add(i);
                tris.Add(i + 1);
            }
            mesh.triangles = tris.ToArray();
            var mc = go.AddComponent<MeshCollider>();
            mc.convex = true;
            mc.sharedMesh = mesh;
            var b = Float(go, density);
            b.centreOfMassOffset = comOffset;
            return b;
        }

        BoundedBuoyancyWater Water(Vector3 at, Vector3 size, float density = 1000f)
        {
            GameObject go = Make("Water", at);
            var w = go.AddComponent<BoundedBuoyancyWater>();
            w.size = size;
            w.density = density;
            w.Sync();
            return w;
        }

        static float Tilt(Component b)
        {
            return Vector3.Angle(b.transform.rotation * Vector3.up, Vector3.up);
        }

        static IEnumerator Steps(int n)
        {
            for (int i = 0; i < n; i++)
                yield return new WaitForFixedUpdate();
        }

        // B1 -------------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator B1_FirstFrameAcceleration()
        {
            float[] ratios = { 0.001225f, 0.03f, R_TT, 0.25f, 0.917f, 2.7f };
            var usual = new List<BoundedBuoyancyBody>();
            var bounded = new List<BoundedBuoyancyBody>();
            for (int i = 0; i < ratios.Length; i++)
            {
                usual.Add(Ball(R, ratios[i] * 1000f, new Vector3(i * 0.2f, -0.5f, 0f), 0f, BoundedBuoyancyBody.Model.Usual));
                bounded.Add(Ball(R, ratios[i] * 1000f, new Vector3(i * 0.2f + 0.1f, -0.5f, 0f)));
            }
            BoundedBuoyancyBody box = Box(new Vector3(0.1f, 0.1f, 0.1f), 500f, new Vector3(5f, -0.5f, 0f));
            yield return new WaitForFixedUpdate();
            for (int i = 0; i < ratios.Length; i++)
            {
                float r = ratios[i];
                float want = G * (1f - r) / r;
                Assert.AreEqual(want, usual[i].Body.Velocity().y / Dt, Mathf.Max(0.002f * Mathf.Abs(want), 0.01f), "usual, density ratio " + r);
                Assert.AreEqual(G * (1f - r) / (r + 0.5f), bounded[i].Body.Velocity().y / Dt, 0.01f, "bounded, density ratio " + r);
            }
            Assert.AreEqual(G * 0.5f, box.Body.Velocity().y / Dt, 0.01f, "box (upright cube), bounded");
            Assert.AreEqual(0.5f, box.OwnMass, 1e-5f, "box's own mass, density x volume");
        }

        // B2 -------------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator B2_ReleaseAndSurfacing()
        {
            float[] ratios = { 0.03f, R_TT, 0.25f, 0.5f };
            float[] wantTime = { 0.517f, 0.533f, 0.617f, 0.783f };
            float[] wantPeak = { 6.7f, 6.1f, 4.9f, 3.3f };
            var balls = new List<BoundedBuoyancyBody>();
            for (int i = 0; i < ratios.Length; i++)
                balls.Add(Ball(R, ratios[i] * 1000f, new Vector3(i * 0.2f, -0.5f, 0f)));
            var surfaced = new float[4] { -1f, -1f, -1f, -1f };
            var peak = new float[4] { -1e9f, -1e9f, -1e9f, -1e9f };
            for (int step = 1; step <= 180; step++)
            {
                yield return new WaitForFixedUpdate();
                for (int i = 0; i < 4; i++)
                {
                    float y = balls[i].Body.position.y;
                    if (surfaced[i] < 0f && y >= 0f)
                        surfaced[i] = step * Dt;
                    peak[i] = Mathf.Max(peak[i], y);
                }
            }
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(wantTime[i], surfaced[i], 0.001f, "time to surface, density ratio " + ratios[i]);
                Assert.AreEqual(wantPeak[i], 100f * peak[i], 0.25f, "peak height (cm), density ratio " + ratios[i]);
            }
        }

        // B5: colliders become the right shapes -------------------------------------------------------------------

        [UnityTest]
        public IEnumerator B5_CollidersMakeTheRightShapes()
        {
            GameObject cap = Make("Capsule", new Vector3(0f, -2f, 0f));
            var cc = cap.AddComponent<CapsuleCollider>();
            cc.radius = 0.05f;
            cc.height = 0.3f;
            BoundedBuoyancyBody capsule = Float(cap, 500f);

            GameObject scaled = Make("Scaled box", new Vector3(2f, -2f, 0f));
            scaled.transform.localScale = new Vector3(2f, 1f, 3f);
            scaled.AddComponent<BoxCollider>().size = new Vector3(0.1f, 0.1f, 0.1f);
            BoundedBuoyancyBody box = Float(scaled, 500f);

            GameObject compound = Make("Compound", new Vector3(4f, -2f, 0f));
            var child = new GameObject("Child sphere");
            child.transform.SetParent(compound.transform, false);
            child.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            child.AddComponent<SphereCollider>().radius = 0.1f;
            compound.AddComponent<BoxCollider>().size = new Vector3(0.2f, 0.2f, 0.2f);
            BoundedBuoyancyBody both = Float(compound, 500f);

            BoundedBuoyancyBody wedge = Hull(new[] { new Vector3(-0.1f, 0, -0.1f), new Vector3(0.1f, 0, -0.1f), new Vector3(0, 0.1f, -0.1f),
                new Vector3(-0.1f, 0, 0.1f), new Vector3(0.1f, 0, 0.1f), new Vector3(0, 0.1f, 0.1f) }, 500f, new Vector3(6f, -2f, 0f), 0f, Vector3.zero);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(Mathf.PI * 0.0025f * 0.2f + 4f / 3f * Mathf.PI * 0.000125f, capsule.Volume, 1e-8f, "capsule volume");
            Assert.AreEqual(0.2f * 0.1f * 0.3f, box.Volume, 1e-8f, "box scaled 2 x 1 x 3");
            Assert.AreEqual(4f / 3f * Mathf.PI * 0.001f + 0.008f, both.Volume, 1e-7f, "box with a child sphere");
            Assert.AreEqual(0.002f, wedge.Volume, 1e-8f, "convex mesh collider (a wedge)");
            Assert.AreEqual(500f * both.Volume, both.OwnMass, 1e-4f, "own mass");
            Vector3 expectedCom = new Vector3(0f, 0.3f * (4f / 3f * Mathf.PI * 0.001f) / both.Volume, 0f);
            Assert.AreEqual(expectedCom.y, both.Body.centerOfMass.y, 1e-5f, "centre of mass at the centre of the volume");
            Assert.AreEqual(1f, capsule.Body.Velocity().y / Dt / (G * 0.5f / 1f), 0.01f, "fully under water, half density: g/2 up");
        }

        // B5: floating and righting ------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator B5_FloatingAndRighting()
        {
            Fast();
            BoundedBuoyancyBody level = Box(new Vector3(0.2f, 0.2f, 0.2f), 500f, Vector3.zero);
            BoundedBuoyancyBody raft = Box(new Vector3(0.6f, 0.15f, 0.4f), 400f, new Vector3(2f, 0f, 0f), 30f);
            BoundedBuoyancyBody tall = Box(new Vector3(0.1f, 0.4f, 0.1f), 600f, new Vector3(4f, 0.04f, 0f), 2f);
            BoundedBuoyancyBody boat = Hull(new[] { new Vector3(-0.25f, 0.06f, -0.3f), new Vector3(0.25f, 0.06f, -0.3f), new Vector3(-0.12f, -0.06f, -0.3f), new Vector3(0.12f, -0.06f, -0.3f),
                new Vector3(-0.25f, 0.06f, 0.3f), new Vector3(0.25f, 0.06f, 0.3f), new Vector3(-0.12f, -0.06f, 0.3f), new Vector3(0.12f, -0.06f, 0.3f) },
                300f, new Vector3(6f, 0f, 0f), 60f, new Vector3(0f, -0.05f, 0f));
            yield return Steps(480);
            Assert.AreEqual(0f, 1000f * Mathf.Abs(level.Body.position.y), 1f, "cube at half density stays level: drift (mm)");
            Assert.AreEqual(0f, Tilt(raft), 2f, "raft dropped at 30 deg: tilt after 8 s");
            Assert.AreEqual(90f, Tilt(tall), 5f, "tall block, upright: tilt after 8 s");
            Assert.AreEqual(0f, Tilt(boat), 3f, "boat with low centre of mass, from 60 deg");
        }

        // B6 -------------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator B6_WaterVolumes()
        {
            Fast();
            Water(Vector3.zero, new Vector3(4f, 2f, 4f));
            Water(new Vector3(20f, 0f, 0f), new Vector3(4f, 2f, 4f), 1025f);
            BoundedBuoyancyBody tt = Ball(R, R_TT * 1000f, new Vector3(0f, -0.5f, 0f));
            BoundedBuoyancyBody dry = Ball(R, R_TT * 1000f, new Vector3(30f, -0.5f, 0f));
            BoundedBuoyancyBody salt = Box(new Vector3(0.2f, 0.2f, 0.2f), 900f, new Vector3(20f, -0.07f, 0f));
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(-G, dry.Body.Velocity().y / Dt, 0.01f, "ball outside any water falls at g");
            float surfaced = tt.Body.position.y >= 0f ? Dt : -1f;
            for (int step = 2; step <= 600; step++)
            {
                yield return new WaitForFixedUpdate();
                if (surfaced < 0f && tt.Body.position.y >= 0f)
                    surfaced = step * Dt;
            }
            Assert.AreEqual(0.533f, surfaced, 0.001f, "table-tennis ball in a water volume: time to surface");
            Assert.AreEqual(100f * 0.1f * (1f - 2f * 900f / 1025f), 100f * salt.Body.position.y, 0.15f, "cube floats at its sea-water level (cm)");
        }

        [UnityTest]
        public IEnumerator B6_Waves()
        {
            Fast();
            BoundedBuoyancyWater sea = Water(new Vector3(50f, 0f, 0f), new Vector3(20f, 3f, 20f));
            sea.waveHeight = 0.05f;
            sea.waveLength = 3f;
            sea.Sync();
            BoundedBuoyancyBody rider = Box(new Vector3(0.6f, 0.15f, 0.4f), 400f, new Vector3(50f, 0f, 0f));
            float lo = 1e9f, hi = -1e9f, tilt = 0f;
            for (int step = 1; step <= 600; step++)
            {
                yield return new WaitForFixedUpdate();
                if (step * Dt > 3f)
                {
                    lo = Mathf.Min(lo, rider.Body.position.y);
                    hi = Mathf.Max(hi, rider.Body.position.y);
                    tilt = Mathf.Max(tilt, Tilt(rider));
                }
            }
            Assert.AreEqual(10f, 100f * (hi - lo), 6f, "raft on 5 cm waves: rises and falls (cm)");
            Assert.AreEqual(8f, tilt, 7.5f, "raft on 5 cm waves: rocks but stays upright (deg)");
        }

        // B7 -------------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator B7_Air()
        {
            Water(Vector3.zero, new Vector3(4f, 2f, 4f));
            BoundedBuoyancyBody balU = Ball(0.1f, 0.45f, new Vector3(100f, 5f, 0f), 1.2f, BoundedBuoyancyBody.Model.Usual);
            BoundedBuoyancyBody balB = Ball(0.1f, 0.45f, new Vector3(102f, 5f, 0f), 1.2f);
            BoundedBuoyancyBody stone = Ball(0.1f, 2700f, new Vector3(104f, 5f, 0f), 1.2f);
            BoundedBuoyancyBody half = Box(new Vector3(0.1f, 0.1f, 0.1f), 300f, new Vector3(1.5f, 0f, 1.5f), 0f, 1.2f);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(G * (1.2f - 0.45f) / 0.45f, balU.Body.Velocity().y / Dt, 0.02f, "helium balloon in air, usual");
            Assert.AreEqual(G * (1.2f - 0.45f) / (0.45f + 0.6f), balB.Body.Velocity().y / Dt, 0.002f, "helium balloon in air, bounded");
            Assert.AreEqual(G * (2700f - 1.2f) / (2700f + 0.6f), -stone.Body.Velocity().y / Dt, 0.002f, "stone in air: just under g");
            Assert.AreEqual(G * (0.5006f - 0.3f) / (0.3f + 0.5f * 0.5006f), half.Body.Velocity().y / Dt, 0.002f, "cube half in water, half in air");
        }

        [UnityTest]
        public IEnumerator B7_DensityChangedWhileRunning()
        {
            // Set up (held still), then made lighter: ballast tanks blown, or a balloon's air heated.
            BoundedBuoyancyBody ballast = Box(new Vector3(0.1f, 0.1f, 0.1f), 2000f, new Vector3(-1.5f, -0.5f, -1.5f));
            ballast.Body.isKinematic = true;
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(2f, ballast.OwnMass, 1e-5f, "own mass before the change");
            ballast.Density = 500f;
            ballast.Body.isKinematic = false;
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(G * 0.5f, ballast.Body.Velocity().y / Dt, 0.002f, "density changed while running: acceleration");
            Assert.AreEqual(0.5f, ballast.OwnMass, 1e-5f, "density changed while running: own mass");
            Assert.AreEqual(0.5f * 0.02f / 12f, ballast.Body.inertiaTensor.x, 1e-7f, "density changed while running: inertia");
        }

        [UnityTest]
        public IEnumerator B7_BalloonFindsItsLevel()
        {
            Fast();
            BoundedBuoyancyBody high = Ball(0.1f, 0.6f, new Vector3(106f, 2f, 0f), 1.2f);
            high.airHalfHeight = 10f;
            BoundedBuoyancyBody hot = Ball(1f, 1f, new Vector3(110f, 10f * Mathf.Log(1.2f) / Mathf.Log(2f), 0f), 1.2f);
            hot.transform.rotation = Quaternion.Euler(0f, 0f, 30f);
            hot.airHalfHeight = 10f;
            hot.centreOfMassOffset = new Vector3(0f, -0.6f, 0f);
            yield return Steps(60 * 120);
            Assert.AreEqual(10f, high.Body.position.y, 0.3f, "balloon (0.6) in air halving every 10 m floats at 10 m");
            Assert.AreEqual(0f, Tilt(hot), 2f, "hot-air balloon tipped 30 deg, low centre of mass: rights");
        }

        // B8 -------------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator B8_FastBodies()
        {
            Fast();
            Water(new Vector3(200f, 0f, 0f), new Vector3(10f, 60f, 10f));
            BoundedBuoyancyBody falling = Ball(0.1f, 2700f, new Vector3(199f, 50f, 0f), 1.2f);
            BoundedBuoyancyBody fired = Ball(0.1f, 100f, new Vector3(201f, 0.5f, 0f));
            BoundedBuoyancyBody skimmer = Box(new Vector3(0.4f, 0.1f, 0.3f), 300f, new Vector3(198f, 0f, -4f));   // room to slow down inside the water
            fired.Body.SetVelocity(new Vector3(0f, -30f, 0f));
            skimmer.Body.SetVelocity(new Vector3(0f, 0f, 25f));
            float entry = 0f, worst = 0f;
            for (int step = 0; step < 1200; step++)
            {
                yield return new WaitForFixedUpdate();
                if (entry == 0f && falling.Body.position.y < 0.3f)
                    entry = -falling.Body.Velocity().y;
                worst = Mathf.Max(worst, Mathf.Max(falling.Body.Velocity().magnitude, Mathf.Max(fired.Body.Velocity().magnitude, skimmer.Body.Velocity().magnitude)));
            }
            float vol = 4f / 3f * Mathf.PI * 0.001f;
            float vStone = Mathf.Sqrt(2f * 1700f * vol * G / (1000f * 0.47f * Mathf.PI * 0.01f));
            float fAir = (2700f - 1.2f) * vol * G;
            float kAir = 0.5f * 1.2f * 0.47f * Mathf.PI * 0.01f;
            float vHit = Mathf.Sqrt(fAir / kAir * (1f - Mathf.Exp(-2f * kAir * 49.7f / ((2700f + 0.6f) * vol))));
            Assert.AreEqual(vHit, entry, 0.5f, "stone dropped 50 m hits the water at");
            Assert.AreEqual(vStone, -falling.Body.Velocity().y, 0.1f, "then sinks at its terminal speed in water");
            Assert.AreEqual(100f * (0.1f - 2f * 0.1f * 0.19580f), 100f * fired.Body.position.y, 1f, "light ball fired down at 30 m/s floats again (cm)");
            Assert.AreEqual(0f, Tilt(skimmer), 1f, "box skimming at 25 m/s: slowed, still level");
            Assert.AreEqual(vHit, worst, 0.5f, "fastest speed seen: the stone's, as it hits");
        }

        // B9 -------------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator B9_FlowingWater()
        {
            Fast();
            BoundedBuoyancyWater river = Water(new Vector3(300f, 0f, 0f), new Vector3(120f, 3f, 10f));
            river.current = new Vector3(1.5f, 0f, 0f);
            river.currentEdge = 2f;
            BoundedBuoyancyWater turned = Water(new Vector3(400f, 0f, 0f), new Vector3(120f, 3f, 10f));
            turned.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            turned.current = new Vector3(1.5f, 0f, 0f);
            BoundedBuoyancyBody mid = Ball(0.1f, 500f, new Vector3(242f, 0f, 0f));
            BoundedBuoyancyBody bank = Ball(0.1f, 500f, new Vector3(242f, 0f, 4.5f));
            BoundedBuoyancyBody other = Ball(0.1f, 500f, new Vector3(400f, 0f, 58f));
            BoundedBuoyancyBody drifter = Box(new Vector3(0.6f, 0.15f, 0.4f), 400f, new Vector3(242f, 0f, -1f));
            yield return Steps(3600);
            Assert.AreEqual(1.5f, mid.Body.Velocity().x, 0.02f, "ball mid-river drifts with the current");
            Assert.AreEqual(1.5f * 0.25f, bank.Body.Velocity().x, 0.02f, "ball 0.5 m from the bank drifts slower");
            Assert.AreEqual(-1.5f, other.Body.Velocity().z, 0.02f, "river turned a quarter turn flows along -z");
            Assert.AreEqual(1.5f, drifter.Body.Velocity().x, 0.1f, "raft drifts with the current");
            Assert.AreEqual(0f, Tilt(drifter), 1f, "raft stays level in the current");
        }

        // B10 ------------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator B10_Wind()
        {
            Fast();
            Water(new Vector3(560f, 0f, 0f), new Vector3(200f, 3f, 60f));
            GameObject wgo = Make("Wind", new Vector3(560f, 0f, 0f));
            var breeze = wgo.AddComponent<BoundedBuoyancyWind>();
            breeze.velocity = new Vector3(3f, 0f, 0f);
            breeze.gustStrength = 0f;
            breeze.directionSwing = 0f;
            breeze.everywhere = false;
            breeze.size = new Vector3(200f, 40f, 60f);
            breeze.Sync();
            BoundedBuoyancyBody drifting = Ball(0.1f, 0.6f, new Vector3(470f, 10f, 0f), 1.2f);
            drifting.airHalfHeight = 10f;
            BoundedBuoyancyBody still = Ball(0.1f, 0.6f, new Vector3(300f, 10f, 30f), 1.2f);
            still.airHalfHeight = 10f;
            BoundedBuoyancyBody crate = Box(new Vector3(0.4f, 0.4f, 0.4f), 500f, new Vector3(470f, 0f, 10f), 0f, 1.2f);
            BoundedBuoyancyBody sailer = Hull(new[] { new Vector3(-0.45f, 0.12f, -0.25f), new Vector3(0.45f, 0.12f, -0.25f), new Vector3(-0.35f, -0.12f, -0.15f), new Vector3(0.35f, -0.12f, -0.15f),
                new Vector3(-0.45f, 0.12f, 0.25f), new Vector3(0.45f, 0.12f, 0.25f), new Vector3(-0.35f, -0.12f, 0.15f), new Vector3(0.35f, -0.12f, 0.15f) },
                300f, new Vector3(470f, 0.05f, -10f), 0f, new Vector3(0f, -0.04f, 0f));
            sailer.sailArea = 0.5f;
            sailer.sailCentre = new Vector3(0f, 0.6f, 0f);
            sailer.airDensity = 1.2f;
            float heel = 0f;
            for (int step = 1; step <= 1800; step++)
            {
                yield return new WaitForFixedUpdate();
                if (step * Dt > 10f)
                    heel = Mathf.Max(heel, Tilt(sailer));
            }
            Assert.AreEqual(0.75f, sailer.Body.Velocity().x, 0.6f, "boat with a sail runs before the wind");
            Assert.AreEqual(1f, heel, 0.9f, "boat heels a little in a light wind, stays up");
            Assert.AreEqual(3f, drifting.Body.Velocity().x, 0.06f, "balloon in a 3 m/s wind drifts with it");
            Assert.AreEqual(0f, still.Body.Velocity().magnitude, 0.01f, "balloon outside the wind's box stays put");
            Assert.AreEqual(0.1f, crate.Body.Velocity().x, 0.1f, "crate with no sail barely moves");

            var gusty = Make("Gusts", new Vector3(0f, 500f, 0f)).AddComponent<BoundedBuoyancyWind>();
            gusty.velocity = new Vector3(5f, 0f, 0f);
            gusty.gustStrength = 0.3f;
            gusty.everywhere = false;
            gusty.Sync();
            float lo = 1e9f, hi = 0f;
            for (int k = 0; k < 6000; k++)
            {
                gusty.WindTime = k * 0.05f;
                float s = gusty.VelocityAt(new Vector3(0f, 500f, 0f)).magnitude;
                lo = Mathf.Min(lo, s);
                hi = Mathf.Max(hi, s);
            }
            Assert.AreEqual(3.5f, lo, 0.25f, "gusts: slowest wind");
            Assert.AreEqual(6.5f, hi, 0.25f, "gusts: strongest wind");
        }

        // B11 ------------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator B11_SplashesAndEvents()
        {
            Fast();
            BoundedBuoyancyWater pond = Water(new Vector3(700f, 0f, 0f), new Vector3(10f, 4f, 10f));
            BoundedBuoyancyBody dropped = Ball(0.1f, 800f, new Vector3(700f, 1.1f, 0f));
            BoundedBuoyancyBody popper = Ball(0.1f, 100f, new Vector3(702f, -1f, 0f));
            var entries = new List<float>();
            var points = new List<Vector3>();
            int exits = 0;
            dropped.waterEntered.AddListener((speed, at) => { entries.Add(speed); points.Add(at); });
            popper.waterExited.AddListener((speed, at) => exits++);
            popper.Body.SetVelocity(new Vector3(0f, 6f, 0f));
            bool splashed = false, ringed = false;
            for (int step = 0; step < 300; step++)
            {
                yield return new WaitForFixedUpdate();
                splashed |= pond.transform.Find("Splashes") != null;
                ringed |= pond.transform.Find("Ripple") != null;
            }
            Assert.AreEqual(1, entries.Count, "ball dropped 1 m: water entered once");
            Assert.AreEqual(Mathf.Sqrt(2f * G * 1f), entries.Count > 0 ? entries[0] : 0f, 0.2f, "impact speed reported");
            Assert.AreEqual(0f, points.Count > 0 ? 100f * points[0].y : 100f, 0.5f, "impact point on the surface (cm)");
            Assert.IsTrue(exits >= 1, "cork shot up: water exited reported");
            Assert.IsTrue(splashed, "splash thrown up");
            Assert.IsTrue(ringed, "rings drawn");
        }

        // B12 ------------------------------------------------------------------------------------------------------

        BoundedBuoyancyBody2D Float2D(GameObject go, float density, float air = 0f, BoundedBuoyancyBody.Model model = BoundedBuoyancyBody.Model.Bounded)
        {
            go.AddComponent<Rigidbody2D>();
            var b = go.AddComponent<BoundedBuoyancyBody2D>();
            b.Density = density;
            b.airDensity = air;
            b.model = model;
            return b;
        }

        BoundedBuoyancyBody2D Circle2D(float r, float density, Vector2 at, float air = 0f, BoundedBuoyancyBody.Model model = BoundedBuoyancyBody.Model.Bounded)
        {
            GameObject go = Make("Circle", at);
            go.AddComponent<CircleCollider2D>().radius = r;
            return Float2D(go, density, air, model);
        }

        BoundedBuoyancyBody2D Rect2D(Vector2 size, float density, Vector2 at)
        {
            GameObject go = Make("Rect", at);
            go.AddComponent<BoxCollider2D>().size = size;
            return Float2D(go, density);
        }

        BoundedBuoyancyWater2D Water2D(Vector2 at, Vector2 size)
        {
            GameObject go = Make("Water 2D", at);
            var w = go.AddComponent<BoundedBuoyancyWater2D>();
            w.size = size;
            w.Sync();
            return w;
        }

        [UnityTest]
        public IEnumerator B12_TwoD()
        {
            Fast();
            Water2D(Vector2.zero, new Vector2(40f, 30f));
            BoundedBuoyancyWater2D flume = Water2D(new Vector2(120f, 0f), new Vector2(120f, 10f));
            flume.current = new Vector2(1.5f, 0f);
            flume.Sync();
            BoundedBuoyancyWater2D swell = Water2D(new Vector2(300f, 0f), new Vector2(40f, 10f));
            swell.waveHeight = 0.2f;
            swell.waveLength = 6f;
            swell.Sync();
            BoundedBuoyancyBody2D cUsual = Circle2D(0.2f, 250f, new Vector2(-6f, -5f), 0f, BoundedBuoyancyBody.Model.Usual);
            BoundedBuoyancyBody2D cBound = Circle2D(0.2f, 250f, new Vector2(-5f, -5f));
            BoundedBuoyancyBody2D balloon = Circle2D(0.2f, 0.45f, new Vector2(50f, 30f), 1.2f);
            BoundedBuoyancyBody2D plank = Rect2D(new Vector2(2f, 0.5f), 500f, new Vector2(0f, 0.2f));
            GameObject boatGo = Make("Boat 2D", new Vector2(6f, 0.1f), -40f);
            boatGo.AddComponent<PolygonCollider2D>().SetPath(0, new[] { new Vector2(-0.6f, 0.15f), new Vector2(0.6f, 0.15f), new Vector2(0.4f, -0.15f), new Vector2(-0.4f, -0.15f) });
            BoundedBuoyancyBody2D boat = Float2D(boatGo, 300f);
            boat.centreOfMassOffset = new Vector2(0f, -0.12f);
            GameObject logGo = Make("Log 2D", new Vector2(-10f, 4f));
            var cap = logGo.AddComponent<CapsuleCollider2D>();
            cap.size = new Vector2(0.4f, 1.0f);
            cap.direction = CapsuleDirection2D.Vertical;
            BoundedBuoyancyBody2D log = Float2D(logGo, 600f);
            BoundedBuoyancyBody2D drift = Circle2D(0.2f, 500f, new Vector2(62f, 0f));
            BoundedBuoyancyBody2D drop = Circle2D(0.2f, 800f, new Vector2(12f, 1.2f));
            BoundedBuoyancyBody2D raft = Rect2D(new Vector2(1.2f, 0.3f), 400f, new Vector2(300f, 0f));
            var hits = new List<float>();
            drop.waterEntered.AddListener((speed, at) => hits.Add(speed));
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(G * 750f / 250f, cUsual.Body.Velocity().y / Dt, 0.005f, "2D circle (C = 1), usual: first frame");
            Assert.AreEqual(G * 750f / 1250f, cBound.Body.Velocity().y / Dt, 0.001f, "2D circle (C = 1), bounded: first frame");
            Assert.AreEqual(G * 0.75f / 1.65f, balloon.Body.Velocity().y / Dt, 0.001f, "2D balloon in air: first frame");
            Assert.AreEqual(0.4f * 0.6f + Mathf.PI * 0.04f, log.Area, 1e-6f, "2D capsule area");
            float plankSum = 0f, raftLo = 1e9f, raftHi = -1e9f;
            int plankN = 0;
            for (int step = 2; step <= 1800; step++)
            {
                yield return new WaitForFixedUpdate();
                float t = step * Dt;
                if (t > 7f && t < 10f)
                {
                    plankSum += plank.Body.position.y;
                    plankN++;
                }
                if (t > 5f)
                {
                    raftLo = Mathf.Min(raftLo, raft.Body.position.y);
                    raftHi = Mathf.Max(raftHi, raft.Body.position.y);
                }
            }
            Assert.AreEqual(0f, plankSum / plankN, 0.01f, "2D plank at half density floats with its centre at the surface");
            Assert.AreEqual(0f, Mathf.Abs(Mathf.DeltaAngle(0f, boat.Body.rotation)), 3f, "2D boat tipped 40 deg, low centre of mass: rights");
            Assert.AreEqual(1.5f, drift.Body.Velocity().x, 0.05f, "2D ball in a 1.5 m/s current drifts with it");
            Assert.AreEqual(Mathf.Sqrt(2f * G * 1f), hits.Count > 0 ? hits[0] : 0f, 0.08f, "2D ball dropped 1 m: water entered speed");
            Assert.AreEqual(0.5f, raftHi - raftLo, 0.3f, "2D raft on 0.2 m waves rises and falls");
        }

        // B13 ------------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator B13_OtherWaterSystem()
        {
            Fast();
            // A stand-in for another water system gives the same waves as a water 40 m away, so a raft on it must move
            // as its twin on that water's own waves.
            BoundedBuoyancyWater own = Water(new Vector3(0f, 0f, 500f), new Vector3(20f, 4f, 20f));
            own.waveHeight = 0.08f;
            own.waveLength = 4f;
            own.waveDirection = 20f;
            own.Sync();
            BoundedBuoyancyWater other = Water(new Vector3(40f, 0f, 500f), new Vector3(20f, 4f, 20f));
            var copy = other.gameObject.AddComponent<ShiftedSurface>();
            copy.from = own;
            copy.shift = new Vector3(40f, 0f, 0f);
            copy.maxWave = 0.12f;
            other.surfaceSource = copy;
            other.Sync();
            Assert.IsTrue(other.UsesOtherSurface, "the water uses the other surface");
            BoundedBuoyancyBody a = Box(new Vector3(0.6f, 0.15f, 0.4f), 450f, new Vector3(-4f, 0.05f, 500f), 15f);
            BoundedBuoyancyBody b = Box(new Vector3(0.6f, 0.15f, 0.4f), 450f, new Vector3(36f, 0.05f, 500f), 15f);
            float worst = 0f, worstTilt = 0f, lo = 1e9f, hi = -1e9f;
            for (int step = 1; step <= 240; step++)
            {
                yield return new WaitForFixedUpdate();
                worst = Mathf.Max(worst, Mathf.Abs(a.Body.position.y - b.Body.position.y));
                worstTilt = Mathf.Max(worstTilt, Mathf.Abs(Tilt(a) - Tilt(b)));
                if (step * Dt > 2f)
                {
                    lo = Mathf.Min(lo, b.Body.position.y);
                    hi = Mathf.Max(hi, b.Body.position.y);
                }
            }
            Assert.AreEqual(0f, 1000f * worst, 0.5f, "same waves from another system: height (mm)");
            Assert.AreEqual(0f, worstTilt, 0.2f, "same waves from another system: tilt (deg)");
            Assert.AreEqual(20f, 100f * (hi - lo), 12f, "raft on the other system's waves rises and falls (cm)");
        }

        [UnityTest]
        public IEnumerator Inertia2DIsAboutTheCentreOfMass()
        {
            // A plain Rigidbody2D with its centre of mass off the origin and its inertia set by hand: a torque must turn
            // it at torque / inertia, also after the mass changes. Bounded Buoyancy Body 2D relies on this.
            GameObject go = Make("Inertia probe", new Vector3(-50f, 50f, 0f));
            go.AddComponent<BoxCollider2D>().size = new Vector2(1f, 1f);
            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.useAutoMass = false;
            rb.mass = 2f;
            rb.centerOfMass = new Vector2(0f, 0.5f);
            rb.inertia = 0.4f;
            yield return new WaitForFixedUpdate();
            rb.mass = 3f;
            rb.inertia = 0.4f;
            rb.AddTorque(1f, ForceMode2D.Force);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(0.4f, rb.inertia, 1e-5f, "inertia reads back as set");
            Assert.AreEqual(1f / 0.4f, rb.angularVelocity * Mathf.Deg2Rad / Dt, 0.05f, "angular acceleration = torque / inertia");
        }
    }

    /// <summary>The Rigidbody velocity, under its Unity 6 or 2022.3 name.</summary>
    static class VelocityExtensions
    {
        public static Vector3 Velocity(this Rigidbody rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }

        public static void SetVelocity(this Rigidbody rb, Vector3 v)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = v;
#else
            rb.velocity = v;
#endif
        }

        public static Vector2 Velocity(this Rigidbody2D rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }
    }
}
