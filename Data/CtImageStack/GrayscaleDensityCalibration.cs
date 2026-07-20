// GAIA/Data/CtImageStack/GrayscaleDensityCalibration.cs
//
// A grayscale -> density calibration for a CT volume.
//
// The user assigns a few known densities to grayscale levels (by clicking ROIs and
// picking a material from the library, e.g. Air and Quartz). From those anchor points
// this builds a continuous mapping rho(gray) so EVERY voxel in the volume gets a density,
// not only the segmented ones. Two points give a straight line; three or more give a
// quadratic least-squares fit (which passes exactly through three points).
//
// Density is stored in kg/m^3 (the SI unit used by the physical simulations). Material.Density
// elsewhere in the CT model is g/cm^3 - callers converting to that must divide by 1000.

namespace GAIA.Data.CtImageStack;

public sealed class GrayscaleDensityCalibration
{
    public enum FitKind
    {
        None,
        Constant,
        Linear,
        Quadratic
    }

    // rho(g) = A*g^2 + B*g + C   (kg/m^3, g in [0,255])
    public double A { get; set; }
    public double B { get; set; }
    public double C { get; set; }

    public FitKind Kind { get; set; } = FitKind.None;
    public double RSquared { get; set; }
    public DateTime CalibratedUtc { get; set; }

    public List<CalibrationPoint> Points { get; set; } = new();

    public bool IsValid => Kind != FitKind.None;

    /// <summary>Density (kg/m^3) for a given grayscale value, clamped to a physical floor.</summary>
    public float EvaluateKgM3(double gray)
    {
        var g = Math.Clamp(gray, 0.0, 255.0);
        var rho = A * g * g + B * g + C;
        return (float)Math.Max(1.0, rho); // never below ~vacuum
    }

    /// <summary>Human-readable fit equation for the UI.</summary>
    public string EquationText()
    {
        return Kind switch
        {
            FitKind.Constant => $"rho = {C:F1} kg/m3 (constant)",
            FitKind.Linear => $"rho = {B:F4}*g + {C:F2} kg/m3",
            FitKind.Quadratic => $"rho = {A:F6}*g^2 + {B:F4}*g + {C:F2} kg/m3",
            _ => "no calibration"
        };
    }

    /// <summary>
    ///     Fits the mapping from the current <see cref="Points" />. Chooses the model from the number of
    ///     distinct grayscale anchors: 1 -> constant, 2 -> linear, 3+ -> quadratic least squares. Falls
    ///     back to a simpler model if the higher one is singular (e.g. collinear/duplicate grays).
    /// </summary>
    public void Fit()
    {
        A = B = C = 0;
        RSquared = 0;
        Kind = FitKind.None;

        var pts = Points?.Where(p => p != null).ToList() ?? new List<CalibrationPoint>();
        if (pts.Count == 0) return;

        var distinctGrays = pts.Select(p => Math.Round(p.Gray, 3)).Distinct().Count();

        if (pts.Count == 1 || distinctGrays == 1)
        {
            C = pts.Average(p => p.Density_kg_m3);
            Kind = FitKind.Constant;
            RSquared = 1.0;
            CalibratedUtc = DateTime.UtcNow;
            return;
        }

        if (distinctGrays >= 3 && TryFitQuadratic(pts))
        {
            Kind = FitKind.Quadratic;
        }
        else
        {
            FitLinear(pts);
            Kind = FitKind.Linear;
        }

        RSquared = ComputeRSquared(pts);
        CalibratedUtc = DateTime.UtcNow;
    }

    private void FitLinear(List<CalibrationPoint> pts)
    {
        double n = pts.Count;
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        foreach (var p in pts)
        {
            sx += p.Gray;
            sy += p.Density_kg_m3;
            sxx += (double)p.Gray * p.Gray;
            sxy += (double)p.Gray * p.Density_kg_m3;
        }

        var denom = n * sxx - sx * sx;
        A = 0;
        if (Math.Abs(denom) < 1e-9)
        {
            B = 0;
            C = sy / n;
            return;
        }

        B = (n * sxy - sx * sy) / denom;
        C = (sy - B * sx) / n;
    }

    private bool TryFitQuadratic(List<CalibrationPoint> pts)
    {
        // Normal equations for y = a*g^2 + b*g + c, unknowns ordered [c, b, a].
        double s0 = pts.Count, s1 = 0, s2 = 0, s3 = 0, s4 = 0;
        double t0 = 0, t1 = 0, t2 = 0;
        foreach (var p in pts)
        {
            double g = p.Gray;
            double g2 = g * g;
            double y = p.Density_kg_m3;
            s1 += g;
            s2 += g2;
            s3 += g2 * g;
            s4 += g2 * g2;
            t0 += y;
            t1 += g * y;
            t2 += g2 * y;
        }

        // | s0 s1 s2 | | c |   | t0 |
        // | s1 s2 s3 | | b | = | t1 |
        // | s2 s3 s4 | | a |   | t2 |
        var m = new[,]
        {
            { s0, s1, s2 },
            { s1, s2, s3 },
            { s2, s3, s4 }
        };
        var rhs = new[] { t0, t1, t2 };

        if (!Solve3x3(m, rhs, out var sol)) return false;

        C = sol[0];
        B = sol[1];
        A = sol[2];
        return true;
    }

    private static bool Solve3x3(double[,] m, double[] rhs, out double[] x)
    {
        x = new double[3];
        // Gaussian elimination with partial pivoting.
        var a = (double[,])m.Clone();
        var b = (double[])rhs.Clone();

        for (var col = 0; col < 3; col++)
        {
            var pivot = col;
            for (var r = col + 1; r < 3; r++)
                if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col]))
                    pivot = r;

            if (Math.Abs(a[pivot, col]) < 1e-12) return false;

            if (pivot != col)
            {
                for (var c = 0; c < 3; c++) (a[col, c], a[pivot, c]) = (a[pivot, c], a[col, c]);
                (b[col], b[pivot]) = (b[pivot], b[col]);
            }

            for (var r = col + 1; r < 3; r++)
            {
                var f = a[r, col] / a[col, col];
                for (var c = col; c < 3; c++) a[r, c] -= f * a[col, c];
                b[r] -= f * b[col];
            }
        }

        for (var row = 2; row >= 0; row--)
        {
            var sum = b[row];
            for (var c = row + 1; c < 3; c++) sum -= a[row, c] * x[c];
            x[row] = sum / a[row, row];
        }

        return true;
    }

    private double ComputeRSquared(List<CalibrationPoint> pts)
    {
        var meanY = pts.Average(p => (double)p.Density_kg_m3);
        double ssRes = 0, ssTot = 0;
        foreach (var p in pts)
        {
            var pred = A * p.Gray * p.Gray + B * p.Gray + C;
            var res = p.Density_kg_m3 - pred;
            var tot = p.Density_kg_m3 - meanY;
            ssRes += res * res;
            ssTot += tot * tot;
        }

        if (ssTot < 1e-12) return 1.0;
        return 1.0 - ssRes / ssTot;
    }

    public sealed class CalibrationPoint
    {
        public float Gray { get; set; } // mean grayscale of the anchor ROI/material, 0..255
        public float Density_kg_m3 { get; set; } // known density assigned to that grayscale
        public string MaterialName { get; set; } // optional: library material the density came from
    }
}
