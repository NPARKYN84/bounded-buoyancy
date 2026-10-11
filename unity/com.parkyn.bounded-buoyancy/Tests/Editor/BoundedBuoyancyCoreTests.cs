// Edit-mode checks of the engine-free core: shapes, waterline cuts, inertia, waves and gusts. They run in a
// second, without entering play mode (Window > General > Test Runner > EditMode).
using System;
using System.Collections.Generic;
using BoundedBuoyancy.Core;
using NUnit.Framework;

namespace BoundedBuoyancy.Tests
{
    public class BoundedBuoyancyCoreTests
    {
        static BuoyancyShape3D Box(double x, double y, double z)
        {
            var b = new ShapeBuilder3D();
            b.AddBox(V3.Zero, new V3(x / 2, 0, 0), new V3(0, y / 2, 0), new V3(0, 0, z / 2));
            return b.Build();
        }

        [Test]
        public void BoxAt45DegreesIsHalfUnderWithItsCentreAtTheSurface()
        {
            var box = Box(0.2, 0.1, 0.3);
            double vol;
            V3 cb;
            Immersion3D.Immerse(box, new BodyPose(V3.Zero, M3.TurnAboutZ(45.0)), WaterSurface.Flat(0.0), out vol, out cb);
            Assert.AreEqual(0.5, vol / box.Volume, 1e-9);
            Assert.AreEqual(-0.038302, cb.y, 1e-5, "centre of buoyancy below the centre");
        }

        [Test]
        public void SphereCapMatchesTheFormula()
        {
            var b = new ShapeBuilder3D();
            b.AddSphere(V3.Zero, 0.5);
            var s = b.Build();
            Assert.IsTrue(s.SingleSphere);
            double vol;
            V3 cb;
            Immersion3D.Immerse(s, new BodyPose(new V3(0, 0.2, 0), M3.Identity), WaterSurface.Flat(0.0), out vol, out cb);
            double h = 0.3;
            Assert.AreEqual(Math.PI * h * h * (1.5 - h) / 3.0, vol, 1e-12);
        }

        [Test]
        public void CapsuleKeepsItsExactVolumeAndCutsInHalfOnItsSide()
        {
            var b = new ShapeBuilder3D();
            b.AddCapsule(V3.Zero, new V3(0, 1, 0), 0.05, 0.3);
            var cap = b.Build();
            Assert.AreEqual(Math.PI * 0.0025 * 0.2 + 4.0 / 3.0 * Math.PI * 0.000125, cap.Volume, 1e-12);
            double vol;
            V3 cb;
            Immersion3D.Immerse(cap, new BodyPose(V3.Zero, M3.TurnAboutZ(90.0)), WaterSurface.Flat(0.0), out vol, out cb);
            Assert.AreEqual(0.5, vol / cap.Volume, 1e-9);
        }

        [Test]
        public void ConvexHullOfAWedge()
        {
            var pts = new List<V3>
            {
                new V3(-0.1, 0, -0.1), new V3(0.1, 0, -0.1), new V3(0, 0.1, -0.1),
                new V3(-0.1, 0, 0.1), new V3(0.1, 0, 0.1), new V3(0, 0.1, 0.1),
                new V3(0, 0.02, 0),   // inside: ignored
            };
            var b = new ShapeBuilder3D();
            Assert.IsTrue(b.AddConvexHull(pts));
            Assert.AreEqual(0.002, b.Build().Volume, 1e-12);
        }

        [Test]
        public void TurnedBoxHasTheBoxsPrincipalMoments()
        {
            M3 r = M3.TurnAboutZ(30.0);
            var b = new ShapeBuilder3D();
            b.AddBox(V3.Zero, r.Mul(new V3(0.3, 0, 0)), r.Mul(new V3(0, 0.1, 0)), r.Mul(new V3(0, 0, 0.2)));
            var s = b.Build();
            V3 m;
            M3 axes;
            s.UnitInertia(s.Centroid).Principal(out m, out axes);
            var got = new[] { m.x, m.y, m.z };
            Array.Sort(got);
            double v = 0.6 * 0.2 * 0.4;
            var want = new[] { v * 0.2 / 12.0, v * 0.4 / 12.0, v * 0.52 / 12.0 };
            Array.Sort(want);
            for (int i = 0; i < 3; i++)
                Assert.AreEqual(want[i], got[i], 1e-12);
            Assert.AreEqual(1.0, axes.Determinant, 1e-9);
        }

        [Test]
        public void FirstFrameAccelerationIsBounded()
        {
            var b = new ShapeBuilder3D();
            b.AddSphere(V3.Zero, 0.02);
            var core = new BodyCore3D { Shape = b.Build(), Density = 1.225, AirDensity = 0.0 };
            var pose = new BodyPose(new V3(0, -0.5, 0), M3.Identity);
            core.Step(pose, pose.Position, V3.Zero, WaterSample.Flat(0.0, 1000.0), V3.Zero, 9.80665, 1.0 / 60.0);
            Assert.AreEqual(9.80665 * (1 - 0.001225) / (0.001225 + 0.5), core.LastAcceleration.y, 1e-9, "never above g/C");
        }

        [Test]
        public void WavesHaveDeepWaterSpeedAndTheirHeight()
        {
            var w = WaveSet.Create(0.5, 4.0, 0.0, 9.81);
            Assert.AreEqual(Math.Sqrt(9.81 * 2.0 * Math.PI / 4.0), w.W1, 1e-12);
            Assert.AreEqual(0.7, w.MaxWave, 1e-12);
            double hi = 0;
            for (int i = 0; i < 2000; i++)
                hi = Math.Max(hi, Math.Abs(w.Offset(i * 0.01, 0.0, 0.0)));
            Assert.LessOrEqual(hi, w.MaxWave + 1e-12);
        }

        [Test]
        public void GustsStayWithinTheirStrength()
        {
            var f = new WindField { Velocity = new V3(5, 0, 0), GustStrength = 0.3 };
            double lo = 1e9, hi = 0;
            for (int k = 0; k < 6000; k++)
            {
                f.Time = k * 0.05;
                double s = f.VelocityAt(V3.Zero).Length;
                lo = Math.Min(lo, s);
                hi = Math.Max(hi, s);
            }
            Assert.AreEqual(3.5, lo, 0.25);
            Assert.AreEqual(6.5, hi, 0.25);
        }

        [Test]
        public void CircleSegmentIn2D()
        {
            var b = new ShapeBuilder2D();
            b.AddCircle(V2.Zero, 1.0);
            var s = b.Build();
            double area;
            V2 c;
            Immersion2D.Immerse(s, new BodyPose2D(V2.Zero, 0.0), WaterSurface2D.Flat(0.0), out area, out c);
            Assert.AreEqual(0.5 * Math.PI, area, 1e-12);
            Assert.AreEqual(-4.0 / (3.0 * Math.PI), c.y, 1e-12, "centroid of a half disc");
        }
    }
}
