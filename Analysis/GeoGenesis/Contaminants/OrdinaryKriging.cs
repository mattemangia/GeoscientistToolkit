// GAIA.GeoGenesis/Contaminants/OrdinaryKriging.cs
//
// Ordinary kriging of scattered contaminant concentrations onto a regular 3-D voxel grid, using a
// fitted variogram model. For each grid node the N nearest samples are selected and the ordinary
// kriging system is solved:
//
//      | Γ  1 | | w |   | γ0 |
//      | 1ᵀ 0 | | μ | = |  1 |
//
// where Γ_ij = γ(|x_i − x_j|), γ0_i = γ(|x_i − x_node|), w are the weights and μ the Lagrange
// multiplier enforcing Σw = 1 (unbiasedness). The estimate is Σ w_i z_i and the kriging variance is
// Σ w_i γ0_i + μ. Node solves are embarrassingly parallel (Parallel.For), so large grids interpolate
// quickly. Reference: Isaaks & Srivastava (1989); Cressie (1993).

using MathNet.Numerics.LinearAlgebra;

namespace GAIA.GeoGenesis.Contaminants;

/// <summary>A kriged scalar field on a regular grid, plus the kriging variance.</summary>
public sealed class KrigingResult
{
    public int Nx { get; init; }
    public int Ny { get; init; }
    public int Nz { get; init; }
    public (double X, double Y, double Z) Origin { get; init; }   // grid node (0,0,0) world position
    public (double X, double Y, double Z) Spacing { get; init; }  // node spacing in each axis
    public float[] Values { get; init; } = Array.Empty<float>();   // flattened [x + nx*(y + ny*z)]
    public float[] Variance { get; init; } = Array.Empty<float>();
    public string Analyte { get; init; } = string.Empty;
    public double? TimeDays { get; init; }

    public int Index(int x, int y, int z) => x + Nx * (y + Ny * z);
    public (double X, double Y, double Z) NodePosition(int x, int y, int z)
        => (Origin.X + x * Spacing.X, Origin.Y + y * Spacing.Y, Origin.Z + z * Spacing.Z);
}

public static class OrdinaryKriging
{
    /// <summary>
    ///     Krige <paramref name="samples"/> onto an (nx,ny,nz) grid spanning their bounding box
    ///     (optionally padded), using up to <paramref name="maxNeighbors"/> nearest samples per node.
    /// </summary>
    public static KrigingResult Interpolate(
        IReadOnlyList<(double X, double Y, double Z, double Value)> samples,
        VariogramModel model,
        int nx, int ny, int nz,
        int maxNeighbors = 16,
        double padFraction = 0.05,
        string analyte = "",
        double? timeDays = null)
    {
        nx = Math.Max(1, nx); ny = Math.Max(1, ny); nz = Math.Max(1, nz);
        var values = new float[nx * ny * nz];
        var variance = new float[nx * ny * nz];
        if (samples.Count == 0)
            return new KrigingResult { Nx = nx, Ny = ny, Nz = nz, Values = values, Variance = variance, Analyte = analyte, TimeDays = timeDays };

        // Grid geometry from the sample bounding box, padded a little.
        double minX = samples.Min(s => s.X), maxX = samples.Max(s => s.X);
        double minY = samples.Min(s => s.Y), maxY = samples.Max(s => s.Y);
        double minZ = samples.Min(s => s.Z), maxZ = samples.Max(s => s.Z);
        Pad(ref minX, ref maxX, padFraction); Pad(ref minY, ref maxY, padFraction); Pad(ref minZ, ref maxZ, padFraction);

        var spacing = (
            X: nx > 1 ? (maxX - minX) / (nx - 1) : 1.0,
            Y: ny > 1 ? (maxY - minY) / (ny - 1) : 1.0,
            Z: nz > 1 ? (maxZ - minZ) / (nz - 1) : 1.0);
        var origin = (X: minX, Y: minY, Z: minZ);

        var result = new KrigingResult
        {
            Nx = nx, Ny = ny, Nz = nz, Origin = origin, Spacing = spacing,
            Values = values, Variance = variance, Analyte = analyte, TimeDays = timeDays
        };

        int neigh = Math.Min(maxNeighbors, samples.Count);

        Parallel.For(0, nz, z =>
        {
            for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                {
                    var (px, py, pz) = result.NodePosition(x, y, z);
                    var (est, var) = EstimateNode(px, py, pz, samples, model, neigh);
                    var idx = result.Index(x, y, z);
                    values[idx] = (float)est;
                    variance[idx] = (float)var;
                }
        });

        return result;
    }

    private static (double estimate, double variance) EstimateNode(
        double px, double py, double pz,
        IReadOnlyList<(double X, double Y, double Z, double Value)> samples,
        VariogramModel model, int neigh)
    {
        // Nearest-N neighbours by squared distance.
        var nearest = samples
            .Select((s, i) => (i, d2: Sq(s.X - px) + Sq(s.Y - py) + Sq(s.Z - pz)))
            .OrderBy(t => t.d2)
            .Take(neigh)
            .Select(t => samples[t.i])
            .ToList();
        int k = nearest.Count;
        if (k == 0) return (0, 0);

        // Exact hit on a sample → return it.
        var first = nearest[0];
        if (Sq(first.X - px) + Sq(first.Y - py) + Sq(first.Z - pz) < 1e-12)
            return (first.Value, 0.0);

        // Build the (k+1)×(k+1) ordinary-kriging system.
        var A = Matrix<double>.Build.Dense(k + 1, k + 1);
        var b = Vector<double>.Build.Dense(k + 1);
        for (int i = 0; i < k; i++)
        {
            for (int j = 0; j < k; j++)
                A[i, j] = model.Gamma(Dist(nearest[i], nearest[j]));
            A[i, k] = 1.0; A[k, i] = 1.0;
            b[i] = model.Gamma(Math.Sqrt(Sq(nearest[i].X - px) + Sq(nearest[i].Y - py) + Sq(nearest[i].Z - pz)));
        }
        A[k, k] = 0.0; b[k] = 1.0;

        Vector<double> w;
        try { w = A.Solve(b); }
        catch { return (nearest.Average(s => s.Value), double.NaN); }
        if (w.Any(double.IsNaN)) return (nearest.Average(s => s.Value), double.NaN);

        double est = 0, varc = w[k]; // start variance with the Lagrange multiplier μ
        for (int i = 0; i < k; i++) { est += w[i] * nearest[i].Value; varc += w[i] * b[i]; }
        return (est, Math.Max(0.0, varc));
    }

    private static double Dist((double X, double Y, double Z, double V) a, (double X, double Y, double Z, double V) b)
        => Math.Sqrt(Sq(a.X - b.X) + Sq(a.Y - b.Y) + Sq(a.Z - b.Z));
    private static double Sq(double v) => v * v;

    private static void Pad(ref double lo, ref double hi, double frac)
    {
        if (hi <= lo) { hi = lo + 1.0; return; }
        var pad = (hi - lo) * frac;
        lo -= pad; hi += pad;
    }
}
