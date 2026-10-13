// Bounded Buoyancy for Unity. Engine-free core. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System;
using System.Collections.Generic;

namespace BoundedBuoyancy.Core
{
    /// <summary>What a foil or a propeller works in.</summary>
    public enum Medium
    {
        /// <summary>The water: a keel, a centreboard, a rudder, a fin, a propeller. Acts on the part under the surface.</summary>
        Water,
        /// <summary>The air: a sail, a wing, a tail fin, a fan. Acts on the part above the surface, with the wind.</summary>
        Air,
    }

    /// <summary>
    /// A thin flat foil fixed to a body: a keel, centreboard, rudder, fin, sail or wing. The flow past it makes a force
    /// square to its face (lift and pressure drag together, as for a flat plate) and a little friction along it, at its
    /// centre of effort, so a keel stops a boat sliding sideways, a rudder turns it and a sail drives it across the wind.
    /// <para>The force square to the face is ½ ρ A q² C_N(α), with α the angle between the flow and the foil and
    /// C_N(α) = k sin α cos α + 1.2 sin² α, no more than 1.4 (a stalled plate). k is the lift slope of a wing of the
    /// foil's aspect ratio (Helmbold: 2π AR / (2 + √(AR² + 4)), which tends to 2π for long wings and to π AR / 2 for
    /// short ones); 1.2 is a flat plate square to the flow, the drag the plugin's sails have always had.</para>
    /// <para>The pose is set by the engine layer each step, in world space: the pivot, the span (the foil's long axis,
    /// up a keel or a mast) and the chord at rest (across it, pointing aft, from the leading edge to the trailing edge).
    /// The foil is turned about its span by Angle (a rudder), or by itself when AutoTrim is on (a sail).</para>
    /// </summary>
    public sealed class Foil3D
    {
        /// <summary>Normal-force coefficient of a flat plate square to the flow.</summary>
        public const double SquareOnCoefficient = 1.2;
        /// <summary>Most normal-force coefficient at any angle: a stalled plate makes no more.</summary>
        public const double MaxNormalCoefficient = 1.4;
        /// <summary>Skin friction along the chord, both faces together.</summary>
        public const double FrictionCoefficient = 0.02;

        // Settings
        public Medium Medium = Medium.Water;
        /// <summary>Area of one face, m².</summary>
        public double Area = 0.1;
        /// <summary>Span² / area: about 1 to 2 for a keel, 2 to 3 for a rudder, 3 to 5 for a sail.</summary>
        public double AspectRatio = 2.0;
        /// <summary>Angle the foil is turned about its span from rest, degrees (right-handed): a rudder's angle. With
        /// AutoTrim on, the foil sets it itself each step.</summary>
        public double Angle;
        /// <summary>Trim itself like a sail: swing about the span, away from the wind, to meet the flow at
        /// TrimAngleOfAttack, no further than MaxTrim from its rest position.</summary>
        public bool AutoTrim;
        /// <summary>Angle of attack a trimmed sail keeps to the flow, degrees.</summary>
        public double TrimAngleOfAttack = 15.0;
        /// <summary>How far a trimmed sail may swing from its rest position, degrees (the boom's limit).</summary>
        public double MaxTrim = 85.0;
        /// <summary>Distance from the pivot to the centre of effort, along the chord (aft), metres. 0: the pivot is
        /// the centre. A sail pivots at its mast and pushes about a third of its chord aft of it.</summary>
        public double EffortOffset;

        // Pose this step (world), set by the engine layer
        /// <summary>The foil's pivot (its node's origin).</summary>
        public V3 Pivot;
        /// <summary>Unit vector along the span.</summary>
        public V3 Span = V3.Up;
        /// <summary>Unit vector along the chord at rest (Angle 0), pointing aft.</summary>
        public V3 RestChord = new V3(0, 0, 1);

        /// <summary>Unit vector along the chord at the foil's present angle, pointing aft.</summary>
        public V3 Chord { get { return Turn(RestChord, Span, Angle); } }

        // Results of the last step
        /// <summary>Mean force over the last step (world, N).</summary>
        public V3 Force;
        /// <summary>Fraction of the foil in its medium.</summary>
        public double Immersion;
        /// <summary>Angle between the flow and the foil, degrees (signed about the span).</summary>
        public double AngleOfAttack;

