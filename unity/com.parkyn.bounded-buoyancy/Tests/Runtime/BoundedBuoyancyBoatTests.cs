// Play-mode checks of foils and propellers in Unity's physics engine (B14): the same checks the engine-free core
// and the Godot plugin pass. Boats here point along +Z (Unity's forward), +X to starboard.
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BoundedBuoyancy.Tests
{
    public class BoundedBuoyancyBoatTests
    {
        const float G = 9.80665f;
        const float Dt = 1f / 60f;

        readonly List<GameObject> made = new List<GameObject>();
        Vector3 savedGravity;
        float savedFixed, savedScale, savedMaxDelta;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            savedGravity = Physics.gravity;
            savedFixed = Time.fixedDeltaTime;
            savedScale = Time.timeScale;
            savedMaxDelta = Time.maximumDeltaTime;
            Physics.gravity = new Vector3(0f, -G, 0f);
            Time.fixedDeltaTime = Dt;
            // Long runs go faster than real time; each physics step is still 1/60 s.
            Time.maximumDeltaTime = 1f;
            Time.timeScale = 20f;
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
            Time.fixedDeltaTime = savedFixed;
            Time.timeScale = savedScale;
            Time.maximumDeltaTime = savedMaxDelta;
            yield return null;
        }

        static IEnumerator Steps(float seconds)
        {
            int n = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < n; i++)
                yield return new WaitForFixedUpdate();
        }

        GameObject Make(string name, Vector3 at, float yawDegrees = 0f)
        {
            var go = new GameObject(name);
            go.transform.position = at;
            go.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            made.Add(go);
            return go;
        }

        void Water(Vector3 at, Vector3 size, Vector3 current = default(Vector3))
        {
            var w = Make("Water", at).AddComponent<BoundedBuoyancyWater>();
            w.size = size;
            w.current = current;
            w.showSurface = false;
            w.splashes = false;
            w.ripples = false;
            w.Sync();
        }

        /// <summary>A body whose collider is the convex hull of `points` (bow at +Z).</summary>
        BoundedBuoyancyBody Hull(string name, Vector3[] points, float density, Vector3 at, float yaw, Vector3 comOffset, float air = 0f)
        {
            GameObject go = Make(name, at, yaw);
            var mesh = new Mesh { name = name };
            var faces = Core.ConvexHull.Build(System.Array.ConvertAll(points, p => new Core.V3(p.x, p.y, p.z)));
            var verts = new Vector3[faces.Count];
            var tris = new int[faces.Count];
            for (int i = 0; i < faces.Count; i++)
            {
                verts[i] = new Vector3((float)faces[i].x, (float)faces[i].y, (float)faces[i].z);
                tris[i] = i;
            }
            mesh.vertices = verts;
            mesh.triangles = tris;
            var mc = go.AddComponent<MeshCollider>();
            mc.convex = true;
            mc.sharedMesh = mesh;
            go.AddComponent<Rigidbody>();
            var b = go.AddComponent<BoundedBuoyancyBody>();
            b.Density = density;
            b.airDensity = air;
            b.centreOfMassOffset = comOffset;
            return b;
        }

        static BoundedBuoyancyFoil Foil(BoundedBuoyancyBody b, Vector3 at, float area, float ar, Core.Medium medium = Core.Medium.Water, float effort = 0f)
        {
            var go = new GameObject("Foil");
            go.transform.SetParent(b.transform, false);
            go.transform.localPosition = at;
            var f = go.AddComponent<BoundedBuoyancyFoil>();
            f.area = area;
            f.aspectRatio = ar;
            f.medium = medium;
            f.effortOffset = effort;
            return f;
        }

        /// <summary>A small open motorboat, 2.4 m long, ballasted low: a keel, a rudder and a propeller, as asked.</summary>
        BoundedBuoyancyBody Motorboat(Vector3 at, float yaw, bool keel, bool rudder, bool prop, out BoundedBuoyancyFoil rudderFoil, out BoundedBuoyancyThruster thruster)
        {
            var pts = new[]
            {
                new Vector3(-0.45f, 0.2f, 1.1f), new Vector3(0.45f, 0.2f, 1.1f), new Vector3(-0.3f, -0.2f, 0.9f), new Vector3(0.3f, -0.2f, 0.9f),
                new Vector3(-0.45f, 0.2f, -1.2f), new Vector3(0.45f, 0.2f, -1.2f), new Vector3(-0.3f, -0.2f, -1.1f), new Vector3(0.3f, -0.2f, -1.1f),
                new Vector3(0f, 0.2f, 1.6f), new Vector3(0f, -0.1f, 1.4f),
            };
            BoundedBuoyancyBody b = Hull("Motorboat", pts, 250f, at, yaw, new Vector3(0f, -0.12f, 0f));
            if (keel)
                Foil(b, new Vector3(0f, -0.4f, 0f), 0.15f, 1.5f);
            rudderFoil = rudder ? Foil(b, new Vector3(0f, -0.3f, -1.15f), 0.06f, 2f, Core.Medium.Water, 0.04f) : null;
            thruster = null;
            if (prop)
            {
                var go = new GameObject("Propeller");
                go.transform.SetParent(b.transform, false);
                go.transform.localPosition = new Vector3(0f, -0.3f, -1.1f);
                thruster = go.AddComponent<BoundedBuoyancyThruster>();
                thruster.maxThrust = 400f;
                thruster.maxSpeed = 5f;
                thruster.diameter = 0.25f;
            }
            return b;
        }

        [UnityTest]
        public IEnumerator B14_FoilForceIsBounded()
        {
            // A 0.1 m cube at water's density carrying a 10 m² foil, moving across it at 10 m/s: the flow across the
            // foil is stopped in one step, never reversed (explicitly, the step would leave about -6650 m/s).
            Water(new Vector3(0f, 0f, 900f), new Vector3(20f, 6f, 20f));
            GameObject go = Make("Cube", new Vector3(0f, -2f, 900f));
            go.AddComponent<BoxCollider>().size = new Vector3(0.1f, 0.1f, 0.1f);
            go.AddComponent<Rigidbody>();
            var b = go.AddComponent<BoundedBuoyancyBody>();
            b.Density = 1000f;
            b.airDensity = 0f;
            b.dragCoefficient = 0f;
            Foil(b, Vector3.zero, 10f, 2f);
            yield return new WaitForFixedUpdate();
            b.Body.SetVelocity(new Vector3(10f, 0f, 0f));
            yield return new WaitForFixedUpdate();
            float m = b.OwnMass + b.AddedMass;
            float c = 0.5f * 1000f * 10f * 1.2f * 10f;
            Assert.AreEqual(10f / (1f + c * Dt / m), b.Body.Velocity().x, 2e-3f, "speed across the foil after one step");
        }

        [UnityTest]
        public IEnumerator B14_KeelRudderAndPropeller()
        {
            Water(new Vector3(0f, 0f, 1500f), new Vector3(600f, 8f, 600f));
            BoundedBuoyancyFoil r;
            BoundedBuoyancyThruster t;
            // A keel against leeway.
            BoundedBuoyancyBody keeled = Motorboat(new Vector3(-60f, 0f, 1400f), 0f, true, false, false, out r, out t);
            BoundedBuoyancyBody bare = Motorboat(new Vector3(-50f, 0f, 1400f), 0f, false, false, false, out r, out t);
            // A propeller with nothing else in the way.
            BoundedBuoyancyThruster full, half, astern;
            BoundedBuoyancyBody fullBoat = Motorboat(new Vector3(40f, 0f, 1300f), 0f, false, false, true, out r, out full);
            BoundedBuoyancyBody halfBoat = Motorboat(new Vector3(50f, 0f, 1300f), 0f, false, false, true, out r, out half);
            BoundedBuoyancyBody asternBoat = Motorboat(new Vector3(60f, 0f, 1500f), 0f, false, false, true, out r, out astern);
            foreach (BoundedBuoyancyBody b in new[] { fullBoat, halfBoat, asternBoat })
            {
                b.dragCoefficient = 0f;
                b.keepMomentumOnEntry = false;
            }
            full.throttle = 1f;
            half.throttle = 0.5f;
            astern.throttle = -1f;
            // The rudder hard over either way at full throttle.
            BoundedBuoyancyFoil rightRudder, leftRudder;
            BoundedBuoyancyThruster rightProp, leftProp;
            BoundedBuoyancyBody right = Motorboat(new Vector3(150f, 0f, 1400f), 0f, true, true, true, out rightRudder, out rightProp);
            BoundedBuoyancyBody left = Motorboat(new Vector3(180f, 0f, 1400f), 0f, true, true, true, out leftRudder, out leftProp);
            rightRudder.Angle = 25f;
            leftRudder.Angle = -25f;
            rightProp.throttle = 1f;
            leftProp.throttle = 1f;

            yield return Steps(3f);
            keeled.Body.SetVelocity(keeled.transform.TransformDirection(new Vector3(0.5f, 0f, 3f)));
            bare.Body.SetVelocity(bare.transform.TransformDirection(new Vector3(0.5f, 0f, 3f)));
            keeled.Body.angularVelocity = Vector3.zero;
            bare.Body.angularVelocity = Vector3.zero;
            yield return Steps(1f);
            Vector3 k = keeled.transform.InverseTransformDirection(keeled.Body.Velocity());
            Vector3 n = bare.transform.InverseTransformDirection(bare.Body.Velocity());
            Assert.AreEqual(0f, Mathf.Abs(k.x) / Mathf.Abs(n.x), 0.3f, "keel: sideways slip after 1 s, of the slip without one");
            Assert.AreEqual(1f, k.z / n.z, 0.15f, "keel: speed ahead kept, of the speed without one");

            yield return Steps(26f);   // 30 s from the start
            float r1 = right.Body.angularVelocity.y;
            yield return Steps(5f);
            float r2 = right.Body.angularVelocity.y;
            Assert.Greater(right.Body.angularVelocity.y, 0f, "rudder +25 deg turns the boat to starboard (clockwise seen from above)");
            Assert.AreEqual(1f, left.Body.angularVelocity.y / -right.Body.angularVelocity.y, 0.02f, "rudder -25 deg: the mirror image");
            Assert.AreEqual(0f, 100f * Mathf.Abs(r2 - r1) / Mathf.Abs(r1), 3f, "steady turn (% change over 5 s)");
            Assert.AreEqual(3.5f, right.Body.Velocity().magnitude / Mathf.Abs(right.Body.angularVelocity.y) / 2.4f, 3f, "turning circle radius, boat lengths");

            yield return Steps(25f);   // 60 s
            Assert.AreEqual(5f, fullBoat.Body.Velocity().z, 0.03f, "nothing in the way, full throttle: top speed");
            Assert.AreEqual(2.5f, halfBoat.Body.Velocity().z, 0.03f, "nothing in the way, half throttle: top speed");
            Assert.AreEqual(-5f, asternBoat.Body.Velocity().z, 0.03f, "nothing in the way, full astern: top speed");
        }

        [UnityTest]
        public IEnumerator B14_DinghySailsUpwind()
        {
            // A dinghy with a centreboard, a rudder and a self-trimming sail, steered by an autopilot 45 deg off a
            // 6 m/s wind, makes ground towards the wind and stays upright.
            Water(new Vector3(0f, 0f, 2500f), new Vector3(1000f, 8f, 1000f));
            var wind = Make("Wind", Vector3.zero).AddComponent<BoundedBuoyancyWind>();
            wind.velocity = new Vector3(0f, 0f, -6f);   // blowing towards -z, so the wind comes from +z
            wind.gustStrength = 0f;
            wind.directionSwing = 0f;
            var pts = new[]
            {
                new Vector3(-0.7f, 0.25f, 1.4f), new Vector3(0.7f, 0.25f, 1.4f), new Vector3(-0.45f, -0.15f, 1.2f), new Vector3(0.45f, -0.15f, 1.2f),
                new Vector3(-0.7f, 0.25f, -1.7f), new Vector3(0.7f, 0.25f, -1.7f), new Vector3(-0.45f, -0.15f, -1.6f), new Vector3(0.45f, -0.15f, -1.6f),
                new Vector3(0f, 0.25f, 1.9f), new Vector3(0f, -0.05f, 1.7f),
            };
            BoundedBuoyancyBody boat = Hull("Dinghy", pts, 160f, new Vector3(0f, 0f, 2300f), 45f, new Vector3(0f, -0.45f, 0f), 1.2f);
            Foil(boat, new Vector3(0f, -0.6f, 0.1f), 0.5f, 2f);
            BoundedBuoyancyFoil rudder = Foil(boat, new Vector3(0f, -0.35f, -1.65f), 0.12f, 2.5f, Core.Medium.Water, 0.06f);
            BoundedBuoyancyFoil sail = Foil(boat, new Vector3(0f, 2.1f, 0.6f), 8f, 3f, Core.Medium.Air, 0.55f);
            sail.autoTrim = true;
            var course = new Vector3(Mathf.Sin(45f * Mathf.Deg2Rad), 0f, Mathf.Cos(45f * Mathf.Deg2Rad));
            Vector3 start = Vector3.zero;
            float heel = 0f;
            for (int i = 0; i < 60 * 90; i++)
            {
                // Positive SignedAngle about +Y and a positive rudder angle both mean to starboard here.
                float error = Vector3.SignedAngle(boat.transform.forward, course, Vector3.up);
                rudder.Angle = Mathf.Clamp(1.5f * error - 25f * boat.Body.angularVelocity.y * Mathf.Rad2Deg / 60f, -30f, 30f);
                yield return new WaitForFixedUpdate();
                if (i == 60 * 30)
                    start = boat.Body.position;
                if (i > 60 * 30)
                    heel = Mathf.Max(heel, Vector3.Angle(boat.transform.up, Vector3.up));
            }
            float towards = (boat.Body.position - start).z / 60f;
            Assert.Greater(towards, 0.5f, "dinghy 45 deg off the wind: towards the wind (m/s)");
            Assert.Less(heel, 30f, "dinghy stays upright (deg)");
            Assert.AreEqual(0f, Vector3.Angle(boat.transform.forward, course), 8f, "dinghy holds its course (deg)");
            Assert.AreNotEqual(0f, sail.Angle, "the sail trimmed itself");
        }
    }
}
