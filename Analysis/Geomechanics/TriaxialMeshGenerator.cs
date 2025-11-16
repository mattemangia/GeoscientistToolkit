// GeoscientistToolkit/Analysis/Geomechanics/TriaxialMeshGenerator.cs
// Generates 3D cylindrical meshes for triaxial compression/extension tests
// Supports various mesh densities and boundary conditions

using System.Numerics;

namespace GeoscientistToolkit.Analysis.Geomechanics;

/// <summary>
/// Generates hexahedral mesh for cylindrical rock samples used in triaxial testing.
/// Includes platens (top/bottom loading surfaces) and radial confinement zones.
/// </summary>
public class TriaxialMeshGenerator
{
    public class TriaxialMesh
    {
        public Vector3[] Nodes { get; set; }
        public int[] Elements { get; set; } // 8 nodes per hex element
        public int[] TopPlatenNodes { get; set; }
        public int[] BottomPlatenNodes { get; set; }
        public int[] LateralSurfaceNodes { get; set; }
        public int NRadial { get; set; }
        public int NCircumferential { get; set; }
        public int NAxial { get; set; }
        public float Radius { get; set; }
        public float Height { get; set; }

        public int TotalNodes => Nodes.Length;
        public int TotalElements => Elements.Length / 8;
    }

    /// <summary>
    /// Generate a cylindrical mesh for triaxial testing.
    /// </summary>
    /// <param name="radius">Cylinder radius in meters (typically 0.025-0.05m for lab samples)</param>
    /// <param name="height">Cylinder height in meters (typically 0.05-0.1m for lab samples)</param>
    /// <param name="nRadial">Number of divisions in radial direction (recommend 4-8)</param>
    /// <param name="nCircumferential">Number of divisions around circumference (recommend 12-24)</param>
    /// <param name="nAxial">Number of divisions in axial direction (recommend 10-20)</param>
    /// <returns>Cylindrical mesh with hex elements</returns>
    public static TriaxialMesh GenerateCylindricalMesh(
        float radius,
        float height,
        int nRadial,
        int nCircumferential,
        int nAxial)
    {
        var mesh = new TriaxialMesh
        {
            Radius = radius,
            Height = height,
            NRadial = nRadial,
            NCircumferential = nCircumferential,
            NAxial = nAxial
        };

        // Generate nodes in cylindrical coordinates, then convert to Cartesian
        var nodes = new List<Vector3>();
        var nodeIndices = new Dictionary<(int r, int theta, int z), int>();

        // Generate nodes layer by layer (bottom to top)
        for (int iz = 0; iz <= nAxial; iz++)
        {
            float z = (iz / (float)nAxial) * height;

            for (int ir = 0; ir <= nRadial; ir++)
            {
                float r = (ir / (float)nRadial) * radius;

                // For each radial position, generate circumferential nodes
                int thetaSteps = (ir == 0) ? 1 : nCircumferential; // Center has only 1 node

                for (int itheta = 0; itheta < thetaSteps; itheta++)
                {
                    float theta = (itheta / (float)nCircumferential) * 2f * MathF.PI;

                    float x = r * MathF.Cos(theta);
                    float y = r * MathF.Sin(theta);

                    int nodeIdx = nodes.Count;
                    nodes.Add(new Vector3(x, y, z));
                    nodeIndices[(ir, itheta, iz)] = nodeIdx;
                }
            }
        }

        // Generate hex elements
        var elements = new List<int>();

        for (int iz = 0; iz < nAxial; iz++)
        {
            for (int ir = 0; ir < nRadial; ir++)
            {
                int thetaSteps = (ir == 0) ? nCircumferential : nCircumferential;

                for (int itheta = 0; itheta < thetaSteps; itheta++)
                {
                    int nextTheta = (itheta + 1) % nCircumferential;

                    // Handle center specially (wedge elements converted to degenerate hex)
                    if (ir == 0)
                    {
                        // Inner ring element (degenerate hex with center node)
                        int n0 = nodeIndices[(0, 0, iz)];       // Center bottom
                        int n1 = nodeIndices[(1, itheta, iz)];  // Outer bottom 1
                        int n2 = nodeIndices[(1, nextTheta, iz)]; // Outer bottom 2
                        int n3 = n2; // Degenerate (repeat node 2)

                        int n4 = nodeIndices[(0, 0, iz + 1)];       // Center top
                        int n5 = nodeIndices[(1, itheta, iz + 1)];  // Outer top 1
                        int n6 = nodeIndices[(1, nextTheta, iz + 1)]; // Outer top 2
                        int n7 = n6; // Degenerate (repeat node 6)

                        elements.AddRange(new[] { n0, n1, n2, n3, n4, n5, n6, n7 });
                    }
                    else
                    {
                        // Regular hex element
                        // Bottom face (counter-clockwise when viewed from bottom)
                        int n0 = nodeIndices[(ir, itheta, iz)];
                        int n1 = nodeIndices[(ir + 1, itheta, iz)];
                        int n2 = nodeIndices[(ir + 1, nextTheta, iz)];
                        int n3 = nodeIndices[(ir, nextTheta, iz)];

                        // Top face (counter-clockwise when viewed from bottom)
                        int n4 = nodeIndices[(ir, itheta, iz + 1)];
                        int n5 = nodeIndices[(ir + 1, itheta, iz + 1)];
                        int n6 = nodeIndices[(ir + 1, nextTheta, iz + 1)];
                        int n7 = nodeIndices[(ir, nextTheta, iz + 1)];

                        elements.AddRange(new[] { n0, n1, n2, n3, n4, n5, n6, n7 });
                    }
                }
            }
        }

        // Identify boundary nodes
        var topPlaten = new List<int>();
        var bottomPlaten = new List<int>();
        var lateralSurface = new List<int>();

        foreach (var kvp in nodeIndices)
        {
            var (ir, itheta, iz) = kvp.Key;
            int nodeIdx = kvp.Value;

            // Top platen (all nodes at iz == nAxial)
            if (iz == nAxial)
                topPlaten.Add(nodeIdx);

            // Bottom platen (all nodes at iz == 0)
            if (iz == 0)
                bottomPlaten.Add(nodeIdx);

            // Lateral surface (all nodes at ir == nRadial)
            if (ir == nRadial)
                lateralSurface.Add(nodeIdx);
        }

        mesh.Nodes = nodes.ToArray();
        mesh.Elements = elements.ToArray();
        mesh.TopPlatenNodes = topPlaten.ToArray();
        mesh.BottomPlatenNodes = bottomPlaten.ToArray();
        mesh.LateralSurfaceNodes = lateralSurface.ToArray();

        return mesh;
    }

