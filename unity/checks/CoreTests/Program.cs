// Runs the engine-free core of Bounded Buoyancy for Unity against the same benchmarks the Godot plugin
// passes (tests/run_tests.gd, B1-B12), with a minimal integrator standing in for the physics engine.
//   dotnet run -c Release
using System;
using System.Collections.Generic;
using BoundedBuoyancy.Core;

namespace BoundedBuoyancy.Checks
{
    static partial class Program
    {
        const double G = 9.80665;
        const double R = 0.02;
        const double R_TT = 0.0805721899;   // table-tennis ball, 2.7 g, 40 mm
        static int failures;
        static int checks;

        static void Check(string label, double got, double want, double tol)
        {
            checks++;
            bool ok = Math.Abs(got - want) <= tol && !double.IsNaN(got);
            if (!ok)
                failures++;
            Console.WriteLine("  {0,-58} got {1,11:F4}   expected {2,11:F4}   {3}", label, got, want, ok ? "ok" : "FAIL");
        }

        // Shapes ----------------------------------------------------------------------------------------------

        static BuoyancyShape3D Sphere(double r)
        {
            var b = new ShapeBuilder3D();
            b.AddSphere(V3.Zero, r);
            return b.Build();
        }

        static BuoyancyShape3D Box(double x, double y, double z)
        {
            var b = new ShapeBuilder3D();
            b.AddBox(V3.Zero, new V3(x / 2, 0, 0), new V3(0, y / 2, 0), new V3(0, 0, z / 2));
            return b.Build();
        }

        static BuoyancyShape3D Hull(params double[] xyz)
        {
            var pts = new List<V3>();
            for (int i = 0; i < xyz.Length; i += 3)
                pts.Add(new V3(xyz[i], xyz[i + 1], xyz[i + 2]));
            var b = new ShapeBuilder3D();
            b.AddConvexHull(pts);
            return b.Build();
        }

        static BuoyancyShape3D BoatHull()
        {
            return Hull(-0.45, 0.12, -0.25, 0.45, 0.12, -0.25, -0.35, -0.12, -0.15, 0.35, -0.12, -0.15,
                -0.45, 0.12, 0.25, 0.45, 0.12, 0.25, -0.35, -0.12, 0.15, 0.35, -0.12, 0.15);
        }

        static SimBody Body(SimWorld w, BuoyancyShape3D s, double density, V3 at, double roll = 0.0, bool bounded = true,
            double air = 0.0, double halfHeight = 0.0, V3? com = null)
        {
            var b = new SimBody();
            b.Core.Bounded = bounded;
            b.Core.AirDensity = air;
            b.Core.AirHalfHeight = halfHeight;
            b.SetUp(s, density, at, roll, com);
            return w.Add(b);
        }

        static double CapFraction(double r)
        {
            double lo = 0.0, hi = 1.0;
            for (int i = 0; i < 60; i++)
            {
                double x = 0.5 * (lo + hi);
                if (x * x * (3.0 - 2.0 * x) < r)
                    lo = x;
                else
                    hi = x;
            }
            return 0.5 * (lo + hi);
        }

        static double HalfBoxCbDepth()
        {
            double c = Math.Cos(Math.PI / 4.0);
            double hx = 0.1, hy = 0.05;
            var pts = new[] { new V2(-hx, -hy), new V2(hx, -hy), new V2(hx, hy), new V2(-hx, hy) };
            var rot = new List<V2>();
            foreach (var p in pts)
                rot.Add(new V2(p.x * c - p.y * c, p.x * c + p.y * c));
            var poly = new List<V2>();
            for (int k = 0; k < 4; k++)
            {
                V2 a = rot[k], b = rot[(k + 1) % 4];
                if (a.y <= 0.0)
                    poly.Add(a);
                if ((a.y < 0.0 && b.y > 0.0) || (a.y > 0.0 && b.y < 0.0))
                    poly.Add(a + (b - a) * (a.y / (a.y - b.y)));
            }
            double area = 0.0, cy = 0.0;
            for (int k = 0; k < poly.Count; k++)
            {
                V2 p = poly[k], q = poly[(k + 1) % poly.Count];
                double cr = p.x * q.y - q.x * p.y;
                area += cr;
                cy += (p.y + q.y) * cr;
            }
            return -cy / (3.0 * area);
        }

        static WaterVolume Water(SimWorld w, V3 at, V3 size, double density = 1000.0)
        {
            var v = new WaterVolume { Position = at, Size = size, Density = density };
            v.SetWaves(0.0, 4.0, 0.0, w.G);
            w.Waters.Add(v);
            return v;
        }

        static void Run(SimWorld w, double seconds, Action<double> each = null)
        {
            double t = 0.0;
            while (t < seconds - 1e-9)
            {
                w.Step();
                t += w.Dt;
                if (each != null)
                    each(t);
            }
        }

        /// <summary>A ball of radius `a` and `density` at `at` flying at `velocity`, as BoundedBuoyancyCannonball: a fast
        /// ball's air drag in air of density `air`, and its steps that meet the water worked through.</summary>
        static SimBody Shot(SimWorld w, double a, double density, V3 at, V3 velocity, double air)
        {
            var b = Body(w, Sphere(a), density, at, 0.0, true, air);
            b.Ball = true;
            b.Core.FastBallDrag = true;
            b.V = velocity;
            return b;
        }

        /// <summary>The flight of a ball `d` across of mass `m` fired at `v0`, `deg` degrees up, from `h` above the water,
        /// through still air of 1.2 kg/m³, worked through in steps of 0.1 ms (second order) with the cannonball's drag,
        /// its air buoyancy and the air it takes along, to the moment its bottom touches the water.</summary>
        static void Flight(double v0, double deg, double h, double d, double m, double g, out double range, out double time)
        {
            double rho = 1.2, vol = Math.PI / 6.0 * d * d * d, mi = m + 0.5 * rho * vol, area = Math.PI * 0.25 * d * d, a = 0.5 * d;
            double px = 0.0, py = h, vx = v0 * Math.Cos(deg * Math.PI / 180.0), vy = v0 * Math.Sin(deg * Math.PI / 180.0);
            double step = 1e-4, t = 0.0, up = (rho * vol - m) * g / mi;
            while (true)
            {
                double s = Math.Sqrt(vx * vx + vy * vy);
                double k = 0.5 * SphereDrag.CoefficientAt(s, d, rho) * rho * area * s / mi;
                double mx = vx - k * vx * 0.5 * step, my = vy + (up - k * vy) * 0.5 * step;
                double sm = Math.Sqrt(mx * mx + my * my);
                double km = 0.5 * SphereDrag.CoefficientAt(sm, d, rho) * rho * area * sm / mi;
                double ox = px, oy = py;
                px += mx * step;
                py += my * step;
                vx -= km * mx * step;
                vy += (up - km * my) * step;
                t += step;
                if (py - a <= 0.0)
                {
                    double f = (oy - a) / (oy - py);
                    range = ox + f * (px - ox);
                    time = t - step + f * step;
                    return;
                }
            }
        }

        /// <summary>Whether a ball of relative density `sigma`, 14 cm across, its bottom a tenth of a millimetre above
        /// still water, meeting it at `speed`, `deg` below level, skips.</summary>
        static bool Skips(double sigma, double speed, double deg)
        {
            var w = new SimWorld();
            Water(w, new V3(0, 0, 20000), new V3(6000, 300, 6000));
            double r = deg * Math.PI / 180.0;
            var ball = Shot(w, 0.07, 1000.0 * sigma, new V3(1500, 0.07 + 1e-4, 20000), new V3(Math.Cos(r), -Math.Sin(r), 0) * speed, 1.2);
            for (int n = 0; n < 90; n++)
            {
                w.Step();
                if (ball.Core.Skips > 0)
                    return true;
                if (ball.Core.SubmergedVolume >= 0.999 * ball.Core.Shape.Volume)
                    return false;
            }
            return false;
        }

        static double Critical(double sigma, double speed, double guess)
        {
            double lo = 0.5 * guess, hi = 1.5 * guess;
            for (int n = 0; n < 12; n++)
            {
                double mid = 0.5 * (lo + hi);
                if (Skips(sigma, speed, mid))
                    lo = mid;
                else
                    hi = mid;
            }
            return 0.5 * (lo + hi);
        }

        /// <summary>Speed out over speed in, and the angle out (degrees), of a ball of `density` and radius `a` meeting still
        /// water at `speed`, `deg` below level, in still air.</summary>
        static void Bounce(double density, double a, double speed, double deg, out double ratio, out double angle)
        {
            var w = new SimWorld();
            Water(w, new V3(0, 0, 20000), new V3(6000, 300, 6000));
            double r = deg * Math.PI / 180.0;
            var ball = Shot(w, a, density, new V3(1500, a + 1e-4, 20000), new V3(Math.Cos(r), -Math.Sin(r), 0) * speed, 0.0);
            for (int n = 0; n < 120 && ball.Core.Skips == 0; n++)
                w.Step();
            V3 v = ball.Core.ExitVelocity;
            ratio = v.Length / speed;
            angle = Math.Atan2(v.y, v.x) * 180.0 / Math.PI;
        }

