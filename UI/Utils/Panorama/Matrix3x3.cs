// GeoscientistToolkit/Business/Photogrammetry/Math/Matrix3x3.cs

using System;
using System.Numerics;

namespace GeoscientistToolkit
{
    /// <summary>
    /// Represents a 3x3 matrix for photogrammetry calculations
    /// </summary>
    public struct Matrix3x3
    {
        public float M11, M12, M13, M21, M22, M23, M31, M32, M33;
        
        public static readonly Matrix3x3 Zero = new(0, 0, 0, 0, 0, 0, 0, 0, 0);
        public static readonly Matrix3x3 Identity = CreateDiagonal(1, 1, 1);

        public Matrix3x3(float m11, float m12, float m13, float m21, float m22, float m23, 
                        float m31, float m32, float m33)
        {
            M11 = m11; M12 = m12; M13 = m13;
            M21 = m21; M22 = m22; M23 = m23;
            M31 = m31; M32 = m32; M33 = m33;
        }

        public Matrix3x3(float[] values)
        {
            if (values.Length != 9)
                throw new ArgumentException("Array must contain exactly 9 elements", nameof(values));
            
            M11 = values[0]; M12 = values[1]; M13 = values[2];
            M21 = values[3]; M22 = values[4]; M23 = values[5];
            M31 = values[6]; M32 = values[7]; M33 = values[8];
        }

        public float this[int row, int col]
        {
            get => (row, col) switch
            {
                (0, 0) => M11, (0, 1) => M12, (0, 2) => M13,
                (1, 0) => M21, (1, 1) => M22, (1, 2) => M23,
                (2, 0) => M31, (2, 1) => M32, (2, 2) => M33,
                _ => throw new IndexOutOfRangeException()
            };
            set
            {
                switch ((row, col))
                {
                    case (0, 0): M11 = value; break;
                    case (0, 1): M12 = value; break;
                    case (0, 2): M13 = value; break;
                    case (1, 0): M21 = value; break;
                    case (1, 1): M22 = value; break;
                    case (1, 2): M23 = value; break;
                    case (2, 0): M31 = value; break;
                    case (2, 1): M32 = value; break;
                    case (2, 2): M33 = value; break;
                    default: throw new IndexOutOfRangeException();
                }
            }
        }

        public static Matrix3x3 operator *(Matrix3x3 a, Matrix3x3 b)
        {
            return new Matrix3x3(
                a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31,
                a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32,
                a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33,
                a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31,
                a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32,
                a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33,
                a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31,
                a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32,
                a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33
            );
        }

        public static Vector3 operator *(Matrix3x3 m, Vector3 v)
        {
            return new Vector3(
                m.M11 * v.X + m.M12 * v.Y + m.M13 * v.Z,
                m.M21 * v.X + m.M22 * v.Y + m.M23 * v.Z,
                m.M31 * v.X + m.M32 * v.Y + m.M33 * v.Z
            );
        }

        public static Matrix3x3 operator *(float scalar, Matrix3x3 m)
        {
            return new Matrix3x3(
                scalar * m.M11, scalar * m.M12, scalar * m.M13,
                scalar * m.M21, scalar * m.M22, scalar * m.M23,
                scalar * m.M31, scalar * m.M32, scalar * m.M33
            );
        }

        public static Matrix3x3 operator *(Matrix3x3 m, float scalar) => scalar * m;

        public static Matrix3x3 operator +(Matrix3x3 a, Matrix3x3 b)
        {
            return new Matrix3x3(
                a.M11 + b.M11, a.M12 + b.M12, a.M13 + b.M13,
                a.M21 + b.M21, a.M22 + b.M22, a.M23 + b.M23,
                a.M31 + b.M31, a.M32 + b.M32, a.M33 + b.M33
            );
        }
        
        public static Matrix3x3 operator -(Matrix3x3 a, Matrix3x3 b)
        {
            return new Matrix3x3(
                a.M11 - b.M11, a.M12 - b.M12, a.M13 - b.M13,
                a.M21 - b.M21, a.M22 - b.M22, a.M23 - b.M23,
                a.M31 - b.M31, a.M32 - b.M32, a.M33 - b.M33
            );
        }

        public static Matrix3x3 operator -(Matrix3x3 m) => -1 * m;

        public static Matrix3x3 Transpose(Matrix3x3 m)
        {
            return new Matrix3x3(
                m.M11, m.M21, m.M31,
                m.M12, m.M22, m.M32,
                m.M13, m.M23, m.M33
            );
        }

        public static float Determinant(Matrix3x3 m)
        {
            return m.M11 * (m.M22 * m.M33 - m.M23 * m.M32) -
                   m.M12 * (m.M21 * m.M33 - m.M23 * m.M31) +
                   m.M13 * (m.M21 * m.M32 - m.M22 * m.M31);
        }

