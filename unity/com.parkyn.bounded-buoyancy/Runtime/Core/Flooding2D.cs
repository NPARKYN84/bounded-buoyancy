// Bounded Buoyancy for Unity. Engine-free core. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System;
using System.Collections.Generic;

namespace BoundedBuoyancy.Core
{
    /// <summary>A hole in a 2D compartment: its position in the compartment's own coordinates and its area (m²).</summary>
    public struct Hole2D
    {
        public V2 Position;
        public double Area;

        public Hole2D(V2 position, double area)
        {
            Position = position;
            Area = area;
        }
    }

    /// <summary>
    /// A space inside a 2D body that can flood, in a side view: a rectangle at a pose in the body's own coordinates, as
    /// deep into the screen as the body. As Compartment3D: water pours in through holes below the waterline by the
    /// orifice law Q = C A √(2 g Δh) and drains out through holes above it; the water inside lies level, and is weight
    /// the body carries at its own centre. Metres, y up.
    /// </summary>
    public sealed class Compartment2D
    {
        /// <summary>Centre of the rectangle in the body's own coordinates.</summary>
        public V2 Position;
        /// <summary>Its turn relative to the body, radians.</summary>
        public double Angle;
        /// <summary>Width and height, metres.</summary>
        public V2 Size = new V2(1, 1);
        public readonly List<Hole2D> Holes = new List<Hole2D>();
        public double DischargeCoefficient = 0.6;
        /// <summary>Area of a hatch in the top of the space, m² (0: closed): once it goes under, the sea pours in through
        /// it as through a hole.</summary>
        public double HatchArea;
        /// <summary>Water the pumps take out, m³/s.</summary>
        public double PumpRate;
        /// <summary>Water inside now, m³.</summary>
        public double FloodVolume;

        /// <summary>Flow in through the holes over the last step, m³/s (negative when it drains).</summary>
        public double Inflow;
        /// <summary>Height (world y) of the water inside.</summary>
        public double WaterLevel;
        /// <summary>Centre of the water inside (world).</summary>
        public V2 WaterCentre;

        readonly V2[] corners = new V2[4];
        readonly List<V2> clip = new List<V2>(8);

        /// <summary>Volume of the space for a body `thickness` m deep (m³).</summary>
        public double Capacity(double thickness) { return Size.x * Size.y * thickness; }

        /// <summary>World corners of the rectangle at `pose` (world), in order round it.</summary>
        public V2[] Corners(BodyPose2D pose)
        {
            V2 h = 0.5 * Size;
            corners[0] = pose.Apply(new V2(-h.x, -h.y));
            corners[1] = pose.Apply(new V2(h.x, -h.y));
            corners[2] = pose.Apply(new V2(h.x, h.y));
            corners[3] = pose.Apply(new V2(-h.x, h.y));
            return corners;
        }

        /// <summary>The compartment's world pose for a body at `body`.</summary>
        public BodyPose2D WorldPose(BodyPose2D body)
        {
            double a = Math.Atan2(body.Sin, body.Cos) + Angle;
            return new BodyPose2D(body.Apply(Position), a);
        }

        /// <summary>Area (m²) of the polygon `c` below the height `level`, and its centre.</summary>
        public double Below(V2[] c, double level, out V2 centre)
        {
            clip.Clear();
            int n = c.Length;
            for (int i = 0; i < n; i++)
            {
                V2 a = c[i], b = c[(i + 1) % n];
                bool aIn = a.y <= level, bIn = b.y <= level;
                if (aIn)
                    clip.Add(a);
                if (aIn != bIn)
                    clip.Add(a + (b - a) * ((level - a.y) / (b.y - a.y)));
            }
            centre = V2.Zero;
            if (clip.Count < 3)
                return 0.0;
            double area = 0.0;
            V2 m = V2.Zero;
            for (int i = 0; i < clip.Count; i++)
            {
                V2 p = clip[i], q = clip[(i + 1) % clip.Count];
                double cr = p.Cross(q);
                area += cr;
                m += (p + q) * cr;
            }
            if (Math.Abs(area) < 1e-15)
                return 0.0;
            centre = m / (3.0 * area);
            return Math.Abs(0.5 * area);
        }

