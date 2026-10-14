// Bounded Buoyancy for Unity. Engine-free core. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System;
using System.Collections.Generic;

namespace BoundedBuoyancy.Core
{
    /// <summary>A hole in a compartment: its position in the compartment's own coordinates and its area (m²).</summary>
    public struct Hole
    {
        public V3 Position;
        public double Area;

        public Hole(V3 position, double area)
        {
            Position = position;
            Area = area;
        }
    }

    /// <summary>
    /// A space inside a body that can flood: a box at a pose in the body's own coordinates. Water pours in through
    /// holes below the waterline by the orifice law Q = C A √(2 g Δh), Δh the difference between the water's depth
    /// outside and inside at the hole, and drains out through holes above it. The water inside stays level whichever
    /// way the body leans; it is weight the body carries at the water's own centre (the added-weight method of naval
    /// architecture), so a holed ship settles, lists toward its flooded side and sinks or capsizes. The compartment is
    /// taken to be vented: air gets out as water comes in.
    /// </summary>
    public sealed class Compartment3D
    {
        /// <summary>Where the box's centre is, and how it is turned, in the body's own coordinates.</summary>
        public BodyPose Pose = new BodyPose(V3.Zero, M3.Identity);
        /// <summary>Width (x), height (y) and length (z), in metres.</summary>
        public V3 Size = new V3(1, 1, 1);
        public readonly List<Hole> Holes = new List<Hole>();
        /// <summary>Discharge coefficient of the holes: 0.6 for a sharp-edged hole.</summary>
        public double DischargeCoefficient = 0.6;
        /// <summary>Water the pumps take out, m³/s.</summary>
        public double PumpRate;
        /// <summary>Water inside now, m³.</summary>
        public double FloodVolume;

        // Results of the last step
        /// <summary>Flow in through the holes over the last step, m³/s (negative when it drains).</summary>
        public double Inflow;
        /// <summary>Height (world y) of the water inside.</summary>
        public double WaterLevel;
        /// <summary>Centre of the water inside (world).</summary>
        public V3 WaterCentre;

        public double Capacity { get { return Size.x * Size.y * Size.z; } }

        readonly V3[] corners = new V3[8];

        // The box's twelve faces as corner indices, wound so that each faces outward. Corner i is at (±x, ±y, ±z) with
        // bit 0 for +x, bit 1 for +y and bit 2 for +z.
        static readonly int[] Faces = { 0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4,
            2, 6, 7, 2, 7, 3, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5 };

        /// <summary>World positions of the box's corners, for the box at `pose` (world). A mirrored box (a basis with a
        /// negative determinant) has its corners taken in mirrored order, so its faces still wind outward.</summary>
        public V3[] Corners(BodyPose pose)
        {
            double hx = 0.5 * Size.x, hy = 0.5 * Size.y, hz = 0.5 * Size.z;
            if (pose.Basis.Determinant < 0.0)
                hx = -hx;
            for (int i = 0; i < 8; i++)
                corners[i] = pose.Apply(new V3((i & 1) != 0 ? hx : -hx, (i & 2) != 0 ? hy : -hy, (i & 4) != 0 ? hz : -hz));
            return corners;
        }

        /// <summary>Volume of the box with corners `c` (world) below the world height `level`, and its centre.</summary>
        public static double Below(V3[] c, double level, out V3 centre)
        {
            double lo = double.PositiveInfinity;
            V3 mid = V3.Zero;
            for (int i = 0; i < 8; i++)
            {
                lo = Math.Min(lo, c[i].y);
                mid += c[i];
            }
            mid = mid / 8.0;
            centre = new V3(mid.x, level, mid.z);
            if (level <= lo)
                return 0.0;
            var p = new V3(mid.x, level, mid.z);
            double vol = 0.0, mx = 0.0, my = 0.0, mz = 0.0;
            for (int i = 0; i < 36; i += 3)
            {
                V3 a = c[Faces[i]], b = c[Faces[i + 1]], d = c[Faces[i + 2]];
                double da = level - a.y, db = level - b.y, dd = level - d.y;
                int under = (da > 0.0 ? 1 : 0) + (db > 0.0 ? 1 : 0) + (dd > 0.0 ? 1 : 0);
                if (under == 0)
                    continue;
                if (under == 3)
                {
                    double t3 = Tet(p, a, b, d);
                    V3 s3 = p + a + b + d;
                    vol += t3;
                    mx += t3 * s3.x; my += t3 * s3.y; mz += t3 * s3.z;
                    continue;
                }
                // Turn the triangle (keeping its winding) so that the odd vertex out comes first.
                if ((under == 1 && db > 0.0) || (under == 2 && db <= 0.0))
                {
                    V3 tv = a; a = b; b = d; d = tv;
                    double td = da; da = db; db = dd; dd = td;
                }
                else if ((under == 1 && dd > 0.0) || (under == 2 && dd <= 0.0))
                {
                    V3 tv = d; d = b; b = a; a = tv;
                    double td = dd; dd = db; db = da; da = td;
                }
                V3 ab = a + (b - a) * (da / (da - db));
                V3 da2 = d + (a - d) * (dd / (dd - da));
                if (under == 1)
                {
                    double t1 = Tet(p, a, ab, da2);
                    V3 s1 = p + a + ab + da2;
                    vol += t1;
                    mx += t1 * s1.x; my += t1 * s1.y; mz += t1 * s1.z;
                }
                else
                {
                    double t2a = Tet(p, ab, b, d), t2b = Tet(p, ab, d, da2);
                    V3 sa = p + ab + b + d, sb = p + ab + d + da2;
                    vol += t2a + t2b;
                    mx += t2a * sa.x + t2b * sb.x;
                    my += t2a * sa.y + t2b * sb.y;
                    mz += t2a * sa.z + t2b * sb.z;
                }
            }
            if (vol > 1e-15)
                centre = new V3(mx, my, mz) / (4.0 * vol);
            return vol;
        }

