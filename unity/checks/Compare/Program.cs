// Head-to-head: four ways of floating things, on the same integrator, in the same water, through the same tests.
//   dotnet run -c Release            prints the tables and writes compare_*.csv
// The usual method and the pontoon method are written from their published descriptions (see Floater.cs); they are
// stand-ins for the methods, not the engines' own code. Their damping is tuned once, on the wooden float (the
// value that settles it soonest), and then used for everything, as a developer would set it for a project.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using BoundedBuoyancy.Checks;
using BoundedBuoyancy.Core;

namespace BoundedBuoyancy.Compare
{
    static class Program
    {
        const double G = 9.80665;
        static readonly Method[] Methods = { Method.Usual, Method.UsualDamped, Method.Pontoons, Method.Bounded };
        static readonly Dictionary<Method, double> Tuned = new Dictionary<Method, double>();
        static readonly List<string> Csv = new List<string>();

        static string Name(Method m)
        {
            switch (m)
            {
                case Method.Usual: return "Usual (force on own mass)";
                case Method.UsualDamped: return "Usual + damping";
                case Method.Pontoons: return "Pontoons + damping";
                default: return "Bounded Buoyancy";
            }
        }

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

        static WaterVolume Water(double waveHeight = 0.0, double waveLength = 3.0)
        {
            var w = new WaterVolume { Position = V3.Zero, Size = new V3(200, 10, 200) };
            w.SetWaves(waveHeight, waveLength, 0.0, G);
            return w;
        }

        static Floater Ball(Method m, double r, double density, V3 at, WaterVolume water)
        {
            var f = new Floater(m) { Water = water, Damping = Tuned.ContainsKey(m) ? Tuned[m] : 0.0 };
            f.SetUp(Sphere(r), density, at, 0.0);
            f.Pontoons.Add((V3.Zero, r));   // one pontoon, the ball itself: its volume is exact
            return f;
        }

        static Floater Raft(Method m, V3 at, double roll, WaterVolume water)
        {
            var f = new Floater(m) { Water = water, Damping = Tuned.ContainsKey(m) ? Tuned[m] : 0.0 };
            f.SetUp(Box(0.6, 0.15, 0.4), 400.0, at, roll);
            // Four pontoons at the corners, mid-height, sized so that together they have the raft's volume.
            double r = Math.Pow(0.6 * 0.15 * 0.4 * 3.0 / (16.0 * Math.PI), 1.0 / 3.0);
            foreach (double x in new[] { -0.2, 0.2 })
                foreach (double z in new[] { -0.12, 0.12 })
                    f.Pontoons.Add((new V3(x, 0, z), r));
            return f;
        }

        /// <summary>Fraction of a sphere's diameter under water when it floats at density ratio `ratio`.</summary>
        static double CapFraction(double ratio)
        {
            double lo = 0.0, hi = 1.0;
            for (int i = 0; i < 60; i++)
            {
                double x = 0.5 * (lo + hi);
                if (x * x * (3.0 - 2.0 * x) < ratio) lo = x; else hi = x;
            }
            return 0.5 * (lo + hi);
        }

        /// <summary>Time after which `ys` stays within `tol` of `rest` (seconds), or the run's length if it never does.</summary>
        static double SettledAfter(List<double> ys, double rest, double tol, double dt)
        {
            for (int i = ys.Count - 1; i >= 0; i--)
                if (Math.Abs(ys[i] - rest) > tol)
                    return (i + 1) * dt;
            return 0.0;
        }

        // Tests --------------------------------------------------------------------------------------------------------

        /// <summary>A wooden ball (20 cm across, 500 kg/m³) dropped with its lowest point 30 cm above the water.</summary>
        static (double settle, double deepest, double rebound, double depthErr, List<double> ys) WoodenFloat(Method m, double dt, double seconds = 20.0)
        {
            var water = Water();
            var f = Ball(m, 0.1, 500.0, new V3(0, 0.4, 0), water);
            double rest = 0.1 - 2.0 * 0.1 * CapFraction(0.5);   // centre height when floating (Archimedes)
            var ys = new List<double>();
            double deepest = 1e9, rebound = -1e9;
            bool hit = false;
            for (int i = 0; i < (int)(seconds / dt); i++)
            {
                f.Step(G, dt);
                double y = f.Com.y;
                ys.Add(y);
                if (y < 0.1) hit = true;
                if (hit) { deepest = Math.Min(deepest, y); if (f.V.y < 0 || y > rebound) rebound = Math.Max(rebound, y); }
            }
            double tail = ys.Skip(ys.Count - (int)(2.0 / dt)).Average();
            return (SettledAfter(ys, rest, 0.001, dt), rest - deepest, rebound - rest, tail - rest, ys);
        }

