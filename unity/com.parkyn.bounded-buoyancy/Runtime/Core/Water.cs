// Bounded Buoyancy for Unity. Engine-free core. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System;

namespace BoundedBuoyancy.Core
{
    /// <summary>
    /// Two travelling sine waves with the speeds of deep-water waves for their length. The second wave is
    /// shorter, turned and lower, so the surface does not look like a single washboard.
    /// </summary>
    public struct WaveSet
    {
        public const double SecondLength = 0.63;     // second wave: shorter,
        public const double SecondTurnDegrees = 35.0; // turned this many degrees,
        public const double SecondHeight = 0.4;      // this fraction of the height,
        public const double SecondPhase = 1.3;       // and out of step by this much

        public double Height, K1x, K1z, K2x, K2z, W1, W2;

        /// <summary>Waves of `height` (largest crest above the mean) and `length` (crest to crest), travelling
        /// `directionDegrees` round the vertical from +X towards +Z, under gravity `g`.</summary>
        public static WaveSet Create(double height, double length, double directionDegrees, double g)
        {
            length = Math.Max(length, 0.01);
            double a1 = directionDegrees * Math.PI / 180.0;
            double a2 = (directionDegrees + SecondTurnDegrees) * Math.PI / 180.0;
            double n1 = 2.0 * Math.PI / length;
            double n2 = 2.0 * Math.PI / (length * SecondLength);
            g = Math.Max(g, 0.0);
            return new WaveSet
            {
                Height = Math.Max(height, 0.0),
                K1x = Math.Cos(a1) * n1, K1z = Math.Sin(a1) * n1,
                K2x = Math.Cos(a2) * n2, K2z = Math.Sin(a2) * n2,
                W1 = Math.Sqrt(g * n1),   // deep-water waves: frequency = sqrt(g x wave number)
                W2 = Math.Sqrt(g * n2),
            };
        }

        /// <summary>Height of the surface above its mean at (lx, lz) in the water's own frame, at time t.</summary>
        public double Offset(double lx, double lz, double t)
        {
            if (Height <= 0.0)
                return 0.0;
            return Height * (Math.Sin(K1x * lx + K1z * lz - W1 * t)
                + SecondHeight * Math.Sin(K2x * lx + K2z * lz - W2 * t + SecondPhase));
        }

        /// <summary>Slope of the surface along the water's own x and z, at (lx, lz) and time t.</summary>
        public void Slope(double lx, double lz, double t, out double dx, out double dz)
        {
            if (Height <= 0.0)
            {
                dx = 0.0;
                dz = 0.0;
                return;
            }
            double c1 = Height * Math.Cos(K1x * lx + K1z * lz - W1 * t);
            double c2 = Height * SecondHeight * Math.Cos(K2x * lx + K2z * lz - W2 * t + SecondPhase);
            dx = c1 * K1x + c2 * K2x;
            dz = c1 * K1z + c2 * K2z;
        }

        /// <summary>The most the surface rises or falls from its mean height.</summary>
        public double MaxWave { get { return Height * (1.0 + SecondHeight); } }

        /// <summary>Upward acceleration of the water at (lx, lz) in the water's frame, `y` metres above the water's
        /// average level, at time t (m/s²): for each wave, minus its height x its frequency² x the sine of its phase,
        /// dying away with depth as e^(k z), z the average height of the water that is there now (MeanHeight). Water
        /// under a crest is slowing as it rises, so it accelerates downwards. Each wave's part is multiplied by
        /// `average1` or `average2`, for an average over a footprint.</summary>
        public double VerticalAcceleration(double lx, double y, double lz, double t, double average1 = 1.0, double average2 = 1.0)
        {
            if (Height <= 0.0)
                return 0.0;
            double k1 = Math.Sqrt(K1x * K1x + K1z * K1z), k2 = Math.Sqrt(K2x * K2x + K2z * K2z);
            double h1 = Height * Math.Sin(K1x * lx + K1z * lz - W1 * t);
            double h2 = Height * SecondHeight * Math.Sin(K2x * lx + K2z * lz - W2 * t + SecondPhase);
            double z = MeanHeight(y, h1, k1, h2, k2);
            return -(W1 * W1 * h1 * average1 * Math.Exp(k1 * z) + W2 * W2 * h2 * average2 * Math.Exp(k2 * z));
        }

