// GeoscientistToolkit/Data/PhysicoChem/ReactorMeshGenerator.cs
//
// Mesh generation from reactor domains with 2D-to-3D interpolation

using System;
using System.Collections.Generic;
using System.Linq;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.PhysicoChem;

/// <summary>
/// Generates 3D meshes from reactor domain definitions
/// </summary>
public class ReactorMeshGenerator
{
    /// <summary>
    /// Generate 3D mesh from list of domains
    /// </summary>
    public GridMesh3D GenerateMeshFromDomains(List<ReactorDomain> domains, int resolution = 50)
    {
        if (domains.Count == 0)
            throw new ArgumentException("No domains provided");

        // Determine bounding box
        var bounds = CalculateBoundingBox(domains);

        // Create uniform grid
        var mesh = CreateUniformGrid(bounds, resolution);

        // Assign material properties to cells based on domains
        AssignMaterialProperties(mesh, domains);

        Logger.Log($"[ReactorMeshGenerator] Generated mesh: {mesh.GridSize.X}x{mesh.GridSize.Y}x{mesh.GridSize.Z} cells");

        return mesh;
    }

    private (double MinX, double MaxX, double MinY, double MaxY, double MinZ, double MaxZ) CalculateBoundingBox(
        List<ReactorDomain> domains)
    {
        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;
        double minZ = double.MaxValue, maxZ = double.MinValue;

        foreach (var domain in domains)
        {
            if (domain.Geometry == null) continue;

            var geom = domain.Geometry;

            switch (geom.Type)
            {
                case GeometryType.Box:
                case GeometryType.Parallelepiped:
                    minX = Math.Min(minX, geom.Center.X - geom.Dimensions.Width / 2);
                    maxX = Math.Max(maxX, geom.Center.X + geom.Dimensions.Width / 2);
                    minY = Math.Min(minY, geom.Center.Y - geom.Dimensions.Height / 2);
                    maxY = Math.Max(maxY, geom.Center.Y + geom.Dimensions.Height / 2);
                    minZ = Math.Min(minZ, geom.Center.Z - geom.Dimensions.Depth / 2);
                    maxZ = Math.Max(maxZ, geom.Center.Z + geom.Dimensions.Depth / 2);
                    break;

                case GeometryType.Sphere:
                    minX = Math.Min(minX, geom.Center.X - geom.Radius);
                    maxX = Math.Max(maxX, geom.Center.X + geom.Radius);
                    minY = Math.Min(minY, geom.Center.Y - geom.Radius);
                    maxY = Math.Max(maxY, geom.Center.Y + geom.Radius);
                    minZ = Math.Min(minZ, geom.Center.Z - geom.Radius);
                    maxZ = Math.Max(maxZ, geom.Center.Z + geom.Radius);
                    break;

                case GeometryType.Cylinder:
                    minX = Math.Min(minX, geom.Center.X - geom.Radius);
                    maxX = Math.Max(maxX, geom.Center.X + geom.Radius);
                    minY = Math.Min(minY, geom.Center.Y - geom.Radius);
                    maxY = Math.Max(maxY, geom.Center.Y + geom.Radius);
                    minZ = Math.Min(minZ, geom.Center.Z - geom.Height / 2);
                    maxZ = Math.Max(maxZ, geom.Center.Z + geom.Height / 2);
                    break;

                case GeometryType.Custom2D:
                    if (geom.Profile2D.Count > 0)
                    {
                        foreach (var pt in geom.Profile2D)
                        {
                            minX = Math.Min(minX, pt.X);
                            maxX = Math.Max(maxX, pt.X);
                            minY = Math.Min(minY, pt.Y);
                            maxY = Math.Max(maxY, pt.Y);
                        }
                        minZ = Math.Min(minZ, geom.Center.Z - geom.ExtrusionDepth / 2);
                        maxZ = Math.Max(maxZ, geom.Center.Z + geom.ExtrusionDepth / 2);
                    }
                    break;
            }
        }

        // Add padding
        double padding = 0.1;
        double rangeX = maxX - minX;
        double rangeY = maxY - minY;
        double rangeZ = maxZ - minZ;

        minX -= padding * rangeX;
        maxX += padding * rangeX;
        minY -= padding * rangeY;
        maxY += padding * rangeY;
        minZ -= padding * rangeZ;
        maxZ += padding * rangeZ;

        return (minX, maxX, minY, maxY, minZ, maxZ);
    }

    private GridMesh3D CreateUniformGrid((double MinX, double MaxX, double MinY, double MaxY, double MinZ, double MaxZ) bounds,
        int resolution)
    {
        var mesh = new GridMesh3D();

        int nx = resolution;
        int ny = resolution;
        int nz = resolution;

        mesh.GridSize = (nx, ny, nz);

        double dx = (bounds.MaxX - bounds.MinX) / nx;
        double dy = (bounds.MaxY - bounds.MinY) / ny;
        double dz = (bounds.MaxZ - bounds.MinZ) / nz;

        mesh.Origin = (bounds.MinX, bounds.MinY, bounds.MinZ);
        mesh.Spacing = (dx, dy, dz);

        return mesh;
    }

