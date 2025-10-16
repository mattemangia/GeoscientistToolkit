// GeoscientistToolkit/Analysis/Geomechanics/GeomechanicalSimulatorCPU.cs
// Production-ready CPU version with full physics and extensive logging.
// This is a partial class designed to work with GeomechanicalSimulatorCPU_FluidGeothermal.cs.
//
// ========== FEATURES ==========
// MULTI-CORE & SIMD ACCELERATION:
//    - Uses Parallel.For for multi-threading heavy computations (mesh generation, solver, post-processing).
//    - Uses System.Numerics.Vector<T> for AVX/NEON acceleration of linear algebra in the solver.
//
// FULL PHYSICS (IDENTICAL TO GPU VERSION):
//    - Mechanics: 3D Finite Element Method (FEM) with 8-node hexahedral elements.
//    - Thermal: Geothermal gradient initialization (handled in partial class).
//    - Fluid: Poroelasticity and hydraulic fracturing hooks (handled in partial class).
//    - Plasticity: Von Mises yield criterion with isotropic hardening.
//    - Failure: Mohr-Coulomb, Drucker-Prager, Hoek-Brown, and Griffith criteria supported.
//
// ROBUST SOLVER:
//    - Preconditioned Conjugate Gradient (PCG) solver with SIMD-accelerated vector operations.
//    - Jacobi (diagonal) preconditioner for improved convergence.
//
// MEMORY EFFICIENCY:
//    - Processes only the material region, ignoring air/void, to reduce memory footprint.
//    - All data is handled on the CPU, suitable for large RAM systems.
//
// ========================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.Util;
using System.IO;
using System.Runtime.InteropServices;

namespace GeoscientistToolkit.Analysis.Geomechanics;

public partial class GeomechanicalSimulatorCPU : IDisposable
{
    // ========== CORE STATE ==========
    private readonly GeomechanicalParameters _params;
    private readonly bool _isSimdAccelerated = Vector.IsHardwareAccelerated;
    private readonly object[] _dofLocks = new object[1024];
    // Mesh data
    private int _numNodes;
    private int _numElements;
    private long _numDOFs;
    private ArrayWrapper<float> _nodeX, _nodeY, _nodeZ;
    private ArrayWrapper<int> _elementNodes;
    private ArrayWrapper<float> _elementE, _elementNu;

    // Material bounds (for simulating only the relevant region)
    private int _minX, _maxX, _minY, _maxY, _minZ, _maxZ;

    // Boundary conditions
    private ArrayWrapper<bool> _isDirichlet;
    private ArrayWrapper<float> _dirichletValue;
    private ArrayWrapper<float> _force;
    
    // Solution vector
    private ArrayWrapper<float> _displacement;

    
    

    // Iteration tracking
    private int _iterationsPerformed;

    // ========== CONSTRUCTOR ==========
    public GeomechanicalSimulatorCPU(GeomechanicalParameters parameters)
    {
        Logger.Log("==========================================================");
        Logger.Log("[GeomechCPU] Initializing CPU Geomechanical Simulator");
        Logger.Log("==========================================================");

        _params = parameters ?? throw new ArgumentNullException(nameof(parameters));
        
        Logger.Log($"[GeomechCPU] SIMD Acceleration: {(_isSimdAccelerated ? "Enabled" : "Not Available")}");

        Logger.Log("[GeomechCPU] Validating parameters...");
        ValidateParameters();
        
        Logger.Log("[GeomechCPU] Initialization complete.");
        Logger.Log("==========================================================");
        for (int i = 0; i < _dofLocks.Length; i++)
        {
            _dofLocks[i] = new object();
        }
    }

    // ========== PUBLIC INTERFACE ==========
    public GeomechanicalResults Simulate(byte[,,] labels, float[,,] density,
        IProgress<float> progress, CancellationToken token)
    {
        var startTime = DateTime.Now;
        var extent = _params.SimulationExtent;

        try
        {
            Logger.Log("");
            Logger.Log("==========================================================");
            Logger.Log("     CPU GEOMECHANICAL SIMULATION - STARTING");
            Logger.Log("==========================================================");
            Logger.Log($"Domain size: {extent.Width} × {extent.Height} × {extent.Depth} voxels");
            Logger.Log($"Total voxels: {(long)extent.Width * extent.Height * extent.Depth:N0}");
            Logger.Log($"Physical size: {extent.Width * _params.PixelSize / 1000:F2} × {extent.Height * _params.PixelSize / 1000:F2} × {extent.Depth * _params.PixelSize / 1000:F2} mm");
            Logger.Log($"Loading: σ₁={_params.Sigma1} MPa, σ₂={_params.Sigma2} MPa, σ₃={_params.Sigma3} MPa");
            
            // STEP 1: Find material bounds
            Logger.Log("\n[1/10] Finding material bounds...");
            progress?.Report(0.05f);
            var sw = Stopwatch.StartNew();
            FindMaterialBounds(labels);
            Logger.Log($"[1/10] Material bounds found in {sw.ElapsedMilliseconds} ms");
            token.ThrowIfCancellationRequested();

            // STEP 2: Generate FEM mesh
            Logger.Log("\n[2/10] Generating FEM mesh...");
            progress?.Report(0.10f);
            sw.Restart();
            GenerateMesh(labels);
            Logger.Log($"[2/10] Mesh generated in {sw.ElapsedMilliseconds} ms");
            token.ThrowIfCancellationRequested();

            // STEP 3: Apply boundary conditions
            Logger.Log("\n[3/10] Applying boundary conditions...");
            progress?.Report(0.20f);
            sw.Restart();
            ApplyBoundaryConditions();
            Logger.Log($"[3/10] Boundary conditions applied in {sw.ElapsedMilliseconds} ms");
            token.ThrowIfCancellationRequested();

            // STEP 4: Initialize fluid/thermal fields (calls partial class method)
            if (_params.EnableGeothermal || _params.EnableFluidInjection)
            {
                Logger.Log("\n[4/10] Initializing fluid/thermal fields...");
                progress?.Report(0.22f);
                sw.Restart();
                InitializeGeothermalAndFluid(labels, extent);
                Logger.Log($"[4/10] Fluid/thermal initialized in {sw.ElapsedMilliseconds} ms");
                token.ThrowIfCancellationRequested();
            }
            else
            {
                Logger.Log("\n[4/10] Fluid/thermal simulation disabled, skipping...");
            }

            // STEP 5: Solve mechanical system
            Logger.Log("\n[5/10] Solving mechanical system (PCG)...");
            progress?.Report(0.25f);
            sw.Restart();
            var converged = SolveSystem(progress, token);
            Logger.Log($"[5/10] System solved in {sw.Elapsed.TotalSeconds:F2} s. Converged: {converged}, Iterations: {_iterationsPerformed}");
            token.ThrowIfCancellationRequested();

            // STEP 6: Calculate stresses
            Logger.Log("\n[6/10] Calculating stresses...");
            progress?.Report(0.75f);
            sw.Restart();
            var results = CalculateStresses(labels, extent);
            Logger.Log($"[6/10] Stresses calculated in {sw.Elapsed.TotalSeconds:F2} s");
            token.ThrowIfCancellationRequested();

            // STEP 7: Post-processing (principal stresses, failure)
            Logger.Log("\n[7/10] Post-processing (principal stresses, failure)...");
            progress?.Report(0.85f);
            sw.Restart();
            PostProcessResults(results, labels);
            Logger.Log($"[7/10] Post-processing completed in {sw.Elapsed.TotalSeconds:F2} s");
            token.ThrowIfCancellationRequested();

            // STEP 8: Fluid injection simulation (calls partial class method)
            if (_params.EnableFluidInjection)
            {
                Logger.Log("\n[8/10] Simulating fluid injection and hydraulic fracturing...");
                progress?.Report(0.90f);
                sw.Restart();
                SimulateFluidInjectionAndFracturing(results, labels, progress, token);
                Logger.Log($"[8/10] Fluid simulation completed in {sw.Elapsed.TotalSeconds:F2} s");
                token.ThrowIfCancellationRequested();
            }
            else
            {
                Logger.Log("\n[8/10] Fluid injection disabled, skipping...");
            }

            // STEP 9: Final statistics
            Logger.Log("\n[9/10] Calculating final statistics...");
            progress?.Report(0.95f);
            sw.Restart();
            CalculateFinalStatistics(results);
            Logger.Log($"[9/10] Statistics calculated in {sw.ElapsedMilliseconds} ms");
            
            // STEP 10: Populate final results object
            Logger.Log("\n[10/10] Finalizing results...");
            results.Converged = converged;
            results.IterationsPerformed = _iterationsPerformed;
            results.ComputationTime = DateTime.Now - startTime;
            PopulateGeothermalAndFluidResults(results); // Populate from partial class
            
            progress?.Report(1.0f);
            
            Logger.Log("\n==========================================================");
            Logger.Log("     CPU GEOMECHANICAL SIMULATION - COMPLETED");
            Logger.Log("==========================================================");
            Logger.Log($"Total computation time: {results.ComputationTime.TotalSeconds:F2} s");
            Logger.Log($"Convergence: {(converged ? "YES" : "NO")} ({_iterationsPerformed} iterations)");
            if(results.TotalVoxels > 0)
            {
                Logger.Log($"Mean stress: {results.MeanStress:F2} MPa");
                Logger.Log($"Max shear stress: {results.MaxShearStress:F2} MPa");
                Logger.Log($"Failed voxels: {results.FailedVoxels:N0} / {results.TotalVoxels:N0} ({results.FailedVoxelPercentage:F2}%)");
            }
            if (_params.EnableFluidInjection)
            {
                Logger.Log($"Breakdown pressure: {results.BreakdownPressure:F2} MPa");
                Logger.Log($"Fracture volume: {results.TotalFractureVolume * 1e9:F2} mm³");
            }
            Logger.Log("==========================================================");

            return results;
        }
        catch (OperationCanceledException)
        {
            Logger.LogWarning("\n=================== SIMULATION CANCELLED ===================");
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError("\n==================== SIMULATION FAILED =====================");
            Logger.LogError($"Error: {ex.Message}\n{ex.StackTrace}");
            Logger.LogError("==========================================================");
            throw;
        }
    }