        /// <summary>
        /// The average height z (no more than 0) of the water that is `y` metres above the average level now, where the
        /// waves have lifted the surface there by h1 + h2 (h1 and h2 the two waves' parts, k1 and k2 their wave numbers):
        /// water at average height z is lifted by h e^(k z), so z solves z = y − h1 e^(k1 z) − h2 e^(k2 z). Two steps of
        /// Newton's method from y − h1 − h2 find it to well under a millimetre for waves that do not break. A body under
        /// the water that moves with it then keeps the same z, so it feels the same strength of wave as it rises and
        /// falls, as the water round it does.
        /// </summary>
        public static double MeanHeight(double y, double h1, double k1, double h2, double k2)
        {
            double z = Math.Min(y - h1 - h2, 0.0);
            for (int i = 0; i < 2; i++)
            {
                double e1 = h1 * Math.Exp(k1 * z), e2 = h2 * Math.Exp(k2 * z);
                double slope = Math.Max(1.0 + k1 * e1 + k2 * e2, 0.2);   // above a half for waves that do not break
                z = Math.Min(z - (z - y + e1 + e2) / slope, 0.0);
            }
            return z;
        }
    }

    /// <summary>
    /// The surface of another water system (an ocean renderer, say), for bodies to float on its waves.
    /// </summary>
    public interface ISurfaceHeights
    {
        /// <summary>Set heights[i] to the height (world y) of the surface above or below the world point points[i],
        /// for i below count. Bodies ask for all the points they need at once.</summary>
        void HeightsAt(V3[] points, double[] heights, int count);

        /// <summary>The most the surface rises or falls from its average height. Bodies further than twice this from
        /// the surface are not measured.</summary>
        double MaxWave { get; }
    }

    /// <summary>
    /// A water system that also knows how its water flows (a river's current). An ISurfaceHeights that implements
    /// this carries bodies with its own current instead of the water volume's.
    /// </summary>
    public interface ISurfaceFlow
    {
        /// <summary>The water's velocity (world frame) at the world point p; false to use the water volume's current.</summary>
        bool FlowAt(V3 p, out V3 flow);
    }

    /// <summary>
    /// A water system that also knows how its water accelerates up and down in its waves. An ISurfaceHeights that
    /// implements this pushes bodies with the water's motion, as the water node's own waves do; without it, bodies on it
    /// are buoyed by its surface alone.
    /// </summary>
    public interface ISurfaceMotion
    {
        /// <summary>The water's upward acceleration (m/s²) at the world point p in the water, averaged over a footprint
        /// `across` (world X) by `along` (world Z) metres centred on p (both 0 for the point itself); false if not known.
        /// For waves made of sines, each wave's part is −height x ω² x sin(phase) x e^(k z), ω its frequency, k its wave
        /// number and z the average height of the water now at p: p's height above the water's average level less how
        /// far the waves have lifted that water, no more than 0 (WaveSet.MeanHeight works it out for two waves). A
        /// footprint can be averaged at the four points p ± (across, along) / (2√3).</summary>
        bool VerticalAccelerationAt(V3 p, double across, double along, out double acceleration);
    }

    /// <summary>The water surface a body is measured against: flat at Level, with waves, or another water system's
    /// surface (Source).</summary>
    public struct WaterSurface
    {
        public double Level;
        public WaveSet Waves;
        public double Time;
        /// <summary>The water's position and turn, to put world points into the water's frame for the waves.</summary>
        public BodyPose Frame;
        /// <summary>Another water system's surface; when set, it is used instead of Level and Waves.</summary>
        public ISurfaceHeights Source;

        public bool Wavy { get { return Source != null || Waves.Height > 0.0; } }
        public double MaxWave { get { return Source != null ? Math.Max(Source.MaxWave, 0.0) : (Waves.Height > 0.0 ? Waves.MaxWave : 0.0); } }

        public static WaterSurface Flat(double level)
        {
            return new WaterSurface { Level = level, Frame = new BodyPose(V3.Zero, M3.Identity) };
        }

        [ThreadStatic] static V3[] onePoint;
        [ThreadStatic] static double[] oneHeight;

        /// <summary>Height (world y) of the surface above or below the world point p.</summary>
        public double HeightAt(V3 p)
        {
            if (Source != null)
            {
                if (onePoint == null)
                {
                    onePoint = new V3[1];
                    oneHeight = new double[1];
                }
                onePoint[0] = p;
                Source.HeightsAt(onePoint, oneHeight, 1);
                return oneHeight[0];
            }
            if (Waves.Height <= 0.0)
                return Level;
            V3 l = Frame.Inverse(p);
            return Level + Waves.Offset(l.x, l.z, Time);
        }