        /// <summary>Length of the span, m.</summary>
        public double SpanLength { get { return Math.Sqrt(Math.Max(Area, 0.0) * Math.Max(AspectRatio, 1e-6)); } }
        /// <summary>Length of the chord, m.</summary>
        public double ChordLength { get { double s = SpanLength; return s > 0.0 ? Area / s : 0.0; } }
        /// <summary>Lift slope k of the foil, per radian.</summary>
        public double LiftSlope { get { return LiftSlopeFor(AspectRatio); } }
        /// <summary>Where the force acts this step (world).</summary>
        public V3 CentreOfEffort { get { return Pivot + Chord * EffortOffset; } }

        /// <summary>Lift slope of a wing of aspect ratio `ar`, per radian (Helmbold).</summary>
        public static double LiftSlopeFor(double ar)
        {
            ar = Math.Max(ar, 1e-6);
            return 2.0 * Math.PI * ar / (2.0 + Math.Sqrt(ar * ar + 4.0));
        }

        /// <summary>Normal-force coefficient at angle of attack `alpha` (radians) for lift slope `k`.</summary>
        public static double NormalCoefficient(double alpha, double k)
        {
            double s = Math.Sin(alpha), c = Math.Abs(Math.Cos(alpha));
            double cn = Math.Min(k * Math.Abs(s) * c + SquareOnCoefficient * s * s, MaxNormalCoefficient);
            return s < 0.0 ? -cn : cn;
        }

        /// <summary>A unit vector `v` turned by `degrees` about the unit axis `axis` (right-handed).</summary>
        public static V3 Turn(V3 v, V3 axis, double degrees)
        {
            double a = degrees * Math.PI / 180.0;
            return v * Math.Cos(a) + axis.Cross(v) * Math.Sin(a) + axis * (axis.Dot(v) * (1.0 - Math.Cos(a)));
        }

        /// <summary>Running nearly dead downwind, a trimmed sail stays on the side it is on until the wind comes this
        /// many degrees round to the other side (sailing "by the lee"), so it does not flick from side to side.</summary>
        public const double GybeMargin = 10.0;

        /// <summary>
        /// The angle (degrees, about the span) a sail at rest along `restChord` swings to, for air moving past it at
        /// `airPast` (the air's velocity relative to the sail): away from the wind, so that its chord meets the flow
        /// at `angleOfAttack`, but no further than `maxTrim`. Head to wind (air coming straight down the chord) it
        /// stays at rest, square to nothing, and makes no lift; running before the wind it swings out to maxTrim.
        /// `current` is the angle it has now: within GybeMargin of dead downwind it keeps to that side.
        /// </summary>
        public static double Trim(V3 restChord, V3 span, V3 airPast, double angleOfAttack, double maxTrim, double current = 0.0)
        {
            V3 a = airPast - span * span.Dot(airPast);
            if (a.LengthSquared < 1e-18)
                return current;
            double beta = Math.Atan2(restChord.Cross(a).Dot(span), restChord.Dot(a)) * 180.0 / Math.PI;
            double swing = Math.Min(Math.Max(Math.Abs(beta) - angleOfAttack, 0.0), maxTrim);
            bool side = beta < 0.0;
            if (current != 0.0 && (current < 0.0) != side && Math.Abs(beta) > 180.0 - GybeMargin)
                side = current < 0.0;   // by the lee: keep the side it is on
            return side ? -swing : swing;
        }
    }

    /// <summary>
    /// A propeller (in the water) or a fan (in the air) fixed to a body. It pushes along its direction with
    /// MaxThrust x Throttle at rest, falling in a straight line as its speed through its medium along that direction
    /// grows, to nothing at Throttle x MaxSpeed: so it gives a boat a top speed of its own, and a reversed throttle
    /// pushes backwards. Out of its medium it pushes nothing.
    /// </summary>
    public sealed class Thruster3D
    {
        public Medium Medium = Medium.Water;
        /// <summary>Push at full throttle from rest, N.</summary>
        public double MaxThrust = 200.0;
        /// <summary>Speed through the medium at which full throttle pushes no more, m/s.</summary>
        public double MaxSpeed = 5.0;
        /// <summary>-1 (full astern) to 1 (full ahead).</summary>
        public double Throttle;
        /// <summary>Diameter, m: how deep it must be to work fully.</summary>
        public double Diameter = 0.3;

        // Pose this step (world), set by the engine layer
        public V3 Position;
        /// <summary>Unit vector the thruster pushes the body along.</summary>
        public V3 Direction = new V3(0, 0, -1);