        /// <summary>Another water system's surface for the checks: a water volume's waves, moved by `offset`, with how its
        /// water moves in them.</summary>
        sealed class CopiedSurface : ISurfaceHeights, ISurfaceMotion
        {
            readonly WaterVolume from;
            readonly V3 offset;
            readonly double most;
            public int Points;

            public CopiedSurface(WaterVolume from, V3 offset, double maxWave)
            {
                this.from = from;
                this.offset = offset;
                most = maxWave;
            }

            public void HeightsAt(V3[] points, double[] heights, int count)
            {
                Points += count;
                for (int i = 0; i < count; i++)
                    heights[i] = from.HeightAt(points[i] - offset);
            }

            public double MaxWave { get { return most; } }

            public bool VerticalAccelerationAt(V3 p, double across, double along, out double acceleration)
            {
                acceleration = from.Surface.VerticalAccelerationAt(p - offset, across, along);
                return true;
            }
        }

        /// <summary>Another water system's flat surface at a fixed height.</summary>
        sealed class FlatSurface : ISurfaceHeights
        {
            readonly double level;
            public FlatSurface(double level) { this.level = level; }
            public void HeightsAt(V3[] points, double[] heights, int count)
            {
                for (int i = 0; i < count; i++)
                    heights[i] = level;
            }
            public double MaxWave { get { return 0.0; } }
        }

        /// <summary>Another water system's flat surface that also knows its current (a river): bodies drift with it.</summary>
        sealed class FlowingSurface : ISurfaceHeights, ISurfaceFlow
        {
            readonly double level;
            readonly V3 flow;
            public FlowingSurface(double level, V3 flow) { this.level = level; this.flow = flow; }
            public void HeightsAt(V3[] points, double[] heights, int count)
            {
                for (int i = 0; i < count; i++)
                    heights[i] = level;
            }
            public double MaxWave { get { return 0.0; } }
            public bool FlowAt(V3 p, out V3 f) { f = flow; return true; }
        }

        // Benchmarks ------------------------------------------------------------------------------------------