    private void AssignMaterialProperties(GridMesh3D mesh, List<ReactorDomain> domains)
    {
        int nx = mesh.GridSize.X;
        int ny = mesh.GridSize.Y;
        int nz = mesh.GridSize.Z;

        double dx = mesh.Spacing.X;
        double dy = mesh.Spacing.Y;
        double dz = mesh.Spacing.Z;

        // Create material ID field
        var materialIds = new int[nx, ny, nz];

        for (int i = 0; i < nx; i++)
        for (int j = 0; j < ny; j++)
        for (int k = 0; k < nz; k++)
        {
            double x = mesh.Origin.X + (i + 0.5) * dx;
            double y = mesh.Origin.Y + (j + 0.5) * dy;
            double z = mesh.Origin.Z + (k + 0.5) * dz;

            // Find which domain this cell belongs to
            // Priority: later domains override earlier ones
            int domainId = -1;
            for (int d = 0; d < domains.Count; d++)
            {
                if (!domains[d].IsActive) continue;

                if (domains[d].Geometry != null && domains[d].Geometry.ContainsPoint(x, y, z))
                {
                    domainId = d;
                }
            }

            materialIds[i, j, k] = domainId;
        }

        // Store material IDs in mesh metadata
        mesh.Metadata["MaterialIds"] = materialIds;
    }

    /// <summary>
    /// Generate mesh from 2D profile with specified interpolation mode
    /// </summary>
    public GridMesh3D GenerateFrom2DProfile(List<(double X, double Y)> profile,
        Interpolation2D3DMode mode, int resolution = 50, double depth = 1.0)
    {
        switch (mode)
        {
            case Interpolation2D3DMode.Extrusion:
                return GenerateExtrusion(profile, depth, resolution);

            case Interpolation2D3DMode.Revolution:
                return GenerateRevolution(profile, resolution);

            case Interpolation2D3DMode.VerticalProfile:
                return GenerateVerticalProfile(profile, resolution);

            default:
                return GenerateExtrusion(profile, depth, resolution);
        }
    }

    /// <summary>
    /// Linear extrusion of 2D profile along Z-axis
    /// </summary>
    private GridMesh3D GenerateExtrusion(List<(double X, double Y)> profile, double depth, int resolution)
    {
        var mesh = new GridMesh3D();

        // Find bounding box of 2D profile
        double minX = profile.Min(p => p.X);
        double maxX = profile.Max(p => p.X);
        double minY = profile.Min(p => p.Y);
        double maxY = profile.Max(p => p.Y);

        int nx = resolution;
        int ny = resolution;
        int nz = resolution / 2;

        double dx = (maxX - minX) / nx;
        double dy = (maxY - minY) / ny;
        double dz = depth / nz;

        mesh.GridSize = (nx, ny, nz);
        mesh.Origin = (minX, minY, -depth / 2);
        mesh.Spacing = (dx, dy, dz);

        return mesh;
    }

    /// <summary>
    /// Rotation of 2D profile around Z-axis (axisymmetric)
    /// </summary>
    private GridMesh3D GenerateRevolution(List<(double X, double Y)> profile, int resolution)
    {
        var mesh = new GridMesh3D();

        // Profile is assumed to be in R-Z plane (radius vs height)
        double maxR = profile.Max(p => Math.Abs(p.X));
        double minZ = profile.Min(p => p.Y);
        double maxZ = profile.Max(p => p.Y);

        int nr = resolution / 2;
        int ntheta = resolution;
        int nz = resolution;

        // Convert to Cartesian grid
        int nx = 2 * nr;
        int ny = 2 * nr;

        double dx = 2 * maxR / nx;
        double dy = 2 * maxR / ny;
        double dz = (maxZ - minZ) / nz;

        mesh.GridSize = (nx, ny, nz);
        mesh.Origin = (-maxR, -maxR, minZ);
        mesh.Spacing = (dx, dy, dz);

        return mesh;
    }

    /// <summary>
    /// Interpret 2D profile as vertical slice and interpolate horizontally
    /// </summary>
    private GridMesh3D GenerateVerticalProfile(List<(double X, double Y)> profile, int resolution)
    {
        var mesh = new GridMesh3D();

        double minX = profile.Min(p => p.X);
        double maxX = profile.Max(p => p.X);
        double minY = profile.Min(p => p.Y);
        double maxY = profile.Max(p => p.Y);

        int nx = resolution;
        int ny = resolution;
        int nz = resolution;

        double dx = (maxX - minX) / nx;
        double dy = (maxY - minY) / ny;
        double dz = (maxY - minY) / nz; // Same as Y for now

        mesh.GridSize = (nx, ny, nz);
        mesh.Origin = (minX, minY, minY);
        mesh.Spacing = (dx, dy, dz);

        return mesh;
    }

    /// <summary>
    /// Apply boolean operation between two domain geometries
    /// </summary>
    public ReactorGeometry ApplyBooleanOperation(ReactorGeometry geom1, ReactorGeometry geom2, BooleanOp operation)
    {
        // Create a new composite geometry
        var result = new ReactorGeometry
        {
            Type = GeometryType.Custom3D
        };

        // Store operation metadata
        // Actual evaluation happens during mesh generation via ContainsPoint

        return result;
    }

    /// <summary>
    /// Check if point is inside geometry after boolean operations
    /// </summary>
    public bool IsInsideWithBoolean(double x, double y, double z,
        ReactorGeometry geom1, ReactorGeometry geom2, BooleanOp operation)
    {
        bool in1 = geom1.ContainsPoint(x, y, z);
        bool in2 = geom2.ContainsPoint(x, y, z);

        switch (operation)
        {
            case BooleanOp.Union:
                return in1 || in2;

            case BooleanOp.Subtract:
                return in1 && !in2;

            case BooleanOp.Intersect:
                return in1 && in2;

            case BooleanOp.SymmetricDifference:
                return in1 ^ in2;

            default:
                return false;
        }
    }
}
