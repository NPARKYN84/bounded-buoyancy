// Bounded Buoyancy for Unity. Engine-free core. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System;
using System.Collections.Generic;

namespace BoundedBuoyancy.Core
{
    /// <summary>
    /// The closed surface of a body, in the body's own coordinates, ready for cutting at the waterline: each
    /// vertex stored once, three vertex indices per triangle, and a weight per triangle (orientation sign x
    /// exact / faceted volume, so curved shapes keep their exact volume with few facets). A body made of a
    /// single sphere is kept as a sphere and cut exactly.
    /// </summary>
    public sealed class BuoyancyShape3D
    {
        public bool SingleSphere;
        public double Radius;
        public V3 SphereCentre;

        public V3[] Vertices = new V3[0];
        public int[] Triangles = new int[0];
        public double[] Weights = new double[0];

        /// <summary>Volume of the shape (m³).</summary>
        public double Volume;
        /// <summary>Centre of the volume, in body coordinates.</summary>
        public V3 Centroid;
        /// <summary>Distance from the centroid to the farthest vertex.</summary>
        public double BoundRadius;
        /// <summary>Area the body shows looking along each body axis (bounding-box faces times how much of the
        /// box the shape fills), for drag.</summary>
        public V3 FaceArea;
        /// <summary>Size of the bounding box along the body's own axes (m).</summary>
        public V3 Size;
        /// <summary>Area seen from above (or the sphere's cross-section).</summary>
        public double Area;

        internal double[] Depth = new double[0];
        internal V3[] WorldPoints = new V3[0];

        public bool IsEmpty { get { return Volume <= 0.0; } }

        /// <summary>Area the body shows to a flow along `along` (world frame): the faces of its bounding box, each
        /// seen at its angle to the flow.</summary>
        public double DragArea(V3 along, M3 basis)
        {
            double n = along.Length;
            if (SingleSphere || n < 1e-9)
                return Area;
            V3 d = basis.MulT(along).Abs() / n;
            return d.Dot(FaceArea);
        }

        /// <summary>Inertia tensor of the shape at unit density about `com` (body axes).</summary>
        public Sym3 UnitInertia(V3 com)
        {
            if (SingleSphere)
            {
                V3 d = SphereCentre - com;
                double m = Volume;
                double i0 = 0.4 * m * Radius * Radius;
                double dd = d.LengthSquared;
                return new Sym3
                {
                    xx = i0 + m * (dd - d.x * d.x), yy = i0 + m * (dd - d.y * d.y), zz = i0 + m * (dd - d.z * d.z),
                    xy = -m * d.x * d.y, xz = -m * d.x * d.z, yz = -m * d.y * d.z,
                };
            }
            // Second moments of the tetrahedra (com, a, b, c): ∫ x_i x_j dV = V/20 (Σ a_i a_j + (Σ a_i)(Σ a_j)).
            double sxx = 0, syy = 0, szz = 0, sxy = 0, sxz = 0, syz = 0;
            for (int i = 0; i < Triangles.Length; i += 3)
            {
                V3 a = Vertices[Triangles[i]] - com;
                V3 b = Vertices[Triangles[i + 1]] - com;
                V3 c = Vertices[Triangles[i + 2]] - com;
                double v = Weights[i / 3] * a.Dot(b.Cross(c)) / 6.0;
                V3 s = a + b + c;
                double k = v / 20.0;
                sxx += k * (a.x * a.x + b.x * b.x + c.x * c.x + s.x * s.x);
                syy += k * (a.y * a.y + b.y * b.y + c.y * c.y + s.y * s.y);
                szz += k * (a.z * a.z + b.z * b.z + c.z * c.z + s.z * s.z);
                sxy += k * (a.x * a.y + b.x * b.y + c.x * c.y + s.x * s.y);
                sxz += k * (a.x * a.z + b.x * b.z + c.x * c.z + s.x * s.z);
                syz += k * (a.y * a.z + b.y * b.z + c.y * c.z + s.y * s.z);
            }
            return new Sym3 { xx = syy + szz, yy = sxx + szz, zz = sxx + syy, xy = -sxy, xz = -sxz, yz = -syz };
        }
    }