        // Results of the last step
        /// <summary>Mean force over the last step (world, N).</summary>
        public V3 Force;
        /// <summary>Fraction of the thruster in its medium.</summary>
        public double Immersion;
    }

    /// <summary>
    /// Applies a body's foils and thrusters for one step, after its buoyancy and drag. Each one gives the body an
    /// impulse at its point, worked out implicitly (as the body's drag is) against the inertia the body has there:
    /// the impulse may stop the flow past the foil or bring the propeller to its speed, but never overshoot. So a
    /// large keel on a light boat cannot throw it from side to side, however big the step. They are applied one
    /// after another, each seeing the motion the ones before it left, so several together cannot overshoot either.
    /// </summary>
    public static class Appendages
    {
        /// <summary>
        /// One step for the body's foils and thrusters. `mass` is its inertial mass (water taken along included),
        /// `com` its centre of mass, `axes` and `moments` its principal axes (world, as columns) and moments of
        /// inertia, `v` and `w` its velocity and angular velocity, updated in place. `water` is what the body finds
        /// (its surface is used at each foil), `wind` the air's velocity and `airDensity` its density at the body.
        /// Returns the angular impulse about the centre of mass (world) for the engine to apply; the linear impulse
        /// is in the change to `v`.
        /// </summary>
        public static V3 Apply(IList<Foil3D> foils, IList<Thruster3D> thrusters, double mass, V3 com, M3 axes, V3 moments,
            ref V3 v, ref V3 w, WaterSample water, V3 wind, double airDensity, double dt)
        {
            V3 angular = V3.Zero;
            if (dt <= 0.0 || mass <= 0.0)
                return angular;
            V3 flow = water.HasWater ? water.Flow : V3.Zero;
            if (foils != null)
            {
                for (int i = 0; i < foils.Count; i++)
                {
                    Foil3D f = foils[i];
                    if (f == null)
                        continue;
                    f.Force = V3.Zero;
                    f.Immersion = 0.0;
                    f.AngleOfAttack = 0.0;
                    if (f.Area <= 0.0)
                        continue;
                    V3 s = f.Span;
                    if (f.AutoTrim)
                    {
                        // A sail swings away from the wind it feels at its pivot.
                        V3 rp = f.Pivot - com;
                        V3 air = (f.Medium == Medium.Water ? flow : wind) - (v + w.Cross(rp));
                        f.Angle = Foil3D.Trim(f.RestChord, s, air, f.TrimAngleOfAttack, f.MaxTrim, f.Angle);
                    }
                    V3 c = f.Chord;
                    V3 p = f.CentreOfEffort;
                    double half = 0.5 * (f.SpanLength * Math.Abs(s.y) + f.ChordLength * Math.Abs(c.y));
                    double wet = WetFraction(water, p, half);
                    bool inWater = f.Medium == Medium.Water;
                    double frac = inWater ? wet : 1.0 - wet;
                    double rho = inWater ? water.Density : airDensity;
                    f.Immersion = frac;
                    if (frac <= 0.0 || rho <= 0.0)
                        continue;
                    V3 medium = inWater ? flow : wind;
                    V3 r = p - com;
                    V3 u = v + w.Cross(r) - medium;   // the foil's velocity through its medium
                    V3 up = u - s * s.Dot(u);           // the part across the span
                    double q = up.Length;
                    if (q < 1e-9)
                        continue;
                    V3 n = s.Cross(c);
                    double un = up.Dot(n), uc = up.Dot(c);
                    f.AngleOfAttack = Math.Atan2(-un, -uc) * 180.0 / Math.PI;   // flow past the foil against its chord
                    if (f.AngleOfAttack > 90.0) f.AngleOfAttack -= 180.0;
                    else if (f.AngleOfAttack < -90.0) f.AngleOfAttack += 180.0;
                    double area = f.Area * frac;
                    // Normal force: -½ ρ A (k |uc| + 1.2 |un|) un n, capped at ½ ρ A 1.4 q² (stall).
                    double coeff = f.LiftSlope * Math.Abs(uc) + Foil3D.SquareOnCoefficient * Math.Abs(un);
                    if (Math.Abs(un) > 1e-12)
                        coeff = Math.Min(coeff, Foil3D.MaxNormalCoefficient * q * q / Math.Abs(un));
                    double cn = 0.5 * rho * area * coeff;
                    V3 jn = Impulse(n, cn, un, 0.0, double.NegativeInfinity, double.PositiveInfinity, r, mass, axes, moments, dt);
                    Push(jn, r, mass, axes, moments, ref v, ref w, ref angular);
                    // Friction along the chord, with the flow left after the push across it.
                    V3 u2 = v + w.Cross(r) - medium;
                    double cc = 0.5 * rho * area * Foil3D.FrictionCoefficient * q;
                    V3 jc = Impulse(c, cc, u2.Dot(c), 0.0, double.NegativeInfinity, double.PositiveInfinity, r, mass, axes, moments, dt);
                    Push(jc, r, mass, axes, moments, ref v, ref w, ref angular);
                    f.Force = (jn + jc) / dt;
                }
            }
            if (thrusters != null)
            {
                for (int i = 0; i < thrusters.Count; i++)
                {
                    Thruster3D t = thrusters[i];
                    if (t == null)
                        continue;
                    t.Force = V3.Zero;
                    t.Immersion = 0.0;
                    double wet = WetFraction(water, t.Position, 0.5 * Math.Max(t.Diameter, 1e-3));
                    bool inWater = t.Medium == Medium.Water;
                    double frac = inWater ? wet : 1.0 - wet;
                    t.Immersion = frac;
                    double throttle = Math.Max(-1.0, Math.Min(1.0, t.Throttle));
                    double full = t.MaxThrust * frac;
                    if (full <= 0.0 || throttle == 0.0 || t.MaxSpeed <= 0.0)
                        continue;
                    V3 d = t.Direction;
                    V3 r = t.Position - com;
                    double ua = (v + w.Cross(r) - (inWater ? flow : wind)).Dot(d);
                    // Thrust full x (throttle - ua / MaxSpeed): a pull towards the speed throttle x MaxSpeed, at
                    // full / MaxSpeed newtons per m/s, kept between nothing and full x throttle.
                    double target = throttle * t.MaxSpeed;
                    double lo = Math.Min(0.0, throttle) * full * dt, hi = Math.Max(0.0, throttle) * full * dt;
                    V3 j = Impulse(d, full / t.MaxSpeed, ua, target, lo, hi, r, mass, axes, moments, dt);
                    Push(j, r, mass, axes, moments, ref v, ref w, ref angular);
                    t.Force = j / dt;
                }
            }
            return angular;
        }