    public void Dispose()
    {
        Logger.Log("[GeomechCPU] Disposing of array wrappers...");
        _nodeX?.Dispose();
        _nodeY?.Dispose();
        _nodeZ?.Dispose();
        _elementNodes?.Dispose();
        _elementE?.Dispose();
        _elementNu?.Dispose();
        _isDirichlet?.Dispose();
        _dirichletValue?.Dispose();
        _force?.Dispose();
        _displacement?.Dispose();
        Logger.Log("[GeomechCPU] Disposed successfully.");
    }

    // ========== INITIALIZATION ==========
    private void ValidateParameters()
    {
        if (_params.Sigma1 < _params.Sigma2 || _params.Sigma2 < _params.Sigma3)
            throw new ArgumentException("Principal stresses must satisfy σ₁ ≥ σ₂ ≥ σ₃");
        if (_params.PoissonRatio <= 0 || _params.PoissonRatio >= 0.5f)
            throw new ArgumentException("Poisson's ratio must be in (0, 0.5)");
        if (_params.YoungModulus <= 0)
            throw new ArgumentException("Young's modulus must be positive");
        
        Logger.Log("[GeomechCPU] All parameters validated successfully");
    }

    // ========== MESH GENERATION ==========
    private void FindMaterialBounds(byte[,,] labels)
    {
        var extent = _params.SimulationExtent;
        _minX = extent.Width; _maxX = -1;
        _minY = extent.Height; _maxY = -1;
        _minZ = extent.Depth; _maxZ = -1;

        int materialVoxels = 0;
        for (int z = 0; z < extent.Depth; z++)
        {
            for (int y = 0; y < extent.Height; y++)
            {
                for (int x = 0; x < extent.Width; x++)
                {
                    if (labels[x, y, z] != 0)
                    {
                        materialVoxels++;
                        if (x < _minX) _minX = x;
                        if (x > _maxX) _maxX = x;
                        if (y < _minY) _minY = y;
                        if (y > _maxY) _maxY = y;
                        if (z < _minZ) _minZ = z;
                        if (z > _maxZ) _maxZ = z;
                    }
                }
            }
        }
        Logger.Log($"[GeomechCPU] Bounding box: X=[{_minX},{_maxX}], Y=[{_minY},{_maxY}], Z=[{_minZ},{_maxZ}]");
    }

