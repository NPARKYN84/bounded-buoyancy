// Bounded Buoyancy for Unity: the look of the demo's fish (perch, trout, bass and mackerel) and of its submarine's hull,
// as plain data (meshes and painted textures), with no Unity types in it, so that it can be built and checked outside
// Unity. BoundedBuoyancyDemoTackle.cs turns it into GameObjects. The same fish and hull as the Godot plugin's demo
// (demo_tackle.gd, demo_sub_look.gd), in Unity's axes: nose or bow toward +Z.
// Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System;
using System.Collections.Generic;

namespace BoundedBuoyancy.Samples
{
    public static class TackleLook
    {
        public const int Perch = 0, Trout = 1, Bass = 2, Mackerel = 3;
        public static readonly string[] Names = { "perch", "trout", "bass", "mackerel" };

        // Each kind's shape: its depth (half its height at the deepest, as a share of its length), how far back it is
        // deepest (0 the nose, 1 the tail), how much higher its back rises than its belly falls, its width as a share of
        // its depth, and its tail's fork (negative: rounded).
        struct Shape { public float Depth, Peak, Hump, Wide, Fork; }
        static readonly Shape[] Shapes =
        {
            new Shape { Depth = 0.155f, Peak = 0.36f, Hump = 1.35f, Wide = 0.5f, Fork = 0.35f },
            new Shape { Depth = 0.115f, Peak = 0.42f, Hump = 1.08f, Wide = 0.58f, Fork = 0.12f },
            new Shape { Depth = 0.14f, Peak = 0.38f, Hump = 1.12f, Wide = 0.55f, Fork = -0.08f },
            new Shape { Depth = 0.105f, Peak = 0.42f, Hump = 1.0f, Wide = 0.6f, Fork = 0.6f },
        };

        // The fish's paint is one texture: the body on the left (u from the nose to the tail over 0..BodyU, v from the
        // belly to the back), and each fin in a strip of its own to the right (u along the fin, v out from the body).
        const float BodyU = 0.7f, FinU0 = 0.72f, FinStrip = 0.056f, FinWide = 0.05f;
        const int SoftTop = 0, SpinyTop = 1, Tail = 2, Lower = 3, Side = 4;

        static float Smooth(float a, float b, float x)
        {
            float t = Math.Max(0f, Math.Min(1f, (x - a) / (b - a)));
            return t * t * (3f - 2f * t);
        }

        static float Fract(float x) { return x - (float)Math.Floor(x); }
        static float Sin(float x) { return (float)Math.Sin(x); }
        static float Cos(float x) { return (float)Math.Cos(x); }
        static float Pow(float x, float p) { return (float)Math.Pow(x, p); }
        const float Pi = (float)Math.PI;

        /// <summary>The body's half-height above (up &gt; 0) or below, and its half-width, `t` of the way back.</summary>
        static void Radius(Shape s, float t, float up, out float h, out float w)
        {
            float u = t < s.Peak ? 0.5f * t / s.Peak : 0.5f + 0.5f * (t - s.Peak) / (1f - s.Peak);
            float g = Pow(Sin(Pi * Math.Max(0f, Math.Min(1f, u * 0.9f + 0.05f))), 0.62f);
            float d = s.Depth * g;
            h = d * (up > 0f ? s.Hump : 0.88f);
            w = d * s.Wide;
        }

        /// <summary>A point on the body (of length 1, nose toward +Z), `t` of the way back.</summary>
        static P3 Surface(Shape s, float t, float up, float side)
        {
            float h, w;
            Radius(s, t, up, out h, out w);
            return new P3(side * w, up * h, 0.5f - t);
        }

        /// <summary>A fish one metre long (scale it to its length), nose toward +Z: its body and its fins, both sides of
        /// each fin drawn. Paint it with FishPaint.</summary>
        public static MeshData Fish(int kind)
        {
            Shape s = Shapes[kind];
            var m = new MeshData();
            const int rings = 24, around = 18;
            var idx = new int[rings + 1, around];
            for (int i = 0; i <= rings; i++)
            {
                float t = (float)i / rings;
                for (int j = 0; j < around; j++)
                {
                    float a = 2f * Pi * j / around;
                    idx[i, j] = m.Add(Surface(s, t, Sin(a), Cos(a)), BodyU * t, 0.5f + 0.5f * Sin(a));
                }
            }
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < around; j++)
                {
                    int j1 = (j + 1) % around;
                    int a = idx[i, j], b = idx[i, j1], c = idx[i + 1, j], d = idx[i + 1, j1];
                    m.Tri(a, c, b);
                    m.Tri(b, c, d);
                }
            int nose = m.Add(new P3(0f, -0.02f, 0.51f), 0f, 0.5f);
            int end = m.Add(new P3(0f, 0f, -0.505f), BodyU, 0.5f);
            for (int j = 0; j < around; j++)
            {
                int j1 = (j + 1) % around;
                m.Tri(nose, idx[0, j], idx[0, j1]);
                m.Tri(end, idx[rings, j1], idx[rings, j]);
            }
            Fins(m, kind, s);
            return m;
        }