        static void TuneDamping()
        {
            Console.WriteLine("Damping for the usual and pontoon methods, tuned on the wooden float (the rate, 1/s, that settles it soonest):");
            foreach (Method m in new[] { Method.UsualDamped, Method.Pontoons })
            {
                double best = 0.0, bestT = 1e9;
                for (double d = 0.25; d <= 40.0; d *= 1.08)
                {
                    Tuned[m] = d;
                    double t = WoodenFloat(m, 1.0 / 60.0).settle;
                    if (t < bestT) { bestT = t; best = d; }
                }
                Tuned[m] = best;
                Console.WriteLine("  {0,-26} {1,6:F2} /s  (settles in {2:F2} s)", Name(m), best, bestT);
            }
            Console.WriteLine();
        }

        static void Row(string test, string measure, string truth, Func<Method, string> value)
        {
            Console.Write("  {0,-44}", measure);
            var cells = new List<string> { test, measure, truth };
            foreach (Method m in Methods)
            {
                string v = value(m);
                cells.Add(v);
                Console.Write(" {0,14}", v);
            }
            Console.WriteLine("   {0}", truth);
            Csv.Add(string.Join(",", cells.Select(c => "\"" + c.Replace("\"", "'") + "\"")));
        }

        static string F(double v, string fmt = "F2") { return v.ToString(fmt, CultureInfo.InvariantCulture); }

        static void Header(string title)
        {
            Console.WriteLine(title);
            Console.Write("  {0,-44}", "");
            foreach (Method m in Methods)
                Console.Write(" {0,14}", m == Method.Usual ? "usual" : m == Method.UsualDamped ? "usual+damp" : m == Method.Pontoons ? "pontoons" : "bounded");
            Console.WriteLine("   physics says");
        }

