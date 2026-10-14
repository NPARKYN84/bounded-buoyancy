// Bounded Buoyancy for Unity. Engine-free core, 2D (side view, y up, metres).
// Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System;
using System.Collections.Generic;

namespace BoundedBuoyancy.Core
{
    /// <summary>Two travelling sine waves in a side view, with deep-water speeds.</summary>
    public struct WaveSet2D
    {
        public double Height, K1, K2, W1, W2;

        public static WaveSet2D Create(double height, double length, double g)
        {
            length = Math.Max(length, 0.01);
            double k1 = 2.0 * Math.PI / length;
            double k2 = 2.0 * Math.PI / (length * WaveSet.SecondLength);
            g = Math.Max(g, 0.0);
            return new WaveSet2D { Height = Math.Max(height, 0.0), K1 = k1, K2 = k2, W1 = Math.Sqrt(g * k1), W2 = Math.Sqrt(g * k2) };
        }

        public double Offset(double lx, double t)
        {
            if (Height <= 0.0)
                return 0.0;
            return Height * (Math.Sin(K1 * lx - W1 * t) + WaveSet.SecondHeight * Math.Sin(K2 * lx - W2 * t + WaveSet.SecondPhase));
        }

        public double MaxWave { get { return Height * (1.0 + WaveSet.SecondHeight); } }
    }

    public struct WaterSurface2D
    {
        public double Level;
        public WaveSet2D Waves;
        public double Time;
        /// <summary>x of the water's middle; the waves are measured from it.</summary>
        public double OriginX;

        public bool Wavy { get { return Waves.Height > 0.0; } }
        public double MaxWave { get { return Wavy ? Waves.MaxWave : 0.0; } }

        public static WaterSurface2D Flat(double level) { return new WaterSurface2D { Level = level }; }

        /// <summary>Height (y) of the surface above the position x.</summary>
        public double HeightAt(double x)
        {
            if (Waves.Height <= 0.0)
                return Level;
            return Level + Waves.Offset(x - OriginX, Time);
        }
    }

    public struct WaterSample2D
    {
        public bool HasWater;
        public double Density;
        public V2 Flow;
        public WaterSurface2D Surface;
        /// <summary>The water this is (its WaterVolume2D, say), so a body can tell when it has moved into other water.</summary>
        public object Owner;

        public static WaterSample2D Flat(double level, double density)
        {
            return new WaterSample2D { HasWater = true, Density = density, Surface = WaterSurface2D.Flat(level) };
        }

        public static WaterSample2D None(double level, double density)
        {
            return new WaterSample2D { HasWater = false, Density = density, Surface = WaterSurface2D.Flat(level) };
        }
    }

    /// <summary>A rectangle of water below Position (the middle of its surface): width and depth, density, waves
    /// and a current.</summary>
    public sealed class WaterVolume2D
    {
        public V2 Position;
        public V2 Size = new V2(12, 3);
        public double Density = 1000.0;
        public V2 Current;
        public double Time;

        WaveSet2D waves;
        double waveHeight = -1.0, waveLength, waveGravity;

        public WaveSet2D Waves { get { return waves; } }

        public void SetWaves(double height, double length, double g)
        {
            if (height == waveHeight && length == waveLength && g == waveGravity)
                return;
            waveHeight = height;
            waveLength = length;
            waveGravity = g;
            waves = WaveSet2D.Create(height, length, g);
        }

        public WaterSurface2D Surface
        {
            get { return new WaterSurface2D { Level = Position.y, Waves = waves, Time = Time, OriginX = Position.x }; }
        }

        public double HeightAt(double x) { return Surface.HeightAt(x); }

        public double MaxWave { get { return waves.MaxWave; } }

        /// <summary>Whether the point lies over or in the water: inside it across, and above its floor.</summary>
        public bool Contains(V2 p)
        {
            V2 l = p - Position;
            return Math.Abs(l.x) <= 0.5 * Size.x && l.y >= -Size.y;
        }

        public V2 CurrentAt(V2 p) { return Current; }

        public WaterSample2D Sample(V2 p)
        {
            return new WaterSample2D { HasWater = true, Density = Density, Flow = CurrentAt(p), Surface = Surface, Owner = this };
        }
    }

