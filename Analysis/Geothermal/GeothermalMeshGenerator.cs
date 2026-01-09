// GeoscientistToolkit/Analysis/Geothermal/GeothermalMeshGenerator.cs

using System.Numerics;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.Mesh3D;

namespace GeoscientistToolkit.Analysis.Geothermal;

/// <summary>
///     Generates 3D computational meshes from borehole data for geothermal simulations.
/// </summary>
public static class GeothermalMeshGenerator
{
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
        // CORRECTED: Z coordinates are negative going down
        // Surface is at Z=0, bottom is at Z=-depth
        var minDepth = -options.DomainExtension; // Above surface
        var maxDepth = borehole.TotalDepth + options.DomainExtension; // Below bottom

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
        var rMin = (float)(borehole.Diameter / 2.0); // Borehole diameter is already in meters
        var rMax = (float)options.DomainRadius;

        // Ensure valid radius values
        if (rMin <= 0) rMin = 0.05f; // Default 50mm radius if invalid
        if (rMax <= rMin) rMax = Math.Max(50f, rMin * 1000); // Ensure domain is large enough

        for (var i = 0; i < nr; i++)
            if (i < 10) // Fine spacing near borehole
            {
                var t = i / 9f;
                mesh.R[i] = rMin + (1f - rMin) * t;
            }
            else
            {
                var t = (float)(i - 9) / (nr - 10);
                var logMin = MathF.Log(1f);
                var logMax = MathF.Log(rMax);
                mesh.R[i] = MathF.Exp(logMin + (logMax - logMin) * t);
            }

        // Generate angular coordinates (uniform)
        for (var j = 0; j < ntheta; j++) mesh.Theta[j] = (float)(2.0 * Math.PI * j / ntheta);

        // CORRECTED: Generate vertical coordinates
        // Z goes from positive (above surface) to negative (below surface)
        var layerDepths = new List<float>();

        // Only add layer depths if lithology exists
        if (borehole.Lithology != null && borehole.Lithology.Any())
            layerDepths = borehole.Lithology
                .SelectMany(l => new[] { l.DepthFrom, l.DepthTo })
                .Where(d => d >= 0 && d <= maxDepth)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