        /// <summary>A fin as a strip from `base` (points on the body) to `tip`, painted from its strip of the texture; both
        /// sides.</summary>
        static void Fin(MeshData m, List<P3> bases, List<P3> tips, int strip)
        {
            int n = bases.Count;
            float u0 = FinU0 + strip * FinStrip;
            for (int side = 0; side < 2; side++)
            {
                var b = new int[n];
                var t = new int[n];
                for (int k = 0; k < n; k++)
                {
                    float along = u0 + FinWide * k / (n - 1);
                    b[k] = m.Add(bases[k], along, 0.02f);
                    t[k] = m.Add(tips[k], along, 0.98f);
                }
                for (int k = 0; k < n - 1; k++)
                {
                    if (side == 0)
                    {
                        m.Tri(b[k], t[k], b[k + 1]);
                        m.Tri(b[k + 1], t[k], t[k + 1]);
                    }
                    else
                    {
                        m.Tri(b[k], b[k + 1], t[k]);
                        m.Tri(b[k + 1], t[k + 1], t[k]);
                    }
                }
            }
        }

        /// <summary>A fin along the back (or under the belly) from t0 to t1, `high` at its tallest, raked back; a spiny one
        /// has a ragged edge.</summary>
        static void RidgeFin(MeshData m, Shape s, float t0, float t1, float high, bool below, int strip, bool spiny)
        {
            var bases = new List<P3>();
            var tips = new List<P3>();
            const int n = 12;
            float sign = below ? -1f : 1f;
            for (int k = 0; k < n; k++)
            {
                float a = (float)k / (n - 1);
                float t = t0 + (t1 - t0) * a;
                float h, w;
                Radius(s, t, sign, out h, out w);
                var p = new P3(0f, sign * h, 0.5f - t);
                float f = high * Pow(Sin(Pi * Math.Max(0f, Math.Min(1f, a * 0.85f + 0.12f))), 0.7f);
                if (spiny)
                    f *= (k % 2 == 0 ? 1f : 0.78f) * (1.1f - 0.5f * a);
                bases.Add(p - new P3(0f, 0.003f * sign, 0f));
                tips.Add(p + new P3(0f, sign * f, -0.35f * f));
            }
            Fin(m, bases, tips, strip);
        }

        static void Fins(MeshData m, int kind, Shape s)
        {
            // The tail: a fan from the narrow end of the body, forked or rounded.
            const float tb = 0.955f;
            float hb, wb;
            Radius(s, tb, 1f, out hb, out wb);
            var bases = new List<P3>();
            var tips = new List<P3>();
            float span = kind == Perch ? 0.15f : kind == Mackerel ? 0.17f : 0.14f;
            for (int k = 0; k < 13; k++)
            {
                float u = 1f - 2f * k / 12f;
                float reach = 0.17f * (1f - s.Fork * (1f - Math.Abs(u)));
                bases.Add(new P3(0f, u * hb, 0.5f - tb));
                tips.Add(new P3(0f, u * span, 0.5f - tb - reach));
            }
            Fin(m, bases, tips, Tail);
            switch (kind)
            {
                case Perch:
                    RidgeFin(m, s, 0.24f, 0.5f, 0.13f, false, SpinyTop, true);
                    RidgeFin(m, s, 0.53f, 0.73f, 0.09f, false, SoftTop, false);
                    break;
                case Trout:
                    RidgeFin(m, s, 0.36f, 0.53f, 0.10f, false, SoftTop, false);
                    RidgeFin(m, s, 0.76f, 0.82f, 0.035f, false, SoftTop, false);
                    break;
                case Bass:
                    RidgeFin(m, s, 0.28f, 0.5f, 0.095f, false, SpinyTop, true);
                    RidgeFin(m, s, 0.5f, 0.71f, 0.11f, false, SoftTop, false);
                    break;
                default:
                    RidgeFin(m, s, 0.3f, 0.42f, 0.09f, false, SpinyTop, true);
                    RidgeFin(m, s, 0.55f, 0.65f, 0.06f, false, SoftTop, false);
                    break;
            }
            RidgeFin(m, s, 0.62f, 0.76f, kind == Mackerel ? 0.05f : 0.085f, true, Lower, kind == Perch || kind == Bass);
            // The paired fins: pelvic fins under the belly, pectoral fins behind the gills, angled back.
            foreach (float side in new[] { -1f, 1f })
            {
                PairedFin(m, s, side, 0.33f, 0.41f, -0.85f, new P3(0.05f, -0.07f, 0.07f), Lower);
                PairedFin(m, s, side, 0.22f, 0.27f, -0.35f, new P3(0.07f, -0.025f, 0.1f), Side);
            }
        }