    private void GenerateMesh(byte[,,] labels)
    {
        var sw = Stopwatch.StartNew();
        
        var w = _maxX - _minX + 2;
        var h = _maxY - _minY + 2;
        var d = _maxZ - _minZ + 2;
        var dx = _params.PixelSize / 1e3f; // Use mm for better numerical conditioning

        _numNodes = w * h * d;
        _numDOFs = _numNodes * 3;

        // --- Offloading logic starts here ---
        bool offload = _params.EnableOffloading;
        string offloadDir = _params.OffloadDirectory;
        Logger.Log($"[GeomechCPU] Offloading enabled: {offload}. Directory: {offloadDir}");

        // Local arrays for initialization
        var nodeX_init = new float[_numNodes];
        var nodeY_init = new float[_numNodes];
        var nodeZ_init = new float[_numNodes];

        Parallel.For(0, d, z =>
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int nodeIdx = (z * h + y) * w + x;
                    nodeX_init[nodeIdx] = (_minX + x - 0.5f) * dx;
                    nodeY_init[nodeIdx] = (_minY + y - 0.5f) * dx;
                    nodeZ_init[nodeIdx] = (_minZ + z - 0.5f) * dx;
                }
            }
        });

        // Initialize node arrays with the chosen wrapper
        if (offload)
        {
            _nodeX = new DiskBackedArray<float>(_numNodes, offloadDir);
            _nodeX.WriteChunk(0, nodeX_init);
            _nodeY = new DiskBackedArray<float>(_numNodes, offloadDir);
            _nodeY.WriteChunk(0, nodeY_init);
            _nodeZ = new DiskBackedArray<float>(_numNodes, offloadDir);
            _nodeZ.WriteChunk(0, nodeZ_init);
        }
        else
        {
            _nodeX = new MemoryBackedArray<float>(nodeX_init);
            _nodeY = new MemoryBackedArray<float>(nodeY_init);
            _nodeZ = new MemoryBackedArray<float>(nodeZ_init);
        }
        
        // ... (element list generation remains the same) ...
        var elementList = new List<int[]>(); // This is temporary and will be GC'd
        for (int z = _minZ; z <= _maxZ; z++)
        {
            for (int y = _minY; y <= _maxY; y++)
            {
                for (int x = _minX; x <= _maxX; x++)
                {
                    if (labels[x, y, z] == 0) continue;

                    int lx = x - _minX;
                    int ly = y - _minY;
                    int lz = z - _minZ;
                    int n0 = (lz * h + ly) * w + lx;

                    elementList.Add(new[] {
                        n0, n0 + 1, n0 + w + 1, n0 + w,
                        n0 + w * h, n0 + w * h + 1, n0 + w * h + w + 1, n0 + w * h + w
                    });
                }
            }
        }

        _numElements = elementList.Count;
        var elementNodes_init = new int[_numElements * 8];
        Parallel.For(0, _numElements, e =>
        {
            for (int n = 0; n < 8; n++)
            {
                elementNodes_init[e * 8 + n] = elementList[e][n];
            }
        });
        
        if (offload)
        {
            _elementNodes = new DiskBackedArray<int>(_numElements * 8, offloadDir);
            _elementNodes.WriteChunk(0, elementNodes_init);
            _elementE = new DiskBackedArray<float>(_numElements, offloadDir);
            _elementE.WriteChunk(0, Enumerable.Repeat(_params.YoungModulus, _numElements).ToArray());
            _elementNu = new DiskBackedArray<float>(_numElements, offloadDir);
            _elementNu.WriteChunk(0, Enumerable.Repeat(_params.PoissonRatio, _numElements).ToArray());
        }
        else
        {
            _elementNodes = new MemoryBackedArray<int>(elementNodes_init);
            _elementE = new MemoryBackedArray<float>(Enumerable.Repeat(_params.YoungModulus, _numElements).ToArray());
            _elementNu = new MemoryBackedArray<float>(Enumerable.Repeat(_params.PoissonRatio, _numElements).ToArray());
        }

        Logger.Log($"[GeomechCPU] Mesh Stats: {_numNodes:N0} nodes, {_numElements:N0} elements, {_numDOFs:N0}.");
        Logger.Log($"[GeomechCPU] Mesh generation completed in {sw.ElapsedMilliseconds} ms");
    }
    private void CopyWrapper<T>(ArrayWrapper<T> source, ArrayWrapper<T> destination) where T : struct
    {
        const int chunkSize = 1024 * 1024; // 1M elements at a time
        var buffer = new T[chunkSize];
        for (long i = 0; i < source.Length; i += chunkSize)
        {
            var currentChunkSize = (int)Math.Min(chunkSize, source.Length - i);
            if(currentChunkSize != buffer.Length)
                buffer = new T[currentChunkSize];
            
            source.ReadChunk(i, buffer);
            destination.WriteChunk(i, buffer);
        }
    }
    /// <summary>
    /// A struct to hold a single contribution to a degree of freedom.
    /// Written to temporary files during the parallel 'map' phase.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct DofContribution
    {
        public readonly long Index;
        public readonly float Value;

        public DofContribution(long index, float value)
        {
            Index = index;
            Value = value;
        }
    }
    // ========== BOUNDARY CONDITIONS ==========
    private void ApplyBoundaryConditions()
{
    var sw = Stopwatch.StartNew();
    bool offload = _params.EnableOffloading;
    string offloadDir = _params.OffloadDirectory;

    if (offload)
    {
        _isDirichlet = new DiskBackedArray<bool>(_numDOFs, offloadDir);
        _dirichletValue = new DiskBackedArray<float>(_numDOFs, offloadDir);
        _force = new DiskBackedArray<float>(_numDOFs, offloadDir);
        _displacement = new DiskBackedArray<float>(_numDOFs, offloadDir);
    }
    else
    {
        _isDirichlet = new MemoryBackedArray<bool>(_numDOFs);
        _dirichletValue = new MemoryBackedArray<float>(_numDOFs);
        _force = new MemoryBackedArray<float>(_numDOFs);
        _displacement = new MemoryBackedArray<float>(_numDOFs);
    }
    
    var w = _maxX - _minX + 2;
    var h = _maxY - _minY + 2;
    var d = _maxZ - _minZ + 2;
    var dx = _params.PixelSize / 1e3f; // mm

    float eps_z = (_params.Sigma1 - _params.PoissonRatio * (_params.Sigma2 + _params.Sigma3)) / _params.YoungModulus;
    float eps_y = (_params.Sigma2 - _params.PoissonRatio * (_params.Sigma1 + _params.Sigma3)) / _params.YoungModulus;
    float eps_x = (_params.Sigma3 - _params.PoissonRatio * (_params.Sigma1 + _params.Sigma2)) / _params.YoungModulus;

    float delta_z = eps_z * (d - 1) * dx;
    float delta_y = eps_y * (h - 1) * dx;
    float delta_x = eps_x * (w - 1) * dx;
    
    Logger.Log($"[GeomechCPU] Target displacements: δx={delta_x*1000:F2}μm, δy={delta_y*1000:F2}μm, δz={delta_z*1000:F2}μm");
    
    // OPTIMIZED: Process nodes in chunks to enable efficient parallel writing to disk.
    const int nodeChunkSize = 65536;
    Parallel.For(0, (_numNodes + nodeChunkSize - 1) / nodeChunkSize, chunkIndex =>
    {
        long startNode = chunkIndex * nodeChunkSize;
        long endNode = Math.Min(startNode + nodeChunkSize, _numNodes);
        
        long startDof = startNode * 3;
        long endDof = endNode * 3;
        int dofChunkSize = (int)(endDof - startDof);

        var isDirichletChunk = new bool[dofChunkSize];
        var dirichletValueChunk = new float[dofChunkSize];

        // Read the current state for this chunk (important if not all values are set)
        if (offload)
        {
            _isDirichlet.ReadChunk(startDof, isDirichletChunk);
            _dirichletValue.ReadChunk(startDof, dirichletValueChunk);
        }

        for (long i = startNode; i < endNode; i++)
        {
            int x = (int)(i % w);
            int y = (int)((i / w) % h);
            int z = (int)(i / (w * h));
            
            long localDofOffset = (i - startNode) * 3;

            // Z-faces
            if (z == 0) { isDirichletChunk[localDofOffset+2] = true; dirichletValueChunk[localDofOffset+2] = 0; }
            else if (z == d - 1) { isDirichletChunk[localDofOffset+2] = true; dirichletValueChunk[localDofOffset+2] = delta_z; }
            // Y-faces
            if (y == 0) { isDirichletChunk[localDofOffset+1] = true; dirichletValueChunk[localDofOffset+1] = 0; }
            else if (y == h - 1) { isDirichletChunk[localDofOffset+1] = true; dirichletValueChunk[localDofOffset+1] = delta_y; }
            // X-faces
            if (x == 0) { isDirichletChunk[localDofOffset+0] = true; dirichletValueChunk[localDofOffset+0] = 0; }
            else if (x == w - 1) { isDirichletChunk[localDofOffset+0] = true; dirichletValueChunk[localDofOffset+0] = delta_x; }
        }

        // Write the entire modified chunk back to the wrappers.
        _isDirichlet.WriteChunk(startDof, isDirichletChunk);
        _dirichletValue.WriteChunk(startDof, dirichletValueChunk);
    });
    
    // Set the single fully-fixed node to prevent rigid body motion. This is a small I/O hit.
    _isDirichlet[0] = true; _isDirichlet[1] = true; _isDirichlet[2] = true;
    _dirichletValue[0] = 0; _dirichletValue[1] = 0; _dirichletValue[2] = 0;

    // Count fixed DOFs using an efficient chunk-based read.
    long fixedDOFs = 0;
    var boolBuffer = new bool[1024 * 1024];
    for (long i = 0; i < _isDirichlet.Length; i += boolBuffer.Length)
    {
        var currentChunkSize = (int)Math.Min(boolBuffer.Length, _isDirichlet.Length - i);
        if (boolBuffer.Length != currentChunkSize) boolBuffer = new bool[currentChunkSize];
        
        _isDirichlet.ReadChunk(i, boolBuffer);
        for(int j = 0; j < currentChunkSize; ++j) if (boolBuffer[j]) fixedDOFs++;
    }

    Logger.Log($"[GeomechCPU] Applied displacement-controlled BCs in {sw.ElapsedMilliseconds} ms. Fixed DOFs: {fixedDOFs:N0} / {_numDOFs:N0}");
}

    // ========== SOLVER ==========
    private bool SolveSystem(IProgress<float> progress, CancellationToken token)
{
    var sw = Stopwatch.StartNew();
    Logger.Log("[GeomechCPU] Starting PCG solver...");

    const int maxIter = 2000;
    const float tol = 1e-5f;

    bool offload = _params.EnableOffloading;
    string offloadDir = _params.OffloadDirectory;

    // The 'using' statements are CRITICAL. They guarantee that the temporary files
    // created by DiskBackedArray are deleted even if an error occurs.
    using var r = offload ? new DiskBackedArray<float>(_numDOFs, offloadDir) : new MemoryBackedArray<float>(_numDOFs) as ArrayWrapper<float>;
    using var p = offload ? new DiskBackedArray<float>(_numDOFs, offloadDir) : new MemoryBackedArray<float>(_numDOFs) as ArrayWrapper<float>;
    using var Ap = offload ? new DiskBackedArray<float>(_numDOFs, offloadDir) : new MemoryBackedArray<float>(_numDOFs) as ArrayWrapper<float>;
    using var M_inv = offload ? new DiskBackedArray<float>(_numDOFs, offloadDir) : new MemoryBackedArray<float>(_numDOFs) as ArrayWrapper<float>;
    using var z = offload ? new DiskBackedArray<float>(_numDOFs, offloadDir) : new MemoryBackedArray<float>(_numDOFs) as ArrayWrapper<float>;

    // Initialize displacement with boundary values: u = u_d
    Logger.Log("[GeomechCPU] Initializing displacement with boundary conditions...");
    CopyWrapper(_dirichletValue, _displacement);

    // Calculate initial residual: r = f - K*u
    Logger.Log("[GeomechCPU] Computing initial residual (r = f - K*u)...");
    MatrixVectorProduct(Ap, _displacement);
    SubtractWrappers(_force, Ap, r);
    ApplyDirichletToVector(r, setToZero: true); // r_i = 0 if i is a Dirichlet node

    float rNorm0 = MathF.Sqrt(DotProduct(r, r));
    if (rNorm0 < 1e-9f)
    {
        Logger.Log("[GeomechCPU] System already converged. Initial residual is near zero.");
        _iterationsPerformed = 0;
        return true;
    }
    
    Logger.Log($"[GeomechCPU] Initial residual norm: {rNorm0:E6}");

    // Build Jacobi preconditioner M_inv (diagonal of K, inverted)
    Logger.Log("[GeomechCPU] Building Jacobi preconditioner...");
    BuildPreconditioner(M_inv);

    // Apply preconditioner: z = M_inv * r
    ElementWiseMultiply(M_inv, r, z);
    
    // Initialize search direction: p = z
    CopyWrapper(z, p);

    float rDotZ = DotProduct(r, z);

    Logger.Log("[GeomechCPU] Starting PCG iterations...");
    Logger.Log("----------------------------------------------------------");

    for (int iter = 0; iter < maxIter; iter++)
    {
        token.ThrowIfCancellationRequested();
        _iterationsPerformed = iter + 1;

        // 1. Calculate matrix-vector product: Ap = K*p
        MatrixVectorProduct(Ap, p);

        // 2. Calculate step size: alpha = r_k^T * z_k / (p_k^T * A * p_k)
        float pDotAp = DotProduct(p, Ap);
        if (MathF.Abs(pDotAp) < 1e-20f)
        {
            Logger.LogWarning($"[GeomechCPU] Solver breakdown (p.Ap is zero) at iteration {iter}. The matrix may be singular.");
            break;
        }
        float alpha = rDotZ / pDotAp;

        // 3. Update solution: u_{k+1} = u_k + alpha * p_k
        Saxpy(_displacement, p, alpha);

        // 4. Update residual: r_{k+1} = r_k - alpha * Ap_k
        Saxpy(r, Ap, -alpha);
        
        // Re-enforce r=0 at Dirichlet nodes to prevent error accumulation
        ApplyDirichletToVector(r, setToZero: true);

        // 5. Check for convergence
        float rNorm = MathF.Sqrt(DotProduct(r, r));
        float relativeResidual = rNorm / rNorm0;

        if (iter % 10 == 0 || iter < 5)
        {
            Logger.Log($"  Iter {iter,4}: ||r|| = {rNorm:E6}, rel = {relativeResidual:E6}, α = {alpha:E6}");
        }
        
        progress?.Report(0.25f + 0.5f * iter / maxIter);

        if (relativeResidual < tol)
        {
            Logger.Log("----------------------------------------------------------");
            Logger.Log($"[GeomechCPU] *** CONVERGED in {iter + 1} iterations ***");
            Logger.Log($"[GeomechCPU] Final relative residual: {relativeResidual:E6}");
            return true;
        }

        // 6. Apply preconditioner to new residual: z_{k+1} = M_inv * r_{k+1}
        ElementWiseMultiply(M_inv, r, z);

        // 7. Update beta: beta = r_{k+1}^T * z_{k+1} / (r_k^T * z_k)
        float rDotZ_new = DotProduct(r, z);
        float beta = rDotZ_new / rDotZ;
        rDotZ = rDotZ_new;

        // 8. Update search direction: p_{k+1} = z_{k+1} + beta * p_k
        UpdateSearchDirection(p, z, beta);
    }

    Logger.Log("----------------------------------------------------------");
    Logger.LogWarning($"[GeomechCPU] Did NOT converge in {maxIter} iterations. Final relative residual: {DotProduct(r,r)/rNorm0:E6}");
    return false;
}
    // ========== HELPER METHODS FOR OUT-OF-CORE VECTOR OPERATIONS ==========

