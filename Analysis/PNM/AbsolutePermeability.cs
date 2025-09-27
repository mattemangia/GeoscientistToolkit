// GeoscientistToolkit/Analysis/Pnm/AbsolutePermeability.cs - Fixed Version
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.Util;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Analysis.Pnm
{
    public sealed class PermeabilityOptions
    {
        public PNMDataset Dataset { get; set; }
        public FlowAxis Axis { get; set; } = FlowAxis.Z;
        public float FluidViscosity { get; set; } = 1.0f; // in cP
        public bool CorrectForTortuosity { get; set; } = true;
        public bool UseGpu { get; set; } = false;
        public bool CalculateDarcy { get; set; }
        public bool CalculateNavierStokes { get; set; }
        public bool CalculateLatticeBoltzmann { get; set; }
    }

    public static class AbsolutePermeability
    {
        // --- PUBLIC API ---

        public static void Calculate(PermeabilityOptions options)
        {
            var pnm = options.Dataset;
            if (pnm.Pores.Count == 0 || pnm.Throats.Count == 0)
            {
                Logger.LogWarning("[Permeability] PNM is empty, cannot calculate permeability.");
                return;
            }

            // First, ensure tortuosity is calculated
            if (pnm.Tortuosity <= 0 || pnm.Tortuosity == 1.0f)
            {
                Logger.Log("[Permeability] Calculating geometric tortuosity...");
                pnm.Tortuosity = CalculateGeometricTortuosity(pnm, options.Axis);
                Logger.Log($"[Permeability] Tortuosity = {pnm.Tortuosity:F3}");
            }

            if (options.CalculateDarcy)
            {
                pnm.DarcyPermeability = RunEngine(options, "Darcy");
                Logger.Log($"[Permeability] Darcy permeability = {pnm.DarcyPermeability:F3} mD (apparent)");
                
                // Store both corrected and uncorrected
                if (options.CorrectForTortuosity && pnm.Tortuosity > 1.0f)
                {
                    float corrected = pnm.DarcyPermeability / (pnm.Tortuosity * pnm.Tortuosity);
                    Logger.Log($"[Permeability] Darcy permeability = {corrected:F3} mD (τ²-corrected)");
                }
            }

            if (options.CalculateNavierStokes)
            {
                pnm.NavierStokesPermeability = RunEngine(options, "NavierStokes");
                Logger.Log($"[Permeability] Navier-Stokes permeability = {pnm.NavierStokesPermeability:F3} mD");
            }

            if (options.CalculateLatticeBoltzmann)
            {
                pnm.LatticeBoltzmannPermeability = RunEngine(options, "LatticeBoltzmann");
                Logger.Log($"[Permeability] Lattice-Boltzmann permeability = {pnm.LatticeBoltzmannPermeability:F3} mD");
            }

            Logger.Log("[Permeability] Calculations complete.");
        }

        // --- GEOMETRIC TORTUOSITY CALCULATION ---
        
        private static float CalculateGeometricTortuosity(PNMDataset pnm, FlowAxis axis)
        {
            // Geometric tortuosity is the average path length through the network divided by the straight-line distance
            // This is a simplified calculation based on pore connectivity
            
            if (pnm.Pores.Count == 0) return 1.0f;
            
            // Find inlet and outlet pores
            var (inletPores, outletPores, modelLength, _) = GetBoundaryPores(pnm, axis);
            
            if (inletPores.Count == 0 || outletPores.Count == 0) return 1.0f;
            
            // Build adjacency map
            var adjacency = new Dictionary<int, List<(int neighbor, float distance)>>();
            foreach (var throat in pnm.Throats)
            {
                var p1 = pnm.Pores.FirstOrDefault(p => p.ID == throat.Pore1ID);
                var p2 = pnm.Pores.FirstOrDefault(p => p.ID == throat.Pore2ID);
                
                if (p1 != null && p2 != null)
                {
                    float dist = Vector3.Distance(p1.Position, p2.Position) * pnm.VoxelSize * 1e-6f; // Convert to meters
                    
                    if (!adjacency.ContainsKey(p1.ID)) adjacency[p1.ID] = new List<(int, float)>();
                    if (!adjacency.ContainsKey(p2.ID)) adjacency[p2.ID] = new List<(int, float)>();
                    
                    adjacency[p1.ID].Add((p2.ID, dist));
                    adjacency[p2.ID].Add((p1.ID, dist));
                }
            }
            
            // Find shortest paths from each inlet to each outlet using Dijkstra
            float totalPathLength = 0;
            int pathCount = 0;
            
            foreach (int inlet in inletPores)
            {
                var distances = DijkstraShortestPath(adjacency, inlet, pnm.Pores.Max(p => p.ID) + 1);
                
                foreach (int outlet in outletPores)
                {
                    if (distances.ContainsKey(outlet) && distances[outlet] < float.MaxValue)
                    {
                        totalPathLength += distances[outlet];
                        pathCount++;
                    }
                }
            }
            
            if (pathCount == 0) return 1.0f;
            
            float avgPathLength = totalPathLength / pathCount;
            float tortuosity = avgPathLength / modelLength;
            
            // Clamp to reasonable range
            return Math.Max(1.0f, Math.Min(10.0f, tortuosity));
        }
        
        private static Dictionary<int, float> DijkstraShortestPath(Dictionary<int, List<(int neighbor, float distance)>> adjacency, int start, int maxId)
        {
            var distances = new Dictionary<int, float>();
            var visited = new HashSet<int>();
            var pq = new PriorityQueue<int, float>();
            
            distances[start] = 0;
            pq.Enqueue(start, 0);
            
            while (pq.Count > 0)
            {
                pq.TryDequeue(out int current, out float currentDist);
                
                if (visited.Contains(current)) continue;
                visited.Add(current);
                
                if (!adjacency.ContainsKey(current)) continue;
                
                foreach (var (neighbor, edgeWeight) in adjacency[current])
                {
                    if (visited.Contains(neighbor)) continue;
                    
                    float newDist = currentDist + edgeWeight;
                    
                    if (!distances.ContainsKey(neighbor) || newDist < distances[neighbor])
                    {
                        distances[neighbor] = newDist;
                        pq.Enqueue(neighbor, newDist);
                    }
                }
            }
            
            return distances;
        }

        // --- CORE PERMEABILITY CALCULATION ---

        private static float RunEngine(PermeabilityOptions options, string engine)
        {
            var stopwatch = Stopwatch.StartNew();
            var pnm = options.Dataset;
            
            // 1. Identify boundary pores (inlets/outlets)
            var (inletPores, outletPores, modelLength, crossSectionalArea) = GetBoundaryPores(pnm, options.Axis);
            if (inletPores.Count == 0 || outletPores.Count == 0)
            {
                Logger.LogWarning($"[Permeability] No inlet/outlet pores found for axis {options.Axis}. Result will be zero.");
                return 0f;
            }

            Logger.Log($"[Permeability] Found {inletPores.Count} inlet and {outletPores.Count} outlet pores");

            // 2. Build the linear system Ax=b representing the pressure network
            var (matrix, b) = BuildLinearSystem(pnm, engine, inletPores, outletPores, options.FluidViscosity);

            // 3. Solve for pore pressures (x)
            float[] pressures = options.UseGpu && OpenCLContext.IsAvailable
                ? SolveWithGpu(matrix, b)
                : SolveWithCpu(matrix, b);

            if (pressures == null)
            {
                Logger.LogError($"[Permeability] Linear system solver failed for engine '{engine}'.");
                return 0f;
            }
            
            // 4. Calculate total flow rate Q
            float totalFlowRate = CalculateTotalFlow(pnm, engine, pressures, inletPores, options.FluidViscosity);
            
            Logger.Log($"[Permeability] Total flow rate Q = {totalFlowRate:E3} m³/s");

            // 5. Calculate APPARENT permeability K using Darcy's law
            // K = Q * μ * L / (A * ΔP)
            float viscosityPaS = options.FluidViscosity * 0.001f; // cP to Pa·s
            float pressureDrop = 1.0f; // Pa (we set inlet=1, outlet=0)
            
            float permeability_m2 = (totalFlowRate * viscosityPaS * modelLength) / (crossSectionalArea * pressureDrop);
            
            // NOTE: We do NOT apply tortuosity correction here anymore
            // The returned value is the APPARENT permeability
            // Tortuosity correction is applied in the viewer when displaying
            
            // Convert from m² to milliDarcy (mD)
            float permeability_mD = permeability_m2 * 1.01325e15f;
            
            stopwatch.Stop();
            Logger.Log($"[Permeability] Engine '{engine}' took {stopwatch.ElapsedMilliseconds}ms. Result: {permeability_mD:F3} mD (apparent)");
            
            return permeability_mD;
        }

        private static (HashSet<int> inlets, HashSet<int> outlets, float L, float A) GetBoundaryPores(PNMDataset pnm, FlowAxis axis)
        {
            if (pnm.Pores.Count == 0) 
                return (new HashSet<int>(), new HashSet<int>(), 0, 0);

            var bounds = (
                Min: new Vector3(
                    pnm.Pores.Min(p => p.Position.X), 
                    pnm.Pores.Min(p => p.Position.Y), 
                    pnm.Pores.Min(p => p.Position.Z)),
                Max: new Vector3(
                    pnm.Pores.Max(p => p.Position.X), 
                    pnm.Pores.Max(p => p.Position.Y), 
                    pnm.Pores.Max(p => p.Position.Z))
            );

            float voxelSize_m = pnm.VoxelSize * 1e-6f; // μm to m
            var inlets = new HashSet<int>();
            var outlets = new HashSet<int>();
            float L = 0, A = 0;
            float tolerance = 1.0f; // Tolerance in voxels

            switch (axis)
            {
                case FlowAxis.X:
                    L = (bounds.Max.X - bounds.Min.X) * voxelSize_m;
                    A = (bounds.Max.Y - bounds.Min.Y) * (bounds.Max.Z - bounds.Min.Z) * voxelSize_m * voxelSize_m;
                    foreach (var pore in pnm.Pores)
                    {
                        if (pore.Position.X <= bounds.Min.X + tolerance) inlets.Add(pore.ID);
                        if (pore.Position.X >= bounds.Max.X - tolerance) outlets.Add(pore.ID);
                    }
                    break;
                case FlowAxis.Y:
                    L = (bounds.Max.Y - bounds.Min.Y) * voxelSize_m;
                    A = (bounds.Max.X - bounds.Min.X) * (bounds.Max.Z - bounds.Min.Z) * voxelSize_m * voxelSize_m;
                    foreach (var pore in pnm.Pores)
                    {
                        if (pore.Position.Y <= bounds.Min.Y + tolerance) inlets.Add(pore.ID);
                        if (pore.Position.Y >= bounds.Max.Y - tolerance) outlets.Add(pore.ID);
                    }
                    break;
                case FlowAxis.Z:
                    L = (bounds.Max.Z - bounds.Min.Z) * voxelSize_m;
                    A = (bounds.Max.X - bounds.Min.X) * (bounds.Max.Y - bounds.Min.Y) * voxelSize_m * voxelSize_m;
                    foreach (var pore in pnm.Pores)
                    {
                        if (pore.Position.Z <= bounds.Min.Z + tolerance) inlets.Add(pore.ID);
                        if (pore.Position.Z >= bounds.Max.Z - tolerance) outlets.Add(pore.ID);
                    }
                    break;
            }
            
            return (inlets, outlets, L, A);
        }

        private static (SparseMatrix, float[]) BuildLinearSystem(PNMDataset pnm, string engine, HashSet<int> inlets, HashSet<int> outlets, float viscosity_cP)
        {
            int maxId = pnm.Pores.Max(p => p.ID);
            var poreMap = pnm.Pores.ToDictionary(p => p.ID);
            var matrix = new SparseMatrix(maxId + 1);
            var b = new float[maxId + 1];

            // Convert viscosity to Pa·s
            float viscosity_PaS = viscosity_cP * 0.001f;

            foreach (var throat in pnm.Throats)
            {
                if (!poreMap.TryGetValue(throat.Pore1ID, out var p1) || 
                    !poreMap.TryGetValue(throat.Pore2ID, out var p2)) 
                    continue;

                float conductance = CalculateConductance(p1, p2, throat, engine, pnm.VoxelSize, viscosity_PaS);

                // Build the system matrix (Kirchhoff's current law at each pore)
                matrix.Add(p1.ID, p1.ID, conductance);
                matrix.Add(p2.ID, p2.ID, conductance);
                matrix.Add(p1.ID, p2.ID, -conductance);
                matrix.Add(p2.ID, p1.ID, -conductance);
            }

            // Apply boundary conditions (Dirichlet: set pressure)
            foreach (int id in inlets)
            {
                matrix.ClearRow(id);
                matrix.Set(id, id, 1.0f);
                b[id] = 1.0f; // Inlet pressure = 1 Pa
            }
            
            foreach (int id in outlets)
            {
                matrix.ClearRow(id);
                matrix.Set(id, id, 1.0f);
                b[id] = 0.0f; // Outlet pressure = 0 Pa
            }

            return (matrix, b);
        }

        private static float CalculateConductance(Pore p1, Pore p2, Throat t, string engine, float voxelSize_um, float viscosity_PaS)
        {
            float voxelSize_m = voxelSize_um * 1e-6f;
            float r_t = t.Radius * voxelSize_m; // Throat radius in meters
            float r_p1 = p1.Radius * voxelSize_m;
            float r_p2 = p2.Radius * voxelSize_m;
            float length = Vector3.Distance(p1.Position, p2.Position) * voxelSize_m;

            if (length < 1e-9f) return 0f;

            // Hagen-Poiseuille conductance for a cylindrical tube: g = πr⁴/(8μL)
            float g_throat = (float)(Math.PI * Math.Pow(r_t, 4)) / (8 * viscosity_PaS * length);

            if (engine == "Darcy")
            {
                // Simplified model - only throat resistance
                return g_throat;
            }
            else // NavierStokes and LatticeBoltzmann use more detailed models
            {
                // Model as three segments in series: pore1 → throat → pore2
                // Each segment has its own conductance
                
                // Effective lengths for each segment
                float l_p1 = r_p1; // Pore body length approximated by its radius
                float l_p2 = r_p2;
                float l_t = Math.Max(1e-9f, length - l_p1 - l_p2);
                
                // Conductance of each segment
                float g_p1 = (float)(Math.PI * Math.Pow(r_p1, 4)) / (8 * viscosity_PaS * l_p1);
                float g_p2 = (float)(Math.PI * Math.Pow(r_p2, 4)) / (8 * viscosity_PaS * l_p2);
                float g_t_segment = (float)(Math.PI * Math.Pow(r_t, 4)) / (8 * viscosity_PaS * l_t);
                
                // Total conductance (resistances in series)
                float totalResistance = (1 / g_p1) + (1 / g_t_segment) + (1 / g_p2);
                
                return 1 / totalResistance;
            }
        }

        private static float CalculateTotalFlow(PNMDataset pnm, string engine, float[] pressures, HashSet<int> inletPores, float viscosity_cP)
        {
            float totalFlow = 0;
            var poreMap = pnm.Pores.ToDictionary(p => p.ID);
            float viscosity_PaS = viscosity_cP * 0.001f;

            // Calculate flow leaving inlet pores
            foreach (var throat in pnm.Throats)
            {
                bool p1_inlet = inletPores.Contains(throat.Pore1ID);
                bool p2_inlet = inletPores.Contains(throat.Pore2ID);

                // Only count flow from inlet to non-inlet pores
                if (p1_inlet && !p2_inlet)
                {
                    if (!poreMap.TryGetValue(throat.Pore1ID, out var p1) || 
                        !poreMap.TryGetValue(throat.Pore2ID, out var p2)) 
                        continue;
                    
                    float conductance = CalculateConductance(p1, p2, throat, engine, pnm.VoxelSize, viscosity_PaS);
                    float pressure_p1 = pressures[p1.ID];
                    float pressure_p2 = pressures[p2.ID];
                    float flow = conductance * (pressure_p1 - pressure_p2);
                    totalFlow += flow;
                }
                else if (!p1_inlet && p2_inlet)
                {
                    if (!poreMap.TryGetValue(throat.Pore1ID, out var p1) || 
                        !poreMap.TryGetValue(throat.Pore2ID, out var p2)) 
                        continue;
                    
                    float conductance = CalculateConductance(p1, p2, throat, engine, pnm.VoxelSize, viscosity_PaS);
                    float pressure_p1 = pressures[p1.ID];
                    float pressure_p2 = pressures[p2.ID];
                    float flow = conductance * (pressure_p2 - pressure_p1);
                    totalFlow += flow;
                }
            }
            
            return totalFlow;
        }

        // --- CPU CONJUGATE GRADIENT SOLVER ---

        private static float[] SolveWithCpu(SparseMatrix A, float[] b, float tolerance = 1e-6f, int maxIterations = 5000)
        {
            int n = b.Length;
            var x = new float[n];
            var r = new float[n];
            Array.Copy(b, r, n);
            
            // r = b - Ax (but x = 0 initially, so r = b)
            var p = new float[n];
            Array.Copy(r, p, n);

            float rsold = Dot(r, r);
            
            if (rsold < tolerance * tolerance) return x; // Already solved

            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                var Ap = A.Multiply(p);
                float pAp = Dot(p, Ap);
                
                if (Math.Abs(pAp) < 1e-10f)
                {
                    Logger.LogWarning($"[CG Solver] Breakdown at iteration {iteration}, pAp={pAp}");
                    break;
                }
                
                float alpha = rsold / pAp;

                // x = x + alpha * p
                Axpy(x, p, alpha);
                
                // r = r - alpha * Ap
                Axpy(r, Ap, -alpha);

                float rsnew = Dot(r, r);
                
                if (Math.Sqrt(rsnew) < tolerance)
                {
                    Logger.Log($"[CG Solver] Converged in {iteration + 1} iterations");
                    break;
                }

                float beta = rsnew / rsold;
                
                // p = r + beta * p
                for (int j = 0; j < n; j++) 
                    p[j] = r[j] + beta * p[j];
                
                rsold = rsnew;
            }
            
            return x;
        }

        private static float Dot(float[] a, float[] b)
        {
            double sum = 0;
            for (int i = 0; i < a.Length; i++)
                sum += a[i] * b[i];
            return (float)sum;
        }

        private static void Axpy(float[] y, float[] x, float alpha)
        {
            for (int i = 0; i < y.Length; i++)
                y[i] += alpha * x[i];
        }

        // --- GPU SOLVER (simplified placeholder) ---
        
        private static float[] SolveWithGpu(SparseMatrix A, float[] b, float tolerance = 1e-6f, int maxIterations = 5000)
        {
            try
            {
                // Try GPU if available
                return OpenCLContext.Solve(A, b, tolerance, maxIterations);
            }
            catch(Exception ex)
            {
                Logger.LogWarning($"[GPU Solver] Failed: {ex.Message}. Falling back to CPU.");
                return SolveWithCpu(A, b, tolerance, maxIterations);
            }
        }
    }
    
    // --- SPARSE MATRIX CLASS ---

    public class SparseMatrix
    {
        private readonly Dictionary<int, float>[] _rows;
        public int Size { get; }

        public SparseMatrix(int size)
        {
            Size = size;
            _rows = new Dictionary<int, float>[size];
            for (int i = 0; i < size; i++) 
                _rows[i] = new Dictionary<int, float>();
        }

        public void Set(int row, int col, float value) => _rows[row][col] = value;
        
        public void Add(int row, int col, float value)
        {
            _rows[row].TryGetValue(col, out float current);
            _rows[row][col] = current + value;
        }
        
        public void ClearRow(int row) => _rows[row].Clear();
        
        public Dictionary<int, float> GetRow(int row) => _rows[row];

        public float[] Multiply(float[] vector)
        {
            var result = new float[Size];
            Parallel.For(0, Size, i =>
            {
                double sum = 0;
                foreach (var (col, value) in _rows[i])
                {
                    sum += value * vector[col];
                }
                result[i] = (float)sum;
            });
            return result;
        }
    }

    // --- OPENCL CONTEXT (simplified) ---
    
    internal static class OpenCLContext
    {
        private static readonly Lazy<bool> _isAvailable = new(CheckAvailability);
        public static bool IsAvailable { get; set; } = _isAvailable.Value;

        private static bool CheckAvailability()
        {
            try
            {
                // Check if OpenCL is available
                var cl = CL.GetApi();
                unsafe
                {
                    uint numPlatforms;
                    cl.GetPlatformIDs(0, null, &numPlatforms);
                    return numPlatforms > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public static float[] Solve(SparseMatrix A, float[] b, float tolerance, int maxIterations)
        {
            // Simplified placeholder - in production, implement full OpenCL solver
            throw new NotImplementedException("OpenCL solver not fully implemented");
        }
    }
}