    /// <summary>Collects a body's parts (in body coordinates) and builds its BuoyancyShape3D.</summary>
    public sealed class ShapeBuilder3D
    {
        struct Sphere
        {
            public V3 c;
            public double r;
        }

        /// <summary>Facets round the curve of capsules, and of spheres that share a body with other parts.</summary>
        public int CurveSegments = 16;
        /// <summary>Problems met while building, for the caller to report.</summary>
        public readonly List<string> Warnings = new List<string>();

        readonly List<V3> verts = new List<V3>();
        readonly Dictionary<V3, int> index = new Dictionary<V3, int>();
        readonly List<int> tris = new List<int>();
        readonly List<double> weights = new List<double>();
        readonly List<Sphere> spheres = new List<Sphere>();
        double volume;
        int parts;

        public int PartCount { get { return parts; } }

        public void AddSphere(V3 centre, double radius)
        {
            if (!(radius > 0.0))
            {
                Warnings.Add("a sphere has no radius");
                return;
            }
            spheres.Add(new Sphere { c = centre, r = radius });
            parts++;
        }

        /// <summary>A box given by its centre and its three half-edges (body coordinates; they may be skewed by
        /// scaling).</summary>
        public bool AddBox(V3 centre, V3 halfX, V3 halfY, V3 halfZ)
        {
            var c = new V3[8];
            for (int i = 0; i < 8; i++)
                c[i] = centre + ((i & 1) != 0 ? halfX : -halfX) + ((i & 2) != 0 ? halfY : -halfY) + ((i & 4) != 0 ? halfZ : -halfZ);
            int[] quads = { 1, 3, 7, 5, 0, 4, 6, 2, 2, 6, 7, 3, 0, 1, 5, 4, 4, 5, 7, 6, 0, 2, 3, 1 };
            var faces = new List<V3>(36);
            for (int q = 0; q < quads.Length; q += 4)
            {
                faces.Add(c[quads[q]]); faces.Add(c[quads[q + 1]]); faces.Add(c[quads[q + 2]]);
                faces.Add(c[quads[q]]); faces.Add(c[quads[q + 2]]); faces.Add(c[quads[q + 3]]);
            }
            return AddTriangles(faces, -1.0);
        }

        /// <summary>A capsule: centre, unit axis, radius and total height (at least twice the radius).</summary>
        public bool AddCapsule(V3 centre, V3 axis, double radius, double height)
        {
            if (!(radius > 0.0))
            {
                Warnings.Add("a capsule has no radius");
                return false;
            }
            height = Math.Max(height, 2.0 * radius);
            double half = 0.5 * height - radius;
            int seg = Math.Max(8, CurveSegments);
            int nh = Math.Max(2, seg / 4);
            var r = new List<double>();
            var a = new List<double>();
            for (int i = 0; i <= nh; i++)
            {
                double t = 0.5 * Math.PI * i / nh;
                r.Add(i == 0 ? 0.0 : radius * Math.Sin(t));
                a.Add(-half - radius * Math.Cos(t));
            }
            for (int i = nh; i >= 0; i--)
            {
                double t = 0.5 * Math.PI * i / nh;
                r.Add(i == 0 ? 0.0 : radius * Math.Sin(t));
                a.Add(half + radius * Math.Cos(t));
            }
            double exact = Math.PI * radius * radius * (height - 2.0 * radius) + 4.0 / 3.0 * Math.PI * radius * radius * radius;
            return AddTriangles(Lathe(centre, axis, r, a, seg), exact);
        }

        /// <summary>The convex hull of the points (what a convex MeshCollider collides with).</summary>
        public bool AddConvexHull(IList<V3> points)
        {
            var faces = ConvexHull.Build(points);
            if (faces == null)
            {
                Warnings.Add("a convex mesh is flat or has fewer than four points");
                return false;
            }
            return AddTriangles(faces, -1.0);
        }

