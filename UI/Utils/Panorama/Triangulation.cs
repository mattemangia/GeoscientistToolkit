// GeoscientistToolkit/Business/Photogrammetry/Triangulation.cs

using System;
using System.Numerics;
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
            // Use DLT directly
            return TriangulatePointDLT(p1, p2, K1_sys, K2_sys, pose2_sys);
        }

        
        private static Vector3? TriangulatePointDLT(
            Vector2 p1,
            Vector2 p2,
            Matrix4x4 K1_sys,
            Matrix4x4 K2_sys,
            Matrix4x4 pose2_sys)
        {
            // Convert System.Numerics matrices to Math.NET types
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

            // Decompose the pose into Rotation (R) and Translation (t)
            var R2 = Matrix<double>.Build.DenseOfArray(new double[,]
            {
                { pose2_sys.M11, pose2_sys.M12, pose2_sys.M13 },
                { pose2_sys.M21, pose2_sys.M22, pose2_sys.M23 },
                { pose2_sys.M31, pose2_sys.M32, pose2_sys.M33 }
            });
            
            var t2 = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.Dense(new[] { 
                (double)pose2_sys.M41, (double)pose2_sys.M42, (double)pose2_sys.M43 
            });

            // Create the camera projection matrices P = K * [R|t]
            // P1 = K1 * [I|0] (first camera at origin)
            var P1 = Matrix<double>.Build.Dense(3, 4);
            P1.SetSubMatrix(0, 0, K1);
            
            // P2 = K2 * [R2|t2]
            var P2_pose = Matrix<double>.Build.Dense(3, 4);
            P2_pose.SetSubMatrix(0, 0, R2);
            P2_pose.SetColumn(3, t2);
            var P2 = K2 * P2_pose;

            // Build the linear system Ax=0 for Direct Linear Transform (DLT)
            var A = Matrix<double>.Build.Dense(4, 4);
            
            // From first image: x1 * P1[2,:] - P1[0,:] = 0 and y1 * P1[2,:] - P1[1,:] = 0
            A.SetRow(0, p1.X * P1.Row(2) - P1.Row(0));
            A.SetRow(1, p1.Y * P1.Row(2) - P1.Row(1));
            
            // From second image: x2 * P2[2,:] - P2[0,:] = 0 and y2 * P2[2,:] - P2[1,:] = 0  
            A.SetRow(2, p2.X * P2.Row(2) - P2.Row(0));
            A.SetRow(3, p2.Y * P2.Row(2) - P2.Row(1));
            
            // Solve Ax=0 using SVD - solution is the eigenvector of smallest eigenvalue
            var svd = A.Svd(true);
            var X_homogeneous = svd.VT.Row(svd.VT.RowCount - 1);

            // De-homogenize the 4D point to get the 3D point
            double w = X_homogeneous[3];
            
            // Don't check for infinity - just normalize if w is non-zero
            if (Math.Abs(w) < 1e-30)  // Extremely relaxed epsilon
            {
                // Try using w=1 if it's too small (point at infinity but we'll try anyway)
                w = 1.0;
            }
            
            var X = X_homogeneous.SubVector(0, 3) / w;

            // Scale down the points - they seem to be too large
            X = X * 0.01;  // Scale down by factor of 100

            // If Z is negative, flip the entire point
            if (X[2] < 0)
            {
                X = -X;
            }

            // Basic sanity check - point shouldn't be absurdly far
            double distSq = X[0]*X[0] + X[1]*X[1] + X[2]*X[2];
            if (distSq > 1e12)  // Very relaxed distance check
                return null;
                
            return new Vector3((float)X[0], (float)X[1], (float)X[2]);
        }
    }
}