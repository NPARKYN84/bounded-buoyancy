// Bounded Buoyancy for Unity: the look of the demo's brigs, as plain data (meshes, painted textures and rig parts),
// with no Unity types in it, so that it can be built and checked outside Unity. BoundedBuoyancyDemoShips.cs turns it
// into GameObjects. The same ship as the Godot plugin's demo (demo_ship.gd), in Unity's axes: bow toward +Z,
// starboard toward +X, the origin on the keel line.
// Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System;
using System.Collections.Generic;

namespace BoundedBuoyancy.Samples
{
    /// <summary>A point or a direction.</summary>
    public struct P3
    {
        public float x, y, z;
        public P3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static P3 operator +(P3 a, P3 b) { return new P3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static P3 operator -(P3 a, P3 b) { return new P3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static P3 operator *(P3 a, float k) { return new P3(a.x * k, a.y * k, a.z * k); }
        public float Length { get { return (float)Math.Sqrt(x * x + y * y + z * z); } }
        public P3 Normalized { get { float l = Length; return l > 1e-9f ? this * (1f / l) : new P3(0f, 1f, 0f); } }
        public static P3 Cross(P3 a, P3 b) { return new P3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x); }
        public static P3 Lerp(P3 a, P3 b, float t) { return a + (b - a) * t; }
    }

    /// <summary>A mesh: positions, texture coordinates and triangles (clockwise from the front, as Unity draws them).</summary>
    public sealed class MeshData
    {
        public readonly List<P3> Positions = new List<P3>();
        public readonly List<float> UV = new List<float>();   // u, v for each position
        public readonly List<int> Triangles = new List<int>();

        public int Add(P3 p, float u, float v)
        {
            Positions.Add(p);
            UV.Add(u);
            UV.Add(v);
            return Positions.Count - 1;
        }

        public void Tri(int a, int b, int c)
        {
            Triangles.Add(a);
            Triangles.Add(b);
            Triangles.Add(c);
        }
    }

    /// <summary>A part of the rig or fittings: a box (Centre, axes X, Y, Z and Size) or a cylinder (from A to B, of Radius),
    /// drawn in the material named by Look.</summary>
    public struct Part
    {
        public bool Cylinder;
        public P3 A, B;
        public float Radius;
        public P3 Centre, X, Y, Z, Size;
        public string Look;
    }

    /// <summary>A ship's colours, as 0..1 red, green, blue: its upper sides, the band along its ports, below the
    /// waterline, the port lids and the flag, and the height its waterline is painted at.</summary>
    public struct ShipColours
    {
        public float[] Wood, Band, Bottom, Lids, Flag;
        public float Waterline;
    }

    public static class ShipLook
    {
        public const float Length = 15f, Beam = 4.6f, Keel = -1.3f, MaxBeam = 0.7f, Deck = 1.35f, Rail = 2.05f;
        public const float Tumble = 0.12f, Tuck = 0.45f;
        const int Stations = 56, Rows = 22;
        // The hull's paint covers it from YMin to YMax.
        public const float YMin = -1.4f, YMax = 3.6f;
        public const float PortLow = Deck + 0.17f, PortHigh = Deck + 0.6f, PortWidth = 0.55f;

        public static readonly ShipColours Navy = new ShipColours
        {
            Wood = new[] { 0.13f, 0.11f, 0.09f }, Band = new[] { 0.80f, 0.62f, 0.27f }, Bottom = new[] { 0.62f, 0.36f, 0.20f },
            Lids = new[] { 0.08f, 0.07f, 0.06f }, Flag = new[] { 0.05f, 0.05f, 0.06f }, Waterline = 0.3f,
        };
        public static readonly ShipColours Merchant = new ShipColours
        {
            Wood = new[] { 0.42f, 0.28f, 0.16f }, Band = new[] { 0.55f, 0.13f, 0.10f }, Bottom = new[] { 0.86f, 0.84f, 0.78f },
            Lids = new[] { 0.30f, 0.10f, 0.08f }, Flag = new[] { 0.62f, 0.10f, 0.10f }, Waterline = 0.7f,
        };
        public static readonly ShipColours Pirate = new ShipColours
        {
            Wood = new[] { 0.26f, 0.17f, 0.10f }, Band = new[] { 0.55f, 0.10f, 0.08f }, Bottom = new[] { 0.40f, 0.30f, 0.22f },
            Lids = new[] { 0.55f, 0.10f, 0.08f }, Flag = new[] { 0.02f, 0.02f, 0.02f }, Waterline = 0.35f,
        };

