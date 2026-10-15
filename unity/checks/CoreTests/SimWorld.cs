// A minimal rigid-body integrator standing in for the physics engine, so the engine-free core can be run
// against the benchmarks in plain .NET. Semi-implicit Euler at a fixed step, like PhysX and Godot Physics:
// the velocity is set first, then the position moves with the new velocity.
using System;
using System.Collections.Generic;
using BoundedBuoyancy.Core;

namespace BoundedBuoyancy.Checks
{
    struct Quat
    {
        public double w, x, y, z;
        public Quat(double w, double x, double y, double z) { this.w = w; this.x = x; this.y = y; this.z = z; }
        public static Quat AboutZ(double deg)
        {
            double a = deg * Math.PI / 360.0;
            return new Quat(Math.Cos(a), 0, 0, Math.Sin(a));
        }
        public static Quat AboutY(double deg)
        {
            double a = deg * Math.PI / 360.0;
            return new Quat(Math.Cos(a), 0, Math.Sin(a), 0);
        }
        public M3 Matrix { get { return M3.FromQuaternion(x, y, z, w); } }
        public Quat Integrate(V3 omega, double dt)
        {
            // q' = q + 0.5 (0, ω) q dt
            double hw = 0.5 * dt;
            var dq = new Quat(
                -omega.x * x - omega.y * y - omega.z * z,
                omega.x * w + omega.y * z - omega.z * y,
                omega.y * w + omega.z * x - omega.x * z,
                omega.z * w + omega.x * y - omega.y * x);
            var q = new Quat(w + hw * dq.w, x + hw * dq.x, y + hw * dq.y, z + hw * dq.z);
            double n = Math.Sqrt(q.w * q.w + q.x * q.x + q.y * q.y + q.z * q.z);
            return new Quat(q.w / n, q.x / n, q.y / n, q.z / n);
        }
    }

    sealed class SimBody
    {
        public BodyCore3D Core = new BodyCore3D();
        public V3 Com;
        public Quat Rot = new Quat(1, 0, 0, 0);
        public V3 V, W;
        public V3 ComLocal;
        public V3 Moments;
        public M3 Axes = M3.Identity;
        public bool Frozen;
        /// <summary>Its velocity at the start of the step, before the core's step (what a line reads).</summary>
        public V3 V0;
        /// <summary>Stepped by the core this step, its position still to move.</summary>
        public bool Moving;
        /// <summary>A fast ball (BoundedBuoyancyCannonball): steps that meet the water are worked through by TryStepBall.</summary>
        public bool Ball;
        public double WaterLevel;
        public double WaterDensity = 1000.0;
        public WaterVolume Water;          // the water it is in, this step
        /// <summary>Foils with their pivot, span and rest chord in the body's own coordinates (from its origin).</summary>
        public readonly List<(Foil3D foil, V3 pivot, V3 span, V3 chord)> Foils = new List<(Foil3D, V3, V3, V3)>();
        /// <summary>Thrusters with their position and push direction in the body's own coordinates.</summary>
        public readonly List<(Thruster3D thruster, V3 at, V3 dir)> Thrusters = new List<(Thruster3D, V3, V3)>();
        readonly List<Foil3D> foilList = new List<Foil3D>();
        readonly List<Thruster3D> thrusterList = new List<Thruster3D>();
        public event Action<double, V3> Entered;
        public event Action<double, V3> Exited;

        public M3 Basis { get { return Rot.Matrix; } }
        public BodyPose BodyPose { get { M3 r = Basis; return new BodyPose(Com - r.Mul(ComLocal), r); } }
        public V3 Position { get { return BodyPose.Position; } }
        public double TiltDegrees { get { return Math.Acos(Math.Max(-1.0, Math.Min(1.0, Basis.Y.y))) * 180.0 / Math.PI; } }

        /// <summary>Set up like the Unity component does: mass from density, centre of mass at the centroid unless
        /// given, inertia from the shape.</summary>
        public void SetUp(BuoyancyShape3D shape, double density, V3 origin, double rollDegrees, V3? comLocal = null)
        {
            Core.Shape = shape;
            Core.Density = density;
            Rot = Quat.AboutZ(rollDegrees);
            ComLocal = comLocal ?? shape.Centroid;
            Com = origin + Basis.Mul(ComLocal);
            RecomputeInertia();
        }

        public void RecomputeInertia()
        {
            V3 m;
            M3 a;
            (Core.Shape.UnitInertia(ComLocal) * Core.Density).Principal(out m, out a);
            Moments = m;
            Axes = a;
        }