    /// <summary>Wind in a side view, with gusts that travel downwind.</summary>
    public sealed class WindField2D
    {
        public V2 Position;
        public V2 Velocity = new V2(4, 0);
        public double GustStrength = 0.3;
        public double GustPeriod = 5.0;
        public bool Everywhere = true;
        public V2 Size = new V2(10, 10);
        public double Time;

        public V2 VelocityAt(V2 p)
        {
            if (!Everywhere)
            {
                V2 l = p - Position;
                if (Math.Abs(l.x) > 0.5 * Size.x || Math.Abs(l.y) > 0.5 * Size.y)
                    return V2.Zero;
            }
            double speed2 = Velocity.LengthSquared;
            if (speed2 < 1e-12 || GustStrength <= 0.0)
                return Velocity;
            double s = (Time - p.Dot(Velocity) / speed2) / Math.Max(GustPeriod, 0.01);
            return Velocity * (1.0 + GustStrength * WindField.Pattern(s));
        }
    }

    /// <summary>The outline of a 2D body in its own coordinates: polygons (each with a factor that keeps curved
    /// parts at their exact area), or a single circle, cut exactly.</summary>
    public sealed class BuoyancyShape2D
    {
        public bool Circle;
        public double Radius;
        public V2 CircleCentre;
        public V2[][] Polygons = new V2[0][];
        public double[] Scales = new double[0];
        public double Area;
        public V2 Centroid;
        public double BoundRadius;
        /// <summary>Size of the bounding box, body axes.</summary>
        public V2 Box;
        /// <summary>How much of the bounding box the shape fills.</summary>
        public double Fill = 1.0;

        internal V2[] scratchPts = new V2[0];
        internal double[] scratchDepth = new double[0];
        internal V2[] scratchClip = new V2[0];

        public bool IsEmpty { get { return Area <= 0.0; } }

        /// <summary>Length the body shows to a flow along `along` (world): its bounding box seen at that angle,
        /// times its fill.</summary>
        public double DragLength(V2 along, BodyPose2D pose)
        {
            if (Circle)
                return 2.0 * Radius;
            double n = along.Length;
            if (n < 1e-9)
                return Box.x * Fill;
            V2 d = pose.RotateBack(along).Abs() / n;
            return (d.x * Box.y + d.y * Box.x) * Fill;
        }

        /// <summary>Rotational inertia at unit mass per area about `com`.</summary>
        public double UnitInertia(V2 com)
        {
            if (Circle)
            {
                V2 d = CircleCentre - com;
                return Area * (0.5 * Radius * Radius + d.LengthSquared);
            }
            double j = 0.0;
            for (int k = 0; k < Polygons.Length; k++)
            {
                V2[] p = Polygons[k];
                double jk = 0.0;
                for (int i = 0; i < p.Length; i++)
                {
                    V2 a = p[i] - com;
                    V2 b = p[(i + 1) % p.Length] - com;
                    jk += a.Cross(b) * (a.Dot(a) + a.Dot(b) + b.Dot(b));
                }
                j += Math.Abs(jk) / 12.0 * Scales[k];
            }
            return j;
        }
    }

    public sealed class ShapeBuilder2D
    {
        struct Disc
        {
            public V2 c;
            public double r;
        }

        public readonly List<string> Warnings = new List<string>();
        readonly List<V2[]> polys = new List<V2[]>();
        readonly List<double> scales = new List<double>();
        readonly List<Disc> discs = new List<Disc>();
        int parts;

        public int PartCount { get { return parts; } }

        public void AddCircle(V2 centre, double radius)
        {
            if (!(radius > 0.0))
            {
                Warnings.Add("a circle has no radius");
                return;
            }
            discs.Add(new Disc { c = centre, r = radius });
            parts++;
        }

        /// <summary>A polygon (body coordinates, either winding). With exactArea > 0 its area counts as that.</summary>
        public bool AddPolygon(IList<V2> points, double exactArea = -1.0)
        {
            if (points.Count < 3)
                return false;
            var p = new V2[points.Count];
            points.CopyTo(p, 0);
            double a = SignedArea(p);
            if (Math.Abs(a) < 1e-14)
            {
                Warnings.Add("a polygon has no area");
                return false;
            }
            polys.Add(p);
            scales.Add(exactArea > 0.0 ? exactArea / Math.Abs(a) : 1.0);
            parts++;
            return true;
        }

