// Bounded Buoyancy for Unity. Engine-free core, 2D (side view, y up, metres). Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System;
using System.Collections.Generic;

namespace BoundedBuoyancy.Core
{
    /// <summary>A touch of the water by a ball in a side view: entering or leaving, where, and its speed.</summary>
    public struct SurfaceTouch2D
    {
        public bool Entering;
        public V2 Point;
        public double Speed;
    }

    /// <summary>
    /// A ball in fast flight in a side view (round shot): the same physics as a fast BodyCore3D ball, worked out as a
    /// ball though it is drawn as a circle. In the air its drag changes with its speed (SphereDrag); meeting the water,
    /// the water pushes on the part of it under the surface that faces into its path (WaterImpact, Birkhoff's), so it
    /// skips when it comes down at a shallow angle; wholly under, its drag is a body's. A physics step is worked through
    /// in short steps (Advance), short enough near the water that the ball moves into or out of it by no more than a
    /// fifth of how deep it is.
    /// </summary>
    public sealed class BallFlight2D
    {
        public double Radius = 0.07;
        public double Mass = 11.0;
        public double AddedMassCoefficient = 0.5;
        /// <summary>Drag coefficient in the water.</summary>
        public double DragCoefficient = 0.47;
        public double AirDensity = 1.2;
        public double SpeedOfSound = 343.0;
        /// <summary>Skip off the water at shallow angles.</summary>
        public bool Ricochet = true;

        /// <summary>Whether the ball is touching the water now.</summary>
        public bool Wet;
        /// <summary>How many times it has left the water.</summary>
        public int Skips;
        /// <summary>Its velocity as it last left the water.</summary>
        public V2 ExitVelocity;
        /// <summary>Mach number of its flight through the air, last short step.</summary>
        public double Mach;
        /// <summary>Touches of the water in the steps since this was last cleared.</summary>
        public readonly List<SurfaceTouch2D> Touches = new List<SurfaceTouch2D>();

        public const double SubStep = 0.2;
        double rate;

        public double Volume { get { return 4.0 / 3.0 * Math.PI * Radius * Radius * Radius; } }

        /// <summary>Whether a ball at `velocity` is moving fast enough to be worked through in short steps: faster than it
        /// would fall through its own radius.</summary>
        public bool IsFast(V2 velocity, double g)
        {
            return velocity.LengthSquared >= 2.0 * g * Radius;
        }

        /// <summary>How deep (0 to 2 radii) the ball with its centre at `x` is in water whose surface is `level` (or none,
        /// at -infinity).</summary>
        public double DepthIn(V2 x, double level)
        {
            if (double.IsNegativeInfinity(level))
                return 0.0;
            return Math.Max(0.0, Math.Min(2.0 * Radius, level - (x.y - Radius)));
        }

        static double LevelOf(WaterSample2D w, double x)
        {
            return w.HasWater ? w.Surface.HeightAt(x) : double.NegativeInfinity;
        }

        static V2 NormalAt(Func<V2, WaterSample2D> waterAt, V2 x)
        {
            const double h = 0.05;
            double l0 = LevelOf(waterAt(x - new V2(h, 0)), x.x - h);
            double l1 = LevelOf(waterAt(x + new V2(h, 0)), x.x + h);
            if (double.IsInfinity(l0) || double.IsInfinity(l1))
                return new V2(0, 1);
            V2 n = new V2(-(l1 - l0) / (2.0 * h), 1.0);
            return n / n.Length;
        }

