// GeoscientistToolkit/Analysis/Geomechanics/GeomechanicalSimulatorCPU.cs
// CRITICAL FIX: Properly handle displacement-controlled boundary conditions in matrix-vector product

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.Util;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;


namespace GeoscientistToolkit.Analysis.Geomechanics;

public partial class GeomechanicalSimulatorCPU : IDisposable
{
    private readonly GeomechanicalParameters _params;
    private readonly bool _isSimdAccelerated = Vector.IsHardwareAccelerated;
    private readonly object[] _dofLocks = new object[1024];
    
    private int _numNodes;
    private int _numElements;
    private long _numDOFs;
    private ArrayWrapper<float> _nodeX, _nodeY, _nodeZ;
    private ArrayWrapper<int> _elementNodes;
    private ArrayWrapper<float> _elementE, _elementNu;
    private int _minX, _maxX, _minY, _maxY, _minZ, _maxZ;
    private ArrayWrapper<bool> _isDirichlet;
    private ArrayWrapper<float> _dirichletValue;
    private ArrayWrapper<float> _force;
    private ArrayWrapper<float> _displacement;
    private int _iterationsPerformed;

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
            _dofLocks[i] = new object();
    }

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
            
            Logger.Log("\n[1/10] Finding material bounds...");
            progress?.Report(0.05f);
            var sw = Stopwatch.StartNew();
            FindMaterialBounds(labels);
            Logger.Log($"[1/10] Material bounds found in {sw.ElapsedMilliseconds} ms");
            token.ThrowIfCancellationRequested();

            Logger.Log("\n[2/10] Generating FEM mesh...");
            progress?.Report(0.10f);
            sw.Restart();
            GenerateMesh(labels);
            Logger.Log($"[2/10] Mesh generated in {sw.ElapsedMilliseconds} ms");
            token.ThrowIfCancellationRequested();

            Logger.Log("\n[3/10] Applying boundary conditions...");
            progress?.Report(0.20f);
            sw.Restart();
            ApplyBoundaryConditions();
            Logger.Log($"[3/10] Boundary conditions applied in {sw.ElapsedMilliseconds} ms");
            token.ThrowIfCancellationRequested();

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

            Logger.Log("\n[5/10] Solving mechanical system (PCG)...");
            progress?.Report(0.25f);
            sw.Restart();
            var converged = SolveSystem(progress, token);
            Logger.Log($"[5/10] System solved in {sw.Elapsed.TotalSeconds:F2} s. Converged: {converged}, Iterations: {_iterationsPerformed}");
            token.ThrowIfCancellationRequested();

            Logger.Log("\n[6/10] Calculating stresses...");
            progress?.Report(0.75f);
            sw.Restart();
            var results = CalculateStresses(labels, extent);
            Logger.Log($"[6/10] Stresses calculated in {sw.Elapsed.TotalSeconds:F2} s");
            token.ThrowIfCancellationRequested();

            Logger.Log("\n[7/10] Post-processing (principal stresses, failure)...");
            progress?.Report(0.85f);
            sw.Restart();
            PostProcessResults(results, labels);
            Logger.Log($"[7/10] Post-processing completed in {sw.Elapsed.TotalSeconds:F2} s");
            token.ThrowIfCancellationRequested();

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

            Logger.Log("\n[9/10] Calculating final statistics...");
            progress?.Report(0.95f);
            sw.Restart();
            CalculateFinalStatistics(results);
            Logger.Log($"[9/10] Statistics calculated in {sw.ElapsedMilliseconds} ms");
            
            Logger.Log("\n[10/10] Finalizing results...");
            results.Converged = converged;
            results.IterationsPerformed = _iterationsPerformed;
            results.ComputationTime = DateTime.Now - startTime;
            PopulateGeothermalAndFluidResults(results);
            
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
    var dx = _params.PixelSize / 1e3f; // mm

    _numNodes = w * h * d;
    _numDOFs = _numNodes * 3;

    bool offload = _params.EnableOffloading;
    string offloadDir = _params.OffloadDirectory;
    Logger.Log($"[GeomechCPU] Offloading enabled: {offload}. Directory: {offloadDir}");

    var nodeX_init = new float[_numNodes];
    var nodeY_init = new float[_numNodes];
    var nodeZ_init = new float[_numNodes];

    Parallel.For(0, d, new ParallelOptions(), (int z) =>  // FIX: Added ParallelOptions
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
    
    var elementList = new List<int[]>();
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

        float height_mm = (d - 2) * dx;
        float width_mm = (w - 2) * dx;
        float depth_mm = (h - 2) * dx;

        float eps_z = (_params.Sigma1 - _params.PoissonRatio * (_params.Sigma2 + _params.Sigma3)) / _params.YoungModulus;
        float eps_y = (_params.Sigma2 - _params.PoissonRatio * (_params.Sigma1 + _params.Sigma3)) / _params.YoungModulus;
        float eps_x = (_params.Sigma3 - _params.PoissonRatio * (_params.Sigma1 + _params.Sigma2)) / _params.YoungModulus;

        float delta_z = eps_z * height_mm;
        float delta_y = eps_y * depth_mm;
        float delta_x = eps_x * width_mm;
        
        Logger.Log($"[GeomechCPU] Target displacements: δx={delta_x*1000:F2}μm, δy={delta_y*1000:F2}μm, δz={delta_z*1000:F2}μm");
        
        const int BATCH_SIZE = 1_048_576;
        int numBatches = (int)((_numDOFs + BATCH_SIZE - 1) / BATCH_SIZE);
        
        Parallel.For(0, numBatches, (int batchIdx) =>
        {
            long startDof = (long)batchIdx * BATCH_SIZE;
            long endDof = Math.Min(startDof + BATCH_SIZE, _numDOFs);
            int batchSize = (int)(endDof - startDof);
            
            var isDirBatch = new bool[batchSize];
            var dirValBatch = new float[batchSize];
            
            for (long dof = startDof; dof < endDof; dof++)
            {
                long nodeIdx = dof / 3;
                int comp = (int)(dof % 3);
                
                long temp = nodeIdx;
                int x = (int)(temp % w);
                temp /= w;
                int y = (int)(temp % h);
                int z = (int)(temp / h);
                
                int localIdx = (int)(dof - startDof);

                if (z == 0)
                {
                    isDirBatch[localIdx] = true;
                    dirValBatch[localIdx] = 0;
                }
                else if (z == d - 2 && comp == 2)
                {
                    isDirBatch[localIdx] = true;
                    dirValBatch[localIdx] = delta_z;
                }
                else if ((x == 0 || x == w - 2) && comp == 0)
                {
                    isDirBatch[localIdx] = true;
                    dirValBatch[localIdx] = (x == 0) ? 0 : delta_x;
                }
                else if ((y == 0 || y == h - 2) && comp == 1)
                {
                    isDirBatch[localIdx] = true;
                    dirValBatch[localIdx] = (y == 0) ? 0 : delta_y;
                }
            }
            
            _isDirichlet.WriteChunk(startDof, isDirBatch);
            _dirichletValue.WriteChunk(startDof, dirValBatch);
        });
        
        long fixedDOFs = 0;
        const int COUNT_CHUNK = 1_048_576;
        var countBuffer = new bool[COUNT_CHUNK];
        
        for (long i = 0; i < _isDirichlet.Length; i += COUNT_CHUNK)
        {
            int chunkSize = (int)Math.Min(COUNT_CHUNK, _isDirichlet.Length - i);
            if (chunkSize != countBuffer.Length) countBuffer = new bool[chunkSize];
            _isDirichlet.ReadChunk(i, countBuffer);
            
            for (int j = 0; j < chunkSize; j++)
            {
                if (countBuffer[j]) fixedDOFs++;
            }
        }
        
        Logger.Log($"[GeomechCPU] Applied displacement-controlled BCs in {sw.ElapsedMilliseconds} ms. Fixed DOFs: {fixedDOFs:N0} / {_numDOFs:N0}");
    }

    private bool SolveSystem(IProgress<float> progress, CancellationToken token)
    {
        var sw = Stopwatch.StartNew();
        Logger.Log("[GeomechCPU] Starting PCG solver...");

        const int maxIter = 2000;
        const float tol = 1e-6f;

        bool offload = _params.EnableOffloading;
        string offloadDir = _params.OffloadDirectory;

        using var r = offload ? new DiskBackedArray<float>(_numDOFs, offloadDir) : new MemoryBackedArray<float>(_numDOFs) as ArrayWrapper<float>;
        using var p = offload ? new DiskBackedArray<float>(_numDOFs, offloadDir) : new MemoryBackedArray<float>(_numDOFs) as ArrayWrapper<float>;
        using var Ap = offload ? new DiskBackedArray<float>(_numDOFs, offloadDir) : new MemoryBackedArray<float>(_numDOFs) as ArrayWrapper<float>;
        using var M_inv = offload ? new DiskBackedArray<float>(_numDOFs, offloadDir) : new MemoryBackedArray<float>(_numDOFs) as ArrayWrapper<float>;
        using var z = offload ? new DiskBackedArray<float>(_numDOFs, offloadDir) : new MemoryBackedArray<float>(_numDOFs) as ArrayWrapper<float>;

        Logger.Log("[GeomechCPU] Initializing displacement with boundary conditions...");
        CopyWrapper(_dirichletValue, _displacement);
        
        // CRITICAL DIAGNOSTIC: Verify displacement was actually set
        long verifyNonZero = 0;
        float verifyMax = 0;
        const int VERIFY_CHUNK = 1_048_576;
        var verifyBuffer = new float[VERIFY_CHUNK];
        for (long i = 0; i < _displacement.Length; i += VERIFY_CHUNK)
        {
            int chunkSize = (int)Math.Min(VERIFY_CHUNK, _displacement.Length - i);
            if (chunkSize != verifyBuffer.Length) verifyBuffer = new float[chunkSize];
            _displacement.ReadChunk(i, verifyBuffer);
            for (int j = 0; j < chunkSize; j++)
            {
                if (MathF.Abs(verifyBuffer[j]) > 1e-12f)
                {
                    verifyNonZero++;
                    verifyMax = MathF.Max(verifyMax, MathF.Abs(verifyBuffer[j]));
                }
            }
        }
        Logger.Log($"[GeomechCPU] Displacement after copy: {verifyNonZero:N0} non-zero, max = {verifyMax*1000:F6} μm");
        
        // CRITICAL DIAGNOSTIC: Test direct indexing on a known non-zero value
        bool foundTestValue = false;
        for (long testIdx = 0; testIdx < Math.Min(100000, _dirichletValue.Length); testIdx++)
        {
            float dirVal = _dirichletValue[testIdx];
            if (MathF.Abs(dirVal) > 1e-12f)
            {
                float dispVal = _displacement[testIdx];
                Logger.Log($"[GeomechCPU] Test index {testIdx}: dirichlet={dirVal*1000:F6}μm, displacement={dispVal*1000:F6}μm");
                if (MathF.Abs(dispVal - dirVal) > 1e-9f)
                {
                    Logger.LogWarning($"[GeomechCPU] MISMATCH! Displacement not copied correctly!");
                }
                foundTestValue = true;
                break;
            }
        }
        if (!foundTestValue)
        {
            Logger.LogWarning($"[GeomechCPU] Could not find non-zero dirichlet value in first 100k entries!");
        }

        // CRITICAL FIX: Add diagnostic logging
        long nonZeroDisp = 0;
        float maxDisp = 0;
        const int DIAG_CHUNK = 1_048_576;
        var diagBuffer = new float[DIAG_CHUNK];
        for (long i = 0; i < _displacement.Length; i += DIAG_CHUNK)
        {
            int chunkSize = (int)Math.Min(DIAG_CHUNK, _displacement.Length - i);
            if (chunkSize != diagBuffer.Length) diagBuffer = new float[chunkSize];
            _displacement.ReadChunk(i, diagBuffer);
            for (int j = 0; j < chunkSize; j++)
            {
                if (MathF.Abs(diagBuffer[j]) > 1e-12f)
                {
                    nonZeroDisp++;
                    maxDisp = MathF.Max(maxDisp, MathF.Abs(diagBuffer[j]));
                }
            }
        }
        Logger.Log($"[GeomechCPU] Initial displacement: {nonZeroDisp:N0} non-zero DOFs, max = {maxDisp*1000:F6} μm");

        Logger.Log("[GeomechCPU] Computing initial residual (r = f - K*u)...");
        MatrixVectorProduct(Ap, _displacement, progress, 0.25f, 0.05f);
        
        // DIAGNOSTIC: Check K*u
        long nonZeroKu = 0;
        float maxKu = 0;
        for (long i = 0; i < Ap.Length; i += DIAG_CHUNK)
        {
            int chunkSize = (int)Math.Min(DIAG_CHUNK, Ap.Length - i);
            if (chunkSize != diagBuffer.Length) diagBuffer = new float[chunkSize];
            Ap.ReadChunk(i, diagBuffer);
            for (int j = 0; j < chunkSize; j++)
            {
                if (MathF.Abs(diagBuffer[j]) > 1e-12f)
                {
                    nonZeroKu++;
                    maxKu = MathF.Max(maxKu, MathF.Abs(diagBuffer[j]));
                }
            }
        }
        Logger.Log($"[GeomechCPU] K*u: {nonZeroKu:N0} non-zero entries, max = {maxKu:E6}");
        
        SubtractWrappers(_force, Ap, r);
        ApplyDirichletToVector(r, setToZero: true);

        float rNorm0 = MathF.Sqrt(DotProduct(r, r));
        
        // CRITICAL FIX: Use relative tolerance based on system size
        float relativeTol = tol * MathF.Sqrt(_numDOFs);
        
        Logger.Log($"[GeomechCPU] Initial residual norm: {rNorm0:E6}");
        Logger.Log($"[GeomechCPU] Convergence tolerance: {relativeTol:E6}");
        
        if (rNorm0 < relativeTol)
        {
            Logger.Log("[GeomechCPU] System already converged. Initial residual is near zero.");
            _iterationsPerformed = 0;
            return true;
        }
        
        Logger.Log("----------------------------------------------------------");

        Logger.Log("[GeomechCPU] Building Jacobi preconditioner...");
        BuildPreconditioner(M_inv, progress, 0.30f, 0.05f);
        ElementWiseMultiply(M_inv, r, z);
        CopyWrapper(z, p);

        float rDotZ = DotProduct(r, z);

        Logger.Log("[GeomechCPU] Starting PCG iterations...");
        Logger.Log("----------------------------------------------------------");

        for (int iter = 0; iter < maxIter; iter++)
        {
            token.ThrowIfCancellationRequested();
            _iterationsPerformed = iter + 1;

            MatrixVectorProduct(Ap, p, null, 0, 0);

            float pDotAp = DotProduct(p, Ap);
            if (MathF.Abs(pDotAp) < 1e-20f)
            {
                Logger.LogWarning($"[GeomechCPU] Solver breakdown (p.Ap is zero) at iteration {iter}.");
                break;
            }
            float alpha = rDotZ / pDotAp;

            Saxpy(_displacement, p, alpha);
            Saxpy(r, Ap, -alpha);
            ApplyDirichletToVector(r, setToZero: true);

            float rNorm = MathF.Sqrt(DotProduct(r, r));
            float relativeResidual = rNorm / rNorm0;

            if (iter % 10 == 0 || iter < 5)
            {
                Logger.Log($"  Iter {iter,4}: ||r|| = {rNorm:E6}, rel = {relativeResidual:E6}, α = {alpha:E6}");
            }
            
            progress?.Report(0.35f + 0.40f * (iter / (float)maxIter));

            if (relativeResidual < tol)
            {
                Logger.Log("----------------------------------------------------------");
                Logger.Log($"[GeomechCPU] *** CONVERGED in {iter + 1} iterations ***");
                Logger.Log($"[GeomechCPU] Final relative residual: {relativeResidual:E6}");
                return true;
            }

            ElementWiseMultiply(M_inv, r, z);
            float rDotZ_new = DotProduct(r, z);
            float beta = rDotZ_new / rDotZ;
            rDotZ = rDotZ_new;
            UpdateSearchDirection(p, z, beta);
        }

        Logger.Log("----------------------------------------------------------");
        Logger.LogWarning($"[GeomechCPU] Did NOT converge in {maxIter} iterations.");
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReadNodeTriplet(ArrayWrapper<float> v, int node, ref float[] into24, int offset)
    {
        long baseIdx = (long)node * 3;
        into24[offset + 0] = v[baseIdx + 0];
        into24[offset + 1] = v[baseIdx + 1];
        into24[offset + 2] = v[baseIdx + 2];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void BuildBMatrixAtCenter(float a, float b, float c, float[] B)
    {
        Array.Clear(B, 0, B.Length);

        float inv8a = 1f / (8f * a);
        float inv8b = 1f / (8f * b);
        float inv8c = 1f / (8f * c);

        int[] sx = { -1, +1, +1, -1, -1, +1, +1, -1 };
        int[] sy = { -1, -1, +1, +1, -1, -1, +1, +1 };
        int[] sz = { -1, -1, -1, -1, +1, +1, +1, +1 };

        for (int i = 0; i < 8; i++)
        {
            int col = i * 3;
            float dNdx = sx[i] * inv8a;
            float dNdy = sy[i] * inv8b;
            float dNdz = sz[i] * inv8c;

            B[0 * 24 + col + 0] = dNdx;
            B[1 * 24 + col + 1] = dNdy;
            B[2 * 24 + col + 2] = dNdz;
            B[3 * 24 + col + 0] = dNdy;
            B[3 * 24 + col + 1] = dNdx;
            B[4 * 24 + col + 0] = dNdz;
            B[4 * 24 + col + 2] = dNdx;
            B[5 * 24 + col + 1] = dNdz;
            B[5 * 24 + col + 2] = dNdy;
        }
    }

    private void CopyWrapper<T>(ArrayWrapper<T> source, ArrayWrapper<T> destination) where T : struct
    {
        const int chunkSize = 1024 * 1024;
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

    private void Scale(ArrayWrapper<float> v, float a)
    {
        const int CH = 1_048_576; var buf = new float[CH];
        for (long i = 0; i < v.Length; i += CH)
        {
            int n = (int)Math.Min(CH, v.Length - i);
            if (buf.Length != n) buf = new float[n];
            v.ReadChunk(i, buf);
            if (_isSimdAccelerated)
            {
                int stride = Vector<float>.Count; var va = new Vector<float>(a);
                int j = 0; for (; j <= n - stride; j += stride)
                { var vx = new Vector<float>(buf, j); (vx * va).CopyTo(buf, j); }
                for (; j < n; j++) buf[j] *= a;
            }
            else for (int j = 0; j < n; j++) buf[j] *= a;
            v.WriteChunk(i, buf);
        }
    }

    private void AddInPlace(ArrayWrapper<float> y, ArrayWrapper<float> x)
    {
        const int CH = 1_048_576; var bx = new float[CH]; var by = new float[CH];
        for (long i = 0; i < y.Length; i += CH)
        {
            int n = (int)Math.Min(CH, y.Length - i);
            if (bx.Length != n) { bx = new float[n]; by = new float[n]; }
            y.ReadChunk(i, by); x.ReadChunk(i, bx);
            if (_isSimdAccelerated)
            {
                int stride = Vector<float>.Count; int j = 0;
                for (; j <= n - stride; j += stride)
                { var vx = new Vector<float>(bx, j); var vy = new Vector<float>(by, j); (vx + vy).CopyTo(by, j); }
                for (; j < n; j++) by[j] += bx[j];
            }
            else for (int j = 0; j < n; j++) by[j] += bx[j];
            y.WriteChunk(i, by);
        }
    }

    private void ElementWiseMultiply(ArrayWrapper<float> a, ArrayWrapper<float> b, ArrayWrapper<float> outv)
    {
        const int CH = 1_048_576; var ba = new float[CH]; var bb = new float[CH];
        for (long i = 0; i < outv.Length; i += CH)
        {
            int n = (int)Math.Min(CH, outv.Length - i);
            if (ba.Length != n) { ba = new float[n]; bb = new float[n]; }
            a.ReadChunk(i, ba); b.ReadChunk(i, bb);
            if (_isSimdAccelerated)
            {
                int stride = Vector<float>.Count; int j = 0;
                for (; j <= n - stride; j += stride)
                { var vx = new Vector<float>(ba, j); var vy = new Vector<float>(bb, j); (vx * vy).CopyTo(ba, j); }
                for (; j < n; j++) ba[j] = ba[j] * bb[j];
            }
            else for (int j = 0; j < n; j++) ba[j] = ba[j] * bb[j];
            outv.WriteChunk(i, ba);
        }
    }

    private void SubtractWrappers(ArrayWrapper<float> a, ArrayWrapper<float> b, ArrayWrapper<float> outv)
    {
        const int CH = 1_048_576; var ba = new float[CH]; var bb = new float[CH];
        for (long i = 0; i < outv.Length; i += CH)
        {
            int n = (int)Math.Min(CH, outv.Length - i);
            if (ba.Length != n) { ba = new float[n]; bb = new float[n]; }
            a.ReadChunk(i, ba); b.ReadChunk(i, bb);
            if (_isSimdAccelerated)
            {
                int stride = Vector<float>.Count; int j = 0;
                for (; j <= n - stride; j += stride)
                { var vx = new Vector<float>(ba, j); var vy = new Vector<float>(bb, j); (vx - vy).CopyTo(ba, j); }
                for (; j < n; j++) ba[j] = ba[j] - bb[j];
            }
            else for (int j = 0; j < n; j++) ba[j] = ba[j] - bb[j];
            outv.WriteChunk(i, ba);
        }
    }

    private float DotProduct(ArrayWrapper<float> a, ArrayWrapper<float> b)
    {
        const int CH = 1_048_576; var ba = new float[CH]; var bb = new float[CH];
        double total = 0d;
        object lockObj = new object();
        Parallel.For(0, (int)((a.Length + CH - 1) / CH), t =>
        {
            long start = (long)t * CH;
            int n = (int)Math.Min(CH, a.Length - start);
            var la = new float[n]; var lb = new float[n];
            a.ReadChunk(start, la); b.ReadChunk(start, lb);
            double sum = 0d;
            if (Vector.IsHardwareAccelerated && n >= Vector<float>.Count)
            {
                int s = Vector<float>.Count; int j = 0; var acc = Vector<float>.Zero;
                for (; j <= n - s; j += s)
                { var va = new Vector<float>(la, j); var vb = new Vector<float>(lb, j); acc += va * vb; }
                var tmp = new float[s]; acc.CopyTo(tmp);
                for (int k = 0; k < s; k++) sum += tmp[k];
                for (; j < n; j++) sum += la[j] * lb[j];
            }
            else for (int j = 0; j < n; j++) sum += la[j] * lb[j];
            lock (lockObj) total += sum;
        });
        return (float)total;
    }

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

    private void MatrixVectorProduct(ArrayWrapper<float> result, ArrayWrapper<float> vector,
                                 IProgress<float> progress, float progressStart, float progressSpan)
{
    // CRITICAL DIAGNOSTIC: Verify input vector has non-zero values
    long inputNonZero = 0;
    float inputMax = 0;
    const int CHECK_CHUNK = 1_048_576;
    var checkBuffer = new float[CHECK_CHUNK];
    for (long i = 0; i < Math.Min(vector.Length, CHECK_CHUNK * 10); i += CHECK_CHUNK)
    {
        int chunkSize = (int)Math.Min(CHECK_CHUNK, vector.Length - i);
        if (chunkSize != checkBuffer.Length) checkBuffer = new float[chunkSize];
        vector.ReadChunk(i, checkBuffer);
        for (int j = 0; j < chunkSize; j++)
        {
            if (MathF.Abs(checkBuffer[j]) > 1e-12f)
            {
                inputNonZero++;
                inputMax = MathF.Max(inputMax, MathF.Abs(checkBuffer[j]));
            }
        }
    }
    Logger.Log($"[GeomechCPU] MVP input vector: {inputNonZero:N0} non-zero in first 10M entries, max = {inputMax*1000:F6} μm");
    
    const int ZERO_CHUNK = 4_194_304;
    var zeroBuffer = new float[ZERO_CHUNK];
    for (long i = 0; i < result.Length; i += ZERO_CHUNK)
    {
        int chunkSize = (int)Math.Min(ZERO_CHUNK, result.Length - i);
        if (chunkSize != zeroBuffer.Length) zeroBuffer = new float[chunkSize];
        Array.Clear(zeroBuffer, 0, chunkSize);
        result.WriteChunk(i, zeroBuffer);
    }

    const int CACHE_ELEMENTS = 256;
    const int CACHE_NODES = 2048;
    
    int workers = Environment.ProcessorCount;
    int elementsPerWorker = (_numElements + workers - 1) / workers;
    
    var accumulators = new ThreadLocalAccumulator[workers];
    for (int i = 0; i < workers; i++)
    {
        accumulators[i] = new ThreadLocalAccumulator(result.Length, 8_388_608);
    }
    
    // DIAGNOSTIC: Track total contributions
    long totalContributions = 0;
    float maxContribution = 0;
    long elementsProcessed = 0;
    long elementsSkipped = 0;
    long nonZeroDisplacements = 0;
    object diagLock = new object();
    
    Parallel.For(0, workers, workerId =>
    {
        var accumulator = accumulators[workerId];
        int startElem = workerId * elementsPerWorker;
        int endElem = Math.Min(startElem + elementsPerWorker, _numElements);
        
        // Caches for node data
        var nodeXCache = new Dictionary<int, float>(CACHE_NODES);
        var nodeYCache = new Dictionary<int, float>(CACHE_NODES);
        var nodeZCache = new Dictionary<int, float>(CACHE_NODES);
        var vectorCache = new Dictionary<int, (float, float, float)>(CACHE_NODES);
        
        long localElemsProcessed = 0;
        long localElemsSkipped = 0;
        long localNonZeroDisp = 0;
        bool firstElemLogged = false;
        bool geometryLogged = false;
        
        for (int batchStart = startElem; batchStart < endElem; batchStart += CACHE_ELEMENTS)
        {
            int batchSize = Math.Min(CACHE_ELEMENTS, endElem - batchStart);
            
            // CRITICAL FIX: Read directly into the buffer
            var elemNodeBuffer = new int[batchSize * 8];
            var elemEBuffer = new float[batchSize];
            var elemNuBuffer = new float[batchSize];
            
            _elementNodes.ReadChunk((long)batchStart * 8, elemNodeBuffer);
            _elementE.ReadChunk(batchStart, elemEBuffer);
            _elementNu.ReadChunk(batchStart, elemNuBuffer);
            
            // Collect unique nodes for this batch
            var uniqueNodes = new HashSet<int>();
            for (int i = 0; i < batchSize * 8; i++)
            {
                uniqueNodes.Add(elemNodeBuffer[i]);
            }
            
            // Load all unique nodes into cache BEFORE purging
            foreach (int nodeId in uniqueNodes)
            {
                if (!nodeXCache.ContainsKey(nodeId))
                {
                    nodeXCache[nodeId] = _nodeX[nodeId];
                    nodeYCache[nodeId] = _nodeY[nodeId];
                    nodeZCache[nodeId] = _nodeZ[nodeId];
                    
                    // DIAGNOSTIC: Check first few node coordinates
                    if (!geometryLogged && workerId == 0 && nodeXCache.Count <= 3)
                    {
                        Logger.Log($"[GeomechCPU] Node {nodeId}: x={nodeXCache[nodeId]:F6}, y={nodeYCache[nodeId]:F6}, z={nodeZCache[nodeId]:F6}");
                        if (nodeXCache.Count == 3) geometryLogged = true;
                    }
                }
                
                if (!vectorCache.ContainsKey(nodeId))
                {
                    // CRITICAL FIX: Read displacement components individually
                    long baseIdx = (long)nodeId * 3;
                    float ux = vector[baseIdx + 0];
                    float uy = vector[baseIdx + 1];
                    float uz = vector[baseIdx + 2];
                    vectorCache[nodeId] = (ux, uy, uz);
                }
            }
            
            // NOW process all elements in the batch
            for (int elemIdx = 0; elemIdx < batchSize; elemIdx++)
            {
                int elemOffset = elemIdx * 8;
                
                int n0 = elemNodeBuffer[elemOffset + 0];
                int n1 = elemNodeBuffer[elemOffset + 1];
                int n2 = elemNodeBuffer[elemOffset + 2];
                int n3 = elemNodeBuffer[elemOffset + 3];
                int n4 = elemNodeBuffer[elemOffset + 4];
                int n5 = elemNodeBuffer[elemOffset + 5];
                int n6 = elemNodeBuffer[elemOffset + 6];
                int n7 = elemNodeBuffer[elemOffset + 7];
                
                var elemDisp = new float[24];
                var (u0x, u0y, u0z) = vectorCache[n0];
                elemDisp[0] = u0x; elemDisp[1] = u0y; elemDisp[2] = u0z;
                var (u1x, u1y, u1z) = vectorCache[n1];
                elemDisp[3] = u1x; elemDisp[4] = u1y; elemDisp[5] = u1z;
                var (u2x, u2y, u2z) = vectorCache[n2];
                elemDisp[6] = u2x; elemDisp[7] = u2y; elemDisp[8] = u2z;
                var (u3x, u3y, u3z) = vectorCache[n3];
                elemDisp[9] = u3x; elemDisp[10] = u3y; elemDisp[11] = u3z;
                var (u4x, u4y, u4z) = vectorCache[n4];
                elemDisp[12] = u4x; elemDisp[13] = u4y; elemDisp[14] = u4z;
                var (u5x, u5y, u5z) = vectorCache[n5];
                elemDisp[15] = u5x; elemDisp[16] = u5y; elemDisp[17] = u5z;
                var (u6x, u6y, u6z) = vectorCache[n6];
                elemDisp[18] = u6x; elemDisp[19] = u6y; elemDisp[20] = u6z;
                var (u7x, u7y, u7z) = vectorCache[n7];
                elemDisp[21] = u7x; elemDisp[22] = u7y; elemDisp[23] = u7z;
                
                float E = elemEBuffer[elemIdx];
                float nu = elemNuBuffer[elemIdx];
                
                float x0 = nodeXCache[n0], x6 = nodeXCache[n6];
                float y0 = nodeYCache[n0], y6 = nodeYCache[n6];
                float z0 = nodeZCache[n0], z6 = nodeZCache[n6];
                float a = (x6 - x0) * 0.5f;
                float b = (y6 - y0) * 0.5f;
                float c = (z6 - z0) * 0.5f;
                
                // DIAGNOSTIC: Log first element geometry
                if (!firstElemLogged && workerId == 0 && localElemsProcessed + localElemsSkipped == 0)
                {
                    Logger.Log($"[GeomechCPU] First elem: nodes n0={n0}, n1={n1}, n6={n6}, n7={n7}");
                    Logger.Log($"[GeomechCPU] First elem: x0={x0:F6}, x6={x6:F6}, a={a:F6}");
                    Logger.Log($"[GeomechCPU] First elem: y0={y0:F6}, y6={y6:F6}, b={b:F6}");
                    Logger.Log($"[GeomechCPU] First elem: z0={z0:F6}, z6={z6:F6}, c={c:F6}");
                }
                
                if (Math.Abs(a) < 1e-12f || Math.Abs(b) < 1e-12f || Math.Abs(c) < 1e-12f)
                {
                    localElemsSkipped++;
                    continue;
                }
                
                localElemsProcessed++;
                
                // Check if displacement is non-zero
                bool hasDisp = false;
                for (int i = 0; i < 24; i++)
                {
                    if (MathF.Abs(elemDisp[i]) > 1e-12f)
                    {
                        hasDisp = true;
                        break;
                    }
                }
                if (hasDisp) localNonZeroDisp++;
                
                // DIAGNOSTIC: Log first element with non-zero displacement
                if (!firstElemLogged && hasDisp && workerId == 0)
                {
                    firstElemLogged = true;
                    Logger.Log($"[GeomechCPU] First element: nodes={n0},{n1},...,{n7}");
                    Logger.Log($"[GeomechCPU] First element: u_max={elemDisp.Max(x => MathF.Abs(x)):E6}");
                    Logger.Log($"[GeomechCPU] First element: geometry a={a:E6}, b={b:E6}, c={c:E6}");
                    Logger.Log($"[GeomechCPU] First element: E={E:E6}, nu={nu:F3}");
                }
                
                float f = E / ((1.0f + nu) * (1.0f - 2.0f * nu));
                float c11 = f * (1 - nu);
                float c12 = f * nu;
                float c44 = f * (1 - 2 * nu) * 0.5f;
                float detJ = 8f * a * b * c;
                
                var B = new float[6 * 24];
                BuildBMatrixAtCenter(a, b, c, B);
                
                var DB = new float[6 * 24];
                for (int j = 0; j < 24; j++)
                {
                    float Bx = B[0 * 24 + j];
                    float By = B[1 * 24 + j];
                    float Bz = B[2 * 24 + j];
                    DB[0 * 24 + j] = c11 * Bx + c12 * (By + Bz);
                    DB[1 * 24 + j] = c11 * By + c12 * (Bx + Bz);
                    DB[2 * 24 + j] = c11 * Bz + c12 * (Bx + By);
                    DB[3 * 24 + j] = c44 * B[3 * 24 + j];
                    DB[4 * 24 + j] = c44 * B[4 * 24 + j];
                    DB[5 * 24 + j] = c44 * B[5 * 24 + j];
                }
                
                var tmp6 = new float[6];
                for (int i = 0; i < 6; i++)
                {
                    float sum = 0;
                    int row = i * 24;
                    for (int j = 0; j < 24; j++)
                        sum += DB[row + j] * elemDisp[j];
                    tmp6[i] = sum;
                }
                
                var ye = new float[24];
                for (int j = 0; j < 24; j++)
                {
                    float sum = 0;
                    for (int i = 0; i < 6; i++)
                        sum += B[i * 24 + j] * tmp6[i];
                    ye[j] = sum * detJ;
                }
                
                // DIAGNOSTIC: Check if ye is non-zero
                float ye_max = ye.Max(y => MathF.Abs(y));
                if (!firstElemLogged && ye_max > 1e-12f && workerId == 0 && localElemsProcessed == 1)
                {
                    Logger.Log($"[GeomechCPU] First element: ye_max={ye_max:E6}, detJ={detJ:E6}");
                }
                
                int[] nodes = { n0, n1, n2, n3, n4, n5, n6, n7 };
                for (int i = 0; i < 8; i++)
                {
                    long dofBase = (long)nodes[i] * 3;
                    accumulator.Add(dofBase + 0, ye[i * 3 + 0]);
                    accumulator.Add(dofBase + 1, ye[i * 3 + 1]);
                    accumulator.Add(dofBase + 2, ye[i * 3 + 2]);
                    
                    // DIAGNOSTIC: Track contributions
                    lock (diagLock)
                    {
                        if (MathF.Abs(ye[i * 3 + 0]) > 1e-12f) totalContributions++;
                        if (MathF.Abs(ye[i * 3 + 1]) > 1e-12f) totalContributions++;
                        if (MathF.Abs(ye[i * 3 + 2]) > 1e-12f) totalContributions++;
                        maxContribution = MathF.Max(maxContribution, MathF.Abs(ye[i * 3 + 0]));
                        maxContribution = MathF.Max(maxContribution, MathF.Abs(ye[i * 3 + 1]));
                        maxContribution = MathF.Max(maxContribution, MathF.Abs(ye[i * 3 + 2]));
                    }
                }
            }
            
            // AFTER processing all elements, purge cache if it's too large
            if (nodeXCache.Count > CACHE_NODES * 2)
            {
                var keysToRemove = nodeXCache.Keys.Take(CACHE_NODES / 2).ToList();
                foreach (var key in keysToRemove)
                {
                    nodeXCache.Remove(key);
                    nodeYCache.Remove(key);
                    nodeZCache.Remove(key);
                    vectorCache.Remove(key);
                }
            }
            
            if (progress != null && batchStart % 10000 == 0)
            {
                float frac = (float)(batchStart - startElem) / Math.Max(1, endElem - startElem);
                progress.Report(progressStart + frac * progressSpan * 0.98f);
            }
        }
        
        // Report local statistics
        lock (diagLock)
        {
            elementsProcessed += localElemsProcessed;
            elementsSkipped += localElemsSkipped;
            nonZeroDisplacements += localNonZeroDisp;
        }
    });
    
    Logger.Log($"[GeomechCPU] MVP: Processed {elementsProcessed:N0} elements, skipped {elementsSkipped:N0}");
    Logger.Log($"[GeomechCPU] MVP: Elements with non-zero displacement: {nonZeroDisplacements:N0}");
    Logger.Log($"[GeomechCPU] MVP: Generated {totalContributions:N0} non-zero element contributions, max = {maxContribution:E6}");
    
    for (int i = 0; i < workers; i++)
    {
        accumulators[i].FlushToResult(result);
        accumulators[i].Dispose();
    }
    
    progress?.Report(progressStart + progressSpan);
    ApplyDirichletToVector(result, setToZero: true);
}
    private sealed class ThreadLocalAccumulator : IDisposable
    {
        private readonly Dictionary<long, float> _contributions;
        private readonly int _maxSize;
        
        public ThreadLocalAccumulator(long totalDofs, int maxSize)
        {
            _contributions = new Dictionary<long, float>((int)Math.Min(maxSize, totalDofs / 10));
            _maxSize = maxSize;
        }
        
        public void Add(long index, float value)
        {
            if (_contributions.TryGetValue(index, out float existing))
            {
                _contributions[index] = existing + value;
            }
            else
            {
                _contributions[index] = value;
            }
        }
        
        public void FlushToResult(ArrayWrapper<float> result)
        {
            if (_contributions.Count == 0) return;

            var sortedPairs = _contributions.OrderBy(kvp => kvp.Key).ToList();
            
            const int CHUNK_SIZE = 1_048_576;
            var buffer = new float[CHUNK_SIZE];
            long currentChunkStart = -1;
            
            foreach (var kvp in sortedPairs)
            {
                long index = kvp.Key;
                float value = kvp.Value;
                
                long chunkStart = (index / CHUNK_SIZE) * CHUNK_SIZE;
                int offset = (int)(index % CHUNK_SIZE);
                
                if (chunkStart != currentChunkStart)
                {
                    if (currentChunkStart >= 0)
                    {
                        int chunkSizeToWrite = (int)Math.Min(CHUNK_SIZE, result.Length - currentChunkStart);
                        var chunkToWrite = new float[chunkSizeToWrite];
                        Array.Copy(buffer, 0, chunkToWrite, 0, chunkSizeToWrite);
                        result.WriteChunk(currentChunkStart, chunkToWrite);
                    }
                    
                    currentChunkStart = chunkStart;
                    int chunkSizeToRead = (int)Math.Min(CHUNK_SIZE, result.Length - currentChunkStart);
                    var chunkToRead = new float[chunkSizeToRead];
                    result.ReadChunk(currentChunkStart, chunkToRead);
                    
                    Array.Clear(buffer, 0, buffer.Length);
                    Array.Copy(chunkToRead, 0, buffer, 0, chunkSizeToRead);
                }
                
                if(offset < buffer.Length)
                {
                    buffer[offset] += value;
                }
            }
            
            if (currentChunkStart >= 0)
            {
                int finalChunkSize = (int)Math.Min(CHUNK_SIZE, result.Length - currentChunkStart);
                var finalChunk = new float[finalChunkSize];
                Array.Copy(buffer, 0, finalChunk, 0, finalChunkSize);
                result.WriteChunk(currentChunkStart, finalChunk);
            }
        }
        
        public void Dispose()
        {
            _contributions.Clear();
        }
    }

    private void BuildPreconditioner(ArrayWrapper<float> M_inv, IProgress<float> progress,
                                 float progressStart, float progressSpan)
{
    const int ZERO_CHUNK = 4_194_304;
    var zeroBuffer = new float[ZERO_CHUNK];
    for (long i = 0; i < M_inv.Length; i += ZERO_CHUNK)
    {
        int chunkSize = (int)Math.Min(ZERO_CHUNK, M_inv.Length - i);
        if (chunkSize != zeroBuffer.Length) zeroBuffer = new float[chunkSize];
        Array.Clear(zeroBuffer, 0, chunkSize);
        M_inv.WriteChunk(i, zeroBuffer);
    }
    
    const int CACHE_ELEMENTS = 256;
    const int CACHE_NODES = 2048;
    
    int workers = Environment.ProcessorCount;
    int elementsPerWorker = (_numElements + workers - 1) / workers;
    
    var diagonalAccumulators = new ThreadLocalAccumulator[workers];
    for (int i = 0; i < workers; i++)
    {
        diagonalAccumulators[i] = new ThreadLocalAccumulator(_numDOFs, 4_194_304);
    }
    
    Parallel.For(0, workers, workerId =>
    {
        var accumulator = diagonalAccumulators[workerId];
        int startElem = workerId * elementsPerWorker;
        int endElem = Math.Min(startElem + elementsPerWorker, _numElements);
        
        // Caches for node data
        var nodeXCache = new Dictionary<int, float>(CACHE_NODES);
        var nodeYCache = new Dictionary<int, float>(CACHE_NODES);
        var nodeZCache = new Dictionary<int, float>(CACHE_NODES);
        
        for (int batchStart = startElem; batchStart < endElem; batchStart += CACHE_ELEMENTS)
        {
            int batchSize = Math.Min(CACHE_ELEMENTS, endElem - batchStart);
            
            // CRITICAL FIX: Read directly into new buffers
            var elemNodeBuffer = new int[batchSize * 8];
            var elemEBuffer = new float[batchSize];
            var elemNuBuffer = new float[batchSize];
            
            _elementNodes.ReadChunk((long)batchStart * 8, elemNodeBuffer);
            _elementE.ReadChunk(batchStart, elemEBuffer);
            _elementNu.ReadChunk(batchStart, elemNuBuffer);
            
            // Collect unique nodes for this batch
            var uniqueNodes = new HashSet<int>();
            for (int i = 0; i < batchSize * 8; i++)
            {
                uniqueNodes.Add(elemNodeBuffer[i]);
            }
            
            // Load all unique nodes into cache BEFORE processing elements
            foreach (int nodeId in uniqueNodes)
            {
                if (!nodeXCache.ContainsKey(nodeId))
                {
                    nodeXCache[nodeId] = _nodeX[nodeId];
                    nodeYCache[nodeId] = _nodeY[nodeId];
                    nodeZCache[nodeId] = _nodeZ[nodeId];
                }
            }
            
            // NOW process all elements in the batch
            for (int elemIdx = 0; elemIdx < batchSize; elemIdx++)
            {
                int elemOffset = elemIdx * 8;
                
                int n0 = elemNodeBuffer[elemOffset + 0];
                int n1 = elemNodeBuffer[elemOffset + 1];
                int n2 = elemNodeBuffer[elemOffset + 2];
                int n3 = elemNodeBuffer[elemOffset + 3];
                int n4 = elemNodeBuffer[elemOffset + 4];
                int n5 = elemNodeBuffer[elemOffset + 5];
                int n6 = elemNodeBuffer[elemOffset + 6];
                int n7 = elemNodeBuffer[elemOffset + 7];
                
                float E = elemEBuffer[elemIdx];
                float nu = elemNuBuffer[elemIdx];
                
                float x0 = nodeXCache[n0], x6 = nodeXCache[n6];
                float y0 = nodeYCache[n0], y6 = nodeYCache[n6];
                float z0 = nodeZCache[n0], z6 = nodeZCache[n6];
                
                float a = (x6 - x0) * 0.5f;
                float b = (y6 - y0) * 0.5f;
                float c = (z6 - z0) * 0.5f;
                
                if (Math.Abs(a) < 1e-12f || Math.Abs(b) < 1e-12f || Math.Abs(c) < 1e-12f)
                    continue;
                
                float f = E / ((1.0f + nu) * (1.0f - 2.0f * nu));
                float c11 = f * (1 - nu);
                float c12 = f * nu;
                float c44 = f * (1 - 2 * nu) * 0.5f;
                float detJ = 8f * a * b * c;
                
                var B = new float[6 * 24];
                BuildBMatrixAtCenter(a, b, c, B);
                
                var DB = new float[6 * 24];
                for (int j = 0; j < 24; j++)
                {
                    float Bx = B[0 * 24 + j];
                    float By = B[1 * 24 + j];
                    float Bz = B[2 * 24 + j];
                    DB[0 * 24 + j] = c11 * Bx + c12 * (By + Bz);
                    DB[1 * 24 + j] = c11 * By + c12 * (Bx + Bz);
                    DB[2 * 24 + j] = c11 * Bz + c12 * (Bx + By);
                    DB[3 * 24 + j] = c44 * B[3 * 24 + j];
                    DB[4 * 24 + j] = c44 * B[4 * 24 + j];
                    DB[5 * 24 + j] = c44 * B[5 * 24 + j];
                }
                
                int[] nodes = { n0, n1, n2, n3, n4, n5, n6, n7 };
                for (int j = 0; j < 24; j++)
                {
                    float kii = 0f;
                    for (int i = 0; i < 6; i++)
                    {
                        kii += B[i * 24 + j] * DB[i * 24 + j];
                    }
                    kii *= detJ;
                    
                    int nodeLocal = j / 3;
                    int comp = j % 3;
                    long dofIdx = (long)nodes[nodeLocal] * 3 + comp;
                    accumulator.Add(dofIdx, kii);
                }
            }
            
            // AFTER processing all elements, purge cache if it's too large
            if (nodeXCache.Count > CACHE_NODES * 2)
            {
                var keysToRemove = nodeXCache.Keys.Take(CACHE_NODES / 2).ToList();
                foreach (var key in keysToRemove)
                {
                    nodeXCache.Remove(key);
                    nodeYCache.Remove(key);
                    nodeZCache.Remove(key);
                }
            }
            
            if (progress != null && batchStart % 10000 == 0)
            {
                float frac = (float)(batchStart - startElem) / Math.Max(1, endElem - startElem);
                progress.Report(progressStart + frac * progressSpan * 0.98f);
            }
        }
    });
    
    for (int i = 0; i < workers; i++)
    {
        diagonalAccumulators[i].FlushToResult(M_inv);
        diagonalAccumulators[i].Dispose();
    }
    
    const int INV_CHUNK = 1_048_576;
    var diagBuffer = new float[INV_CHUNK];
    var isDirBuffer = new bool[INV_CHUNK];
    
    for (long i = 0; i < M_inv.Length; i += INV_CHUNK)
    {
        int chunkSize = (int)Math.Min(INV_CHUNK, M_inv.Length - i);
        if (chunkSize != diagBuffer.Length)
        {
            diagBuffer = new float[chunkSize];
            isDirBuffer = new bool[chunkSize];
        }
        
        M_inv.ReadChunk(i, diagBuffer);
        _isDirichlet.ReadChunk(i, isDirBuffer);
        
        for (int j = 0; j < chunkSize; j++)
        {
            if (isDirBuffer[j])
            {
                diagBuffer[j] = 1f;
            }
            else
            {
                diagBuffer[j] = (diagBuffer[j] > 1e-12f) ? 1f / diagBuffer[j] : 0f;
            }
        }
        
        M_inv.WriteChunk(i, diagBuffer);
    }
    
    progress?.Report(progressStart + progressSpan);
}

    private void Saxpy(ArrayWrapper<float> y, ArrayWrapper<float> x, float a)
    {
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
                for (int j = currentChunkSize - (currentChunkSize % vecSize); j < currentChunkSize; j++) 
                    bufferY[j] += a * bufferX[j];
            }
            else
            {
                for (int j = 0; j < currentChunkSize; j++) 
                    bufferY[j] += a * bufferX[j];
            }

            y.WriteChunk(i, bufferY);
        }
    }

    private GeomechanicalResults CalculateStresses(byte[,,] labels, BoundingBox extent)
    {
        var w = extent.Width;
        var h = extent.Height; 
        var d = extent.Depth;
        
        bool useOffload = _params.EnableOffloading || ((long)w * h * d * 6 * 4) > 500_000_000;
        string offloadDir = _params.OffloadDirectory ?? Path.GetTempPath();
        
        var results = new GeomechanicalResults
        {
            MaterialLabels = labels,
            Parameters = _params
        };
        
        if (useOffload)
        {
            Directory.CreateDirectory(offloadDir);
            results.StressXX = new float[w, h, d];
            results.StressYY = new float[w, h, d];
            results.StressZZ = new float[w, h, d];
            results.StressXY = new float[w, h, d];
            results.StressXZ = new float[w, h, d];
            results.StressYZ = new float[w, h, d];
            
            Logger.Log($"[GeomechCPU] Using chunked stress calculation for large dataset");
        }
        else
        {
            results.StressXX = new float[w, h, d];
            results.StressYY = new float[w, h, d];
            results.StressZZ = new float[w, h, d];
            results.StressXY = new float[w, h, d];
            results.StressXZ = new float[w, h, d];
            results.StressYZ = new float[w, h, d];
        }

        const int BATCH_SIZE = 1024;
        int numBatches = (_numElements + BATCH_SIZE - 1) / BATCH_SIZE;
        
        var stressAccumulators = new ConcurrentDictionary<(int, int, int), float[]>();
        object lockObj = new object();
        
        Parallel.For(0, numBatches, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, (int batchIdx) =>
        {
            int startElem = batchIdx * BATCH_SIZE;
            int endElem = Math.Min(startElem + BATCH_SIZE, _numElements);
            
            // CRITICAL FIX: Read directly into new buffers
            int actualBatchSize = endElem - startElem;
            var elemNodesBatch = new int[actualBatchSize * 8];
            var elemEBatch = new float[actualBatchSize];
            var elemNuBatch = new float[actualBatchSize];
            
            _elementNodes.ReadChunk((long)startElem * 8, elemNodesBatch);
            _elementE.ReadChunk(startElem, elemEBatch);
            _elementNu.ReadChunk(startElem, elemNuBatch);
            
            var nodeDataCache = new Dictionary<int, (float x, float y, float z, float ux, float uy, float uz)>();
            for (int i = 0; i < actualBatchSize * 8; i++)
            {
                int nodeId = elemNodesBatch[i];
                if (!nodeDataCache.ContainsKey(nodeId))
                {
                    float x = _nodeX[nodeId];
                    float y = _nodeY[nodeId];
                    float z = _nodeZ[nodeId];
                    // CRITICAL FIX: Read displacement components individually
                    long dofBase = (long)nodeId * 3;
                    float ux = _displacement[dofBase + 0];
                    float uy = _displacement[dofBase + 1];
                    float uz = _displacement[dofBase + 2];
                    nodeDataCache[nodeId] = (x, y, z, ux, uy, uz);
                }
            }
            
            for (int elemIdx = 0; elemIdx < actualBatchSize; elemIdx++)
            {
                int elemOffset = elemIdx * 8;
                
                var elemNodes = new int[8];
                for (int i = 0; i < 8; i++)
                    elemNodes[i] = elemNodesBatch[elemOffset + i];
                
                var elemDisp = new float[24];
                for (int i = 0; i < 8; i++)
                {
                    var nodeData = nodeDataCache[elemNodes[i]];
                    elemDisp[i * 3 + 0] = nodeData.ux;
                    elemDisp[i * 3 + 1] = nodeData.uy;
                    elemDisp[i * 3 + 2] = nodeData.uz;
                }
                
                var node0 = nodeDataCache[elemNodes[0]];
                var node6 = nodeDataCache[elemNodes[6]];
                float a = (node6.x - node0.x) / 2.0f;
                float b = (node6.y - node0.y) / 2.0f;
                float c_elem = (node6.z - node0.z) / 2.0f;
                
                if (Math.Abs(a) < 1e-12f || Math.Abs(b) < 1e-12f || Math.Abs(c_elem) < 1e-12f)
                    continue;
                
                var B = new float[6, 24];
                float inv_a = 1.0f / a;
                float inv_b = 1.0f / b;
                float inv_c = 1.0f / c_elem;
                
                float[] dN_dxi = {-0.125f, 0.125f, 0.125f, -0.125f, -0.125f, 0.125f, 0.125f, -0.125f};
                float[] dN_deta = {-0.125f, -0.125f, 0.125f, 0.125f, -0.125f, -0.125f, 0.125f, 0.125f};
                float[] dN_dzeta = {-0.125f, -0.125f, -0.125f, -0.125f, 0.125f, 0.125f, 0.125f, 0.125f};
                
                for (int i = 0; i < 8; i++)
                {
                    float dN_dx = dN_dxi[i] * inv_a;
                    float dN_dy = dN_deta[i] * inv_b;
                    float dN_dz = dN_dzeta[i] * inv_c;
                    
                    B[0, i * 3] = dN_dx;
                    B[1, i * 3 + 1] = dN_dy;
                    B[2, i * 3 + 2] = dN_dz;
                    B[3, i * 3] = dN_dy;
                    B[3, i * 3 + 1] = dN_dx;
                    B[4, i * 3 + 1] = dN_dz;
                    B[4, i * 3 + 2] = dN_dy;
                    B[5, i * 3] = dN_dz;
                    B[5, i * 3 + 2] = dN_dx;
                }
                
                var strain = new float[6];
                for (int i = 0; i < 6; i++)
                {
                    float sum = 0;
                    for (int j = 0; j < 24; j++)
                        sum += B[i, j] * elemDisp[j];
                    strain[i] = sum;
                }
                
                float E = elemEBatch[elemIdx];
                float nu = elemNuBatch[elemIdx];
                float lambda = E * nu / ((1.0f + nu) * (1.0f - 2.0f * nu));
                float mu = E / (2.0f * (1.0f + nu));
                float trace = strain[0] + strain[1] + strain[2];
                
                var stress = new float[6];
                stress[0] = lambda * trace + 2.0f * mu * strain[0];
                stress[1] = lambda * trace + 2.0f * mu * strain[1];
                stress[2] = lambda * trace + 2.0f * mu * strain[2];
                stress[3] = mu * strain[3];
                stress[4] = mu * strain[4];
                stress[5] = mu * strain[5];
                
                float cx = (node0.x + node6.x) / 2.0f;
                float cy = (node0.y + node6.y) / 2.0f;
                float cz = (node0.z + node6.z) / 2.0f;
                var dx_um = _params.PixelSize;
                int vx = (int)Math.Round(cx * 1000 / dx_um);
                int vy = (int)Math.Round(cy * 1000 / dx_um);
                int vz = (int)Math.Round(cz * 1000 / dx_um);
                
                if (vx >= 0 && vx < w && vy >= 0 && vy < h && vz >= 0 && vz < d && labels[vx, vy, vz] != 0)
                {
                    lock (lockObj)
                    {
                        results.StressXX[vx, vy, vz] = stress[0];
                        results.StressYY[vx, vy, vz] = stress[1];
                        results.StressZZ[vx, vy, vz] = stress[2];
                        results.StressXY[vx, vy, vz] = stress[3];
                        results.StressYZ[vx, vy, vz] = stress[4];
                        results.StressXZ[vx, vy, vz] = stress[5];
                    }
                }
            }
        });
        
        return results;
    }

    private void PostProcessResults(GeomechanicalResults results, byte[,,] labels)
    {
        var w = labels.GetLength(0);
        var h = labels.GetLength(1);
        var d = labels.GetLength(2);
        
        results.Sigma1 = new float[w, h, d];
        results.Sigma2 = new float[w, h, d];
        results.Sigma3 = new float[w, h, d];
        results.FailureIndex = new float[w, h, d];
        results.DamageField = new byte[w, h, d];
        results.FractureField = new bool[w, h, d];
        
        var cohesion_Pa = _params.Cohesion * 1e6f;
        var phi = _params.FrictionAngle * MathF.PI / 180f;
        var tensile_Pa = _params.TensileStrength * 1e6f;
        
        const int CHUNK_SIZE = 16;
        int numChunksX = (w + CHUNK_SIZE - 1) / CHUNK_SIZE;
        int numChunksY = (h + CHUNK_SIZE - 1) / CHUNK_SIZE;
        int numChunksZ = (d + CHUNK_SIZE - 1) / CHUNK_SIZE;
        int totalChunks = numChunksX * numChunksY * numChunksZ;
        
        Parallel.For(0, totalChunks, (int chunkIdx) =>
        {
            int cz = chunkIdx / (numChunksX * numChunksY);
            int cy = (chunkIdx / numChunksX) % numChunksY;
            int cx = chunkIdx % numChunksX;
            
            int startX = cx * CHUNK_SIZE;
            int startY = cy * CHUNK_SIZE;
            int startZ = cz * CHUNK_SIZE;
            int endX = Math.Min(startX + CHUNK_SIZE, w);
            int endY = Math.Min(startY + CHUNK_SIZE, h);
            int endZ = Math.Min(startZ + CHUNK_SIZE, d);
            
            for (int z = startZ; z < endZ; z++)
            {
                for (int y = startY; y < endY; y++)
                {
                    for (int x = startX; x < endX; x++)
                    {
                        if (labels[x, y, z] == 0) continue;
                        
                        float sxx = results.StressXX[x, y, z];
                        float syy = results.StressYY[x, y, z];
                        float szz = results.StressZZ[x, y, z];
                        float sxy = results.StressXY[x, y, z];
                        float sxz = results.StressXZ[x, y, z];
                        float syz = results.StressYZ[x, y, z];
                        
                        if (_params.EnablePlasticity)
                        {
                            float mean = (sxx + syy + szz) / 3.0f;
                            float s_dev_xx = sxx - mean;
                            float s_dev_yy = syy - mean;
                            float s_dev_zz = szz - mean;
                            float J2 = 0.5f * (s_dev_xx * s_dev_xx + s_dev_yy * s_dev_yy + s_dev_zz * s_dev_zz) 
                                     + sxy * sxy + sxz * sxz + syz * syz;
                            float vonMises = MathF.Sqrt(3.0f * J2);
                            float yieldStress = _params.Cohesion * 1e6f * 2.0f;
                            
                            if (vonMises > yieldStress)
                            {
                                float scale = yieldStress / vonMises;
                                sxx = mean + s_dev_xx * scale;
                                syy = mean + s_dev_yy * scale;
                                szz = mean + s_dev_zz * scale;
                                sxy *= scale;
                                sxz *= scale;
                                syz *= scale;
                                
                                results.StressXX[x, y, z] = sxx;
                                results.StressYY[x, y, z] = syy;
                                results.StressZZ[x, y, z] = szz;
                                results.StressXY[x, y, z] = sxy;
                                results.StressXZ[x, y, z] = sxz;
                                results.StressYZ[x, y, z] = syz;
                            }
                        }
                        
                        var (s1, s2, s3) = CalculatePrincipalStresses(sxx, syy, szz, sxy, sxz, syz);
                        results.Sigma1[x, y, z] = s1;
                        results.Sigma2[x, y, z] = s2;
                        results.Sigma3[x, y, z] = s3;
                        
                        float fi = CalculateFailureIndex(s1, s2, s3, cohesion_Pa, phi, tensile_Pa);
                        results.FailureIndex[x, y, z] = fi;
                        results.FractureField[x, y, z] = fi >= 1.0f;
                        results.DamageField[x, y, z] = (byte)Math.Clamp(fi * 200.0f, 0.0f, 255.0f);
                    }
                }
            }
        });
    }

    private static (float s1, float s2, float s3) CalculatePrincipalStresses(
        float sxx, float syy, float szz,
        float sxy, float sxz, float syz)
    {
        float i1 = sxx + syy + szz;
        float i2 = (sxx * syy) + (syy * szz) + (szz * sxx) - (sxy * sxy) - (sxz * sxz) - (syz * syz);
        float i3 = (sxx * syy * szz) + (2.0f * sxy * sxz * syz)
                   - (sxx * syz * syz) - (syy * sxz * sxz) - (szz * sxy * sxy);

        float p = (i1 * i1 / 3.0f) - i2;
        float q = i3 + (2.0f * i1 * i1 * i1 - 9.0f * i1 * i2) / 27.0f;
        float r = MathF.Sqrt(p * p * p / 27.0f);
        
        if (r < 1e-9f)
        {
            float val = i1 / 3.0f;
            return (val, val, val);
        }
        
        float phi = MathF.Acos(Math.Clamp(-q / (2.0f * r), -1.0f, 1.0f));

        float l1 = i1 / 3.0f + 2.0f * MathF.Sqrt(p / 3.0f) * MathF.Cos(phi / 3.0f);
        float l2 = i1 / 3.0f + 2.0f * MathF.Sqrt(p / 3.0f) * MathF.Cos((phi + 2.0f * MathF.PI) / 3.0f);
        float l3 = i1 / 3.0f + 2.0f * MathF.Sqrt(p / 3.0f) * MathF.Cos((phi + 4.0f * MathF.PI) / 3.0f);

        if (l1 < l2) (l1, l2) = (l2, l1);
        if (l1 < l3) (l1, l3) = (l3, l1);
        if (l2 < l3) (l2, l3) = (l3, l2);

        return (l1, l2, l3);
    }

    private float CalculateFailureIndex(float s1, float s2, float s3, float cohesion, float phi, float tensile)
    {
        switch (_params.FailureCriterion)
        {
            case FailureCriterion.MohrCoulomb:
                float shear_strength = 2.0f * cohesion * MathF.Cos(phi) + (s1 + s3) * MathF.Sin(phi);
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
                float mb = _params.HoekBrown_mb; 
                float s = _params.HoekBrown_s; 
                float a = _params.HoekBrown_a;
                
                if (s3 < -tensile) return -s3 / tensile;
                float strength = s3 + ucs * MathF.Pow(mb * s3 / ucs + s, a);
                return (strength > 1e-3f) ? s1 / strength : s1;
            
            case FailureCriterion.Griffith:
                if (3 * s1 + s3 < 0)
                {
                    if (s3 < 0) return -s3 / tensile;
                }
                else
                {
                    float strength_denom = 8.0f * tensile * (s1 + s3);
                    if (strength_denom > 1e-3f) return MathF.Pow(s1 - s3, 2) / strength_denom;
                }
                return 0.0f;

            default: 
                return 0.0f;
        }
    }

    private void CalculateFinalStatistics(GeomechanicalResults results)
    {
        var w = results.MaterialLabels.GetLength(0);
        var h = results.MaterialLabels.GetLength(1);
        var d = results.MaterialLabels.GetLength(2);
        
        const int SLICE_SIZE = 10;
        int numSlices = (d + SLICE_SIZE - 1) / SLICE_SIZE;
        
        var threadData = new ThreadLocal<LocalStats>(() => new LocalStats(), trackAllValues: true);
        
        try
        {
            Parallel.For(0, numSlices, (int sliceIdx) =>
            {
                int startZ = sliceIdx * SLICE_SIZE;
                int endZ = Math.Min(startZ + SLICE_SIZE, d);
                var localStats = threadData.Value;
                
                for (int z = startZ; z < endZ; z++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            if (results.MaterialLabels[x, y, z] == 0) continue;
                            
                            localStats.ValidVoxels++;
                            
                            if (results.FractureField != null && results.FractureField[x, y, z])
                                localStats.FailedVoxels++;
                            
                            float sxx = results.StressXX[x, y, z];
                            float syy = results.StressYY[x, y, z];
                            float szz = results.StressZZ[x, y, z];
                            float sxy = results.StressXY[x, y, z];
                            float sxz = results.StressXZ[x, y, z];
                            float syz = results.StressYZ[x, y, z];
                            
                            float mean = (sxx + syy + szz) / 3.0f;
                            localStats.SumMeanStress += mean;
                            
                            float s1 = results.Sigma1?[x, y, z] ?? 0f;
                            float s3 = results.Sigma3?[x, y, z] ?? 0f;
                            float shear = (s1 - s3) / 2.0f;
                            localStats.MaxShear = Math.Max(localStats.MaxShear, shear);
                            
                            float s_dev_xx = sxx - mean;
                            float s_dev_yy = syy - mean;
                            float s_dev_zz = szz - mean;
                            float J2 = 0.5f * (s_dev_xx * s_dev_xx + s_dev_yy * s_dev_yy + s_dev_zz * s_dev_zz) 
                                     + sxy * sxy + sxz * sxz + syz * syz;
                            float vonMises = MathF.Sqrt(3.0f * J2);
                            localStats.SumVonMises += vonMises;
                            localStats.MaxVonMises = Math.Max(localStats.MaxVonMises, vonMises);
                        }
                    }
                }
            });
            
            long totalValidVoxels = 0;
            long totalFailedVoxels = 0;
            double totalSumMeanStress = 0;
            double totalSumVonMises = 0;
            float globalMaxShear = 0;
            float globalMaxVonMises = 0;
            
            foreach (var stats in threadData.Values)
            {
                totalValidVoxels += stats.ValidVoxels;
                totalFailedVoxels += stats.FailedVoxels;
                totalSumMeanStress += stats.SumMeanStress;
                totalSumVonMises += stats.SumVonMises;
                globalMaxShear = Math.Max(globalMaxShear, stats.MaxShear);
                globalMaxVonMises = Math.Max(globalMaxVonMises, stats.MaxVonMises);
            }
            
            if (totalValidVoxels > 0)
            {
                results.MeanStress = (float)(totalSumMeanStress / totalValidVoxels);
                results.MaxShearStress = globalMaxShear;
                results.VonMisesStress_Mean = (float)(totalSumVonMises / totalValidVoxels);
                results.VonMisesStress_Max = globalMaxVonMises;
                results.TotalVoxels = (int)totalValidVoxels;
                results.FailedVoxels = (int)totalFailedVoxels;
                results.FailedVoxelPercentage = 100.0f * totalFailedVoxels / totalValidVoxels;
            }
        }
        finally
        {
            threadData.Dispose();
        }
    }

    private class LocalStats
    {
        public long ValidVoxels;
        public long FailedVoxels;
        public double SumMeanStress;
        public double SumVonMises;
        public float MaxShear;
        public float MaxVonMises;
    }
}