        static void PairedFin(MeshData m, Shape s, float side, float t0, float t1, float up, P3 o, int strip)
        {
            var bases = new List<P3>();
            var tips = new List<P3>();
            for (int k = 0; k < 3; k++)
            {
                float t = t0 + (t1 - t0) * k / 2f;
                float h, w;
                Radius(s, t, up, out h, out w);
                var p = new P3(side * w * (float)Math.Sqrt(1f - up * up) * 0.95f, up * h, 0.5f - t);
                bases.Add(p);
                float k1 = 1f - 0.3f * Math.Abs(k - 1f);
                tips.Add(p + new P3(side * o.x, o.y, -o.z) * k1);
            }
            Fin(m, bases, tips, strip);
        }

        /// <summary>Where the eyes are on a fish one metre long (the starboard one; the port one has -x), and their radius.</summary>
        public static P3 Eye(int kind, out float radius)
        {
            Shape s = Shapes[kind];
            float t = kind == Bass ? 0.095f : 0.085f;
            float h, w;
            Radius(s, t, 1f, out h, out w);
            radius = 0.03f;
            return new P3(w * 0.92f - 0.25f * radius, 0.3f * h, 0.5f - t);
        }

        // ---- Paint ---------------------------------------------------------------------------------------------------

        static float Hash(float x, float y)
        {
            float v = Sin(x * 127.1f + y * 311.7f) * 43758.5453f;
            return Fract(v);
        }

        /// <summary>Distance from (qx, qy) to the nearest of the spots scattered over a grid (a share `density` of its
        /// cells have one).</summary>
        static float Spots(float qx, float qy, float density)
        {
            float cx = (float)Math.Floor(qx), cy = (float)Math.Floor(qy);
            float best = 9f;
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                {
                    float x = cx + i, y = cy + j;
                    if (Hash(x, y) < density)
                    {
                        float ox = x + 0.5f + 0.5f * (Hash(x + 7.1f, y + 7.1f) - 0.5f);
                        float oy = y + 0.5f + 0.5f * (Hash(x + 3.3f, y + 3.3f) - 0.5f);
                        float d = (float)Math.Sqrt((qx - ox) * (qx - ox) + (qy - oy) * (qy - oy));
                        if (d < best)
                            best = d;
                    }
                }
            return best;
        }

        struct C3
        {
            public float r, g, b;
            public C3(float r, float g, float b) { this.r = r; this.g = g; this.b = b; }
            public static C3 Mix(C3 a, C3 b, float t) { return new C3(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t); }
            public static C3 operator *(C3 a, float k) { return new C3(a.r * k, a.g * k, a.b * k); }
        }

        /// <summary>RGBA bytes (row 0 at the bottom) of a fish's paint, for the mesh Fish makes: its colours, bars, spots,
        /// lateral line, gill cover and mouth, and its fins coloured by kind, with rays.</summary>
        public static byte[] FishPaint(int kind, int w, int h)
        {
            var px = new byte[w * h * 4];
            for (int r = 0; r < h; r++)
                for (int q = 0; q < w; q++)
                {
                    float u = (q + 0.5f) / w, v = (r + 0.5f) / h;
                    C3 c = u < (BodyU + FinU0) * 0.5f ? Body(kind, Math.Min(u / BodyU, 1f), 2f * v - 1f) : FinColour(kind, u, v);
                    int o = 4 * (r * w + q);
                    px[o] = (byte)Math.Max(0, Math.Min(255, (int)(c.r * 255f + 0.5f)));
                    px[o + 1] = (byte)Math.Max(0, Math.Min(255, (int)(c.g * 255f + 0.5f)));
                    px[o + 2] = (byte)Math.Max(0, Math.Min(255, (int)(c.b * 255f + 0.5f)));
                    px[o + 3] = 255;
                }
            return px;
        }