        /// <summary>An indexed mesh (body coordinates). With closeOpen, a mesh that is not watertight is closed
        /// first (see MeshClosing.Close); the result is reported in Openings: 0 closed already, n openings
        /// capped, -1 replaced by its convex hull.</summary>
        public bool AddMesh(IList<V3> vertices, IList<int> triangles, double exactVolume = -1.0, bool closeOpen = false)
        {
            var faces = new List<V3>(triangles.Count);
            for (int i = 0; i + 2 < triangles.Count; i += 3)
            {
                faces.Add(vertices[triangles[i]]);
                faces.Add(vertices[triangles[i + 1]]);
                faces.Add(vertices[triangles[i + 2]]);
            }
            Openings = 0;
            if (closeOpen)
            {
                int n;
                faces = MeshClosing.Close(faces, out n);
                Openings = n;
            }
            return AddTriangles(faces, exactVolume);
        }

        /// <summary>What the last AddMesh with closeOpen did: 0 nothing (closed already), n openings capped,
        /// -1 the mesh was replaced by its convex hull.</summary>
        public int Openings { get; private set; }

        /// <summary>A closed set of triangles (body coordinates), three points each. With exactVolume > 0 the
        /// faceted volume is scaled to that value; the sign corrects for the winding of the triangles.</summary>
        public bool AddTriangles(IList<V3> faces, double exactVolume = -1.0)
        {
            double faceted = 0.0;
            for (int i = 0; i + 2 < faces.Count; i += 3)
                faceted += faces[i].Dot(faces[i + 1].Cross(faces[i + 2])) / 6.0;
            if (Math.Abs(faceted) < 1e-15)
            {
                Warnings.Add("a buoyancy shape has no volume; is the mesh closed?");
                return false;
            }
            double w = Math.Sign(faceted) * (exactVolume > 0.0 ? exactVolume / Math.Abs(faceted) : 1.0);
            for (int i = 0; i + 2 < faces.Count; i += 3)
            {
                tris.Add(Index(faces[i]));
                tris.Add(Index(faces[i + 1]));
                tris.Add(Index(faces[i + 2]));
                weights.Add(w);
            }
            volume += Math.Abs(faceted) * Math.Abs(w);
            parts++;
            return true;
        }

        int Index(V3 v)
        {
            int k;
            if (!index.TryGetValue(v, out k))
            {
                k = verts.Count;
                index[v] = k;
                verts.Add(v);
            }
            return k;
        }

        public BuoyancyShape3D Build()
        {
            var s = new BuoyancyShape3D();
            if (parts == 1 && spheres.Count == 1)
            {
                double r = spheres[0].r;
                s.SingleSphere = true;
                s.Radius = r;
                s.SphereCentre = spheres[0].c;
                s.Volume = 4.0 / 3.0 * Math.PI * r * r * r;
                s.Centroid = s.SphereCentre;
                s.BoundRadius = r;
                s.Area = Math.PI * r * r;
                s.FaceArea = new V3(s.Area, s.Area, s.Area);
                s.Size = new V3(2.0 * r, 2.0 * r, 2.0 * r);
                return s;
            }
            // Spheres sharing a body with other parts are faceted, with their exact volume.
            int seg = Math.Max(8, CurveSegments);
            int nLat = Math.Max(4, seg / 2);
            foreach (var sp in spheres)
            {
                var r = new List<double>();
                var a = new List<double>();
                for (int k = 0; k <= nLat; k++)
                {
                    double t = Math.PI * k / nLat;
                    r.Add(k == 0 || k == nLat ? 0.0 : sp.r * Math.Sin(t));
                    a.Add(-sp.r * Math.Cos(t));
                }
                parts--;   // AddTriangles counts it again
                AddTriangles(Lathe(sp.c, V3.Up, r, a, seg), 4.0 / 3.0 * Math.PI * sp.r * sp.r * sp.r);
            }
            if (tris.Count == 0 || volume <= 0.0)
                return s;
            s.Vertices = verts.ToArray();
            s.Triangles = tris.ToArray();
            s.Weights = weights.ToArray();
            s.Volume = volume;
            s.Depth = new double[s.Vertices.Length];
            s.WorldPoints = new V3[s.Vertices.Length];
            V3 moment = V3.Zero;
            for (int i = 0; i < s.Triangles.Length; i += 3)
            {
                V3 a = s.Vertices[s.Triangles[i]];
                V3 b = s.Vertices[s.Triangles[i + 1]];
                V3 c = s.Vertices[s.Triangles[i + 2]];
                double t = s.Weights[i / 3] * a.Dot(b.Cross(c)) / 6.0;
                moment += t * (a + b + c) * 0.25;
            }
            s.Centroid = moment / volume;
            V3 lo = s.Vertices[0], hi = s.Vertices[0];
            double bound = 0.0;
            foreach (var v in s.Vertices)
            {
                lo = V3.Min(lo, v);
                hi = V3.Max(hi, v);
                bound = Math.Max(bound, (v - s.Centroid).Length);
            }
            s.BoundRadius = bound;
            V3 size = hi - lo;
            s.Size = size;
            double boxVolume = size.x * size.y * size.z;
            double fill = boxVolume > 0.0 ? WaterVolume.Clamp(volume / boxVolume, 0.3, 1.0) : 1.0;
            s.FaceArea = new V3(size.y * size.z, size.x * size.z, size.x * size.y) * fill;
            s.Area = s.FaceArea.y;
            return s;
        }

