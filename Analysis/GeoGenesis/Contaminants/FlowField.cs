// GAIA.GeoGenesis/Contaminants/FlowField.cs
//
// Derives a groundwater flow field (vectors for arrow glyphs) from a kriged scalar grid. With a
// hydraulic-head grid the Darcy flux is q = −(K/φ)·∇h (Darcy's law); if only a contaminant
// concentration grid is available, the negative concentration gradient is used as a proxy for the
// advective transport direction (plume moves down-gradient). Gradients use central differences and
// the vector sampling is parallelised. Reference: Bear (1972), "Dynamics of Fluids in Porous Media".

namespace GAIA.GeoGenesis.Contaminants;

public readonly record struct FlowVector(double X, double Y, double Z, double Vx, double Vy, double Vz)
{
    public double Magnitude => Math.Sqrt(Vx * Vx + Vy * Vy + Vz * Vz);
}

public static class FlowField
{
    /// <summary>
    ///     Compute flow vectors on a coarsened sub-grid of <paramref name="grid"/>. When the grid is a
    ///     hydraulic head (m), pass the conductivity/porosity scale to get a Darcy velocity; for a
    ///     concentration grid leave the scale at 1 to get the (negative) gradient direction.
    /// </summary>
    public static List<FlowVector> FromScalarGrid(KrigingResult grid, double conductivityScale = 1.0, int stride = 4)
    {
        var vectors = new List<FlowVector>();
        if (grid.Nx < 2 && grid.Ny < 2 && grid.Nz < 2) return vectors;
        stride = Math.Max(1, stride);

        var bag = new System.Collections.Concurrent.ConcurrentBag<FlowVector>();
        Parallel.For(0, grid.Nz, z =>
        {
            if (z % stride != 0) return;
            for (int y = 0; y < grid.Ny; y += stride)
                for (int x = 0; x < grid.Nx; x += stride)
                {
                    var gx = CentralDiff(grid, x, y, z, 0) / Math.Max(grid.Spacing.X, 1e-9);
                    var gy = CentralDiff(grid, x, y, z, 1) / Math.Max(grid.Spacing.Y, 1e-9);
                    var gz = CentralDiff(grid, x, y, z, 2) / Math.Max(grid.Spacing.Z, 1e-9);
                    // Flux is down-gradient: q = -K ∇h.
                    var (px, py, pz) = grid.NodePosition(x, y, z);
                    bag.Add(new FlowVector(px, py, pz, -conductivityScale * gx, -conductivityScale * gy, -conductivityScale * gz));
                }
        });
        vectors.AddRange(bag);
        return vectors;
    }

    private static double CentralDiff(KrigingResult g, int x, int y, int z, int axis)
    {
        int xm = x, xp = x, ym = y, yp = y, zm = z, zp = z;
        switch (axis)
        {
            case 0: xm = Math.Max(0, x - 1); xp = Math.Min(g.Nx - 1, x + 1); break;
            case 1: ym = Math.Max(0, y - 1); yp = Math.Min(g.Ny - 1, y + 1); break;
            default: zm = Math.Max(0, z - 1); zp = Math.Min(g.Nz - 1, z + 1); break;
        }
        var span = axis == 0 ? (xp - xm) : axis == 1 ? (yp - ym) : (zp - zm);
        if (span == 0) return 0.0;
        return (g.Values[g.Index(xp, yp, zp)] - g.Values[g.Index(xm, ym, zm)]) / span;
    }
}