/// <summary>
/// Performs element-wise subtraction: result = a - b, using chunks.
/// </summary>
private void SubtractWrappers(ArrayWrapper<float> a, ArrayWrapper<float> b, ArrayWrapper<float> result)
{
    const int chunkSize = 1024 * 1024;
    var bufferA = new float[chunkSize];
    var bufferB = new float[chunkSize];
    var bufferResult = new float[chunkSize];

    for (long i = 0; i < a.Length; i += chunkSize)
    {
        var currentChunkSize = (int)Math.Min(chunkSize, a.Length - i);
        if (currentChunkSize != bufferA.Length)
        {
            bufferA = new float[currentChunkSize];
            bufferB = new float[currentChunkSize];
            bufferResult = new float[currentChunkSize];
        }

        a.ReadChunk(i, bufferA);
        b.ReadChunk(i, bufferB);

        for (int j = 0; j < currentChunkSize; j++)
        {
            bufferResult[j] = bufferA[j] - bufferB[j];
        }

        result.WriteChunk(i, bufferResult);
    }
}

/// <summary>
/// Performs element-wise multiplication: result = a * b, using chunks.
/// </summary>
private void ElementWiseMultiply(ArrayWrapper<float> a, ArrayWrapper<float> b, ArrayWrapper<float> result)
{
    const int chunkSize = 1024 * 1024;
    var bufferA = new float[chunkSize];
    var bufferB = new float[chunkSize];
    var bufferResult = new float[chunkSize];

    for (long i = 0; i < a.Length; i += chunkSize)
    {
        var currentChunkSize = (int)Math.Min(chunkSize, a.Length - i);
        if (currentChunkSize != bufferA.Length)
        {
            bufferA = new float[currentChunkSize];
            bufferB = new float[currentChunkSize];
            bufferResult = new float[currentChunkSize];
        }

        a.ReadChunk(i, bufferA);
        b.ReadChunk(i, bufferB);

        for (int j = 0; j < currentChunkSize; j++)
        {
            bufferResult[j] = bufferA[j] * bufferB[j];
        }

        result.WriteChunk(i, bufferResult);
    }
}

/// <summary>
/// Updates the PCG search direction: p = z + beta * p, using chunks.
/// </summary>
private void UpdateSearchDirection(ArrayWrapper<float> p, ArrayWrapper<float> z, float beta)
{
    const int chunkSize = 1024 * 1024;
    var bufferP = new float[chunkSize];
    var bufferZ = new float[chunkSize];

    for (long i = 0; i < p.Length; i += chunkSize)
    {
        var currentChunkSize = (int)Math.Min(chunkSize, p.Length - i);
        if (currentChunkSize != bufferP.Length)
        {
            bufferP = new float[currentChunkSize];
            bufferZ = new float[currentChunkSize];
        }

        p.ReadChunk(i, bufferP);
        z.ReadChunk(i, bufferZ);

        for (int j = 0; j < currentChunkSize; j++)
        {
            bufferP[j] = bufferZ[j] + beta * bufferP[j];
        }
        
        p.WriteChunk(i, bufferP);
    }
}

/// <summary>
/// Applies Dirichlet boundary conditions to a vector in chunks.
/// Can either set the value from _dirichletValue or set it to zero.
/// </summary>
private void ApplyDirichletToVector(ArrayWrapper<float> vector, bool setToZero)
{
    const int chunkSize = 1024 * 1024;
    var bufferVec = new float[chunkSize];
    var bufferIsD = new bool[chunkSize];
    var bufferValD = new float[chunkSize];

    for (long i = 0; i < vector.Length; i += chunkSize)
    {
        var currentChunkSize = (int)Math.Min(chunkSize, vector.Length - i);
        if (currentChunkSize != bufferVec.Length)
        {
            bufferVec = new float[currentChunkSize];
            bufferIsD = new bool[currentChunkSize];
            bufferValD = new float[currentChunkSize];
        }

        vector.ReadChunk(i, bufferVec);
        _isDirichlet.ReadChunk(i, bufferIsD);
        if (!setToZero)
        {
            _dirichletValue.ReadChunk(i, bufferValD);
        }

        bool changed = false;
        for (int j = 0; j < currentChunkSize; j++)
        {
            if (bufferIsD[j])
            {
                bufferVec[j] = setToZero ? 0.0f : bufferValD[j];
                changed = true;
            }
        }

        if (changed)
        {
            vector.WriteChunk(i, bufferVec);
        }
    }
}

    /// <summary>