        /// <summary>Upward acceleration of the water in the waves at the world point p (m/s²): from the waves, or from
        /// another water system if it says (ISurfaceMotion), otherwise 0. With `across` and `along` (world X and Z, m),
        /// averaged over that footprint centred on p, at the four points that average a uniform rectangle exactly for
        /// anything varying no faster than a cubic (two-point Gauss–Legendre each way); for the waves, each wave's part
        /// is then multiplied by cos(kx hx) cos(kz hz), hx and hz the points' offsets.</summary>
        public double VerticalAccelerationAt(V3 p, double across = 0.0, double along = 0.0)
        {
            double hx = across / (2.0 * Math.Sqrt(3.0)), hz = along / (2.0 * Math.Sqrt(3.0));
            if (Source != null)
            {
                double a;
                var motion = Source as ISurfaceMotion;
                return motion != null && motion.VerticalAccelerationAt(p, across, along, out a) ? a : 0.0;
            }
            if (Waves.Height <= 0.0)
                return 0.0;
            V3 l = Frame.Inverse(p);
            V3 k1 = Frame.Basis.Mul(new V3(Waves.K1x, 0.0, Waves.K1z)), k2 = Frame.Basis.Mul(new V3(Waves.K2x, 0.0, Waves.K2z));
            return Waves.VerticalAcceleration(l.x, p.y - Level, l.z, Time,
                Math.Cos(k1.x * hx) * Math.Cos(k1.z * hz), Math.Cos(k2.x * hx) * Math.Cos(k2.z * hz));
        }
    }

    /// <summary>What a body finds at its position: the water it is in (if any) and its surface.</summary>
    public struct WaterSample
    {
        /// <summary>False when the scene has water volumes but the body is in none of them.</summary>
        public bool HasWater;
        public double Density;
        /// <summary>Velocity of the water at the body (world frame).</summary>
        public V3 Flow;
        public WaterSurface Surface;
        /// <summary>The water this is (its WaterVolume, say), so a body can tell when it has moved into other water.</summary>
        public object Owner;

        public static WaterSample Flat(double level, double density)
        {
            return new WaterSample { HasWater = true, Density = density, Surface = WaterSurface.Flat(level) };
        }

        public static WaterSample None(double level, double density)
        {
            return new WaterSample { HasWater = false, Density = density, Surface = WaterSurface.Flat(level) };
        }
    }

    /// <summary>
    /// A box of water whose top face is the surface: position and turn, size, density, waves and current.
    /// The Unity component keeps one of these up to date; bodies ask it what they find.
    /// </summary>
    public sealed class WaterVolume
    {
        /// <summary>Middle of the surface (world).</summary>
        public V3 Position;
        /// <summary>The water's axes in world space (a turn about the vertical; tilts are not meant).</summary>
        public M3 Rotation = M3.Identity;
        /// <summary>Width (x), depth (y) and length (z), in metres.</summary>
        public V3 Size = new V3(10, 3, 10);
        public double Density = 1000.0;
        /// <summary>Velocity of the water in its own frame.</summary>
        public V3 Current;
        /// <summary>Width of the slower water along each bank; 0 keeps the current the same to the sides.</summary>
        public double CurrentEdge;
        /// <summary>Wave clock in seconds.</summary>
        public double Time;
        /// <summary>Another water system's surface, used instead of the waves when set.</summary>
        public ISurfaceHeights Source;

        WaveSet waves;
        double waveHeight = -1.0, waveLength, waveDirection, waveGravity;

        public WaveSet Waves { get { return waves; } }

        /// <summary>Set the waves; worked out again only when something changed.</summary>
        public void SetWaves(double height, double length, double directionDegrees, double g)
        {
            if (height == waveHeight && length == waveLength && directionDegrees == waveDirection && g == waveGravity)
                return;
            waveHeight = height;
            waveLength = length;
            waveDirection = directionDegrees;
            waveGravity = g;
            waves = WaveSet.Create(height, length, directionDegrees, g);
        }

        BodyPose Frame { get { return new BodyPose(Position, Rotation); } }

        public WaterSurface Surface
        {
            get { return new WaterSurface { Level = Position.y, Waves = waves, Time = Time, Frame = Frame, Source = Source }; }
        }