        /// <summary>
        /// Calculates the inverse of a 3x3 matrix.
        /// </summary>
        /// <param name="matrix">The matrix to invert.</param>
        /// <param name="result">The inverted matrix.</param>
        /// <returns>True if the matrix was inverted successfully, false otherwise (if the matrix is singular).</returns>
        public static bool Invert(Matrix3x3 matrix, out Matrix3x3 result)
        {
            float det = Determinant(matrix);

            if (Math.Abs(det) < 1e-8f)
            {
                result = Zero;
                return false;
            }

            float invDet = 1.0f / det;
            
            result = new Matrix3x3(
                invDet * (matrix.M22 * matrix.M33 - matrix.M23 * matrix.M32),
                invDet * (matrix.M13 * matrix.M32 - matrix.M12 * matrix.M33),
                invDet * (matrix.M12 * matrix.M23 - matrix.M13 * matrix.M22),
                
                invDet * (matrix.M23 * matrix.M31 - matrix.M21 * matrix.M33),
                invDet * (matrix.M11 * matrix.M33 - matrix.M13 * matrix.M31),
                invDet * (matrix.M13 * matrix.M21 - matrix.M11 * matrix.M23),
                
                invDet * (matrix.M21 * matrix.M32 - matrix.M22 * matrix.M31),
                invDet * (matrix.M12 * matrix.M31 - matrix.M11 * matrix.M32),
                invDet * (matrix.M11 * matrix.M22 - matrix.M12 * matrix.M21)
            );
            
            return true;
        }

        public static Matrix3x3 CreateDiagonal(float s1, float s2, float s3)
        {
            return new Matrix3x3(s1, 0, 0, 0, s2, 0, 0, 0, s3);
        }

        public static Matrix3x3 CreateFromOuterProduct(Vector3 a, Vector3 b)
        {
            return new Matrix3x3(
                a.X * b.X, a.X * b.Y, a.X * b.Z,
                a.Y * b.X, a.Y * b.Y, a.Y * b.Z,
                a.Z * b.X, a.Z * b.Y, a.Z * b.Z
            );
        }

        public static Matrix3x3 CreateSkewSymmetric(Vector3 v)
        {
            return new Matrix3x3(
                0, -v.Z, v.Y,
                v.Z, 0, -v.X,
                -v.Y, v.X, 0
            );
        }

        /// <summary>
        /// Converts a rotation vector (axis-angle representation) to a rotation matrix using Rodrigues' formula
        /// </summary>
        /// <param name="w">Rotation vector where direction is the axis and magnitude is the angle in radians</param>
        /// <returns>The corresponding 3x3 rotation matrix</returns>
        public static Matrix3x3 Rodrigues(Vector3 w)
        {
            float theta = w.Length();
            
            if (theta < 1e-8f)
            {
                return Identity + CreateSkewSymmetric(w);
            }

            var axis = w / theta;
            var K = CreateSkewSymmetric(axis);
            var K2 = K * K;
            
            float sinTheta = MathF.Sin(theta);
            float cosTheta = MathF.Cos(theta);
            
            return Identity + (sinTheta * K) + ((1.0f - cosTheta) * K2);
        }

        /// <summary>
        /// Converts a rotation matrix to a rotation vector (axis-angle representation) - inverse of Rodrigues
        /// </summary>
        /// <param name="R">The rotation matrix</param>
        /// <returns>The rotation vector</returns>
        public static Vector3 InverseRodrigues(Matrix3x3 R)
        {
            float trace = R.M11 + R.M22 + R.M33;
            float theta = MathF.Acos(Math.Clamp((trace - 1) / 2, -1.0f, 1.0f));
            
            if (MathF.Abs(theta) < 1e-8f)
            {
                return Vector3.Zero;
            }
            
            if (MathF.Abs(theta - MathF.PI) < 1e-8f)
            {
                float xx = (R.M11 + 1) / 2;
                float yy = (R.M22 + 1) / 2;
                float zz = (R.M33 + 1) / 2;
                float xy = (R.M12 + R.M21) / 4;
                float xz = (R.M13 + R.M31) / 4;
                float yz = (R.M23 + R.M32) / 4;
                
                Vector3 axis;
                if (xx > yy && xx > zz)
                {
                    float x = MathF.Sqrt(xx);
                    axis = new Vector3(x, xy / x, xz / x);
                }
                else if (yy > zz)
                {
                    float y = MathF.Sqrt(yy);
                    axis = new Vector3(xy / y, y, yz / y);
                }
                else
                {
                    float z = MathF.Sqrt(zz);
                    axis = new Vector3(xz / z, yz / z, z);
                }
                
                return axis * theta;
            }
            
            float coefficient = theta / (2 * MathF.Sin(theta));
            return new Vector3(
                R.M32 - R.M23,
                R.M13 - R.M31,
                R.M21 - R.M12
            ) * coefficient;
        }

        public double[,] ToDoubleArray2D()
        {
            return new double[,]
            {
                { M11, M12, M13 },
                { M21, M22, M23 },
                { M31, M32, M33 }
            };
        }
    }
}