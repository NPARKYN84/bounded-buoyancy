// Bounded Buoyancy for Unity. Engine-free core. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System;

namespace BoundedBuoyancy.Core
{
    /// <summary>The part of a body below the water surface: its volume and the centre of that volume.</summary>
    public static class Immersion3D
    {
        /// <summary>Submerged volume and its centre (world) for the body at `pose`. With waves, each vertex is
        /// measured against the surface above it.</summary>
        public static void Immerse(BuoyancyShape3D s, BodyPose pose, WaterSurface surface, out double volume, out V3 centre)
        {
            double level;
            Immerse(s, pose, surface, out volume, out centre, out level);
        }

        /// <summary>As above, and `level` the height of the surface over the centre of the body's volume.</summary>
        public static void Immerse(BuoyancyShape3D s, BodyPose pose, WaterSurface surface, out double volume, out V3 centre, out double level)
        {
            if (s.SingleSphere)
            {
                // Exact spherical cap, the same whichever way the sphere has turned.
                V3 c = pose.Apply(s.SphereCentre);
                double r = s.Radius;
                level = surface.HeightAt(c);
                double h = WaterVolume.Clamp(r + level - c.y, 0.0, 2.0 * r);
                volume = Math.PI * h * h * (3.0 * r - h) / 3.0;
                double drop = h <= 0.0 ? 0.0 : 3.0 * (2.0 * r - h) * (2.0 * r - h) / (4.0 * (3.0 * r - h));
                centre = c - V3.Up * drop;
                return;
            }
            V3 cw = pose.Apply(s.Centroid);
            if (s.IsEmpty)
            {
                volume = 0.0;
                centre = cw;
                level = surface.HeightAt(cw);
                return;
            }
            level = surface.HeightAt(cw);
            // swell bounds how far the surface anywhere under the body can be from its height over the centre: twice
            // the most it rises or falls from its mean.
            double swell = 2.0 * surface.MaxWave;
            if (cw.y - s.BoundRadius >= level + swell)
            {
                volume = 0.0;
                centre = cw;
                return;
            }
            if (cw.y + s.BoundRadius <= level - swell)
            {
                volume = s.Volume;
                centre = cw;
                return;
            }
            // Work in the body's own coordinates: only the depth of each vertex needs the pose. Each triangle is
            // cut at the surface, and the part below makes tetrahedra with a point p on the surface. The cut face
            // lies in the surface plane, so it adds nothing: the sum is the exact volume below the surface, and its
            // first moment gives the centre of buoyancy.
            V3 up = pose.Basis.Row1;   // world height per unit of each body axis
            double hb = level - pose.Position.y;
            V3 p = s.Centroid + up * ((hb - up.Dot(s.Centroid)) / up.LengthSquared);
            V3[] verts = s.Vertices;
            double[] depth = s.Depth;
            if (surface.Source != null)
            {
                // Another water system's surface: ask it for the heights over all the vertices at once.
                V3[] world = s.WorldPoints;
                for (int j = 0; j < verts.Length; j++)
                    world[j] = pose.Apply(verts[j]);
                surface.Source.HeightsAt(world, depth, verts.Length);
                for (int j = 0; j < verts.Length; j++)
                    depth[j] -= world[j].y;
            }
            else if (surface.Wavy)
            {
                // Waves: measure each vertex against the surface above it. The cut then follows the waves, which
                // is accurate while the waves are long compared with the body.
                for (int j = 0; j < verts.Length; j++)
                {
                    V3 wp = pose.Apply(verts[j]);
                    depth[j] = surface.HeightAt(wp) - wp.y;
                }
            }
            else
            {
                for (int j = 0; j < verts.Length; j++)
                    depth[j] = hb - up.Dot(verts[j]);
            }
            double vol;
            V3 moment;
            Clip(verts, s.Triangles, s.Weights, depth, p, out vol, out moment);
            volume = WaterVolume.Clamp(vol, 0.0, s.Volume);
            centre = vol > 1e-12 ? pose.Apply(moment / (4.0 * vol)) : cw;
        }

        /// <summary>Volume below the surface (depth > 0) of a closed weighted triangle set, and its first moment
        /// times four, from tetrahedra with the point p on the surface.</summary>
        public static void Clip(V3[] verts, int[] idx, double[] weight, double[] depth, V3 p, out double volume, out V3 moment)
        {
            double vol = 0.0;
            double mx = 0.0, my = 0.0, mz = 0.0;
            for (int i = 0; i < idx.Length; i += 3)
            {
                int ia = idx[i], ib = idx[i + 1], ic = idx[i + 2];
                double da = depth[ia], db = depth[ib], dc = depth[ic];
                int under = (da > 0.0 ? 1 : 0) + (db > 0.0 ? 1 : 0) + (dc > 0.0 ? 1 : 0);
                if (under == 0)
                    continue;
                double w = weight[i / 3];
                V3 a = verts[ia], b = verts[ib], c = verts[ic];
                if (under == 3)
                {
                    double t3 = w * Tet(p, a, b, c);
                    vol += t3;
                    V3 s3 = p + a + b + c;
                    mx += t3 * s3.x; my += t3 * s3.y; mz += t3 * s3.z;
                    continue;
                }
                // Turn the triangle (keeping its winding) so that the odd vertex out comes first.
                if ((under == 1 && db > 0.0) || (under == 2 && db <= 0.0))
                {
                    V3 tv = a; a = b; b = c; c = tv;
                    double td = da; da = db; db = dc; dc = td;
                }
                else if ((under == 1 && dc > 0.0) || (under == 2 && dc <= 0.0))
                {
                    V3 tv = c; c = b; b = a; a = tv;
                    double td = dc; dc = db; db = da; da = td;
                }
                V3 ab = a + (b - a) * (da / (da - db));
                V3 ca = c + (a - c) * (dc / (dc - da));
                if (under == 1)
                {
                    double t1 = w * Tet(p, a, ab, ca);
                    vol += t1;
                    V3 s1 = p + a + ab + ca;
                    mx += t1 * s1.x; my += t1 * s1.y; mz += t1 * s1.z;
                }
                else
                {
                    double t2a = w * Tet(p, ab, b, c);
                    double t2b = w * Tet(p, ab, c, ca);
                    vol += t2a + t2b;
                    V3 sa = p + ab + b + c, sb = p + ab + c + ca;
                    mx += t2a * sa.x + t2b * sb.x;
                    my += t2a * sa.y + t2b * sb.y;
                    mz += t2a * sa.z + t2b * sb.z;
                }
            }
            volume = vol;
            moment = new V3(mx, my, mz);
        }

        static double Tet(V3 p, V3 a, V3 b, V3 c)
        {
            return (a - p).Dot((b - p).Cross(c - p)) / 6.0;
        }
    }
}
