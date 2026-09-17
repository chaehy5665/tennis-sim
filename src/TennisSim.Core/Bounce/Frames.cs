using System;

namespace TennisSim.Core.Bounce
{
    // Orthogonal transform helper. Velocity is a true vector (v' = A v); angular velocity is a
    // polar vector (omega' = det(A) * A omega). Reflections therefore need the determinant sign.
    public struct Mat3
    {
        public double M11, M12, M13, M21, M22, M23, M31, M32, M33;

        public static Mat3 Identity => new Mat3 { M11 = 1, M22 = 1, M33 = 1 };

        public static Mat3 Rotation(Vec3 unitAxis, double angle)
        {
            if (!unitAxis.IsFinite) throw new ArgumentException("Rotation axis must be finite");
            double length = unitAxis.Length;
            if (Math.Abs(length - 1) > 1e-12) throw new ArgumentException("Rotation axis must be normalized");
            double c = Math.Cos(angle), s = Math.Sin(angle), t = 1 - c;
            double x = unitAxis.X, y = unitAxis.Y, z = unitAxis.Z;
            return new Mat3
            {
                M11 = t * x * x + c, M12 = t * x * y - s * z, M13 = t * x * z + s * y,
                M21 = t * x * y + s * z, M22 = t * y * y + c, M23 = t * y * z - s * x,
                M31 = t * x * z - s * y, M32 = t * y * z + s * x, M33 = t * z * z + c
            };
        }

        public static Mat3 Reflection(int axis)
        {
            var m = Identity;
            if (axis == 0) m.M11 = -1; else if (axis == 1) m.M22 = -1; else if (axis == 2) m.M33 = -1;
            else throw new ArgumentException("Reflection axis must be 0, 1 or 2");
            return m;
        }

        public double Determinant => M11 * (M22 * M33 - M23 * M32) - M12 * (M21 * M33 - M23 * M31) + M13 * (M21 * M32 - M22 * M31);

        public Mat3 Transpose() => new Mat3
        {
            M11 = M11, M12 = M21, M13 = M31,
            M21 = M12, M22 = M22, M23 = M32,
            M31 = M13, M32 = M23, M33 = M33
        };

        public Mat3 Multiply(Mat3 b) => new Mat3
        {
            M11 = M11 * b.M11 + M12 * b.M21 + M13 * b.M31, M12 = M11 * b.M12 + M12 * b.M22 + M13 * b.M32, M13 = M11 * b.M13 + M12 * b.M23 + M13 * b.M33,
            M21 = M21 * b.M11 + M22 * b.M21 + M23 * b.M31, M22 = M21 * b.M12 + M22 * b.M22 + M23 * b.M32, M23 = M21 * b.M13 + M22 * b.M23 + M23 * b.M33,
            M31 = M31 * b.M11 + M32 * b.M21 + M33 * b.M31, M32 = M31 * b.M12 + M32 * b.M22 + M33 * b.M32, M33 = M31 * b.M13 + M32 * b.M23 + M33 * b.M33
        };

        public Vec3 Apply(Vec3 v) => new Vec3(M11 * v.X + M12 * v.Y + M13 * v.Z, M21 * v.X + M22 * v.Y + M23 * v.Z, M31 * v.X + M32 * v.Y + M33 * v.Z);

        // Polar vector transform. det(A) = +1 for proper rotations; -1 for reflections.
        public Vec3 ApplyAngular(Vec3 w)
        {
            double sign = Determinant;
            Vec3 rotated = Apply(w);
            return new Vec3(rotated.X * sign, rotated.Y * sign, rotated.Z * sign);
        }

        public ImpactState ApplyState(ImpactState state) => new ImpactState
        {
            PositionM = Apply(state.PositionM),
            VelocityMS = Apply(state.VelocityMS),
            AngularVelocityRadS = ApplyAngular(state.AngularVelocityRadS),
            ImpactTimeS = state.ImpactTimeS
        };
    }
}
