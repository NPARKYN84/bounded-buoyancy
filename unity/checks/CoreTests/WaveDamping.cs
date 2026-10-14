// B15: wave-making damping. A body bobbing at the surface sends out ripples that carry its energy away, so it settles;
// a body riding waves is not held back.
using System;
using System.Collections.Generic;
using BoundedBuoyancy.Core;

namespace BoundedBuoyancy.Checks
{
    static partial class Program
    {
        /// <summary>Heights of the successive crests of a bob about `rest`, from a list of heights.</summary>
        static List<double> Crests(List<double> ys, double rest)
        {
            var crests = new List<double>();
            for (int i = 1; i + 1 < ys.Count; i++)
                if (ys[i] > ys[i - 1] && ys[i] >= ys[i + 1] && ys[i] > rest)
                    crests.Add(ys[i] - rest);
            return crests;
        }

        static void WaveDampingChecks()
        {
            Console.WriteLine("B15 Wave-making damping (bobbing settles)");
            double dt = 1.0 / 60.0;

            // The damping ratio against the exact one of linear wave theory, computed with Capytaine
            // (reference/wave_damping.csv): A / d² and the exact ratio.
            foreach (var e in new[] {
                ("cube floating half under", 4.0, 0.0401, true), ("box 1 x 1, draft 0.1 (a raft)", 100.0, 0.1597, true),
                ("box 4 x 1, draft 0.1", 400.0, 0.2211, true), ("upright post, draft 4 x its width", 0.194, 0.0044, true),
                ("ball floating half under", 4.972, 0.0848, false), ("log floating half under", 15.536, 0.1464, false) })
            {
                double ratio = BodyCore3D.WaveDampingRatio(e.Item2) / e.Item3;
                if (e.Item4)
                    Check(e.Item1 + ": ratio / exact", ratio, 1.15, 0.35);
                else
                    Check(e.Item1 + ": ratio / exact", ratio, 0.675, 0.075);
            }
            foreach (var e in new[] { (0.5, 0.0270), (1.0, 0.0514), (4.0, 0.1508), (20.0, 0.2894) })
                Check(string.Format("side view, beam {0} x draft: ratio / exact", e.Item1), BodyCore2D.WaveDampingRatio(e.Item1) / e.Item2, 1.0, 0.03);

            // Let go from rest below where it floats, a body starts exactly as without the damping (it acts on the speed
            // at the start of each step).
            {
                var w = new SimWorld();
                Water(w, new V3(0, 0, 800), new V3(10, 4, 10));
                var half = Body(w, Box(0.2, 0.2, 0.2), 250.0, new V3(0, 0, 800));
                w.Step();
                Check("box at a quarter of water's density, half under: first frame (m/s^2)", half.V.y / dt, G * 0.5, 1e-9);
                Check("  with the damping on (N per m/s)", half.Core.HeaveDamping > 0.0 ? 1 : 0, 1, 0);
            }

            // Bodies let go 3 cm below where they float in still water, with and without the damping.
            {
                var w = new SimWorld();
                Water(w, new V3(0, 0, 900), new V3(40, 5, 40));
                var crate = Body(w, Box(0.4, 0.4, 0.4), 500.0, new V3(-10, -0.03, 900));
                // Wave-making damping alone, to compare with its damping ratio: no drag, and no sharing of momentum with
                // the water taken along as it goes deeper (which damps bobbing a little too).
                crate.Core.DragCoefficient = 0.0;
                crate.Core.KeepMomentumOnEntry = false;
                var ball = Body(w, Sphere(0.1), 500.0, new V3(-7, -0.03, 900));
                var ball0 = Body(w, Sphere(0.1), 500.0, new V3(-4, -0.03, 900));
                ball0.Core.WaveDamping = 0.0;
                var raft = Body(w, Box(1.0, 0.2, 1.0), 300.0, new V3(-1, 0.04 - 0.03, 900));
                var raft0 = Body(w, Box(1.0, 0.2, 1.0), 300.0, new V3(3, 0.04 - 0.03, 900));
                raft0.Core.WaveDamping = 0.0;
                var post = Body(w, Box(0.2, 1.0, 0.2), 800.0, new V3(7, -0.3 - 0.03, 900), 0.0, true, 0.0, 0.0, new V3(0, -0.4, 0));
                var sunk = Body(w, Sphere(0.1), 2000.0, new V3(10, -2.0, 900));
                var crateY = new List<double>();
                // The largest distance from its floating height over a second (a whole bob or more), from 3 s and 9 s.
                double ball3 = 0, ball03 = 0, raft3 = 0, raft03 = 0, ball9 = 0, ball09 = 0;
                Func<SimBody, double, double> off = (b, rest) => Math.Abs(b.Position.y - rest);
                var postY = new List<double>();
                bool sunkDamped = false;
                Run(w, 10.0, t =>
                {
                    crateY.Add(crate.Position.y);
                    postY.Add(post.Position.y);
                    sunkDamped |= sunk.Core.HeaveDamping != 0.0;
                    if (t > 3.0 && t <= 4.0)
                    {
                        ball3 = Math.Max(ball3, off(ball, 0.0));
                        ball03 = Math.Max(ball03, off(ball0, 0.0));
                        raft3 = Math.Max(raft3, off(raft, 0.04));
                        raft03 = Math.Max(raft03, off(raft0, 0.04));
                    }
                    if (t > 9.0)
                    {
                        ball9 = Math.Max(ball9, off(ball, 0.0));
                        ball09 = Math.Max(ball09, off(ball0, 0.0));
                    }
                });
                // The crate's crests: each a share 1 - e^(-2 pi zeta) lower than the last, zeta that of a box 0.4 m wide
                // floating 0.2 m deep (A / d² = 4). Its bobbing is at √(ρ g A / m) exactly, as the formula has it.
                var crests = Crests(crateY, 0.0);
                double zeta = BodyCore3D.WaveDampingRatio(4.0);
                double measured = crests.Count >= 3 ? Math.Log(crests[0] / crests[2]) / (4.0 * Math.PI) : 0.0;
                Console.WriteLine("  crate's crests (mm): {0}", string.Join(" ", crests.GetRange(0, Math.Min(6, crests.Count)).ConvertAll(c => (1000.0 * c).ToString("F2"))));
                Check("crate bobbing, no drag: damping ratio seen / formula", measured / zeta, 1.0, 0.1);
                Console.WriteLine("  wooden ball let go 3 cm low, bob from 3 s to 4 s: {0:F1} mm with wave damping, {1:F1} mm without; from 9 s: {2:F2} mm, {3:F1} mm",
                    1000.0 * ball3, 1000.0 * ball03, 1000.0 * ball9, 1000.0 * ball09);
                Check("wooden ball let go 3 cm low: bob from 3 s to 4 s (mm)", 1000.0 * ball3, 2.5, 2.5);
                Check("  without wave damping, at least 3 times that", ball03 > 3.0 * ball3 ? 1 : 0, 1, 0);
                Check("  with it, settled after 9 s (mm)", 1000.0 * ball9, 0.0, 0.5);
                Check("  without, still bobbing after 9 s (mm, > 2)", 1000.0 * ball09 > 2.0 ? 1 : 0, 1, 0);
                Console.WriteLine("  raft let go 3 cm low, bob from 3 s to 4 s: {0:F2} mm with wave damping, {1:F1} mm without", 1000.0 * raft3, 1000.0 * raft03);
                Check("raft let go 3 cm low: settled after 3 s (mm)", 1000.0 * raft3, 0.0, 0.5);
                var postCrests = Crests(postY, -0.3);
                Check("deep post (A / d² = 0.06): still bobbing after 10 s (last crest / first)",
                    postCrests.Count >= 3 ? postCrests[postCrests.Count - 1] / postCrests[0] : 0.0, 0.75, 0.25);
                Check("body wholly under water: no wave damping", sunkDamped ? 1 : 0, 0, 0);
            }

            // A ball leaping out of the water is damped only while it is at the surface, so how high it pops up hardly
            // depends on the time step.
            {
                var peaks = new List<double>();
                foreach (double hz in new[] { 60.0, 120.0, 480.0 })
                {
                    var w = new SimWorld { Dt = 1.0 / hz };
                    Water(w, new V3(0, 0, 950), new V3(10, 4, 10));
                    var tt = Body(w, Sphere(R), R_TT * 1000.0, new V3(0, -0.5, 950));
                    double peak = -1.0;
                    Run(w, 2.0, t => peak = Math.Max(peak, tt.Position.y));
                    peaks.Add(peak);
                }
                Console.WriteLine("  table-tennis ball let go 0.5 m under: pops up to {0:F1} / {1:F1} / {2:F1} cm at 60 / 120 / 480 Hz",
                    100.0 * peaks[0], 100.0 * peaks[1], 100.0 * peaks[2]);
                Check("  at 60 Hz, of its height at 480 Hz", peaks[0] / peaks[2], 1.0, 0.15);
                Check("  at 120 Hz, of its height at 480 Hz", peaks[1] / peaks[2], 1.0, 0.1);
            }

            // Riding waves the body is not held back: the damping acts on its bobbing relative to the surface under it.
            {
                var w = new SimWorld();
                var sea = Water(w, new V3(0, 0, 1000), new V3(80, 5, 80));
                sea.SetWaves(0.15, 12.0, 0.0, w.G);
                var raft = Body(w, Box(0.6, 0.15, 0.4), 400.0, new V3(0, 0.015, 1000));
                var raft0 = Body(w, Box(0.6, 0.15, 0.4), 400.0, new V3(0, 0.015, 1004));
                raft0.Core.WaveDamping = 0.0;
                double lo = 1e9, hi = -1e9, slo = 1e9, shi = -1e9, lo0 = 1e9, hi0 = -1e9;
                Run(w, 20.0, t =>
                {
                    if (t < 5.0)
                        return;
                    double y = raft.Position.y, s = sea.HeightAt(raft.Core.CentroidWorld(raft.BodyPose));
                    lo = Math.Min(lo, y); hi = Math.Max(hi, y);
                    slo = Math.Min(slo, s); shi = Math.Max(shi, s);
                    lo0 = Math.Min(lo0, raft0.Position.y); hi0 = Math.Max(hi0, raft0.Position.y);
                });
                Console.WriteLine("  raft on 12 m waves rises and falls {0:F0}% as far as the surface under it ({1:F0}% without wave damping)", 100.0 * (hi - lo) / (shi - slo), 100.0 * (hi0 - lo0) / (shi - slo));
                Check("raft on long waves follows the surface (rise and fall / surface's)", (hi - lo) / (shi - slo), 1.0, 0.06);
            }

            // Moved to other water, or teleported, a body is not kicked by the jump in the surface under it: its step is
            // the same as a fresh body's. A surface rising smoothly under it lifts it.
            {
                Func<BodyCore3D> make = () =>
                {
                    var c = new BodyCore3D { Shape = Box(0.4, 0.4, 0.4), Density = 500.0, AirDensity = 0.0, KeepMomentumOnEntry = false };
                    return c;
                };
                var pose = new BodyPose(V3.Zero, M3.Identity);
                var moved = new BodyPose(new V3(5, 0, 0), M3.Identity);
                var v = new V3(0, 0.05, 0);
                var a = new object();
                var b = new object();
                Func<double, object, WaterSample> at = (level, owner) => { var s = WaterSample.Flat(level, 1000.0); s.Owner = owner; return s; };

                var other = make();
                other.Step(pose, pose.Position, v, at(0.0, a), V3.Zero, G, dt);
                other.Step(pose, pose.Position + v * dt, v, at(0.1, b), V3.Zero, G, dt);
                var fresh = make();
                fresh.Step(pose, pose.Position + v * dt, v, at(0.1, b), V3.Zero, G, dt);
                Check("into other water 10 cm higher: step as a fresh body's (m/s)", other.VelocityTarget.y - fresh.VelocityTarget.y, 0.0, 1e-12);

                var jumped = make();
                jumped.Step(pose, pose.Position, v, at(0.0, a), V3.Zero, G, dt);
                jumped.Step(moved, moved.Position, v, at(0.1, a), V3.Zero, G, dt);
                var fresh2 = make();
                fresh2.Step(moved, moved.Position, v, at(0.1, a), V3.Zero, G, dt);
                Check("teleported 5 m to a surface 10 cm higher: step as a fresh body's (m/s)", jumped.VelocityTarget.y - fresh2.VelocityTarget.y, 0.0, 1e-12);

                var lifted = make();
                lifted.Step(pose, pose.Position, v, at(0.0, a), V3.Zero, G, dt);
                lifted.Step(pose, pose.Position + v * dt, v, at(0.15, a), V3.Zero, G, dt);
                var fresh4 = make();
                fresh4.Step(pose, pose.Position + v * dt, v, at(0.15, a), V3.Zero, G, dt);
                Check("its water lifted 15 cm in a step: step as a fresh body's (m/s)", lifted.VelocityTarget.y - fresh4.VelocityTarget.y, 0.0, 1e-12);

                var riding = make();
                riding.Step(pose, pose.Position, v, at(0.0, a), V3.Zero, G, dt);
                riding.Step(pose, pose.Position + v * dt, v, at(0.005, a), V3.Zero, G, dt);
                var fresh3 = make();
                fresh3.Step(pose, pose.Position + v * dt, v, at(0.005, a), V3.Zero, G, dt);
                Check("surface rising 0.3 m/s under it: lifted more than a fresh body", riding.VelocityTarget.y > fresh3.VelocityTarget.y ? 1 : 0, 1, 0);
            }

            // Damped only while at the surface: a body rising out of the water at 10 m/s from a centre of buoyancy 5 cm
            // down (a prism 10 cm deep) is out after 0.01 s; one plunging in at 10 m/s with as much out of the water as
            // in is under after 0.01 s, and with three times as much out, not within the step.
            Check("at the surface for: rising out (s)", BodyCore3D.AtSurface(0.05, 1.0, 10.0, dt), 0.01, 1e-12);
            Check("at the surface for: plunging in (s)", BodyCore3D.AtSurface(0.05, 1.0, -10.0, dt), 0.01, 1e-12);
            Check("at the surface for: plunging in, three times as much out (s)", BodyCore3D.AtSurface(0.05, 3.0, -10.0, dt), dt, 1e-12);
            Check("at the surface for: still (s)", BodyCore3D.AtSurface(0.05, 1.0, 0.0, dt), dt, 1e-12);

            // Water rising steadily (a lock filling, speeding up smoothly to 0.2 m/s over 2 s) carries a raft up with it at
            // the same depth as a raft without wave damping (drag alone holds both a little deeper): the damping acts on
            // the raft's speed relative to the surface, not on its own speed (which would hold it about 8 mm deeper).
            Func<double, double> lockSpeed = t => t < 2.0 ? 0.1 * (1.0 - Math.Cos(Math.PI * t / 2.0)) : 0.2;
            {
                var w = new SimWorld();
                var lockWater = Water(w, new V3(0, 0, 1200), new V3(10, 6, 10));
                var raft = Body(w, Box(1.0, 0.2, 1.0), 300.0, new V3(-2, 0.04, 1200));
                var raft0 = Body(w, Box(1.0, 0.2, 1.0), 300.0, new V3(2, 0.04, 1200));
                raft0.Core.WaveDamping = 0.0;
                double sum = 0.0, sum0 = 0.0;
                int n = 0;
                Run(w, 6.0, t =>
                {
                    lockWater.Position += new V3(0, lockSpeed(t) * dt, 0);
                    if (t > 5.0)
                    {
                        sum += raft.Position.y - lockWater.Position.y - 0.04;
                        sum0 += raft0.Position.y - lockWater.Position.y - 0.04;
                        n++;
                    }
                });
                Console.WriteLine("  raft in water rising at 0.2 m/s: {0:F2} mm from its usual depth with wave damping, {1:F2} mm without", 1000.0 * sum / n, 1000.0 * sum0 / n);
                Check("raft in water rising at 0.2 m/s: depth, less a raft's without (mm)", 1000.0 * (sum - sum0) / n, 0.0, 0.5);
            }
            {
                var w = new SimWorld2();
                var lockWater = new WaterVolume2D { Position = new V2(0, 0), Size = new V2(10, 6) };
                w.Waters.Add(lockWater);
                Func<double, double, BuoyancyShape2D> rect = (x, y) => { var bld = new ShapeBuilder2D(); bld.AddPolygon(new[] { new V2(-x / 2, -y / 2), new V2(x / 2, -y / 2), new V2(x / 2, y / 2), new V2(-x / 2, y / 2) }); return bld.Build(); };
                var rafts = new SimBody2[2];
                for (int k = 0; k < 2; k++)
                {
                    rafts[k] = new SimBody2();
                    rafts[k].Core.AirDensity = 0.0;
                    rafts[k].Core.WaveDamping = k == 0 ? 1.0 : 0.0;
                    rafts[k].SetUp(rect(1.2, 0.3), 400.0, new V2(-2 + 4 * k, 0.03), 0.0);   // draft 0.12: centre 3 cm up
                    w.Add(rafts[k]);
                }
                double sum = 0.0, sum0 = 0.0, t = 0.0;
                int n = 0;
                Run2(w, 6.0, () =>
                {
                    t += w.Dt;
                    lockWater.Position += new V2(0, lockSpeed(t) * w.Dt);
                    if (t > 5.0)
                    {
                        sum += rafts[0].Position.y - lockWater.Position.y - 0.03;
                        sum0 += rafts[1].Position.y - lockWater.Position.y - 0.03;
                        n++;
                    }
                });
                Console.WriteLine("  2D raft in water rising at 0.2 m/s: {0:F2} mm from its usual depth with wave damping, {1:F2} mm without", 1000.0 * sum / n, 1000.0 * sum0 / n);
                Check("2D raft in water rising at 0.2 m/s: depth, less a raft's without (mm)", 1000.0 * (sum - sum0) / n, 0.0, 0.5);
            }

            // Side view: a 2D crate and plank settle; without wave damping the crate keeps bobbing.
            {
                var w = new SimWorld2();
                w.Waters.Add(new WaterVolume2D { Position = new V2(0, 0), Size = new V2(60, 10) });
                Func<double, double, BuoyancyShape2D> rect = (x, y) => { var bld = new ShapeBuilder2D(); bld.AddPolygon(new[] { new V2(-x / 2, -y / 2), new V2(x / 2, -y / 2), new V2(x / 2, y / 2), new V2(-x / 2, y / 2) }); return bld.Build(); };
                Func<BuoyancyShape2D, double, V2, double, SimBody2> add = (s, d, p, damping) =>
                {
                    var bd = new SimBody2();
                    bd.Core.AirDensity = 0.0;
                    bd.Core.WaveDamping = damping;
                    bd.SetUp(s, d, p, 0.0);
                    return w.Add(bd);
                };
                var crate = add(rect(0.4, 0.4), 500.0, new V2(-10, -0.03), 1.0);
                crate.Core.DragCoefficient = 0.0;
                crate.Core.KeepMomentumOnEntry = false;
                var crate0 = add(rect(0.4, 0.4), 500.0, new V2(-5, -0.03), 0.0);
                var plank = add(rect(1.6, 0.2), 400.0, new V2(0, 0.02 - 0.03), 1.0);
                var ys = new List<double>();
                Run2(w, 10.0, () => ys.Add(crate.Position.y));
                var crests = Crests(ys, 0.0);
                double measured = crests.Count >= 3 ? Math.Log(crests[0] / crests[2]) / (4.0 * Math.PI) : 0.0;
                Check("2D crate bobbing, no drag: damping ratio seen / formula", measured / BodyCore2D.WaveDampingRatio(2.0), 1.0, 0.1);
                Check("2D plank let go 3 cm low: settled after 10 s (mm)", 1000.0 * Math.Abs(plank.Position.y - 0.02), 0.0, 0.5);
                Check("2D crate without wave damping still bobbing after 10 s (mm, > 0.5)", 1000.0 * Math.Abs(crate0.Position.y) > 0.5 ? 1 : 0, 1, 0);
            }
        }

        static void Run2(SimWorld2 w, double seconds, Action each = null)
        {
            double t = 0.0;
            while (t < seconds - 1e-9)
            {
                w.Step();
                t += w.Dt;
                if (each != null)
                    each();
            }
        }
    }
}
