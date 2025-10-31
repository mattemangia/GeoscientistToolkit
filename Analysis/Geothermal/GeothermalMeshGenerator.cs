// GeoscientistToolkit/Analysis/Geothermal/GeothermalMeshGenerator.cs

using System.Numerics;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.Mesh3D;

namespace GeoscientistToolkit.Analysis.Geothermal;

/// <summary>
/// Generates 3D computational meshes from borehole data for geothermal simulations.
/// </summary>
public static class GeothermalMeshGenerator
{
    /// <summary>
    /// Creates a cylindrical mesh around a borehole for simulation.
    /// </summary>
    public static GeothermalMesh GenerateCylindricalMesh(BoreholeDataset borehole, GeothermalSimulationOptions options)
    {
        var mesh = new GeothermalMesh();
        
        // Calculate domain bounds
        var minDepth = -options.DomainExtension;
        var maxDepth = borehole.TotalDepth + options.DomainExtension;
        var totalHeight = maxDepth - minDepth;
        
        // Create grid dimensions
        var nr = options.RadialGridPoints;
        var ntheta = options.AngularGridPoints;
        var nz = options.VerticalGridPoints;
        
        mesh.RadialPoints = nr;
        mesh.AngularPoints = ntheta;
        mesh.VerticalPoints = nz;
        
        // Create coordinate arrays
        mesh.R = new float[nr];
        mesh.Theta = new float[ntheta];
        mesh.Z = new float[nz];
        
        // Generate radial coordinates (logarithmic spacing near borehole)
        var rMin = (float)(borehole.Diameter / 2000.0); // Convert mm to m
        var rMax = (float)options.DomainRadius;
        
        for (int i = 0; i < nr; i++)
        {
            if (i < 10) // Fine spacing near borehole
            {
                var t = (float)i / 9f;
                mesh.R[i] = rMin + (1f - rMin) * t;
            }
            else
            {
                var t = (float)(i - 9) / (nr - 10);
                var logMin = MathF.Log(1f);
                var logMax = MathF.Log(rMax);
                mesh.R[i] = MathF.Exp(logMin + (logMax - logMin) * t);
            }
        }
        
        // Generate angular coordinates (uniform)
        for (int j = 0; j < ntheta; j++)
        {
            mesh.Theta[j] = (float)(2.0 * Math.PI * j / ntheta);
        }
        
        // Generate vertical coordinates (refined near layers)
        var layerDepths = borehole.Lithology
            .Select(l => l.DepthFrom)
            .Concat(borehole.Lithology.Select(l => l.DepthTo))
            .Distinct()
            .OrderBy(d => d)
            .ToList();
        
        mesh.Z = GenerateRefinedVerticalGrid(minDepth, maxDepth, nz, layerDepths);
        
        // Initialize property arrays
        var totalCells = nr * ntheta * nz;
        mesh.CellVolumes = new float[nr, ntheta, nz];
        mesh.MaterialIds = new byte[nr, ntheta, nz];
        mesh.ThermalConductivities = new float[nr, ntheta, nz];
        mesh.SpecificHeats = new float[nr, ntheta, nz];
        mesh.Densities = new float[nr, ntheta, nz];
        mesh.Porosities = new float[nr, ntheta, nz];
        mesh.Permeabilities = new float[nr, ntheta, nz];
        
        // Calculate cell volumes and assign properties
        for (int i = 0; i < nr; i++)
        {
            for (int j = 0; j < ntheta; j++)
            {
                for (int k = 0; k < nz; k++)
                {
                    // Cell volume calculation
                    var r = mesh.R[i];
                    var dr = (i < nr - 1) ? mesh.R[i + 1] - mesh.R[i] : mesh.R[i] - mesh.R[i - 1];
                    var dtheta = 2f * MathF.PI / ntheta;
                    var dz = (k < nz - 1) ? mesh.Z[k + 1] - mesh.Z[k] : mesh.Z[k] - mesh.Z[k - 1];
                    
                    mesh.CellVolumes[i, j, k] = r * dr * dtheta * dz;
                    
                    // Determine geological layer at this depth
                    var depth = mesh.Z[k];
                    var layer = borehole.Lithology.FirstOrDefault(l => depth >= l.DepthFrom && depth <= l.DepthTo);
                    
                    if (layer != null)
                    {
                        // Assign material properties based on layer
                        mesh.MaterialIds[i, j, k] = (byte)(borehole.Lithology.IndexOf(layer) + 1);
                        
                        var layerName = layer.RockType ?? "Unknown";
                        mesh.ThermalConductivities[i, j, k] = (float)options.LayerThermalConductivities.GetValueOrDefault(layerName, 2.5);
                        mesh.SpecificHeats[i, j, k] = (float)options.LayerSpecificHeats.GetValueOrDefault(layerName, 900);
                        mesh.Densities[i, j, k] = (float)options.LayerDensities.GetValueOrDefault(layerName, 2650);
                        mesh.Porosities[i, j, k] = (float)options.LayerPorosities.GetValueOrDefault(layerName, 0.1);
                        mesh.Permeabilities[i, j, k] = (float)options.LayerPermeabilities.GetValueOrDefault(layerName, 1e-14);
                    }
                    else
                    {
                        // Default properties outside borehole range
                        mesh.MaterialIds[i, j, k] = 0;
                        mesh.ThermalConductivities[i, j, k] = 2.5f;
                        mesh.SpecificHeats[i, j, k] = 900f;
                        mesh.Densities[i, j, k] = 2650f;
                        mesh.Porosities[i, j, k] = 0.1f;
                        mesh.Permeabilities[i, j, k] = 1e-14f;
                    }
                    
                    // Mark heat exchanger region
                    if (r <= rMin * 1.1f && depth >= 0 && depth <= borehole.TotalDepth)
                    {
                        mesh.MaterialIds[i, j, k] = 255; // Special ID for heat exchanger
                    }
                }
            }
        }
        
        // Create fracture network if enabled
        if (options.SimulateFractures && borehole.Fractures != null && borehole.Fractures.Any())
        {
            mesh.FractureNetwork = GenerateFractureNetwork(borehole, mesh, options);
        }
        
        return mesh;
    }
    
