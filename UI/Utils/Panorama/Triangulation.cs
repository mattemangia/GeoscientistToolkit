// GeoscientistToolkit/Business/Photogrammetry/Triangulation.cs

using System;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace GeoscientistToolkit
{
    /// <summary>
    /// Provides triangulation methods for 3D reconstruction using Direct Linear Transform (DLT).
    /// Reference: Hartley & Zisserman, "Multiple View Geometry in Computer Vision" (2003), Section 12.2
    /// </summary>
    public static class Triangulation
    {
        /// <summary>
        /// Triangulates a 3D point from two image observations using linear DLT method.
        /// Uses Math.NET matrices exclusively with column-vector convention.
        /// </summary>
        /// <param name="p1">Observed 2D point in the first image (pixel coordinates).</param>
        /// <param name="p2">Observed 2D point in the second image (pixel coordinates).</param>
        /// <param name="K1">Intrinsic matrix of the first camera (3x3).</param>
        /// <param name="K2">Intrinsic matrix of the second camera (3x3).</param>
        /// <param name="R2">Rotation matrix from camera 1 to camera 2 (3x3).</param>
        /// <param name="t2">Translation vector from camera 1 to camera 2 (3x1).</param>
        /// <returns>The triangulated 3D point in the first camera's coordinate system, or null if triangulation fails.</returns>
        public static Vector<double> TriangulatePoint(
            Vector<double> p1,
            Vector<double> p2,
            Matrix<double> K1,
            Matrix<double> K2,
            Matrix<double> R2,
            Vector<double> t2)
        {
            // Build camera projection matrices using column-vector convention
            // P = K * [R|t] where P is 3x4
            
            // P1 = K1 * [I|0] (first camera at origin)
            var P1 = Matrix<double>.Build.Dense(3, 4);
            P1.SetSubMatrix(0, 0, K1);  // K1 in left 3x3 block, zeros in 4th column
            
            // P2 = K2 * [R2|t2]
            var Rt2 = Matrix<double>.Build.Dense(3, 4);
            Rt2.SetSubMatrix(0, 0, R2);  // R2 in left 3x3 block
            Rt2.SetColumn(3, t2);        // t2 in 4th column
            var P2 = K2 * Rt2;

            // Build the linear system Ax=0 for DLT (Hartley & Zisserman, Eq. 12.2)
            // Each 2D point provides 2 equations: x*P[2,:] - P[0,:] and y*P[2,:] - P[1,:]
            var A = Matrix<double>.Build.Dense(4, 4);
            
            // Equations from first image point
            A.SetRow(0, p1[0] * P1.Row(2) - P1.Row(0));
            A.SetRow(1, p1[1] * P1.Row(2) - P1.Row(1));
            
            // Equations from second image point
            A.SetRow(2, p2[0] * P2.Row(2) - P2.Row(0));
            A.SetRow(3, p2[1] * P2.Row(2) - P2.Row(1));
            
            // Solve Ax=0 using SVD: X is the eigenvector corresponding to smallest singular value
            var svd = A.Svd(true);
            var singularValues = svd.S;
            
            // Check for degenerate configuration
            if (singularValues[0] < 1e-10)  // System has no unique solution
                return null;
            
            // Check condition number - reject if ill-conditioned
            double conditionNumber = singularValues[0] / singularValues[singularValues.Count - 1];
            if (conditionNumber > 1e8)
                return null;
            
            // Solution is last row of V^T (corresponding to smallest singular value)
            var X_homogeneous = svd.VT.Row(svd.VT.RowCount - 1);

            // Convert from homogeneous to 3D coordinates
            double w = X_homogeneous[3];
            if (Math.Abs(w) < 1e-10)  // Point at infinity
                return null;
            
            var X = X_homogeneous.SubVector(0, 3) / w;

            // Cheirality check: Point must be in front of first camera (positive Z)
            if (X[2] <= 1e-6)
                return null;

            // Sanity check: Reject points unreasonably far from camera
            double distSq = X[0]*X[0] + X[1]*X[1] + X[2]*X[2];
            if (distSq > 1e8)  // More than 10,000 units away
                return null;
                
            return X;
        }

        /// <summary>
        /// Helper to convert pixel coordinates to Math.NET vector
        /// </summary>
        public static Vector<double> MakePoint2D(double x, double y)
        {
            return Vector<double>.Build.DenseOfArray(new[] { x, y });
        }

        /// <summary>
        /// Helper to convert 3x3 Math.NET matrix to array
        /// </summary>
        public static double[,] ToArray3x3(Matrix<double> mat)
        {
            return new double[,]
            {
                { mat[0,0], mat[0,1], mat[0,2] },
                { mat[1,0], mat[1,1], mat[1,2] },
                { mat[2,0], mat[2,1], mat[2,2] }
            };
        }
    }
}