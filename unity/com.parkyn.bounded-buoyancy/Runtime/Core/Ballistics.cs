// Bounded Buoyancy for Unity. Engine-free core. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System;

namespace BoundedBuoyancy.Core
{
    /// <summary>
    /// Drag of a ball in fast flight. Below about two thirds of the speed of sound a cannonball's drag coefficient is
    /// about 0.2, well under a slow ball's 0.47, as the air round it is turbulent (the drag crisis); it rises through
    /// the speed of sound to about 1. This is the sphere drag of Miller and Bailey (1979), worked out from 18th and 19th
    /// century cannon firings, as fitted by Collins (arc.id.au): C_D = k(M) + t(M) C_Re(s(M) Re), k and t cubic Bezier
    /// curves in the Mach number M, s(M) = 0.78 + 0.22 atan(−12 (M − 0.23)), and C_Re Morrison's (2013) correlation for
    /// a smooth sphere at the Reynolds number Re, with M held between 0.2 and 1.5.
    /// </summary>
    public static class SphereDrag
    {
        /// <summary>Viscosity of air at 15 °C, kg/(m s).</summary>
        public const double AirViscosity = 1.81e-5;

        // k(M) and t(M) every 0.005 from Mach 0.2 to 1.5, from their Bezier curves (control points: Mach, value).
        static readonly double[] Shock = Table(new[] { 0.1, 0.0, 0.95, 0.0, 0.55, 0.95, 1.5, 1.0 }, 1.5, 1.0);
        static readonly double[] Mask = Table(new[] { 0.0, 1.1, 0.85, 1.1, 0.57, 0.05, 1.0, 0.0 }, 1.0, 0.0);

        /// <summary>Drag coefficient in air of density `airDensity` of a ball `diameter` metres across flying at `speed`,
        /// the speed of sound being `soundSpeed`. About 0.2 for a cannonball below Mach 0.6, 0.8 at Mach 1, 1 above 1.4.</summary>
        public static double CoefficientAt(double speed, double diameter, double airDensity, double soundSpeed = 343.0)
        {
            double m = Math.Min(Math.Max(speed / Math.Max(soundSpeed, 1.0), 0.2), 1.5);
            if (double.IsNaN(m))
                m = 0.2;
            double f = (m - 0.2) / 0.005;
            int i = Math.Min((int)f, Shock.Length - 2);
            f -= i;
            double k = Shock[i] + (Shock[i + 1] - Shock[i]) * f;
            double t = Mask[i] + (Mask[i + 1] - Mask[i]) * f;
            double s = 0.78 + 0.22 * Math.Atan(-12.0 * (m - 0.23));
            double re = Math.Max(speed * diameter * Math.Max(airDensity, 1e-6) / AirViscosity, 1.0);
            return k + t * Morrison(s * re);
        }

        /// <summary>Drag coefficient of a smooth sphere at the Reynolds number `re`, from creeping flow through the drag
        /// crisis (Morrison, 2013): 0.47 at 10^5, about 0.1 just past the crisis at 5 x 10^5.</summary>
        public static double Morrison(double re)
        {
            return 24.0 / re + 2.6 * (re / 5.0) / (1.0 + Math.Pow(re / 5.0, 1.52))
                + 0.411 * Math.Pow(re / 2.63e5, -7.94) / (1.0 + Math.Pow(re / 2.63e5, -8.0))
                + 0.25 * (re / 1.0e6) / (1.0 + re / 1.0e6);
        }

        static double[] Table(double[] p, double end, double after)
        {
            var t = new double[261];
            for (int j = 0; j < t.Length; j++)
            {
                double m = 0.2 + 0.005 * j;
                t[j] = m > end || (end == 1.5 && m >= 1.5) ? after : BezierAt(p, m);
            }
            return t;
        }