        /// <summary>One short step from `x` at `v`, of no more than `rest` seconds, with the water at a point given by
        /// `waterAt` and gravity `g`. Moves x and v on, and returns the time it took.</summary>
        public double Advance(ref V2 x, ref V2 v, Func<V2, WaterSample2D> waterAt, double g, double rest)
        {
            double a = Radius, m = Mass, vol = Volume, area = Math.PI * a * a, air = AirDensity;
            WaterSample2D w = waterAt(x);
            double level = LevelOf(w, x.x);
            double rho = w.Density;
            V2 flow = w.HasWater ? w.Flow : V2.Zero;
            double dd = DepthIn(x, level);
            double h;
            if (dd <= 0.0 || !Ricochet)
            {
                if (Wet)
                {
                    Wet = false;
                    Skips++;
                    ExitVelocity = v;
                    Touches.Add(new SurfaceTouch2D { Entering = false, Point = new V2(x.x, level), Speed = (v - flow).Length });
                }
                // Clear of the water: fly on for as long as it surely stays clear, or to the end of the step.
                double clear = double.IsNegativeInfinity(level) ? double.PositiveInfinity : (x.y - a) - level;
                double closing = Math.Max(-v.y, 0.0) + g * rest;
                h = closing <= 0.0 || double.IsInfinity(clear) ? rest : Math.Min(rest, Math.Max(clear, 0.05 * a) / closing);
                h = Math.Min(h, 20.0 * a / Math.Max(v.Length, 1e-6));
                double mi = m + AddedMassCoefficient * air * vol;
                double s = v.Length;
                Mach = s / Math.Max(SpeedOfSound, 1.0);
                double ca = 0.5 * SphereDrag.CoefficientAt(s, 2.0 * a, air, SpeedOfSound) * area * air * s * h / mi;
                V2 v1 = (v + new V2(0.0, (air * vol - m) * g / mi * h)) / (1.0 + ca);
                x = x + v1 * h;
                v = v1;
                return h;
            }
            if (dd >= 2.0 * a)
            {
                // Wholly under: its drag as a body's.
                double mi = m + AddedMassCoefficient * rho * vol;
                V2 u = v - flow;
                h = Math.Min(rest, 4.0 * a / Math.Max(u.Length, 1e-6));
                double cw = 0.5 * DragCoefficient * area * rho * u.Length * h / mi;
                V2 v1 = (v + new V2(0.0, (rho * vol - m) * g / mi * h) + cw * flow) / (1.0 + cw);
                x = x + v1 * h;
                v = v1;
                return h;
            }
            if (!Wet)
            {
                Wet = true;
                Touches.Add(new SurfaceTouch2D { Entering = true, Point = new V2(x.x, level), Speed = (v - flow).Length });
            }
            // A short step, second order (midpoint).
            V2 normal = NormalAt(waterAt, x);
            V2 acc = Accel(v, dd, normal, flow, rho, g);
            double speed = (v - flow).Length;
            double refDepth = Math.Max(dd, 0.05 * a);
            h = Math.Min(rest, 10.0 * a / Math.Max(speed, 1e-6));
            h = Math.Min(h, SubStep * refDepth / Math.Max(Math.Abs((v - flow).Dot(normal)), 1e-6));
            h = Math.Min(h, Math.Sqrt(2.0 * SubStep * refDepth / Math.Max(Math.Abs(acc.Dot(normal)), 1e-9)));
            h = Math.Min(h, 0.5 / Math.Max(rate, 1e-9));
            V2 xm = x + v * (0.5 * h);
            V2 vm = v + acc * (0.5 * h);
            double ddm = DepthIn(xm, LevelOf(waterAt(xm), xm.x));
            V2 accm = Accel(vm, ddm, NormalAt(waterAt, xm), flow, rho, g);
            x = x + vm * h;
            v = v + accm * h;
            return h;
        }

        /// <summary>Acceleration of the ball moving at `v`, `dd` deep in water whose surface has `normal`.</summary>
        V2 Accel(V2 v, double dd, V2 normal, V2 flow, double rho, double g)
        {
            double a = Radius, m = Mass, vol = Volume, area = Math.PI * a * a, air = AirDensity;
            double under = Math.PI * dd * dd * (3.0 * a - dd) / 3.0;
            double displaced = rho * under + air * (vol - under);
            double mi = m + AddedMassCoefficient * displaced;
            V2 acc = new V2(0.0, (displaced - m) * g / mi);
            V2 u = v - flow;
            double speed = u.Length;
            double dragArea = area * under / vol;
            if (dd > 0.0 && speed > 1e-9)
            {
                double t = Math.Max(0.0, Math.Min(1.0, speed / Math.Sqrt(2.0 * g * a) - 1.0));
                double fast = t * t * (3.0 - 2.0 * t);
                if (fast > 0.0)
                {
                    double into = u.Dot(normal);
                    V2 skim = u - normal * into;
                    double skimSpeed = skim.Length;
                    V2 alongDir = skimSpeed > 1e-12 ? skim / skimSpeed : V2.Zero;
                    double pa, pu;
                    WaterImpact.Push(skimSpeed / speed, into / speed, 1.0 - dd / a, out pa, out pu);
                    V2 push = (alongDir * pa + normal * pu) * (-0.5 * rho * speed * speed * a * a);
                    V2 dir = u / speed;
                    double back = -push.Dot(dir);
                    acc += (push + dir * back) * (fast / mi);
                    dragArea += (back / (0.25 * rho * speed * speed) - dragArea) * fast;
                }
            }
            double rateWater = 0.5 * DragCoefficient * dragArea * rho * speed / mi;
            double airSpeed = v.Length;
            double rateAir = 0.5 * SphereDrag.CoefficientAt(airSpeed, 2.0 * a, air, SpeedOfSound) * area * air * (vol - under) / vol * airSpeed / mi;
            rate = rateWater + rateAir;
            return acc - u * rateWater - v * rateAir;
        }
    }
}
