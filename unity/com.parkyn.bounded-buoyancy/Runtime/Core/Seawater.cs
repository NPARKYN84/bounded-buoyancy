// Bounded Buoyancy for Unity. Engine-free core. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System;

namespace BoundedBuoyancy.Core
{
    /// <summary>
    /// Real water's density, from its temperature, its salt and the pressure on it: the UNESCO equation of state of
    /// sea water (EOS-80; Millero and Poisson, 1981, with the secant bulk modulus of Millero and others, 1980), valid
    /// from -2 to 40 °C, 0 to 42 g/kg and 0 to 1000 bar. Fresh water is salinity 0. With it, a water's temperature and
    /// salt change from the surface's to the deep water's across a thermocline (and halocline).
    /// </summary>
    public static class Seawater
    {
        /// <summary>Density of sea water (or fresh, salinity 0), kg/m³, at `salinity` g/kg, `temperature` °C and
        /// `pressure` bar above the air's.</summary>
        public static double Density(double salinity, double temperature, double pressure)
        {
            double t = temperature;
            double sv = salinity;
            double s15 = sv * Math.Sqrt(Math.Max(sv, 0.0));
            double rw = 999.842594 + t * (6.793952e-2 + t * (-9.095290e-3 + t * (1.001685e-4 + t * (-1.120083e-6 + t * 6.536332e-9))));
            double r0 = rw + sv * (0.824493 + t * (-4.0899e-3 + t * (7.6438e-5 + t * (-8.2467e-7 + t * 5.3875e-9))))
                + s15 * (-5.72466e-3 + t * (1.0227e-4 - t * 1.6546e-6)) + 4.8314e-4 * sv * sv;
            if (pressure <= 0.0)
                return r0;
            double kw = 19652.21 + t * (148.4206 + t * (-2.327105 + t * (1.360477e-2 - t * 5.155288e-5)));
            double k0 = kw + sv * (54.6746 + t * (-0.603459 + t * (1.09987e-2 - t * 6.1670e-5))) + s15 * (7.944e-2 + t * (1.6483e-2 - t * 5.3009e-4));
            double a = 3.239908 + t * (1.43713e-3 + t * (1.16092e-4 - t * 5.77905e-7)) + sv * (2.2838e-3 + t * (-1.0981e-5 - t * 1.6078e-6)) + 1.91075e-4 * s15;
            double b = 8.50935e-5 + t * (-6.12293e-6 + t * 5.2787e-8) + sv * (-9.9348e-7 + t * (2.0816e-8 + t * 9.1697e-10));
            double p = pressure;
            return r0 / (1.0 - p / (k0 + a * p + b * p * p));
        }

        /// <summary>How far `depth` metres down is through a thermocline centred `thermoclineDepth` down: 0 at the surface
        /// (so the surface has exactly its own water), 1 well below; the change takes about as many metres again as the
        /// thermocline's depth (a tanh profile).</summary>
        public static double DeepShare(double depth, double thermoclineDepth)
        {
            double d = Math.Max(depth, 0.0);
            double z = Math.Max(thermoclineDepth, 1e-6);
            double deep = 0.5 * (1.0 + Math.Tanh((d - z) / (0.5 * z)));
            double start = 0.5 * (1.0 + Math.Tanh(-2.0));
            return WaterVolume.Clamp((deep - start) / (1.0 - start), 0.0, 1.0);
        }
    }
}
