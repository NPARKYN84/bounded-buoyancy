// Bounded Buoyancy for Unity. Engine-free core. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System;
using System.Collections.Generic;

namespace BoundedBuoyancy.Core
{
    /// <summary>A ball touching the water or leaving it, during a step worked through by TryStepBall.</summary>
    public struct SurfaceTouch
    {
        public bool Entering;
        /// <summary>Where, on the surface (world).</summary>
        public V3 Point;
        /// <summary>Speed through the water then (m/s).</summary>
        public double Speed;
    }

    public sealed partial class BodyCore3D
    {
        /// <summary>Air drag of a fast ball (SphereDrag): changes with the speed through the sound barrier. Off, the
        /// air's drag coefficient is DragCoefficient, as in the water.</summary>
        public bool FastBallDrag;
        /// <summary>Speed of sound in the air, for FastBallDrag (m/s): 343 at 20 °C.</summary>
        public double SpeedOfSound = 343.0;

        // Results of TryStepBall
        /// <summary>How many times the ball has skipped off the water.</summary>
        public int Skips;
        /// <summary>The ball's velocity as it last left the water (world).</summary>
        public V3 ExitVelocity;
        /// <summary>Where TryStepBall leaves the ball's centre at the end of the step (world).</summary>
        public V3 BallCentre;
        /// <summary>The touches and skips of the last step worked through by TryStepBall.</summary>
        public readonly List<SurfaceTouch> Touches = new List<SurfaceTouch>();
        /// <summary>Whether the ball came to the planking in the last step worked through by StepBallToWall.</summary>
        public bool WallStruck;
        /// <summary>How far into that step it came to it (s); BallCentre is where its centre was then.</summary>
        public double WallTime;
        /// <summary>The ball's velocity as it came to it (world).</summary>
        public V3 WallVelocity;

        /// <summary>Each short step in the water moves the ball in or out by no more than this share of its depth.</summary>
        public const double SubStep = 0.2;

        double rate;   // how fast drag takes away the ball's speed, 1/s (WaterAccel)

        /// <summary>Drag coefficient in the air at `speed` (m/s through it).</summary>
        public double AirDragCoefficient(double speed)
        {
            if (!FastBallDrag || !Shape.SingleSphere)
                return DragCoefficient;   // a fast ball's drag is for a ball
            return SphereDrag.CoefficientAt(speed, 2.0 * Shape.Radius, AirDensityHere, SpeedOfSound);
        }

        /// <summary>
        /// A step in which a fast ball (one sphere, Bounded model) meets the water, worked through in short steps while
        /// it is in the water, each taking it no more than a fifth of its depth further in or out (second order, by the
        /// midpoint), with the water's push on the part of it under the surface (WaterImpact): a ball meeting the water
        /// at a shallow angle skips off it. Sets VelocityTarget, BallCentre (where the step leaves the ball's centre),
        /// Touches and the usual results, and returns true. Returns false, leaving the step to Step, when the ball is
        /// slow (Froude number below 1), wholly under the water, out of it all step, or where there is no water. The
        /// push across the ball's path comes in between Froude numbers 1 and 2.
        /// </summary>
        public bool TryStepBall(BodyPose pose, V3 velocity, WaterSample water, V3 wind, double g, double dt)
        {
            Touches.Clear();
            if (!Shape.SingleSphere || !Bounded || !water.HasWater || dt <= 0.0)
                return false;
            double a = Shape.Radius;
            V3 c = pose.Apply(Shape.SphereCentre);
            V3 flow = water.Flow;
            WaterSurface surface = water.Surface;
            if ((velocity - flow).LengthSquared < 2.0 * g * a)
                return false;
            double depth = surface.HeightAt(c) - (c.y - a);
            if (depth >= 2.0 * a)
                return false;
            if (depth <= 0.0)
            {
                // Out of the water: does it reach it this step? Its path's lowest point is at one end of it; over
                // waves, the surface between may reach as high as the crests.
                V3 end = c + velocity * dt + V3.Up * (-0.5 * g * dt * dt);
                double lowest = Math.Min(c.y, end.y) - a;
                if (surface.Wavy)
                {
                    if (lowest > surface.Level + surface.MaxWave)
                        return false;
                }
                else if (end.y - a > surface.HeightAt(end))
                {
                    return false;
                }
            }
            ThroughTheWater(c, velocity, water, wind, g, dt, false, V3.Zero, V3.Up);
            return true;
        }