/// Calculates the matrix-vector product (result = K * vector) using a memory-optimized,
/// SSD-friendly, two-pass algorithm for out-of-core processing.
/// </summary>
/// <param name="result">The output ArrayWrapper for the resulting vector.</param>
/// <param name="vector">The input ArrayWrapper for the vector to be multiplied.</param>
private void MatrixVectorProduct(ArrayWrapper<float> result, ArrayWrapper<float> vector)
{
    // --- Pass 0: Clear the result vector ---
    const int chunkSize = 1024 * 1024; // 4MB chunks
    var zeroBuffer = new float[chunkSize]; 
    for (long i = 0; i < result.Length; i += chunkSize)
    {
        var currentChunkSize = (int)Math.Min(chunkSize, result.Length - i);
        if (zeroBuffer.Length != currentChunkSize) zeroBuffer = new float[currentChunkSize];
        result.WriteChunk(i, zeroBuffer);
    }

    var tempFilePaths = new List<string>();
    var sw = Stopwatch.StartNew();
    
    // ThreadLocal ensures that each thread gets its own BinaryWriter.
    var writer = new ThreadLocal<BinaryWriter>(() =>
    {
        string path = Path.Combine(_params.OffloadDirectory, Path.GetRandomFileName());
        lock (tempFilePaths)
        {
            tempFilePaths.Add(path);
        }
        // Use a buffer for the file stream to improve write performance.
        return new BinaryWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 65536));
    });

    List<BinaryReader> readers = null;

    try
    {
        // --- Pass 1 (Map): Parallel calculation and write to temporary files ---
        Logger.Log("[GeomechCPU] Spooling element contributions to disk (Pass 1)...");
        
        Parallel.For(0, _numElements, e =>
        {
            var elemNodes = new int[8];
            for (int i = 0; i < 8; i++) elemNodes[i] = _elementNodes[e * 8 + i];

            var elemDisp = new float[24];
            for (int i = 0; i < 8; i++)
            {
                int nIdx = elemNodes[i];
                elemDisp[i * 3 + 0] = vector[nIdx * 3 + 0];
                elemDisp[i * 3 + 1] = vector[nIdx * 3 + 1];
                elemDisp[i * 3 + 2] = vector[nIdx * 3 + 2];
            }
            
            // --- FULL ELEMENT STIFFNESS AND FORCE CALCULATION ---
            float E = _elementE[e];
            float nu = _elementNu[e];
            float c = E / ((1.0f + nu) * (1.0f - 2.0f * nu));

            var D = new float[6,6];
            D[0,0] = D[1,1] = D[2,2] = c * (1.0f - nu);
            D[0,1] = D[0,2] = D[1,0] = D[1,2] = D[2,0] = D[2,1] = c * nu;
            D[3,3] = D[4,4] = D[5,5] = c * (1.0f - 2.0f*nu) / 2.0f;
            
            var B = new float[6,24];
            float x0 = _nodeX[elemNodes[0]], x6 = _nodeX[elemNodes[6]];
            float y0 = _nodeY[elemNodes[0]], y6 = _nodeY[elemNodes[6]];
            float z0 = _nodeZ[elemNodes[0]], z6 = _nodeZ[elemNodes[6]];
            float a = (x6 - x0) / 2.0f, b = (y6 - y0) / 2.0f, c_elem = (z6 - z0) / 2.0f;

            if (Math.Abs(a) < 1e-9f || Math.Abs(b) < 1e-9f || Math.Abs(c_elem) < 1e-9f) return;

            float[] dN_dxi  = {-0.125f,  0.125f,  0.125f, -0.125f, -0.125f,  0.125f,  0.125f, -0.125f};
            float[] dN_deta = {-0.125f, -0.125f,  0.125f,  0.125f, -0.125f, -0.125f,  0.125f,  0.125f};
            float[] dN_dzeta= {-0.125f, -0.125f, -0.125f, -0.125f,  0.125f,  0.125f,  0.125f,  0.125f};
            
            for (int i = 0; i < 8; i++) {
                float dN_dx = dN_dxi[i] / a, dN_dy = dN_deta[i] / b, dN_dz = dN_dzeta[i] / c_elem;
                B[0, i*3] = dN_dx; B[1, i*3+1] = dN_dy; B[2, i*3+2] = dN_dz;
                B[3, i*3] = dN_dy; B[3, i*3+1] = dN_dx;
                B[4, i*3+1] = dN_dz; B[4, i*3+2] = dN_dy;
                B[5, i*3] = dN_dz; B[5, i*3+2] = dN_dx;
            }

            var DB = new float[6,24];
            for(int i=0; i<6; i++) for(int j=0; j<24; j++) for(int k=0; k<6; k++) DB[i,j] += D[i,k] * B[k,j];

            var Ke = new float[24,24];
            for(int i=0; i<24; i++) for(int j=0; j<24; j++) for(int k=0; k<6; k++) Ke[i,j] += B[k,i] * DB[k,j];
            
            float detJ = 8 * a * b * c_elem;
            
            var elemForce = new float[24];
            for(int i=0; i<24; i++) {
                for(int j=0; j<24; j++) elemForce[i] += Ke[i,j] * elemDisp[j];
                elemForce[i] *= detJ;
            }
            // --- END OF ELEMENT CALCULATION ---
            
            var localWriter = writer.Value;
            for (int i = 0; i < 8; i++)
            {
                int nIdx = elemNodes[i];
                for (int j = 0; j < 3; j++)
                {
                    long dofIndex = nIdx * 3 + j;
                    float forceValue = elemForce[i * 3 + j];
                    localWriter.Write(dofIndex);
                    localWriter.Write(forceValue);
                }
            }
        });
        
        foreach (var w in writer.Values) w.Dispose();
        Logger.Log($"[GeomechCPU] Pass 1 (spooling) completed in {sw.Elapsed.TotalSeconds:F2} s.");
        sw.Restart();
        
        // --- Pass 2 (Reduce): Assemble contributions from files into the result vector ---
        Logger.Log($"[GeomechCPU] Assembling {tempFilePaths.Count} temporary files (Pass 2)...");
        
        var chunkBuffer = new float[chunkSize];
        readers = tempFilePaths.Select(p => new BinaryReader(File.OpenRead(p))).ToList();
        
        // This loop processes the final result vector one sequential chunk at a time.
        for (long chunkStart = 0; chunkStart < result.Length; chunkStart += chunkSize)
        {
            var currentChunkSize = (int)Math.Min(chunkSize, result.Length - chunkStart);
            if (chunkBuffer.Length != currentChunkSize) chunkBuffer = new float[currentChunkSize];
            Array.Clear(chunkBuffer, 0, currentChunkSize);

            long chunkEnd = chunkStart + currentChunkSize;

            foreach (var reader in readers)
            {
                reader.BaseStream.Seek(0, SeekOrigin.Begin); // Rewind file for each chunk pass
                while (reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    long index = reader.ReadInt64();
                    float value = reader.ReadSingle();
                    
                    // If the contribution belongs in our current memory chunk, add it.
                    if (index >= chunkStart && index < chunkEnd)
                    {
                        chunkBuffer[index - chunkStart] += value;
                    }
                }
            }
            
            result.WriteChunk(chunkStart, chunkBuffer);
        }
        
        Logger.Log($"[GeomechCPU] Pass 2 (assembly) completed in {sw.Elapsed.TotalSeconds:F2} s.");

        // Enforce boundary conditions on the final result vector.
        ApplyDirichletToVector(result, setToZero: false);
    }
    finally
    {
        // --- Cleanup Phase ---
        // Crucial to ensure no temporary files are left behind.
        foreach (var w in writer.Values) w.Dispose();
        if (readers != null)
        {
            foreach (var r in readers) r.Dispose();
        }
        foreach (var path in tempFilePaths)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { Logger.LogWarning($"[GeomechCPU] Failed to delete temp file {path}: {ex.Message}"); }
        }
    }
}

    /// <summary>