        public void Fire(Crossing c, double speed, V3 at)
        {
            if (c == Crossing.Entered && Entered != null)
                Entered(speed, at);
            if (c == Crossing.Exited && Exited != null)
                Exited(speed, at);
        }

        /// <summary>Foils and thrusters, as the engine layers do after the body's step: poses set from the body's
        /// pose, then the core's Appendages.Apply on the target velocity and the angular velocity.</summary>
        public void ApplyAppendages(BodyPose pose, WaterSample sample, V3 wind, double dt)
        {
            if (Foils.Count == 0 && Thrusters.Count == 0)
                return;
            foilList.Clear();
            thrusterList.Clear();
            foreach (var f in Foils)
            {
                f.foil.Pivot = pose.Apply(f.pivot);
                f.foil.Span = pose.Basis.Mul(f.span);
                f.foil.RestChord = pose.Basis.Mul(f.chord);
                foilList.Add(f.foil);
            }
            foreach (var t in Thrusters)
            {
                t.thruster.Position = pose.Apply(t.at);
                t.thruster.Direction = pose.Basis.Mul(t.dir);
                thrusterList.Add(t.thruster);
            }
            V3 v = Core.VelocityTarget;
            V3 w = W;
            Appendages.Apply(foilList, thrusterList, Core.InertialMass, Com, pose.Basis * Axes, Moments, ref v, ref w, sample, wind, Core.AirDensityHere, dt);
            Core.VelocityTarget = v;
            W = w;
        }
    }

    sealed class SimWorld
    {
        public double G = 9.80665;
        public double Dt = 1.0 / 60.0;
        public readonly List<WaterVolume> Waters = new List<WaterVolume>();
        public readonly List<WindField> Winds = new List<WindField>();
        public readonly List<SimBody> Bodies = new List<SimBody>();
        public readonly List<SimWall> Walls = new List<SimWall>();
        public readonly List<SimLine> Lines = new List<SimLine>();
        /// <summary>Real air for every body, as BoundedBuoyancyAir gives it (null: each body's fixed air); its thermals'
        /// updrafts are added to the wind, as BoundedBuoyancyWind.TotalAt adds them.</summary>
        public AirField Air;

        public SimBody Add(SimBody b) { Bodies.Add(b); return b; }

        public void Step()
        {
            // Water and wind clocks advance first (DefaultExecutionOrder(-100) in Unity).
            foreach (var w in Waters)
                w.Time += Dt;
            foreach (var w in Winds)
                w.Time += Dt;
            foreach (var b in Bodies)
            {
                b.V0 = b.V;
                if (b.Frozen)
                {
                    b.Core.Frozen();
                    continue;
                }
                BodyPose pose = b.BodyPose;
                V3 cw = b.Core.CentroidWorld(pose);
                WaterSample sample = Find(b, cw);
                V3 wind = V3.Zero;
                foreach (var w in Winds)
                    wind += w.VelocityAt(cw);
                if (Air != null)
                {
                    b.Core.Air = Air;
                    wind += Air.UpdraftAt(cw);
                }
                if (b.Ball && StrikeWall(b, pose, sample, wind))
                    continue;
                if (b.Ball && b.Core.TryStepBall(pose, b.V, sample, wind, G, Dt))
                {
                    // A fast ball meeting the water, worked through in short steps: it ends the step where they leave
                    // it (its centre of mass is its centre), as BoundedBuoyancyCannonball places it.
                    foreach (SurfaceTouch touch in b.Core.Touches)
                        b.Fire(touch.Entering ? Crossing.Entered : Crossing.Exited, touch.Speed, touch.Point);
                    b.V = b.Core.VelocityTarget;
                    b.Com = b.Core.BallCentre;
                    b.W *= Math.Max(0.0, 1.0 - b.Core.AngularDamp * Dt);
                    b.Rot = b.Rot.Integrate(b.W, Dt);
                    continue;
                }
                b.Core.Step(pose, b.Com, b.V, sample, wind, G, Dt);
                if (b.Core.Crossing != Crossing.None)
                    b.Fire(b.Core.Crossing, b.Core.CrossingSpeed, b.Core.CrossingPoint);
                b.ApplyAppendages(pose, sample, wind, Dt);
                // AddForce(VelocityChange): the body lands on the target velocity.
                b.V = b.Core.VelocityTarget;
                // Torque about the centre of mass, through the body's own rotational inertia, with any flood water's
                // added as BoundedBuoyancyBody does (there only when it has changed by 1%; here every step).
                M3 r = b.Basis;
                M3 axes = b.Axes;
                V3 moments = b.Moments;
                if (b.Core.FloodMass > 0.0)
                    (Sym3.FromPrincipal(b.Moments, b.Axes) + b.Core.FloodInertia).Principal(out moments, out axes);
                V3 tl = axes.MulT(r.MulT(b.Core.Torque));
                V3 al = new V3(tl.x / moments.x, tl.y / moments.y, tl.z / moments.z);
                b.W += r.Mul(axes.Mul(al)) * Dt;
                b.W *= Math.Max(0.0, 1.0 - b.Core.AngularDamp * Dt);
                b.Moving = true;
            }
            // Lines after the bodies (BoundedBuoyancyLine runs after them, DefaultExecutionOrder(100)), their pull added to
            // the velocity the bodies landed on, as the engine adds a force to a velocity change; then the bodies move.
            foreach (var l in Lines)
                l.Step(Dt);
            foreach (var b in Bodies)
            {
                if (!b.Moving)
                    continue;
                b.Moving = false;
                b.Com += b.V * Dt;
                b.Rot = b.Rot.Integrate(b.W, Dt);
            }
        }

