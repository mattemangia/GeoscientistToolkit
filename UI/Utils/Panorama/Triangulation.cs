// GeoscientistToolkit/Business/Photogrammetry/Triangulation.cs

using System;
using System.Numerics;
// Add using statements for MathNet
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace GeoscientistToolkit
{
    /// <summary>
    /// Provides triangulation methods for 3D reconstruction
    /// </summary>
    public static class Triangulation
    {
        /// <summary>
        /// Triangulates a 3D point from two image observations using a linear DLT method.
        /// </summary>
        /// <param name="p1">Observed 2D point in the first image.</param>
        /// <param name="p2">Observed 2D point in the second image.</param>
        /// <param name="K1_sys">Intrinsic matrix of the first camera (System.Numerics).</param>
        /// <param name="K2_sys">Intrinsic matrix of the second camera (System.Numerics).</param>
        /// <param name="pose2_sys">Pose of the second camera relative to the first (System.Numerics).</param>
        /// <returns>The triangulated 3D point in the first camera's coordinate system, or null if triangulation fails.</returns>
        public static Vector3? TriangulatePoint(
            Vector2 p1,
            Vector2 p2,
            Matrix4x4 K1_sys,
            Matrix4x4 K2_sys,
            Matrix4x4 pose2_sys)
        {
            // --- START OF FIX ---
            // Convert System.Numerics matrices to Math.NET types for robust linear algebra.
            var K1 = Matrix<double>.Build.DenseOfArray(new double[,]
            {
                { K1_sys.M11, K1_sys.M12, K1_sys.M13 },
                { K1_sys.M21, K1_sys.M22, K1_sys.M23 },
                { K1_sys.M31, K1_sys.M32, K1_sys.M33 }
            });

            var K2 = Matrix<double>.Build.DenseOfArray(new double[,]
            {
                { K2_sys.M11, K2_sys.M12, K2_sys.M13 },
                { K2_sys.M21, K2_sys.M22, K2_sys.M23 },
                { K2_sys.M31, K2_sys.M32, K2_sys.M33 }
            });

            // Decompose the pose into Rotation (R) and Translation (t).
            // In System.Numerics.Matrix4x4, translation is stored in the 4th row.
            var R2 = Matrix<double>.Build.DenseOfArray(new double[,]
            {
                { pose2_sys.M11, pose2_sys.M12, pose2_sys.M13 },
                { pose2_sys.M21, pose2_sys.M22, pose2_sys.M23 },
                { pose2_sys.M31, pose2_sys.M32, pose2_sys.M33 }
            });
            var t2 = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.Dense(new[] { (double)pose2_sys.M41, (double)pose2_sys.M42, (double)pose2_sys.M43 });

            // Create the camera projection matrices P = K * [R|t].

            // P1 = K1 * [I|0] (Pose of the first camera is identity).
            var P1 = Matrix<double>.Build.Dense(3, 4);
            P1.SetSubMatrix(0, 0, K1); // The last column is implicitly zeros.

            // P2 = K2 * [R2|t2].
            var P2_pose = Matrix<double>.Build.Dense(3, 4);
            P2_pose.SetSubMatrix(0, 0, R2);
            P2_pose.SetColumn(3, t2);
            var P2 = K2 * P2_pose;
            // --- END OF FIX ---


            // Build the linear system Ax=0 for Direct Linear Transform (DLT).
            var A = Matrix<double>.Build.Dense(4, 4);
            A.SetRow(0, p1.X * P1.Row(2) - P1.Row(0));
            A.SetRow(1, p1.Y * P1.Row(2) - P1.Row(1));
            A.SetRow(2, p2.X * P2.Row(2) - P2.Row(0));
            A.SetRow(3, p2.Y * P2.Row(2) - P2.Row(1));
            
            // Solve Ax=0 using Singular Value Decomposition (SVD).
            // The solution is the vector corresponding to the smallest singular value,
            // which is the last column of V, or equivalently, the last row of V-transpose.
            var svd = A.Svd(true);
            MathNet.Numerics.LinearAlgebra.Vector<double> X_homogeneous = svd.VT.Row(svd.VT.RowCount - 1);

            // De-homogenize the 4D point to get the 3D point.
            double w = X_homogeneous[3];
            if (Math.Abs(w) < 1e-8)
            {
                return null; // Point is at infinity, triangulation failed.
            }
            
            var X = X_homogeneous.SubVector(0, 3) / w;

            // Return the result as a System.Numerics.Vector3.
            return new Vector3((float)X[0], (float)X[1], (float)X[2]);
        }
    }
}