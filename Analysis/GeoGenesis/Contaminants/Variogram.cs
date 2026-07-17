// GAIA.GeoGenesis/Contaminants/Variogram.cs
//
// Experimental and model (theoretical) variograms for geostatistical interpolation of contaminant
// concentrations. The experimental variogram bins sample pairs by separation distance h and reports
// the semivariance γ(h) = 1/(2N(h)) Σ (z_i − z_j)². A model (spherical / exponential / Gaussian)
// with nugget, sill and range is then fitted; that model supplies γ(h) to ordinary kriging.
//
// References: Matheron (1963); Cressie (1993), "Statistics for Spatial Data", Wiley;
// Isaaks & Srivastava (1989), "An Introduction to Applied Geostatistics", OUP.

using System.Collections.Concurrent;

namespace GAIA.GeoGenesis.Contaminants;

public enum VariogramModelType { Spherical, Exponential, Gaussian }

/// <summary>A fitted theoretical variogram model: γ(h) with nugget, sill and range.</summary>
public sealed class VariogramModel
{
    public VariogramModelType Type { get; init; } = VariogramModelType.Spherical;
    public double Nugget { get; init; }
    public double Sill { get; init; } = 1.0;
    public double Range { get; init; } = 1.0;

    /// <summary>Semivariance at separation distance h.</summary>
    public double Gamma(double h)
    {
        if (h <= 0) return 0.0; // γ(0)=0 (the nugget appears as a discontinuity in the limit)
        var c = Sill - Nugget;
        var a = Range <= 0 ? 1e-9 : Range;
        return Type switch
        {
            VariogramModelType.Spherical => h >= a ? Sill : Nugget + c * (1.5 * (h / a) - 0.5 * Math.Pow(h / a, 3)),
            VariogramModelType.Exponential => Nugget + c * (1.0 - Math.Exp(-3.0 * h / a)),
            VariogramModelType.Gaussian => Nugget + c * (1.0 - Math.Exp(-3.0 * h * h / (a * a))),
            _ => Sill
        };
    }
}

public sealed class ExperimentalVariogram
{
    public double[] Lag { get; init; } = Array.Empty<double>();          // mean separation of each bin
    public double[] Semivariance { get; init; } = Array.Empty<double>(); // γ for each bin
    public int[] PairCount { get; init; } = Array.Empty<int>();
}

public static class Variogram
{
    /// <summary>
    ///     Compute the (omnidirectional) experimental variogram. Pair enumeration is parallelised
    ///     across sample indices, accumulating into per-bin partial sums.
    /// </summary>
    public static ExperimentalVariogram Experimental(
        IReadOnlyList<(double X, double Y, double Z, double Value)> pts, int nLags = 12, double? maxLag = null)
    {
        var n = pts.Count;
        var lag = new double[nLags];
        var semi = new double[nLags];
        var cnt = new int[nLags];
        if (n < 2) return new ExperimentalVariogram { Lag = lag, Semivariance = semi, PairCount = cnt };

        double maxH = maxLag ?? EstimateMaxLag(pts);
        if (maxH <= 0) return new ExperimentalVariogram { Lag = lag, Semivariance = semi, PairCount = cnt };
        double binW = maxH / nLags;

        var sumH = new double[nLags];
        var sumG = new double[nLags];
        var locks = new object[nLags];
        for (int b = 0; b < nLags; b++) locks[b] = new object();

        Parallel.For(0, n, i =>
        {
            var pi = pts[i];
            var localH = new double[nLags];
            var localG = new double[nLags];
            var localC = new int[nLags];
            for (int j = i + 1; j < n; j++)
            {
                var pj = pts[j];
                var dx = pi.X - pj.X; var dy = pi.Y - pj.Y; var dz = pi.Z - pj.Z;
                var h = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (h <= 0 || h > maxH) continue;
                int b = Math.Min(nLags - 1, (int)(h / binW));
                var dv = pi.Value - pj.Value;
                localH[b] += h; localG[b] += 0.5 * dv * dv; localC[b]++;
            }
            for (int b = 0; b < nLags; b++)
            {
                if (localC[b] == 0) continue;
                lock (locks[b]) { sumH[b] += localH[b]; sumG[b] += localG[b]; cnt[b] += localC[b]; }
            }
        });

        for (int b = 0; b < nLags; b++)
        {
            if (cnt[b] > 0) { lag[b] = sumH[b] / cnt[b]; semi[b] = sumG[b] / cnt[b]; }
            else { lag[b] = (b + 0.5) * binW; semi[b] = double.NaN; }
        }
        return new ExperimentalVariogram { Lag = lag, Semivariance = semi, PairCount = cnt };
    }

    /// <summary>
    ///     Fit a theoretical model to the experimental variogram by a coarse-to-implicit search over
    ///     (nugget, sill, range) minimising pair-count-weighted squared error. The search grid is
    ///     evaluated in parallel.
    /// </summary>
    public static VariogramModel FitModel(ExperimentalVariogram exp, VariogramModelType type)
    {
        var pts = Enumerable.Range(0, exp.Lag.Length)
            .Where(i => exp.PairCount[i] > 0 && double.IsFinite(exp.Semivariance[i]))
            .Select(i => (h: exp.Lag[i], g: exp.Semivariance[i], w: (double)exp.PairCount[i]))
            .ToList();
        if (pts.Count < 2)
            return new VariogramModel { Type = type, Nugget = 0, Sill = 1, Range = 1 };

        var maxG = pts.Max(p => p.g);
        var maxH = pts.Max(p => p.h);
        var sillGrid = Linspace(maxG * 0.5, maxG * 1.5, 12);
        var rangeGrid = Linspace(maxH * 0.2, maxH * 1.2, 16);
        var nuggetGrid = Linspace(0.0, maxG * 0.6, 8);

        var best = new ConcurrentBag<(double sse, double nug, double sill, double rng)>();
        Parallel.ForEach(sillGrid, sill =>
        {
            foreach (var rng in rangeGrid)
                foreach (var nug in nuggetGrid)
                {
                    if (nug > sill) continue;
                    var m = new VariogramModel { Type = type, Nugget = nug, Sill = sill, Range = rng };
                    double sse = 0;
                    foreach (var p in pts) { var e = m.Gamma(p.h) - p.g; sse += p.w * e * e; }
                    best.Add((sse, nug, sill, rng));
                }
        });

        var b = best.OrderBy(x => x.sse).First();
        return new VariogramModel { Type = type, Nugget = b.nug, Sill = b.sill, Range = b.rng };
    }

    private static double EstimateMaxLag(IReadOnlyList<(double X, double Y, double Z, double Value)> pts)
    {
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        foreach (var p in pts)
        {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
        }
        var diag = Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY) + (maxZ - minZ) * (maxZ - minZ));
        return diag / 2.0; // standard rule of thumb: model to half the maximum extent
    }

    private static double[] Linspace(double a, double b, int n)
    {
        if (n <= 1) return new[] { a };
        var arr = new double[n];
        for (int i = 0; i < n; i++) arr[i] = a + (b - a) * i / (n - 1);
        return arr;
    }
}
