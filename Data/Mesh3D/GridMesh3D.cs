// GeoscientistToolkit/Data/Mesh3D/GridMesh3D.cs
//
// Simple uniform grid mesh for PhysicoChem and other grid-based simulations

using System;

namespace GeoscientistToolkit.Data.Mesh3D;

/// <summary>
/// Uniform 3D grid mesh for structured simulations
/// </summary>
public class GridMesh3D
{
    public (int X, int Y, int Z) GridSize { get; set; }
    public (double X, double Y, double Z) Origin { get; set; }
    public (double X, double Y, double Z) Spacing { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();

    public GridMesh3D()
    {
    }

    public GridMesh3D(int nx, int ny, int nz)
    {
        GridSize = (nx, ny, nz);
        Origin = (0, 0, 0);
        Spacing = (1.0, 1.0, 1.0);
    }

    /// <summary>
    /// Get world coordinates of a grid cell center
    /// </summary>
    public (double X, double Y, double Z) GetCellCenter(int i, int j, int k)
    {
        double x = Origin.X + (i + 0.5) * Spacing.X;
        double y = Origin.Y + (j + 0.5) * Spacing.Y;
        double z = Origin.Z + (k + 0.5) * Spacing.Z;
        return (x, y, z);
    }

    /// <summary>
    /// Get total domain size
    /// </summary>
    public (double X, double Y, double Z) GetDomainSize()
    {
        return (GridSize.X * Spacing.X,
                GridSize.Y * Spacing.Y,
                GridSize.Z * Spacing.Z);
    }
}
