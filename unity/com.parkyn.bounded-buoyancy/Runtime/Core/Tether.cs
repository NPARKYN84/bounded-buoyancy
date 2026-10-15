// Bounded Buoyancy for Unity. Engine-free core. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System;

namespace BoundedBuoyancy.Core
{
    /// <summary>
    /// A line that pulls but never pushes (a fishing line, a mooring rope, a tow line), elastic as nylon is: stretched by x
    /// beyond its length L it pulls with T = E A x / L, E the stiffness of its material (Young's modulus) and A its
    /// cross-section (Hooke's law). Shorter than its length it is slack and pulls nothing.
    /// <para>Like the buoyancy, the pull is bounded: it is worked out each step as an impulse, with the stretch and the
    /// speed at the end of the step (backward Euler), against the inertia of what is on each end at the points where the
    /// line is made fast. So a stiff line on a light float neither shakes nor flings it, and a weight hanging still
    /// stretches it by exactly m g / k when the speed the ends draw apart at includes the other forces on them over the
    /// step.</para>
    /// <para>The line's own weight and the water's drag on it are left out: nylon is little denser than water and a line
    /// is thin.</para>
    /// </summary>
    public static class Tether
    {
        /// <summary>Spring constant of a whole line, N/m: E A / L, with E `stiffnessGPa` GPa, a round cross-section
        /// `diameter` metres across and `length` metres long (at least 0.1 mm).</summary>
        public static double SpringConstant(double stiffnessGPa, double diameter, double length)
        {
            return stiffnessGPa * 1e9 * Math.PI * diameter * diameter * 0.25 / Math.Max(length, 1e-4);
        }

        /// <summary>1 / the inertia a body puts up against a pull along the unit vector `n` at a point `r` from its centre
        /// of mass: 1/m plus its turning, (r × n)·I⁻¹(r × n), with I its principal `moments` along the columns of `axes`
        /// (world). A moment of 0 is taken as no turning about that axis.</summary>
        public static double InverseMassAt(double mass, V3 r, V3 n, V3 moments, M3 axes)
        {
            if (!(mass > 0.0))
                return 0.0;
            double inv = 1.0 / mass;
            V3 a = axes.MulT(r.Cross(n));
            if (moments.x > 0.0)
                inv += a.x * a.x / moments.x;
            if (moments.y > 0.0)
                inv += a.y * a.y / moments.y;
            if (moments.z > 0.0)
                inv += a.z * a.z / moments.z;
            return inv;
        }

        /// <summary>Damping coefficient of the line's stretch, N s/m, for the damping ratio `damping`
        /// (0 springs back for ever, 1 settles without springing back), spring constant `k` and combined inverse inertia
        /// `inverseMass`: c = 2 ζ √(k / w).</summary>
        public static double DampingCoefficient(double damping, double k, double inverseMass)
        {
            return inverseMass > 0.0 ? 2.0 * damping * Math.Sqrt(k / inverseMass) : 0.0;
        }

        /// <summary>
        /// The pull over one step, as an impulse J (N s, along the line, ≥ 0): the one the stretch and the speed at the end
        /// of the step give (backward Euler), J = dt (k x + (k dt + c) u) / (1 + dt (k dt + c) w). It never pushes.
        /// <para>`stretch` is how far the line is stretched beyond its length now (m; negative while slack); `separating`
        /// the speed at which its ends draw apart now plus what the other forces on them (gravity, buoyancy, drag) would
        /// add to it over the step (m/s); `inverseMass` 1 / the inertia along the line of both ends together (see
        /// InverseMassAt); `k` the spring constant; `damping` the damping ratio ζ (c = 2 ζ √(k / w)).</para>
        /// <para>With `slipTension` over 0 the line runs out instead of pulling harder than that (a reel's drag, a winch's
        /// slipping clutch): the pull is held to it, and `paidOut` is the length it runs out, what it would otherwise have
        /// stretched beyond the stretch that pull gives.</para>
        /// </summary>
        public static double Pull(double stretch, double separating, double inverseMass, double k, double damping, double dt,
            double slipTension, out double paidOut)
        {
            paidOut = 0.0;
            if (!(inverseMass > 0.0) || !(dt > 0.0) || !(k > 0.0))
                return 0.0;
            double c = DampingCoefficient(damping, k, inverseMass);
            double j = dt * (k * stretch + (k * dt + c) * separating) / (1.0 + dt * (k * dt + c) * inverseMass);
            if (!(j > 0.0))
                return 0.0;
            if (slipTension > 0.0 && j > slipTension * dt)
            {
                j = slipTension * dt;
                double stretched = stretch + (separating - j * inverseMass) * dt;
                paidOut = Math.Max(stretched - slipTension / k, 0.0);
            }
            return j;
        }
    }
}
