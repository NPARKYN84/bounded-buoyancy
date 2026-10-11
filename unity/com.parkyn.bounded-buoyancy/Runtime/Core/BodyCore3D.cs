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
        public Crossing Crossing;
        public double CrossingSpeed;
        public V3 CrossingPoint;

        double prevAddedMass = -1.0;
        bool wasInWater;
        bool firstStep = true;

        /// <summary>The body's own mass, density x volume (kg).</summary>
        public double OwnMass { get { return Density * Shape.Volume; } }

        /// <summary>Centre of the body's volume in world space.</summary>
        public V3 CentroidWorld(BodyPose pose) { return pose.Apply(Shape.Centroid); }

        /// <summary>Call while the body is frozen (kinematic): nothing is applied, and momentum starts afresh.</summary>
        public void Frozen()
        {
            prevAddedMass = -1.0;
            LastAcceleration = V3.Zero;
            Torque = V3.Zero;
            Crossing = Crossing.None;
        }

        /// <summary>One physics step. `velocity` is the body's (centre of mass) velocity now, `comWorld` its centre of
        /// mass, `water` what it finds at its centroid, `wind` the air's velocity there, g the strength of gravity.</summary>
        public void Step(BodyPose pose, V3 comWorld, V3 velocity, WaterSample water, V3 wind, double g, double dt)
        {
            double volumeAll = Shape.Volume;
            V3 cw = pose.Apply(Shape.Centroid);
            double rho = water.Density;
            V3 flow = water.HasWater ? water.Flow : V3.Zero;
            if (water.HasWater)
            {
                Immersion3D.Immerse(Shape, pose, water.Surface, out SubmergedVolume, out CentreOfBuoyancy);
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
                CrossingPoint = new V3(cw.x, water.Surface.HeightAt(cw), cw.z);
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
        }
    }
}