        static C3 Body(int kind, float t, float up)
        {
            C3 back, flank, belly;
            switch (kind)
            {
                case Perch: back = new C3(0.10f, 0.18f, 0.05f); flank = new C3(0.60f, 0.62f, 0.12f); belly = new C3(0.96f, 0.92f, 0.78f); break;
                case Trout: back = new C3(0.20f, 0.17f, 0.07f); flank = new C3(0.72f, 0.50f, 0.18f); belly = new C3(0.98f, 0.86f, 0.50f); break;
                case Bass: back = new C3(0.07f, 0.16f, 0.06f); flank = new C3(0.36f, 0.50f, 0.20f); belly = new C3(0.96f, 0.96f, 0.90f); break;
                default: back = new C3(0.05f, 0.30f, 0.34f); flank = new C3(0.70f, 0.76f, 0.80f); belly = new C3(0.97f, 0.97f, 0.98f); break;
            }
            C3 c = C3.Mix(flank, back, Smooth(-0.05f, 0.65f, up));
            c = C3.Mix(c, belly, Smooth(-0.2f, -0.6f, up));
            if (kind == Perch)
            {
                // Six or seven bold dark bars across the back, fading toward the belly.
                float b = 0.5f + 0.5f * Cos(2f * Pi * (t * 6.4f - 0.25f));
                float bar = Smooth(0.5f, 0.8f, b) * Smooth(0.2f, 0.26f, t) * Smooth(0.9f, 0.84f, t) * Smooth(-0.55f, -0.1f, up);
                c = C3.Mix(c, new C3(0.06f, 0.09f, 0.04f), bar * 0.88f);
            }
            else if (kind == Trout)
            {
                // Black spots over the back and upper flank; red ones with pale rings along the middle.
                float k = Spots(t * 13f, up * 2.6f, 0.6f);
                c = C3.Mix(c, new C3(0.04f, 0.03f, 0.02f), Smooth(0.3f, 0.22f, k) * Smooth(-0.3f, 0.05f, up) * Smooth(0.1f, 0.18f, t));
                float rd = Spots(t * 9f + 0.5f, up * 2.2f + 0.3f, 0.5f);
                float mid = Smooth(0.5f, 0.3f, Math.Abs(up + 0.05f)) * Smooth(0.2f, 0.28f, t) * Smooth(0.9f, 0.8f, t);
                c = C3.Mix(c, new C3(0.98f, 0.92f, 0.80f), (Smooth(0.42f, 0.36f, rd) - Smooth(0.28f, 0.24f, rd)) * mid);
                c = C3.Mix(c, new C3(0.85f, 0.08f, 0.04f), Smooth(0.28f, 0.22f, rd) * mid);
            }
            else if (kind == Bass)
            {
                // A dark ragged band of blotches from the eye to the tail, and mottling above it.
                float y0 = 0.06f + 0.06f * Sin(t * 47f) + 0.03f * Sin(t * 131f);
                float band = Smooth(0.24f, 0.12f, Math.Abs(up - y0)) * Smooth(0.1f, 0.16f, t) * (0.75f + 0.25f * Sin(t * 83f));
                c = C3.Mix(c, new C3(0.06f, 0.10f, 0.05f), band * 0.9f);
                float k = Spots(t * 14f, up * 3f, 0.5f);
                c = C3.Mix(c, new C3(0.10f, 0.16f, 0.07f), Smooth(0.35f, 0.2f, k) * Smooth(0.2f, 0.5f, up) * 0.6f);
            }
            else
            {
                // Wavy black stripes across the blue-green back.
                float wv = Sin(t * 70f + 3f * Sin(up * 9f + t * 20f));
                c = C3.Mix(c, new C3(0.02f, 0.05f, 0.06f), Smooth(0.45f, 0.8f, wv) * Smooth(0.1f, 0.35f, up) * Smooth(0.12f, 0.2f, t) * Smooth(0.9f, 0.82f, t));
            }
            // The lateral line, the edge of the gill cover, and the mouth.
            float line = 0.5f * Smooth(0.03f, 0f, Math.Abs(up - 0.22f + 0.1f * t)) * (t >= 0.22f ? 1f : 0f);
            c = C3.Mix(c, c * 1.35f, line);
            float gill = Smooth(0.014f, 0f, Math.Abs(t - (0.21f + 0.035f * up * up))) * (up >= -0.75f && up <= 0.65f ? 1f : 0f);
            c = c * (1f - 0.45f * gill);
            float gape = kind == Bass ? 0.13f : 0.07f;
            c = c * (1f - 0.7f * Smooth(0.04f, 0f, Math.Abs(up + 0.12f - 0.6f * t)) * (t <= gape ? 1f : 0f));
            // Scales.
            c = c * (0.94f + 0.06f * Sin(t * 260f + up * 22f) * Sin(t * 260f - up * 22f));
            return c;
        }

