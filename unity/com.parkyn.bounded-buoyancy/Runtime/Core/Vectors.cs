// Bounded Buoyancy for Unity. Engine-free core: no UnityEngine types, so it can be tested in plain .NET.
// Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System;

namespace BoundedBuoyancy.Core
{
    /// <summary>A 3D vector in double precision.</summary>
    public struct V3 : IEquatable<V3>
    {
        public double x, y, z;

        public V3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }

        public static V3 Zero { get { return new V3(0.0, 0.0, 0.0); } }
        public static V3 Up { get { return new V3(0.0, 1.0, 0.0); } }

        public static V3 operator +(V3 a, V3 b) { return new V3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static V3 operator -(V3 a, V3 b) { return new V3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static V3 operator -(V3 a) { return new V3(-a.x, -a.y, -a.z); }
        public static V3 operator *(V3 a, double s) { return new V3(a.x * s, a.y * s, a.z * s); }
        public static V3 operator *(double s, V3 a) { return new V3(a.x * s, a.y * s, a.z * s); }
        public static V3 operator /(V3 a, double s) { return new V3(a.x / s, a.y / s, a.z / s); }

        public double Dot(V3 b) { return x * b.x + y * b.y + z * b.z; }
        public V3 Cross(V3 b) { return new V3(y * b.z - z * b.y, z * b.x - x * b.z, x * b.y - y * b.x); }
        public double Length { get { return Math.Sqrt(x * x + y * y + z * z); } }
        public double LengthSquared { get { return x * x + y * y + z * z; } }
        public V3 Abs() { return new V3(Math.Abs(x), Math.Abs(y), Math.Abs(z)); }
        public V3 Normalized() { double n = Length; return n > 0.0 ? this / n : Zero; }
        public static V3 Min(V3 a, V3 b) { return new V3(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Min(a.z, b.z)); }
        public static V3 Max(V3 a, V3 b) { return new V3(Math.Max(a.x, b.x), Math.Max(a.y, b.y), Math.Max(a.z, b.z)); }

        public bool Equals(V3 o) { return x == o.x && y == o.y && z == o.z; }
        public override bool Equals(object o) { return o is V3 && Equals((V3)o); }
        public override int GetHashCode()
        {
            unchecked
            {
                // 0 and -0 are equal, so they must hash alike.
                int h = (x == 0.0 ? 0.0 : x).GetHashCode();
                h = h * 397 ^ (y == 0.0 ? 0.0 : y).GetHashCode();
                return h * 397 ^ (z == 0.0 ? 0.0 : z).GetHashCode();
            }
        }
        public override string ToString() { return "(" + x.ToString("G6") + ", " + y.ToString("G6") + ", " + z.ToString("G6") + ")"; }
    }

    /// <summary>A 3x3 matrix stored by columns: the body's axes in world space when used for a pose.</summary>
    public struct M3
    {
        public V3 X, Y, Z;

        public M3(V3 x, V3 y, V3 z) { X = x; Y = y; Z = z; }

        public static M3 Identity { get { return new M3(new V3(1, 0, 0), new V3(0, 1, 0), new V3(0, 0, 1)); } }

        /// <summary>This matrix times v (body to world for a pose).</summary>
        public V3 Mul(V3 v) { return X * v.x + Y * v.y + Z * v.z; }
        /// <summary>The transpose times v (world to body for a rotation).</summary>
        public V3 MulT(V3 v) { return new V3(X.Dot(v), Y.Dot(v), Z.Dot(v)); }
        /// <summary>The second row: world height gained per unit of each body axis.</summary>
        public V3 Row1 { get { return new V3(X.y, Y.y, Z.y); } }
        public double Determinant { get { return X.Dot(Y.Cross(Z)); } }

        /// <summary>Rotation about the world vertical by `degrees`, turning +X towards +Z.</summary>
        public static M3 TurnAboutY(double degrees)
        {
            double a = degrees * Math.PI / 180.0;
            double c = Math.Cos(a), s = Math.Sin(a);
            return new M3(new V3(c, 0, s), new V3(0, 1, 0), new V3(-s, 0, c));
        }

        /// <summary>Rotation about the world Z axis by `degrees`, turning +X towards +Y (a roll).</summary>
        public static M3 TurnAboutZ(double degrees)
        {
            double a = degrees * Math.PI / 180.0;
            double c = Math.Cos(a), s = Math.Sin(a);
            return new M3(new V3(c, s, 0), new V3(-s, c, 0), new V3(0, 0, 1));
        }

        /// <summary>Rotation matrix of a unit quaternion (x, y, z, w).</summary>
        public static M3 FromQuaternion(double qx, double qy, double qz, double qw)
        {
            double xx = qx * qx, yy = qy * qy, zz = qz * qz;
            double xy = qx * qy, xz = qx * qz, yz = qy * qz;
            double wx = qw * qx, wy = qw * qy, wz = qw * qz;
            return new M3(
                new V3(1 - 2 * (yy + zz), 2 * (xy + wz), 2 * (xz - wy)),
                new V3(2 * (xy - wz), 1 - 2 * (xx + zz), 2 * (yz + wx)),
                new V3(2 * (xz + wy), 2 * (yz - wx), 1 - 2 * (xx + yy)));
        }

        public static M3 operator *(M3 a, M3 b) { return new M3(a.Mul(b.X), a.Mul(b.Y), a.Mul(b.Z)); }
    }

    /// <summary>Position and orientation of a rigid body (no scale).</summary>
    public struct BodyPose
    {
        public V3 Position;
        public M3 Basis;

        public BodyPose(V3 position, M3 basis) { Position = position; Basis = basis; }

        /// <summary>A point given in the body's own coordinates, in world space.</summary>
        public V3 Apply(V3 p) { return Position + Basis.Mul(p); }
        /// <summary>A world point in the body's own coordinates.</summary>
        public V3 Inverse(V3 p) { return Basis.MulT(p - Position); }
    }

    /// <summary>A 2D vector in double precision.</summary>
    public struct V2 : IEquatable<V2>
    {
        public double x, y;

        public V2(double x, double y) { this.x = x; this.y = y; }

        public static V2 Zero { get { return new V2(0.0, 0.0); } }

        public static V2 operator +(V2 a, V2 b) { return new V2(a.x + b.x, a.y + b.y); }
        public static V2 operator -(V2 a, V2 b) { return new V2(a.x - b.x, a.y - b.y); }
        public static V2 operator -(V2 a) { return new V2(-a.x, -a.y); }
        public static V2 operator *(V2 a, double s) { return new V2(a.x * s, a.y * s); }
        public static V2 operator *(double s, V2 a) { return new V2(a.x * s, a.y * s); }
        public static V2 operator /(V2 a, double s) { return new V2(a.x / s, a.y / s); }

        public double Dot(V2 b) { return x * b.x + y * b.y; }
        /// <summary>z of the 3D cross product: positive when b is anticlockwise from this.</summary>
        public double Cross(V2 b) { return x * b.y - y * b.x; }
        public double Length { get { return Math.Sqrt(x * x + y * y); } }
        public double LengthSquared { get { return x * x + y * y; } }
        public V2 Abs() { return new V2(Math.Abs(x), Math.Abs(y)); }
        public static V2 FromAngle(double radians) { return new V2(Math.Cos(radians), Math.Sin(radians)); }

        public bool Equals(V2 o) { return x == o.x && y == o.y; }
        public override bool Equals(object o) { return o is V2 && Equals((V2)o); }
        public override int GetHashCode() { unchecked { return (x == 0.0 ? 0.0 : x).GetHashCode() * 397 ^ (y == 0.0 ? 0.0 : y).GetHashCode(); } }
        public override string ToString() { return "(" + x.ToString("G6") + ", " + y.ToString("G6") + ")"; }
    }

    /// <summary>Position and rotation of a 2D rigid body. The angle is anticlockwise, in radians.</summary>
    public struct BodyPose2D
    {
        public V2 Position;
        public double Cos, Sin;

        public BodyPose2D(V2 position, double angleRadians)
        {
            Position = position;
            Cos = Math.Cos(angleRadians);
            Sin = Math.Sin(angleRadians);
        }

        public V2 Rotate(V2 v) { return new V2(Cos * v.x - Sin * v.y, Sin * v.x + Cos * v.y); }
        public V2 RotateBack(V2 v) { return new V2(Cos * v.x + Sin * v.y, -Sin * v.x + Cos * v.y); }
        public V2 Apply(V2 p) { return Position + Rotate(p); }
        public V2 Inverse(V2 p) { return RotateBack(p - Position); }
    }

    /// <summary>A symmetric 3x3 matrix, used for inertia tensors.</summary>
    public struct Sym3
    {
        public double xx, yy, zz, xy, xz, yz;

        public static Sym3 operator *(Sym3 a, double s)
        {
            return new Sym3 { xx = a.xx * s, yy = a.yy * s, zz = a.zz * s, xy = a.xy * s, xz = a.xz * s, yz = a.yz * s };
        }

        public static Sym3 operator +(Sym3 a, Sym3 b)
        {
            return new Sym3 { xx = a.xx + b.xx, yy = a.yy + b.yy, zz = a.zz + b.zz, xy = a.xy + b.xy, xz = a.xz + b.xz, yz = a.yz + b.yz };
        }

        /// <summary>The tensor with principal moments `m` along the columns of `axes`: A diag(m) Aᵀ.</summary>
        public static Sym3 FromPrincipal(V3 m, M3 axes)
        {
            V3 a = axes.X, b = axes.Y, c = axes.Z;
            return new Sym3
            {
                xx = m.x * a.x * a.x + m.y * b.x * b.x + m.z * c.x * c.x,
                yy = m.x * a.y * a.y + m.y * b.y * b.y + m.z * c.y * c.y,
                zz = m.x * a.z * a.z + m.y * b.z * b.z + m.z * c.z * c.z,
                xy = m.x * a.x * a.y + m.y * b.x * b.y + m.z * c.x * c.y,
                xz = m.x * a.x * a.z + m.y * b.x * b.z + m.z * c.x * c.z,
                yz = m.x * a.y * a.z + m.y * b.y * b.z + m.z * c.y * c.z,
            };
        }

        /// <summary>Principal moments and axes (columns of a right-handed rotation): I = A diag(m) Aᵀ.</summary>
        public void Principal(out V3 moments, out M3 axes)
        {
            double[,] a = { { xx, xy, xz }, { xy, yy, yz }, { xz, yz, zz } };
            double[,] v = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            double scale = Math.Abs(xx) + Math.Abs(yy) + Math.Abs(zz) + 1e-300;
            for (int sweep = 0; sweep < 60; sweep++)
            {
                double off = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
                if (off <= 1e-15 * scale)
                    break;
                for (int p = 0; p < 2; p++)
                {
                    for (int q = p + 1; q < 3; q++)
                    {
                        if (Math.Abs(a[p, q]) <= 1e-18 * scale)
                            continue;
                        double theta = (a[q, q] - a[p, p]) / (2.0 * a[p, q]);
                        double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1.0));
                        if (theta == 0.0)
                            t = 1.0;
                        double c = 1.0 / Math.Sqrt(t * t + 1.0);
                        double s = t * c;
                        // A <- Jᵀ A J and V <- V J, with J the rotation in the (p, q) plane.
                        for (int k = 0; k < 3; k++)
                        {
                            double akp = a[k, p], akq = a[k, q];
                            a[k, p] = c * akp - s * akq;
                            a[k, q] = s * akp + c * akq;
                        }
                        for (int k = 0; k < 3; k++)
                        {
                            double apk = a[p, k], aqk = a[q, k];
                            a[p, k] = c * apk - s * aqk;
                            a[q, k] = s * apk + c * aqk;
                        }
                        for (int k = 0; k < 3; k++)
                        {
                            double vkp = v[k, p], vkq = v[k, q];
                            v[k, p] = c * vkp - s * vkq;
                            v[k, q] = s * vkp + c * vkq;
                        }
                    }
                }
            }
            moments = new V3(a[0, 0], a[1, 1], a[2, 2]);
            var c0 = new V3(v[0, 0], v[1, 0], v[2, 0]);
            var c1 = new V3(v[0, 1], v[1, 1], v[2, 1]);
            var c2 = new V3(v[0, 2], v[1, 2], v[2, 2]);
            if (c0.Dot(c1.Cross(c2)) < 0.0)
                c0 = -c0;
            axes = new M3(c0, c1, c2);
        }
    }
}