        /// <summary>A fast ball whose straight path this step reaches planking strikes it, as BoundedBuoyancyCannonball
        /// strikes a body with a BoundedBuoyancyHull: through the water first, if it meets the water on its way (the core's
        /// StepBallToWall), and otherwise straight. It stops there, and the wall's Struck is called.</summary>
        bool StrikeWall(SimBody b, BodyPose pose, WaterSample sample, V3 wind)
        {
            double a = b.Core.Shape.Radius, speed = b.V.Length;
            if (speed < Planking.StrikeSpeed)
                return false;
            V3 c = b.Com, end = c + b.V * Dt;
            foreach (var wall in Walls)
            {
                double d0 = (c - wall.Point).Dot(wall.Normal) - a, d1 = (end - wall.Point).Dot(wall.Normal) - a;
                if (d0 < 0.0 || d1 > 0.0 || -b.V.Dot(wall.Normal) < Planking.Glance * speed)
                    continue;
                V3 centre = c + (end - c) * (d0 / (d0 - d1));
                if (b.Core.MeetsWaterBefore(c, centre, sample))
                {
                    b.Core.StepBallToWall(pose, b.V, sample, wind, G, Dt, centre, wall.Normal);
                    foreach (SurfaceTouch touch in b.Core.Touches)
                        b.Fire(touch.Entering ? Crossing.Entered : Crossing.Exited, touch.Speed, touch.Point);
                    if (!b.Core.WallStruck)
                    {
                        b.V = b.Core.VelocityTarget;
                        b.Com = b.Core.BallCentre;
                        return true;
                    }
                    wall.Fire(b, b.Core.BallCentre - wall.Normal * a, b.Core.WallVelocity);
                    b.Com = b.Core.BallCentre;
                }
                else
                {
                    wall.Fire(b, centre - wall.Normal * a, b.V);
                    b.Com = centre;
                }

                b.V = V3.Zero;
                b.Frozen = true;
                return true;
            }
            return false;
        }

        WaterSample Find(SimBody b, V3 cw)
        {
            b.Water = null;
            foreach (var w in Waters)
            {
                if (w.Contains(cw))
                {
                    b.Water = w;
                    return w.Sample(cw);
                }
            }
            return Waters.Count == 0 ? WaterSample.Flat(b.WaterLevel, b.WaterDensity) : WaterSample.None(b.WaterLevel, b.WaterDensity);
        }
    }

    /// <summary>Planking for fast balls to strike: the plane through Point, its face square to Normal (outward).</summary>
    sealed class SimWall
    {
        public V3 Point, Normal;
        /// <summary>A ball has struck it: where (on its face), and the ball's velocity then.</summary>
        public event Action<SimBody, V3, V3> Struck;
        public void Fire(SimBody b, V3 at, V3 v) { if (Struck != null) Struck(b, at, v); }
    }

    /// <summary>A line, as BoundedBuoyancyLine: from Holder's centre of mass (or Anchor, held fast, when there is no
    /// Holder) to Body's, both made fast at their centres of mass, so it does not turn them.</summary>
    sealed class SimLine
    {
        public SimBody Holder;
        public V3 Anchor;
        public SimBody Body;
        public double Length = 2.0, Diameter = 0.0003, Stiffness = 2.5, Damping = 0.2, SlipTension, BreakingStrength;
        public double Tension, Stretch, Span;
        public bool Broken;

        public double SpringConstant { get { return Tether.SpringConstant(Stiffness, Diameter, Length); } }