    /// <summary>
    /// Generate a simplified cylindrical mesh using brick elements (faster, less accurate)
    /// </summary>
    public static TriaxialMesh GenerateCartesianCylindricalMesh(
        float radius,
        float height,
        int nX,
        int nY,
        int nZ)
    {
        var mesh = new TriaxialMesh
        {
            Radius = radius,
            Height = height,
            NRadial = Math.Max(nX, nY),
            NCircumferential = 4,
            NAxial = nZ
        };

        var nodes = new List<Vector3>();
        var nodeIndices = new Dictionary<(int x, int y, int z), int>();

        // Generate Cartesian grid
        for (int iz = 0; iz <= nZ; iz++)
        {
            float z = (iz / (float)nZ) * height;

            for (int iy = 0; iy <= nY; iy++)
            {
                float y = ((iy / (float)nY) - 0.5f) * 2f * radius;

                for (int ix = 0; ix <= nX; ix++)
                {
                    float x = ((ix / (float)nX) - 0.5f) * 2f * radius;

                    // Only include nodes inside or on cylinder
                    float r = MathF.Sqrt(x * x + y * y);
                    if (r <= radius * 1.01f) // Slight tolerance
                    {
                        int nodeIdx = nodes.Count;
                        nodes.Add(new Vector3(x, y, z));
                        nodeIndices[(ix, iy, iz)] = nodeIdx;
                    }
                }
            }
        }

        // Generate hex elements
        var elements = new List<int>();

        for (int iz = 0; iz < nZ; iz++)
        {
            for (int iy = 0; iy < nY; iy++)
            {
                for (int ix = 0; ix < nX; ix++)
                {
                    // Check if all 8 corner nodes exist
                    bool allExist = true;
                    int[] cornerNodes = new int[8];
                    (int, int, int)[] offsets = new[]
                    {
                        (0, 0, 0), (1, 0, 0), (1, 1, 0), (0, 1, 0),
                        (0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1)
                    };

                    for (int i = 0; i < 8; i++)
                    {
                        var key = (ix + offsets[i].Item1, iy + offsets[i].Item2, iz + offsets[i].Item3);
                        if (!nodeIndices.ContainsKey(key))
                        {
                            allExist = false;
                            break;
                        }
                        cornerNodes[i] = nodeIndices[key];
                    }

                    if (allExist)
                        elements.AddRange(cornerNodes);
                }
            }
        }

        // Identify boundary nodes
        var topPlaten = new List<int>();
        var bottomPlaten = new List<int>();
        var lateralSurface = new List<int>();

        foreach (var kvp in nodeIndices)
        {
            var (ix, iy, iz) = kvp.Key;
            int nodeIdx = kvp.Value;
            var pos = nodes[nodeIdx];

            if (iz == nZ)
                topPlaten.Add(nodeIdx);

            if (iz == 0)
                bottomPlaten.Add(nodeIdx);

            float r = MathF.Sqrt(pos.X * pos.X + pos.Y * pos.Y);
            if (r >= radius * 0.99f)
                lateralSurface.Add(nodeIdx);
        }

        mesh.Nodes = nodes.ToArray();
        mesh.Elements = elements.ToArray();
        mesh.TopPlatenNodes = topPlaten.ToArray();
        mesh.BottomPlatenNodes = bottomPlaten.ToArray();
        mesh.LateralSurfaceNodes = lateralSurface.ToArray();

        return mesh;
    }
}
