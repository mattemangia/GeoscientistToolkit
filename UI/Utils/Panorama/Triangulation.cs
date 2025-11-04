// GeoscientistToolkit/Business/Photogrammetry/Triangulation.cs

using System;
using System.Numerics;
using GeoscientistToolkit.Business.Photogrammetry.Math;

namespace GeoscientistToolkit
{
    /// <summary>
    /// Provides triangulation methods for 3D reconstruction
    /// </summary>
    public static class Triangulation
    {
        /// <summary>
        /// Triangulates a 3D point from two image observations
        /// </summary>
        public static Vector3? TriangulatePoint(
            Vector2 p1, 
            Vector2 p2,
            Matrix4x4 K1, 
            Matrix4x4 K2,
            Matrix4x4 pose2)
        {
            var P1 = K1.As3x4();
            var P2 = (K2 * pose2).As3x4();

            // Build the linear system
            var A = new double[4, 4];
            
            for (int i = 0; i < 4; i++)
            {
                A[0, i] = p1.X * P1[2, i] - P1[0, i];
                A[1, i] = p1.Y * P1[2, i] - P1[1, i];
                A[2, i] = p2.X * P2[2, i] - P2[0, i];
                A[3, i] = p2.Y * P2[2, i] - P2[1, i];
            }

            // Solve using SVD
            var svd = new SvdDecomposition(A, false, true);
            var V = svd.V;

            double w = V[3, 3];
            if (Math.Abs(w) < 1e-8)
                return null;

            return new Vector3(
                (float)(V[0, 3] / w),
                (float)(V[1, 3] / w),
                (float)(V[2, 3] / w)
            );
        }
    }
}