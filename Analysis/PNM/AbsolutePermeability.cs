// GeoscientistToolkit/Analysis/Pnm/AbsolutePermeability.cs - Fixed Version
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
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

    public sealed class PermeabilityResults
    {
        public float DarcyUncorrected { get; set; }
        public float DarcyCorrected { get; set; }
        public float NavierStokesUncorrected { get; set; }
        public float NavierStokesCorrected { get; set; }
        public float LatticeBoltzmannUncorrected { get; set; }
        public float LatticeBoltzmannCorrected { get; set; }
        public float Tortuosity { get; set; }
    }

    public static class AbsolutePermeability
    {
        // Store results for display
        private static PermeabilityResults _lastResults = new PermeabilityResults();
        
        public static PermeabilityResults GetLastResults() => _lastResults;

        public static void Calculate(PermeabilityOptions options)
        {
            var pnm = options.Dataset;
            if (pnm.Pores.Count == 0 || pnm.Throats.Count == 0)
            {
                Logger.LogWarning("[Permeability] PNM is empty, cannot calculate permeability.");
                return;
            }

            // Calculate pixel size in meters (important for correct units)
            float pixelSize_m = pnm.VoxelSize * 1e-6f; // μm to meters
            Logger.Log($"[Permeability] Using pixel size: {pnm.VoxelSize} μm = {pixelSize_m} m");

            // First, ensure tortuosity is calculated
            if (pnm.Tortuosity <= 0 || pnm.Tortuosity == 1.0f)
            {
                Logger.Log("[Permeability] Calculating geometric tortuosity...");
                pnm.Tortuosity = CalculateGeometricTortuosity(pnm, options.Axis);
                Logger.Log($"[Permeability] Tortuosity = {pnm.Tortuosity:F3}");
            }

            _lastResults.Tortuosity = pnm.Tortuosity;
            float tau2 = pnm.Tortuosity * pnm.Tortuosity;

            if (options.CalculateDarcy)
            {
                float darcyUncorrected = RunEngine(options, "Darcy");
                pnm.DarcyPermeability = darcyUncorrected; // Store uncorrected in dataset
                
                _lastResults.DarcyUncorrected = darcyUncorrected;
                _lastResults.DarcyCorrected = darcyUncorrected / tau2;
                
                Logger.Log($"[Permeability] Darcy permeability:");
                Logger.Log($"  Uncorrected: {darcyUncorrected:F3} mD");
                Logger.Log($"  τ²-corrected: {_lastResults.DarcyCorrected:F3} mD");
            }

            if (options.CalculateNavierStokes)
            {
                float nsUncorrected = RunEngine(options, "NavierStokes");
                pnm.NavierStokesPermeability = nsUncorrected;
                
                _lastResults.NavierStokesUncorrected = nsUncorrected;
                _lastResults.NavierStokesCorrected = nsUncorrected / tau2;
                
                Logger.Log($"[Permeability] Navier-Stokes permeability:");
                Logger.Log($"  Uncorrected: {nsUncorrected:F3} mD");
                Logger.Log($"  τ²-corrected: {_lastResults.NavierStokesCorrected:F3} mD");
            }

            if (options.CalculateLatticeBoltzmann)
            {
                float lbmUncorrected = RunEngine(options, "LatticeBoltzmann");
                pnm.LatticeBoltzmannPermeability = lbmUncorrected;
                
                _lastResults.LatticeBoltzmannUncorrected = lbmUncorrected;
                _lastResults.LatticeBoltzmannCorrected = lbmUncorrected / tau2;
                
                Logger.Log($"[Permeability] Lattice-Boltzmann permeability:");
                Logger.Log($"  Uncorrected: {lbmUncorrected:F3} mD");
                Logger.Log($"  τ²-corrected: {_lastResults.LatticeBoltzmannCorrected:F3} mD");
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
            
            // CRITICAL: Use actual pixel size from dataset
            float pixelSize_m = pnm.VoxelSize * 1e-6f; // μm to m
            
            // 1. Identify boundary pores (inlets/outlets)
            var (inletPores, outletPores, modelLength, crossSectionalArea) = GetBoundaryPores(pnm, options.Axis);
            if (inletPores.Count == 0 || outletPores.Count == 0)
            {
                Logger.LogWarning($"[Permeability] No inlet/outlet pores found for axis {options.Axis}. Result will be zero.");
                return 0f;
            }

            Logger.Log($"[Permeability] Found {inletPores.Count} inlet and {outletPores.Count} outlet pores");
            Logger.Log($"[Permeability] Model dimensions: Length={modelLength:E3} m, Area={crossSectionalArea:E3} m²");

            // 2. Build the linear system Ax=b representing the pressure network
            var (matrix, b) = BuildLinearSystem(pnm, engine, inletPores, outletPores, options.FluidViscosity, pixelSize_m);

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
            float totalFlowRate = CalculateTotalFlow(pnm, engine, pressures, inletPores, options.FluidViscosity, pixelSize_m);
            
            Logger.Log($"[Permeability] Total flow rate Q = {totalFlowRate:E3} m³/s");

            // 5. Calculate permeability K using Darcy's law
            // K = Q * μ * L / (A * ΔP)
            float viscosityPaS = options.FluidViscosity * 0.001f; // cP to Pa·s
            float pressureDrop = 1.0f; // Pa (we set inlet=1, outlet=0)
            
            float permeability_m2 = (totalFlowRate * viscosityPaS * modelLength) / (crossSectionalArea * pressureDrop);
            
            // Convert from m² to milliDarcy (mD)
            float permeability_mD = permeability_m2 * 1.01325e15f;
            
            stopwatch.Stop();
            Logger.Log($"[Permeability] Engine '{engine}' took {stopwatch.ElapsedMilliseconds}ms. Result: {permeability_mD:F3} mD");
            
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

        private static (SparseMatrix, float[]) BuildLinearSystem(PNMDataset pnm, string engine, 
            HashSet<int> inlets, HashSet<int> outlets, float viscosity_cP, float voxelSize_m)
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

                float conductance = CalculateConductance(p1, p2, throat, engine, voxelSize_m, viscosity_PaS);

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

        private static float CalculateConductance(Pore p1, Pore p2, Throat t, string engine, 
            float voxelSize_m, float viscosity_PaS)
        {
            // Convert all dimensions to meters
            float r_t = t.Radius * voxelSize_m;  // Throat radius in meters
            float r_p1 = p1.Radius * voxelSize_m;
            float r_p2 = p2.Radius * voxelSize_m;
            float length = Vector3.Distance(p1.Position, p2.Position) * voxelSize_m;

            if (length < 1e-9f) return 0f;

            // Different models for different engines
            switch (engine)
            {
                case "Darcy":
                    // Simple Hagen-Poiseuille for throat only
                    return (float)(Math.PI * Math.Pow(r_t, 4)) / (8 * viscosity_PaS * length);

                case "NavierStokes":
                    // Include entrance/exit effects
                    float entranceLength = 0.06f * r_t * 2000; // Approximate entrance length
                    float effectiveLength = length + entranceLength;
                    return (float)(Math.PI * Math.Pow(r_t, 4)) / (8 * viscosity_PaS * effectiveLength);

                case "LatticeBoltzmann":
                    // More complex model with pore body resistance
                    float l_p1 = r_p1 * 0.5f; // Pore body effective length
                    float l_p2 = r_p2 * 0.5f;
                    float l_t = Math.Max(1e-9f, length - l_p1 - l_p2);
                    
                    // Conductances in series
                    float g_p1 = (float)(Math.PI * Math.Pow(r_p1, 4)) / (8 * viscosity_PaS * l_p1);
                    float g_p2 = (float)(Math.PI * Math.Pow(r_p2, 4)) / (8 * viscosity_PaS * l_p2);
                    float g_t = (float)(Math.PI * Math.Pow(r_t, 4)) / (8 * viscosity_PaS * l_t);
                    
                    // Total resistance = sum of resistances
                    float totalResistance = (1 / g_p1) + (1 / g_t) + (1 / g_p2);
                    return 1 / totalResistance;

                default:
                    return (float)(Math.PI * Math.Pow(r_t, 4)) / (8 * viscosity_PaS * length);
            }
        }
        private static float CalculateTotalFlow(PNMDataset pnm, string engine, float[] pressures, 
            HashSet<int> inletPores, float viscosity_cP, float voxelSize_m)
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
                    
                    float conductance = CalculateConductance(p1, p2, throat, engine, voxelSize_m, viscosity_PaS);
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
                    
                    float conductance = CalculateConductance(p1, p2, throat, engine, voxelSize_m, viscosity_PaS);
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
        private static CL _cl;
        private static nint _context;
        private static nint _device;
        private static nint _queue;
        private static nint _program;
        private static bool _initialized = false;

        public static bool IsAvailable 
        { 
            get 
            { 
                EnsureInitialized();
                return _isAvailable.Value && _initialized; 
            } 
        }

        private static bool CheckAvailability()
        {
            try
            {
                _cl = CL.GetApi();
                unsafe
                {
                    uint numPlatforms;
                    _cl.GetPlatformIDs(0, null, &numPlatforms);
                    return numPlatforms > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            
            try
            {
                _cl = CL.GetApi();
                
                unsafe
                {
                    // Get platforms
                    uint numPlatforms;
                    _cl.GetPlatformIDs(0, null, &numPlatforms);
                    if (numPlatforms == 0) return;
                    
                    var platforms = stackalloc nint[(int)numPlatforms];
                    _cl.GetPlatformIDs(numPlatforms, platforms, null);
                    
                    // Find a device (prefer GPU)
                    nint selectedPlatform = 0;
                    _device = 0;
                    
                    for (int i = 0; i < numPlatforms; i++)
                    {
                        uint numDevices;
                        _cl.GetDeviceIDs(platforms[i], DeviceType.Gpu, 0, null, &numDevices);
                        
                        if (numDevices == 0)
                            _cl.GetDeviceIDs(platforms[i], DeviceType.Cpu, 0, null, &numDevices);
                        
                        if (numDevices > 0)
                        {
                            selectedPlatform = platforms[i];
                            var devices = stackalloc nint[(int)numDevices];
                            _cl.GetDeviceIDs(selectedPlatform, DeviceType.All, numDevices, devices, null);
                            _device = devices[0];
                            break;
                        }
                    }
                    
                    if (_device == 0) return;
                    
                    // Create context and command queue
                    int err;
                    nint* devicePtr = stackalloc nint[1];
                    devicePtr[0] = _device;
                    _context = _cl.CreateContext(null, 1, devicePtr, null, null, &err);
                    if (err != 0) return;
                    
                    _queue = _cl.CreateCommandQueue(_context, _device, CommandQueueProperties.None, &err);
                    if (err != 0) return;
                    
                    // Create program with CG kernel
                    string kernelSource = @"
                    __kernel void spmv_csr(
                        __global const int* row_ptr,
                        __global const int* col_idx,
                        __global const float* values,
                        __global const float* x,
                        __global float* y,
                        const int num_rows)
                    {
                        int row = get_global_id(0);
                        if (row >= num_rows) return;
                        
                        float sum = 0.0f;
                        int row_start = row_ptr[row];
                        int row_end = row_ptr[row + 1];
                        
                        for (int j = row_start; j < row_end; j++) {
                            sum += values[j] * x[col_idx[j]];
                        }
                        
                        y[row] = sum;
                    }
                    
                    __kernel void vector_ops(
                        __global float* y,
                        __global const float* x,
                        const float alpha,
                        const int n,
                        const int op) // 0=copy, 1=axpy, 2=scale
                    {
                        int i = get_global_id(0);
                        if (i >= n) return;
                        
                        if (op == 0) y[i] = x[i];
                        else if (op == 1) y[i] += alpha * x[i];
                        else if (op == 2) y[i] = alpha * x[i];
                    }
                    
                    __kernel void dot_product(
                        __global const float* a,
                        __global const float* b,
                        __global float* result,
                        __local float* scratch,
                        const int n)
                    {
                        int global_id = get_global_id(0);
                        int local_id = get_local_id(0);
                        int group_size = get_local_size(0);
                        
                        float accumulator = 0;
                        while (global_id < n) {
                            accumulator += a[global_id] * b[global_id];
                            global_id += get_global_size(0);
                        }
                        
                        scratch[local_id] = accumulator;
                        barrier(CLK_LOCAL_MEM_FENCE);
                        
                        for (int offset = group_size / 2; offset > 0; offset /= 2) {
                            if (local_id < offset) {
                                scratch[local_id] += scratch[local_id + offset];
                            }
                            barrier(CLK_LOCAL_MEM_FENCE);
                        }
                        
                        if (local_id == 0) {
                            result[get_group_id(0)] = scratch[0];
                        }
                    }";
                    
                    var sourcePtr = kernelSource;
                    var sourceLen = (nuint)kernelSource.Length;
                    _program = _cl.CreateProgramWithSource(_context, 1, new[] { sourcePtr }, in sourceLen, &err);
                    if (err != 0) return;
                    
                    nint* devicePtr2 = stackalloc nint[1];
                    devicePtr2[0] = _device;
                    err = _cl.BuildProgram(_program, 1, devicePtr2, string.Empty, null, null);
                    
                    if (err != 0)
                    {
                        // Get build log
                        nuint logSize;
                        _cl.GetProgramBuildInfo(_program, _device, ProgramBuildInfo.BuildLog, 0, null, &logSize);
                        if (logSize > 0)
                        {
                            var log = new byte[logSize];
                            fixed (byte* logPtr = log)
                            {
                                _cl.GetProgramBuildInfo(_program, _device, ProgramBuildInfo.BuildLog, logSize, logPtr, null);
                                string logStr = System.Text.Encoding.ASCII.GetString(log);
                                Logger.LogError($"[OpenCL] Build failed: {logStr}");
                            }
                        }
                        return;
                    }
                    
                    _initialized = true;
                    Logger.Log("[OpenCL] Context initialized successfully");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[OpenCL] Initialization failed: {ex.Message}");
                _initialized = false;
            }
        }

        public static float[] Solve(SparseMatrix A, float[] b, float tolerance, int maxIterations)
        {
            EnsureInitialized();
            if (!_initialized) throw new InvalidOperationException("OpenCL not initialized");
            
            unsafe
            {
                int n = b.Length;
                
                // Convert sparse matrix to CSR format
                var (rowPtr, colIdx, values) = ConvertToCSR(A);
                
                // Create OpenCL buffers
                int err;
                nint clRowPtr, clColIdx, clValues, clB, clX, clR, clP, clAp, clDotResult;
                
                // Pin arrays and create buffers
                fixed (int* rowPtrPtr = rowPtr)
                fixed (int* colIdxPtr = colIdx)
                fixed (float* valuesPtr = values)
                fixed (float* bPtr = b)
                {
                    clRowPtr = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, 
                        (nuint)(rowPtr.Length * sizeof(int)), rowPtrPtr, &err);
                    if (err != 0) throw new Exception($"Failed to create rowPtr buffer: {err}");
                    
                    clColIdx = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                        (nuint)(colIdx.Length * sizeof(int)), colIdxPtr, &err);
                    if (err != 0) throw new Exception($"Failed to create colIdx buffer: {err}");
                    
                    clValues = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                        (nuint)(values.Length * sizeof(float)), valuesPtr, &err);
                    if (err != 0) throw new Exception($"Failed to create values buffer: {err}");
                    
                    clB = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                        (nuint)(b.Length * sizeof(float)), bPtr, &err);
                    if (err != 0) throw new Exception($"Failed to create b buffer: {err}");
                }
                
                clX = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
                    (nuint)(n * sizeof(float)), null, &err);
                if (err != 0) throw new Exception($"Failed to create x buffer: {err}");
                
                clR = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
                    (nuint)(n * sizeof(float)), null, &err);
                if (err != 0) throw new Exception($"Failed to create r buffer: {err}");
                
                clP = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
                    (nuint)(n * sizeof(float)), null, &err);
                if (err != 0) throw new Exception($"Failed to create p buffer: {err}");
                
                clAp = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
                    (nuint)(n * sizeof(float)), null, &err);
                if (err != 0) throw new Exception($"Failed to create Ap buffer: {err}");
                
                clDotResult = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
                    (nuint)(256 * sizeof(float)), null, &err);
                if (err != 0) throw new Exception($"Failed to create dot result buffer: {err}");
                
                // Create kernels
                var spmvKernel = _cl.CreateKernel(_program, "spmv_csr", &err);
                if (err != 0) throw new Exception($"Failed to create spmv kernel: {err}");
                
                var vectorOpsKernel = _cl.CreateKernel(_program, "vector_ops", &err);
                if (err != 0) throw new Exception($"Failed to create vector_ops kernel: {err}");
                
                var dotKernel = _cl.CreateKernel(_program, "dot_product", &err);
                if (err != 0) throw new Exception($"Failed to create dot kernel: {err}");
                
                // Initialize x = 0, r = b, p = r
                float zero = 0.0f;
                _cl.EnqueueFillBuffer(_queue, clX, &zero, (nuint)sizeof(float), 0, (nuint)(n * sizeof(float)), 0, null, null);
                _cl.EnqueueCopyBuffer(_queue, clB, clR, 0, 0, (nuint)(n * sizeof(float)), 0, null, null);
                _cl.EnqueueCopyBuffer(_queue, clR, clP, 0, 0, (nuint)(n * sizeof(float)), 0, null, null);
                
                // CG iteration
                float rsold = ComputeDotProduct(clR, clR, n, dotKernel, clDotResult);
                
                if (Math.Sqrt(rsold) < tolerance)
                {
                    // Already converged
                    float[] result = new float[n];
                    fixed (float* resultPtr = result)
                    {
                        _cl.EnqueueReadBuffer(_queue, clX, true, 0, (nuint)(n * sizeof(float)), resultPtr, 0, null, null);
                    }
                    
                    // Cleanup
                    CleanupBuffers(clRowPtr, clColIdx, clValues, clB, clX, clR, clP, clAp, clDotResult);
                    CleanupKernels(spmvKernel, vectorOpsKernel, dotKernel);
                    
                    return result;
                }
                
                for (int iter = 0; iter < maxIterations; iter++)
                {
                    // Ap = A * p
                    ComputeSpmv(spmvKernel, clRowPtr, clColIdx, clValues, clP, clAp, n);
                    
                    // pAp = dot(p, Ap)
                    float pAp = ComputeDotProduct(clP, clAp, n, dotKernel, clDotResult);
                    
                    if (Math.Abs(pAp) < 1e-10f) break;
                    
                    float alpha = rsold / pAp;
                    
                    // x = x + alpha * p
                    ComputeAxpy(vectorOpsKernel, clX, clP, alpha, n, 1);
                    
                    // r = r - alpha * Ap
                    ComputeAxpy(vectorOpsKernel, clR, clAp, -alpha, n, 1);
                    
                    // rsnew = dot(r, r)
                    float rsnew = ComputeDotProduct(clR, clR, n, dotKernel, clDotResult);
                    
                    if (Math.Sqrt(rsnew) < tolerance)
                    {
                        Logger.Log($"[OpenCL CG] Converged in {iter + 1} iterations");
                        break;
                    }
                    
                    float beta = rsnew / rsold;
                    
                    // p = r + beta * p
                    ComputeScale(vectorOpsKernel, clP, beta, n);
                    ComputeAxpy(vectorOpsKernel, clP, clR, 1.0f, n, 1);
                    
                    rsold = rsnew;
                    
                    if (iter % 50 == 0)
                    {
                        Logger.Log($"[OpenCL CG] Iteration {iter}, residual: {Math.Sqrt(rsnew):E3}");
                    }
                }
                
                // Read result
                float[] solution = new float[n];
                fixed (float* solutionPtr = solution)
                {
                    _cl.EnqueueReadBuffer(_queue, clX, true, 0, (nuint)(n * sizeof(float)), solutionPtr, 0, null, null);
                }
                
                // Cleanup
                CleanupBuffers(clRowPtr, clColIdx, clValues, clB, clX, clR, clP, clAp, clDotResult);
                CleanupKernels(spmvKernel, vectorOpsKernel, dotKernel);
                
                return solution;
            }
        }
        

        private static unsafe void ComputeSpmv(nint kernel, nint rowPtr, nint colIdx, nint values, 
            nint x, nint y, int numRows)
        {
            _cl.SetKernelArg(kernel, 0, (nuint)sizeof(nint), &rowPtr);
            _cl.SetKernelArg(kernel, 1, (nuint)sizeof(nint), &colIdx);
            _cl.SetKernelArg(kernel, 2, (nuint)sizeof(nint), &values);
            _cl.SetKernelArg(kernel, 3, (nuint)sizeof(nint), &x);
            _cl.SetKernelArg(kernel, 4, (nuint)sizeof(nint), &y);
            _cl.SetKernelArg(kernel, 5, (nuint)sizeof(int), &numRows);
            
            nuint globalSize = (nuint)numRows;
            _cl.EnqueueNdrangeKernel(_queue, kernel, 1, null, &globalSize, null, 0, null, null);
            _cl.Finish(_queue);
        }

        private static unsafe void ComputeAxpy(nint kernel, nint y, nint x, float alpha, int n, int op)
        {
            _cl.SetKernelArg(kernel, 0, (nuint)sizeof(nint), &y);
            _cl.SetKernelArg(kernel, 1, (nuint)sizeof(nint), &x);
            _cl.SetKernelArg(kernel, 2, (nuint)sizeof(float), &alpha);
            _cl.SetKernelArg(kernel, 3, (nuint)sizeof(int), &n);
            _cl.SetKernelArg(kernel, 4, (nuint)sizeof(int), &op);
            
            nuint globalSize = (nuint)n;
            _cl.EnqueueNdrangeKernel(_queue, kernel, 1, null, &globalSize, null, 0, null, null);
            _cl.Finish(_queue);
        }

        private static unsafe void ComputeScale(nint kernel, nint x, float alpha, int n)
        {
            int op = 2; // scale operation
            _cl.SetKernelArg(kernel, 0, (nuint)sizeof(nint), &x);
            _cl.SetKernelArg(kernel, 1, (nuint)sizeof(nint), &x); // input and output same for scaling
            _cl.SetKernelArg(kernel, 2, (nuint)sizeof(float), &alpha);
            _cl.SetKernelArg(kernel, 3, (nuint)sizeof(int), &n);
            _cl.SetKernelArg(kernel, 4, (nuint)sizeof(int), &op);
            
            nuint globalSize = (nuint)n;
            _cl.EnqueueNdrangeKernel(_queue, kernel, 1, null, &globalSize, null, 0, null, null);
            _cl.Finish(_queue);
        }

        private static unsafe float ComputeDotProduct(nint a, nint b, int n, nint kernel, nint result)
        {
            int localSize = 256;
            int numGroups = (n + localSize - 1) / localSize;
            
            _cl.SetKernelArg(kernel, 0, (nuint)sizeof(nint), &a);
            _cl.SetKernelArg(kernel, 1, (nuint)sizeof(nint), &b);
            _cl.SetKernelArg(kernel, 2, (nuint)sizeof(nint), &result);
            _cl.SetKernelArg(kernel, 3, (nuint)(localSize * sizeof(float)), null); // local memory
            _cl.SetKernelArg(kernel, 4, (nuint)sizeof(int), &n);
            
            nuint globalSize = (nuint)(numGroups * localSize);
            nuint localSizePtr = (nuint)localSize;
            _cl.EnqueueNdrangeKernel(_queue, kernel, 1, null, &globalSize, &localSizePtr, 0, null, null);
            _cl.Finish(_queue);
            
            // Read partial results and sum
            float[] partialResults = new float[numGroups];
            fixed (float* ptr = partialResults)
            {
                _cl.EnqueueReadBuffer(_queue, result, true, 0, (nuint)(numGroups * sizeof(float)), ptr, 0, null, null);
            }
            
            float sum = 0;
            for (int i = 0; i < numGroups; i++)
                sum += partialResults[i];
            
            return sum;
        }

        private static (int[] rowPtr, int[] colIdx, float[] values) ConvertToCSR(SparseMatrix matrix)
        {
            var rowPtr = new List<int>();
            var colIdx = new List<int>();
            var values = new List<float>();
            
            rowPtr.Add(0);
            
            for (int row = 0; row < matrix.Size; row++)
            {
                var rowData = matrix.GetRow(row);
                var sortedCols = rowData.OrderBy(kvp => kvp.Key);
                
                foreach (var kvp in sortedCols)
                {
                    colIdx.Add(kvp.Key);
                    values.Add(kvp.Value);
                }
                
                rowPtr.Add(values.Count);
            }
            
            return (rowPtr.ToArray(), colIdx.ToArray(), values.ToArray());
        }

        private static unsafe void CleanupBuffers(params nint[] buffers)
        {
            foreach (var buffer in buffers)
            {
                if (buffer != 0)
                    _cl.ReleaseMemObject(buffer);
            }
        }

        private static unsafe void CleanupKernels(params nint[] kernels)
        {
            foreach (var kernel in kernels)
            {
                if (kernel != 0)
                    _cl.ReleaseKernel(kernel);
            }
        }
    }
}