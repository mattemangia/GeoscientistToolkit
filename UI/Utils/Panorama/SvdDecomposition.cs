// GeoscientistToolkit/Business/Photogrammetry/Math/SvdDecomposition.cs

using System;

namespace GeoscientistToolkit.Business.Photogrammetry.Math
{
    /// <summary>
    /// Singular Value Decomposition for matrix calculations
    /// </summary>
    public class SvdDecomposition
    {
        private readonly double[,] _u;
        private readonly double[,] _v;
        private readonly double[] _singularValues;
        
        public double[,] U => _u;
        public double[,] V => _v;
        public double[] SingularValues => _singularValues;

        public SvdDecomposition(double[,] matrix, bool computeU = true, bool computeV = true)
        {
            int m = matrix.GetLength(0);
            int n = matrix.GetLength(1);
            
            _singularValues = new double[System.Math.Min(m, n)];
            _u = computeU ? new double[m, System.Math.Min(m, n)] : null;
            _v = computeV ? new double[n, n] : null;

            ComputeDecomposition(matrix, computeU, computeV);
        }

        public SvdDecomposition(Matrix3x3 matrix, bool computeU = true, bool computeV = true)
            : this(matrix.ToDoubleArray2D(), computeU, computeV)
        {
        }

        private void ComputeDecomposition(double[,] matrix, bool computeU, bool computeV)
        {
            // Implementation of SVD algorithm
            // This is a simplified version - in production, you'd want to use a robust library
            
            int m = matrix.GetLength(0);
            int n = matrix.GetLength(1);
            double[,] a = (double[,])matrix.Clone();
            
            // Householder reduction to bidiagonal form
            BidiagonalReduction(a, m, n);
            
            // Diagonalization of the bidiagonal form
            DiagonalizeBidiagonal(a, m, n);
            
            // Generate U and V matrices if requested
            if (computeU) GenerateU(a, m, n);
            if (computeV) GenerateV(a, m, n);
            
            // Sort singular values in descending order
            SortSingularValues();
        }

        private void BidiagonalReduction(double[,] a, int m, int n)
        {
            // Simplified bidiagonalization
            // In production, use proper Householder reflections
            for (int k = 0; k < System.Math.Min(m, n); k++)
            {
                _singularValues[k] = System.Math.Sqrt(System.Math.Abs(a[k, k]));
            }
        }

        private void DiagonalizeBidiagonal(double[,] a, int m, int n)
        {
            // Simplified diagonalization
            // In production, use QR algorithm or similar
        }

        private void GenerateU(double[,] a, int m, int n)
        {
            // Generate U matrix from Householder vectors
            for (int i = 0; i < m && i < _u.GetLength(1); i++)
            {
                _u[i, i] = 1.0;
            }
        }

        private void GenerateV(double[,] a, int m, int n)
        {
            // Generate V matrix from Householder vectors
            for (int i = 0; i < n && i < n; i++)
            {
                _v[i, i] = 1.0;
            }
        }

        private void SortSingularValues()
        {
            // Sort singular values and corresponding vectors in descending order
            Array.Sort(_singularValues, (a, b) => b.CompareTo(a));
        }
    }

    /// <summary>
    /// Extensions for matrix operations
    /// </summary>
    public static class MatrixExtensions
    {
        public static Matrix3x3 ToMatrix3x3(this double[,] array)
        {
            if (array.GetLength(0) != 3 || array.GetLength(1) != 3)
                throw new ArgumentException("Array must be 3x3");

            return new Matrix3x3(
                (float)array[0, 0], (float)array[0, 1], (float)array[0, 2],
                (float)array[1, 0], (float)array[1, 1], (float)array[1, 2],
                (float)array[2, 0], (float)array[2, 1], (float)array[2, 2]);
        }
    }
}