        public double HeightAt(V3 p) { return Surface.HeightAt(p); }

        public double MaxWave { get { return Surface.MaxWave; } }

        /// <summary>Whether the world point p lies over or in the water: inside it across, and above its floor.</summary>
        public bool Contains(V3 p)
        {
            V3 l = Frame.Inverse(p);
            return Math.Abs(l.x) <= 0.5 * Size.x && Math.Abs(l.z) <= 0.5 * Size.z && l.y >= -Size.y;
        }

        /// <summary>Velocity of the water at the world point p (world frame), slowing towards the banks if
        /// CurrentEdge is set.</summary>
        public V3 CurrentAt(V3 p)
        {
            if (Current.x == 0.0 && Current.y == 0.0 && Current.z == 0.0)
                return V3.Zero;
            double f = 1.0;
            if (CurrentEdge > 0.0)
            {
                double fx = Current.x, fz = Current.z;
                double n = Math.Sqrt(fx * fx + fz * fz);
                if (n > 0.0)
                {
                    double ax = -fz / n, az = fx / n;   // across the flow
                    V3 l = Frame.Inverse(p);
                    double half = 0.5 * (Math.Abs(ax) * Size.x + Math.Abs(az) * Size.z);
                    f = Clamp((half - Math.Abs(ax * l.x + az * l.z)) / CurrentEdge, 0.0, 1.0);
                }
            }
            return Rotation.Mul(Current * f);
        }

        public WaterSample Sample(V3 p)
        {
            V3 flow;
            var flowing = Source as ISurfaceFlow;
            if (flowing == null || !flowing.FlowAt(p, out flow))
                flow = CurrentAt(p);
            return new WaterSample { HasWater = true, Density = Density, Flow = flow, Surface = Surface, Owner = this };
        }

        internal static double Clamp(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }

    /// <summary>Wind with gusts that travel downwind, so nearby bodies feel each gust one after another.</summary>
    public sealed class WindField
    {
        public V3 Position;
        public M3 Rotation = M3.Identity;
        /// <summary>Velocity of the wind in its own frame (m/s).</summary>
        public V3 Velocity = new V3(5, 0, 0);
        public double GustStrength = 0.3;
        public double GustPeriod = 5.0;
        public double DirectionSwingDegrees = 10.0;
        public bool Everywhere = true;
        public V3 Size = new V3(10, 10, 10);
        /// <summary>Wind clock in seconds.</summary>
        public double Time;

        /// <summary>Velocity of the wind at the world point p (world frame), gusts included.</summary>
        public V3 VelocityAt(V3 p)
        {
            V3 baseWind = Rotation.Mul(Velocity);
            if (!Everywhere)
            {
                V3 l = Rotation.MulT(p - Position);
                if (Math.Abs(l.x) > 0.5 * Size.x || Math.Abs(l.y) > 0.5 * Size.y || Math.Abs(l.z) > 0.5 * Size.z)
                    return V3.Zero;
            }
            return Gust(baseWind, p, Time, GustStrength, GustPeriod, DirectionSwingDegrees);
        }

        /// <summary>The wind `baseWind` at the point p and time t with gusts: the phase depends on the time and
        /// on the distance downwind.</summary>
        public static V3 Gust(V3 baseWind, V3 p, double t, double strength, double period, double swingDegrees)
        {
            double speed2 = baseWind.LengthSquared;
            if (speed2 < 1e-12 || (strength <= 0.0 && swingDegrees <= 0.0))
                return baseWind;
            double s = (t - p.Dot(baseWind) / speed2) / Math.Max(period, 0.01);
            double g = Pattern(s);
            double turn = swingDegrees * Math.PI / 180.0 * Math.Sin(2.0 * Math.PI * 0.37 * s + 2.1);
            double c = Math.Cos(turn), sn = Math.Sin(turn);
            var turned = new V3(baseWind.x * c + baseWind.z * sn, baseWind.y, -baseWind.x * sn + baseWind.z * c);
            return turned * (1.0 + strength * g);
        }

        /// <summary>The gust pattern: three sines, between -1 and 1.</summary>
        public static double Pattern(double s)
        {
            return 0.6 * Math.Sin(2.0 * Math.PI * s) + 0.3 * Math.Sin(2.0 * Math.PI * 2.3 * s + 1.7)
                + 0.1 * Math.Sin(2.0 * Math.PI * 5.1 * s + 0.4);
        }
    }
}
