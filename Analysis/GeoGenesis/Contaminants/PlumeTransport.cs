// GAIA.GeoGenesis/Contaminants/PlumeTransport.cs
//
// Animates a contaminant plume by advecting/dispersing an initial concentration grid under a
// user-defined regional groundwater flow (a velocity vector, e.g. "toward the sea"). Solves the
// advection–dispersion equation
//
//      ∂C/∂t = −v·∇C + ∇·(D ∇C)
//
// with first-order upwind advection and central-difference dispersion on the kriged voxel grid.
// Each output frame is a full grid, so the UI can play them as a time animation. Internal
// substepping keeps the explicit scheme stable (CFL); cell updates are parallelised.
//
// Reference: Bear (1972); Zheng & Bennett (2002), "Applied Contaminant Transport Modeling", Wiley.

namespace GAIA.GeoGenesis.Contaminants;

/// <summary>A regional groundwater flow velocity (m/day) plus longitudinal dispersivity (m).</summary>
public readonly record struct RegionalFlow(double Vx, double Vy, double Vz, double Dispersivity_m)
{
    /// <summary>Build a horizontal flow from a compass azimuth (deg from North, clockwise) and speed (m/day).</summary>
    public static RegionalFlow FromAzimuth(double azimuthDeg, double speed_m_day, double dispersivity_m)
    {
        var rad = azimuthDeg * Math.PI / 180.0;
        // Azimuth 0 = +Y (north), 90 = +X (east).
        return new RegionalFlow(speed_m_day * Math.Sin(rad), speed_m_day * Math.Cos(rad), 0.0, dispersivity_m);
    }

    public double Speed => Math.Sqrt(Vx * Vx + Vy * Vy + Vz * Vz);
}

public static class PlumeTransport
{
    /// <summary>
    ///     Advect/disperse <paramref name="initial"/> under <paramref name="flow"/> and return
    ///     <paramref name="frames"/> grids spaced <paramref name="frameDt_days"/> apart (frame 0 is
    ///     the initial state). The grid geometry (origin/spacing/size) is preserved.
    /// </summary>
    public static List<KrigingResult> Animate(KrigingResult initial, RegionalFlow flow, int frames, double frameDt_days,
        SorptionModel? sorption = null)
    {
        var result = new List<KrigingResult>(Math.Max(1, frames));
        int nx = initial.Nx, ny = initial.Ny, nz = initial.Nz;
        var c = (float[])initial.Values.Clone();
        result.Add(Snapshot(initial, c, 0));
        if (frames <= 1 || frameDt_days <= 0) return result;

        double dx = Math.Max(initial.Spacing.X, 1e-9);
        double dy = Math.Max(initial.Spacing.Y, 1e-9);
        double dz = nz > 1 ? Math.Max(initial.Spacing.Z, 1e-9) : double.PositiveInfinity;

        // Dispersion coefficients D = αL·|v| (+ small molecular term) per axis.
        double speed = flow.Speed;
        double D = flow.Dispersivity_m * speed + 1e-4;

        // Stable explicit time step from the CFL (advection) and diffusion-number conditions.
        double dtAdv = 0.5 / (Math.Abs(flow.Vx) / dx + Math.Abs(flow.Vy) / dy + (double.IsInfinity(dz) ? 0 : Math.Abs(flow.Vz) / dz) + 1e-12);
        double dtDif = 0.2 / (D * (1 / (dx * dx) + 1 / (dy * dy) + (double.IsInfinity(dz) ? 0 : 1 / (dz * dz))) + 1e-12);
        double dt = Math.Min(dtAdv, dtDif);

        var next = new float[c.Length];
        for (int f = 1; f < frames; f++)
        {
            double elapsed = 0;
            while (elapsed < frameDt_days)
            {
                double step = Math.Min(dt, frameDt_days - elapsed);
                Step(c, next, nx, ny, nz, dx, dy, dz, flow, D, step, sorption);
                (c, next) = (next, c);
                elapsed += step;
            }
            result.Add(Snapshot(initial, c, f * frameDt_days));
        }
        return result;
    }

