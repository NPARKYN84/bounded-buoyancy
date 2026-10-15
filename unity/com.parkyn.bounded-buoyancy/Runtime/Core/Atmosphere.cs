// Bounded Buoyancy for Unity. Engine-free core. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System;
using System.Collections.Generic;

namespace BoundedBuoyancy.Core
{
    /// <summary>
    /// The standard atmosphere (the US Standard Atmosphere, 1976, the same as the International Standard Atmosphere to
    /// 32 km): the temperature falls 6.5 °C for each kilometre of geopotential height up to 11 km and stays the same
    /// above, and the pressure falls with the weight of the air above. Damp air is less dense than dry, as water vapour
    /// is lighter than the air it replaces.
    /// </summary>
    public static class Atmosphere
    {
        /// <summary>The fall in temperature with height to 11 km, K/m.</summary>
        public const double Lapse = 0.0065;
        /// <summary>Geopotential height above which the temperature stays the same, m.</summary>
        public const double Tropopause = 11000.0;
        /// <summary>The Earth's radius the standard atmosphere's heights are worked with, m.</summary>
        public const double Earth = 6356766.0;
        public const double G0 = 9.80665;
        /// <summary>Molar mass of dry air, kg/mol.</summary>
        public const double Molar = 0.0289644;
        /// <summary>Gas constant, J/(mol K).</summary>
        public const double RGas = 8.3144598;
        /// <summary>Specific gas constants of dry air and of water vapour, J/(kg K).</summary>
        public const double RDry = 287.058, RVapour = 461.495;
        /// <summary>Sea-level temperature (°C) and pressure (hPa) of the standard atmosphere.</summary>
        public const double SeaTemperature = 15.0, SeaPressure = 1013.25;

        /// <summary>Temperature (K) and pressure (Pa) of the standard atmosphere `height` metres above sea level, with
        /// `seaTemperature` °C and `seaPressure` hPa at sea level.</summary>
        public static void StandardAir(double height, double seaTemperature, double seaPressure, out double temperature, out double pressure)
        {
            double h = Earth * height / (Earth + height);   // geopotential height
            double t0 = seaTemperature + 273.15;
            double power = G0 * Molar / (RGas * Lapse);
            double p0 = seaPressure * 100.0;
            if (h <= Tropopause)
            {
                temperature = t0 - Lapse * h;
                pressure = p0 * Math.Pow(temperature / t0, power);
                return;
            }
            double t11 = t0 - Lapse * Tropopause;
            temperature = t11;
            pressure = p0 * Math.Pow(t11 / t0, power) * Math.Exp(-G0 * Molar * (h - Tropopause) / (RGas * t11));
        }

        /// <summary>Density of the air `height` metres above sea level, kg/m³, in the standard atmosphere with
        /// `seaTemperature` °C and `seaPressure` hPa at sea level, at `humidity` % relative humidity, and `warmer` °C warmer
        /// than the air around (at the same pressure).</summary>
        public static double Density(double height, double seaTemperature = SeaTemperature, double seaPressure = SeaPressure,
            double humidity = 0.0, double warmer = 0.0)
        {
            double t0, p;
            StandardAir(height, seaTemperature, seaPressure, out t0, out p);
            double t = Math.Max(t0 + warmer, 1.0);
            double vapour = humidity > 0.0 ? humidity * 0.01 * VapourPressure(t - 273.15) : 0.0;
            return (p - vapour) / (RDry * t) + vapour / (RVapour * t);
        }

        /// <summary>Saturation pressure of water vapour over water at `celsius` °C, Pa (Magnus' formula, Alduchov and
        /// Eskridge, 1996).</summary>
        public static double VapourPressure(double celsius)
        {
            return 610.94 * Math.Exp(17.625 * celsius / (celsius + 243.04));
        }

        /// <summary>Speed of sound in dry air at `kelvin`, m/s: √(γ R T), γ = 1.4 (340.29 at 15 °C).</summary>
        public static double SpeedOfSound(double kelvin)
        {
            return Math.Sqrt(1.4 * RDry * Math.Max(kelvin, 1.0));
        }
    }