        // ---- The shape, worked out as in the Godot demo: t runs from -1 at the bow to 1 at the stern. ---------------

        static float Smooth(float a, float b, float x)
        {
            float t = Math.Max(0f, Math.Min(1f, (x - a) / (b - a)));
            return t * t * (3f - 2f * t);
        }

        static float Pow(float a, float b) { return (float)Math.Pow(a, b); }

        /// <summary>Half the hull's width at t along it and height y, its sides drawn in toward `rail` (the drawn rail if
        /// not given).</summary>
        public static float HalfWidth(float t, float y, float rail = -1f)
        {
            float plan;
            if (t < 0f)
                plan = Pow(Math.Max(1f - Pow(-t, 2.3f), 0f), 0.55f);
            else
            {
                float above = 1f - 0.3f * Pow(t, 2.2f);
                float below = Pow(Math.Max(1f - Pow(t, 2.6f), 0f), 0.6f);
                plan = below + (above - below) * Smooth(Tuck - 0.7f, Tuck + 0.15f, y);
            }
            float low = MaxBeam - KeelAt(t);
            float g;
            if (y < MaxBeam)
            {
                float u = Math.Max(0f, Math.Min(1f, (MaxBeam - y) / Math.Max(low, 0.05f)));
                g = Pow(1f - Pow(u, 2.4f), 1f / 2.4f);
            }
            else
            {
                float u = Math.Max(0f, Math.Min(1f, (y - MaxBeam) / ((rail > 0f ? rail : RailAt(t)) - MaxBeam)));
                g = 1f - Tumble * u * u;
            }
            return Math.Max(0.5f * Beam * plan * g, 0.07f * Math.Min(plan * 4f, 1f));
        }

        public static float KeelAt(float t)
        {
            return Keel + 1.7f * Pow(Math.Max((-t - 0.5f) / 0.5f, 0f), 2f) + 0.35f * Pow(Math.Max((t - 0.8f) / 0.2f, 0f), 2f);
        }

        /// <summary>The drawn rail: it sweeps up toward both ends, and up to a raised quarterdeck at the stern.</summary>
        public static float RailAt(float t) { return Rail + 0.45f * t * t + 0.85f * Smooth(0.5f, 0.75f, t); }

        public static float DeckAt(float t) { return Deck + 0.25f * t * t + 0.75f * Smooth(0.5f, 0.56f, t); }

        /// <summary>The rail and deck the physics uses (lower, with an upright stern), as the BATTLE scene's hull and
        /// compartments have them.</summary>
        public static float HullRailAt(float t) { return Rail + 0.3f * t * t + 0.55f * Smooth(0.55f, 0.85f, t); }

        public static float HullDeckAt(float t) { return Deck + 0.25f * t * t + 0.55f * Smooth(0.55f, 0.85f, t); }

        /// <summary>A point given as in the Godot demo (bow toward -z), in Unity's axes (bow toward +z).</summary>
        static P3 U(float x, float y, float gz) { return new P3(x, y, -gz); }

        static float Rake(float t, float y) { return 0.35f * (y - Tuck) * Smooth(0.9f, 1f, t); }

        // ---- Meshes -------------------------------------------------------------------------------------------------