        /// <summary>The ball is in the water already, without having crossed its surface (it has come out of a ship below
        /// the waterline): the next step reports no entry.</summary>
        public void InTheWaterAlready()
        {
            wasInWater = true;
            firstStep = false;
        }

        /// <summary>Whether a ball (one sphere) flying straight from `from` to `to` (its centre) is in the water on the
        /// way, or already in it: the water then takes it first.</summary>
        public bool MeetsWaterBefore(V3 from, V3 to, WaterSample water)
        {
            if (!Shape.SingleSphere || !water.HasWater)
                return false;
            double a = Shape.Radius;
            WaterSurface surface = water.Surface;
            for (int i = 0; i <= 8; i++)
            {
                V3 p = from + (to - from) * (i / 8.0);
                if (surface.HeightAt(p) - (p.y - a) > 0.0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// A step in which a fast ball (one sphere) meets the water on its way to planking, worked through as TryStepBall
        /// works it, but stopping where the ball comes to the planking: the plane through `wallCentre` (where its centre
        /// would be as it touches the planking, flying straight) square to `wallNormal` (the planking's outward normal).
        /// The planking's face is taken as flat over the little way the water turns the ball from its straight path. If
        /// it comes to it no slower than Planking.StrikeSpeed and no more glancingly than Planking.Glance, WallStruck is
        /// set, with WallTime, WallVelocity and BallCentre (where its centre is then), and the results are for a ball out
        /// of the water, as it goes into the planking; otherwise the step is worked through to its end, as TryStepBall
        /// does, and the engine meets the planking as it meets anything.
        /// </summary>
        public void StepBallToWall(BodyPose pose, V3 velocity, WaterSample water, V3 wind, double g, double dt, V3 wallCentre, V3 wallNormal)
        {
            Touches.Clear();
            ThroughTheWater(pose.Apply(Shape.SphereCentre), velocity, water, wind, g, dt, true, wallCentre, wallNormal);
        }

        void ThroughTheWater(V3 start, V3 vStart, WaterSample water, V3 wind, double g, double dt, bool hasWall, V3 wallCentre, V3 wallNormal)
        {
            double a = Shape.Radius, vol = Shape.Volume, m = OwnMass, rho = water.Density;
            V3 flow = water.Flow;
            WaterSurface surface = water.Surface;
            AirDensityHere = AirDensity;
            if (AirHalfHeight > 0.0)
                AirDensityHere *= Math.Pow(0.5, Math.Max(start.y / AirHalfHeight, -10.0));
            double air = AirDensityHere;
            WindHere = wind;
            double slope = WaveSlope(surface);
            V3 x = start, v = vStart;
            double t = 0.0, wetTime = 0.0;
            bool wet = wasInWater && !firstStep;   // as the last step left it, so each touch is reported once
            double prevAdded = prevAddedMass;       // -1 on a first step: nothing to share then
            WallStruck = false;
            WallTime = 0.0;
            WallVelocity = V3.Zero;
            for (int n = 0; n < 5000 && t < dt - 1e-12; n++)
            {
                double rest = dt - t;
                V3 x0 = x, v0 = v;
                double t0 = t;
                double level = surface.HeightAt(x);
                if (level - (x.y - a) <= 0.0)
                {
                    if (wet)
                    {
                        wet = false;
                        Skips++;
                        ExitVelocity = v;
                        Touches.Add(new SurfaceTouch { Entering = false, Point = new V3(x.x, level, x.z), Speed = (v - flow).Length });
                    }
                    // Clear of the water: fly on for as long as it surely stays clear, or to the end of the step.
                    double clear = (x.y - a) - level;
                    double closing = Math.Max(-v.y, 0.0) + slope * Math.Sqrt(v.x * v.x + v.z * v.z) + g * rest;
                    double h = closing <= 0.0 ? rest : Math.Min(rest, Math.Max(clear, 0.05 * a) / closing);
                    double miAir = m + AddedMassCoefficient * air * vol;
                    V3 ua = v - wind;
                    double ca = 0.5 * AirDragCoefficient(ua.Length) * Shape.Area * air * ua.Length * h / miAir;
                    v = (v + V3.Up * ((air * vol - m) * g / miAir * h) + ca * wind) / (1.0 + ca);
                    x += v * h;
                    t += h;
                    prevAdded = AddedMassCoefficient * air * vol;
                }
                else
                {
                    if (!wet)
                    {
                        wet = true;
                        Touches.Add(new SurfaceTouch { Entering = true, Point = new V3(x.x, level, x.z), Speed = (v - flow).Length });
                    }
                    // Depth measured square to the surface, which waves tilt: a ball meets the face of a wave steeply.
                    V3 normal = SurfaceNormal(surface, x, a);
                    double dd = Math.Min(Math.Max(a - (x.y - level) * normal.y, 0.0), 2.0 * a);
                    double under = Math.PI * dd * dd * (3.0 * a - dd) / 3.0;
                    double added = AddedMassCoefficient * (rho * under + air * (vol - under));
                    if (KeepMomentumOnEntry && prevAdded >= 0.0 && added > prevAdded)
                        v = flow + (v - flow) * ((m + prevAdded) / (m + added));   // water taken along starts with the water's own velocity
                    prevAdded = added;
                    if (dd >= 2.0 * a)
                    {
                        // Wholly under, the push across its path is nothing (the ball is round): the rest of the step as
                        // Step takes it, its drag worked out with the speed at the end; on its way to planking, a
                        // diameter at a time, so that it gets there at the speed it has then.
                        double mi = m + added;
                        double us = (v - flow).Length;
                        double h = hasWall ? Math.Min(rest, 2.0 * a / Math.Max(us, 1e-6)) : rest;
                        double cw = 0.5 * DragCoefficient * Shape.Area * rho * us * h / mi;
                        v = (v + V3.Up * ((rho * vol - m) * g / mi * h) + cw * flow) / (1.0 + cw);
                        x += v * h;
                        wetTime += h;
                        t += h;
                    }
                    else
                    {
                        V3 acc = WaterAccel(x, v, dd, normal, flow, wind, rho, air, g);
                        double speed = (v - flow).Length;
                        double reference = Math.Max(dd, 0.05 * a);
                        double step = Math.Min(rest, 10.0 * a / Math.Max(speed, 1e-6));
                        step = Math.Min(step, SubStep * reference / Math.Max(Math.Abs((v - flow).Dot(normal)), 1e-6));
                        step = Math.Min(step, Math.Sqrt(2.0 * SubStep * reference / Math.Max(Math.Abs(acc.Dot(normal)), 1e-9)));
                        step = Math.Min(step, 0.5 / Math.Max(rate, 1e-9));
                        V3 xm = x + v * (0.5 * step);
                        V3 vm = v + acc * (0.5 * step);
                        double lm = surface.HeightAt(xm);
                        V3 nm = SurfaceNormal(surface, xm, a);
                        double ddm = Math.Min(Math.Max(a - (xm.y - lm) * nm.y, 0.0), 2.0 * a);
                        V3 accm = WaterAccel(xm, vm, ddm, nm, flow, wind, rho, air, g);
                        x += vm * step;
                        v += accm * step;
                        t += step;
                        wetTime += step * under / vol;
                    }
                }
                if (hasWall)
                {
                    // Has it come to the planking?
                    double d1 = (x - wallCentre).Dot(wallNormal);
                    if (d1 <= 0.0)
                    {
                        double d0 = (x0 - wallCentre).Dot(wallNormal);
                        double f = d0 > 0.0 ? Math.Min(Math.Max(d0 / (d0 - d1), 0.0), 1.0) : 0.0;
                        V3 vw = v0 + (v - v0) * f;
                        double sw = vw.Length;
                        if (sw >= Planking.StrikeSpeed && -vw.Dot(wallNormal) >= Planking.Glance * sw)
                        {
                            x = x0 + (x - x0) * f;
                            v = vw;
                            t = t0 + (t - t0) * f;
                            WallStruck = true;
                            break;
                        }
                        hasWall = false;   // glancing, or slow: the engine meets it as it meets anything
                    }
                }
            }
            if (WallStruck)
            {
                // Into the planking, from the water or from the air after skipping: out of the sea inside it.
                WallTime = t;
                WallVelocity = v;
                SubmergedVolume = 0.0;
                CentreOfBuoyancy = x;
                AddedMass = AddedMassCoefficient * air * vol;
                InertialMass = m + AddedMass;
                FloodMass = 0.0;
                FloodInertia = new Sym3();
                VelocityTarget = v;
                LastAcceleration = (v - vStart) / dt;
                BallCentre = x;
                Torque = V3.Zero;
                HeaveDamping = 0.0;
                WaveAcceleration = 0.0;
                Crossing = Crossing.None;
                prevAddedMass = AddedMass;
                prevFlood = 0.0;
                prevSurface = double.NaN;
                wasInWater = false;
                firstStep = false;
                return;
            }
            // Where the step leaves it: a touch, or a skip, in its last moment counts too.
            double levelEnd = surface.HeightAt(x);
            if (levelEnd - (x.y - a) > 0.0 && !wet)
            {
                Touches.Add(new SurfaceTouch { Entering = true, Point = new V3(x.x, levelEnd, x.z), Speed = (v - flow).Length });
            }
            else if (levelEnd - (x.y - a) <= 0.0 && wet)
            {
                Skips++;
                ExitVelocity = v;
                Touches.Add(new SurfaceTouch { Entering = false, Point = new V3(x.x, levelEnd, x.z), Speed = (v - flow).Length });
            }
            double dEnd = Math.Min(Math.Max(levelEnd - (x.y - a), 0.0), 2.0 * a);
            SubmergedVolume = Math.PI * dEnd * dEnd * (3.0 * a - dEnd) / 3.0;
            double sunk = 3.0 * (2.0 * a - dEnd) * (2.0 * a - dEnd) / (4.0 * (3.0 * a - dEnd));   // the cap's centre below the ball's
            CentreOfBuoyancy = SubmergedVolume > 0.0 ? x + V3.Up * (-sunk) : x;
            AddedMass = AddedMassCoefficient * (rho * SubmergedVolume + air * (vol - SubmergedVolume));
            InertialMass = m + AddedMass;
            FloodMass = 0.0;
            FloodInertia = new Sym3();
            VelocityTarget = v;
            LastAcceleration = (v - vStart) / dt;
            BallCentre = x;
            Torque = V3.Zero;
            HeaveDamping = 0.0;
            WaveAcceleration = 0.0;
            Crossing = Crossing.None;
            double wetShare = wetTime / dt;
            AngularDamp = AngularDrag * (wetShare + (1.0 - wetShare) * Math.Min(1.0, air / Density));
            prevAddedMass = AddedMass;
            prevFlood = 0.0;
            prevSurface = double.NaN;
            wasInWater = SubmergedVolume > 0.0;
            firstStep = false;
        }

        /// <summary>Acceleration of a ball at `x` moving at `v`, `dd` deep in water whose surface there has the normal
        /// `normal`: weight, buoyancy, the water's push on the part under it and the air's drag on the rest, against the
        /// ball's mass and the water and air it takes along. Leaves in `rate` how fast drag takes away its speed.</summary>
        V3 WaterAccel(V3 x, V3 v, double dd, V3 normal, V3 flow, V3 wind, double rho, double air, double g)
        {
            double a = Shape.Radius, vol = Shape.Volume, m = OwnMass;
            double under = Math.PI * dd * dd * (3.0 * a - dd) / 3.0;
            double displaced = rho * under + air * (vol - under);
            double mi = m + AddedMassCoefficient * displaced;
            V3 acc = V3.Up * ((displaced - m) * g / mi);
            V3 u = v - flow;
            double speed = u.Length;
            // Drag in the water: as Step's, the area times the share of the ball under water.
            double dragArea = Shape.Area * under / vol;
            if (dd > 0.0 && speed > 1e-9)
            {
                double fast = Smoothstep(1.0, 2.0, speed / Math.Sqrt(2.0 * g * a));
                if (fast > 0.0)
                {
                    // The water's push on the part under the surface that faces into the ball's path: across the path,
                    // a lift; along it, a drag, scaled so that wholly under it is the ball's own drag.
                    double into = u.Dot(normal);
                    V3 skim = u - normal * into;
                    double skimSpeed = skim.Length;
                    V3 alongDir = skimSpeed > 1e-12 ? skim / skimSpeed : V3.Zero;
                    double pa, pu;
                    WaterImpact.Push(skimSpeed / speed, into / speed, 1.0 - dd / a, out pa, out pu);
                    V3 push = (alongDir * pa + normal * pu) * (-0.5 * rho * speed * speed * a * a);
                    V3 dir = u / speed;
                    double back = -push.Dot(dir);
                    acc += (push + dir * back) * (fast / mi);
                    dragArea += (back / (0.25 * rho * speed * speed) - dragArea) * fast;
                }
            }
            double rateWater = 0.5 * DragCoefficient * dragArea * rho * speed / mi;
            V3 ua = v - wind;
            double airSpeed = ua.Length;
            double rateAir = 0.5 * AirDragCoefficient(airSpeed) * Shape.Area * air * (vol - under) / vol * airSpeed / mi;
            rate = rateWater + rateAir;
            return acc - u * rateWater - ua * rateAir;
        }

        /// <summary>The surface's normal under the world point `p`, from its heights `radius` either side.</summary>
        static V3 SurfaceNormal(WaterSurface surface, V3 p, double radius)
        {
            if (!surface.Wavy)
                return V3.Up;
            double e = Math.Max(radius, 0.02);
            double dx = surface.HeightAt(p + new V3(e, 0.0, 0.0)) - surface.HeightAt(p - new V3(e, 0.0, 0.0));
            double dz = surface.HeightAt(p + new V3(0.0, 0.0, e)) - surface.HeightAt(p - new V3(0.0, 0.0, e));
            return new V3(-dx, 2.0 * e, -dz).Normalized();
        }

        /// <summary>How steeply the surface can slope: for the water's own waves, k H for each wave.</summary>
        static double WaveSlope(WaterSurface surface)
        {
            if (!surface.Wavy)
                return 0.0;
            if (surface.Source != null)
                return 0.3;
            WaveSet w = surface.Waves;
            double k1 = Math.Sqrt(w.K1x * w.K1x + w.K1z * w.K1z), k2 = Math.Sqrt(w.K2x * w.K2x + w.K2z * w.K2z);
            return w.Height * (k1 + WaveSet.SecondHeight * k2);
        }

        static double Smoothstep(double from, double to, double x)
        {
            double s = Math.Min(Math.Max((x - from) / (to - from), 0.0), 1.0);
            return s * s * (3.0 - 2.0 * s);
        }
    }
}