        static C3 FinColour(int kind, float u, float v)
        {
            int strip = Math.Max(0, Math.Min(4, (int)((u - FinU0) / FinStrip)));
            float along = Math.Max(0f, Math.Min(1f, (u - FinU0 - strip * FinStrip) / FinWide));
            float output = v;
            C3 top, tail, low, side;
            switch (kind)
            {
                case Perch: top = new C3(0.22f, 0.26f, 0.16f); tail = new C3(0.70f, 0.40f, 0.12f); low = new C3(1f, 0.30f, 0.04f); side = new C3(0.95f, 0.55f, 0.15f); break;
                case Trout: top = new C3(0.36f, 0.28f, 0.14f); tail = new C3(0.34f, 0.26f, 0.13f); low = new C3(0.90f, 0.58f, 0.25f); side = new C3(0.80f, 0.52f, 0.24f); break;
                case Bass: top = new C3(0.16f, 0.24f, 0.11f); tail = new C3(0.18f, 0.26f, 0.12f); low = new C3(0.45f, 0.52f, 0.30f); side = new C3(0.50f, 0.55f, 0.32f); break;
                default: top = new C3(0.10f, 0.20f, 0.24f); tail = new C3(0.12f, 0.22f, 0.26f); low = new C3(0.75f, 0.78f, 0.80f); side = new C3(0.45f, 0.52f, 0.56f); break;
            }
            C3 c = strip <= SpinyTop ? top : strip == Tail ? tail : strip == Lower ? low : side;
            c = c * (0.82f + 0.18f * Sin(along * 70f));
            c = c * (1f - 0.28f * output);
            // The perch's black spot at the back of its spiny fin; the trout's spotted back fin; the white leading edge
            // of a trout's lower fins.
            if (kind == Perch && strip == SpinyTop)
                c = C3.Mix(c, new C3(0.03f, 0.03f, 0.03f), Smooth(0.7f, 0.85f, along) * Smooth(0.15f, 0.45f, output));
            if (kind == Trout && strip == SoftTop)
                c = C3.Mix(c, new C3(0.05f, 0.05f, 0.05f), Smooth(0.33f, 0.22f, Spots(along * 7f, output * 3f, 0.6f)));
            if (kind == Trout && strip == Lower)
                c = C3.Mix(c, new C3(0.95f, 0.95f, 0.95f), Smooth(0.12f, 0f, along));
            return c;
        }

        // ---- Shapes --------------------------------------------------------------------------------------------------

        /// <summary>A ring about the Y axis, `radius` to the middle of its tube, the tube `thick` across.</summary>
        public static MeshData Torus(float radius, float thick, int around = 16, int tube = 6)
        {
            var m = new MeshData();
            var idx = new int[around, tube];
            for (int i = 0; i < around; i++)
            {
                float a = 2f * Pi * i / around;
                for (int j = 0; j < tube; j++)
                {
                    float b = 2f * Pi * j / tube;
                    float r = radius + 0.5f * thick * Cos(b);
                    idx[i, j] = m.Add(new P3(r * Cos(a), 0.5f * thick * Sin(b), r * Sin(a)), (float)i / around, (float)j / tube);
                }
            }
            for (int i = 0; i < around; i++)
                for (int j = 0; j < tube; j++)
                {
                    int i1 = (i + 1) % around, j1 = (j + 1) % tube;
                    m.Tri(idx[i, j], idx[i, j1], idx[i1, j]);
                    m.Tri(idx[i1, j], idx[i, j1], idx[i1, j1]);
                }
            return m;
        }

