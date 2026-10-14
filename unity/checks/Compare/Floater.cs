// One rigid body floated by one of four methods, on the same fixed-step integrator (semi-implicit Euler at 60 Hz,
// as PhysX, Godot Physics and Jolt step), in the same water, with the same quadratic drag and spin damping. Only the
// buoyancy method differs.
using System;
using System.Collections.Generic;
using BoundedBuoyancy.Checks;
using BoundedBuoyancy.Core;

namespace BoundedBuoyancy.Compare
{
    enum Method
    {
        /// <summary>Buoyancy as a force on the body's own mass, the exact submerged volume of its shape: what most
        /// engine scripts and tutorials do.</summary>
        Usual,
        /// <summary>The same, plus linear and angular damping while in the water (Rigidbody drag in the water), the
        /// usual cure for its bouncing.</summary>
        UsualDamped,
        /// <summary>Spheres ("pontoons") approximating the shape, each pushed up by the water it displaces at its own
        /// point, with damping on vertical speed: the method of Unreal's Water plugin and many assets.</summary>
        Pontoons,
        /// <summary>Bounded Buoyancy: the added mass of the water displaced in the inertia, and the energy its bobbing
        /// sends out as ripples (wave-making damping, from linear wave theory); nothing tuned.</summary>
        Bounded,
    }

    sealed class Floater
    {
        public readonly Method Method;
        public readonly BodyCore3D Core = new BodyCore3D();
        public V3 Com, V, W;
        public Quat Rot = new Quat(1, 0, 0, 0);
        public V3 ComLocal, Moments;
        public M3 Axes = M3.Identity;
        /// <summary>Damping rate in the water (1/s): linear and angular for UsualDamped; for Pontoons, each pontoon's
        /// damping is this x the body's mass / pontoons, scaled by how far under it is.</summary>
        public double Damping;
        /// <summary>Pontoons: centre (body coordinates, from the origin) and radius.</summary>
        public readonly List<(V3 at, double r)> Pontoons = new List<(V3, double)>();
        public WaterVolume Water;

        public Floater(Method m) { Method = m; }

        public M3 Basis { get { return Rot.Matrix; } }
        public BodyPose Pose { get { M3 r = Basis; return new BodyPose(Com - r.Mul(ComLocal), r); } }
        public V3 Position { get { return Pose.Position; } }
        public double TiltDegrees { get { return Math.Acos(Math.Max(-1.0, Math.Min(1.0, Basis.Y.y))) * 180.0 / Math.PI; } }

        public void SetUp(BuoyancyShape3D shape, double density, V3 origin, double rollDegrees)
        {
            Core.Shape = shape;
            Core.Density = density;
            Core.AirDensity = 0.0;   // as in the paper's benchmarks
            Core.Bounded = Method == Method.Bounded;
            Rot = Quat.AboutZ(rollDegrees);
            ComLocal = shape.Centroid;
            Com = origin + Basis.Mul(ComLocal);
            V3 mo;
            M3 ax;
            (shape.UnitInertia(ComLocal) * density).Principal(out mo, out ax);
            Moments = mo;
            Axes = ax;
        }

        WaterSample Sample(V3 at)
        {
            return Water.Contains(at) ? Water.Sample(at) : WaterSample.None(0.0, 1000.0);
        }

        public void Step(double g, double dt)
        {
            BodyPose pose = Pose;
            M3 r = pose.Basis;
            V3 cw = Core.CentroidWorld(pose);
            WaterSample sample = Sample(cw);
            V3 torque;
            double spinDamp;
            if (Method != Method.Pontoons)
            {
                Core.Step(pose, Com, V, sample, V3.Zero, g, dt);
                V = Core.VelocityTarget;
                torque = Core.Torque;
                spinDamp = Core.AngularDamp;
                if (Method == Method.UsualDamped && Core.SubmergedVolume > 0.0)
                {
                    V = V / (1.0 + Damping * dt);
                    spinDamp += Damping;
                }
            }
            else
            {
                double m = Core.OwnMass;
                double rho = sample.Density;
                V3 force = V3.Up * (-m * g);
                torque = V3.Zero;
                double under = 0.0, sphereTotal = 0.0;
                int n = Pontoons.Count;
                foreach (var p in Pontoons)
                {
                    V3 c = pose.Apply(p.at);
                    double sphere = 4.0 / 3.0 * Math.PI * p.r * p.r * p.r;
                    sphereTotal += sphere;
                    if (!sample.HasWater)
                        continue;
                    double d = Math.Max(0.0, Math.Min(2.0 * p.r, sample.Surface.HeightAt(c) - (c.y - p.r)));
                    double cap = Math.PI * d * d * (3.0 * p.r - d) / 3.0;
                    if (cap <= 0.0)
                        continue;
                    under += cap;
                    double vz = (V + W.Cross(c - Com)).y;
                    double lift = rho * g * cap - Damping * m / n * (cap / sphere) * vz;
                    V3 f = V3.Up * lift;
                    force += f;
                    torque += (c - Com).Cross(f);
                }
                // The same quadratic drag as the other methods, on the part under water, applied implicitly.
                double frac = sphereTotal > 0.0 ? Math.Min(1.0, under / sphereTotal) : 0.0;
                V3 flow = sample.HasWater ? sample.Flow : V3.Zero;
                V3 through = V - flow;
                double cWater = 0.5 * Core.DragCoefficient * Core.Shape.DragArea(through, r) * rho * frac * through.Length * dt / m;
                V = (V + force / m * dt + cWater * flow) / (1.0 + cWater);
                spinDamp = Core.AngularDrag * frac;
            }
            // Torque through the body's own rotational inertia, then the spin damping, as the engines do.
            V3 tl = Axes.MulT(r.MulT(torque));
            V3 al = new V3(tl.x / Moments.x, tl.y / Moments.y, tl.z / Moments.z);
            W += r.Mul(Axes.Mul(al)) * dt;
            W *= Math.Max(0.0, 1.0 - spinDamp * dt);
            Com += V * dt;
            Rot = Rot.Integrate(W, dt);
        }
    }
}
