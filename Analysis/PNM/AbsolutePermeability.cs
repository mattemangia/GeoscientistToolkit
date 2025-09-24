// GeoscientistToolkit/Analysis/Pnm/AbsolutePermeability.cs
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

            if (options.CalculateDarcy)
                pnm.DarcyPermeability = RunEngine(options, "Darcy");

            if (options.CalculateNavierStokes)
                pnm.NavierStokesPermeability = RunEngine(options, "NavierStokes");

            if (options.CalculateLatticeBoltzmann)
                pnm.LatticeBoltzmannPermeability = RunEngine(options, "LatticeBoltzmann");

            Logger.Log("[Permeability] Calculations complete.");
        }

        // --- CORE LOGIC ---

        private static float RunEngine(PermeabilityOptions options, string engine)
        {
            var stopwatch = Stopwatch.StartNew();
            var pnm = options.Dataset;
            
            // 1. Identify boundary pores (inlets/outlets)
            var (inletPores, outletPores, modelLength, crossSectionalArea) = GetBoundaryConditions(pnm, options.Axis);
            if (inletPores.Count == 0 || outletPores.Count == 0)
            {
                Logger.LogWarning($"[Permeability] No inlet/outlet pores found for axis {options.Axis}. Result will be zero.");
                return 0f;
            }

            // 2. Build the linear system Ax=b representing the pressure network
            var (matrix, b) = BuildLinearSystem(pnm, engine, inletPores, outletPores);

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
            float totalFlowRate = CalculateTotalFlow(pnm, engine, pressures, inletPores);

            // 5. Calculate permeability K using Darcy's macroscopic law
            float viscosityPaS = options.FluidViscosity * 0.001f; // cP to Pa·s
            float pressureDrop = 1.0f; // Pa (since we set inlet=1, outlet=0)
            
            float permeability_m2 = (totalFlowRate * viscosityPaS * modelLength) / (crossSectionalArea * pressureDrop);
            
            // 6. Optional tortuosity correction
            if (options.CorrectForTortuosity && pnm.Tortuosity > 1.0f)
            {
                permeability_m2 /= (pnm.Tortuosity * pnm.Tortuosity);
            }

            // 7. Convert from m^2 to milliDarcy (mD)
            float permeability_mD = permeability_m2 * 1.01325e15f;
            
            stopwatch.Stop();
            Logger.Log($"[Permeability] Engine '{engine}' took {stopwatch.ElapsedMilliseconds}ms. Result: {permeability_mD:F3} mD");
            
            return permeability_mD;
        }

        // --- STEP-BY-STEP IMPLEMENTATION ---

        private static (HashSet<int> inlets, HashSet<int> outlets, float L, float A) GetBoundaryConditions(PNMDataset pnm, FlowAxis axis)
        {
            var bounds = (
                Min: new Vector3(pnm.Pores.Min(p => p.Position.X), pnm.Pores.Min(p => p.Position.Y), pnm.Pores.Min(p => p.Position.Z)),
                Max: new Vector3(pnm.Pores.Max(p => p.Position.X), pnm.Pores.Max(p => p.Position.Y), pnm.Pores.Max(p => p.Position.Z))
            );

            float voxelSize = pnm.VoxelSize * 1e-6f; // um to m
            var inlets = new HashSet<int>();
            var outlets = new HashSet<int>();
            float L = 0, A = 0;
            float tolerance = 2.0f * voxelSize; // Tolerate pores being slightly inside the boundary

            switch (axis)
            {
                case FlowAxis.X:
                    L = (bounds.Max.X - bounds.Min.X) * voxelSize;
                    A = (bounds.Max.Y - bounds.Min.Y) * (bounds.Max.Z - bounds.Min.Z) * voxelSize * voxelSize;
                    foreach (var pore in pnm.Pores)
                    {
                        if (pore.Position.X * voxelSize <= bounds.Min.X * voxelSize + tolerance) inlets.Add(pore.ID);
                        if (pore.Position.X * voxelSize >= bounds.Max.X * voxelSize - tolerance) outlets.Add(pore.ID);
                    }
                    break;
                case FlowAxis.Y:
                    L = (bounds.Max.Y - bounds.Min.Y) * voxelSize;
                    A = (bounds.Max.X - bounds.Min.X) * (bounds.Max.Z - bounds.Min.Z) * voxelSize * voxelSize;
                    foreach (var pore in pnm.Pores)
                    {
                        if (pore.Position.Y * voxelSize <= bounds.Min.Y * voxelSize + tolerance) inlets.Add(pore.ID);
                        if (pore.Position.Y * voxelSize >= bounds.Max.Y * voxelSize - tolerance) outlets.Add(pore.ID);
                    }
                    break;
                case FlowAxis.Z:
                    L = (bounds.Max.Z - bounds.Min.Z) * voxelSize;
                    A = (bounds.Max.X - bounds.Min.X) * (bounds.Max.Y - bounds.Min.Y) * voxelSize * voxelSize;
                    foreach (var pore in pnm.Pores)
                    {
                        if (pore.Position.Z * voxelSize <= bounds.Min.Z * voxelSize + tolerance) inlets.Add(pore.ID);
                        if (pore.Position.Z * voxelSize >= bounds.Max.Z * voxelSize - tolerance) outlets.Add(pore.ID);
                    }
                    break;
            }
            return (inlets, outlets, L, A);
        }

        private static (SparseMatrix, float[]) BuildLinearSystem(PNMDataset pnm, string engine, HashSet<int> inlets, HashSet<int> outlets)
        {
            int maxId = pnm.Pores.Max(p => p.ID);
            var poreMap = pnm.Pores.ToDictionary(p => p.ID);
            var matrix = new SparseMatrix(maxId + 1);
            var b = new float[maxId + 1];

            foreach (var throat in pnm.Throats)
            {
                if (!poreMap.TryGetValue(throat.Pore1ID, out var p1) || !poreMap.TryGetValue(throat.Pore2ID, out var p2)) continue;

                float conductance = CalculateConductance(p1, p2, throat, engine, pnm.VoxelSize);

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

        private static float CalculateConductance(Pore p1, Pore p2, Throat t, string engine, float voxelSize)
        {
            float voxelSize_m = voxelSize * 1e-6f;
            float r_t = t.Radius * voxelSize_m;
            float r_p1 = p1.Radius * voxelSize_m;
            float r_p2 = p2.Radius * voxelSize_m;
            float length = Vector3.Distance(p1.Position, p2.Position) * voxelSize_m;

            if (length < 1e-9f) return 0f;

            // Hagen-Poiseuille conductance for a cylindrical tube (assuming viscosity of 1 for now)
            float g_throat = (float)(Math.PI * Math.Pow(r_t, 4)) / (8 * length);

            if (engine == "Darcy")
            {
                // Darcy model often simplified to only consider throat resistance
                return g_throat;
            }
            else // NS and LBM use a more detailed model with pore body resistance
            {
                // Model as 3 segments in series: pore-half 1, throat, pore-half 2
                float l_p1 = r_p1;
                float l_p2 = r_p2;
                float l_t = Math.Max(1e-9f, length - l_p1 - l_p2);
                
                // Conductance of each segment
                float g_p1 = (float)(Math.PI * Math.Pow(r_p1, 4)) / (8 * l_p1);
                float g_p2 = (float)(Math.PI * Math.Pow(r_p2, 4)) / (8 * l_p2);
                float g_t_segment = (float)(Math.PI * Math.Pow(r_t, 4)) / (8 * l_t);

                // Total resistance is sum of resistances (1/g)
                float totalResistance = (1 / g_p1) + (1 / g_t_segment) + (1 / g_p2);
                
                return 1 / totalResistance;
            }
        }

        private static float CalculateTotalFlow(PNMDataset pnm, string engine, float[] pressures, HashSet<int> inletPores)
        {
            float totalFlow = 0;
            var poreMap = pnm.Pores.ToDictionary(p => p.ID);

            foreach (var throat in pnm.Throats)
            {
                bool p1_inlet = inletPores.Contains(throat.Pore1ID);
                bool p2_inlet = inletPores.Contains(throat.Pore2ID);

                // Sum flow out of inlet pores into the network
                if (p1_inlet && !p2_inlet)
                {
                    if (!poreMap.TryGetValue(throat.Pore1ID, out var p1) || !poreMap.TryGetValue(throat.Pore2ID, out var p2)) continue;
                    float conductance = CalculateConductance(p1, p2, throat, engine, pnm.VoxelSize);
                    float pressure_p1 = pressures[p1.ID];
                    float pressure_p2 = pressures[p2.ID];
                    totalFlow += conductance * (pressure_p1 - pressure_p2);
                }
                // Sum flow into inlet pores from outside the network (should not happen with Dirichlet BC)
                else if (!p1_inlet && p2_inlet)
                {
                     if (!poreMap.TryGetValue(throat.Pore1ID, out var p1) || !poreMap.TryGetValue(throat.Pore2ID, out var p2)) continue;
                    float conductance = CalculateConductance(p1, p2, throat, engine, pnm.VoxelSize);
                    float pressure_p1 = pressures[p1.ID];
                    float pressure_p2 = pressures[p2.ID];
                    totalFlow += conductance * (pressure_p2 - pressure_p1);
                }
            }
            return totalFlow;
        }


        // --- CPU CONJUGATE GRADIENT SOLVER ---

        private static float[] SolveWithCpu(SparseMatrix A, float[] b, float tolerance = 1e-6f, int maxIterations = 1000)
        {
            int n = b.Length;
            var x = new float[n];
            var r = new float[n];
            Array.Copy(b, r, n);
            var p = new float[n];
            Array.Copy(r, p, n);

            float rsold = Dot(r, r);

            for (int i = 0; i < maxIterations; i++)
            {
                var Ap = A.Multiply(p);
                float alpha = rsold / Dot(p, Ap);

                Axpy(x, p, alpha);
                Axpy(r, Ap, -alpha);

                float rsnew = Dot(r, r);
                if (Math.Sqrt(rsnew) < tolerance) break;

                float beta = rsnew / rsold;
                for (int j = 0; j < n; j++) p[j] = r[j] + beta * p[j];
                
                rsold = rsnew;
            }
            return x;
        }

        private static float Dot(float[] a, float[] b)
        {
            double sum = 0;
            Parallel.For(0, a.Length, () => 0.0, (i, loop, partialSum) =>
            {
                return partialSum + (a[i] * b[i]);
            },
            (partialSum) =>
            {
                lock (b) sum += partialSum;
            });
            return (float)sum;
        }

        private static void Axpy(float[] y, float[] x, float alpha)
        {
            Parallel.For(0, y.Length, i => y[i] += alpha * x[i]);
        }
        
        // --- GPU OPENCL SOLVER ---

        private static float[] SolveWithGpu(SparseMatrix A, float[] b, float tolerance = 1e-6f, int maxIterations = 1000)
        {
            try
            {
                return OpenCLContext.Solve(A, b, tolerance, maxIterations);
            }
            catch(Exception ex)
            {
                Logger.LogError($"[OpenCL Solver] GPU execution failed: {ex.Message}. Falling back to CPU.");
                OpenCLContext.IsAvailable = false; // Disable for subsequent calls
                return SolveWithCpu(A, b, tolerance, maxIterations);
            }
        }
    }
    
    // --- UTILITY CLASSES ---

    public class SparseMatrix
    {
        private readonly Dictionary<int, float>[] _rows;
        public int Size { get; }

        public SparseMatrix(int size)
        {
            Size = size;
            _rows = new Dictionary<int, float>[size];
            for (int i = 0; i < size; i++) _rows[i] = new Dictionary<int, float>();
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

    internal static class OpenCLContext
    {
        private static readonly Lazy<bool> _isAvailable = new(Initialize);
        public static bool IsAvailable { get; set; } = _isAvailable.Value;

        private static CL _cl;
        private static nint _context, _queue, _program;

        private const string Kernels = @"
            __kernel void spmv(__global const float* values,
                               __global const int* col_indices,
                               __global const int* row_offsets,
                               __global const float* x,
                               __global float* y,
                               const int N) {
                int i = get_global_id(0);
                if (i >= N) return;
                float sum = 0.0f;
                int start = row_offsets[i];
                int end = row_offsets[i+1];
                for (int j = start; j < end; ++j) {
                    sum += values[j] * x[col_indices[j]];
                }
                y[i] = sum;
            }

            __kernel void axpy(__global float* y, __global const float* x, const float alpha) {
                int i = get_global_id(0);
                y[i] += alpha * x[i];
            }

            __kernel void vec_update(__global float* p, __global const float* r, const float beta) {
                int i = get_global_id(0);
                p[i] = r[i] + beta * p[i];
            }
        ";

        private static bool Initialize()
        {
            try
            {
                _cl = CL.GetApi();
                unsafe
                {
                    // Basic setup: find first available GPU
                    uint numPlatforms;
                    _cl.GetPlatformIDs(0, null, &numPlatforms);
                    if (numPlatforms == 0) return false;
                    var platforms = new nint[numPlatforms];
                    
                    // --- FIX: Pin the array to get a stable pointer for the native call ---
                    fixed (nint* pPlatforms = platforms)
                    {
                        _cl.GetPlatformIDs(numPlatforms, pPlatforms, null);
                    }
                    
                    nint device = 0;
                    foreach(var p in platforms)
                    {
                        uint numDevices;
                        if (_cl.GetDeviceIDs(p, DeviceType.Gpu, 0, null, &numDevices) == 0 && numDevices > 0)
                        {
                            _cl.GetDeviceIDs(p, DeviceType.Gpu, 1, &device, null);
                            break;
                        }
                    }
                    if (device == 0) return false;

                    int err;
                    _context = _cl.CreateContext(null, 1, &device, null, null, &err);
                    
                    // --- FIX: Use type-safe enum to resolve ambiguity ---
                    _queue = _cl.CreateCommandQueue(_context, device, CommandQueueProperties.None, &err);

                    var sources = new[] { Kernels };
                    _program = _cl.CreateProgramWithSource(_context, 1, sources, null, &err);

                    // --- FIX: Pass string.Empty instead of null to resolve ambiguity ---
                    _cl.BuildProgram(_program, 1, &device, string.Empty, null, null);
                }
                Logger.Log("[OpenCL] GPU context initialized successfully.");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[OpenCL] Initialization failed: {ex.Message}");
                return false;
            }
        }
        
        public static float[] Solve(SparseMatrix A, float[] b, float tolerance, int maxIterations)
        {
             // 1. Convert matrix to CSR format
            var (values, colIndices, rowOffsets) = ToCsr(A);
            int n = b.Length;

            unsafe
            {
                // 2. Create buffers
                int err;
                var d_values = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)values.Length * sizeof(float), values.AsSpan(), &err);
                var d_colIndices = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)colIndices.Length * sizeof(int), colIndices.AsSpan(), &err);
                var d_rowOffsets = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)rowOffsets.Length * sizeof(int), rowOffsets.AsSpan(), &err);

                var d_x = _cl.CreateBuffer(_context, MemFlags.ReadWrite, (nuint)n * sizeof(float), null, &err);
                var d_r = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)n * sizeof(float), b.AsSpan(), &err);
                var d_p = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)n * sizeof(float), b.AsSpan(), &err);
                var d_Ap = _cl.CreateBuffer(_context, MemFlags.ReadWrite, (nuint)n * sizeof(float), null, &err);

                // 3. Create kernels
                var spmvKernel = _cl.CreateKernel(_program, "spmv", &err);
                var axpyKernel = _cl.CreateKernel(_program, "axpy", &err);
                var vecUpdateKernel = _cl.CreateKernel(_program, "vec_update", &err);

                // 4. Conjugate Gradient Loop
                var r_h = new float[n];

                // --- FIX: Cast null to pointer type to resolve ambiguity ---
                 _cl.EnqueueReadBuffer(_queue, d_r, true, 0, (nuint)n * sizeof(float), r_h.AsSpan(), 0, (nint*)null, (nint*)null);
                float rsold = r_h.Sum(v => v * v);

                for (int i = 0; i < maxIterations; i++)
                {
                    // Ap = A * p
                    _cl.SetKernelArg(spmvKernel, 0, (nuint)IntPtr.Size, &d_values);
                    _cl.SetKernelArg(spmvKernel, 1, (nuint)IntPtr.Size, &d_colIndices);
                    _cl.SetKernelArg(spmvKernel, 2, (nuint)IntPtr.Size, &d_rowOffsets);
                    _cl.SetKernelArg(spmvKernel, 3, (nuint)IntPtr.Size, &d_p);
                    _cl.SetKernelArg(spmvKernel, 4, (nuint)IntPtr.Size, &d_Ap);
                    _cl.SetKernelArg(spmvKernel, 5, (nuint)sizeof(int), &n);
                    nuint gws = (nuint)n;
                    // --- FIX: Cast nulls to pointer types ---
                    _cl.EnqueueNdrangeKernel(_queue, spmvKernel, 1, (nuint*)null, &gws, (nuint*)null, 0, (nint*)null, (nint*)null);

                    // alpha = rsold / (p' * Ap)
                    var p_h = new float[n]; var Ap_h = new float[n];
                    // --- FIX: Cast nulls to pointer types ---
                    _cl.EnqueueReadBuffer(_queue, d_p, true, 0, (nuint)n * sizeof(float), p_h.AsSpan(), 0, (nint*)null, (nint*)null);
                    _cl.EnqueueReadBuffer(_queue, d_Ap, true, 0, (nuint)n * sizeof(float), Ap_h.AsSpan(), 0, (nint*)null, (nint*)null);
                    float pAp = p_h.Zip(Ap_h, (v1, v2) => v1 * v2).Sum();
                    float alpha = rsold / pAp;
                    
                    // x = x + alpha * p
                    _cl.SetKernelArg(axpyKernel, 0, (nuint)IntPtr.Size, &d_x);
                    _cl.SetKernelArg(axpyKernel, 1, (nuint)IntPtr.Size, &d_p);
                    _cl.SetKernelArg(axpyKernel, 2, (nuint)sizeof(float), &alpha);
                    // --- FIX: Cast nulls to pointer types ---
                    _cl.EnqueueNdrangeKernel(_queue, axpyKernel, 1, (nuint*)null, &gws, (nuint*)null, 0, (nint*)null, (nint*)null);

                    // r = r - alpha * Ap
                    float nalpha = -alpha;
                    _cl.SetKernelArg(axpyKernel, 0, (nuint)IntPtr.Size, &d_r);
                    _cl.SetKernelArg(axpyKernel, 1, (nuint)IntPtr.Size, &d_Ap);
                    _cl.SetKernelArg(axpyKernel, 2, (nuint)sizeof(float), &nalpha);
                    // --- FIX: Cast nulls to pointer types ---
                    _cl.EnqueueNdrangeKernel(_queue, axpyKernel, 1, (nuint*)null, &gws, (nuint*)null, 0, (nint*)null, (nint*)null);
                    
                    // rsnew = r' * r
                    // --- FIX: Cast nulls to pointer types ---
                    _cl.EnqueueReadBuffer(_queue, d_r, true, 0, (nuint)n * sizeof(float), r_h.AsSpan(), 0, (nint*)null, (nint*)null);
                    float rsnew = r_h.Sum(v => v * v);

                    if (Math.Sqrt(rsnew) < tolerance) break;

                    // p = r + (rsnew / rsold) * p
                    float beta = rsnew / rsold;
                    _cl.SetKernelArg(vecUpdateKernel, 0, (nuint)IntPtr.Size, &d_p);
                    _cl.SetKernelArg(vecUpdateKernel, 1, (nuint)IntPtr.Size, &d_r);
                    _cl.SetKernelArg(vecUpdateKernel, 2, (nuint)sizeof(float), &beta);
                    // --- FIX: Cast nulls to pointer types ---
                    _cl.EnqueueNdrangeKernel(_queue, vecUpdateKernel, 1, (nuint*)null, &gws, (nuint*)null, 0, (nint*)null, (nint*)null);
                    
                    rsold = rsnew;
                }
                
                // Read result back
                var x_h = new float[n];
                // --- FIX: Cast nulls to pointer types ---
                _cl.EnqueueReadBuffer(_queue, d_x, true, 0, (nuint)n * sizeof(float), x_h.AsSpan(), 0, (nint*)null, (nint*)null);

                // Cleanup
                _cl.ReleaseMemObject(d_values); _cl.ReleaseMemObject(d_colIndices); _cl.ReleaseMemObject(d_rowOffsets);
                _cl.ReleaseMemObject(d_x); _cl.ReleaseMemObject(d_r); _cl.ReleaseMemObject(d_p); _cl.ReleaseMemObject(d_Ap);
                _cl.ReleaseKernel(spmvKernel); _cl.ReleaseKernel(axpyKernel); _cl.ReleaseKernel(vecUpdateKernel);

                return x_h;
            }
        }

        private static (float[] values, int[] colIndices, int[] rowOffsets) ToCsr(SparseMatrix A)
        {
            var values = new List<float>();
            var colIndices = new List<int>();
            var rowOffsets = new int[A.Size + 1];
            
            for (int i = 0; i < A.Size; i++)
            {
                rowOffsets[i] = values.Count;
                foreach (var (col, value) in A.GetRow(i).OrderBy(kv => kv.Key))
                {
                    values.Add(value);
                    colIndices.Add(col);
                }
            }
            rowOffsets[A.Size] = values.Count;
            return (values.ToArray(), colIndices.ToArray(), rowOffsets);
        }
    }
}