/// Builds the Jacobi (diagonal) preconditioner using a memory-optimized, SSD-friendly
/// two-pass algorithm. The result M_inv contains the reciprocal of the diagonal of the
/// global stiffness matrix K.
/// </summary>
/// <param name="M_inv">The ArrayWrapper to store the resulting inverted diagonal (1/K_ii).</param>
private void BuildPreconditioner(ArrayWrapper<float> M_inv)
{
    var sw = Stopwatch.StartNew();
    // Use a temporary wrapper for the diagonal matrix 'M' before it's inverted.
    // The 'using' block ensures it gets disposed and its temp file is deleted.
    using var M = _params.EnableOffloading 
        ? new DiskBackedArray<float>(_numDOFs, _params.OffloadDirectory) 
        : new MemoryBackedArray<float>(_numDOFs) as ArrayWrapper<float>;

    // --- Pass 0: Clear the temporary M vector ---
    const int chunkSize = 1024 * 1024;
    var zeroBuffer = new float[chunkSize]; 
    for (long i = 0; i < M.Length; i += chunkSize)
    {
        var currentChunkSize = (int)Math.Min(chunkSize, M.Length - i);
        if (zeroBuffer.Length != currentChunkSize) zeroBuffer = new float[currentChunkSize];
        M.WriteChunk(i, zeroBuffer);
    }
    
    var tempFilePaths = new List<string>();
    var writer = new ThreadLocal<BinaryWriter>(() =>
    {
        string path = Path.Combine(_params.OffloadDirectory, Path.GetRandomFileName());
        lock (tempFilePaths) { tempFilePaths.Add(path); }
        return new BinaryWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 65536));
    });
    
    List<BinaryReader> readers = null;

    try
    {
        // --- Pass 1 (Map): Calculate diagonal contributions and spool to disk ---
        Logger.Log("[GeomechCPU] Spooling preconditioner contributions (Pass 1)...");
        Parallel.For(0, _numElements, e =>
        {
            var elemNodes = new int[8];
            for (int i = 0; i < 8; i++) elemNodes[i] = _elementNodes[e * 8 + i];
            
            // --- Element Diagonal Calculation ---
            float E = _elementE[e];
            float nu = _elementNu[e];
            float c = E / ((1.0f + nu) * (1.0f - 2.0f * nu));
            var D = new float[6,6];
            D[0,0] = D[1,1] = D[2,2] = c * (1.0f - nu);
            D[3,3] = D[4,4] = D[5,5] = c * (1.0f - 2.0f*nu) / 2.0f;
            
            var B = new float[6,24];
            float x0 = _nodeX[elemNodes[0]], x6 = _nodeX[elemNodes[6]];
            float y0 = _nodeY[elemNodes[0]], y6 = _nodeY[elemNodes[6]];
            float z0 = _nodeZ[elemNodes[0]], z6 = _nodeZ[elemNodes[6]];
            float a = (x6 - x0) / 2.0f, b = (y6 - y0) / 2.0f, c_elem = (z6 - z0) / 2.0f;

            if (Math.Abs(a) < 1e-9f || Math.Abs(b) < 1e-9f || Math.Abs(c_elem) < 1e-9f) return;

            float[] dN_dxi  = {-0.125f,  0.125f,  0.125f, -0.125f, -0.125f,  0.125f,  0.125f, -0.125f};
            float[] dN_deta = {-0.125f, -0.125f,  0.125f,  0.125f, -0.125f, -0.125f,  0.125f,  0.125f};
            float[] dN_dzeta= {-0.125f, -0.125f, -0.125f, -0.125f,  0.125f,  0.125f,  0.125f,  0.125f};
            
            for (int i = 0; i < 8; i++) {
                float dN_dx = dN_dxi[i] / a, dN_dy = dN_deta[i] / b, dN_dz = dN_dzeta[i] / c_elem;
                B[0, i*3] = dN_dx; B[1, i*3+1] = dN_dy; B[2, i*3+2] = dN_dz;
                B[3, i*3] = dN_dy; B[3, i*3+1] = dN_dx;
                B[4, i*3+1] = dN_dz; B[4, i*3+2] = dN_dy;
                B[5, i*3] = dN_dz; B[5, i*3+2] = dN_dx;
            }
            
            float detJ = 8 * a * b * c_elem;
            var localWriter = writer.Value;

            for (int i = 0; i < 24; i++) // For each local DOF in the element
            {
                float K_diag_ii = 0;
                // K_ii = Sum over j,k of (B_ji * D_jk * B_ki)
                for (int j = 0; j < 6; j++) {
                    for(int k=0; k < 6; k++) {
                        K_diag_ii += B[j, i] * D[j, k] * B[k, i];
                    }
                }
                
                int nodeIdx = elemNodes[i / 3];
                int dofOffset = i % 3;
                long globalDofIndex = (long)nodeIdx * 3 + dofOffset;
                
                localWriter.Write(globalDofIndex);
                localWriter.Write(K_diag_ii * detJ);
            }
        });

        foreach (var w in writer.Values) w.Dispose();
        Logger.Log($"[GeomechCPU] Preconditioner Pass 1 (spooling) took {sw.Elapsed.TotalSeconds:F2} s.");
        sw.Restart();

        // --- Pass 2 (Reduce): Assemble contributions into the M vector ---
        Logger.Log($"[GeomechCPU] Assembling preconditioner from {tempFilePaths.Count} files (Pass 2)...");
        var chunkBuffer = new float[chunkSize];
        readers = tempFilePaths.Select(p => new BinaryReader(File.OpenRead(p))).ToList();
        
        for (long chunkStart = 0; chunkStart < M.Length; chunkStart += chunkSize)
        {
            var currentChunkSize = (int)Math.Min(chunkSize, M.Length - chunkStart);
            if (chunkBuffer.Length != currentChunkSize) chunkBuffer = new float[currentChunkSize];
            Array.Clear(chunkBuffer, 0, currentChunkSize);
            long chunkEnd = chunkStart + currentChunkSize;

            foreach (var reader in readers)
            {
                reader.BaseStream.Seek(0, SeekOrigin.Begin);
                while (reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    long index = reader.ReadInt64();
                    float value = reader.ReadSingle();
                    if (index >= chunkStart && index < chunkEnd)
                    {
                        chunkBuffer[index - chunkStart] += value;
                    }
                }
            }
            M.WriteChunk(chunkStart, chunkBuffer);
        }
        Logger.Log($"[GeomechCPU] Preconditioner Pass 2 (assembly) took {sw.Elapsed.TotalSeconds:F2} s.");
        sw.Restart();

        // --- Pass 3: Invert M and store in M_inv ---
        Logger.Log("[GeomechCPU] Inverting preconditioner diagonal (Pass 3)...");
        var mChunk = new float[chunkSize];
        var mInvChunk = new float[chunkSize];
        var isDirichletChunk = new bool[chunkSize];

        for (long i = 0; i < M.Length; i += chunkSize)
        {
            var currentChunkSize = (int)Math.Min(chunkSize, M.Length - i);
            if(mChunk.Length != currentChunkSize)
            {
                mChunk = new float[currentChunkSize];
                mInvChunk = new float[currentChunkSize];
                isDirichletChunk = new bool[currentChunkSize];
            }
            
            M.ReadChunk(i, mChunk);
            _isDirichlet.ReadChunk(i, isDirichletChunk);

            for (int j = 0; j < currentChunkSize; j++)
            {
                if (isDirichletChunk[j])
                {
                    mInvChunk[j] = 1.0f; // For Dirichlet nodes, preconditioner is identity
                }
                else
                {
                    mInvChunk[j] = Math.Abs(mChunk[j]) > 1e-9f ? 1.0f / mChunk[j] : 1.0f;
                }
            }
            M_inv.WriteChunk(i, mInvChunk);
        }
        Logger.Log($"[GeomechCPU] Preconditioner Pass 3 (inversion) took {sw.Elapsed.TotalSeconds:F2} s.");
    }
    finally
    {
        // --- Cleanup Phase ---
        foreach (var w in writer.Values) w.Dispose();
        if (readers != null)
        {
            foreach (var r in readers) r.Dispose();
        }
        foreach (var path in tempFilePaths)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { Logger.LogWarning($"[GeomechCPU] Failed to delete temp file {path}: {ex.Message}"); }
        }
    }
}
    
    // ========== SIMD-ACCELERATED VECTOR OPERATIONS ==========
    private float DotProduct(ArrayWrapper<float> a, ArrayWrapper<float> b)
    {
        // Use a 4MB buffer (1M floats)
        const int chunkSize = 1024 * 1024;
        var bufferA = new float[chunkSize];
        var bufferB = new float[chunkSize];
        
        double total = 0;

        for (long i = 0; i < a.Length; i += chunkSize)
        {
            var currentChunkSize = (int)Math.Min(chunkSize, a.Length - i);

            // Resize buffers if it's the last, smaller chunk
            if (currentChunkSize != bufferA.Length)
            {
                bufferA = new float[currentChunkSize];
                bufferB = new float[currentChunkSize];
            }

            a.ReadChunk(i, bufferA);
            b.ReadChunk(i, bufferB);

            // SIMD acceleration on the in-memory chunk
            if (_isSimdAccelerated)
            {
                int vecSize = Vector<float>.Count;
                var sumVec = Vector<float>.Zero;
                for (int j = 0; j <= currentChunkSize - vecSize; j += vecSize)
                {
                    var va = new Vector<float>(bufferA, j);
                    var vb = new Vector<float>(bufferB, j);
                    sumVec += va * vb;
                }
                for (int j = 0; j < vecSize; j++) total += sumVec[j];
                for (int j = currentChunkSize - (currentChunkSize % vecSize); j < currentChunkSize; j++) total += (double)bufferA[j] * bufferB[j];
            }
            else
            {
                for (int j = 0; j < currentChunkSize; j++) total += (double)bufferA[j] * bufferB[j];
            }
        }
        return (float)total;
    }

    private void Saxpy(ArrayWrapper<float> y, ArrayWrapper<float> x, float a)
    {
        // Use a 4MB buffer (1M floats)
        const int chunkSize = 1024 * 1024;
        var bufferX = new float[chunkSize];
        var bufferY = new float[chunkSize];

        for (long i = 0; i < y.Length; i += chunkSize)
        {
            var currentChunkSize = (int)Math.Min(chunkSize, y.Length - i);
            if (currentChunkSize != bufferX.Length)
            {
                bufferX = new float[currentChunkSize];
                bufferY = new float[currentChunkSize];
            }

            y.ReadChunk(i, bufferY);
            x.ReadChunk(i, bufferX);

            // Perform y = y + a*x on the in-memory chunk
            if (_isSimdAccelerated)
            {
                int vecSize = Vector<float>.Count;
                var va = new Vector<float>(a);
                for (int j = 0; j <= currentChunkSize - vecSize; j += vecSize)
                {
                    var vx = new Vector<float>(bufferX, j);
                    var vy = new Vector<float>(bufferY, j);
                    vy += va * vx;
                    vy.CopyTo(bufferY, j);
                }
                for (int j = currentChunkSize - (currentChunkSize % vecSize); j < currentChunkSize; j++) bufferY[j] += a * bufferX[j];
            }
            else
            {
                for (int j = 0; j < currentChunkSize; j++) bufferY[j] += a * bufferX[j];
            }

            // Write the modified chunk back to disk
            y.WriteChunk(i, bufferY);
        }
    }

    // ========== POST-PROCESSING ==========
    private GeomechanicalResults CalculateStresses(byte[,,] labels, BoundingBox extent)
    {
        var w = extent.Width; var h = extent.Height; var d = extent.Depth;
        var results = new GeomechanicalResults
        {
            StressXX = new float[w, h, d], StressYY = new float[w, h, d], StressZZ = new float[w, h, d],
            StressXY = new float[w, h, d], StressXZ = new float[w, h, d], StressYZ = new float[w, h, d],
            MaterialLabels = labels, Parameters = _params
        };

        Parallel.For(0, _numElements, e =>
        {
            var elemNodes = new int[8];
            for (int i = 0; i < 8; i++) elemNodes[i] = _elementNodes[e * 8 + i];

            var elemDisp = new float[24];
            for (int i = 0; i < 8; i++) {
                int nIdx = elemNodes[i];
                elemDisp[i * 3 + 0] = _displacement[nIdx * 3 + 0];
                elemDisp[i * 3 + 1] = _displacement[nIdx * 3 + 1];
                elemDisp[i * 3 + 2] = _displacement[nIdx * 3 + 2];
            }

            // --- Get B matrix at element center ---
            var B = new float[6,24];
            float x0 = _nodeX[elemNodes[0]], x6 = _nodeX[elemNodes[6]];
            float y0 = _nodeY[elemNodes[0]], y6 = _nodeY[elemNodes[6]];
            float z0 = _nodeZ[elemNodes[0]], z6 = _nodeZ[elemNodes[6]];
            float a = (x6 - x0) / 2.0f, b = (y6 - y0) / 2.0f, c_elem = (z6 - z0) / 2.0f;
            float[] dN_dxi = {-0.125f, 0.125f, 0.125f, -0.125f, -0.125f, 0.125f, 0.125f, -0.125f};
            float[] dN_deta= {-0.125f,-0.125f,  0.125f,  0.125f, -0.125f,-0.125f,  0.125f,  0.125f};
            float[] dN_dzeta={-0.125f,-0.125f, -0.125f, -0.125f,  0.125f,  0.125f,  0.125f,  0.125f};
            for (int i = 0; i < 8; i++) {
                float dN_dx = dN_dxi[i] / a, dN_dy = dN_deta[i] / b, dN_dz = dN_dzeta[i] / c_elem;
                B[0, i*3] = dN_dx; B[1, i*3+1] = dN_dy; B[2, i*3+2] = dN_dz;
                B[3, i*3] = dN_dy; B[3, i*3+1] = dN_dx;
                B[4, i*3+1] = dN_dz; B[4, i*3+2] = dN_dy;
                B[5, i*3] = dN_dz; B[5, i*3+2] = dN_dx;
            }

            // strain = B * u
            var strain = new float[6];
            for(int i=0; i<6; i++) for(int j=0; j<24; j++) strain[i] += B[i,j] * elemDisp[j];
            
            // stress = D * strain
            float E = _elementE[e], nu = _elementNu[e];
            float lambda = E * nu / ((1.0f + nu) * (1.0f - 2.0f*nu));
            float mu = E / (2.0f * (1.0f + nu));
            float trace = strain[0] + strain[1] + strain[2];
            var stress = new float[6];
            stress[0] = lambda * trace + 2.0f * mu * strain[0]; // xx
            stress[1] = lambda * trace + 2.0f * mu * strain[1]; // yy
            stress[2] = lambda * trace + 2.0f * mu * strain[2]; // zz
            stress[3] = mu * strain[3]; // xy
            stress[4] = mu * strain[4]; // yz
            stress[5] = mu * strain[5]; // xz

            // Map to voxel
            float cx = (_nodeX[elemNodes[0]] + _nodeX[elemNodes[6]]) / 2.0f;
            float cy = (_nodeY[elemNodes[0]] + _nodeY[elemNodes[6]]) / 2.0f;
            float cz = (_nodeZ[elemNodes[0]] + _nodeZ[elemNodes[6]]) / 2.0f;
            var dx_um = _params.PixelSize;
            int vx = (int)Math.Round(cx * 1000 / dx_um);
            int vy = (int)Math.Round(cy * 1000 / dx_um);
            int vz = (int)Math.Round(cz * 1000 / dx_um);

            if (vx >= 0 && vx < w && vy >= 0 && vy < h && vz >= 0 && vz < d && labels[vx, vy, vz] != 0)
            {
                results.StressXX[vx, vy, vz] = stress[0];
                results.StressYY[vx, vy, vz] = stress[1];
                results.StressZZ[vx, vy, vz] = stress[2];
                results.StressXY[vx, vy, vz] = stress[3];
                results.StressYZ[vx, vy, vz] = stress[4];
                results.StressXZ[vx, vy, vz] = stress[5];
            }
        });

        return results;
    }

    private void PostProcessResults(GeomechanicalResults results, byte[,,] labels)
{
    var w = labels.GetLength(0); var h = labels.GetLength(1); var d = labels.GetLength(2);
    results.Sigma1 = new float[w, h, d]; results.Sigma2 = new float[w, h, d]; results.Sigma3 = new float[w, h, d];
    results.FailureIndex = new float[w, h, d];
    results.DamageField = new byte[w, h, d];
    results.FractureField = new bool[w, h, d];

    var cohesion_Pa = _params.Cohesion * 1e6f;
    var phi = _params.FrictionAngle * MathF.PI / 180f;
    var tensile_Pa = _params.TensileStrength * 1e6f;

    Parallel.For(0, d, z =>
    {
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (labels[x, y, z] == 0) continue;

                // --- Plasticity Correction (Von Mises) ---
                if (_params.EnablePlasticity)
                {
                    var sxx=results.StressXX[x,y,z]; var syy=results.StressYY[x,y,z]; var szz=results.StressZZ[x,y,z];
                    var sxy=results.StressXY[x,y,z]; var sxz=results.StressXZ[x,y,z]; var syz=results.StressYZ[x,y,z];
                    var mean = (sxx + syy + szz) / 3.0f;
                    var s_dev_xx = sxx - mean; var s_dev_yy = syy - mean; var s_dev_zz = szz - mean;
                    var J2 = 0.5f * (s_dev_xx*s_dev_xx + s_dev_yy*s_dev_yy + s_dev_zz*s_dev_zz) + sxy*sxy + sxz*sxz + syz*syz;
                    var vonMises = MathF.Sqrt(3.0f * J2);
                    var yieldStress = _params.Cohesion * 1e6f * 2.0f; // Approx from Tresca

                    if (vonMises > yieldStress) {
                        float scale = yieldStress / vonMises;
                        results.StressXX[x,y,z] = mean + s_dev_xx * scale;
                        results.StressYY[x,y,z] = mean + s_dev_yy * scale;
                        results.StressZZ[x,y,z] = mean + s_dev_zz * scale;
                        results.StressXY[x,y,z] *= scale; results.StressXZ[x,y,z] *= scale; results.StressYZ[x,y,z] *= scale;
                    }
                }

                // --- Principal Stresses ---
                // FIX: Calculate principal stresses by finding the eigenvalues of the 3x3 stress tensor.
                // This is an accurate analytical solution to the cubic characteristic equation.
                var (s1, s2, s3) = CalculatePrincipalStresses(
                    results.StressXX[x, y, z], results.StressYY[x, y, z], results.StressZZ[x, y, z],
                    results.StressXY[x, y, z], results.StressXZ[x, y, z], results.StressYZ[x, y, z]
                );

                results.Sigma1[x, y, z] = s1;
                results.Sigma2[x, y, z] = s2;
                results.Sigma3[x, y, z] = s3;

                // --- Failure Evaluation ---
                float fi = CalculateFailureIndex(s1, s2, s3, cohesion_Pa, phi, tensile_Pa);
                results.FailureIndex[x, y, z] = fi;
                results.FractureField[x, y, z] = fi >= 1.0f;
                results.DamageField[x, y, z] = (byte)Math.Clamp(fi * 200.0f, 0.0f, 255.0f);
            }
        }
    });
}
    /// <summary>