        /// <summary>A capsule: centre, unit axis along its length, radius, and total length (at least 2 x radius).</summary>
        public bool AddCapsule(V2 centre, V2 axis, double radius, double length)
        {
            if (!(radius > 0.0))
                return false;
            double half = Math.Max(0.5 * length - radius, 0.0);
            double n = axis.Length;
            V2 u = n > 0.0 ? axis / n : new V2(0, 1);
            V2 side = new V2(u.y, -u.x);
            var pts = new List<V2>();
            for (int i = 0; i <= 12; i++)
            {
                double t = Math.PI * i / 12.0;   // round the near end, from one side to the other
                pts.Add(centre - u * half + (side * Math.Cos(t) - u * Math.Sin(t)) * radius);
            }
            for (int i = 0; i <= 12; i++)
            {
                double t = Math.PI * i / 12.0;   // round the far end, back again
                pts.Add(centre + u * half + (-side * Math.Cos(t) + u * Math.Sin(t)) * radius);
            }
            return AddPolygon(pts, 4.0 * radius * half + Math.PI * radius * radius);
        }

        public BuoyancyShape2D Build()
        {
            var s = new BuoyancyShape2D();
            if (parts == 1 && discs.Count == 1)
            {
                double r = discs[0].r;
                s.Circle = true;
                s.Radius = r;
                s.CircleCentre = discs[0].c;
                s.Area = Math.PI * r * r;
                s.Centroid = s.CircleCentre;
                s.BoundRadius = r;
                s.Box = new V2(2 * r, 2 * r);
                s.Fill = Math.PI / 4.0;
                return s;
            }
            foreach (var d in discs)
            {
                var pts = new List<V2>();
                for (int i = 0; i < 24; i++)
                    pts.Add(d.c + V2.FromAngle(2.0 * Math.PI * i / 24.0) * d.r);
                parts--;   // AddPolygon counts it again
                AddPolygon(pts, Math.PI * d.r * d.r);
            }
            if (polys.Count == 0)
                return s;
            double area = 0.0;
            V2 moment = V2.Zero;
            int most = 0;
            for (int k = 0; k < polys.Count; k++)
            {
                double a = Math.Abs(SignedArea(polys[k])) * scales[k];
                area += a;
                moment += PolygonCentroid(polys[k]) * a;
                most = Math.Max(most, polys[k].Length);
            }
            s.Polygons = polys.ToArray();
            s.Scales = scales.ToArray();
            s.Area = area;
            s.Centroid = moment / area;
            var lo = new V2(double.PositiveInfinity, double.PositiveInfinity);
            var hi = new V2(double.NegativeInfinity, double.NegativeInfinity);
            double bound = 0.0;
            foreach (var poly in polys)
            {
                foreach (var p in poly)
                {
                    lo = new V2(Math.Min(lo.x, p.x), Math.Min(lo.y, p.y));
                    hi = new V2(Math.Max(hi.x, p.x), Math.Max(hi.y, p.y));
                    bound = Math.Max(bound, (p - s.Centroid).Length);
                }
            }
            s.BoundRadius = bound;
            s.Box = hi - lo;
            double boxArea = s.Box.x * s.Box.y;
            s.Fill = boxArea > 0.0 ? WaterVolume.Clamp(area / boxArea, 0.3, 1.0) : 1.0;
            s.scratchPts = new V2[most];
            s.scratchDepth = new double[most];
            s.scratchClip = new V2[2 * most + 2];
            return s;
        }

        public static double SignedArea(V2[] p)
        {
            double a = 0.0;
            for (int i = 0; i < p.Length; i++)
                a += p[i].Cross(p[(i + 1) % p.Length]);
            return 0.5 * a;
        }

        public static V2 PolygonCentroid(V2[] p)
        {
            double a = 0.0;
            V2 c = V2.Zero;
            for (int i = 0; i < p.Length; i++)
            {
                V2 q = p[(i + 1) % p.Length];
                double cr = p[i].Cross(q);
                a += cr;
                c += (p[i] + q) * cr;
            }
            return c / (3.0 * a);
        }
    }