    /// <summary>
    /// Generates a refined vertical grid with higher resolution near layer boundaries.
    /// </summary>
    private static float[] GenerateRefinedVerticalGrid(float minZ, float maxZ, int nz, List<float> layerDepths)
    {
        var z = new float[nz];
        var refinementZones = new List<(float center, float width)>();
        
        // Add refinement zones around each layer boundary
        foreach (var depth in layerDepths)
        {
            if (depth > minZ && depth < maxZ)
            {
                refinementZones.Add((depth, 2f)); // 2m refinement zone
            }
        }
        
        // Generate points with refinement
        for (int i = 0; i < nz; i++)
        {
            var t = (float)i / (nz - 1);
            var zUniform = minZ + (maxZ - minZ) * t;
            
            // Apply refinement function
            var zRefined = zUniform;
            foreach (var (center, width) in refinementZones)
            {
                var dist = Math.Abs(zUniform - center);
                if (dist < width)
                {
                    var pull = 0.5f * (1f - dist / width);
                    zRefined = zRefined * (1f - pull) + center * pull;
                }
            }
            
            z[i] = zRefined;
        }
        
        // Sort to ensure monotonic increasing
        Array.Sort(z);
        
        return z;
    }
    
    /// <summary>
    /// Generates fracture network representation.
    /// </summary>
    private static FractureNetwork GenerateFractureNetwork(BoreholeDataset borehole, GeothermalMesh mesh, GeothermalSimulationOptions options)
    {
        var network = new FractureNetwork();
        
        foreach (var fracture in borehole.Fractures)
        {
            var frac = new Fracture3D
            {
                Depth = fracture.Depth,
                Strike = fracture.Strike ?? 0,
                Dip = fracture.Dip ?? 45,
                Aperture = (float)options.FractureAperture,
                Permeability = (float)options.FracturePermeability,
                Length = 10f // Assume 10m fracture length
            };
            
            // Calculate fracture plane in 3D
            var strikeRad = frac.Strike * MathF.PI / 180f;
            var dipRad = frac.Dip * MathF.PI / 180f;
            
            frac.Normal = new Vector3(
                MathF.Sin(strikeRad) * MathF.Sin(dipRad),
                MathF.Cos(strikeRad) * MathF.Sin(dipRad),
                MathF.Cos(dipRad)
            );
            
            frac.Origin = new Vector3(0, 0, frac.Depth);
            
            network.Fractures.Add(frac);
        }
        
        // Build connectivity between fractures
        for (int i = 0; i < network.Fractures.Count; i++)
        {
            for (int j = i + 1; j < network.Fractures.Count; j++)
            {
                var f1 = network.Fractures[i];
                var f2 = network.Fractures[j];
                
                // Check if fractures intersect (simplified)
                if (Math.Abs(f1.Depth - f2.Depth) < f1.Length + f2.Length)
                {
                    network.Connections.Add((i, j));
                }
            }
        }
        
        return network;
    }
    
