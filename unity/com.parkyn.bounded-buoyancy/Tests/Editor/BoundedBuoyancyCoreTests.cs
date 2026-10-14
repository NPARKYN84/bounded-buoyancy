// Edit-mode checks of the engine-free core: shapes, waterline cuts, inertia, waves, gusts and other water systems. They run in a
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

        /// <summary>Another water system's surface for the checks: a water volume's waves, moved by `shift`.</summary>
        sealed class ShiftedWaves : ISurfaceHeights
        {
            readonly WaterVolume from;
            readonly V3 shift;
            public int Points;
            public ShiftedWaves(WaterVolume from, V3 shift) { this.from = from; this.shift = shift; }
            public void HeightsAt(V3[] points, double[] heights, int count)
            {
                Points += count;
                for (int i = 0; i < count; i++)
                    heights[i] = from.HeightAt(points[i] - shift);
            }
            public double MaxWave { get { return from.MaxWave; } }
        }

        [Test]
        public void AnotherSystemsSurfaceCutsBodiesAsTheSameWavesDo()
        {
            var sea = new WaterVolume { Position = V3.Zero, Size = new V3(50, 5, 50) };
            sea.SetWaves(0.3, 5.0, 25.0, 9.81);
            sea.Time = 1.7;
            var shift = new V3(30, 0, 0);
            var copy = new WaterSurface { Source = new ShiftedWaves(sea, shift) };
            var box = Box(0.8, 0.3, 0.5);
            for (int i = 0; i < 20; i++)
            {
                var pose = new BodyPose(new V3(-3.0 + 0.3 * i, 0.15 * Math.Sin(i), 0.2 * i), M3.TurnAboutZ(7.0 * i));
                double v1, v2;
                V3 c1, c2;
                Immersion3D.Immerse(box, pose, sea.Surface, out v1, out c1);
                Immersion3D.Immerse(box, new BodyPose(pose.Position + shift, pose.Basis), copy, out v2, out c2);
                Assert.AreEqual(v1, v2, 1e-12, "submerged volume");
                Assert.AreEqual(c1.x + shift.x, c2.x, 1e-9, "centre of buoyancy, x");
                Assert.AreEqual(c1.y, c2.y, 1e-9, "centre of buoyancy, y");
            }
        }

        [Test]
        public void BodyWellClearOfAnotherSystemsSurfaceAsksForOneHeight()
        {
            var sea = new WaterVolume { Position = V3.Zero, Size = new V3(50, 5, 50) };
            sea.SetWaves(0.3, 5.0, 0.0, 9.81);
            var source = new ShiftedWaves(sea, V3.Zero);
            var surface = new WaterSurface { Source = source };
            var box = Box(0.4, 0.4, 0.4);
            double vol;
            V3 cb;
            Immersion3D.Immerse(box, new BodyPose(new V3(0, 3, 0), M3.Identity), surface, out vol, out cb);
            Assert.AreEqual(0.0, vol, 0.0, "above the surface");
            Immersion3D.Immerse(box, new BodyPose(new V3(0, -3, 0), M3.Identity), surface, out vol, out cb);
            Assert.AreEqual(box.Volume, vol, 1e-12, "below the surface");
            Assert.AreEqual(2.0, source.Points, 0.0, "one height each, at the centre");
        }

        [Test]
        public void LooseWaterMovesAcrossALeaningTankAsTheTextbookSays()
        {
            // Water 0.4 m deep in a 2 m wide, 4 m long tank turned 10 degrees: its centre moves across the tank by
            // i tan(angle) / V, i = l b³ / 12 (the free-surface effect).
            var tank = new Compartment3D { Size = new V3(2, 1, 4) };
            var pose = new BodyPose(V3.Zero, M3.TurnAboutZ(10.0));
            double vol = 3.2;
            V3 centre;
            Compartment3D.LevelFor(tank.Corners(pose), vol, out centre);
            double across = Math.Abs(pose.Inverse(centre).x);
            Assert.AreEqual((4.0 * 8.0 / 12.0) * Math.Tan(10.0 * Math.PI / 180.0) / vol, across, 1e-9);
        }

        [Test]
        public void WaterComesInThroughAHoleByTheOrificeLaw()
        {
            // A hole of 0.01 m² 0.5 m below the water outside, in an empty compartment: Q = 0.6 A √(2 g h).
            var c = new Compartment3D { Size = new V3(1, 1, 1) };
            c.Holes.Add(new Hole(new V3(0, -0.5, 0), 0.01));
            c.Step(new BodyPose(V3.Zero, M3.Identity), p => 0.0, 1000.0, 9.81, 0.001);
            Assert.AreEqual(0.6 * 0.01 * Math.Sqrt(2.0 * 9.81 * 0.5), c.Inflow, 1e-9);
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
        public void FoilLiftAndItsBound()
        {
            // The lift slope tends to 2π for a long wing and π AR / 2 for a short one (Helmbold).
            Assert.AreEqual(1.0, Foil3D.LiftSlopeFor(1e6) / (2.0 * Math.PI), 1e-5, "long wing");
            Assert.AreEqual(1.0, Foil3D.LiftSlopeFor(1e-3) / (Math.PI * 1e-3 / 2.0), 1e-5, "short wing");
            Assert.AreEqual(1.2, Foil3D.NormalCoefficient(Math.PI / 2.0, 3.0), 1e-9, "square to the flow: a flat plate's 1.2");
            Assert.AreEqual(1.4, Foil3D.NormalCoefficient(Math.PI / 4.0, 6.0), 1e-9, "stalled: no more than 1.4");
            // A 10 m² foil on a 1 kg body moving across it at 10 m/s: stopped in one step, never thrown back.
            var foil = new Foil3D { Area = 10.0, Pivot = V3.Zero, Span = V3.Up, RestChord = new V3(0, 0, 1) };
            V3 v = new V3(10, 0, 0), w = V3.Zero;
            Appendages.Apply(new[] { foil }, null, 1.0, V3.Zero, M3.Identity, new V3(0.1, 0.1, 0.1), ref v, ref w,
                WaterSample.Flat(10.0, 1000.0), V3.Zero, 0.0, 1.0 / 60.0);
            Assert.AreEqual(10.0 / (1.0 + 60000.0 / 60.0), v.x, 1e-9, "speed across the foil after one step");
            // A sail with the wind abeam swings out, keeping 15 degrees to it; head to wind it lines up.
            Assert.AreEqual(75.0, Math.Abs(Foil3D.Trim(new V3(0, 0, 1), V3.Up, new V3(1, 0, 0), 15.0, 85.0)), 1e-9, "wind abeam");
            Assert.AreEqual(0.0, Foil3D.Trim(new V3(0, 0, 1), V3.Up, new V3(0, 0, 1), 15.0, 85.0), 1e-9, "head to wind");
            Assert.AreEqual(85.0, Math.Abs(Foil3D.Trim(new V3(0, 0, 1), V3.Up, new V3(0, 0, -1), 15.0, 85.0)), 1e-9, "running");
        }

        [Test]
        public void PropellerReachesItsSpeedWithoutOvershooting()
        {
            var t = new Thruster3D { MaxThrust = 1e7, MaxSpeed = 5.0, Throttle = 1.0, Position = V3.Zero, Direction = new V3(0, 0, 1) };
            V3 v = V3.Zero, w = V3.Zero;
            Appendages.Apply(null, new[] { t }, 1.0, V3.Zero, M3.Identity, new V3(1, 1, 1), ref v, ref w, WaterSample.Flat(10.0, 1000.0), V3.Zero, 0.0, 1.0 / 60.0);
            Assert.AreEqual(5.0, v.z, 1e-3, "a 10 MN propeller on 1 kg reaches 5 m/s in a step, no more");
            var lifted = new Thruster3D { MaxThrust = 400.0, MaxSpeed = 5.0, Throttle = 1.0, Position = new V3(0, 1, 0), Direction = new V3(0, 0, 1) };
            v = V3.Zero;
            Appendages.Apply(null, new[] { lifted }, 100.0, V3.Zero, M3.Identity, new V3(1, 1, 1), ref v, ref w, WaterSample.Flat(0.0, 1000.0), V3.Zero, 1.2, 1.0 / 60.0);
            Assert.AreEqual(0.0, lifted.Force.Length, 0.0, "out of the water it pushes nothing");
        }

        [Test]
        public void WaveMakingDampingFollowsLinearWaveTheory()
        {
            // Damping ratios against the exact ones (Capytaine; reference/wave_damping.csv).
            Assert.AreEqual(1.15, BodyCore3D.WaveDampingRatio(4.0) / 0.0401, 0.35, "cube floating half under");
            Assert.AreEqual(1.15, BodyCore3D.WaveDampingRatio(100.0) / 0.1597, 0.35, "box 1 x 1, draft 0.1");
            Assert.AreEqual(0.675, BodyCore3D.WaveDampingRatio(4.972) / 0.0848, 0.075, "ball floating half under");
            Assert.AreEqual(1.0, BodyCore2D.WaveDampingRatio(4.0) / 0.1508, 0.03, "side view, beam 4 x draft");
            // B = 2 ζ m ω for a box 0.4 m wide floating 0.2 m deep, with its inertial mass.
            double omega;
            double b = BodyCore3D.WaveDampingCoefficient(1000.0, 9.81, 0.032, 0.1, 48.0, out omega);
            Assert.AreEqual(Math.Sqrt(1000.0 * 9.81 * 0.16 / 48.0), omega, 1e-12, "its own bobbing frequency");
            Assert.AreEqual(2.0 * BodyCore3D.WaveDampingRatio(4.0) * 48.0 * omega, b, 1e-9);
            // Let go from rest half under, a box starts exactly as without it.
            var shape = new ShapeBuilder3D();
            shape.AddBox(V3.Zero, new V3(0.1, 0, 0), new V3(0, 0.1, 0), new V3(0, 0, 0.1));
            var core = new BodyCore3D { Shape = shape.Build(), Density = 250.0, AirDensity = 0.0 };
            var pose = new BodyPose(V3.Zero, M3.Identity);
            core.Step(pose, pose.Position, V3.Zero, WaterSample.Flat(0.0, 1000.0), V3.Zero, 9.80665, 1.0 / 60.0);
            Assert.IsTrue(core.HeaveDamping > 0.0, "damping on");
            Assert.AreEqual(9.80665 * 0.5, core.LastAcceleration.y, 1e-9, "first frame unchanged");
        }

        [Test]
        public void WaterAccelerationIsTheSurfacesSecondDerivative()
        {
            // At the surface the water's upward acceleration is the second time derivative of the surface's height;
            // well below, the waves' motion has died away.
            var sea = new WaterVolume { Position = new V3(3, 0.5, -2), Size = new V3(100, 20, 100) };
            sea.SetWaves(0.3, 8.0, 20.0, 9.81);
            var p = new V3(1, 0, 2);
            double h = 1e-3, t = 2.1;
            sea.Time = t + h;
            double up = sea.HeightAt(p);
            sea.Time = t - h;
            double down = sea.HeightAt(p);
            sea.Time = t;
            double here = sea.HeightAt(p);
            double fd = (up - 2.0 * here + down) / (h * h);
            Assert.AreEqual(fd, sea.Surface.VerticalAccelerationAt(new V3(p.x, here, p.z)), 1e-3, "at the surface");
            Assert.AreEqual(0.0, sea.Surface.VerticalAccelerationAt(new V3(p.x, -49.5, p.z)), 1e-9, "50 m down");
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
