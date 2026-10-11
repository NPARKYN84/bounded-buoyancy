// Bounded Buoyancy for Unity. Engine-free core. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System;
using System.Collections.Generic;

namespace BoundedBuoyancy.Core
{
    /// <summary>Convex hull of a set of points, as triangles: what a convex MeshCollider collides with.</summary>
    public static class ConvexHull
    {
        struct Face
        {
            public int a, b, c;
            public V3 n;
            public double d;
            public bool alive;
        }

        /// <summary>Triangles of the hull, three points each, all wound the same way round; null if the points
        /// are flat or fewer than four.</summary>
        public static List<V3> Build(IList<V3> input)
        {
            var pts = Unique(input);
            int n = pts.Count;
            if (n < 4)
                return null;
            V3 lo = pts[0], hi = pts[0];
            for (int i = 1; i < n; i++)
            {
                lo = V3.Min(lo, pts[i]);
                hi = V3.Max(hi, pts[i]);
            }
            double eps = 1e-9 * Math.Max((hi - lo).Length, 1e-12);

            // A first tetrahedron from points far apart.
            int i0 = 0;
            for (int i = 1; i < n; i++)
                if (pts[i].x < pts[i0].x)
                    i0 = i;
            int i1 = Farthest(pts, i => (pts[i] - pts[i0]).LengthSquared);
            V3 e = pts[i1] - pts[i0];
            if (e.Length <= eps)
                return null;
            int i2 = Farthest(pts, i => e.Cross(pts[i] - pts[i0]).LengthSquared);
            V3 nrm = e.Cross(pts[i2] - pts[i0]);
            if (nrm.Length <= eps * e.Length)
                return null;
            nrm = nrm.Normalized();
            int i3 = Farthest(pts, i => Math.Abs(nrm.Dot(pts[i] - pts[i0])));
            if (Math.Abs(nrm.Dot(pts[i3] - pts[i0])) <= eps)
                return null;

            var faces = new List<Face>();
            var edges = new Dictionary<long, int>();   // directed edge a->b : face that has it
            V3 inside = (pts[i0] + pts[i1] + pts[i2] + pts[i3]) * 0.25;
            AddFace(pts, faces, edges, i0, i1, i2, inside);
            AddFace(pts, faces, edges, i0, i1, i3, inside);
            AddFace(pts, faces, edges, i0, i2, i3, inside);
            AddFace(pts, faces, edges, i1, i2, i3, inside);

            var visible = new List<int>();
            var horizon = new List<long>();
            for (int p = 0; p < n; p++)
            {
                if (p == i0 || p == i1 || p == i2 || p == i3)
                    continue;
                V3 q = pts[p];
                visible.Clear();
                for (int f = 0; f < faces.Count; f++)
                    if (faces[f].alive && faces[f].n.Dot(q) - faces[f].d > eps)
                        visible.Add(f);
                if (visible.Count == 0)
                    continue;
                // The horizon: edges of visible faces whose neighbour across the edge is not visible.
                horizon.Clear();
                foreach (int f in visible)
                {
                    Face fc = faces[f];
                    TryHorizon(faces, edges, fc.a, fc.b, q, eps, horizon);
                    TryHorizon(faces, edges, fc.b, fc.c, q, eps, horizon);
                    TryHorizon(faces, edges, fc.c, fc.a, q, eps, horizon);
                }
                foreach (int f in visible)
                {
                    Face fc = faces[f];
                    fc.alive = false;
                    faces[f] = fc;
                    edges.Remove(Key(fc.a, fc.b));
                    edges.Remove(Key(fc.b, fc.c));
                    edges.Remove(Key(fc.c, fc.a));
                }
                // Each horizon edge a->b keeps its direction, so the new face (a, b, p) faces outwards.
                foreach (long k in horizon)
                {
                    int a = (int)(k >> 32), b = (int)(k & 0xffffffffL);
                    AddFaceOriented(pts, faces, edges, a, b, p);
                }
            }

            var tris = new List<V3>();
            foreach (var f in faces)
            {
                if (!f.alive)
                    continue;
                tris.Add(pts[f.a]);
                tris.Add(pts[f.b]);
                tris.Add(pts[f.c]);
            }
            return tris.Count >= 12 ? tris : null;
        }

        static void TryHorizon(List<Face> faces, Dictionary<long, int> edges, int a, int b, V3 q, double eps, List<long> horizon)
        {
            int other;
            if (!edges.TryGetValue(Key(b, a), out other))
            {
                horizon.Add(Key(a, b));
                return;
            }
            Face o = faces[other];
            if (!(o.n.Dot(q) - o.d > eps))
                horizon.Add(Key(a, b));
        }

        static long Key(int a, int b) { return ((long)a << 32) | (uint)b; }

        static void AddFace(List<V3> pts, List<Face> faces, Dictionary<long, int> edges, int a, int b, int c, V3 inside)
        {
            V3 n = (pts[b] - pts[a]).Cross(pts[c] - pts[a]);
            if (n.Dot(inside - pts[a]) > 0.0)
            {
                int t = b;
                b = c;
                c = t;
            }
            AddFaceOriented(pts, faces, edges, a, b, c);
        }

        static void AddFaceOriented(List<V3> pts, List<Face> faces, Dictionary<long, int> edges, int a, int b, int c)
        {
            V3 n = (pts[b] - pts[a]).Cross(pts[c] - pts[a]).Normalized();
            var f = new Face { a = a, b = b, c = c, n = n, d = n.Dot(pts[a]), alive = true };
            int index = faces.Count;
            faces.Add(f);
            edges[Key(a, b)] = index;
            edges[Key(b, c)] = index;
            edges[Key(c, a)] = index;
        }

        static int Farthest(List<V3> pts, Func<int, double> measure)
        {
            int best = 0;
            double bestValue = double.NegativeInfinity;
            for (int i = 0; i < pts.Count; i++)
            {
                double m = measure(i);
                if (m > bestValue)
                {
                    bestValue = m;
                    best = i;
                }
            }
            return best;
        }

        static List<V3> Unique(IList<V3> input)
        {
            var seen = new HashSet<V3>();
            var list = new List<V3>(input.Count);
            foreach (var p in input)
                if (seen.Add(p))
                    list.Add(p);
            return list;
        }
    }
}