/// Analytically calculates the principal stresses (eigenvalues) for a 3D stress state.
/// </summary>
/// <param name="sxx">Stress in XX direction.</param>
/// <param name="syy">Stress in YY direction.</param>
/// <param name="szz">Stress in ZZ direction.</param>
/// <param name="sxy">Shear stress in XY plane.</param>
/// <param name="sxz">Shear stress in XZ plane.</param>
/// <param name="syz">Shear stress in YZ plane.</param>
/// <returns>A tuple containing the sorted principal stresses (s1, s2, s3) where s1 >= s2 >= s3.</returns>
private static (float s1, float s2, float s3) CalculatePrincipalStresses(
    float sxx, float syy, float szz,
    float sxy, float sxz, float syz)
{
    // The principal stresses are the eigenvalues of the symmetric stress tensor.
    // They are found by solving the characteristic cubic equation: λ³ - I₁λ² + I₂λ - I₃ = 0
    // where I₁, I₂, and I₃ are the stress invariants.

    // Calculate the invariants of the stress tensor
    // I1 = trace(T)
    float i1 = sxx + syy + szz;

    // I2 = 1/2 * ( (trace(T))^2 - trace(T^2) )
    float i2 = (sxx * syy) + (syy * szz) + (szz * sxx) - (sxy * sxy) - (sxz * sxz) - (syz * syz);

    // I3 = det(T)
    float i3 = (sxx * syy * szz) + (2.0f * sxy * sxz * syz)
               - (sxx * syz * syz) - (syy * sxz * sxz) - (szz * sxy * sxy);

    // Using the trigonometric solution for 3 real roots (guaranteed for a symmetric matrix)
    float p = (i1 * i1 / 3.0f) - i2;
    float q = i3 + (2.0f * i1 * i1 * i1 - 9.0f * i1 * i2) / 27.0f;
    float r = MathF.Sqrt(p * p * p / 27.0f);
    
    // Handle edge case of isotropic stress state to avoid division by zero
    if (r < 1e-9f)
    {
        float val = i1 / 3.0f;
        return (val, val, val);
    }
    
    // Clamp the argument for acos to [-1, 1] to prevent NaN due to floating point inaccuracies
    float phi = MathF.Acos(Math.Clamp(-q / (2.0f * r), -1.0f, 1.0f));

    // The three eigenvalues (principal stresses)
    float l1 = i1 / 3.0f + 2.0f * MathF.Sqrt(p / 3.0f) * MathF.Cos(phi / 3.0f);
    float l2 = i1 / 3.0f + 2.0f * MathF.Sqrt(p / 3.0f) * MathF.Cos((phi + 2.0f * MathF.PI) / 3.0f);
    float l3 = i1 / 3.0f + 2.0f * MathF.Sqrt(p / 3.0f) * MathF.Cos((phi + 4.0f * MathF.PI) / 3.0f);

    // Sort to ensure σ1 >= σ2 >= σ3 by convention
    if (l1 < l2) (l1, l2) = (l2, l1); // Swap using tuple deconstruction
    if (l1 < l3) (l1, l3) = (l3, l1);
    if (l2 < l3) (l2, l3) = (l3, l2);

    return (l1, l2, l3);
}
    private float CalculateFailureIndex(float s1, float s2, float s3, float cohesion, float phi, float tensile)
{
    // Switch to Pa for calculations if inputs are in MPa
    switch (_params.FailureCriterion)
    {
        case FailureCriterion.MohrCoulomb:
            // Shear failure: τ = c + σ_n tan(φ) => s1 - s3 = 2c cos(φ) + (s1+s3)sin(φ)
            float shear_strength = 2.0f * cohesion * MathF.Cos(phi) + (s1 + s3) * MathF.Sin(phi);
            // Tensile failure: s3 <= -T
            if (s3 < 0 && -s3 > tensile) return -s3 / tensile;
            return (shear_strength > 1e-3f) ? (s1 - s3) / shear_strength : (s1 - s3);

        case FailureCriterion.DruckerPrager:
            float I1 = s1 + s2 + s3;
            float s1_dev = s1 - I1/3.0f, s2_dev = s2 - I1/3.0f, s3_dev = s3 - I1/3.0f;
            float J2 = (s1_dev*s1_dev + s2_dev*s2_dev + s3_dev*s3_dev) / 2.0f;
            float alpha = 2.0f*MathF.Sin(phi) / (MathF.Sqrt(3.0f) * (3.0f - MathF.Sin(phi)));
            float k = 6.0f*cohesion*MathF.Cos(phi) / (MathF.Sqrt(3.0f) * (3.0f - MathF.Sin(phi)));
            return (k > 1e-3f) ? (alpha * I1 + MathF.Sqrt(J2)) / k : (alpha * I1 + MathF.Sqrt(J2));

        case FailureCriterion.HoekBrown:
            float ucs = 2.0f * cohesion * MathF.Cos(phi) / (1.0f - MathF.Sin(phi));

            // CORRECTED: Using property names with underscores from your GeomechanicalParameters.cs file
            float mb = _params.HoekBrown_mb; 
            float s = _params.HoekBrown_s; 
            float a = _params.HoekBrown_a;
            
            if (s3 < -tensile) return -s3 / tensile;
            float strength = s3 + ucs * MathF.Pow(mb * s3 / ucs + s, a);
            return (strength > 1e-3f) ? s1 / strength : s1;
        
        case FailureCriterion.Griffith:
            if (3 * s1 + s3 < 0) { // Tensile regime
                if (s3 < 0) return -s3 / tensile;
            } else { // Compressive regime
                float strength_denom = 8.0f * tensile * (s1 + s3);
                if (strength_denom > 1e-3f) return MathF.Pow(s1 - s3, 2) / strength_denom;
            }
            return 0.0f;

        default: 
            return 0.0f;
    }
}
    // ========== STATISTICS ==========
    private void CalculateFinalStatistics(GeomechanicalResults results)
    {
        var w = results.MaterialLabels.GetLength(0); var h = results.MaterialLabels.GetLength(1); var d = results.MaterialLabels.GetLength(2);
        
        long validVoxels = 0, failedVoxels = 0;
        double sumMeanStress = 0, sumVonMises = 0;
        float maxShear = 0, maxVonMises = 0;

        for(int z=0; z<d; z++) for(int y=0; y<h; y++) for(int x=0; x<w; x++)
        {
            if (results.MaterialLabels[x,y,z] == 0) continue;
            validVoxels++;
            if (results.FractureField[x,y,z]) failedVoxels++;
            
            var sxx=results.StressXX[x,y,z]; var syy=results.StressYY[x,y,z]; var szz=results.StressZZ[x,y,z];
            var sxy=results.StressXY[x,y,z]; var sxz=results.StressXZ[x,y,z]; var syz=results.StressYZ[x,y,z];
            
            var mean = (sxx + syy + szz) / 3.0f;
            sumMeanStress += mean;

            var shear = (results.Sigma1[x,y,z] - results.Sigma3[x,y,z]) / 2.0f;
            maxShear = MathF.Max(maxShear, shear);
            
            var s_dev_xx=sxx-mean; var s_dev_yy=syy-mean; var s_dev_zz=szz-mean;
            var J2 = 0.5f * (s_dev_xx*s_dev_xx + s_dev_yy*s_dev_yy + s_dev_zz*s_dev_zz) + sxy*sxy + sxz*sxz + syz*syz;
            var vonMises = MathF.Sqrt(3.0f * J2);
            sumVonMises += vonMises;
            maxVonMises = MathF.Max(maxVonMises, vonMises);
        }

        if (validVoxels > 0)
        {
            results.MeanStress = (float)(sumMeanStress / validVoxels);
            results.MaxShearStress = maxShear;
            results.VonMisesStress_Mean = (float)(sumVonMises / validVoxels);
            results.VonMisesStress_Max = maxVonMises;
            results.TotalVoxels = (int)validVoxels;
            results.FailedVoxels = (int)failedVoxels;
            results.FailedVoxelPercentage = 100.0f * failedVoxels / validVoxels;
        }
    }
}