    public static class Immersion2D
    {
        /// <summary>Area under water and its centre (world), for the body at `pose`.</summary>
        public static void Immerse(BuoyancyShape2D s, BodyPose2D pose, WaterSurface2D surface, out double area, out V2 centre)
        {
            if (s.Circle)
            {
                V2 c = pose.Apply(s.CircleCentre);
                double r = s.Radius;
                double q = c.y - surface.HeightAt(c.x);   // height of the centre above the surface
                if (q >= r)
                {
                    area = 0.0;
                    centre = c;
                }
                else if (q <= -r)
                {
                    area = s.Area;
                    centre = c;
                }
                else
                {
                    double h = Math.Sqrt(r * r - q * q);
                    area = r * r * Math.Acos(q / r) - q * h;
                    centre = c - new V2(0.0, (2.0 / 3.0) * h * h * h / area);
                }
                return;
            }
            V2 cw = pose.Apply(s.Centroid);
            if (s.IsEmpty)
            {
                area = 0.0;
                centre = cw;
                return;
            }
            // swell bounds how far the surface anywhere under the body can be from its height over the centre: twice
            // the most it rises or falls from its mean.
            double swell = 2.0 * surface.MaxWave;
            double top = surface.HeightAt(cw.x);
            if (cw.y - s.BoundRadius >= top + swell)
            {
                area = 0.0;
                centre = cw;
                return;
            }
            if (cw.y + s.BoundRadius <= top - swell)
            {
                area = s.Area;
                centre = cw;
                return;
            }
            double total = 0.0;
            V2 moment = V2.Zero;
            V2[] pts = s.scratchPts;
            double[] depth = s.scratchDepth;
            V2[] clipped = s.scratchClip;
            for (int k = 0; k < s.Polygons.Length; k++)
            {
                V2[] poly = s.Polygons[k];
                int n = poly.Length;
                for (int j = 0; j < n; j++)
                {
                    V2 p = pose.Apply(poly[j]);
                    pts[j] = p;
                    depth[j] = surface.HeightAt(p.x) - p.y;   // positive below the surface
                }
                int m = 0;
                for (int j = 0; j < n; j++)
                {
                    int jn = j + 1 == n ? 0 : j + 1;
                    double da = depth[j], db = depth[jn];
                    if (da > 0.0)
                        clipped[m++] = pts[j];
                    if ((da > 0.0) != (db > 0.0))
                        clipped[m++] = pts[j] + (pts[jn] - pts[j]) * (da / (da - db));
                }
                if (m < 3)
                    continue;
                double a2 = 0.0;
                V2 cm = V2.Zero;
                V2 o = clipped[0];
                for (int j = 1; j < m - 1; j++)
                {
                    double t = 0.5 * (clipped[j] - o).Cross(clipped[j + 1] - o);
                    a2 += t;
                    cm += t * (o + clipped[j] + clipped[j + 1]) / 3.0;
                }
                double sc = s.Scales[k];
                total += Math.Abs(a2) * sc;
                moment += cm * (Math.Sign(a2) * sc);
            }
            area = WaterVolume.Clamp(total, 0.0, s.Area);
            centre = total > 1e-12 ? moment / total : cw;
        }
    }

    /// <summary>One 2D body's buoyancy, every physics step (side view, y up). Masses are per `Thickness` metres of
    /// depth into the screen.</summary>
    public sealed class BodyCore2D
    {
        public BuoyancyShape2D Shape = new BuoyancyShape2D();

        public bool Bounded = true;
        public double Density = 500.0;
        public double AddedMassCoefficient = 1.0;
        public double DragCoefficient = 1.0;
        public double AngularDrag = 3.0;
        public bool KeepMomentumOnEntry = true;
        public double AirDensity = 1.2;
        public double AirHalfHeight;
        /// <summary>Length of a sail for the wind to push on (m).</summary>
        public double SailSize;
        public V2 SailCentre = new V2(0, 0.4);
        /// <summary>Depth of the body into the screen (m), to turn areas into kilograms.</summary>
        public double Thickness = 1.0;
        /// <summary>Wave-making damping (Bounded model): 1 is the physics (see WaveDampingRatio), 0 leaves it out, other
        /// values scale it.</summary>
        public double WaveDamping = 1.0;

