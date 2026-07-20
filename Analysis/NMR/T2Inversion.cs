// GAIA/Analysis/NMR/T2Inversion.cs

using GAIA.Util;

namespace GAIA.Analysis.NMR;

/// <summary>
///     Shared T2 spectrum inversion used by both the CPU and GPU simulation backends,
///     so the same decay curve always produces the same spectrum regardless of backend.
///     Solves the Fredholm problem M(t) = Σ A_j · exp(-t/T2_j) as a Tikhonov-regularized
///     least-squares system with a non-negativity constraint (projected Gauss-Seidel).
/// </summary>
public static class T2Inversion
{
    /// <summary>
    ///     Inverts a magnetization decay into a normalized T2 amplitude spectrum.
    ///     Bins are logarithmically spaced between t2MinMs and t2MaxMs.
    /// </summary>
    /// <returns>(bins in ms, normalized amplitudes)</returns>
    public static (double[] bins, double[] amplitudes) Invert(
        double[] timePointsMs,
        double[] magnetization,
        double t2MinMs,
        double t2MaxMs,
        int binCount,
        double lambda = 0.01)
    {
        var logMin = Math.Log10(t2MinMs);
        var logMax = Math.Log10(t2MaxMs);
        var logStep = (logMax - logMin) / binCount;

        var bins = new double[binCount];
        for (var i = 0; i < binCount; i++)
            bins[i] = Math.Pow(10, logMin + i * logStep);

        // Components with T2 much longer than the observed window are indistinguishable
        // from a constant offset; warn so the user extends the simulation instead of
        // trusting artifacts in the long-T2 tail.
        var totalTimeMs = timePointsMs.Length > 0 ? timePointsMs[^1] : 0;
        if (totalTimeMs > 0 && t2MaxMs > totalTimeMs)
            Logger.LogWarning(
                $"[T2Inversion] Simulated decay spans {totalTimeMs:F1} ms but the T2 range extends to {t2MaxMs:F0} ms. " +
                "Components longer than the simulated time cannot be resolved; increase steps or time step.");

        var kernel = BuildKernelMatrix(timePointsMs, bins);
        var amplitudes = SolveRegularizedNonNegative(kernel, magnetization, lambda);

        var sum = amplitudes.Sum();
        if (sum > 0)
            for (var i = 0; i < amplitudes.Length; i++)
                amplitudes[i] /= sum;

        return (bins, amplitudes);
    }

    private static double[,] BuildKernelMatrix(double[] timePoints, double[] t2Values)
    {
        var matrix = new double[timePoints.Length, t2Values.Length];

        for (var i = 0; i < timePoints.Length; i++)
        for (var j = 0; j < t2Values.Length; j++)
            matrix[i, j] = Math.Exp(-timePoints[i] / t2Values[j]);

        return matrix;
    }

    /// <summary>
    ///     Solves (KᵀK + λ·diag(KᵀK)) A = Kᵀm with A ≥ 0 via projected Gauss-Seidel.
    /// </summary>
    private static double[] SolveRegularizedNonNegative(double[,] kernel, double[] data, double lambda)
    {
        var m = kernel.GetLength(0);
        var n = kernel.GetLength(1);

        var ktk = new double[n, n];
        var ktd = new double[n];

        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                double sum = 0;
                for (var k = 0; k < m; k++) sum += kernel[k, i] * kernel[k, j];
                ktk[i, j] = sum;
                if (i == j) ktk[i, j] *= 1.0 + lambda;
            }

            for (var k = 0; k < m; k++) ktd[i] += kernel[k, i] * data[k];
        }

        var x = new double[n];
        var previous = new double[n];

        for (var iter = 0; iter < 200; iter++)
        {
            Array.Copy(x, previous, n);

            for (var i = 0; i < n; i++)
            {
                var sum = ktd[i];
                for (var j = 0; j < n; j++)
                    if (j != i)
                        sum -= ktk[i, j] * x[j];

                x[i] = Math.Max(0, sum / Math.Max(ktk[i, i], 1e-10));
            }

            var maxDelta = 0.0;
            for (var i = 0; i < n; i++)
                maxDelta = Math.Max(maxDelta, Math.Abs(x[i] - previous[i]));
            if (maxDelta < 1e-12) break;
        }

        return x;
    }
}