    /// <summary>
    /// A warm (or cold) place in the air: a thermal over sunlit ground, the hot air over a fire, a cold pool in a valley.
    /// Air there is Warming °C warmer at its middle, less toward its edge and none beyond it, so less dense at the same
    /// pressure; and it rises at Updraft at its middle, carrying bodies up as wind does. A column standing up from
    /// Position along Rotation's Y, of Radius and Height (fading out over its top tenth), or a ball of Radius.
    /// </summary>
    public sealed class ThermalField
    {
        public V3 Position;
        public M3 Rotation = M3.Identity;
        public bool Ball;
        public double Radius = 50.0;
        public double Height = 1000.0;
        public double Warming = 2.0;
        public double Updraft = 2.0;

        /// <summary>How strong it is at the world point p: 1 at its middle, falling smoothly to 0 at its edge.</summary>
        public double StrengthAt(V3 p)
        {
            V3 l = Rotation.MulT(p - Position);
            double r2, rr = Math.Max(Radius * Radius, 1e-12);
            if (Ball)
            {
                r2 = l.LengthSquared / rr;
            }
            else
            {
                if (l.y < 0.0 || l.y > Height)
                    return 0.0;
                r2 = (l.x * l.x + l.z * l.z) / rr;
                double top = WaterVolume.Clamp((Height - l.y) / (0.1 * Height), 0.0, 1.0);
                r2 = 1.0 - (1.0 - Math.Min(r2, 1.0)) * top;
            }
            if (r2 >= 1.0)
                return 0.0;
            double f = 1.0 - r2;
            return f * f;
        }

        /// <summary>How much warmer the air is at p for this thermal, °C.</summary>
        public double WarmingAt(V3 p) { return Warming * StrengthAt(p); }

        /// <summary>The rising air at p (world velocity, m/s).</summary>
        public V3 UpdraftAt(V3 p) { return Rotation.Y.Normalized() * (Updraft * StrengthAt(p)); }
    }

    /// <summary>
    /// The air of a world: the standard atmosphere from the day's Temperature (°C), Pressure (hPa) and Humidity (%) at
    /// SeaLevel (world height), with any Thermals warming it. A body whose core has Air set uses it in place of its own
    /// fixed AirDensity and AirHalfHeight.
    /// </summary>
    public sealed class AirField
    {
        public double SeaLevel;
        public double Temperature = Atmosphere.SeaTemperature;
        public double Pressure = Atmosphere.SeaPressure;
        public double Humidity;
        public readonly List<ThermalField> Thermals = new List<ThermalField>();

        double Warmer(V3 p)
        {
            double w = 0.0;
            for (int i = 0; i < Thermals.Count; i++)
                w += Thermals[i].WarmingAt(p);
            return w;
        }

        /// <summary>Density of the air at the world point p, kg/m³.</summary>
        public double DensityAt(V3 p)
        {
            return Atmosphere.Density(p.y - SeaLevel, Temperature, Pressure, Humidity, Warmer(p));
        }

        /// <summary>Temperature of the air at the world point p, K.</summary>
        public double KelvinAt(V3 p)
        {
            double t, pr;
            Atmosphere.StandardAir(p.y - SeaLevel, Temperature, Pressure, out t, out pr);
            return t + Warmer(p);
        }

        /// <summary>Speed of sound in the air at the world point p, m/s.</summary>
        public double SoundSpeedAt(V3 p) { return Atmosphere.SpeedOfSound(KelvinAt(p)); }

        /// <summary>The rising air of the thermals at p (world velocity, m/s).</summary>
        public V3 UpdraftAt(V3 p)
        {
            V3 u = V3.Zero;
            for (int i = 0; i < Thermals.Count; i++)
                u += Thermals[i].UpdraftAt(p);
            return u;
        }
    }
}