        // Generate Z coordinates from top to bottom
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
        for (var i = 0; i < nr; i++)
        for (var j = 0; j < ntheta; j++)
        for (var k = 0; k < nz; k++)
        {
            // Cell volume calculation
            var r = mesh.R[i];
            var dr = i < nr - 1 ? mesh.R[i + 1] - mesh.R[i] : mesh.R[i] - mesh.R[i - 1];
            var dtheta = 2f * MathF.PI / ntheta;
            var dz = k < nz - 1 ? Math.Abs(mesh.Z[k + 1] - mesh.Z[k]) : Math.Abs(mesh.Z[k] - mesh.Z[k - 1]);

            mesh.CellVolumes[i, j, k] = r * dr * dtheta * dz;

            // CORRECTED: Determine geological layer at this depth
            // Z is negative going down, so depth = -Z
            var depth = -mesh.Z[k];
            LithologyUnit layer = null;

            if (borehole.Lithology != null && borehole.Lithology.Any() && depth >= 0)
                layer = borehole.Lithology.FirstOrDefault(l => depth >= l.DepthFrom && depth <= l.DepthTo);

            // Mark cells within the borehole diameter as heat exchanger
            var distanceFromAxis = mesh.R[i];
            var boreholeRadius = (float)(borehole.Diameter / 2000.0); // Convert mm to m

            // --- DEFINITIVE FIX START ---
            // The Material ID for the borehole must extend to the FULL borehole depth.
            // This ensures the borehole's physical presence (filled with grout) is modeled correctly,
            // allowing for passive heat conduction below the active exchanger.
            if (distanceFromAxis <= boreholeRadius * 1.5f && depth >= 0 && depth <= borehole.TotalDepth)
                mesh.MaterialIds[i, j, k] = 255; // Special ID for the entire borehole region
            // --- DEFINITIVE FIX END ---
            else if (layer != null)
                // Assign material properties based on layer
                mesh.MaterialIds[i, j, k] = (byte)(borehole.Lithology.IndexOf(layer) + 1);
            else
                mesh.MaterialIds[i, j, k] = 0; // Default material

            // Assign thermal and hydraulic properties
            if (layer != null)
            {
                var layerName = layer.RockType ?? "Unknown";

                // 1. Thermal Conductivity (W/m·K)
                if (layer.Parameters.TryGetValue("Thermal Conductivity", out var specificConductivity))
                    mesh.ThermalConductivities[i, j, k] = specificConductivity;
                else
                    mesh.ThermalConductivities[i, j, k] =
                        (float)options.LayerThermalConductivities.GetValueOrDefault(layerName, 2.5);

                // 2. Specific Heat (J/kg·K)
                if (layer.Parameters.TryGetValue("Specific Heat", out var specificHeat))
                    mesh.SpecificHeats[i, j, k] = specificHeat;
                else
                    mesh.SpecificHeats[i, j, k] = (float)options.LayerSpecificHeats.GetValueOrDefault(layerName, 900);

                // 3. Density (kg/m³)
                if (layer.Parameters.TryGetValue("Density", out var specificDensity))
                    mesh.Densities[i, j, k] = specificDensity;
                else
                    mesh.Densities[i, j, k] = (float)options.LayerDensities.GetValueOrDefault(layerName, 2650);

                // 4. Porosity (fraction, 0-1)
                if (layer.Parameters.TryGetValue("Porosity", out var specificPorosity))
                    // BoreholeDataset stores porosity in %, simulation needs a fraction
                    mesh.Porosities[i, j, k] = specificPorosity / 100.0f;
                else
                    mesh.Porosities[i, j, k] = (float)options.LayerPorosities.GetValueOrDefault(layerName, 0.1);

                // 5. Permeability (m²)
                if (layer.Parameters.TryGetValue("Permeability", out var specificPermeability))
                    mesh.Permeabilities[i, j, k] = specificPermeability;
                else
                    mesh.Permeabilities[i, j, k] =
                        (float)options.LayerPermeabilities.GetValueOrDefault(layerName, 1e-14);
            }
            else
            {
                // Use default material properties for unknown zones
                mesh.ThermalConductivities[i, j, k] = 2.5f; // Default granite-like
                mesh.SpecificHeats[i, j, k] = 900f;
                mesh.Densities[i, j, k] = 2650f;
                mesh.Porosities[i, j, k] = 0.05f;
                mesh.Permeabilities[i, j, k] = 1e-15f;
            }

            // Override properties for heat exchanger region
            if (mesh.MaterialIds[i, j, k] == 255)
            {
                // Use grout properties in the borehole
                mesh.ThermalConductivities[i, j, k] = (float)options.GroutThermalConductivity;
                mesh.SpecificHeats[i, j, k] = 1000f; // Grout specific heat
                mesh.Densities[i, j, k] = 1800f; // Grout density
                mesh.Porosities[i, j, k] = 0.3f; // Grout porosity
                mesh.Permeabilities[i, j, k] = 1e-16f; // Low permeability
            }
        }

        // Calculate face areas and transmissivities
        CalculateFaceAreas(mesh);
        CalculateTransmissivities(mesh);

        // Generate fracture network if enabled
        if (options.SimulateFractures) GenerateFractureNetwork(mesh, borehole, options);

        // Generate borehole geometry
        mesh.BoreholeElements = GenerateBoreholeElements(borehole, mesh, options);