        // ---- The submarine -------------------------------------------------------------------------------------------

        /// <summary>The submarine's radius `u` metres back from the bow: a rounded bow, a straight middle and a stern
        /// tapering to the propeller.</summary>
        public static float SubRadius(float u, float length, float radius)
        {
            const float bow = 3.4f, stern = 7f;
            if (u < bow)
            {
                float a = 1f - u / bow;
                return radius * (float)Math.Sqrt(Math.Max(1f - a * a, 0f));
            }
            if (u > length - stern)
            {
                float a = (u - (length - stern)) / stern;
                return radius * (1f - 0.84f * Pow(a, 1.5f));
            }
            return radius;
        }

        /// <summary>The submarine's hull, turned on a lathe, bow toward +Z. Texture coordinates: u along from the bow (0) to
        /// the stern (1), v round from the starboard side (0) over the top, for SubPaint.</summary>
        public static MeshData SubHull(float length, float radius)
        {
            var m = new MeshData();
            const int rings = 80, around = 36;
            var idx = new int[rings + 1, around + 1];
            for (int i = 0; i <= rings; i++)
            {
                float u = length * i / rings;
                float r = SubRadius(u, length, radius);
                for (int j = 0; j <= around; j++)
                {
                    float a = 2f * Pi * j / around;
                    idx[i, j] = m.Add(new P3(Cos(a) * r, Sin(a) * r, 0.5f * length - u), (float)i / rings, (float)j / around);
                }
            }
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < around; j++)
                {
                    int a = idx[i, j], b = idx[i, j + 1], c = idx[i + 1, j], d = idx[i + 1, j + 1];
                    m.Tri(a, c, b);
                    m.Tri(b, c, d);
                }
            int tail = m.Add(new P3(0f, 0f, -0.5f * length - 0.05f), 1f, 0.5f);
            for (int j = 0; j < around; j++)
                m.Tri(idx[rings, j + 1], idx[rings, j], tail);
            return m;
        }

        /// <summary>RGBA bytes (row 0 at the bottom) of the submarine's paint, for SubHull: light grey with a red bottom and
        /// a black band between, plating rings and seams, free-flooding holes along the casing, white depth marks.</summary>
        public static byte[] SubPaint(float length, int w, int h)
        {
            var px = new byte[w * h * 4];
            for (int r = 0; r < h; r++)
                for (int q = 0; q < w; q++)
                {
                    float z = ((q + 0.5f) / w - 0.5f) * length;
                    float up = Sin(2f * Pi * (r + 0.5f) / h);
                    var grey = new C3(0.50f, 0.55f, 0.60f);
                    var red = new C3(0.52f, 0.15f, 0.10f);
                    C3 c = up > -0.4f ? grey : red;
                    if (Math.Abs(up + 0.32f) < 0.08f)
                        c = new C3(0.04f, 0.04f, 0.04f);
                    float ring = Smooth(0.03f, 0f, Math.Abs(Fract(z / 2f) - 0.5f) - 0.47f);
                    c = c * (1f - 0.18f * ring);
                    c = c * (1f - 0.15f * Smooth(0.02f, 0f, Math.Abs(up - 0.55f)));
                    bool hole = Math.Abs(up - 0.25f) < 0.04f && Fract(z * 1.5f) >= 0.5f && Math.Abs(z) < length * 0.32f;
                    if (hole)
                        c = C3.Mix(c, new C3(0.03f, 0.03f, 0.03f), 0.85f);
                    bool mark = Math.Abs(Math.Abs(z) - length * 0.42f) < 0.05f && up >= -0.9f && up <= 0f && Fract(up * 6f) >= 0.5f;
                    if (mark)
                        c = new C3(0.95f, 0.95f, 0.95f);
                    int o = 4 * (r * w + q);
                    px[o] = (byte)Math.Max(0, Math.Min(255, (int)(c.r * 255f + 0.5f)));
                    px[o + 1] = (byte)Math.Max(0, Math.Min(255, (int)(c.g * 255f + 0.5f)));
                    px[o + 2] = (byte)Math.Max(0, Math.Min(255, (int)(c.b * 255f + 0.5f)));
                    px[o + 3] = 255;
                }
            return px;
        }
    }
}