        public double SubmergedArea;
        public V2 CentreOfBuoyancy;
        public double AirDensityHere;
        public double AddedMass;
        public double InertialMass;
        public V2 WindHere;
        public V2 VelocityTarget;
        public V2 LastAcceleration;
        /// <summary>Torque (anticlockwise positive), N·m.</summary>
        public double Torque;
        public double AngularDamp;
        /// <summary>Wave-making damping of bobbing this step, N per m/s (0 when not at the surface).</summary>
        public double HeaveDamping;
        public Crossing Crossing;
        public double CrossingSpeed;
        public V2 CrossingPoint;

        double prevAddedMass = -1.0;
        bool wasInWater;
        bool firstStep = true;
        double prevSurface = double.NaN;
        object prevWater;
        double prevLevel;
        V2 prevCom;

        public double OwnMass { get { return Density * Shape.Area * Thickness; } }

        public V2 CentroidWorld(BodyPose2D pose) { return pose.Apply(Shape.Centroid); }

        public void Frozen()
        {
            prevAddedMass = -1.0;
            prevSurface = double.NaN;
            LastAcceleration = V2.Zero;
            Torque = 0.0;
            Crossing = Crossing.None;
        }

        /// <summary>
        /// Wave-making damping of bobbing, N per m/s, for a side-view body `thickness` m deep into the screen with
        /// `submergedArea` m² under water, the centre of that area `depth` m below the surface, and inertial mass `mass`
        /// (kg, water taken along included). `omega` is returned as its own bobbing frequency (rad/s). As in 3D,
        /// B = 2 ζ m ω, for the rectangle with the same area under water and the same depth of its centre (draft
        /// d = 2 z_B, beam b = area / d), ω = √(ρ g b thickness / m).
        /// </summary>
        public static double WaveDampingCoefficient(double rho, double g, double submergedArea, double depth, double thickness, double mass, out double omega)
        {
            omega = 0.0;
            if (submergedArea <= 0.0 || depth <= 1e-9 || mass <= 0.0 || rho <= 0.0 || g <= 0.0 || thickness <= 0.0)
                return 0.0;
            double draft = 2.0 * depth;
            double beam = submergedArea / draft;
            omega = Math.Sqrt(rho * g * beam * thickness / mass);
            return 2.0 * WaveDampingRatio(beam / draft) * mass * omega;
        }

        /// <summary>
        /// Damping ratio of bobbing from the waves a body makes in a side view, where the waves it sends out run only
        /// left and right (a long body, like a strip of a ship's hull): ζ = 0.0591 r / (1 + 0.15 r), r its beam over its
        /// draft. About 0.03 for a deep, narrow body (r = 0.5), 0.05 for a square floating half under (1), 0.15 for a
        /// raft (4) and 0.3 for a plank lying flat (20): more than in 3D, where waves spread out in rings. Fitted within
        /// 2% to the exact results of linear wave theory for long floating boxes, computed with the boundary-element
        /// solver Capytaine (reference/wave_damping.py).
        /// </summary>
        public static double WaveDampingRatio(double beamToDraft)
        {
            if (!(beamToDraft > 0.0))
                return 0.0;
            return 0.0591 * beamToDraft / (1.0 + 0.15 * beamToDraft);
        }