        static int Main()
        {
            Console.WriteLine("Bounded Buoyancy for Unity: engine-free core against the Godot benchmarks (.NET {0})", Environment.Version);
            double dt = 1.0 / 60.0;

            Console.WriteLine("B1  First-frame acceleration (m/s^2, up +)");
            {
                var w = new SimWorld();
                var cases = new[] { ("air bubble", 0.001225), ("foam sphere", 0.03), ("table-tennis ball", R_TT), ("cork", 0.25), ("ice", 0.917), ("rock", 2.7) };
                var list = new List<(string, double, SimBody, SimBody)>();
                int i = 0;
                foreach (var c in cases)
                {
                    list.Add((c.Item1, c.Item2, Body(w, Sphere(R), c.Item2 * 1000.0, new V3(i * 0.2, -0.5, 0), 0, false),
                        Body(w, Sphere(R), c.Item2 * 1000.0, new V3(i * 0.2 + 0.1, -0.5, 0))));
                    i++;
                }
                var box = Body(w, Box(0.1, 0.1, 0.1), 500.0, new V3(5, -0.5, 0));
                w.Step();
                foreach (var e in list)
                {
                    double r = e.Item2;
                    double usual = G * (1.0 - r) / r;
                    Check(e.Item1 + ", usual", e.Item3.V.y / dt, usual, Math.Max(0.002 * Math.Abs(usual), 0.01));
                    Check(e.Item1 + ", bounded", e.Item4.V.y / dt, G * (1.0 - r) / (r + 0.5), 0.01);
                }
                Check("box (upright cube), bounded", box.V.y / dt, G * 0.5, 0.01);
            }

            Console.WriteLine("B2  Release from 0.5 m, bounded model at 60 Hz (paper Table 4)");
            {
                var w = new SimWorld();
                var b2 = new[] { ("foam", 0.03, 0.517, 6.7), ("table-tennis", R_TT, 0.533, 6.1), ("cork", 0.25, 0.617, 4.9), ("pine", 0.5, 0.783, 3.3) };
                var runs = new List<(SimBody b, double surfaced, double peak)>();
                int i = 0;
                foreach (var c in b2)
                {
                    var body = Body(w, Sphere(R), c.Item2 * 1000.0, new V3(i++ * 0.2, -0.5, 0));
                    body.Core.WaveDamping = 0.0;   // the paper's model: no wave-making damping
                    runs.Add((body, -1.0, -1e9));
                }
                for (int step = 1; step <= 180; step++)
                {
                    w.Step();
                    for (int k = 0; k < runs.Count; k++)
                    {
                        var e = runs[k];
                        double y = e.b.Position.y;
                        if (e.surfaced < 0.0 && y >= 0.0)
                            e.surfaced = step * dt;
                        e.peak = Math.Max(e.peak, y);
                        runs[k] = e;
                    }
                }
                for (int k = 0; k < runs.Count; k++)
                {
                    Check(b2[k].Item1 + ", time to surface (s)", runs[k].surfaced, b2[k].Item3, 0.001);
                    Check(b2[k].Item1 + ", peak height (cm)", 100.0 * runs[k].peak, b2[k].Item4, 0.25);
                }
            }

            Console.WriteLine("B5  Shapes, centre of buoyancy and righting");
            {
                var flat = WaterSurface.Flat(0.0);
                double vol;
                V3 cb;
                var box = Box(0.2, 0.1, 0.3);
                var p45 = new BodyPose(V3.Zero, M3.TurnAboutZ(45.0));
                Immersion3D.Immerse(box, p45, flat, out vol, out cb);
                Check("box at 45 deg, centre at surface: V_sub/V", vol / box.Volume, 0.5, 1e-6);
                Check("box at 45 deg: centre of buoyancy below centre (cm)", -100.0 * cb.y, 100.0 * HalfBoxCbDepth(), 0.01);

                var cb3 = new ShapeBuilder3D();
                cb3.AddCapsule(V3.Zero, new V3(0, 1, 0), 0.05, 0.3);
                var cap = cb3.Build();
                Check("capsule volume (cm^3)", cap.Volume * 1e6, (Math.PI * 0.0025 * 0.2 + 4.0 / 3.0 * Math.PI * 0.000125) * 1e6, 0.01);
                Immersion3D.Immerse(cap, new BodyPose(new V3(0, -1, 0), M3.Identity), flat, out vol, out cb);
                Check("capsule fully under: V_sub/V", vol / cap.Volume, 1.0, 1e-9);
                Immersion3D.Immerse(cap, new BodyPose(V3.Zero, M3.TurnAboutZ(90.0)), flat, out vol, out cb);
                Check("capsule on its side, half under: V_sub/V", vol / cap.Volume, 0.5, 1e-6);

                var wedge = Hull(-0.1, 0, -0.1, 0.1, 0, -0.1, 0, 0.1, -0.1, -0.1, 0, 0.1, 0.1, 0, 0.1, 0, 0.1, 0.1);
                Check("convex wedge volume (cm^3)", wedge.Volume * 1e6, 0.5 * 0.2 * 0.1 * 0.2 * 1e6, 0.01);

                // A closed indexed mesh: the same box, as a buoyancy mesh would give it.
                var mv = new List<V3>();
                for (int i = 0; i < 8; i++)
                    mv.Add(new V3((i & 1) != 0 ? 0.1 : -0.1, (i & 2) != 0 ? 0.05 : -0.05, (i & 4) != 0 ? 0.15 : -0.15));
                int[] mt = { 1, 3, 7, 1, 7, 5, 0, 4, 6, 0, 6, 2, 2, 6, 7, 2, 7, 3, 0, 1, 5, 0, 5, 4, 4, 5, 7, 4, 7, 6, 0, 2, 3, 0, 3, 1 };
                var mb = new ShapeBuilder3D();
                mb.AddMesh(mv, mt);
                var mesh = mb.Build();
                Immersion3D.Immerse(mesh, p45, flat, out vol, out cb);
                Check("buoyancy mesh = box shape: V_sub/V", vol / mesh.Volume, 0.5, 1e-6);
                // The same mesh wound the other way round (as Unity's clockwise winding gives it).
                var mr = new int[mt.Length];
                for (int i = 0; i < mt.Length; i += 3)
                {
                    mr[i] = mt[i];
                    mr[i + 1] = mt[i + 2];
                    mr[i + 2] = mt[i + 1];
                }
                var mb2 = new ShapeBuilder3D();
                mb2.AddMesh(mv, mr);
                var mesh2 = mb2.Build();
                Check("mesh wound the other way: volume (cm^3)", mesh2.Volume * 1e6, 6000.0, 1e-6);

                var hullBall = new List<V3>();
                for (int i = 0; i < 400; i++)
                {
                    double zz = 1.0 - 2.0 * (i + 0.5) / 400.0, rr = Math.Sqrt(1.0 - zz * zz), ph = i * 2.399963229728653;
                    hullBall.Add(new V3(rr * Math.Cos(ph), zz, rr * Math.Sin(ph)) * 0.5);
                }
                var hb = new ShapeBuilder3D();
                hb.AddConvexHull(hullBall);
                var hullSphere = hb.Build();
                Check("hull of 400 points on a sphere: volume / sphere's", hullSphere.Volume / (4.0 / 3.0 * Math.PI * 0.125), 0.99, 0.01);

                var mixed = new ShapeBuilder3D();
                mixed.AddSphere(new V3(0, 0.3, 0), 0.1);
                mixed.AddBox(V3.Zero, new V3(0.1, 0, 0), new V3(0, 0.1, 0), new V3(0, 0, 0.1));
                var ms = mixed.Build();
                Check("sphere sharing a body with a box: volume (cm^3)", ms.Volume * 1e6, (4.0 / 3.0 * Math.PI * 0.001 + 0.008) * 1e6, 1e-6);

                // Inertia: a box turned 30 degrees in the body keeps the box's own moments, along turned axes.
                var tb = new ShapeBuilder3D();
                M3 r30 = M3.TurnAboutZ(30.0);
                tb.AddBox(V3.Zero, r30.Mul(new V3(0.3, 0, 0)), r30.Mul(new V3(0, 0.1, 0)), r30.Mul(new V3(0, 0, 0.2)));
                var turned = tb.Build();
                V3 moments;
                M3 axes;
                turned.UnitInertia(turned.Centroid).Principal(out moments, out axes);
                var sorted = new[] { moments.x, moments.y, moments.z };
                Array.Sort(sorted);
                double vbox = 0.6 * 0.2 * 0.4;
                var want = new[] { vbox * (0.04 + 0.16) / 12.0, vbox * (0.36 + 0.16) / 12.0, vbox * (0.36 + 0.04) / 12.0 };
                Array.Sort(want);
                for (int i = 0; i < 3; i++)
                    Check("turned box: principal moment " + (i + 1) + " (x 10^4)", 1e4 * sorted[i], 1e4 * want[i], 1e-6);
                Check("turned box: principal axes right-handed (det)", axes.Determinant, 1.0, 1e-9);
                double best = 0.0;
                foreach (var col in new[] { axes.X, axes.Y, axes.Z })
                    best = Math.Max(best, Math.Abs(col.Dot(r30.X)));
                Check("turned box: one principal axis along the long side", best, 1.0, 1e-9);
                Sym3 single = Sphere(0.1).UnitInertia(new V3(0, -0.05, 0));
                Check("sphere about a point 5 cm below: Ixx (x 10^6)", 1e6 * single.xx, 1e6 * (4.0 / 3.0 * Math.PI * 0.001) * (0.4 * 0.01 + 0.0025), 1e-6);
            }
            {
                var w = new SimWorld();
                var level = Body(w, Box(0.2, 0.2, 0.2), 500.0, V3.Zero);
                var raft = Body(w, Box(0.6, 0.15, 0.4), 400.0, new V3(2, 0, 0), 30.0);
                var tall = Body(w, Box(0.1, 0.4, 0.1), 600.0, new V3(4, 0.04, 0), 2.0);
                var boat = Body(w, Hull(-0.25, 0.06, -0.3, 0.25, 0.06, -0.3, -0.12, -0.06, -0.3, 0.12, -0.06, -0.3,
                    -0.25, 0.06, 0.3, 0.25, 0.06, 0.3, -0.12, -0.06, 0.3, 0.12, -0.06, 0.3), 300.0, new V3(6, 0, 0), 60.0, true, 0, 0, new V3(0, -0.05, 0));
                Run(w, 8.0);
                Check("cube at half density stays level: drift (mm)", 1000.0 * Math.Abs(level.Position.y), 0.0, 1.0);
                Check("raft dropped at 30 deg: tilt after 8 s (deg)", raft.TiltDegrees, 0.0, 2.0);
                Check("tall block, upright: tilt after 8 s (deg)", tall.TiltDegrees, 90.0, 5.0);
                Check("boat with low centre of mass, from 60 deg (deg)", boat.TiltDegrees, 0.0, 3.0);
            }

            Console.WriteLine("B6  Water volumes and waves");
            {
                var w = new SimWorld();
                Water(w, V3.Zero, new V3(4, 2, 4));
                Water(w, new V3(10, 1, 0), new V3(2, 1, 2));
                Water(w, new V3(20, 0, 0), new V3(4, 2, 4), 1025.0);
                var swell = Water(w, new V3(50, 0, 0), new V3(20, 3, 20));
                swell.SetWaves(0.05, 3.0, 0.0, w.G);
                var tt = Body(w, Sphere(R), R_TT * 1000.0, new V3(0, -0.5, 0));
                var dry = Body(w, Sphere(R), R_TT * 1000.0, new V3(30, -0.5, 0));
                var cube = Body(w, Box(0.2, 0.2, 0.2), 850.0, new V3(10, 1.0, 0));
                var salt = Body(w, Box(0.2, 0.2, 0.2), 900.0, new V3(20, -0.07, 0));
                var rider = Body(w, Box(0.6, 0.15, 0.4), 400.0, new V3(50, 0, 0));
                w.Step();
                Check("ball outside any water: falls at g (m/s^2)", dry.V.y / dt, -G, 0.01);
                double surfaced = tt.Position.y >= 0.0 ? dt : -1.0, lowest = 1e9, highest = -1e9, maxTilt = 0.0, cubeSum = 0.0;
                int cubeN = 0;
                Run(w, 10.0 - dt, t =>
                {
                    t += dt;
                    if (surfaced < 0.0 && tt.Position.y >= 0.0)
                        surfaced = t;
                    if (t > 7.0)
                    {
                        cubeSum += cube.Position.y;
                        cubeN++;
                    }
                    if (t > 3.0)
                    {
                        lowest = Math.Min(lowest, rider.Position.y);
                        highest = Math.Max(highest, rider.Position.y);
                        maxTilt = Math.Max(maxTilt, rider.TiltDegrees);
                    }
                });
                Check("table-tennis ball in a water volume: time to surface (s)", surfaced, 0.533, 0.001);
                Check("cube in a raised pool bobs about its level (cm)", 100.0 * cubeSum / cubeN, 100.0 + 100.0 * 0.1 * (1.0 - 2.0 * 0.85), 0.5);
                Check("cube in sea water floats at its sea-water level (cm)", 100.0 * salt.Position.y, 100.0 * 0.1 * (1.0 - 2.0 * 900.0 / 1025.0), 0.15);
                Check("raft on 5 cm waves: rises and falls (cm)", 100.0 * (highest - lowest), 10.0, 6.0);
                Check("raft on 5 cm waves: rocks but stays upright (deg)", maxTilt, 8.0, 7.5);
            }

            Console.WriteLine("B7  Air: balloons, thin air and changing density");
            {
                var w = new SimWorld();
                Water(w, V3.Zero, new V3(4, 2, 4));
                var balU = Body(w, Sphere(0.1), 0.45, new V3(100, 5, 0), 0, false, 1.2);
                var balB = Body(w, Sphere(0.1), 0.45, new V3(102, 5, 0), 0, true, 1.2);
                var stone = Body(w, Sphere(0.1), 2700.0, new V3(104, 5, 0), 0, true, 1.2);
                var high = Body(w, Sphere(0.1), 0.6, new V3(106, 2, 0), 0, true, 1.2, 10.0);
                var half = Body(w, Box(0.1, 0.1, 0.1), 300.0, new V3(1.5, 0, 1.5), 0, true, 1.2);
                var ballast = Body(w, Box(0.1, 0.1, 0.1), 2000.0, new V3(-1.5, -0.5, -1.5));
                var hot = Body(w, Sphere(1.0), 1.0, new V3(110, 10.0 * Math.Log(1.2) / Math.Log(2.0), 0), 30.0, true, 1.2, 10.0, new V3(0, -0.6, 0));
                ballast.Core.Density = 500.0;   // changed while running
                ballast.RecomputeInertia();
                w.Step();
                Check("helium balloon (0.45 kg/m^3) in air, usual", balU.V.y / dt, G * (1.2 - 0.45) / 0.45, 0.02);
                Check("helium balloon (0.45 kg/m^3) in air, bounded", balB.V.y / dt, G * (1.2 - 0.45) / (0.45 + 0.6), 0.002);
                Check("stone in air: just under g (m/s^2)", -stone.V.y / dt, G * (2700.0 - 1.2) / (2700.0 + 0.6), 0.002);
                Check("cube half in water, half in air", half.V.y / dt, G * (0.5006 - 0.3) / (0.3 + 0.5 * 0.5006), 0.002);
                Check("density changed while running: acceleration", ballast.V.y / dt, G * 0.5, 0.002);
                Check("density changed while running: own mass (kg)", ballast.Core.OwnMass, 0.5, 1e-6);
                Check("density changed while running: inertia (x 10^4)", 1e4 * ballast.Moments.x, 1e4 * 0.5 * 0.02 / 12.0, 0.001);
                Run(w, 120.0 - dt);
                Check("balloon (0.6) in air halving every 10 m: floats at (m)", high.Position.y, 10.0, 0.3);
                Check("hot-air balloon tipped 30 deg, low centre of mass: rights (deg)", hot.TiltDegrees, 0.0, 2.0);
            }

            Console.WriteLine("B8  Fast bodies");
            {
                var w = new SimWorld();
                Water(w, new V3(200, 0, 10), new V3(10, 60, 30));   // long enough for the skimmer to slow down in
                var falling = Body(w, Sphere(0.1), 2700.0, new V3(199, 50, 0), 0, true, 1.2);
                var fired = Body(w, Sphere(0.1), 100.0, new V3(201, 0.5, 0));
                var skimmer = Body(w, Box(0.4, 0.1, 0.3), 300.0, new V3(198, 0, -3));
                fired.V = new V3(0, -30, 0);
                skimmer.V = new V3(0, 0, 25);
                double entry = 0.0, worst = 0.0;
                Run(w, 20.0, t =>
                {
                    if (entry == 0.0 && falling.Position.y < 0.3)
                        entry = -falling.V.y;
                    foreach (var b in new[] { falling, fired, skimmer })
                        worst = Math.Max(worst, b.V.Length);
                });
                double vStone = Math.Sqrt(2.0 * 1700.0 * (4.0 / 3.0 * Math.PI * 0.001) * G / (1000.0 * 0.47 * Math.PI * 0.01));
                double vol = 4.0 / 3.0 * Math.PI * 0.001;
                double fAir = (2700.0 - 1.2) * vol * G;
                double kAir = 0.5 * 1.2 * 0.47 * Math.PI * 0.01;
                double vHit = Math.Sqrt(fAir / kAir * (1.0 - Math.Exp(-2.0 * kAir * 49.7 / ((2700.0 + 0.6) * vol))));
                Check("stone dropped 50 m: hits the water at (m/s)", entry, vHit, 0.5);
                Check("then sinks at its terminal speed in water (m/s)", -falling.V.y, vStone, 0.1);
                Check("light ball fired down at 30 m/s: floats again (cm)", 100.0 * fired.Position.y, 100.0 * (0.1 - 2.0 * 0.1 * CapFraction(0.1)), 1.0);
                Check("box skimming at 25 m/s: slowed, still level (deg)", skimmer.TiltDegrees, 0.0, 1.0);
                Check("box skimming at 25 m/s: stopped within 15 m (z, m)", skimmer.Position.z, 6.0, 6.0);
                Check("fastest speed seen: the stone's, as it hits (m/s)", worst, vHit, 0.5);
            }

            Console.WriteLine("B9  Flowing water");
            {
                var w = new SimWorld();
                var river = Water(w, new V3(300, 0, 0), new V3(120, 3, 10));
                river.Current = new V3(1.5, 0, 0);
                river.CurrentEdge = 2.0;
                var turned = Water(w, new V3(400, 0, 0), new V3(120, 3, 10));
                turned.Rotation = Quat.AboutY(90.0).Matrix;   // as Unity's Euler(0, 90, 0): +X turns to -Z
                turned.Current = new V3(1.5, 0, 0);
                var mid = Body(w, Sphere(0.1), 500.0, new V3(242, 0, 0));
                var bank = Body(w, Sphere(0.1), 500.0, new V3(242, 0, 4.5));
                var other = Body(w, Sphere(0.1), 500.0, new V3(400, 0, 58));
                var drifter = Body(w, Box(0.6, 0.15, 0.4), 400.0, new V3(242, 0, -1.0));
                Run(w, 60.0);
                Check("ball mid-river, after 60 s: drifts with the current (m/s)", mid.V.x, 1.5, 0.02);
                Check("ball 0.5 m from the bank drifts slower (m/s)", bank.V.x, 1.5 * 0.25, 0.02);
                Check("river turned a quarter turn: flows along -z (m/s)", other.V.z, -1.5, 0.02);
                Check("raft drifts with the current (m/s)", drifter.V.x, 1.5, 0.1);
                Check("raft stays level in the current (deg)", drifter.TiltDegrees, 0.0, 1.0);
            }

            Console.WriteLine("B10 Wind");
            {
                var w = new SimWorld();
                Water(w, new V3(560, 0, 0), new V3(200, 3, 60));
                var breeze = new WindField { Position = new V3(560, 0, 0), Velocity = new V3(3, 0, 0), GustStrength = 0.0, DirectionSwingDegrees = 0.0, Everywhere = false, Size = new V3(200, 40, 60) };
                w.Winds.Add(breeze);
                var drifting = Body(w, Sphere(0.1), 0.6, new V3(470, 10, 0), 0, true, 1.2, 10.0);
                var still = Body(w, Sphere(0.1), 0.6, new V3(300, 10, 30), 0, true, 1.2, 10.0);
                var sailer = Body(w, BoatHull(), 300.0, new V3(470, 0.05, -10), 0, true, 1.2, 0.0, new V3(0, -0.04, 0));
                sailer.Core.SailArea = 0.5;
                sailer.Core.SailCentre = new V3(0, 0.6, 0);
                var crate = Body(w, Box(0.4, 0.4, 0.4), 500.0, new V3(470, 0, 10), 0, true, 1.2);
                double heel = 0.0;
                Run(w, 30.0, t =>
                {
                    if (t > 10.0)
                        heel = Math.Max(heel, sailer.TiltDegrees);
                });
                Check("balloon in a 3 m/s wind drifts with it (m/s)", drifting.V.x, 3.0, 0.06);
                Check("balloon outside the wind's box stays put (m/s)", still.V.Length, 0.0, 0.01);
                Check("boat with a sail runs before the wind (m/s)", sailer.V.x, 0.75, 0.6);
                Check("crate with no sail barely moves (m/s)", crate.V.x, 0.1, 0.1);
                Check("boat heels a little in a light wind, stays up (deg)", heel, 1.0, 0.9);
                var gusty = new WindField { Position = new V3(0, 500, 0), Velocity = new V3(5, 0, 0), GustStrength = 0.3, Everywhere = false };
                double lo = 1e9, hi = 0.0;
                for (int k = 0; k < 6000; k++)
                {
                    gusty.Time = k * 0.05;
                    double s = gusty.VelocityAt(new V3(0, 500, 0)).Length;
                    lo = Math.Min(lo, s);
                    hi = Math.Max(hi, s);
                }
                Check("gusts: slowest wind (m/s)", lo, 5.0 * 0.7, 0.25);
                Check("gusts: strongest wind (m/s)", hi, 5.0 * 1.3, 0.25);
            }

            Console.WriteLine("B11 Crossings (the events behind splashes)");
            {
                var w = new SimWorld();
                Water(w, new V3(700, 0, 0), new V3(10, 4, 10));
                var dropped = Body(w, Sphere(0.1), 800.0, new V3(700, 1.1, 0));
                var popper = Body(w, Sphere(0.1), 100.0, new V3(702, -1.0, 0));
                popper.V = new V3(0, 6, 0);
                var entries = new List<(double, V3)>();
                int exits = 0;
                dropped.Entered += (s, at) => entries.Add((s, at));
                popper.Exited += (s, at) => exits++;
                Run(w, 5.0);
                Check("ball dropped 1 m: water entered once", entries.Count, 1, 0);
                Check("impact speed reported (m/s)", entries.Count > 0 ? entries[0].Item1 : 0.0, Math.Sqrt(2.0 * G * 1.0), 0.2);
                Check("impact point on the surface (cm)", 100.0 * (entries.Count > 0 ? entries[0].Item2.y : 1.0), 0.0, 0.5);
                Check("cork shot up: water exited reported", exits >= 1 ? 1 : 0, 1, 0);
            }

            Console.WriteLine("B12 2D version (metres, y up)");
            {
                var w = new SimWorld2();
                var tank = new WaterVolume2D { Position = V2.Zero, Size = new V2(40, 30) };
                var flume = new WaterVolume2D { Position = new V2(120, 0), Size = new V2(120, 10), Current = new V2(1.5, 0) };
                var swell = new WaterVolume2D { Position = new V2(300, 0), Size = new V2(40, 10) };
                swell.SetWaves(0.2, 6.0, w.G);
                w.Waters.Add(tank);
                w.Waters.Add(flume);
                w.Waters.Add(swell);
                Func<double, BuoyancyShape2D> circle = r => { var b = new ShapeBuilder2D(); b.AddCircle(V2.Zero, r); return b.Build(); };
                Func<double, double, BuoyancyShape2D> rect = (x, y) => { var b = new ShapeBuilder2D(); b.AddPolygon(new[] { new V2(-x / 2, -y / 2), new V2(x / 2, -y / 2), new V2(x / 2, y / 2), new V2(-x / 2, y / 2) }); return b.Build(); };
                Func<BuoyancyShape2D, double, V2, double, V2?, double, bool, SimBody2> add = (s, d, at, rot, com, air, bounded) =>
                {
                    var b = new SimBody2();
                    b.Core.AirDensity = air;
                    b.Core.Bounded = bounded;
                    b.SetUp(s, d, at, rot, com);
                    return w.Add(b);
                };
                var cUsual = add(circle(0.2), 250.0, new V2(-6, -5), 0, null, 0, false);
                var cBound = add(circle(0.2), 250.0, new V2(-5, -5), 0, null, 0, true);
                var balloon = add(circle(0.2), 0.45, new V2(50, 30), 0, null, 1.2, true);
                var plank = add(rect(2.0, 0.5), 500.0, new V2(0, 0.2), 0, null, 0, true);
                var bb = new ShapeBuilder2D();
                bb.AddPolygon(new[] { new V2(-0.6, 0.15), new V2(0.6, 0.15), new V2(0.4, -0.15), new V2(-0.4, -0.15) });
                var boat = add(bb.Build(), 300.0, new V2(6, 0.1), -40.0, new V2(0, -0.12), 0, true);
                var lb = new ShapeBuilder2D();
                lb.AddCapsule(V2.Zero, new V2(0, 1), 0.2, 1.0);
                var log = add(lb.Build(), 600.0, new V2(-10, 4), 0, null, 0, true);
                var drift = add(circle(0.2), 500.0, new V2(62, 0), 0, null, 0, true);
                var raft = add(rect(1.2, 0.3), 400.0, new V2(300, 0), 0, null, 0, true);
                var drop = add(circle(0.2), 800.0, new V2(12, 1.2), 0, null, 0, true);
                var hits = new List<double>();
                drop.Entered += (s, at) => hits.Add(s);
                w.Step();
                double g = w.G;
                Check("2D circle (C = 1), usual: first frame (m/s^2)", cUsual.V.y / dt, g * 750.0 / 250.0, 0.005);
                Check("2D circle (C = 1), bounded: first frame (m/s^2)", cBound.V.y / dt, g * 750.0 / 1250.0, 0.001);
                Check("2D balloon in air: first frame (m/s^2)", balloon.V.y / dt, g * 0.75 / 1.65, 0.001);
                Check("2D capsule area (m^2)", log.Core.Shape.Area, 0.4 * 0.6 + Math.PI * 0.04, 1e-9);
                double plankSum = 0.0, raftLo = 1e9, raftHi = -1e9;
                int plankN = 0;
                double t = dt;
                for (int k = 0; k < 1799; k++)
                {
                    w.Step();
                    t += dt;
                    if (t > 7.0 && t < 10.0)
                    {
                        plankSum += plank.Position.y;
                        plankN++;
                    }
                    if (t > 5.0)
                    {
                        raftLo = Math.Min(raftLo, raft.Position.y);
                        raftHi = Math.Max(raftHi, raft.Position.y);
                    }
                }
                Check("2D plank at half density floats with its centre at the surface (m)", plankSum / plankN, 0.0, 0.01);
                Check("2D boat tipped 40 deg, low centre of mass: rights (deg)", Math.Abs(boat.Angle * 180.0 / Math.PI), 0.0, 3.0);
                Check("2D ball in a 1.5 m/s current drifts with it (m/s)", drift.V.x, 1.5, 0.05);
                Check("2D raft on 0.2 m waves rises and falls (m)", raftHi - raftLo, 0.5, 0.3);
                Check("2D ball dropped 1 m: water entered speed (m/s)", hits.Count > 0 ? hits[0] : 0.0, Math.Sqrt(2.0 * g * 1.0), 0.08);
            }

            Console.WriteLine("B13 Other water systems");
            {
                // A stand-in "ocean" gives the same waves as a water volume 40 m away, so bodies on it must move
                // exactly as their twins on the volume's own waves.
                var w = new SimWorld();
                var own = Water(w, new V3(0, 0, 500), new V3(20, 4, 20));
                own.SetWaves(0.08, 4.0, 20.0, w.G);
                var other = Water(w, new V3(40, 0, 500), new V3(20, 4, 20));
                var ocean = new CopiedSurface(own, new V3(40, 0, 0), 0.12);
                other.Source = ocean;
                var cb = new ShapeBuilder3D();
                cb.AddCapsule(V3.Zero, new V3(0, 1, 0), 0.1, 0.5);
                var shapes = new[] { Box(0.6, 0.15, 0.4), BoatHull(), Sphere(0.15), cb.Build() };
                var twins = new List<SimBody[]>();
                for (int k = 0; k < shapes.Length; k++)
                {
                    var at = new V3(-4.0 + 2.5 * k, 0.05, 500.0 + 1.5 * k);
                    twins.Add(new[] { Body(w, shapes[k], 450.0, at, 15.0), Body(w, shapes[k], 450.0, at + new V3(40, 0, 0), 15.0) });
                }
                double worstY = 0.0, worstTilt = 0.0, lo = 1e9, hi = -1e9;
                Run(w, 4.0, t =>
                {
                    foreach (var e in twins)
                    {
                        worstY = Math.Max(worstY, Math.Abs(e[0].Position.y - e[1].Position.y));
                        worstTilt = Math.Max(worstTilt, Math.Abs(e[0].TiltDegrees - e[1].TiltDegrees));
                    }
                    if (t > 2.0)
                    {
                        lo = Math.Min(lo, twins[0][1].Position.y);
                        hi = Math.Max(hi, twins[0][1].Position.y);
                    }
                });
                Check("same waves from another system: height (mm)", 1000.0 * worstY, 0.0, 1e-6);
                Check("same waves from another system: tilt (deg)", worstTilt, 0.0, 1e-6);
                Check("raft on the other system's waves rises and falls (cm)", 100.0 * (hi - lo), 20.0, 12.0);
                foreach (var e in twins)
                {
                    e[0].Frozen = true;
                    e[1].Frozen = true;
                }
                // A body far above the other surface asks for one height a step (at its centre), not one per vertex.
                var aloft = Body(w, Box(0.4, 0.4, 0.4), 450.0, new V3(40, 3.0, 503));
                w.Step();
                int before = ocean.Points;
                Run(w, 10 * w.Dt);
                Check("body well above it: heights asked per step", (ocean.Points - before) / 10.0, 1.0, 0.0);
                aloft.Frozen = true;
                // A flat surface 0.25 m up: a cube at 85% of water's density floats on it.
                var flat = Water(w, new V3(80, 0, 500), new V3(10, 4, 10));
                flat.Source = new FlatSurface(0.25);
                var onFlat = Body(w, Box(0.2, 0.2, 0.2), 850.0, new V3(80, 0.3, 500));
                double sum = 0.0;
                int n = 0;
                Run(w, 10.0, t =>
                {
                    if (t > 7.0)
                    {
                        sum += onFlat.Position.y;
                        n++;
                    }
                });
                Check("cube on another system's flat surface (cm)", 100.0 * sum / n, 25.0 + 100.0 * 0.1 * (1.0 - 2.0 * 0.85), 0.5);
                // A system that knows its own current (HDRP's river current) carries bodies with it, over the volume's own.
                var stream = Water(w, new V3(150, 0, 500), new V3(120, 4, 10));
                stream.Current = new V3(0, 0, -5);   // the volume's own current, overridden by the source's
                stream.Source = new FlowingSurface(0.0, new V3(1.5, 0, 0));
                var carried = Body(w, Sphere(0.1), 500.0, new V3(100, 0, 500));
                Run(w, 60.0);   // as in B9: a ball takes about a minute to come up to the current's speed
                Check("ball on a flowing surface drifts with the source's current (m/s)", carried.V.x, 1.5, 0.02);
                Check("and not with the volume's own current (m/s)", carried.V.z, 0.0, 0.02);
            }

            Boats();
            WaveDampingChecks();
            WavePushChecks();

            Console.WriteLine("B17 Flooding");
            {
                // The free surface: water 0.4 m deep in a 2 m wide, 4 m long tank turned 10 degrees moves across the tank
                // by i tan(angle) / V and up it by i tan²(angle) / 2V (i = l b³ / 12), as in any wall-sided tank.
                var tank = new Compartment3D { Size = new V3(2, 1, 4) };
                double tilt = 10.0 * Math.PI / 180.0;
                var place = new V3(1, 2, 3);
                var leaning = new BodyPose(place, M3.TurnAboutZ(10.0));
                double vol = 2.0 * 0.4 * 4.0;
                V3 up, lean;
                double levelUp = Compartment3D.LevelFor((V3[])tank.Corners(new BodyPose(place, M3.Identity)).Clone(), vol, out up);
                Compartment3D.LevelFor(tank.Corners(leaning), vol, out lean);
                V3 shift = leaning.Inverse(lean) - (up - place);
                double i = 4.0 * 8.0 / 12.0;
                Check("free surface: water moves across the tank (mm)", 1000.0 * Math.Abs(shift.x), 1000.0 * i * Math.Tan(tilt) / vol, 0.001);
                Check("free surface: and up it (mm)", 1000.0 * shift.y, 1000.0 * i * Math.Tan(tilt) * Math.Tan(tilt) / (2.0 * vol), 0.001);
                Check("upright: the water's surface 0.4 m above the floor (mm)", 1000.0 * (levelUp - 1.5), 400.0, 0.001);
                // A mirrored tank (scale -1 across, as a port tank copied from a starboard one) holds water the same.
                V3 ignore;
                var mirror = new M3(new V3(-1, 0, 0), new V3(0, 1, 0), new V3(0, 0, 1));
                double levelMirrored = Compartment3D.LevelFor(tank.Corners(new BodyPose(place, mirror)), vol, out ignore);
                Check("  mirrored: the same (mm)", 1000.0 * (levelMirrored - 1.5), 400.0, 0.001);

                var w = new SimWorld();
                Water(w, new V3(0, 0, 6000), new V3(200, 200, 200));
                double g = w.G;
                // The sinking-bowl water clock: a box 1 m square and 0.5 m tall at draft 0.15 m, open to a hole in its
                // floor, sinks steadily and goes under at S (H - d0) / (C a √(2 g d0)); ballasted low to float upright.
                var bowl = Body(w, Box(1, 0.5, 1), 300.0, new V3(0, 0.1, 6000), 0.0, true, 0.0, 0.0, new V3(0, -0.24, 0));
                var hold = new Compartment3D { Size = new V3(1, 0.5, 1) };
                hold.Holes.Add(new Hole(new V3(0, -0.25, 0), 0.005));
                bowl.Core.Compartments.Add(hold);
                double expect = (0.5 - 0.15) / (0.6 * 0.005 * Math.Sqrt(2.0 * g * 0.15));
                double under = -1.0, half = -1.0;
                Run(w, expect * 1.3, t =>
                {
                    double top = bowl.Position.y + 0.25;
                    if (half < 0.0 && top <= 0.175)
                        half = t;
                    if (under < 0.0 && top <= 0.0)
                        under = t;
                });
                Check("sinking bowl: goes under at the water clock's time (s)", under, expect, 0.01 * expect);
                Check("  and sinks steadily: half way at half the time (s)", half, 0.5 * expect, 0.01 * expect);
                Run(w, 15.0);
                // Full, it weighs 650 kg against 500 kg of water displaced: it sinks at about its terminal speed.
                Check("  then sinks at about its terminal speed (m/s)", -bowl.V.y, Math.Sqrt(2.0 * 150.0 * g / (1000.0 * 0.47 * 1.0)), 0.25);
                Check("  and fills: the compartment holds 0.5 m³ (m³)", hold.FloodVolume, 0.5, 0.001);
                bowl.Frozen = true;
                // Without the ballast the same box lolls over as it floods.
                var loose = Body(w, Box(1, 0.5, 1), 300.0, new V3(4, 0.1, 6000));
                loose.Core.Compartments.Add(new Compartment3D { Size = new V3(1, 0.5, 1), FloodVolume = 0.06 });
                loose.W = new V3(0, 0, 0.016);   // a nudge, as the Godot check's
                Run(w, 15.0);
                Check("  unballasted, it lolls over as it floods (deg)", loose.TiltDegrees, 20.0, 18.0);
                loose.Frozen = true;

                // Lost buoyancy: a barge 2 m wide and 6 m long at draft 0.4 m with its middle 2 m open to the sea settles at
                // draft 0.4 x 6 / (6 - 2) = 0.6 m, level.
                var barge = Body(w, Box(2, 1, 6), 400.0, new V3(10, 0.1, 6000));
                var mid = new Compartment3D { Size = new V3(2, 1, 2) };
                mid.Holes.Add(new Hole(new V3(0, -0.5, 0), 0.2));
                barge.Core.Compartments.Add(mid);
                Run(w, 40.0);
                Check("lost buoyancy: barge settles at the draft of the textbook (cm)", 100.0 * (0.5 - barge.Position.y), 60.0, 0.5);
                Check("  level (deg)", barge.TiltDegrees, 0.0, 0.2);
                barge.Frozen = true;

                // Slack water across a barge's whole floor takes away more stability than the barge has: it lolls. With
                // bulkheads down the middle and across, the loss is a quarter and it stays upright.
                var slack = Body(w, Box(2, 1, 4), 500.0, new V3(20, -0.2, 6000), 1.0);
                slack.Core.Compartments.Add(new Compartment3D { Size = new V3(2, 1, 4), FloodVolume = 1.6 });
                var split = Body(w, Box(2, 1, 4), 500.0, new V3(30, -0.2, 6000), 1.0);
                foreach (double side in new[] { -0.5, 0.5 })
                    foreach (double end in new[] { -1.0, 1.0 })
                        split.Core.Compartments.Add(new Compartment3D
                        {
                            Size = new V3(1, 1, 2), FloodVolume = 0.4, Pose = new BodyPose(new V3(side, 0, end), M3.Identity),
                        });
                Run(w, 25.0);
                Check("free surface: a slack tank across the barge makes it loll (deg)", slack.TiltDegrees, 15.0, 12.0);
                Check("  bulkheads down the middle and across keep it upright (deg)", split.TiltDegrees, 0.0, 0.5);
                slack.Frozen = true;
                split.Frozen = true;

                // Side damage: a hole in the starboard (+x) compartment below the waterline lists the ship to starboard;
                // patched and pumped dry, it comes upright again.
                var ship = Body(w, Box(3, 1.2, 8), 350.0, new V3(40, 0.18, 6000));
                var port = new Compartment3D { Size = new V3(1.4, 1.2, 3), Pose = new BodyPose(new V3(-0.75, 0, 1.5), M3.Identity) };
                var starboard = new Compartment3D { Size = new V3(1.4, 1.2, 3), Pose = new BodyPose(new V3(0.75, 0, 1.5), M3.Identity) };
                starboard.Holes.Add(new Hole(new V3(0.7, -0.4, 0), 0.02));
                ship.Core.Compartments.Add(port);
                ship.Core.Compartments.Add(starboard);
                Run(w, 30.0);
                double heel = Math.Asin(Math.Max(-1.0, Math.Min(1.0, -ship.Basis.X.y))) * 180.0 / Math.PI;
                Check("side damage: lists to the holed side (deg, + to starboard)", heel, 10.0, 9.0);
                Check("  the port compartment stays dry (m³)", port.FloodVolume, 0.0, 0.0);
                starboard.Holes.Clear();
                starboard.PumpRate = 1.0;
                Run(w, 20.0);
                Check("  patched and pumped dry: upright again (deg)", ship.TiltDegrees, 0.0, 0.5);
                Check("  no water left (kg)", ship.Core.FloodMass, 0.0, 0.0);
                ship.Frozen = true;

                // A body let go with water already in it keeps its speed: the water is its own, not let in.
                var cask = Body(w, Box(1, 1, 1), 300.0, new V3(60, 20, 6000));
                cask.Core.Compartments.Add(new Compartment3D { Size = new V3(0.9, 0.9, 0.9), FloodVolume = 0.5 });
                cask.V = new V3(5, 0, 0);
                w.Step();
                Check("let go with water in it: keeps its speed (m/s)", cask.V.x, 5.0, 0.001);
            }

            Console.WriteLine("B18 Cannons");
            {
                const double pound = 0.45359237, inch = 0.0254;
                double d24 = 5.55 * inch, m24 = 24.0 * pound;
                double rho24 = m24 / (Math.PI / 6.0 * d24 * d24 * d24);
                double g = 9.80665;
                // In a vacuum: 100 m/s at 45 degrees comes back to the muzzle's height v² / g away.
                {
                    var w = new SimWorld();
                    Water(w, new V3(0, 0, 20000), new V3(6000, 300, 6000));
                    var ball = Shot(w, 0.5 * d24, rho24, new V3(-2500, 30, 20000), new V3(1, 1, 0) * (100.0 / Math.Sqrt(2.0)), 0.0);
                    double landed = double.NaN;
                    V3 prev = ball.Com;
                    Run(w, 20.0, t =>
                    {
                        if (double.IsNaN(landed) && prev.y >= 30.0 && ball.Com.y < 30.0)
                            landed = prev.x + (ball.Com.x - prev.x) * (prev.y - 30.0) / (prev.y - ball.Com.y);
                        prev = ball.Com;
                    });
                    Check("in a vacuum: range at 45 degrees, v² / g (m)", landed + 2500.0, 100.0 * 100.0 / w.G, 2.0);
                }
                // A 24-pounder at 5 degrees from 3 m above the sea at 488 m/s first grazes the water where the same drag
                // law worked through in steps of 0.1 ms puts it, about a mile off.
                {
                    var w = new SimWorld();
                    Water(w, new V3(0, 0, 20000), new V3(6000, 300, 6000));
                    double el = 5.0 * Math.PI / 180.0;
                    var ball = Shot(w, 0.5 * d24, rho24, new V3(-2500, 3, 20000), new V3(Math.Cos(el), Math.Sin(el), 0) * 488.0, 1.2);
                    double graze = double.NaN, when = double.NaN, now = 0.0;
                    ball.Entered += (sp, at) => { if (double.IsNaN(graze)) { graze = at.x + 2500.0; when = now; } };
                    Run(w, 15.0, t => { now = t; });
                    double refRange, refTime;
                    Flight(488.0, 5.0, 3.0, d24, m24, w.G, out refRange, out refTime);
                    Check("24-pounder at 5 degrees: first graze (m)", graze, refRange, 0.005 * refRange);
                    Check("  time of flight (s)", when, refTime, 0.02);
                    Check("  about a mile, as the range tables give (m)", graze, 1609.0, 240.0);
                    Check("  its drag coefficient at Mach 0.5, past the drag crisis", SphereDrag.CoefficientAt(0.5 * 343.0, d24, 1.2), 0.2, 0.05);
                    Check("  and at Mach 1.45", SphereDrag.CoefficientAt(1.45 * 343.0, d24, 1.2), 1.0, 0.05);
                }
                // Skipping: a ball meeting the water at a shallow angle skips off it if it comes down at less than 18° / √σ
                // (Birkhoff and others, 1944: 1 / √(10 σ) radians). The angle found by halving, at 200 m/s.
                foreach (double sigma in new[] { 7.42, 2.7, 1.2 })
                {
                    double birkhoff = 180.0 / Math.PI / Math.Sqrt(10.0 * sigma);
                    Check(string.Format("skips below 18°/√σ: σ = {0:F2} (deg)", sigma), Critical(sigma, 200.0, birkhoff), birkhoff, 0.05 * birkhoff);
                }
                // A steel ball 7 mm across skipping at 57 m/s leaves the water as Lyu, Yun and Wei (2021) measured.
                foreach (var c in new[] { new[] { 58.58, 1.04, 56.81, 0.94 }, new[] { 58.41, 2.60, 50.86, 2.68 }, new[] { 58.66, 3.69, 47.42, 3.26 }, new[] { 56.69, 4.53, 38.75, 4.32 } })
                {
                    double ratio, angle;
                    Bounce(7778.6, 0.0035, c[0], c[1], out ratio, out angle);
                    Check(string.Format("steel ball in at {0:F2} deg: speed out / speed in", c[1]), ratio, c[2] / c[0], 0.12);
                    Check("  and its angle out (deg)", angle, c[3], 0.35);
                }
                double slow = Critical(7.42, 200.0, 6.6), fast = Critical(7.42, 400.0, 6.6);
                Check("  the same at 200 and 400 m/s (deg)", fast, slow, 0.02 * slow);
                // Ricochet fire: a 24-pounder fired level from 3 m comes down at about 1 degree and skips on across the water,
                // each skip with a splash.
                {
                    var w = new SimWorld();
                    Water(w, new V3(0, 0, 20000), new V3(6000, 300, 6000));
                    var ball = Shot(w, 0.5 * d24, rho24, new V3(-2500, 3, 20000), new V3(488.0, 0, 0), 1.2);
                    int splashes = 0;
                    double first = double.PositiveInfinity;
                    ball.Entered += (sp, at) => { splashes++; first = Math.Min(first, at.x + 2500.0); };
                    Run(w, 25.0);
                    Check("ricochet fire: fired level, it skips on (at least 5 skips)", Math.Min(ball.Core.Skips, 10), 10.0, 5.0);
                    Check("  a splash at every touch (splashes - skips)", splashes - ball.Core.Skips, 0.5, 0.5);
                    Check("  and carries far past its first graze (at least 3 times as far)", Math.Min((ball.Com.x + 2500.0) / first, 5.0), 5.0, 2.0);
                }
            }

            Console.WriteLine("B19 Damage");
            {
                const double pound = 0.45359237, inch = 0.0254;
                // Into solid oak (ρ 750 kg/m³, σ 15 MPa): a 32-pounder fired from 400 and from 1000 yards goes 42 and 31.5
                // inches in (Elvin, 1805). The shot flies there through the air.
                double d32 = 6.10 * inch, m32 = 32.0 * pound, a32 = 0.5 * d32;
                double rho32 = m32 / (Math.PI / 6.0 * d32 * d32 * d32);
                foreach (var c in new[] { new[] { 400.0, 0.4, 42.0 }, new[] { 1000.0, 1.3, 31.5 } })
                {
                    var w = new SimWorld();
                    double el = c[1] * Math.PI / 180.0;
                    var wall = new SimWall { Point = new V3(-1000.0 + c[0] * 0.9144, 0, 40000), Normal = new V3(-1, 0, 0) };
                    w.Walls.Add(wall);
                    var ball = Shot(w, a32, rho32, new V3(-1000, 10, 40000), new V3(Math.Cos(el), Math.Sin(el), 0) * 488.0, 1.2);
                    double into = double.NaN;
                    wall.Struck += (b, at, v) => { into = Planking.Depth(m32, Math.PI * a32 * a32, v.Length, 750.0, 15e6); };
                    Run(w, 5.0);
                    Check(string.Format("32-pounder from {0} yards: into solid oak (in)", c[0]), into / inch, c[2], 0.04 * c[2]);
                }
                // Through planking: over its whole depth the shot loses all its speed, and the speed it keeps over part of
                // the way is m v dv = -A (σ + ρ v²) dx worked through in steps of 10 µm.
                {
                    double area = Math.PI * a32 * a32, v0 = 400.0;
                    double depth = Planking.Depth(m32, area, v0, 750.0, 15e6);
                    Check("over its whole depth it stops (m/s)", Planking.SpeedAfter(m32, area, v0, depth, 750.0, 15e6), 0.0, 1e-6);
                    double v = v0;
                    for (int i = 0; i < 25000; i++)
                    {
                        double dvdx = -area * (15e6 + 750.0 * v * v) / (m32 * v);
                        double vm = v + 0.5 * 1e-5 * dvdx;
                        v += 1e-5 * (-area * (15e6 + 750.0 * vm * vm) / (m32 * vm));
                    }
                    Check("through 25 cm of oak at 400 m/s (m/s)", Planking.SpeedAfter(m32, area, v0, 0.25, 750.0, 15e6), v, 0.001 * v);
                }
                // Shot that meets the sea on its way to a ship's side. Fired low, at 4°, to land 3 m short of it, it skips,
                // and strikes the side just above the water, not 14 cm under it where its straight path goes. Fired steeply,
                // at 20°, to land 1.5 m short, it plunges in and strikes below the waterline, slowed by the water as much as
                // the drag of a ball wholly under it would over that way.
                double d24 = 5.55 * inch, m24 = 24.0 * pound, a24 = 0.5 * d24;
                double rho24 = m24 / (Math.PI / 6.0 * d24 * d24 * d24);
                foreach (var c in new[] { new[] { 4.0, 3.0 }, new[] { 20.0, 1.5 } })
                {
                    var w = new SimWorld();
                    Water(w, new V3(0, 0, 20000), new V3(600, 300, 600));
                    var wall = new SimWall { Point = new V3(0, 0, 20000), Normal = new V3(1, 0, 0) };
                    w.Walls.Add(wall);
                    double el = c[0] * Math.PI / 180.0, touch = c[1];
                    var ball = Shot(w, a24, rho24, new V3(touch + 10.0, a24 + 10.0 * Math.Tan(el), 20000), new V3(-Math.Cos(el), -Math.Sin(el), 0) * 400.0, 1.2);
                    int entered = 0;
                    double height = double.NaN, speed = double.NaN;
                    ball.Entered += (sp, at) => entered++;
                    wall.Struck += (b, at, v) => { height = at.y; speed = v.Length; };
                    Run(w, 0.3);
                    if (c[0] < 10.0)
                    {
                        Check("shot meeting the sea 3 m short at 4°: it touches the water first", Math.Min(entered, 1), 1.0, 0.0);
                        Check("  and strikes the side above the water as it skips up (cm)", 100.0 * height, 10.0, 10.0);
                    }
                    else
                    {
                        double vol = 4.0 / 3.0 * Math.PI * a24 * a24 * a24;
                        double k = 0.5 * 1000.0 * 0.47 * Math.PI * a24 * a24 / (m24 + 0.5 * 1000.0 * vol);
                        Check("shot meeting the sea 1.5 m short at 20°: strikes under the water (cm)", 100.0 * height, -100.0 * (touch * Math.Tan(el) - a24), 15.0);
                        Check("  slowed by the water (m/s)", speed, 400.0 * Math.Exp(-k * touch / Math.Cos(el)), 0.05 * 400.0 * Math.Exp(-k * touch / Math.Cos(el)));
                    }
                }
            }

            Console.WriteLine("B20 Fishing");
            {
                // A line (BoundedBuoyancyLine) pulls as Hooke's law gives, T = E A x / L, and never pushes; however stiff
                // it is, it does not shake or fling what is on it. 100 g of steel on 2 m of 0.3 mm nylon (E 2.5 GPa) in the
                // air: stretched m g / k = 11.1 mm.
                var w = new SimWorld();
                double rw = 0.0145, vw = 4.0 / 3.0 * Math.PI * rw * rw * rw;
                var weight = Body(w, Sphere(rw), 0.1 / vw, new V3(0, 8.0, 0));
                var line = new SimLine { Anchor = new V3(0, 10, 0), Body = weight, Length = 2.0, Damping = 1.0 };
                w.Lines.Add(line);
                double k = 2.5e9 * Math.PI * 0.0003 * 0.0003 / 4.0 / 2.0;
                Check("spring constant of 2 m of 0.3 mm nylon (N/m)", line.SpringConstant, k, 1e-9 * k);
                Run(w, 3.0);
                Check("100 g hanging on 2 m of nylon: stretch (mm)", line.Stretch * 1000.0, 0.1 * w.G / k * 1000.0, 1e-6);
                Check("  pull (N)", line.Tension, 0.1 * w.G, 1e-9);
                // Never pushes: thrown up toward the post, the weight rises as if free.
                weight.V = new V3(0, 4.0, 0);
                double most = 0.0;
                Run(w, 20.0 / 60.0, t => most = Math.Max(most, line.Tension));
                Check("  thrown up toward the post: rises as if free (m/s)", weight.V.y, 4.0 - w.G * 20.0 / 60.0, 1e-9);
                Check("  the line pulling meanwhile (N)", most, 0.0, 0.0);
                double paidNone;
                double none = Tether.Pull(-0.05, -1.0, 10.0, k, 0.2, w.Dt, 0.0, out paidNone);
                Check("  a line pushed together: no pull (N s)", none, 0.0, 0.0);
                // A line with a slip (a reel's drag) pulls no harder than it, and runs out: 100 g on a line that slips at
                // 0.5 N falls with (m g - 0.5) / m.
                Run(w, 4.0);
                line.SlipTension = 0.5;
                double before = line.Length;
                Run(w, 0.1);
                double v0 = weight.V.y;
                Run(w, 0.5);
                Check("line slipping at 0.5 N: pull (N)", line.Tension, 0.5, 1e-9);
                Check("  the weight falls at (m/s²)", (v0 - weight.V.y) / 0.5, w.G - 5.0, 1e-6);
                Check("  line run out in 0.6 s (m)", line.Length - before, 0.5 * (w.G - 5.0) * 0.36, 0.03);
                double paidSlip;
                double slip = Tether.Pull(0.5, 0.0, 10.0, k, 0.0, w.Dt, 0.5, out paidSlip);
                Check("  the core's pull held to the slip (N)", slip / w.Dt, 0.5, 1e-12);
                Check("  paying out the stretch beyond 0.5 / k (m)", paidSlip, 0.5 - 0.5 / k - slip * 10.0 * w.Dt, 1e-12);
                // A line breaks when pulled harder than its breaking strength.
                line.SlipTension = 0.0;
                line.BreakingStrength = 2.0;
                weight.Core.Density *= 3.0;   // 300 g
                Run(w, 1.0);
                Check("300 g on a line that breaks at 2 N: broken", line.Broken ? 1.0 : 0.0, 1.0, 0.0);
            }
            {
                // Lead under the water pulls with its weight less its buoyancy (lead 11340 kg/m³); slack, nothing.
                var w = new SimWorld();
                Water(w, V3.Zero, new V3(200, 50, 200));
                var lead = Body(w, Sphere(0.01), 11340.0, new V3(3, -1.0, 0));
                var line = new SimLine { Anchor = new V3(3, 0.5, 0), Body = lead, Length = 1.5, Damping = 1.0 };
                w.Lines.Add(line);
                Run(w, 4.0);
                double pull = (11340.0 - 1000.0) * 4.0 / 3.0 * Math.PI * 1e-6 * w.G;
                Check("lead under water: pull (N)", line.Tension, pull, 1e-6 * pull);
                line.Length = 3.0;
                Run(w, 0.2);
                Check("slack line: pull (N)", line.Tension, 0.0, 0.0);
            }
            {
                // Steel wire (200 GPa) on a 5 g float, let go stretched with no damping: it springs back no faster than the
                // stretch stored, and comes to rest.
                var w = new SimWorld { G = 0.0 };
                double vb = 4.0 / 3.0 * Math.PI * 1e-6;
                var bob = Body(w, Sphere(0.01), 0.005 / vb, new V3(6, 7.99, 0));
                var wire = new SimLine { Anchor = new V3(6, 10, 0), Body = bob, Length = 1.98, Stiffness = 200.0, Damping = 0.0 };
                w.Lines.Add(wire);
                double kw = wire.SpringConstant, fastest = 0.0;
                Run(w, 2.0, t => fastest = Math.Max(fastest, bob.V.Length));
                Check("stiff wire on a 5 g float: fastest (m/s) within stored", fastest, 0.5 * Math.Sqrt(kw / 0.005) * 0.01, 0.5 * Math.Sqrt(kw / 0.005) * 0.01);
                Check("  at rest after 2 s, no further than its length (cm)", Math.Max(wire.Span - wire.Length, 0.0) * 100.0, 0.0, 0.05);
            }
            {
                // Between two free bodies the line pulls both, so their momentum is kept; snatched taut at 2 m/s, it
                // stretches no further than its energy can, v √(μ / k), μ the two masses' reduced mass.
                var w = new SimWorld { G = 0.0 };
                double vs = 4.0 / 3.0 * Math.PI * 0.05 * 0.05 * 0.05;
                var one = Body(w, Sphere(0.05), 2.0 / vs, new V3(10, 5, 0));
                var two = Body(w, Sphere(0.05), 0.5 / vs, new V3(13, 5, 0));
                var tow = new SimLine { Holder = one, Body = two, Length = 3.0, Damping = 1.0 };
                w.Lines.Add(tow);
                two.V = new V3(2.0, 0, 0);
                double longest = 0.0;
                Run(w, 2.0, t => longest = Math.Max(longest, tow.Stretch));
                Check("towing: momentum kept (kg m/s)", one.V.x * 2.0 + two.V.x * 0.5, 1.0, 1e-9);
                double reach = 2.0 * Math.Sqrt(0.4 / tow.SpringConstant);
                Check("  stretched (cm) within", longest * 100.0, 0.5 * reach * 100.0, 0.5 * reach * 100.0);
                Check("  the tow under way (m/s)", one.V.x, 0.4, 0.4);
            }

            Console.WriteLine("Speed: 300 bodies on waves");
            {
                foreach (var kind in new[] { "sphere", "box", "hull", "capsule" })
                {
                    var w = new SimWorld();
                    var sea = Water(w, V3.Zero, new V3(200, 5, 200));
                    sea.SetWaves(0.3, 6.0, 20.0, w.G);
                    var rnd = new Random(1);
                    for (int i = 0; i < 300; i++)
                    {
                        BuoyancyShape3D s;
                        if (kind == "sphere")
                            s = Sphere(0.3);
                        else if (kind == "box")
                            s = Box(0.6, 0.4, 0.8);
                        else if (kind == "hull")
                            s = BoatHull();
                        else
                        {
                            var cb = new ShapeBuilder3D();
                            cb.AddCapsule(V3.Zero, new V3(1, 0, 0), 0.2, 1.0);
                            s = cb.Build();
                        }
                        Body(w, s, 500.0, new V3(rnd.NextDouble() * 180 - 90, rnd.NextDouble() * 0.4 - 0.2, rnd.NextDouble() * 180 - 90), rnd.NextDouble() * 40 - 20, true, 1.2);
                    }
                    Run(w, 1.0);
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    Run(w, 5.0);
                    double ms = watch.Elapsed.TotalMilliseconds / 300.0;
                    Console.WriteLine("  {0,-10} {1,6:F2} ms per step for 300 bodies (whole step, this machine)", kind, ms);
                }
                {
                    // The same boxes on another system's copy of the waves: the cost of asking it for every vertex.
                    var w = new SimWorld();
                    var sea = Water(w, new V3(0, 0, 1000), new V3(200, 5, 200));
                    sea.SetWaves(0.3, 6.0, 20.0, w.G);
                    var copy = Water(w, V3.Zero, new V3(200, 5, 200));
                    copy.Source = new CopiedSurface(sea, new V3(0, 0, -1000), 0.42);
                    var rnd = new Random(1);
                    for (int i = 0; i < 300; i++)
                        Body(w, Box(0.6, 0.4, 0.8), 500.0, new V3(rnd.NextDouble() * 180 - 90, rnd.NextDouble() * 0.4 - 0.2, rnd.NextDouble() * 180 - 90), rnd.NextDouble() * 40 - 20, true, 1.2);
                    Run(w, 1.0);
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    Run(w, 5.0);
                    Console.WriteLine("  {0,-10} {1,6:F2} ms per step for 300 bodies (whole step, this machine)", "box, other", watch.Elapsed.TotalMilliseconds / 300.0);
                }
            }

            Console.WriteLine(failures == 0 ? "PASS (" + checks + " checks)" : "FAIL (" + failures + " of " + checks + " checks)");
            return failures == 0 ? 0 : 1;
        }
    }
}
