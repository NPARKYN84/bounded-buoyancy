// Bounded Buoyancy for Unity. Engine-free core. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System;

namespace BoundedBuoyancy.Core
{
    /// <summary>Whether a body has just come into or out of the water.</summary>
    public enum Crossing
    {
        None,
        Entered,
        Exited,
    }

    /// <summary>
    /// One body's buoyancy, every physics step: the added mass of the water and air it displaces goes into its
    /// inertia (Bounded Buoyancy), so buoyancy drives no fall faster than g and no rise faster than g/C. The step
    /// works out the velocity the body should reach this step, the torque of buoyancy and of a sail, and the
    /// damping of spin; the engine layer applies them.
    /// </summary>
    public sealed class BodyCore3D
    {
        public const double SailDrag = 1.2;   // drag coefficient of a flat sail facing the wind

        public BuoyancyShape3D Shape = new BuoyancyShape3D();

        // Settings
        public bool Bounded = true;
        public double Density = 500.0;
        public double AddedMassCoefficient = 0.5;
        public double DragCoefficient = 0.47;
        public double AngularDrag = 3.0;
        public bool KeepMomentumOnEntry = true;
        public double AirDensity = 1.2;
        public double AirHalfHeight;
        public double SailArea;
        public V3 SailCentre = new V3(0, 0.5, 0);
        /// <summary>Wave-making damping (Bounded model): a body bobbing at the surface sends out ripples, and they carry
        /// its energy away. 1 is the physics (see WaveDampingCoefficient); 0 leaves it out, as the paper's benchmarks
        /// do; other values scale it.</summary>
        public double WaveDamping = 1.0;

        // Results of the last step
        public double SubmergedVolume;
        public V3 CentreOfBuoyancy;
        public double AirDensityHere;
        public double AddedMass;
        public double InertialMass;
        public V3 WindHere;
        /// <summary>Velocity the body should have at the end of this step (weight, buoyancy and drag applied).</summary>
        public V3 VelocityTarget;
        public V3 LastAcceleration;
        /// <summary>Torque of buoyancy (righting) and of the sail, world frame.</summary>
        public V3 Torque;
        /// <summary>Damping of spin, per second.</summary>
        public double AngularDamp;
        /// <summary>Wave-making damping of bobbing this step, N per m/s (0 when not at the surface).</summary>
        public double HeaveDamping;
        public Crossing Crossing;
        public double CrossingSpeed;
        public V3 CrossingPoint;

        double prevAddedMass = -1.0;
        bool wasInWater;
        bool firstStep = true;
        double prevSurface = double.NaN;   // height of the surface under the body last step
        object prevWater;                  // the water it was in then
        double prevLevel;                  // that water's level
        V3 prevCom;                        // and where its centre of mass was

        /// <summary>The body's own mass, density x volume (kg).</summary>
        public double OwnMass { get { return Density * Shape.Volume; } }

        /// <summary>Centre of the body's volume in world space.</summary>
        public V3 CentroidWorld(BodyPose pose) { return pose.Apply(Shape.Centroid); }

        /// <summary>Call while the body is frozen (kinematic): nothing is applied, and momentum starts afresh.</summary>
        public void Frozen()
        {
            prevAddedMass = -1.0;
            prevSurface = double.NaN;
            LastAcceleration = V3.Zero;
            Torque = V3.Zero;
            Crossing = Crossing.None;
        }

        /// <summary>
        /// Wave-making (radiation) damping of bobbing, N per m/s, for a body with `submerged` m³ under water whose centre
        /// of buoyancy is `depth` m below the surface and whose inertial mass (water taken along included) is `mass`, in
        /// water of density `rho` under gravity `g`. `omega` is returned as its own bobbing frequency (rad/s).
        /// <para>A body bobbing at the surface sends out ripples, and they carry its energy away. How much depends on its
        /// shape: B = 2 ζ m ω, with ω = √(ρ g A / m) its bobbing frequency and ζ the damping ratio that linear wave theory
        /// gives a floating body as wide against its draft (WaveDampingRatio). A and the draft d are those of the prism
        /// with the same volume V under water and the same depth z_B of its centre (d = 2 z_B, A = V / d).</para>
        /// </summary>
        public static double WaveDampingCoefficient(double rho, double g, double submerged, double depth, double mass, out double omega)
        {
            omega = 0.0;
            if (submerged <= 0.0 || depth <= 1e-9 || mass <= 0.0 || rho <= 0.0 || g <= 0.0)
                return 0.0;
            double draft = 2.0 * depth;
            double area = submerged / draft;
            omega = Math.Sqrt(rho * g * area / mass);
            return 2.0 * WaveDampingRatio(area / (draft * draft)) * mass * omega;
        }

