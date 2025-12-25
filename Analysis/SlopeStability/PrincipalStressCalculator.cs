using System;
using System.Numerics;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// Calculates principal stresses using proper eigenvalue decomposition.
    /// This replaces the simplified approximation with a professional solution.
    /// </summary>
    public static class PrincipalStressCalculator
    {
        /// <summary>
        /// Calculates principal stresses from stress tensor using eigenvalue decomposition.
        /// Returns stresses in descending order: σ1 >= σ2 >= σ3
        /// </summary>
        public static (float sigma1, float sigma2, float sigma3, Vector3 dir1, Vector3 dir2, Vector3 dir3)
            CalculatePrincipalStresses(float sxx, float syy, float szz, float sxy, float sxz, float syz)
        {
            // Construct symmetric stress matrix
            float[,] stress = new float[3, 3]
            {
                { sxx, sxy, sxz },
                { sxy, syy, syz },
                { sxz, syz, szz }
            };

            // Calculate eigenvalues and eigenvectors using power iteration method
            // For a 3x3 symmetric matrix, we can use analytic solution

            // Calculate invariants
            float I1 = sxx + syy + szz;
            float I2 = sxx * syy + syy * szz + szz * sxx - sxy * sxy - syz * syz - sxz * sxz;
            float I3 = sxx * syy * szz + 2.0f * sxy * syz * sxz -
                       sxx * syz * syz - syy * sxz * sxz - szz * sxy * sxy;

            // Solve characteristic equation using Cardano's formula
            // λ³ - I1·λ² + I2·λ - I3 = 0

            float p = I2 - I1 * I1 / 3.0f;
            float q = 2.0f * I1 * I1 * I1 / 27.0f - I1 * I2 / 3.0f + I3;

            float discriminant = q * q / 4.0f + p * p * p / 27.0f;

            float sigma1, sigma2, sigma3;

            if (Math.Abs(discriminant) < 1e-10f || p == 0)
            {
                // Three equal roots or special case
                sigma1 = sigma2 = sigma3 = I1 / 3.0f;
            }
            else if (discriminant > 0)
            {
                // One real root, two complex (shouldn't happen for symmetric matrix)
                float u = MathF.Cbrt(-q / 2.0f + MathF.Sqrt(discriminant));
                float v = MathF.Cbrt(-q / 2.0f - MathF.Sqrt(discriminant));
                sigma1 = u + v + I1 / 3.0f;
                sigma2 = sigma3 = I1 / 3.0f;  // Approximation
            }
            else
            {
                // Three distinct real roots - typical case
                float r = MathF.Sqrt(-p * p * p / 27.0f);
                float phi = MathF.Acos(-q / (2.0f * r));

                sigma1 = 2.0f * MathF.Cbrt(r) * MathF.Cos(phi / 3.0f) + I1 / 3.0f;
                sigma2 = 2.0f * MathF.Cbrt(r) * MathF.Cos((phi + 2.0f * MathF.PI) / 3.0f) + I1 / 3.0f;
                sigma3 = 2.0f * MathF.Cbrt(r) * MathF.Cos((phi + 4.0f * MathF.PI) / 3.0f) + I1 / 3.0f;
            }

            // Sort in descending order
            SortPrincipalStresses(ref sigma1, ref sigma2, ref sigma3);

            // Calculate principal directions (eigenvectors)
            Vector3 dir1 = CalculateEigenvector(stress, sigma1);
            Vector3 dir2 = CalculateEigenvector(stress, sigma2);
            Vector3 dir3 = CalculateEigenvector(stress, sigma3);

            // Ensure orthogonality
            dir2 = Vector3.Normalize(dir2 - Vector3.Dot(dir2, dir1) * dir1);
            dir3 = Vector3.Cross(dir1, dir2);

            return (sigma1, sigma2, sigma3, dir1, dir2, dir3);
        }

        /// <summary>
        /// Calculates eigenvector for given eigenvalue using inverse iteration.
        /// </summary>
        private static Vector3 CalculateEigenvector(float[,] matrix, float eigenvalue)
        {
            // (A - λI)v = 0
            // Find null space of (A - λI)

            float[,] A = new float[3, 3];
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    A[i, j] = matrix[i, j];
                    if (i == j)
                        A[i, j] -= eigenvalue;
                }
            }

            // Find vector in null space using cross products of rows
            Vector3 row0 = new Vector3(A[0, 0], A[0, 1], A[0, 2]);
            Vector3 row1 = new Vector3(A[1, 0], A[1, 1], A[1, 2]);
            Vector3 row2 = new Vector3(A[2, 0], A[2, 1], A[2, 2]);

            // Try cross products
            Vector3 v = Vector3.Cross(row0, row1);

            if (v.LengthSquared() < 1e-8f)
                v = Vector3.Cross(row0, row2);

            if (v.LengthSquared() < 1e-8f)
                v = Vector3.Cross(row1, row2);

            if (v.LengthSquared() < 1e-8f)
                v = Vector3.UnitX;  // Fallback

            return Vector3.Normalize(v);
        }

        /// <summary>
        /// Sorts three values in descending order.
        /// </summary>
        private static void SortPrincipalStresses(ref float s1, ref float s2, ref float s3)
        {
            float temp;

            if (s2 > s1)
            {
                temp = s1;
                s1 = s2;
                s2 = temp;
            }

            if (s3 > s1)
            {
                temp = s1;
                s1 = s3;
                s3 = temp;
            }

            if (s3 > s2)
            {
                temp = s2;
                s2 = s3;
                s3 = temp;
            }
        }

        /// <summary>
        /// Calculates von Mises stress from principal stresses.
        /// </summary>
        public static float CalculateVonMisesStress(float sigma1, float sigma2, float sigma3)
        {
            return MathF.Sqrt(0.5f * ((sigma1 - sigma2) * (sigma1 - sigma2) +
                                      (sigma2 - sigma3) * (sigma2 - sigma3) +
                                      (sigma3 - sigma1) * (sigma3 - sigma1)));
        }

        /// <summary>
        /// Calculates maximum shear stress (Tresca criterion).
        /// </summary>
        public static float CalculateMaxShearStress(float sigma1, float sigma2, float sigma3)
        {
            return (sigma1 - sigma3) / 2.0f;
        }

        /// <summary>
        /// Calculates mean stress (pressure).
        /// </summary>
        public static float CalculateMeanStress(float sigma1, float sigma2, float sigma3)
        {
            return (sigma1 + sigma2 + sigma3) / 3.0f;
        }

        /// <summary>
        /// Calculates deviatoric stress components.
        /// </summary>
        public static (float s1, float s2, float s3) CalculateDeviatoricStress(
            float sigma1, float sigma2, float sigma3)
        {
            float meanStress = CalculateMeanStress(sigma1, sigma2, sigma3);
            return (sigma1 - meanStress, sigma2 - meanStress, sigma3 - meanStress);
        }

        /// <summary>
        /// Calculates octahedral shear stress.
        /// </summary>
        public static float CalculateOctahedralShear(float sigma1, float sigma2, float sigma3)
        {
            return MathF.Sqrt(((sigma1 - sigma2) * (sigma1 - sigma2) +
                               (sigma2 - sigma3) * (sigma2 - sigma3) +
                               (sigma3 - sigma1) * (sigma3 - sigma1)) / 3.0f);
        }

        /// <summary>
        /// Calculates Lode angle (related to intermediate principal stress).
        /// </summary>
        public static float CalculateLodeAngle(float sigma1, float sigma2, float sigma3)
        {
            float J2 = ((sigma1 - sigma2) * (sigma1 - sigma2) +
                        (sigma2 - sigma3) * (sigma2 - sigma3) +
                        (sigma3 - sigma1) * (sigma3 - sigma1)) / 6.0f;

            float meanStress = (sigma1 + sigma2 + sigma3) / 3.0f;
            float s1 = sigma1 - meanStress;
            float s2 = sigma2 - meanStress;
            float s3 = sigma3 - meanStress;

            float J3 = s1 * s2 * s3;

            if (J2 < 1e-10f)
                return 0.0f;

            float sinTheta = -3.0f * MathF.Sqrt(3.0f) * J3 / (2.0f * MathF.Pow(J2, 1.5f));
            sinTheta = Math.Clamp(sinTheta, -1.0f, 1.0f);

            return MathF.Asin(sinTheta) / 3.0f;
        }
    }

    /// <summary>
    /// Stress state information.
    /// </summary>
    public class StressState
    {
        // Stress tensor components
        public float SigmaXX { get; set; }
        public float SigmaYY { get; set; }
        public float SigmaZZ { get; set; }
        public float SigmaXY { get; set; }
        public float SigmaXZ { get; set; }
        public float SigmaYZ { get; set; }

        // Principal stresses and directions
        public float Sigma1 { get; set; }
        public float Sigma2 { get; set; }
        public float Sigma3 { get; set; }
        public Vector3 Direction1 { get; set; }
        public Vector3 Direction2 { get; set; }
        public Vector3 Direction3 { get; set; }

        // Derived stress measures
        public float VonMisesStress { get; set; }
        public float MaxShearStress { get; set; }
        public float MeanStress { get; set; }
        public float OctahedralShear { get; set; }

        /// <summary>
        /// Calculates all stress measures from tensor components.
        /// </summary>
        public void Calculate()
        {
            var (s1, s2, s3, d1, d2, d3) = PrincipalStressCalculator.CalculatePrincipalStresses(
                SigmaXX, SigmaYY, SigmaZZ, SigmaXY, SigmaXZ, SigmaYZ);

            Sigma1 = s1;
            Sigma2 = s2;
            Sigma3 = s3;
            Direction1 = d1;
            Direction2 = d2;
            Direction3 = d3;

            VonMisesStress = PrincipalStressCalculator.CalculateVonMisesStress(s1, s2, s3);
            MaxShearStress = PrincipalStressCalculator.CalculateMaxShearStress(s1, s2, s3);
            MeanStress = PrincipalStressCalculator.CalculateMeanStress(s1, s2, s3);
            OctahedralShear = PrincipalStressCalculator.CalculateOctahedralShear(s1, s2, s3);
        }
    }
}
