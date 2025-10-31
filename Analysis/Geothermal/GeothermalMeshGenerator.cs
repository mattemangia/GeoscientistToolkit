// GeoscientistToolkit/Analysis/Geothermal/GeothermalMeshGenerator.cs

using System.Numerics;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.Mesh3D;
using System.IO;

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
        // Validate input
        if (borehole == null)
            throw new ArgumentNullException(nameof(borehole));
        
        if (borehole.TotalDepth <= 0)
            throw new ArgumentException("Borehole depth must be positive", nameof(borehole));
        
        if (borehole.Diameter <= 0)
            throw new ArgumentException("Borehole diameter must be positive", nameof(borehole));
        
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
        
        // Ensure valid radius values
        if (rMin <= 0) rMin = 0.05f; // Default 50mm radius if invalid
        if (rMax <= rMin) rMax = Math.Max(50f, rMin * 1000); // Ensure domain is large enough
        
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
        var layerDepths = new List<float>();
        
        // Only add layer depths if lithology exists
        if (borehole.Lithology != null && borehole.Lithology.Any())
        {
            layerDepths = borehole.Lithology
                .SelectMany(l => new[] { (float)l.DepthFrom, (float)l.DepthTo })
                .Where(d => d >= minDepth && d <= maxDepth)
                .Distinct()
                .OrderBy(d => d)
                .ToList();
        }
        
        mesh.Z = GenerateRefinedVerticalGrid((float)minDepth, (float)maxDepth, nz, layerDepths);
        
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
                    
                    mesh.CellVolumes[i, j, k] = r * dr * dtheta * Math.Abs(dz);
                    
                    // Determine geological layer at this depth
                    var depth = mesh.Z[k];
                    LithologyUnit layer = null;
                    
                    if (borehole.Lithology != null && borehole.Lithology.Any())
                    {
                        layer = borehole.Lithology.FirstOrDefault(l => depth >= l.DepthFrom && depth <= l.DepthTo);
                    }
                    
                    if (layer != null)
                    {
                        // Assign material properties based on layer
                        mesh.MaterialIds[i, j, k] = (byte)(borehole.Lithology.IndexOf(layer) + 1);
                        
                        var layerName = layer.RockType ?? "Unknown";

                        // 1. Thermal Conductivity (W/m·K)
                        if (layer.Parameters.TryGetValue("Thermal Conductivity", out var specificConductivity))
                        {
                            mesh.ThermalConductivities[i, j, k] = specificConductivity;
                        }
                        else
                        {
                            mesh.ThermalConductivities[i, j, k] = (float)options.LayerThermalConductivities.GetValueOrDefault(layerName, 2.5);
                        }

                        // 2. Specific Heat (J/kg·K)
                        if (layer.Parameters.TryGetValue("Specific Heat", out var specificHeat))
                        {
                            mesh.SpecificHeats[i, j, k] = specificHeat;
                        }
                        else
                        {
                            mesh.SpecificHeats[i, j, k] = (float)options.LayerSpecificHeats.GetValueOrDefault(layerName, 900);
                        }

                        // 3. Density (kg/m³)
                        if (layer.Parameters.TryGetValue("Density", out var specificDensity))
                        {
                            mesh.Densities[i, j, k] = specificDensity;
                        }
                        else
                        {
                            mesh.Densities[i, j, k] = (float)options.LayerDensities.GetValueOrDefault(layerName, 2650);
                        }

                        // 4. Porosity (fraction, 0-1)
                        if (layer.Parameters.TryGetValue("Porosity", out var specificPorosity))
                        {
                            // BoreholeDataset stores porosity in %, simulation needs a fraction
                            mesh.Porosities[i, j, k] = specificPorosity / 100.0f;
                        }
                        else
                        {
                            mesh.Porosities[i, j, k] = (float)options.LayerPorosities.GetValueOrDefault(layerName, 0.1);
                        }

                        // 5. Permeability (m²)
                        if (layer.Parameters.TryGetValue("Permeability", out var specificPermeability))
                        {
                            mesh.Permeabilities[i, j, k] = specificPermeability;
                        }
                        else
                        {
                            mesh.Permeabilities[i, j, k] = (float)options.LayerPermeabilities.GetValueOrDefault(layerName, 1e-14);
                        }
                    }
                    else
                    {
                        // Use default material properties for unknown zones
                        mesh.MaterialIds[i, j, k] = 0;
                        mesh.ThermalConductivities[i, j, k] = 2.5f;    // Default granite-like
                        mesh.SpecificHeats[i, j, k] = 900f;
                        mesh.Densities[i, j, k] = 2650f;
                        mesh.Porosities[i, j, k] = 0.05f;
                        mesh.Permeabilities[i, j, k] = 1e-15f;
                    }
                }
            }
        }
        
        // Calculate face areas and transmissivities
        CalculateFaceAreas(mesh);
        CalculateTransmissivities(mesh);
        
        // Generate fracture network if enabled
        if (options.SimulateFractures)
        {
            GenerateFractureNetwork(mesh, borehole, options);
        }
        
        // Generate borehole geometry
        mesh.BoreholeElements = GenerateBoreholeElements(borehole, mesh, options);
        
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
                refinementZones.Add((depth, 2.0f)); // 2m refinement zone
            }
        }
        
        // Generate base uniform grid
        for (int i = 0; i < nz; i++)
        {
            var t = (float)i / (nz - 1);
            z[i] = minZ + (maxZ - minZ) * t;
        }
        
        // Apply refinement if there are zones defined
        if (refinementZones.Any())
        {
            // Apply local refinement around each zone
            for (int iter = 0; iter < 3; iter++) // Multiple iterations for smoothing
            {
                var newZ = new float[nz];
                newZ[0] = z[0];
                newZ[nz - 1] = z[nz - 1];
                
                for (int i = 1; i < nz - 1; i++)
                {
                    var pos = z[i];
                    var refinementFactor = 1.0f;
                    
                    // Check proximity to refinement zones
                    foreach (var (center, width) in refinementZones)
                    {
                        var dist = Math.Abs(pos - center);
                        if (dist < width)
                        {
                            var localFactor = 0.25f + 0.75f * (dist / width);
                            refinementFactor = Math.Min(refinementFactor, localFactor);
                        }
                    }
                    
                    // Adjust spacing based on refinement
                    var dz1 = z[i] - z[i - 1];
                    var dz2 = z[i + 1] - z[i];
                    var targetDz = (dz1 + dz2) * 0.5f * refinementFactor;
                    
                    newZ[i] = 0.5f * (z[i - 1] + z[i + 1]);
                }
                
                z = newZ;
            }
        }
        
        return z;
    }
    
    /// <summary>
    /// Calculates face areas for finite volume discretization.
    /// </summary>
    private static void CalculateFaceAreas(GeothermalMesh mesh)
    {
        var nr = mesh.RadialPoints;
        var nth = mesh.AngularPoints;
        var nz = mesh.VerticalPoints;
        
        mesh.RadialFaceAreas = new float[nr + 1, nth, nz];
        mesh.AngularFaceAreas = new float[nr, nth + 1, nz];
        mesh.VerticalFaceAreas = new float[nr, nth, nz + 1];
        
        // Radial face areas
        for (int i = 0; i <= nr; i++)
        {
            var r = (i == 0) ? mesh.R[0] * 0.5f : 
                    (i == nr) ? mesh.R[nr - 1] * 1.5f : 
                    0.5f * (mesh.R[i - 1] + mesh.R[i]);
            
            for (int j = 0; j < nth; j++)
            {
                var dtheta = 2f * MathF.PI / nth;
                
                for (int k = 0; k < nz; k++)
                {
                    var dz = (k < nz - 1) ? mesh.Z[k + 1] - mesh.Z[k] : mesh.Z[k] - mesh.Z[k - 1];
                    mesh.RadialFaceAreas[i, j, k] = r * dtheta * Math.Abs(dz);
                }
            }
        }
        
        // Angular face areas
        for (int i = 0; i < nr; i++)
        {
            var dr = (i < nr - 1) ? mesh.R[i + 1] - mesh.R[i] : mesh.R[i] - mesh.R[i - 1];
            
            for (int k = 0; k < nz; k++)
            {
                var dz = (k < nz - 1) ? mesh.Z[k + 1] - mesh.Z[k] : mesh.Z[k] - mesh.Z[k - 1];
                
                for (int j = 0; j <= nth; j++)
                {
                    mesh.AngularFaceAreas[i, j, k] = dr * Math.Abs(dz);
                }
            }
        }
        
        // Vertical face areas
        for (int i = 0; i < nr; i++)
        {
            var r = mesh.R[i];
            var dr = (i < nr - 1) ? mesh.R[i + 1] - mesh.R[i] : mesh.R[i] - mesh.R[i - 1];
            
            for (int j = 0; j < nth; j++)
            {
                var dtheta = 2f * MathF.PI / nth;
                var area = r * dr * dtheta;
                
                for (int k = 0; k <= nz; k++)
                {
                    mesh.VerticalFaceAreas[i, j, k] = area;
                }
            }
        }
    }
    
    /// <summary>
    /// Calculates thermal transmissivities between cells.
    /// </summary>
    private static void CalculateTransmissivities(GeothermalMesh mesh)
    {
        var nr = mesh.RadialPoints;
        var nth = mesh.AngularPoints;
        var nz = mesh.VerticalPoints;
        
        mesh.RadialTransmissivities = new float[nr + 1, nth, nz];
        mesh.AngularTransmissivities = new float[nr, nth + 1, nz];
        mesh.VerticalTransmissivities = new float[nr, nth, nz + 1];
        
        // Radial transmissivities
        for (int i = 1; i < nr; i++)
        {
            for (int j = 0; j < nth; j++)
            {
                for (int k = 0; k < nz; k++)
                {
                    var k1 = mesh.ThermalConductivities[i - 1, j, k];
                    var k2 = mesh.ThermalConductivities[i, j, k];
                    var dr1 = (i > 1) ? mesh.R[i - 1] - mesh.R[i - 2] : mesh.R[1] - mesh.R[0];
                    var dr2 = mesh.R[i] - mesh.R[i - 1];
                    
                    // Harmonic mean
                    var keff = 2 * k1 * k2 / (k1 * dr2 + k2 * dr1);
                    mesh.RadialTransmissivities[i, j, k] = keff * mesh.RadialFaceAreas[i, j, k] / ((dr1 + dr2) * 0.5f);
                }
            }
        }
        
        // Angular transmissivities (periodic boundary)
        for (int i = 0; i < nr; i++)
        {
            var r = mesh.R[i];
            var dtheta = 2f * MathF.PI / nth;
            
            for (int j = 0; j <= nth; j++)
            {
                for (int k = 0; k < nz; k++)
                {
                    var j1 = (j - 1 + nth) % nth;
                    var j2 = j % nth;
                    
                    var k1 = mesh.ThermalConductivities[i, j1, k];
                    var k2 = mesh.ThermalConductivities[i, j2, k];
                    
                    var keff = 2 * k1 * k2 / (k1 + k2);
                    mesh.AngularTransmissivities[i, j, k] = keff * mesh.AngularFaceAreas[i, j, k] / (r * dtheta);
                }
            }
        }
        
        // Vertical transmissivities
        for (int i = 0; i < nr; i++)
        {
            for (int j = 0; j < nth; j++)
            {
                for (int k = 1; k < nz; k++)
                {
                    var k1 = mesh.ThermalConductivities[i, j, k - 1];
                    var k2 = mesh.ThermalConductivities[i, j, k];
                    var dz1 = (k > 1) ? Math.Abs(mesh.Z[k - 1] - mesh.Z[k - 2]) : Math.Abs(mesh.Z[1] - mesh.Z[0]);
                    var dz2 = Math.Abs(mesh.Z[k] - mesh.Z[k - 1]);
                    
                    var keff = 2 * k1 * k2 / (k1 * dz2 + k2 * dz1);
                    mesh.VerticalTransmissivities[i, j, k] = keff * mesh.VerticalFaceAreas[i, j, k] / ((dz1 + dz2) * 0.5f);
                }
            }
        }
    }
    
    /// <summary>
    /// Generates a stochastic fracture network if enabled.
    /// </summary>
    private static void GenerateFractureNetwork(GeothermalMesh mesh, BoreholeDataset borehole, GeothermalSimulationOptions options)
    {
        // Simple fracture network generation
        var random = new Random(42); // Fixed seed for reproducibility
        var fractures = new List<FractureElement>();
        
        // Generate major fractures based on lithology
        foreach (var unit in borehole.Lithology)
        {
            if (unit.RockType != null && (unit.RockType.Contains("Granite") || unit.RockType.Contains("Basalt")))
            {
                // Higher fracture density in crystalline rocks
                var numFractures = random.Next(5, 15);
                
                for (int i = 0; i < numFractures; i++)
                {
                    var fracture = new FractureElement
                    {
                        Position = new Vector3(
                            (float)(random.NextDouble() * options.DomainRadius),
                            (float)(random.NextDouble() * 2 * Math.PI),
                            (float)(unit.DepthFrom + random.NextDouble() * (unit.DepthTo - unit.DepthFrom))
                        ),
                        Normal = Vector3.Normalize(new Vector3(
                            (float)(random.NextDouble() - 0.5),
                            (float)(random.NextDouble() - 0.5),
                            (float)(random.NextDouble() - 0.5)
                        )),
                        Length = (float)(10 + random.NextDouble() * 50),
                        Aperture = (float)options.FractureAperture,
                        Permeability = (float)options.FracturePermeability
                    };
                    
                    fractures.Add(fracture);
                }
            }
        }
        
        mesh.Fractures = fractures;
    }
    
    /// <summary>
    /// Generates borehole element connectivity for heat exchanger.
    /// </summary>
    private static List<BoreholeElement> GenerateBoreholeElements(
        BoreholeDataset borehole, 
        GeothermalMesh mesh, 
        GeothermalSimulationOptions options)
    {
        var elements = new List<BoreholeElement>();
        var nz = 20; // Number of elements along borehole
        
        for (int i = 0; i < nz; i++)
        {
            var z = i * borehole.TotalDepth / (nz - 1);
            
            // Find mesh cell containing this borehole element
            int kMesh = 0;
            for (int k = 0; k < mesh.VerticalPoints - 1; k++)
            {
                if (z >= -mesh.Z[k] && z <= -mesh.Z[k + 1])
                {
                    kMesh = k;
                    break;
                }
            }
            
            var element = new BoreholeElement
            {
                Depth = (float)z,
                MeshIndices = new[] { 0, 0, kMesh }, // Center of cylindrical mesh
                FlowArea = (float)(Math.PI * Math.Pow(options.PipeInnerDiameter / 2, 2)),
                WetPerimeter = (float)(Math.PI * options.PipeInnerDiameter),
                Length = (float)(borehole.TotalDepth / (nz - 1))
            };
            
            // Calculate local heat transfer properties
            if (options.HeatExchangerType == HeatExchangerType.UTube)
            {
                element.HeatTransferArea = 2 * element.WetPerimeter * element.Length;
            }
            else // Coaxial
            {
                element.HeatTransferArea = element.WetPerimeter * element.Length;
            }
            
            elements.Add(element);
        }
        
        return elements;
    }
    
    /// <summary>
    /// Creates a 3D mesh for visualizing the borehole.
    /// </summary>
    public static Mesh3DDataset CreateBoreholeMesh(BoreholeDataset borehole, GeothermalSimulationOptions options)
    {
        var vertices = new List<Vector3>();
        var faces = new List<int[]>();
        
        int angularSegments = 24;
        int verticalSegments = 50;
        
        var radius = (float)(borehole.Diameter / 2.0);
        var totalDepth = (float)borehole.TotalDepth;
        
        // Create vertices
        for (int i = 0; i <= verticalSegments; i++)
        {
            var z = -i * totalDepth / verticalSegments;
            for (int j = 0; j < angularSegments; j++)
            {
                var angle = j * 2.0f * MathF.PI / angularSegments;
                var x = radius * MathF.Cos(angle);
                var y = radius * MathF.Sin(angle);
                vertices.Add(new Vector3(x, y, z));
            }
        }

        // Create faces
        for (int i = 0; i < verticalSegments; i++)
        {
            for (int j = 0; j < angularSegments; j++)
            {
                int nextJ = (j + 1) % angularSegments;

                int v0 = i * angularSegments + j;
                int v1 = i * angularSegments + nextJ;
                int v2 = (i + 1) * angularSegments + j;
                int v3 = (i + 1) * angularSegments + nextJ;

                faces.Add(new[] { v0, v2, v1 });
                faces.Add(new[] { v1, v2, v3 });
            }
        }
        
        string tempPath = Path.Combine(Path.GetTempPath(), $"{borehole.Name}_borehole_mesh.obj");
        
        return Mesh3DDataset.CreateFromData(
            $"{borehole.Name}_Borehole",
            tempPath,
            vertices,
            faces,
            1.0f,
            "m"
        );
    }
}