    /// <summary>
    /// Creates a 3D mesh representation of the borehole for visualization.
    /// </summary>
    public static Mesh3DDataset CreateBoreholeMesh(BoreholeDataset borehole, GeothermalSimulationOptions options)
    {
        var vertices = new List<Vector3>();
        var faces = new List<int[]>();
        
        var radius = (float)(borehole.Diameter / 2000.0); // mm to m
        var segments = 16; // Angular resolution
        
        // Generate vertices along borehole
        var depths = new List<float> { 0 };
        foreach (var layer in borehole.Lithology)
        {
            depths.Add(layer.DepthFrom);
            depths.Add(layer.DepthTo);
        }
        depths.Add(borehole.TotalDepth);
        depths = depths.Distinct().OrderBy(d => d).ToList();
        
        foreach (var depth in depths)
        {
            for (int i = 0; i < segments; i++)
            {
                var angle = 2f * MathF.PI * i / segments;
                var x = radius * MathF.Cos(angle);
                var y = radius * MathF.Sin(angle);
                vertices.Add(new Vector3(x, y, -depth)); // Negative for display
            }
        }
        
        // Create faces
        for (int d = 0; d < depths.Count - 1; d++)
        {
            var offset1 = d * segments;
            var offset2 = (d + 1) * segments;
            
            for (int i = 0; i < segments; i++)
            {
                var i1 = i;
                var i2 = (i + 1) % segments;
                
                // Two triangles per quad
                faces.Add(new[] { offset1 + i1, offset2 + i1, offset2 + i2 });
                faces.Add(new[] { offset1 + i1, offset2 + i2, offset1 + i2 });
            }
        }
        
        // Add heat exchanger pipes
        if (options.HeatExchangerType == HeatExchangerType.UTube)
        {
            AddUTubeMesh(vertices, faces, borehole, options);
        }
        else if (options.HeatExchangerType == HeatExchangerType.Coaxial)
        {
            AddCoaxialMesh(vertices, faces, borehole, options);
        }
        
        var mesh = Mesh3DDataset.CreateFromData(
            "Borehole_HeatExchanger",
            Path.Combine(Path.GetTempPath(), "borehole_mesh.obj"),
            vertices,
            faces,
            1.0f,
            "m"
        );
        
        return mesh;
    }
    
    private static void AddUTubeMesh(List<Vector3> vertices, List<int[]> faces, BoreholeDataset borehole, GeothermalSimulationOptions options)
    {
        var pipeRadius = (float)(options.PipeOuterDiameter / 2);
        var spacing = (float)(options.PipeSpacing / 2);
        var segments = 8;
        var verticalSegments = 20;
        
        var baseVertexCount = vertices.Count;
        
        // Generate two pipes
        for (int pipe = 0; pipe < 2; pipe++)
        {
            var xOffset = pipe == 0 ? -spacing : spacing;
            
            for (int v = 0; v <= verticalSegments; v++)
            {
                var depth = borehole.TotalDepth * v / verticalSegments;
                
                for (int s = 0; s < segments; s++)
                {
                    var angle = 2f * MathF.PI * s / segments;
                    var x = xOffset + pipeRadius * MathF.Cos(angle);
                    var y = pipeRadius * MathF.Sin(angle);
                    vertices.Add(new Vector3(x, y, -depth));
                }
            }
            
            // Create pipe faces
            for (int v = 0; v < verticalSegments; v++)
            {
                var offset1 = baseVertexCount + pipe * (verticalSegments + 1) * segments + v * segments;
                var offset2 = offset1 + segments;
                
                for (int s = 0; s < segments; s++)
                {
                    var s1 = s;
                    var s2 = (s + 1) % segments;
                    
                    faces.Add(new[] { offset1 + s1, offset2 + s1, offset2 + s2 });
                    faces.Add(new[] { offset1 + s1, offset2 + s2, offset1 + s2 });
                }
            }
        }
        
        // Add U-bend at bottom
        var bendVertexStart = vertices.Count;
        var bendSegments = 10;
        
        for (int b = 0; b <= bendSegments; b++)
        {
            var t = (float)b / bendSegments;
            var angle = MathF.PI * t;
            var x = spacing * MathF.Cos(angle);
            var z = -borehole.TotalDepth - spacing * MathF.Sin(angle);
            
            for (int s = 0; s < segments; s++)
            {
                var ringAngle = 2f * MathF.PI * s / segments;
                var rx = x + pipeRadius * MathF.Cos(ringAngle) * MathF.Sin(angle);
                var ry = pipeRadius * MathF.Sin(ringAngle);
                vertices.Add(new Vector3(rx, ry, z));
            }
        }
        
        // Create bend faces
        for (int b = 0; b < bendSegments; b++)
        {
            var offset1 = bendVertexStart + b * segments;
            var offset2 = offset1 + segments;
            
            for (int s = 0; s < segments; s++)
            {
                var s1 = s;
                var s2 = (s + 1) % segments;
                
                faces.Add(new[] { offset1 + s1, offset2 + s1, offset2 + s2 });
                faces.Add(new[] { offset1 + s1, offset2 + s2, offset1 + s2 });
            }
        }
    }
    