        public void Step(BodyPose2D pose, V2 comWorld, V2 velocity, WaterSample2D water, V2 wind, double g, double dt)
        {
            double areaAll = Shape.Area;
            V2 cw = pose.Apply(Shape.Centroid);
            double rho = water.Density;
            V2 flow = water.HasWater ? water.Flow : V2.Zero;
            if (water.HasWater)
            {
                Immersion2D.Immerse(Shape, pose, water.Surface, out SubmergedArea, out CentreOfBuoyancy);
            }
            else
            {
                SubmergedArea = 0.0;
                CentreOfBuoyancy = cw;
            }
            AirDensityHere = AirDensity;
            if (AirHalfHeight > 0.0)
                AirDensityHere *= Math.Pow(0.5, Math.Max(cw.y / AirHalfHeight, -10.0));
            double airArea = areaAll - SubmergedArea;
            double kg = Thickness;
            double displaced = (rho * SubmergedArea + AirDensityHere * airArea) * kg;
            AddedMass = AddedMassCoefficient * displaced;
            if (prevAddedMass < 0.0)
                prevAddedMass = AddedMass;

            bool inWater = SubmergedArea > 0.0;
            Crossing = Crossing.None;
            if (firstStep)
            {
                wasInWater = inWater;
                firstStep = false;
            }
            else if (inWater != wasInWater)
            {
                wasInWater = inWater;
                Crossing = inWater ? Crossing.Entered : Crossing.Exited;
                CrossingSpeed = (velocity - flow).Length;
                CrossingPoint = new V2(cw.x, water.Surface.HeightAt(cw.x));
            }

            double own = OwnMass;
            InertialMass = Bounded ? own + AddedMass : own;
            V2 v = velocity;
            if (Bounded && KeepMomentumOnEntry && AddedMass > prevAddedMass)
                v = flow + (v - flow) * ((own + prevAddedMass) / InertialMass);

            double m = InertialMass;
            V2 drive = new V2(0.0, (displaced - own) * g);
            WindHere = wind;
            V2 throughWater = v - flow;
            V2 throughAir = v - wind;
            double cWater = 0.5 * DragCoefficient * Shape.DragLength(throughWater, pose) * rho * kg * (SubmergedArea / areaAll)
                * throughWater.Length * dt / m;
            double cAir = 0.5 * DragCoefficient * Shape.DragLength(throughAir, pose) * AirDensityHere * kg * (airArea / areaAll)
                * throughAir.Length * dt / m;
            double cSail = 0.5 * BodyCore3D.SailDrag * SailSize * AirDensityHere * kg * throughAir.Length * dt / m;
            V2 target = (v + drive / m * dt + cWater * flow + (cAir + cSail) * wind) / (1.0 + cWater + cAir + cSail);
            VelocityTarget = target;
            LastAcceleration = (target - v) / dt;

            Torque = 0.0;
            if (cSail > 0.0)
            {
                V2 push = cSail * m / dt * (wind - target);
                Torque += (pose.Apply(SailCentre) - comWorld).Cross(push);
            }
            // Righting moment: buoyancy of the water and the air at the centres of the parts they displace.
            if (displaced > 0.0)
            {
                V2 centre = ((rho - AirDensityHere) * SubmergedArea * CentreOfBuoyancy + AirDensityHere * areaAll * cw) * kg / displaced;
                Torque += (centre - comWorld).Cross(new V2(0.0, displaced * g));
            }
            AngularDamp = AngularDrag * (SubmergedArea + airArea * Math.Min(1.0, AirDensityHere / Density)) / areaAll;
            prevAddedMass = AddedMass;

            // Wave-making damping of bobbing relative to the surface under the body, as in BodyCore3D.
            HeaveDamping = 0.0;
            if (Bounded && WaveDamping > 0.0 && SubmergedArea > 0.0 && SubmergedArea < areaAll * (1.0 - 1e-9))
            {
                double surfaceHere = water.Surface.HeightAt(cw.x);
                double level = water.Surface.Level;
                bool follows = !double.IsNaN(prevSurface) && ReferenceEquals(water.Owner, prevWater)
                    && Math.Abs(level - prevLevel) < TeleportDistance
                    && (comWorld - prevCom - velocity * dt).LengthSquared < TeleportDistance * TeleportDistance;
                double surfaceRate = follows ? (surfaceHere - prevSurface) / dt : 0.0;
                prevSurface = surfaceHere;
                prevWater = water.Owner;
                prevLevel = level;
                prevCom = comWorld;
                double omega, depth = surfaceHere - CentreOfBuoyancy.y;
                HeaveDamping = WaveDamping * WaveDampingCoefficient(rho, g, SubmergedArea, depth, Thickness, m, out omega);
                if (HeaveDamping > 0.0)
                {
                    double rel = v.y - surfaceRate;
                    double time = BodyCore3D.AtSurface(depth, (areaAll - SubmergedArea) / SubmergedArea, rel, dt);
                    double c = 1.0 - Math.Exp(-HeaveDamping * time / m);
                    VelocityTarget = new V2(VelocityTarget.x, VelocityTarget.y - c * rel);
                    LastAcceleration = (VelocityTarget - v) / dt;
                }
            }
            else
            {
                prevSurface = double.NaN;
            }
        }

        const double TeleportDistance = 0.1;
    }
}