        public void Step(double dt)
        {
            Tension = 0.0;
            if (Broken || Body == null)
                return;
            V3 a = Holder != null ? Holder.Com : Anchor, b = Body.Com, d = b - a;
            Span = d.Length;
            Stretch = Span - Length;
            if (Span < 1e-6)
                return;
            V3 n = d / Span;
            bool aMoves = Holder != null && !Holder.Frozen, bMoves = !Body.Frozen;
            double u = 0.0, w = 0.0;
            if (bMoves)
            {
                u += (Body.V0 + Body.Core.LastAcceleration * dt).Dot(n);
                w += Tether.InverseMassAt(Body.Core.InertialMass, V3.Zero, n, Body.Moments, Body.Axes);
            }
            if (aMoves)
            {
                u -= (Holder.V0 + Holder.Core.LastAcceleration * dt).Dot(n);
                w += Tether.InverseMassAt(Holder.Core.InertialMass, V3.Zero, n, Holder.Moments, Holder.Axes);
            }
            if (w <= 0.0)
                return;
            double paid;
            double j = Tether.Pull(Stretch, u, w, SpringConstant, Damping, dt, SlipTension, out paid);
            if (j <= 0.0)
                return;
            Length += paid;
            Tension = j / dt;
            if (BreakingStrength > 0.0 && Tension > BreakingStrength)
            {
                Broken = true;
                Tension = 0.0;
                return;
            }
            if (bMoves)
                Body.V -= n * (j / Body.Core.InertialMass);
            if (aMoves)
                Holder.V += n * (j / Holder.Core.InertialMass);
        }
    }

    sealed class SimBody2
    {
        public BodyCore2D Core = new BodyCore2D();
        public V2 Com;
        public double Angle;
        public V2 V;
        public double W;
        public V2 ComLocal;
        public double Inertia;
        public event Action<double, V2> Entered;

        public BodyPose2D BodyPose { get { var p = new BodyPose2D(V2.Zero, Angle); return new BodyPose2D(Com - p.Rotate(ComLocal), Angle); } }
        public V2 Position { get { return BodyPose.Position; } }

        public void SetUp(BuoyancyShape2D shape, double density, V2 origin, double angleDegrees, V2? comLocal = null)
        {
            Core.Shape = shape;
            Core.Density = density;
            Angle = angleDegrees * Math.PI / 180.0;
            ComLocal = comLocal ?? shape.Centroid;
            Com = origin + new BodyPose2D(V2.Zero, Angle).Rotate(ComLocal);
            Inertia = shape.UnitInertia(ComLocal) * density * Core.Thickness;
        }

        public void Fire(Crossing c, double speed, V2 at)
        {
            if (c == Crossing.Entered && Entered != null)
                Entered(speed, at);
        }
    }

    sealed class SimWorld2
    {
        public double G = 9.80665;
        public double Dt = 1.0 / 60.0;
        public readonly List<WaterVolume2D> Waters = new List<WaterVolume2D>();
        public readonly List<WindField2D> Winds = new List<WindField2D>();
        public readonly List<SimBody2> Bodies = new List<SimBody2>();

        public SimBody2 Add(SimBody2 b) { Bodies.Add(b); return b; }

        public void Step()
        {
            foreach (var w in Waters)
                w.Time += Dt;
            foreach (var w in Winds)
                w.Time += Dt;
            foreach (var b in Bodies)
            {
                BodyPose2D pose = b.BodyPose;
                V2 cw = b.Core.CentroidWorld(pose);
                WaterSample2D sample = WaterSample2D.None(0.0, 1000.0);
                bool found = false;
                foreach (var w in Waters)
                {
                    if (w.Contains(cw))
                    {
                        sample = w.Sample(cw);
                        found = true;
                        break;
                    }
                }
                if (!found && Waters.Count == 0)
                    sample = WaterSample2D.Flat(0.0, 1000.0);
                V2 wind = V2.Zero;
                foreach (var w in Winds)
                    wind += w.VelocityAt(cw);
                b.Core.Step(pose, b.Com, b.V, sample, wind, G, Dt);
                if (b.Core.Crossing != Crossing.None)
                    b.Fire(b.Core.Crossing, b.Core.CrossingSpeed, b.Core.CrossingPoint);
                b.V = b.Core.VelocityTarget;
                b.W += b.Core.Torque / b.Inertia * Dt;
                b.W *= Math.Max(0.0, 1.0 - b.Core.AngularDamp * Dt);
                b.Com += b.V * Dt;
                b.Angle += b.W * Dt;
            }
        }
    }
}