        return mesh;
    }

    /// <summary>
    ///     Generates a refined vertical grid with higher resolution near layer boundaries.
    ///     CORRECTED: Returns Z coordinates from top (positive) to bottom (negative)
    /// </summary>
    private static float[] GenerateRefinedVerticalGrid(float minDepth, float maxDepth, int nz, List<float> layerDepths)
    {
        var z = new float[nz];

        // Generate Z coordinates from top to bottom
        // minDepth is the extension above surface (negative value)
        // maxDepth is the total depth below surface (positive value)

        // Z ranges from +minDepth to -maxDepth
        var zTop = Math.Abs(minDepth); // Positive value above surface
        var zBottom = -maxDepth; // Negative value below surface

        // Generate base uniform grid
        for (var i = 0; i < nz; i++)
        {
            var t = (float)i / (nz - 1);
            z[i] = zTop + (zBottom - zTop) * t;
        }

        // Apply refinement near layer boundaries
        if (layerDepths.Any())
        {
            var refinementZones = new List<(float center, float width)>();

            foreach (var depth in layerDepths)
            {
                // Convert depth to Z coordinate (depth is positive, Z is negative below surface)
                var zCoord = -depth;
                if (zCoord < zTop && zCoord > zBottom) refinementZones.Add((zCoord, 2.0f)); // 2m refinement zone
            }

            // Apply local refinement around each zone
            for (var iter = 0; iter < 3; iter++) // Multiple iterations for smoothing
            {
                var newZ = new float[nz];
                newZ[0] = z[0];
                newZ[nz - 1] = z[nz - 1];

                for (var i = 1; i < nz - 1; i++)
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
                    var dz1 = Math.Abs(z[i] - z[i - 1]);
                    var dz2 = Math.Abs(z[i + 1] - z[i]);
                    var targetDz = (dz1 + dz2) * 0.5f * refinementFactor;

                    newZ[i] = 0.5f * (z[i - 1] + z[i + 1]);
                }

                z = newZ;
            }
        }

        return z;
    }

    /// <summary>
    ///     Calculates face areas for finite volume discretization.
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
        for (var i = 0; i <= nr; i++)
        {
            var r = i == 0 ? mesh.R[0] * 0.5f :
                i == nr ? mesh.R[nr - 1] * 1.5f :
                0.5f * (mesh.R[i - 1] + mesh.R[i]);

            for (var j = 0; j < nth; j++)
            {
                var dtheta = 2f * MathF.PI / nth;

                for (var k = 0; k < nz; k++)
                {
                    var dz = k < nz - 1 ? mesh.Z[k + 1] - mesh.Z[k] : mesh.Z[k] - mesh.Z[k - 1];
                    mesh.RadialFaceAreas[i, j, k] = r * dtheta * Math.Abs(dz);
                }
            }
        }

        // Angular face areas
        for (var i = 0; i < nr; i++)
        {
            var dr = i < nr - 1 ? mesh.R[i + 1] - mesh.R[i] : mesh.R[i] - mesh.R[i - 1];

            for (var k = 0; k < nz; k++)
            {
                var dz = k < nz - 1 ? mesh.Z[k + 1] - mesh.Z[k] : mesh.Z[k] - mesh.Z[k - 1];

                for (var j = 0; j <= nth; j++) mesh.AngularFaceAreas[i, j, k] = dr * Math.Abs(dz);
            }
        }

        // Vertical face areas
        for (var i = 0; i < nr; i++)
        {
            var r = mesh.R[i];
            var dr = i < nr - 1 ? mesh.R[i + 1] - mesh.R[i] : mesh.R[i] - mesh.R[i - 1];

            for (var j = 0; j < nth; j++)
            {
                var dtheta = 2f * MathF.PI / nth;
                var area = r * dr * dtheta;

                for (var k = 0; k <= nz; k++) mesh.VerticalFaceAreas[i, j, k] = area;
            }
        }
    }

    /// <summary>
    ///     Calculates thermal transmissivities between cells.
    /// </summary>
    private static void CalculateTransmissivities(GeothermalMesh mesh)
    {
        var nr = mesh.RadialPoints;
        var nth = mesh.AngularPoints;
        var nz = mesh.VerticalPoints;

        mesh.RadialTransmissivities = new float[nr + 1, nth, nz];
        mesh.AngularTransmissivities = new float[nr, nth + 1, nz];
        mesh.VerticalTransmissivities = new float[nr, nth, nz + 1];

        // --- RADIALE: volto i (tra cella i-1 e i) ---
        for (var i = 1; i < nr; i++)
        {
            // Distanze centro-volto a sinistra/destra
            var dL = mesh.R[i] - mesh.R[i - 1];
            var dR = mesh.R[i + 1 < nr ? i + 1 : i] - mesh.R[i];
            if (i == nr - 1) dR = mesh.R[i] - mesh.R[i - 1]; // bordo interno all’ultima cella

            for (var j = 0; j < nth; j++)
            for (var k = 0; k < nz; k++)
            {
                var kL = MathF.Max(0.05f, mesh.ThermalConductivities[i - 1, j, k]);
                var kR = MathF.Max(0.05f, mesh.ThermalConductivities[i, j, k]);
                var A = mesh.RadialFaceAreas[i, j, k];

                var denom = dL / kL + dR / kR;
                mesh.RadialTransmissivities[i, j, k] = denom > 1e-12f ? 2f * A / denom : 0f;
            }
        }

        // --- ANGOLARE: volto j (tra j-1 e j) ---
        for (var i = 0; i < nr; i++)
        {
            var r = mesh.R[i];
            var dth = 2f * MathF.PI / nth;
            var d = r * dth; // distanza centro-volto in theta (per ciascun lato)
            for (var j = 0; j <= nth; j++)
            for (var k = 0; k < nz; k++)
            {
                var jL = (j - 1 + nth) % nth;
                var jR = j % nth;

                var kL = MathF.Max(0.05f, mesh.ThermalConductivities[i, jL, k]);
                var kR = MathF.Max(0.05f, mesh.ThermalConductivities[i, jR, k]);
                var A = mesh.AngularFaceAreas[i, j, k];

                var denom = d / kL + d / kR;
                mesh.AngularTransmissivities[i, j, k] = denom > 1e-12f ? 2f * A / denom : 0f;
            }
        }

        // --- VERTICALE: volto k (tra k-1 e k) ---
        for (var i = 0; i < nr; i++)
        for (var j = 0; j < nth; j++)
        for (var k = 1; k < nz; k++)
        {
            // spessori delle due celle adiacenti
            var dz1 = MathF.Abs(mesh.Z[k - 1] - mesh.Z[k - 2 >= 0 ? k - 2 : k - 1]);
            var dz2 = MathF.Abs(mesh.Z[k] - mesh.Z[k - 1]);
            if (k == 1) dz1 = MathF.Abs(mesh.Z[1] - mesh.Z[0]); // prima interfaccia

            var kL = MathF.Max(0.05f, mesh.ThermalConductivities[i, j, k - 1]); // sopra
            var kR = MathF.Max(0.05f, mesh.ThermalConductivities[i, j, k]); // sotto
            var A = mesh.VerticalFaceAreas[i, j, k];

            // resistenze in serie (metà spessore per lato -> fattore 2 in numeratore)
            var denom = dz1 / kL + dz2 / kR;
            mesh.VerticalTransmissivities[i, j, k] = denom > 1e-12f ? 2f * A / denom : 0f;
        }
    }


    /// <summary>
    ///     Generates a stochastic fracture network if enabled.
    /// </summary>
    private static void GenerateFractureNetwork(GeothermalMesh mesh, BoreholeDataset borehole,
        GeothermalSimulationOptions options)
    {
        // Simple fracture network generation
        var random = new Random(42); // Fixed seed for reproducibility
        var fractures = new List<FractureElement>();

        // Generate major fractures based on lithology
        foreach (var unit in borehole.Lithology)
            if (unit.RockType != null && (unit.RockType.Contains("Granite") || unit.RockType.Contains("Basalt")))
            {
                // Higher fracture density in crystalline rocks
                var numFractures = random.Next(5, 15);

                for (var i = 0; i < numFractures; i++)
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

        mesh.Fractures = fractures;
    }

    /// <summary>
    ///     Generates borehole element connectivity for heat exchanger.
    /// </summary>
    private static List<BoreholeElement> GenerateBoreholeElements(
        BoreholeDataset borehole,
        GeothermalMesh mesh,
        GeothermalSimulationOptions options)
    {
        var elements = new List<BoreholeElement>();
        var nz = 20; // Number of elements along borehole

        for (var i = 0; i < nz; i++)
        {
            var z = i * borehole.TotalDepth / (nz - 1);

            // Find mesh cell containing this borehole element
            var kMesh = 0;
            for (var k = 0; k < mesh.VerticalPoints - 1; k++)
                if (z >= -mesh.Z[k] && z <= -mesh.Z[k + 1])
                {
                    kMesh = k;
                    break;
                }

            var element = new BoreholeElement
            {
                Depth = z,
                MeshIndices = new[] { 0, 0, kMesh }, // Center of cylindrical mesh
                FlowArea = (float)(Math.PI * Math.Pow(options.PipeInnerDiameter / 2, 2)),
                WetPerimeter = (float)(Math.PI * options.PipeInnerDiameter),
                Length = borehole.TotalDepth / (nz - 1)
            };

            // Calculate local heat transfer properties
            if (options.HeatExchangerType == HeatExchangerType.UTube)
                element.HeatTransferArea = 2 * element.WetPerimeter * element.Length;
            else // Coaxial
                element.HeatTransferArea = element.WetPerimeter * element.Length;

            elements.Add(element);
        }

        return elements;
    }

    /// <summary>
    ///     Creates a 3D mesh for visualizing the borehole.
    /// </summary>
    public static Mesh3DDataset CreateBoreholeMesh(BoreholeDataset borehole, GeothermalSimulationOptions options)
    {
        var vertices = new List<Vector3>();
        var faces = new List<int[]>();
        const int angularSegments = 24; // Resolution for the pipes and borehole

        // 1. Create the outer borehole cylinder (representing the grout/fill)
        var boreholeRadius = borehole.WellDiameter / 2f;
        var boreholeDepth = options.BoreholeDataset.TotalDepth;
        CreateCylinder(vertices, faces, boreholeRadius, boreholeDepth, angularSegments, Vector3.Zero);

        // 2. Check the heat exchanger type and generate the correct internal pipes
        if (options.HeatExchangerType == HeatExchangerType.UTube)
        {
            // --- U-TUBE LOGIC ---
            // A U-tube has two separate pipes offset from the center.
            var pipeRadius = (float)options.PipeOuterDiameter / 2.0f;

            // Shank spacing is the center-to-center distance between the two pipes.
            // Use PipeSpacing if available, otherwise a reasonable default.
            var shankSpacing = (float)(options.PipeSpacing > 0 ? options.PipeSpacing : options.PipeOuterDiameter * 2.5);
            var pipeOffset = shankSpacing / 2.0f;

            // Create the first pipe (down-comer)
            var offset1 = new Vector3(pipeOffset, 0, 0);
            CreateCylinder(vertices, faces, pipeRadius, boreholeDepth, angularSegments, offset1);

            // Create the second pipe (up-comer)
            var offset2 = new Vector3(-pipeOffset, 0, 0);
            CreateCylinder(vertices, faces, pipeRadius, boreholeDepth, angularSegments, offset2);
        }
        else // --- COAXIAL LOGIC ---
        {
            // A coaxial system has an outer pipe (casing) and an inner pipe.
            // The outer pipe is already represented by the main borehole cylinder for visualization.

            // Create the inner pipe. Its outer radius is defined by PipeSpacing / 2.
            var innerPipeRadius = (float)options.PipeSpacing / 2.0f;
            if (innerPipeRadius > 0)
                CreateCylinder(vertices, faces, innerPipeRadius, boreholeDepth, angularSegments, Vector3.Zero);
        }

        return Mesh3DDataset.CreateFromData(
            "BoreholeHeatExchanger",
            Path.Combine(Path.GetTempPath(), "borehole_mesh.obj"),
            vertices,
            faces,
            1.0f,
            "m"
        );
    }

    /// <summary>
    ///     Helper method to generate the vertices and faces for a single cylinder.
    /// </summary>
    /// <param name="vertices">The master list of vertices to add to.</param>
    /// <param name="faces">The master list of faces to add to.</param>
    /// <param name="radius">Radius of the cylinder.</param>
    /// <param name="depth">Height (depth) of the cylinder.</param>
    /// <param name="segments">Number of angular segments for circular resolution.</param>
    /// <param name="offset">3D offset from the center (0,0,0).</param>
    private static void CreateCylinder(List<Vector3> vertices, List<int[]> faces, float radius, float depth,
        int segments, Vector3 offset)
    {
        var baseVertexIndex = vertices.Count;

        // Generate vertices for the top and bottom rings
        for (var i = 0; i < segments; i++)
        {
            var angle = i * 2.0f * MathF.PI / segments;
            var x = radius * MathF.Cos(angle);
            var y = radius * MathF.Sin(angle);

            // Top vertex
            vertices.Add(new Vector3(x, y, 0) + offset);
            // Bottom vertex
            vertices.Add(new Vector3(x, y, -depth) + offset);
        }

        // Generate faces for the cylinder wall
        for (var i = 0; i < segments; i++)
        {
            var next_i = (i + 1) % segments;

            // Indices for the current and next segments
            var topLeft = baseVertexIndex + i * 2;
            var bottomLeft = baseVertexIndex + i * 2 + 1;
            var topRight = baseVertexIndex + next_i * 2;
            var bottomRight = baseVertexIndex + next_i * 2 + 1;

            // Create two triangles for the quad face
            faces.Add(new[] { topLeft, bottomLeft, topRight });
            faces.Add(new[] { topRight, bottomLeft, bottomRight });
        }
    }
}

/// <summary>
///     Represents a fracture element in the mesh.
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
///     Represents a borehole heat exchanger element.
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
///     Specialized mesh structure for geothermal simulations.
/// </summary>
public class GeothermalMesh
{
    // Grid dimensions
    public int RadialPoints { get; set; }
    public int AngularPoints { get; set; }
    public int VerticalPoints { get; set; }

    // Coordinates
    public float[] R { get; set; } // Radial coordinates
    public float[] Theta { get; set; } // Angular coordinates
    public float[] Z { get; set; } // Vertical coordinates (negative = down)

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
    ///     Gets the total number of cells in the mesh.
    /// </summary>
    public int TotalCells => RadialPoints * AngularPoints * VerticalPoints;

    /// <summary>
    ///     Converts mesh indices to Cartesian coordinates.
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