        static int Main(string[] args)
        {
            Console.WriteLine("Bounded Buoyancy: head-to-head with the usual and pontoon methods (60 Hz, .NET {0})", Environment.Version);
            Console.WriteLine();
            double dt = 1.0 / 60.0;
            TuneDamping();
            Csv.Add("\"test\",\"measure\",\"physics says\",\"" + string.Join("\",\"", Methods.Select(Name)) + "\"");

            // 1. Light balls let go 0.5 m under water.
            var traces = new Dictionary<Method, List<double>>();
            foreach (var (label, density) in new[] { ("table-tennis ball (40 mm, 81 kg/m³)", 80.5721899), ("foam ball (40 mm, 30 kg/m³)", 30.0) })
            {
                Header("1. A " + label + ", let go 0.5 m under water");
                double ratio = density / 1000.0;
                var first = new Dictionary<Method, double>();
                var peak = new Dictionary<Method, double>();
                var settle = new Dictionary<Method, double>();
                foreach (Method m in Methods)
                {
                    var f = Ball(m, 0.02, density, new V3(0, -0.5, 0), Water());
                    var ys = new List<double>();
                    double p = -1e9;
                    for (int i = 0; i < (int)(20.0 / dt); i++)
                    {
                        f.Step(G, dt);
                        if (i == 0) first[m] = f.V.y / dt / G;
                        ys.Add(f.Com.y);
                        p = Math.Max(p, f.Com.y);
                    }
                    double rest = 0.02 - 2.0 * 0.02 * CapFraction(ratio);
                    peak[m] = p - rest;
                    settle[m] = SettledAfter(ys, rest, 0.001, dt);
                    if (density < 81.0 && density > 80.0)
                        traces[m] = ys;
                }
                Row(label, "first-frame acceleration (g)", F((1.0 - ratio) / (ratio + 0.5)) + " (added mass)", m => F(first[m], first[m] >= 10 ? "F0" : "F2"));
                Row(label, "highest point above its floating height (cm)", "a few cm", m => F(100.0 * peak[m], "F1"));
                Row(label, "still bobbing more than 1 mm until (s)", "", m => settle[m] >= 19.9 ? "never" : F(settle[m], "F1"));
                Console.WriteLine();
            }

            // 2. The wooden float dropped in (the test the damping was tuned on).
            Header("2. A wooden ball (20 cm across, 500 kg/m³) dropped from 30 cm");
            var wood = Methods.ToDictionary(m => m, m => WoodenFloat(m, dt));
            Row("wooden float", "plunges below its floating height (cm)", "", m => F(100.0 * wood[m].deepest, "F1"));
            Row("wooden float", "rebounds above it (cm)", "", m => F(100.0 * wood[m].rebound, "F1"));
            // Linear wave theory (reference/wave_damping.py): a ball floating half under bobs at 10.1 rad/s with a damping
            // ratio of 0.085, so its 7 cm rebound dies to 1 mm in ln(73) / (0.085 x 10.1) = 5 s, before any drag.
            Row("wooden float", "still bobbing more than 1 mm until (s)", "about 5 (wave-making, linear theory)", m => wood[m].settle >= 19.9 ? "never" : F(wood[m].settle, "F1"));
            Row("wooden float", "floating height error (mm)", "0 (Archimedes)", m => F(1000.0 * wood[m].depthErr, "F1"));
            Console.WriteLine();

            // 3. A raft let go tipped 30 degrees, at its floating height.
            Header("3. A raft (60 x 15 x 40 cm, 400 kg/m³) let go tipped 30 deg");
            var raft = new Dictionary<Method, (double right, double tilt, double err)>();
            foreach (Method m in Methods)
            {
                var f = Raft(m, new V3(0, 0.015, 0), 30.0, Water());
                var tilts = new List<double>();
                var ys = new List<double>();
                for (int i = 0; i < (int)(20.0 / dt); i++)
                {
                    f.Step(G, dt);
                    tilts.Add(f.TiltDegrees);
                    ys.Add(f.Position.y);
                }
                double tail = ys.Skip(ys.Count - (int)(2.0 / dt)).Average();
                raft[m] = (SettledAfter(tilts, 0.0, 1.0, dt), tilts[tilts.Count - 1], tail - 0.015);
            }
            Row("raft", "rocks more than 1 deg until (s)", "", m => raft[m].right >= 19.9 ? "never" : F(raft[m].right, "F1"));
            Row("raft", "floating height error (mm)", "0 (Archimedes)", m => F(1000.0 * raft[m].err, "F1"));
            Console.WriteLine();

            // 4. The raft on waves: how far it follows the surface under it, and how late. Short waves (5 times its length)
            // and long ones (20 times). Linear wave theory (Capytaine; reference/wave_damping.py --raft) gives this raft a
            // heave response of 0.98 in the 3 m waves and 1.00 in their shorter second wave, and 1.00 in the 12 m waves.
            foreach (var (length, label, truth) in new[] { (3.0, "3 m long, 5 times its length", "98 to 100 (linear theory)"),
                (12.0, "12 m long, 20 times its length", "100 (linear theory)") })
            {
                Header("4. The raft on waves (5 cm swell, " + label + ")");
                var follow = new Dictionary<Method, (double ratio, double lag)>();
                foreach (Method m in Methods)
                {
                    var water = Water(0.05, length);
                    var f = Raft(m, new V3(0, 0.015, 0), 0.0, water);
                    var ys = new List<double>();
                    var hs = new List<double>();
                    for (int i = 0; i < (int)(40.0 / dt); i++)
                    {
                        water.Time += dt;
                        f.Step(G, dt);
                        if (i * dt > 10.0)
                        {
                            ys.Add(f.Com.y);
                            hs.Add(water.HeightAt(f.Com));
                        }
                    }
                    double ratio = (ys.Max() - ys.Min()) / (hs.Max() - hs.Min());
                    // The lag that best lines the raft's heave up with the surface under it.
                    double ym = ys.Average(), hm = hs.Average();
                    int bestK = 0;
                    double best = -1e18;
                    for (int k = 0; k < (int)(1.0 / dt); k++)
                    {
                        double sum = 0.0;
                        for (int i = 0; i + k < ys.Count; i++)
                            sum += (ys[i + k] - ym) * (hs[i] - hm);
                        if (sum > best) { best = sum; bestK = k; }
                    }
                    follow[m] = (ratio, bestK * dt);
                }
                string test = "raft on " + F(length, "F0") + " m waves";
                Row(test, "rises and falls, of the surface under it (%)", truth, m => F(100.0 * follow[m].ratio, "F0"));
                Row(test, "lags the surface by (s)", "close to 0", m => F(follow[m].lag, "F2"));
                Console.WriteLine();
            }

            // 5. The same table-tennis ball at three time steps: the physics should not change with the step.
            Header("5. The table-tennis ball again, at 30, 60 and 120 steps a second");
            var byStep = Methods.ToDictionary(m => m, m => new List<double>());
            foreach (double hz in new[] { 30.0, 60.0, 120.0 })
            {
                foreach (Method m in Methods)
                {
                    var f = Ball(m, 0.02, 80.5721899, new V3(0, -0.5, 0), Water());
                    double p = -1e9;
                    for (int i = 0; i < (int)(10.0 * hz); i++)
                    {
                        f.Step(G, 1.0 / hz);
                        p = Math.Max(p, f.Com.y);
                    }
                    byStep[m].Add(100.0 * (p - (0.02 - 2.0 * 0.02 * CapFraction(0.0805721899))));
                }
            }
            Row("time steps", "highest point at 30 / 60 / 120 Hz (cm)", "the same", m => string.Join(" / ", byStep[m].Select(v => F(v, "F1"))));
            Console.WriteLine();

            // 6. Cost: 300 rafts on waves, per physics step.
            Header("6. Cost: 300 rafts riding waves");
            var cost = new Dictionary<Method, double>();
            foreach (Method m in Methods)
            {
                var water = Water(0.05, 3.0);
                var fs = new List<Floater>();
                var rnd = new Random(3);
                for (int i = 0; i < 300; i++)
                    fs.Add(Raft(m, new V3(rnd.NextDouble() * 60 - 30, 0.015, rnd.NextDouble() * 60 - 30), rnd.NextDouble() * 20 - 10, water));
                for (int i = 0; i < 120; i++) { water.Time += dt; foreach (var f in fs) f.Step(G, dt); }
                var sw = Stopwatch.StartNew();
                int n = 600;
                for (int i = 0; i < n; i++) { water.Time += dt; foreach (var f in fs) f.Step(G, dt); }
                cost[m] = sw.Elapsed.TotalMilliseconds / n;
            }
            Row("cost", "ms per step for 300 bodies (this machine)", "", m => F(cost[m], "F2"));
            Console.WriteLine();

            File.WriteAllLines("compare_results.csv", Csv);
            var trace = new List<string> { "t," + string.Join(",", Methods.Select(m => "\"" + Name(m) + "\"")) };
            for (int i = 0; i < (int)(3.0 / dt); i++)
                trace.Add(F((i + 1) * dt, "F4") + "," + string.Join(",", Methods.Select(m => F(100.0 * traces[m][i], "F3"))));
            File.WriteAllLines("compare_table_tennis_trace.csv", trace);
            var woodTrace = new List<string> { "t," + string.Join(",", Methods.Select(m => "\"" + Name(m) + "\"")) };
            for (int i = 0; i < (int)(10.0 / dt); i++)
                woodTrace.Add(F((i + 1) * dt, "F4") + "," + string.Join(",", Methods.Select(m => F(100.0 * wood[m].ys[i], "F3"))));
            File.WriteAllLines("compare_wooden_trace.csv", woodTrace);
            Console.WriteLine("Wrote compare_results.csv, and the heights (cm) of the table-tennis ball (first 3 s) and the wooden ball");
            Console.WriteLine("(first 10 s) in compare_table_tennis_trace.csv and compare_wooden_trace.csv.");
            return 0;
        }
    }
}