        /// <summary>The hull's sides and bottom, rounded at the bilge, the stern raked. Texture coordinates: u along the
        /// ship from the bow (0) to the stern (1), v up from YMin (0) to YMax (1), for HullPaint.</summary>
        public static MeshData Hull()
        {
            var m = new MeshData();
            var idx = new int[2, Stations + 1, Rows + 1];
            var inner = new int[2, Stations + 1, Rows + 1];
            var above = new bool[2, Stations + 1, Rows + 1];
            for (int s = 0; s < 2; s++)
            {
                float side = s == 0 ? 1f : -1f;
                for (int i = 0; i <= Stations; i++)
                {
                    float t = Math.Max(-0.995f, -1f + 2f * i / Stations);
                    float y0 = KeelAt(t), y1 = RailAt(t);
                    for (int j = 0; j <= Rows; j++)
                    {
                        float v = (float)j / Rows;
                        float y = y0 + (y1 - y0) * (1f - Pow(1f - v, 1.4f));
                        float gz = t * 0.5f * Length + Rake(t, y);
                        idx[s, i, j] = m.Add(U(side * HalfWidth(t, y), y, gz), 0.5f * (t + 1f), (y - YMin) / (YMax - YMin));
                        // The inside, a little inboard, seen only above the deck (the bulwarks).
                        inner[s, i, j] = m.Add(U(side * (HalfWidth(t, y) - 0.03f), y, gz), 0.5f * (t + 1f), (y - YMin) / (YMax - YMin));
                        above[s, i, j] = y >= DeckAt(t) - 0.05f;
                    }
                }
            }
            for (int s = 0; s < 2; s++)
                for (int i = 0; i < Stations; i++)
                    for (int j = 0; j < Rows; j++)
                    {
                        int a = idx[s, i, j], b = idx[s, i + 1, j], c = idx[s, i + 1, j + 1], d = idx[s, i, j + 1];
                        if (s == 0)
                        {
                            m.Tri(a, b, c);
                            m.Tri(a, c, d);
                        }
                        else
                        {
                            m.Tri(a, c, b);
                            m.Tri(a, d, c);
                        }
                        if (above[s, i, j] || above[s, i + 1, j])
                        {
                            // The bulwark's inside, wound the other way.
                            a = inner[s, i, j]; b = inner[s, i + 1, j]; c = inner[s, i + 1, j + 1]; d = inner[s, i, j + 1];
                            if (s == 0)
                            {
                                m.Tri(a, c, b);
                                m.Tri(a, d, c);
                            }
                            else
                            {
                                m.Tri(a, b, c);
                                m.Tri(a, c, d);
                            }
                        }
                    }
            // Along the keel, joining the two sides.
            for (int i = 0; i < Stations; i++)
            {
                int a = idx[0, i, 0], b = idx[0, i + 1, 0], aa = idx[1, i, 0], bb = idx[1, i + 1, 0];
                m.Tri(a, bb, b);
                m.Tri(a, aa, bb);
            }
            return m;
        }

        /// <summary>The transom closing the stern. Texture coordinates: u across from x = -2.5 (0) to 2.5 (1), v as the hull's,
        /// for TransomPaint.</summary>
        public static MeshData Transom()
        {
            var m = new MeshData();
            float t = 1f;
            float y0 = KeelAt(t), y1 = RailAt(t);
            int prevS = -1, prevP = -1, prevSi = -1, prevPi = -1;
            for (int j = 0; j <= Rows; j++)
            {
                float v = (float)j / Rows;
                float y = y0 + (y1 - y0) * (1f - Pow(1f - v, 1.4f));
                float x = HalfWidth(t, y);
                float gz = 0.5f * Length + Rake(t, y);
                float tv = (y - YMin) / (YMax - YMin);
                int s = m.Add(U(x, y, gz), (x + 2.5f) / 5f, tv);
                int p = m.Add(U(-x, y, gz), (-x + 2.5f) / 5f, tv);
                // Its inside, a little forward, wound the other way.
                int si = m.Add(U(x, y, gz - 0.03f), (x + 2.5f) / 5f, tv);
                int pi = m.Add(U(-x, y, gz - 0.03f), (-x + 2.5f) / 5f, tv);
                if (j > 0)
                {
                    m.Tri(prevP, s, prevS);
                    m.Tri(prevP, p, s);
                    m.Tri(prevPi, prevSi, si);
                    m.Tri(prevPi, si, pi);
                }
                prevS = s;
                prevP = p;
                prevSi = si;
                prevPi = pi;
            }
            return m;
        }

        /// <summary>The deck, from side to side at deck height, bow to stern, up a step to the quarterdeck. Texture
        /// coordinates: u along from the bow, v across from x = -2.5, for DeckPaint.</summary>
        public static MeshData DeckMesh()
        {
            var m = new MeshData();
            const int n = 48;
            int pa = -1, pb = -1;
            for (int i = 0; i <= n; i++)
            {
                float t = -0.93f + 1.93f * i / n;
                float y = DeckAt(t);
                float w = HalfWidth(t, y) - 0.08f;
                float gz = t * 0.5f * Length;
                int a = m.Add(U(-w, y, gz), 0.5f * (t + 1f), (-w + 2.5f) / 5f);
                int b = m.Add(U(w, y, gz), 0.5f * (t + 1f), (w + 2.5f) / 5f);
                if (i > 0)
                {
                    m.Tri(pa, pb, b);
                    m.Tri(pa, b, a);
                }
                pa = a;
                pb = b;
            }
            return m;
        }

