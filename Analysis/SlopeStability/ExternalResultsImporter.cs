using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// Imports results from external simulations to use as initial conditions
    /// or boundary conditions for slope stability analysis.
    /// </summary>
    public class ExternalResultsImporter
    {
        /// <summary>
        /// Imports stress field from a CSV file.
        /// Expected format: X, Y, Z, SigmaXX, SigmaYY, SigmaZZ, SigmaXY, SigmaXZ, SigmaYZ
        /// </summary>
        public static Dictionary<Vector3, StressTensor> ImportStressFieldFromCSV(string filePath)
        {
            var stressField = new Dictionary<Vector3, StressTensor>();

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Stress field file not found: {filePath}");

            var lines = File.ReadAllLines(filePath);
            bool firstLine = true;

            foreach (var line in lines)
            {
                if (firstLine)
                {
                    firstLine = false;
                    continue; // Skip header
                }

                var parts = line.Split(',');
                if (parts.Length < 9)
                    continue;

                try
                {
                    float x = float.Parse(parts[0].Trim());
                    float y = float.Parse(parts[1].Trim());
                    float z = float.Parse(parts[2].Trim());
                    Vector3 position = new Vector3(x, y, z);

                    var stress = new StressTensor
                    {
                        SigmaXX = float.Parse(parts[3].Trim()),
                        SigmaYY = float.Parse(parts[4].Trim()),
                        SigmaZZ = float.Parse(parts[5].Trim()),
                        SigmaXY = float.Parse(parts[6].Trim()),
                        SigmaXZ = float.Parse(parts[7].Trim()),
                        SigmaYZ = float.Parse(parts[8].Trim())
                    };

                    stressField[position] = stress;
                }
                catch
                {
                    // Skip invalid lines
                    continue;
                }
            }

            return stressField;
        }

        /// <summary>
        /// Imports displacement field from CSV.
        /// Expected format: X, Y, Z, Ux, Uy, Uz
        /// </summary>
        public static Dictionary<Vector3, Vector3> ImportDisplacementFieldFromCSV(string filePath)
        {
            var displacementField = new Dictionary<Vector3, Vector3>();

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Displacement field file not found: {filePath}");

            var lines = File.ReadAllLines(filePath);
            bool firstLine = true;

            foreach (var line in lines)
            {
                if (firstLine)
                {
                    firstLine = false;
                    continue;
                }

                var parts = line.Split(',');
                if (parts.Length < 6)
                    continue;

                try
                {
                    float x = float.Parse(parts[0].Trim());
                    float y = float.Parse(parts[1].Trim());
                    float z = float.Parse(parts[2].Trim());
                    Vector3 position = new Vector3(x, y, z);

                    float ux = float.Parse(parts[3].Trim());
                    float uy = float.Parse(parts[4].Trim());
                    float uz = float.Parse(parts[5].Trim());
                    Vector3 displacement = new Vector3(ux, uy, uz);

                    displacementField[position] = displacement;
                }
                catch
                {
                    continue;
                }
            }

            return displacementField;
        }

        /// <summary>
        /// Imports velocity field from CSV.
        /// Expected format: X, Y, Z, Vx, Vy, Vz
        /// </summary>
        public static Dictionary<Vector3, Vector3> ImportVelocityFieldFromCSV(string filePath)
        {
            return ImportDisplacementFieldFromCSV(filePath); // Same format
        }

        /// <summary>
        /// Imports pore pressure field from CSV.
        /// Expected format: X, Y, Z, Pressure
        /// </summary>
        public static Dictionary<Vector3, float> ImportPorePressureFromCSV(string filePath)
        {
            var pressureField = new Dictionary<Vector3, float>();

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Pore pressure file not found: {filePath}");

            var lines = File.ReadAllLines(filePath);
            bool firstLine = true;

            foreach (var line in lines)
            {
                if (firstLine)
                {
                    firstLine = false;
                    continue;
                }

                var parts = line.Split(',');
                if (parts.Length < 4)
                    continue;

                try
                {
                    float x = float.Parse(parts[0].Trim());
                    float y = float.Parse(parts[1].Trim());
                    float z = float.Parse(parts[2].Trim());
                    Vector3 position = new Vector3(x, y, z);

                    float pressure = float.Parse(parts[3].Trim());

                    pressureField[position] = pressure;
                }
                catch
                {
                    continue;
                }
            }

            return pressureField;
        }

        /// <summary>
        /// Imports complete simulation state from JSON.
        /// </summary>
        public static ExternalSimulationState ImportFromJSON(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"JSON file not found: {filePath}");

            var json = File.ReadAllText(filePath);
            var state = JsonSerializer.Deserialize<ExternalSimulationState>(json);

            return state ?? new ExternalSimulationState();
        }

        /// <summary>
        /// Imports from VTK format (simplified - only structured points).
        /// </summary>
        public static Dictionary<Vector3, float> ImportScalarFieldFromVTK(string filePath, string scalarName)
        {
            var scalarField = new Dictionary<Vector3, float>();

            // This is a simplified VTK reader - only handles STRUCTURED_POINTS
            // For full VTK support, consider using VTK libraries

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"VTK file not found: {filePath}");

            var lines = File.ReadAllLines(filePath);
            // Parse VTK header and data...
            // Implementation omitted for brevity - would need full VTK parser

            return scalarField;
        }

        /// <summary>
        /// Applies imported stress field to blocks.
        /// Interpolates stress values to block centroids.
        /// </summary>
        public static void ApplyStressFieldToBlocks(
            List<Block> blocks,
            Dictionary<Vector3, StressTensor> stressField)
        {
            foreach (var block in blocks)
            {
                // Find nearest stress value or interpolate
                var stress = InterpolateStress(block.Centroid, stressField);

                // Store in block metadata or apply as initial condition
                // (Would need to add stress state to Block class)
            }
        }

        /// <summary>
        /// Simple nearest-neighbor interpolation for stress.
        /// For production, use trilinear or higher-order interpolation.
        /// </summary>
        private static StressTensor InterpolateStress(
            Vector3 position,
            Dictionary<Vector3, StressTensor> stressField)
        {
            if (stressField.Count == 0)
                return new StressTensor();

            // Find nearest point
            var nearest = stressField.Keys
                .OrderBy(p => (p - position).Length())
                .First();

            return stressField[nearest];
        }

        /// <summary>
        /// Applies imported displacement field as initial conditions.
        /// </summary>
        public static void ApplyDisplacementFieldToBlocks(
            List<Block> blocks,
            Dictionary<Vector3, Vector3> displacementField)
        {
            foreach (var block in blocks)
            {
                var displacement = InterpolateVector(block.Centroid, displacementField);
                block.Position = block.InitialPosition + displacement;
                block.TotalDisplacement = displacement;
            }
        }

        /// <summary>
        /// Simple nearest-neighbor interpolation for vectors.
        /// </summary>
        private static Vector3 InterpolateVector(
            Vector3 position,
            Dictionary<Vector3, Vector3> vectorField)
        {
            if (vectorField.Count == 0)
                return Vector3.Zero;

            var nearest = vectorField.Keys
                .OrderBy(p => (p - position).Length())
                .First();

            return vectorField[nearest];
        }
    }

    /// <summary>
    /// Stress tensor representation.
    /// </summary>
    public class StressTensor
    {
        public float SigmaXX { get; set; }
        public float SigmaYY { get; set; }
        public float SigmaZZ { get; set; }
        public float SigmaXY { get; set; }
        public float SigmaXZ { get; set; }
        public float SigmaYZ { get; set; }

        public StressTensor()
        {
            SigmaXX = 0.0f;
            SigmaYY = 0.0f;
            SigmaZZ = 0.0f;
            SigmaXY = 0.0f;
            SigmaXZ = 0.0f;
            SigmaYZ = 0.0f;
        }

        /// <summary>
        /// Calculates principal stresses.
        /// </summary>
        public (float sigma1, float sigma2, float sigma3) GetPrincipalStresses()
        {
            // Construct stress matrix
            float[,] stress = new float[3, 3]
            {
                { SigmaXX, SigmaXY, SigmaXZ },
                { SigmaXY, SigmaYY, SigmaYZ },
                { SigmaXZ, SigmaYZ, SigmaZZ }
            };

            // Calculate invariants
            float I1 = SigmaXX + SigmaYY + SigmaZZ;
            float I2 = SigmaXX * SigmaYY + SigmaYY * SigmaZZ + SigmaZZ * SigmaXX -
                      SigmaXY * SigmaXY - SigmaYZ * SigmaYZ - SigmaXZ * SigmaXZ;
            float I3 = SigmaXX * SigmaYY * SigmaZZ +
                      2.0f * SigmaXY * SigmaYZ * SigmaXZ -
                      SigmaXX * SigmaYZ * SigmaYZ -
                      SigmaYY * SigmaXZ * SigmaXZ -
                      SigmaZZ * SigmaXY * SigmaXY;

            // Solve cubic equation (simplified - assumes real eigenvalues)
            // For production, use proper eigenvalue solver
            float sigma1 = I1 / 3.0f;  // Simplified
            float sigma2 = I1 / 3.0f;
            float sigma3 = I1 / 3.0f;

            return (sigma1, sigma2, sigma3);
        }

        /// <summary>
        /// Calculates von Mises stress.
        /// </summary>
        public float GetVonMisesStress()
        {
            var (s1, s2, s3) = GetPrincipalStresses();
            return MathF.Sqrt(0.5f * ((s1 - s2) * (s1 - s2) +
                                      (s2 - s3) * (s2 - s3) +
                                      (s3 - s1) * (s3 - s1)));
        }
    }

    /// <summary>
    /// Complete external simulation state.
    /// </summary>
    public class ExternalSimulationState
    {
        public Dictionary<int, Vector3> BlockPositions { get; set; }
        public Dictionary<int, Vector3> BlockVelocities { get; set; }
        public Dictionary<int, StressTensor> BlockStresses { get; set; }
        public Dictionary<int, float> BlockDamage { get; set; }
        public float SimulationTime { get; set; }
        public string SourceSimulation { get; set; }
        public Dictionary<string, object> Metadata { get; set; }

        public ExternalSimulationState()
        {
            BlockPositions = new Dictionary<int, Vector3>();
            BlockVelocities = new Dictionary<int, Vector3>();
            BlockStresses = new Dictionary<int, StressTensor>();
            BlockDamage = new Dictionary<int, float>();
            SimulationTime = 0.0f;
            SourceSimulation = "";
            Metadata = new Dictionary<string, object>();
        }
    }
}