        /// <summary>The height (world y) of the water's surface when `volume` m³ lies level in the box with corners `c`,
        /// and the water's centre.</summary>
        public static double LevelFor(V3[] c, double volume, out V3 centre)
        {
            double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            V3 mid = V3.Zero;
            for (int i = 0; i < 8; i++)
            {
                lo = Math.Min(lo, c[i].y);
                hi = Math.Max(hi, c[i].y);
                mid += c[i];
            }
            mid = mid / 8.0;
            if (volume <= 0.0)
            {
                centre = new V3(mid.x, lo, mid.z);
                return lo;
            }
            V3 ignore;
            double full = Below(c, hi + 1.0, out ignore);
            if (volume >= full)
            {
                centre = mid;
                return hi;
            }
            // The volume below a level grows smoothly and steadily with it: the Illinois method (regula falsi that
            // halves the stale end) finds the level in a handful of steps.
            double a = lo, fa = -volume, b = hi, fb = full - volume, level = 0.5 * (lo + hi);
            int side = 0;
            centre = mid;
            for (int n = 0; n < 60; n++)
            {
                level = (a * fb - b * fa) / (fb - fa);
                double f = Below(c, level, out centre) - volume;
                if (Math.Abs(f) <= 1e-10 * full || b - a <= 1e-9 * (hi - lo))
                    break;
                if (f * fb > 0.0)
                {
                    b = level;
                    fb = f;
                    if (side == -1)
                        fa *= 0.5;
                    side = -1;
                }
                else
                {
                    a = level;
                    fa = f;
                    if (side == 1)
                        fb *= 0.5;
                    side = 1;
                }
            }
            return level;
        }

        /// <summary>One step of `dt` for the compartment at `pose` (world), with water whose surface is at
        /// `surface(p)` for a world point p (NaN where there is none), of density `rho`, under gravity `g`. Moves water
        /// through the holes and pumps, and returns the water's mass; its centre is in WaterCentre.</summary>
        public double Step(BodyPose pose, Func<V3, double> surface, double rho, double g, double dt)
        {
            V3[] c = Corners(pose);
            double full = Capacity * Math.Abs(pose.Basis.Determinant);
            FloodVolume = Math.Min(Math.Max(FloodVolume, 0.0), full);
            double before = FloodVolume;
            V3 centre;
            double level = LevelFor(c, FloodVolume, out centre);
            double vol = FloodVolume;
            for (int k = 0; k < Holes.Count; k++)
            {
                Hole h = Holes[k];
                if (h.Area <= 0.0)
                    continue;
                V3 at = pose.Apply(h.Position);
                double outside = surface(at);
                double dOut = double.IsNaN(outside) ? 0.0 : Math.Max(outside - at.y, 0.0);
                double dIn = before > 0.0 ? Math.Max(level - at.y, 0.0) : 0.0;
                double head = dOut - dIn;
                if (Math.Abs(head) < 1e-9)
                    continue;
                double q = DischargeCoefficient * h.Area * Math.Sqrt(2.0 * g * Math.Abs(head)) * dt;
                V3 ignore;
                // Never past the level at which the flow through this hole would stop: the water outside when filling;
                // the water outside, or the hole itself, when draining.
                if (head > 0.0)
                {
                    double stop = Below(c, outside, out ignore);
                    vol = Math.Max(vol, Math.Min(vol + q, stop));
                }
                else
                {
                    double stopLevel = double.IsNaN(outside) ? at.y : Math.Max(outside, at.y);
                    double stop = Below(c, stopLevel, out ignore);
                    vol = Math.Min(vol, Math.Max(vol - q, stop));
                }
            }
            if (PumpRate > 0.0)
                vol -= PumpRate * dt;
            FloodVolume = Math.Min(Math.Max(vol, 0.0), full);
            Inflow = dt > 0.0 ? (FloodVolume - before) / dt : 0.0;
            if (FloodVolume != before)
                level = LevelFor(c, FloodVolume, out centre);
            WaterLevel = level;
            WaterCentre = centre;
            return rho * FloodVolume;
        }

        static double Tet(V3 p, V3 a, V3 b, V3 c)
        {
            return (a - p).Dot((b - p).Cross(c - p)) / 6.0;
        }
    }
}