    private static void Step(float[] c, float[] outc, int nx, int ny, int nz,
        double dx, double dy, double dz, RegionalFlow flow, double D, double dt, SorptionModel? sorption)
    {
        int Idx(int x, int y, int z) => x + nx * (y + ny * z);
        Parallel.For(0, nz, z =>
        {
            for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                {
                    int i = Idx(x, y, z);
                    double ci = c[i];

                    // Conservative donor-cell (first-order upwind) advection in flux-divergence form,
                    // with open boundaries (outside concentration = 0). This conserves mass: solute
                    // only leaves at the outflow face, and nothing is injected at the inflow edge.
                    double advDiv = 0;
                    advDiv += FluxDiv(c, Idx, x, nx, y, ny, z, nz, 0, flow.Vx) / dx;
                    advDiv += FluxDiv(c, Idx, x, nx, y, ny, z, nz, 1, flow.Vy) / dy;
                    if (nz > 1) advDiv += FluxDiv(c, Idx, x, nx, y, ny, z, nz, 2, flow.Vz) / dz;

                    // Central-difference dispersion (Laplacian, zero-flux walls).
                    double lap = Lap(c, Idx, x, nx, dx, y, ny, dy, z, nz, dz);

                    // Soil sorption retards transport: the effective transport rate is divided by the
                    // local retardation factor R = 1 + (ρ_b/θ)·dq/dC (mass is held on the matrix).
                    double r = sorption == null ? 1.0 : sorption.RetardationFactor(ci);
                    outc[i] = (float)Math.Max(0.0, ci + dt / r * (-advDiv + D * lap));
                }
        });
    }

    // Donor-cell advective flux divergence (F_right − F_left) along an axis, open boundaries (0 outside).
    private static double FluxDiv(float[] c, Func<int, int, int, int> Idx,
        int x, int nx, int y, int ny, int z, int nz, int axis, double v)
    {
        if (v == 0) return 0;
        double ci = c[Idx(x, y, z)];
        int hi = axis == 0 ? nx : axis == 1 ? ny : nz;
        int p = axis == 0 ? x : axis == 1 ? y : z;
        double cPlus = p + 1 < hi ? c[Idx(axis == 0 ? x + 1 : x, axis == 1 ? y + 1 : y, axis == 2 ? z + 1 : z)] : 0.0; // outside = 0
        double cMinus = p - 1 >= 0 ? c[Idx(axis == 0 ? x - 1 : x, axis == 1 ? y - 1 : y, axis == 2 ? z - 1 : z)] : 0.0;
        // Donor cell for each face depends on flow direction.
        double fRight = v >= 0 ? v * ci : v * cPlus;
        double fLeft = v >= 0 ? v * cMinus : v * ci;
        return fRight - fLeft;
    }

    private static double Lap(float[] c, Func<int, int, int, int> Idx,
        int x, int nx, double dx, int y, int ny, double dy, int z, int nz, double dz)
    {
        int i = Idx(x, y, z);
        double lap = 0;
        lap += (c[Idx(Math.Min(nx - 1, x + 1), y, z)] - 2 * c[i] + c[Idx(Math.Max(0, x - 1), y, z)]) / (dx * dx);
        lap += (c[Idx(x, Math.Min(ny - 1, y + 1), z)] - 2 * c[i] + c[Idx(x, Math.Max(0, y - 1), z)]) / (dy * dy);
        if (nz > 1)
            lap += (c[Idx(x, y, Math.Min(nz - 1, z + 1))] - 2 * c[i] + c[Idx(x, y, Math.Max(0, z - 1))]) / (dz * dz);
        return lap;
    }

    private static KrigingResult Snapshot(KrigingResult geom, float[] c, double timeDays) => new()
    {
        Nx = geom.Nx, Ny = geom.Ny, Nz = geom.Nz, Origin = geom.Origin, Spacing = geom.Spacing,
        Values = (float[])c.Clone(), Variance = Array.Empty<float>(), Analyte = geom.Analyte, TimeDays = timeDays
    };
}