        /// <summary>A square sail from its head (half-width wh) to its foot (half-width wf), filled by a wind from astern so
        /// that it bellies forward; drawn on both faces. Texture coordinates: u across, v down from the head.</summary>
        public static MeshData Sail(P3 head, P3 foot, float wh, float wf)
        {
            var m = new MeshData();
            const int n = 10;
            var g = new int[n + 1, n + 1];
            for (int i = 0; i <= n; i++)
                for (int j = 0; j <= n; j++)
                {
                    float v = (float)i / n, u = (float)j / n;
                    float w = wh + (wf - wh) * v;
                    P3 p = P3.Lerp(head, foot, v) + new P3((u - 0.5f) * 2f * w, 0f, 0f);
                    p.z += 0.9f * (float)(Math.Sin(Math.PI * u) * Math.Sin(Math.PI * Math.Min(v * 1.15f, 1f)));   // the belly, forward
                    g[i, j] = m.Add(p, u, v);
                }
            DoubleSided(m, n, g, new P3(0f, 0f, -0.02f));
            return m;
        }

        /// <summary>Draw the grid g of m on both faces: the back face is a copy, wound the other way and moved by `back`,
        /// a little behind the front, so that the two never draw at the same depth.</summary>
        static void DoubleSided(MeshData m, int n, int[,] g, P3 back)
        {
            int count = m.Positions.Count;
            for (int k = 0; k < count; k++)
                m.Add(m.Positions[k] + back, m.UV[2 * k], m.UV[2 * k + 1]);
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    int a = g[i, j], b = g[i, j + 1], c = g[i + 1, j + 1], d = g[i + 1, j];
                    m.Tri(a, b, c);
                    m.Tri(a, c, d);
                    m.Tri(count + a, count + c, count + b);
                    m.Tri(count + a, count + d, count + c);
                }
        }

        /// <summary>A triangular jib between head, tack and clew, bellying out a little to one side; both faces.</summary>
        public static MeshData Jib(P3 head, P3 tack, P3 clew)
        {
            var m = new MeshData();
            const int n = 8;
            var g = new int[n + 1, n + 1];
            for (int i = 0; i <= n; i++)
                for (int j = 0; j <= n; j++)
                {
                    float a = (float)i / n, c = (float)j / n;
                    if (a + c > 1f)
                    {
                        float k = 1f / (a + c);   // folded onto the edge from the tack to the clew
                        a *= k;
                        c *= k;
                    }
                    P3 p = head + (tack - head) * a + (clew - head) * c;
                    p.x += 0.35f * (float)(Math.Sin(Math.PI * a) * Math.Sin(Math.PI * Math.Min(c * 2f, 1f))) * (1f - a - c + 0.5f);
                    g[i, j] = m.Add(p, c, a);
                }
            P3 across = P3.Cross(tack - head, clew - head).Normalized;
            DoubleSided(m, n, g, across * -0.02f);
            return m;
        }

        // ---- Paint --------------------------------------------------------------------------------------------------

        static float Hash(float n)
        {
            double v = Math.Sin(n * 91.345) * 47453.13;
            return (float)(v - Math.Floor(v));
        }

        static float Noise(float x)
        {
            float i = (float)Math.Floor(x), f = x - i;
            float s = f * f * (3f - 2f * f);
            return Hash(i) + (Hash(i + 1f) - Hash(i)) * s;
        }

        static float Fract(float x) { return x - (float)Math.Floor(x); }

        /// <summary>Colour (0..1 RGB) of the hull's planking at height y and Godot-z gz, `rail` the rail's height there.</summary>
        static float[] Planking(float y, float along, float rail, ShipColours c, bool transom, float gz, float x)
        {
            const float bandLow = Deck + 0.05f, bandHigh = Deck + 0.68f;
            float across = y / 0.28f;
            float plank = (float)Math.Floor(across);
            float f = across - plank;
            float seam = Smooth(0f, 0.14f, f) * (1f - Smooth(0.88f, 1f, f));
            float al = along + Hash(plank) * 6f;
            float butt = Smooth(0f, 0.03f, (0.5f - Math.Abs(Fract(al / 6f) - 0.5f)) * 6f);   // dark at the butts
            float grain = 0.86f + 0.1f * Noise(along * 3f + plank * 17f) + 0.22f * (Hash(plank + (float)Math.Floor(al / 6f)) - 0.5f);
            float[] col = y < c.Waterline ? c.Bottom : (y > bandLow && y < bandHigh ? c.Band : c.Wood);
            float shade = (0.22f + 0.78f * seam * butt) * grain;
            var o = new[] { col[0] * shade, col[1] * shade, col[2] * shade };
            // Black wales above and below the band, and along the rail; a gilt moulding under the rail.
            if (Math.Abs(y - bandLow) < 0.07f || Math.Abs(y - bandHigh) < 0.05f || y > rail - 0.06f)
                o = new[] { 0.05f, 0.045f, 0.04f };
            if (Math.Abs(y - (rail - 0.16f)) < 0.035f)
                o = new[] { 0.78f, 0.58f, 0.2f };
            return o;
        }