    private static void AddCoaxialMesh(List<Vector3> vertices, List<int[]> faces, BoreholeDataset borehole, GeothermalSimulationOptions options)
    {
        var innerRadius = (float)(options.PipeOuterDiameter / 2);
        var outerRadius = (float)(options.PipeSpacing / 2); // Outer pipe inner radius
        var segments = 12;
        var verticalSegments = 20;
        
        var baseVertexCount = vertices.Count;
        
        // Generate inner and outer pipes
        for (int v = 0; v <= verticalSegments; v++)
        {
            var depth = borehole.TotalDepth * v / verticalSegments;
            
            // Inner pipe
            for (int s = 0; s < segments; s++)
            {
                var angle = 2f * MathF.PI * s / segments;
                var x = innerRadius * MathF.Cos(angle);
                var y = innerRadius * MathF.Sin(angle);
                vertices.Add(new Vector3(x, y, -depth));
            }
            
            // Outer pipe
            for (int s = 0; s < segments; s++)
            {
                var angle = 2f * MathF.PI * s / segments;
                var x = outerRadius * MathF.Cos(angle);
                var y = outerRadius * MathF.Sin(angle);
                vertices.Add(new Vector3(x, y, -depth));
            }
        }
        
        // Create faces for both pipes
        for (int v = 0; v < verticalSegments; v++)
        {
            // Inner pipe
            var innerOffset1 = baseVertexCount + v * segments * 2;
            var innerOffset2 = innerOffset1 + segments * 2;
            
            // Outer pipe
            var outerOffset1 = innerOffset1 + segments;
            var outerOffset2 = innerOffset2 + segments;
            
            for (int s = 0; s < segments; s++)
            {
                var s1 = s;
                var s2 = (s + 1) % segments;
                
                // Inner pipe faces
                faces.Add(new[] { innerOffset1 + s1, innerOffset2 + s1, innerOffset2 + s2 });
                faces.Add(new[] { innerOffset1 + s1, innerOffset2 + s2, innerOffset1 + s2 });
                
                // Outer pipe faces
                faces.Add(new[] { outerOffset1 + s1, outerOffset2 + s1, outerOffset2 + s2 });
                faces.Add(new[] { outerOffset1 + s1, outerOffset2 + s2, outerOffset1 + s2 });
            }
        }
    }
}

/// <summary>
/// Represents a 3D cylindrical mesh for geothermal simulation.
/// </summary>
public class GeothermalMesh
{
    public int RadialPoints { get; set; }
    public int AngularPoints { get; set; }
    public int VerticalPoints { get; set; }
    
    public float[] R { get; set; }
    public float[] Theta { get; set; }
    public float[] Z { get; set; }
    
    public float[,,] CellVolumes { get; set; }
    public byte[,,] MaterialIds { get; set; }
    public float[,,] ThermalConductivities { get; set; }
    public float[,,] SpecificHeats { get; set; }
    public float[,,] Densities { get; set; }
    public float[,,] Porosities { get; set; }
    public float[,,] Permeabilities { get; set; }
    
    public FractureNetwork FractureNetwork { get; set; }
}

/// <summary>
/// Represents a network of fractures in the rock.
/// </summary>
public class FractureNetwork
{
    public List<Fracture3D> Fractures { get; set; } = new();
    public List<(int, int)> Connections { get; set; } = new();
}

/// <summary>
/// Represents a single fracture in 3D space.
/// </summary>
public class Fracture3D
{
    public float Depth { get; set; }
    public float Strike { get; set; }
    public float Dip { get; set; }
    public float Aperture { get; set; }
    public float Permeability { get; set; }
    public float Length { get; set; }
    public Vector3 Origin { get; set; }
    public Vector3 Normal { get; set; }
}