        /// <summary>Fraction of a part `half` metres tall either side of `p` that is under the water.</summary>
        public static double WetFraction(WaterSample water, V3 p, double half)
        {
            if (!water.HasWater)
                return 0.0;
            double depth = water.Surface.HeightAt(p) - p.y;
            if (half < 1e-6)
                return depth > 0.0 ? 1.0 : 0.0;
            return Math.Max(0.0, Math.Min(1.0, 0.5 + 0.5 * depth / half));
        }

        /// <summary>
        /// The impulse along unit `d` at `r` (from the centre of mass) that a pull of `coeff` newtons per m/s
        /// towards the speed `target` gives over dt, with the speed along d at the point now `speed`: worked out
        /// with the speed at the end of the step (implicitly), against the body's inertia at that point, so it
        /// never carries the point past the target. Kept between lo and hi (newton-seconds).
        /// </summary>
        static V3 Impulse(V3 d, double coeff, double speed, double target, double lo, double hi, V3 r, double mass, M3 axes, V3 moments, double dt)
        {
            if (coeff <= 0.0)
                return V3.Zero;
            V3 rd = r.Cross(d);
            double inv = 1.0 / mass + rd.Dot(InverseInertia(rd, axes, moments));   // 1 / inertia at the point along d
            double k = coeff * dt;
            double j = k * (target - speed) / (1.0 + k * inv);
            j = Math.Max(lo, Math.Min(hi, j));
            return d * j;
        }

        static void Push(V3 j, V3 r, double mass, M3 axes, V3 moments, ref V3 v, ref V3 w, ref V3 angular)
        {
            V3 l = r.Cross(j);
            v += j / mass;
            w += InverseInertia(l, axes, moments);
            angular += l;
        }

        /// <summary>The inverse of the inertia tensor (principal axes `axes` as columns, `moments`) applied to `a`.</summary>
        public static V3 InverseInertia(V3 a, M3 axes, V3 moments)
        {
            V3 l = axes.MulT(a);
            l = new V3(moments.x > 0.0 ? l.x / moments.x : 0.0, moments.y > 0.0 ? l.y / moments.y : 0.0, moments.z > 0.0 ? l.z / moments.z : 0.0);
            return axes.Mul(l);
        }
    }
}