        /// <summary>The value of the cubic Bezier curve with control points p (x0, y0, x1, y1, ...) at x, its x rising
        /// along it.</summary>
        static double BezierAt(double[] p, double x)
        {
            double lo = 0.0, hi = 1.0;
            for (int n = 0; n < 40; n++)
            {
                double u = 0.5 * (lo + hi), w = 1.0 - u;
                if (w * w * w * p[0] + 3.0 * w * w * u * p[2] + 3.0 * w * u * u * p[4] + u * u * u * p[6] < x)
                    lo = u;
                else
                    hi = u;
            }
            double v = 0.5 * (lo + hi), r = 1.0 - v;
            return r * r * r * p[1] + 3.0 * r * r * v * p[3] + 3.0 * r * v * v * p[5] + v * v * v * p[7];
        }
    }

    /// <summary>
    /// The water's push on a ball meeting its surface at speed: pressure ½ ρ u² on the part of the ball under the
    /// surface that faces into its path, u the ball's speed into its surface there, square to it (Birkhoff and others,
    /// 1944). Over a ball the push across its path is a lift, and a ball skips when it comes down at less than about
    /// 18° / √σ, σ its density over the water's.
    /// </summary>
    public static class WaterImpact
    {
        static readonly double[] GaussX = { -0.8611363115940526, -0.3399810435848563, 0.3399810435848563, 0.8611363115940526 };
        static readonly double[] GaussW = { 0.3478548451374538, 0.6521451548625461, 0.6521451548625461, 0.3478548451374538 };

        /// <summary>For a ball of unit radius moving with unit velocity (uh along the surface, uy square to it, out of
        /// the water positive), its centre `c` radii above the surface: the integral of (u·n)² n over the part of the
        /// ball under the surface that faces into its path (u·n > 0), along uh's direction and square to the surface.
        /// The force is −½ ρ U² a² times this. The part's edge, z = −c, and the edges of the facing side, at z = ±uh,
        /// split the integral over height z (four-point Gauss–Legendre on each piece); round the ball it is exact.</summary>
        public static void Push(double uh, double uy, double c, out double along, out double up)
        {
            along = 0.0;
            up = 0.0;
            double lo = -1.0, hi = Math.Min(-c, 1.0);
            if (hi <= lo)
                return;
            // The pieces between lo, the facing side's edges where they fall inside, and hi.
            double e1 = hi, e2 = hi;
            if (-uh > lo && -uh < hi)
                e1 = -uh;
            if (uh > lo && uh < hi && uh > -uh)
            {
                if (e1 < hi)
                    e2 = uh;
                else
                    e1 = uh;
            }
            double a0 = lo;
            for (int piece = 0; piece < 3; piece++)
            {
                double b0 = piece == 0 ? e1 : (piece == 1 ? e2 : hi);
                if (b0 <= a0)
                    continue;
                double mid = 0.5 * (a0 + b0), half = 0.5 * (b0 - a0);
                for (int q = 0; q < 4; q++)
                {
                    double z = mid + half * GaussX[q];
                    double s = Math.Sqrt(Math.Max(1.0 - z * z, 0.0));
                    double a = uh * s, b = uy * z;
                    double p0 = Math.PI;
                    if (a < 1e-12)
                    {
                        if (b <= 0.0)
                            continue;
                    }
                    else
                    {
                        double r = -b / a;
                        if (r >= 1.0)
                            continue;
                        if (r > -1.0)
                            p0 = Math.Acos(r);
                    }
                    double sp = Math.Sin(p0), cp = Math.Cos(p0);
                    double j0 = a * a * (p0 + sp * cp) + 4.0 * a * b * sp + 2.0 * b * b * p0;
                    double j1 = a * a * 2.0 * (sp - sp * sp * sp / 3.0) + 2.0 * a * b * (p0 + sp * cp) + 2.0 * b * b * sp;
                    along += GaussW[q] * half * s * j1;
                    up += GaussW[q] * half * z * j0;
                }
                a0 = b0;
            }
        }
    }
}