        /// <summary>Triangles of a surface of revolution: profile radii r and heights a along the axis, from one
        /// pole (r = 0) to the other, turned through `seg` steps.</summary>
        static List<V3> Lathe(V3 centre, V3 axis, List<double> r, List<double> a, int seg)
        {
            axis = axis.Normalized();
            V3 helper = Math.Abs(axis.x) < 0.9 ? new V3(1, 0, 0) : new V3(0, 1, 0);
            V3 u = axis.Cross(helper).Normalized();
            V3 v = axis.Cross(u);
            var faces = new List<V3>();
            int m = r.Count - 1;
            for (int k = 0; k < m; k++)
            {
                for (int j = 0; j < seg; j++)
                {
                    double t0 = 2.0 * Math.PI * j / seg, t1 = 2.0 * Math.PI * (j + 1) / seg;
                    V3 d0 = u * Math.Cos(t0) + v * Math.Sin(t0);
                    V3 d1 = u * Math.Cos(t1) + v * Math.Sin(t1);
                    V3 p00 = centre + axis * a[k] + d0 * r[k];
                    V3 p01 = centre + axis * a[k] + d1 * r[k];
                    V3 p10 = centre + axis * a[k + 1] + d0 * r[k + 1];
                    V3 p11 = centre + axis * a[k + 1] + d1 * r[k + 1];
                    if (r[k] == 0.0)
                    {
                        faces.Add(p00); faces.Add(p11); faces.Add(p10);
                    }
                    else if (r[k + 1] == 0.0)
                    {
                        faces.Add(p00); faces.Add(p01); faces.Add(p10);
                    }
                    else
                    {
                        faces.Add(p00); faces.Add(p01); faces.Add(p11);
                        faces.Add(p00); faces.Add(p11); faces.Add(p10);
                    }
                }
            }
            return faces;
        }
    }

    /// <summary>Makes a set of triangles watertight. Vertices are welded by position; a mesh with every edge
    /// shared by two triangles is returned as it is (openings = 0). Otherwise each opening (a loop of edges
    /// with a triangle on one side only) is capped with a fan from its centre, which is what buoyancy wants
    /// of a hull with no deck (openings = the number capped). A mesh that cannot be capped (triangles wound
    /// both ways, an edge shared by three faces, loops that cannot be followed) is replaced by the convex
    /// hull of its points (openings = -1).</summary>
    public static class MeshClosing
    {
        struct Key : IEquatable<Key>
        {
            public long a, b, c;
            public bool Equals(Key o) { return a == o.a && b == o.b && c == o.c; }
            public override bool Equals(object o) { return o is Key && Equals((Key)o); }
            public override int GetHashCode() { return (int)(a * 73856093L ^ b * 19349663L ^ c * 83492791L); }
        }