        static float[] Window(float r, float[] under)
        {
            if (r < 0.15f)
                return new[] { 1f, 0.78f, 0.45f };       // the glass, lit from within
            if (r < 0.19f)
                return new[] { 0.78f, 0.58f, 0.22f };    // its brass rim
            return under;
        }

        /// <summary>RGBA bytes, w by h, row 0 at the bottom (v = 0), of the hull's sides: planks, the band, the wales, the
        /// gilt moulding, the gun ports at `ports` (Godot-z, bow -), and the round windows along the quarter.</summary>
        public static byte[] HullPaint(int w, int h, ShipColours c, float[] ports)
        {
            var px = new byte[w * h * 4];
            for (int r = 0; r < h; r++)
            {
                float y = YMin + (YMax - YMin) * (r + 0.5f) / h;
                for (int q = 0; q < w; q++)
                {
                    float t = -1f + 2f * (q + 0.5f) / w;
                    float gz = t * 0.5f * Length;
                    float rail = RailAt(t);
                    float[] o = Planking(y, gz, rail, c, false, gz, 0f);
                    if (y > PortLow && y < PortHigh)
                        foreach (float pz in ports)
                            if (Math.Abs(gz - pz) < 0.5f * PortWidth)
                            {
                                float frame = Math.Min(Math.Abs(Math.Abs(gz - pz) - 0.5f * PortWidth), Math.Min(y - PortLow, PortHigh - y));
                                o = frame < 0.05f ? c.Lids : new[] { 0.015f, 0.012f, 0.01f };
                            }
                    if (gz > 4.6f && gz < 7f)
                    {
                        float wx = (Fract((gz + 0.15f) / 0.7f) - 0.5f) * 0.7f, wy = y - (rail - 0.62f);
                        o = Window((float)Math.Sqrt(wx * wx + wy * wy), o);
                    }
                    Put(px, (r * w + q) * 4, o);
                }
            }
            return px;
        }

        /// <summary>RGBA bytes of the transom: planks, band and wales, and a row of round windows across it.</summary>
        public static byte[] TransomPaint(int w, int h, ShipColours c)
        {
            var px = new byte[w * h * 4];
            float rail = RailAt(1f);
            for (int r = 0; r < h; r++)
            {
                float y = YMin + (YMax - YMin) * (r + 0.5f) / h;
                for (int q = 0; q < w; q++)
                {
                    float x = -2.5f + 5f * (q + 0.5f) / w;
                    float[] o = Planking(y, x, rail, c, true, 0f, x);
                    if (Math.Abs(x) < 1.55f)
                    {
                        float wx = (Fract((x + 5f) / 0.6f) - 0.5f) * 0.6f, wy = y - (rail - 0.62f);
                        o = Window((float)Math.Sqrt(wx * wx + wy * wy), o);
                    }
                    Put(px, (r * w + q) * 4, o);
                }
            }
            return px;
        }

        /// <summary>RGBA bytes of the deck: planks along the ship, their butts staggered.</summary>
        public static byte[] DeckPaint(int w, int h)
        {
            var px = new byte[w * h * 4];
            for (int r = 0; r < h; r++)
            {
                float x = -2.5f + 5f * (r + 0.5f) / h;
                float across = (x + 10f) / 0.2f;
                float plank = (float)Math.Floor(across);
                float f = across - plank;
                float seam = Smooth(0f, 0.07f, f) * (1f - Smooth(0.93f, 1f, f));
                for (int q = 0; q < w; q++)
                {
                    float gz = (-1f + 2f * (q + 0.5f) / w) * 0.5f * Length;
                    float al = gz + Hash(plank) * 5f;
                    float butt = Smooth(0f, 0.03f, (0.5f - Math.Abs(Fract(al / 5f) - 0.5f)) * 5f);
                    float k = (0.88f + 0.16f * Hash(plank + (float)Math.Floor(al / 5f))) * (0.45f + 0.55f * seam * butt);
                    Put(px, (r * w + q) * 4, new[] { 0.66f * k, 0.56f * k, 0.42f * k });
                }
            }
            return px;
        }