        /// <summary>
        /// Damping ratio of bobbing from the waves a floating body makes, for a body whose waterplane area over its draft
        /// squared is `widthToDraft` (A / d²): about 0.005 for a deep post or spar buoy (A / d² = 0.2), 0.05 for a cube or
        /// a ball floating half under (4 to 5), 0.14 for a light ball or a log (30) and 0.2 for a raft or a boat (100 to
        /// 400). Each bob is lower than the last by a share 1 − e^(−2πζ): an eighth at 0.02, a half at 0.11, three
        /// quarters at 0.22.
        /// <para>ζ = 0.0182 x / (1 + 0.0798 x), x = (A / d²)^0.869: fitted to the exact damping ratios of linear wave theory
        /// (deep water, with the real added mass of the water) for floating boxes, upright cylinders, spheres and logs,
        /// computed with the boundary-element solver Capytaine (reference/wave_damping.py). Flat-bottomed bodies come out
        /// at 0.8 to 1.5 times the exact value, round-bottomed ones (balls, logs) floating at least half out of the water at
        /// about 0.6 to 0.7 times, and heavy balls floating mostly under at more (4 times at nine tenths under).</para>
        /// </summary>
        public static double WaveDampingRatio(double widthToDraft)
        {
            if (!(widthToDraft > 0.0))
                return 0.0;
            double x = Math.Pow(widthToDraft, 0.869);
            return 0.0182 * x / (1.0 + 0.0798 * x);
        }

        /// <summary>One physics step. `velocity` is the body's (centre of mass) velocity now, `comWorld` its centre of
        /// mass, `water` what it finds at its centroid, `wind` the air's velocity there, g the strength of gravity.</summary>
        public void Step(BodyPose pose, V3 comWorld, V3 velocity, WaterSample water, V3 wind, double g, double dt)
        {
            double volumeAll = Shape.Volume;
            V3 cw = pose.Apply(Shape.Centroid);
            double rho = water.Density;
            V3 flow = water.HasWater ? water.Flow : V3.Zero;
            double surfaceHere = 0.0;   // height of the surface over the centre of the body's volume
            if (water.HasWater)
            {
                Immersion3D.Immerse(Shape, pose, water.Surface, out SubmergedVolume, out CentreOfBuoyancy, out surfaceHere);
            }
            else
            {
                SubmergedVolume = 0.0;
                CentreOfBuoyancy = cw;
            }
            // The rest of the body is in air, thinning with height if AirHalfHeight is set.
            AirDensityHere = AirDensity;
            if (AirHalfHeight > 0.0)
                AirDensityHere *= Math.Pow(0.5, Math.Max(cw.y / AirHalfHeight, -10.0));
            double airVolume = volumeAll - SubmergedVolume;
            double displaced = rho * SubmergedVolume + AirDensityHere * airVolume;   // mass of water and air displaced
            AddedMass = AddedMassCoefficient * displaced;
            if (prevAddedMass < 0.0)
                prevAddedMass = AddedMass;

            bool inWater = SubmergedVolume > 0.0;
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
                CrossingPoint = new V3(cw.x, water.HasWater ? surfaceHere : water.Surface.HeightAt(cw), cw.z);
            }

            double own = OwnMass;
            InertialMass = Bounded ? own + AddedMass : own;
            V3 v = velocity;
            if (Bounded && KeepMomentumOnEntry && AddedMass > prevAddedMass)
            {
                // The water taken along starts with the water's own velocity, so momentum is shared relative to it.
                v = flow + (v - flow) * ((own + prevAddedMass) / InertialMass);
            }