        public static List<V3> Close(IList<V3> faces, out int openings)
        {
            openings = 0;
            var result = new List<V3>(faces);
            if (faces.Count < 9)
                return result;
            // Weld the vertices, so edges match by position.
            double extent = 0.0;
            foreach (V3 v in faces)
                extent = Math.Max(extent, Math.Max(Math.Abs(v.x), Math.Max(Math.Abs(v.y), Math.Abs(v.z))));
            double q = Math.Max(1e-9, extent * 1e-6);
            var index = new Dictionary<Key, int>();
            var verts = new List<V3>();
            var tris = new List<int>();
            for (int i = 0; i + 2 < faces.Count; i += 3)
            {
                int[] t = new int[3];
                for (int k = 0; k < 3; k++)
                {
                    V3 v = faces[i + k];
                    var key = new Key { a = (long)Math.Round(v.x / q), b = (long)Math.Round(v.y / q), c = (long)Math.Round(v.z / q) };
                    int id;
                    if (!index.TryGetValue(key, out id))
                    {
                        id = verts.Count;
                        index[key] = id;
                        verts.Add(v);
                    }
                    t[k] = id;
                }
                if (t[0] == t[1] || t[1] == t[2] || t[0] == t[2])
                    continue;   // a sliver with no area
                tris.AddRange(t);
            }
            // Count the directed edges: a watertight, consistently wound mesh has each edge once each way.
            var edges = new Dictionary<long, int>();
            int n = verts.Count;
            for (int i = 0; i + 2 < tris.Count; i += 3)
                for (int k = 0; k < 3; k++)
                {
                    long e = (long)tris[i + k] * n + tris[i + (k + 1) % 3];
                    int c;
                    edges.TryGetValue(e, out c);
                    edges[e] = c + 1;
                }
            bool tangled = false;
            var open = new Dictionary<int, int>();   // boundary edges a -> b (a triangle on one side only)
            foreach (var e in edges)
            {
                int a = (int)(e.Key / n), b = (int)(e.Key % n);
                int back;
                bool hasBack = edges.TryGetValue((long)b * n + a, out back);
                if (e.Value > 1 || (hasBack && back > 1))
                    tangled = true;
                else if (!hasBack)
                {
                    if (open.ContainsKey(a))
                        tangled = true;   // two openings meet at a vertex: the loops cannot be followed
                    open[a] = b;
                }
            }
            if (open.Count == 0 && !tangled)
                return result;   // closed already
            if (!tangled)
            {
                // Follow each loop of boundary edges and cap it with a fan from its centre, wound the
                // opposite way to the triangles it closes.
                var outFaces = new List<V3>(faces);
                var seen = new HashSet<int>();
                int loops = 0;
                foreach (var start in open)
                {
                    if (seen.Contains(start.Key))
                        continue;
                    var loop = new List<int>();
                    int v = start.Key;
                    bool ok = true;
                    for (;;)
                    {
                        if (seen.Contains(v)) { ok = v == start.Key; break; }
                        seen.Add(v);
                        loop.Add(v);
                        int next;
                        if (!open.TryGetValue(v, out next)) { ok = false; break; }
                        v = next;
                    }
                    if (!ok || loop.Count < 3) { tangled = true; break; }
                    V3 centre = V3.Zero;
                    foreach (int j in loop)
                        centre = centre + verts[j];
                    centre = centre / loop.Count;
                    for (int j = 0; j < loop.Count; j++)
                    {
                        outFaces.Add(verts[loop[(j + 1) % loop.Count]]);
                        outFaces.Add(verts[loop[j]]);
                        outFaces.Add(centre);
                    }
                    loops++;
                }
                if (!tangled)
                {
                    openings = loops;
                    return outFaces;
                }
            }
            openings = -1;
            var hull = ConvexHull.Build(verts);
            return hull ?? result;
        }
    }
}