        /// <summary>The height of the water's surface when `area` m² of water lies level in the polygon `c`, and the
        /// water's centre (Illinois method).</summary>
        public double LevelFor(V2[] c, double area, out V2 centre)
        {
            double top = double.NegativeInfinity, bottom = double.PositiveInfinity;
            V2 mid = V2.Zero;
            foreach (V2 p in c)
            {
                top = Math.Max(top, p.y);
                bottom = Math.Min(bottom, p.y);
                mid += p;
            }
            mid = mid / c.Length;
            if (area <= 0.0)
            {
                centre = new V2(mid.x, bottom);
                return bottom;
            }
            V2 ignore;
            double full = Below(c, top + 1.0, out ignore);
            if (area >= full)
            {
                centre = mid;
                return top;
            }
            double a = bottom, fa = -area, b = top, fb = full - area;
            int side = 0;
            double level = 0.5 * (top + bottom), got = 0.0;
            centre = mid;
            for (int k = 0; k < 60; k++)
            {
                level = (a * fb - b * fa) / (fb - fa);
                got = Below(c, level, out centre);
                double f = got - area;
                if (Math.Abs(f) <= 1e-10 * full || Math.Abs(b - a) <= 1e-12 * (top - bottom))
                    break;
                if (f * fb > 0.0)
                {
                    b = level; fb = f;
                    if (side == -1) fa *= 0.5;
                    side = -1;
                }
                else
                {
                    a = level; fa = f;
                    if (side == 1) fb *= 0.5;
                    side = 1;
                }
            }
            if (got <= 1e-15)
                centre = new V2(mid.x, level);
            return level;
        }

        /// <summary>Make a hole of `area` m² at the world point `at` for the compartment at `pose` (world), moved onto
        /// the rectangle if it lies outside it. Returns its index.</summary>
        public int AddHole(BodyPose2D pose, V2 at, double area)
        {
            V2 l = pose.Inverse(at);
            V2 h = 0.5 * Size;
            l = new V2(Math.Max(-h.x, Math.Min(h.x, l.x)), Math.Max(-h.y, Math.Min(h.y, l.y)));
            Holes.Add(new Hole2D(l, Math.Max(area, 0.0)));
            return Holes.Count - 1;
        }

        /// <summary>Distance from the world point `at` to the rectangle at `pose` (0 inside).</summary>
        public double DistanceTo(BodyPose2D pose, V2 at)
        {
            V2 l = pose.Inverse(at);
            V2 h = 0.5 * Size;
            V2 c = new V2(Math.Max(-h.x, Math.Min(h.x, l.x)), Math.Max(-h.y, Math.Min(h.y, l.y)));
            return (l - c).Length;
        }

        /// <summary>One step of `dt` for the compartment at `pose` (world) in a body `thickness` m deep, with water whose
        /// surface is at `surface(p)` (NaN where there is none), of density `rho`, under gravity `g`. Returns the water's
        /// mass; its centre is in WaterCentre.</summary>
        public double Step(BodyPose2D pose, Func<V2, double> surface, double rho, double g, double dt, double thickness)
        {
            V2[] c = Corners(pose);
            double full = Capacity(thickness);
            FloodVolume = Math.Min(Math.Max(FloodVolume, 0.0), full);
            double before = FloodVolume;
            V2 centre;
            double level = LevelFor(c, FloodVolume / thickness, out centre);
            double vol = FloodVolume;
            V2 ignore;
            int count = Holes.Count + (HatchArea > 0.0 ? 1 : 0);
            for (int k = 0; k < count; k++)
            {
                Hole2D h = k < Holes.Count ? Holes[k] : new Hole2D(new V2(0.0, 0.5 * Size.y), HatchArea);
                if (h.Area <= 0.0)
                    continue;
                V2 at = pose.Apply(h.Position);
                double outside = surface(at);
                double dOut = double.IsNaN(outside) ? 0.0 : Math.Max(outside - at.y, 0.0);
                double dIn = before > 0.0 ? Math.Max(level - at.y, 0.0) : 0.0;
                double head = dOut - dIn;
                if (Math.Abs(head) < 1e-9)
                    continue;
                double q = DischargeCoefficient * h.Area * Math.Sqrt(2.0 * g * Math.Abs(head)) * dt;
                if (head > 0.0)
                {
                    double stop = Below(c, outside, out ignore) * thickness;
                    vol = Math.Max(vol, Math.Min(vol + q, stop));
                }
                else
                {
                    double stopLevel = double.IsNaN(outside) ? at.y : Math.Max(outside, at.y);
                    double stop = Below(c, stopLevel, out ignore) * thickness;
                    vol = Math.Min(vol, Math.Max(vol - q, stop));
                }
            }
            if (PumpRate > 0.0)
                vol -= PumpRate * dt;
            FloodVolume = Math.Min(Math.Max(vol, 0.0), full);
            Inflow = dt > 0.0 ? (FloodVolume - before) / dt : 0.0;
            if (FloodVolume != before)
                level = LevelFor(c, FloodVolume / thickness, out centre);
            WaterLevel = level;
            WaterCentre = centre;
            return rho * FloodVolume;
        }
    }
}