        /// <summary>RGBA bytes of sailcloth: cloths sewn side by side, a reef band, the grime of use toward the foot (v down
        /// from the head).</summary>
        public static byte[] SailPaint(int w, int h)
        {
            var px = new byte[w * h * 4];
            for (int r = 0; r < h; r++)
            {
                float v = (r + 0.5f) / h;   // row 0 is the head (the sail meshes have v = 0 there)
                float reef = 1f - 0.25f * (1f - Smooth(0f, 0.012f, Math.Abs(v - 0.25f)));
                for (int q = 0; q < w; q++)
                {
                    float u = (q + 0.5f) / w;
                    float cloth = Fract(u * 9f);
                    float seam = Smooth(0f, 0.04f, cloth) * (1f - Smooth(0.96f, 1f, cloth));
                    float k = (0.82f + 0.18f * seam) * reef * (1f - 0.14f * v);
                    Put(px, (r * w + q) * 4, new[] { 0.92f * k, 0.88f * k, 0.78f * k });
                }
            }
            return px;
        }

        static void Put(byte[] px, int at, float[] rgb)
        {
            // Stored as sRGB: the colours above are given as they look.
            for (int k = 0; k < 3; k++)
                px[at + k] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(255.0 * Math.Max(0f, Math.Min(1f, rgb[k])))));
            px[at + 3] = 255;
        }

        // ---- The rig and fittings -----------------------------------------------------------------------------------

        public sealed class Rig
        {
            public readonly List<Part> Parts = new List<Part>();
            public readonly List<MeshData> Sails = new List<MeshData>();
            public MeshData JibSail;
            public Part Flag;
        }

        static Part Spar(P3 a, P3 b, float r, string look = "spar")
        {
            return new Part { Cylinder = true, A = a, B = b, Radius = r, Look = look };
        }

        static Part Box(P3 centre, P3 x, P3 y, P3 size, string look)
        {
            P3 xn = x.Normalized, yn = y.Normalized;
            return new Part { Centre = centre, X = xn, Y = yn, Z = P3.Cross(xn, yn).Normalized, Size = size, Look = look };
        }

        /// <summary>Masts, tops, yards, every square sail set and a jib, shrouds and stays, the flag, the open gun ports'
        /// lids (and, with runOut, a gun's muzzle in each), and a lantern at the stern. `ports` in Godot-z (bow -).</summary>
        public static Rig MakeRig(float[] ports, bool runOut)
        {
            var rig = new Rig();
            var parts = rig.Parts;
            float half = 0.5f * Length;
            float fore = -0.22f * Length, main = 0.1f * Length;
            P3 bow = U(0f, RailAt(-0.98f) - 0.2f, -0.49f * Length);
            P3 spritEnd = bow + new P3(0f, 1.6f, 4.6f);
            parts.Add(Spar(bow, spritEnd, 0.1f));
            var caps = new P3[2];
            var heads = new P3[2];
            float[][] masts = { new[] { fore, 8f, 5f }, new[] { main, 8.6f, 5.4f } };
            for (int k = 0; k < 2; k++)
            {
                float z = masts[k][0];
                P3 baseP = U(0f, DeckAt(z / half), z);
                P3 cap = baseP + new P3(0f, masts[k][1], 0f);
                P3 head = cap + new P3(0f, masts[k][2], 0f);
                caps[k] = cap;
                heads[k] = head;
                parts.Add(Spar(baseP, cap + new P3(0f, 0.6f, 0f), 0.17f));
                parts.Add(Spar(cap - new P3(0f, 0.5f, 0f), head, 0.09f));
                parts.Add(Box(cap, new P3(1f, 0f, 0f), new P3(0f, 1f, 0f), new P3(1.5f, 0.08f, 1f), "spar"));
                P3 courseYard = cap - new P3(0f, 1f, 0f);
                const float w1 = 3.6f, w2 = 2.7f;
                parts.Add(Spar(courseYard - new P3(w1, 0f, 0f), courseYard + new P3(w1, 0f, 0f), 0.09f));
                rig.Sails.Add(Sail(courseYard + new P3(0f, -0.1f, -0.05f), U(0f, DeckAt(z / half) + 1.5f, z + 0.05f), w1 * 0.95f, w1 * 1.05f));
                P3 topYard = head - new P3(0f, 0.7f, 0f);
                parts.Add(Spar(topYard - new P3(w2, 0f, 0f), topYard + new P3(w2, 0f, 0f), 0.07f));
                rig.Sails.Add(Sail(topYard + new P3(0f, -0.1f, -0.05f), courseYard + new P3(0f, 0.25f, -0.05f), w2 * 0.95f, w1 * 0.95f));
                // Shrouds from the top down to the rail, three a side, and a backstay from the masthead.
                foreach (float side in new[] { -1f, 1f })
                {
                    for (int s = 0; s < 3; s++)
                    {
                        float zz = z + 0.6f * (s - 0.6f);
                        float t = zz / half;
                        P3 foot = U(side * (HalfWidth(t, RailAt(t)) + 0.05f), RailAt(t), zz);
                        parts.Add(Spar(cap + new P3(side * 0.6f, 0f, -0.1f * s), foot, 0.025f, "rope"));
                    }
                    parts.Add(Spar(head - new P3(0f, 0.4f, 0f), U(side * (HalfWidth(z / half, Rail) + 0.05f), Rail, z + 2.4f), 0.025f, "rope"));
                }
            }
            // Stays: fore and aft between the mastheads, and to the bowsprit.
            parts.Add(Spar(caps[0], bow + new P3(0f, 0.1f, 0.2f), 0.03f, "rope"));
            parts.Add(Spar(heads[0], spritEnd, 0.03f, "rope"));
            parts.Add(Spar(caps[1], caps[0] + new P3(0f, -2f, 0f), 0.03f, "rope"));
            parts.Add(Spar(heads[1], heads[0], 0.03f, "rope"));
            parts.Add(Spar(heads[1], U(0f, RailAt(1f), 0.49f * Length), 0.03f, "rope"));
            rig.JibSail = Jib(heads[0] - new P3(0f, 0.6f, 0f), spritEnd - new P3(0f, 0.1f, -0.3f), bow + new P3(0f, 0.25f, 0.6f));
            // The flag at the main masthead, streaming aft.
            rig.Flag = Box(heads[1] + new P3(0f, -0.35f, -0.85f), new P3(0f, 0f, 1f), new P3(0f, 1f, 0f), new P3(1.6f, 1f, 0.02f), "flag");
            // The gun ports: lids swung up 70 degrees on hinges along their top edges, and the guns' muzzles.
            foreach (float pz in ports)
            {
                float t = pz / half;
                foreach (float side in new[] { -1f, 1f })
                {
                    float x = side * HalfWidth(t, PortHigh);
                    float a = 70f * (float)Math.PI / 180f;
                    float lidH = PortHigh - PortLow + 0.04f;
                    // The lid hangs from its hinge, swung outward and up: its long side points out and up from the hinge.
                    var down = new P3(side * (float)Math.Sin(a), -(float)Math.Cos(a), 0f);
                    P3 hinge = U(x + side * 0.03f, PortHigh + 0.02f, pz);
                    parts.Add(Box(hinge + down * (0.5f * lidH), P3.Cross(new P3(0f, 0f, 1f), down), down * -1f, new P3(0.05f, lidH, 0.6f), "lid"));
                    if (runOut)
                    {
                        P3 mid = U(x + side * 0.15f, 0.5f * (PortLow + PortHigh), pz);
                        parts.Add(Spar(mid - new P3(side * 0.55f, 0f, 0f), mid + new P3(side * 0.55f, 0f, 0f), 0.095f, "iron"));
                    }
                }
            }
            // A lantern on a post at the top of the stern.
            P3 top = U(0f, RailAt(1f), half + Rake(1f, RailAt(1f)) - 0.1f);
            parts.Add(Spar(top, top + new P3(0f, 0.9f, 0f), 0.045f));
            parts.Add(Spar(top + new P3(0f, 0.85f, 0f), top + new P3(0f, 1.35f, 0f), 0.16f, "lantern"));
            parts.Add(Spar(top + new P3(0f, 1.35f, 0f), top + new P3(0f, 1.52f, 0f), 0.09f, "gold"));
            return rig;
        }
    }
}
