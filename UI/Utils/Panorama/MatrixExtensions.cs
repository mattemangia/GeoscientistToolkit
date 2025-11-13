// GeoscientistToolkit/Business/Photogrammetry/Math/MatrixExtensions.cs

using System.Numerics;
using GeoscientistToolkit.Business.Photogrammetry;
using MathNet.Numerics.LinearAlgebra;
namespace GeoscientistToolkit
{
    /// <summary>
    /// Extension methods for matrix operations in photogrammetry
    /// </summary>
    public static class MatrixExtensions
    {
        public static Matrix3x3 As3x3(this Matrix4x4 m)
        {
            return new Matrix3x3(
                m.M11, m.M12, m.M13,
                m.M21, m.M22, m.M23,
                m.M31, m.M32, m.M33
            );
        }

        public static Matrix3x3 As3x3Transposed(this Matrix4x4 m)
        {
            return new Matrix3x3(
                m.M11, m.M21, m.M31,
                m.M12, m.M22, m.M32,
                m.M13, m.M23, m.M33
            );
        }

        public static float[,] As3x4(this Matrix4x4 m)
        {
            return new float[,]
            {
                { m.M11, m.M12, m.M13, m.M14 },
                { m.M21, m.M22, m.M23, m.M24 },
                { m.M31, m.M32, m.M33, m.M34 }
            };
        }

        public static Matrix4x4 CreateFrom(Matrix3x3 rotation, Vector3 translation)
        {
            return new Matrix4x4(
                rotation.M11, rotation.M12, rotation.M13, 0,
                rotation.M21, rotation.M22, rotation.M23, 0,
                rotation.M31, rotation.M32, rotation.M33, 0,
                translation.X, translation.Y, translation.Z, 1
            );
        }

        public static Matrix3x3 ToMatrix3x3(this double[,] m)
        {
            return new Matrix3x3(
                (float)m[0, 0], (float)m[0, 1], (float)m[0, 2],
                (float)m[1, 0], (float)m[1, 1], (float)m[1, 2],
                (float)m[2, 0], (float)m[2, 1], (float)m[2, 2]
            );
        }
        
        public static Matrix3x3 ToMatrix3x3(this Matrix<double> m)
        {
            if (m.RowCount != 3 || m.ColumnCount != 3)
            {
                throw new ArgumentException("Matrix must be 3x3 to convert to Matrix3x3.", nameof(m));
            }

            return new Matrix3x3(
                (float)m[0, 0], (float)m[0, 1], (float)m[0, 2],
                (float)m[1, 0], (float)m[1, 1], (float)m[1, 2],
                (float)m[2, 0], (float)m[2, 1], (float)m[2, 2]
            );
        }
        /// <summary>
        /// Inverts the given 4x4 matrix. Returns the original matrix if inversion fails.
        /// </summary>
        public static Matrix4x4 As4x4Inverted(this Matrix4x4 source)
        {
            if (Matrix4x4.Invert(source, out var inverted))
            {
                return inverted;
            }
            // Return identity or source if inversion is not possible, to avoid crashes.
            // For camera poses, this should not happen.
            return source;
        }
    }
}