            // Density-state drive (weight minus buoyancy), then drag applied implicitly, as engines apply damping.
            // Drag acts on the body's speed through each medium: through the water (which may be flowing) on the
            // part under water, and through the air on the rest. With still water and air this reproduces the
            // reference update of the paper step for step.
            double m = InertialMass;
            V3 drive = V3.Up * ((displaced - own) * g);
            WindHere = wind;
            V3 throughWater = v - flow;
            V3 throughAir = v - wind;
            double cWater = 0.5 * DragCoefficient * Shape.DragArea(throughWater, pose.Basis) * rho * SubmergedVolume / volumeAll
                * throughWater.Length * dt / m;
            double cAir = 0.5 * DragCoefficient * Shape.DragArea(throughAir, pose.Basis) * AirDensityHere * airVolume / volumeAll
                * throughAir.Length * dt / m;
            double cSail = 0.5 * SailDrag * SailArea * AirDensityHere * throughAir.Length * dt / m;
            V3 target = (v + drive / m * dt + cWater * flow + (cAir + cSail) * wind) / (1.0 + cWater + cAir + cSail);
            VelocityTarget = target;
            LastAcceleration = (target - v) / dt;

            Torque = V3.Zero;
            // The sail's push acts at the sail's centre, so it heels the body as well as driving it.
            if (cSail > 0.0)
            {
                V3 push = cSail * m / dt * (wind - target);
                Torque += (pose.Apply(SailCentre) - comWorld).Cross(push);
            }
            // Righting moment: the lift of the water and of the air act at the centres of the parts they displace,
            // the weight at the centre of mass.
            if (displaced > 0.0)
            {
                V3 centre = ((rho - AirDensityHere) * SubmergedVolume * CentreOfBuoyancy + AirDensityHere * volumeAll * cw) / displaced;
                V3 arm = centre - comWorld;
                if (arm.x * arm.x + arm.z * arm.z > 1e-14)
                    Torque += arm.Cross(V3.Up * (displaced * g));
            }
            // Spin is damped by the water, and by the air in proportion to how light the body is against it.
            AngularDamp = AngularDrag * (SubmergedVolume + airVolume * Math.Min(1.0, AirDensityHere / Density)) / volumeAll;
            prevAddedMass = AddedMass;

            // Wave-making damping: bobbing relative to the surface under the body (which rises and falls with any
            // waves, so a body riding them is not held back) sends out ripples that carry energy away. It acts on the
            // speed at the start of the step, so a body let go from rest starts exactly as before, and it takes away
            // at most that speed, so it slows the bobbing but never reverses it. Only while the body is at the surface.
            // The surface's own rise and fall is read from its height under the body last step, unless the body has
            // since moved to other water or been moved by more than its speed (a teleport), or the water itself has been
            // moved up or down by more than that.
            HeaveDamping = 0.0;
            if (Bounded && WaveDamping > 0.0 && SubmergedVolume > 0.0 && SubmergedVolume < volumeAll * (1.0 - 1e-9))
            {
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
                HeaveDamping = WaveDamping * WaveDampingCoefficient(rho, g, SubmergedVolume, depth, m, out omega);
                if (HeaveDamping > 0.0)
                {
                    double rel = v.y - surfaceRate;
                    // Damped for as long as it is at the surface this step: until it leaves the water or goes under.
                    double time = AtSurface(depth, (volumeAll - SubmergedVolume) / SubmergedVolume, rel, dt);
                    double c = 1.0 - Math.Exp(-HeaveDamping * time / m);
                    VelocityTarget = new V3(VelocityTarget.x, VelocityTarget.y - c * rel, VelocityTarget.z);
                    LastAcceleration = (VelocityTarget - v) / dt;
                }
            }
            else
            {
                prevSurface = double.NaN;
            }
        }

        /// <summary>A body found this far from where its velocity would have carried it, or water whose level has moved
        /// this far in a step, has been moved, not carried there (m).</summary>
        const double TeleportDistance = 0.1;

        /// <summary>How long in a step of dt a body stays at the surface (partly under), its centre of buoyancy `depth`
        /// below it and `dryOverWet` its volume out of the water over its volume in, moving up at `rise` relative to the
        /// surface: all of the step, unless it will be out of the water, or wholly under, before the step ends. Measured
        /// on its equivalent prism (draft 2 x depth, the dry part dryOverWet times as tall). So a body leaping out of the
        /// water or plunging into it is damped only while it is at the surface.</summary>
        public static double AtSurface(double depth, double dryOverWet, double rise, double dt)
        {
            if (rise > 0.0)
                return Math.Min(dt, 2.0 * depth / rise);
            if (rise < 0.0)
                return Math.Min(dt, 2.0 * depth * dryOverWet / -rise);
            return dt;
        }
    }
}