/// <summary>
/// Represents a fracture element in the mesh.
/// </summary>
public class FractureElement
{
    public Vector3 Position { get; set; }
    public Vector3 Normal { get; set; }
    public float Length { get; set; }
    public float Aperture { get; set; }
    public float Permeability { get; set; }
    public List<int[]> ConnectedCells { get; set; } = new();
}

/// <summary>
/// Represents a borehole heat exchanger element.
/// </summary>
public class BoreholeElement
{
    public float Depth { get; set; }
    public int[] MeshIndices { get; set; } // i, j, k indices in mesh
    public float FlowArea { get; set; }
    public float WetPerimeter { get; set; }
    public float HeatTransferArea { get; set; }
    public float Length { get; set; }
}

/// <summary>
/// Specialized mesh structure for geothermal simulations.
/// </summary>
public class GeothermalMesh
{
    // Grid dimensions
    public int RadialPoints { get; set; }
    public int AngularPoints { get; set; }
    public int VerticalPoints { get; set; }
    
    // Coordinates
    public float[] R { get; set; }          // Radial coordinates
    public float[] Theta { get; set; }      // Angular coordinates
    public float[] Z { get; set; }          // Vertical coordinates (negative = down)
    
    // Cell properties
    public float[,,] CellVolumes { get; set; }
    public byte[,,] MaterialIds { get; set; }
    public float[,,] ThermalConductivities { get; set; }
    public float[,,] SpecificHeats { get; set; }
    public float[,,] Densities { get; set; }
    public float[,,] Porosities { get; set; }
    public float[,,] Permeabilities { get; set; }
    
    // Face areas for finite volume
    public float[,,] RadialFaceAreas { get; set; }
    public float[,,] AngularFaceAreas { get; set; }
    public float[,,] VerticalFaceAreas { get; set; }
    
    // Transmissivities
    public float[,,] RadialTransmissivities { get; set; }
    public float[,,] AngularTransmissivities { get; set; }
    public float[,,] VerticalTransmissivities { get; set; }
    
    // Special elements
    public List<FractureElement> Fractures { get; set; }
    public List<BoreholeElement> BoreholeElements { get; set; }
    
    /// <summary>
    /// Gets the total number of cells in the mesh.
    /// </summary>
    public int TotalCells => RadialPoints * AngularPoints * VerticalPoints;
    
    /// <summary>
    /// Converts mesh indices to Cartesian coordinates.
    /// </summary>
    public Vector3 GetCartesianPosition(int i, int j, int k)
    {
        var r = R[i];
        var theta = Theta[j];
        var z = Z[k];
        
        return new Vector3(
            r * MathF.Cos(theta),
            r * MathF.Sin(theta),
            z
        );
    }
}
