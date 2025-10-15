// GeoscientistToolkit/Analysis/Geomechanics/GeomechanicalSimulatorCPU.cs
// Improved with chunked processing and offloading cache for huge datasets
// ALL PHYSICS PRESERVED - No simplifications

using System.Numerics;
using System.Runtime.CompilerServices;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.Geomechanics;

public class GeomechanicalSimulatorCPU
{
    private const int CHUNK_OVERLAP = 2;

    // FIX: Lock striping instead of per-voxel locks
    private const int NUM_STRESS_LOCKS = 1024;

    private static readonly object[] _stressLocks = Enumerable.Range(0, NUM_STRESS_LOCKS)
        .Select(_ => new object()).ToArray();

    private readonly object _chunkAccessLock = new();
    private readonly HashSet<int> _chunkAccessSet = new();
    private readonly string _offloadPath;
    private readonly GeomechanicalParameters _params;
    private Queue<int> _chunkAccessOrder = new();

    // Chunking infrastructure
    private List<GeomechanicalChunk> _chunks;
    private List<int> _colIdx;
    private long _currentMemoryUsageBytes;
    private float[] _dirichletValue;
    private float[] _displacement;
    private float[] _elementE;
    private int[] _elementNodes;
    private float[] _elementNu;
    private float[] _force;
    private bool[] _isDirichletDOF;
    private bool _isHugeDataset;
    private int _iterationsPerformed;
    private int _maxLoadedChunks;
    private long _maxMemoryBudgetBytes;
    private int[] _nodeToDOF;
    private float[] _nodeX, _nodeY, _nodeZ;
    private int _numDOFs;
    private int _numElements;
    private int _numNodes;
    private List<int> _rowPtr;
    private List<float> _values;

    public GeomechanicalSimulatorCPU(GeomechanicalParameters parameters)
    {
        _params = parameters;

        // FIX: Add input validation
        ValidateParameters();

        if (_params.EnableOffloading && !string.IsNullOrEmpty(_params.OffloadDirectory))
        {
            _offloadPath = Path.Combine(_params.OffloadDirectory, $"geomech_{Guid.NewGuid()}");
            Directory.CreateDirectory(_offloadPath);
        }

        DetectSystemMemory();
    }

    private void ValidateParameters()
    {
        if (_params.Sigma1 < _params.Sigma2 || _params.Sigma2 < _params.Sigma3)
            throw new ArgumentException(
                $"Principal stresses must satisfy σ₁ ≥ σ₂ ≥ σ₃. Got: σ₁={_params.Sigma1}, σ₂={_params.Sigma2}, σ₃={_params.Sigma3}");

        if (_params.PoissonRatio <= 0 || _params.PoissonRatio >= 0.5f)
            throw new ArgumentException(
                $"Poisson's ratio must be in (0, 0.5). Got: {_params.PoissonRatio}");

        if (_params.YoungModulus <= 0)
            throw new ArgumentException($"Young's modulus must be positive. Got: {_params.YoungModulus}");

        if (_params.Cohesion < 0)
            throw new ArgumentException($"Cohesion must be non-negative. Got: {_params.Cohesion}");

        if (_params.FrictionAngle < 0 || _params.FrictionAngle > 70)
            throw new ArgumentException(
                $"Friction angle must be in [0°, 70°]. Got: {_params.FrictionAngle}°");

        if (_params.TensileStrength < 0)
            throw new ArgumentException($"Tensile strength must be non-negative. Got: {_params.TensileStrength}");

        if (_params.Density <= 0)
            throw new ArgumentException($"Density must be positive. Got: {_params.Density}");

        // FIX: More stringent default tolerance
        if (_params.Tolerance > 1e-4f)
            Logger.LogWarning(
                $"[GeomechCPU] Tolerance {_params.Tolerance} is quite loose. Recommend 1e-6 for accuracy.");

        Logger.Log("[GeomechCPU] Parameter validation passed");
    }

    private void DetectSystemMemory()
    {
        try
        {
            var gcMemoryInfo = GC.GetGCMemoryInfo();
            var totalPhysicalMemory = gcMemoryInfo.TotalAvailableMemoryBytes;

            if (totalPhysicalMemory <= 0)
                totalPhysicalMemory = 16L * 1024 * 1024 * 1024;

            _maxMemoryBudgetBytes = (long)(totalPhysicalMemory * 0.75);

            Logger.Log($"[GeomechCPU] Memory budget: {_maxMemoryBudgetBytes / (1024.0 * 1024 * 1024):F2} GB");
        }
        catch
        {
            _maxMemoryBudgetBytes = 12L * 1024 * 1024 * 1024;
        }
    }

    public GeomechanicalResults Simulate(byte[,,] labels, float[,,] density,
        IProgress<float> progress, CancellationToken token)
    {
        var extent = _params.SimulationExtent;
        var startTime = DateTime.Now;

        try
        {
            Logger.Log("[GeomechCPU] ========== STARTING SIMULATION ==========");
            Logger.Log($"[GeomechCPU] Domain: {extent.Width}×{extent.Height}×{extent.Depth} voxels");
            Logger.Log($"[GeomechCPU] Voxel size: {_params.PixelSize} µm");
            Logger.Log(
                $"[GeomechCPU] Loading: σ1={_params.Sigma1} MPa, σ2={_params.Sigma2} MPa, σ3={_params.Sigma3} MPa");

            // STEP 1: Initialize chunking for memory management
            progress?.Report(0.05f);
            Logger.Log("[GeomechCPU] Initializing memory chunks...");
            InitializeChunking(labels, density);
            ValidateChunkBoundaries();
            token.ThrowIfCancellationRequested();

            // STEP 2: Assemble global stiffness matrix
            progress?.Report(0.15f);
            Logger.Log("[GeomechCPU] Assembling global stiffness matrix...");
            AssembleGlobalStiffnessMatrixChunked(progress, token);
            token.ThrowIfCancellationRequested();

            // STEP 3: Apply boundary conditions and loading
            progress?.Report(0.25f);
            Logger.Log("[GeomechCPU] Applying boundary conditions...");
            ApplyBoundaryConditionsAndLoading(labels);
            token.ThrowIfCancellationRequested();

            // STEP 4: Solve displacement field using PCG
            progress?.Report(0.35f);
            Logger.Log("[GeomechCPU] Solving for displacements (PCG)...");
            var converged = SolveDisplacements(progress, token);
            token.ThrowIfCancellationRequested();

            if (!converged)
                Logger.LogWarning("[GeomechCPU] Solver did not fully converge - results may be approximate");

            // STEP 5: Calculate strains and stresses from displacements
            progress?.Report(0.75f);
            Logger.Log("[GeomechCPU] Computing strains and stresses...");
            var results = CalculateStrainsAndStressesChunked(labels, extent, progress, token);
            results.Converged = converged;
            results.IterationsPerformed = _iterationsPerformed;
            token.ThrowIfCancellationRequested();

            // STEP 6: Calculate principal stresses
            progress?.Report(0.85f);
            Logger.Log("[GeomechCPU] Computing principal stresses...");
            CalculatePrincipalStressesChunked(results, labels, progress, token);
            token.ThrowIfCancellationRequested();

            // STEP 7: Evaluate failure with progressive damage
            progress?.Report(0.90f);
            Logger.Log("[GeomechCPU] Evaluating failure criteria...");
            EvaluateFailureChunked(results, labels, progress, token);
            token.ThrowIfCancellationRequested();

            // STEP 8: Apply plasticity correction if enabled
            if (_params.EnablePlasticity)
            {
                progress?.Report(0.92f);
                Logger.Log("[GeomechCPU] Applying elasto-plastic correction...");
                ApplyPlasticCorrection(results, labels, progress, token);
                token.ThrowIfCancellationRequested();
            }

            // STEP 9: Generate Mohr circles for visualization
            progress?.Report(0.95f);
            Logger.Log("[GeomechCPU] Generating Mohr circles...");
            GenerateMohrCircles(results);

            // STEP 10: Calculate global statistics
            Logger.Log("[GeomechCPU] Calculating global statistics...");
            CalculateGlobalStatistics(results);

            results.ComputationTime = DateTime.Now - startTime;
            progress?.Report(1.0f);

            Logger.Log("[GeomechCPU] ========== SIMULATION COMPLETE ==========");
            Logger.Log($"[GeomechCPU] Computation time: {results.ComputationTime.TotalSeconds:F2} s");
            Logger.Log($"[GeomechCPU] Converged: {converged} ({_iterationsPerformed} iterations)");
            Logger.Log($"[GeomechCPU] Mean stress: {results.MeanStress / 1e6f:F2} MPa");
            Logger.Log($"[GeomechCPU] Max shear: {results.MaxShearStress / 1e6f:F2} MPa");
            Logger.Log(
                $"[GeomechCPU] Failed voxels: {results.FailedVoxels}/{results.TotalVoxels} ({results.FailedVoxelPercentage:F2}%)");

            Cleanup();
            return results;
        }
        catch (OperationCanceledException)
        {
            Logger.Log("[GeomechCPU] Simulation cancelled by user");
            Cleanup();
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError($"[GeomechCPU] Simulation failed: {ex.Message}");
            Logger.LogError($"[GeomechCPU] Stack trace: {ex.StackTrace}");
            Cleanup();
            throw new Exception($"Geomechanical simulation failed: {ex.Message}", ex);
        }
    }

    private void InitializeChunking(byte[,,] labels, float[,,] density)
    {
        var extent = _params.SimulationExtent;
        var w = extent.Width;
        var h = extent.Height;
        var d = extent.Depth;

        var nodesPerChunk = w * h * 32L;
        long bytesPerNode = 3 * sizeof(float) + 3 * sizeof(int);
        var elementBytesPerChunk = nodesPerChunk * 8 * (2 * sizeof(float) + 8 * sizeof(int));
        var chunkMemory = nodesPerChunk * bytesPerNode + elementBytesPerChunk;

        var totalMemoryNeeded = w * h * (long)d * bytesPerNode +
                                w * h * (long)d * 6 * sizeof(float);

        _isHugeDataset = totalMemoryNeeded > _maxMemoryBudgetBytes;

        Logger.Log($"[GeomechCPU] Dataset memory estimate: {totalMemoryNeeded / (1024.0 * 1024 * 1024):F2} GB");
        Logger.Log($"[GeomechCPU] Huge dataset mode: {_isHugeDataset}");

        if (_isHugeDataset)
        {
            _maxLoadedChunks = Math.Max(3, (int)(_maxMemoryBudgetBytes / chunkMemory));
            Logger.Log($"[GeomechCPU] Will keep max {_maxLoadedChunks} chunks in memory");
        }
        else
        {
            _maxLoadedChunks = 999;
        }

        var slicesPerChunk = _isHugeDataset ? 32 : Math.Max(32, d / 4);
        _chunks = new List<GeomechanicalChunk>();

        for (var z = 0; z < d; z += slicesPerChunk - CHUNK_OVERLAP)
        {
            var depth = Math.Min(slicesPerChunk, d - z);
            if (z + depth > d) depth = d - z;

            var chunk = new GeomechanicalChunk
            {
                StartZ = z,
                Depth = depth,
                Width = w,
                Height = h,
                Labels = ExtractChunkLabels(labels, z, depth),
                Density = ExtractChunkDensity(density, z, depth)
            };

            _chunks.Add(chunk);
        }

        Logger.Log(
            $"[GeomechCPU] Created {_chunks.Count} chunks ({slicesPerChunk} slices each with {CHUNK_OVERLAP} overlap)");
    }

    private byte[,,] ExtractChunkLabels(byte[,,] labels, int startZ, int depth)
    {
        var chunk = new byte[_params.SimulationExtent.Width, _params.SimulationExtent.Height, depth];
        for (var z = 0; z < depth && startZ + z < labels.GetLength(2); z++)
        for (var y = 0; y < _params.SimulationExtent.Height; y++)
        for (var x = 0; x < _params.SimulationExtent.Width; x++)
            chunk[x, y, z] = labels[x, y, startZ + z];
        return chunk;
    }

    private float[,,] ExtractChunkDensity(float[,,] density, int startZ, int depth)
    {
        var chunk = new float[_params.SimulationExtent.Width, _params.SimulationExtent.Height, depth];
        for (var z = 0; z < depth && startZ + z < density.GetLength(2); z++)
        for (var y = 0; y < _params.SimulationExtent.Height; y++)
        for (var x = 0; x < _params.SimulationExtent.Width; x++)
            chunk[x, y, z] = density[x, y, startZ + z];
        return chunk;
    }

    private void AssembleGlobalStiffnessMatrixChunked(IProgress<float> progress, CancellationToken token)
    {
        Logger.Log("[GeomechCPU] Starting chunked assembly of global stiffness matrix");

        var totalElements = 0;
        var extent = _params.SimulationExtent;
        var dx = _params.PixelSize / 1e6f;

        foreach (var chunk in _chunks)
            for (var z = 0; z < chunk.Depth - 1; z++)
            for (var y = 0; y < chunk.Height - 1; y++)
            for (var x = 0; x < chunk.Width - 1; x++)
                if (chunk.Labels[x, y, z] != 0)
                    totalElements++;

        _numElements = totalElements;
        _numNodes = extent.Width * extent.Height * extent.Depth;
        _numDOFs = _numNodes * 3;

        Logger.Log($"[GeomechCPU] Total elements: {_numElements}, nodes: {_numNodes}, DOFs: {_numDOFs}");

        _nodeX = new float[_numNodes];
        _nodeY = new float[_numNodes];
        _nodeZ = new float[_numNodes];
        _nodeToDOF = new int[_numNodes];

        var nodeIdx = 0;
        for (var z = 0; z < extent.Depth; z++)
        for (var y = 0; y < extent.Height; y++)
        for (var x = 0; x < extent.Width; x++)
        {
            _nodeX[nodeIdx] = x * dx;
            _nodeY[nodeIdx] = y * dx;
            _nodeZ[nodeIdx] = z * dx;
            _nodeToDOF[nodeIdx] = nodeIdx * 3;
            nodeIdx++;
        }

        _elementNodes = new int[_numElements * 8];
        _elementE = new float[_numElements];
        _elementNu = new float[_numElements];

        var elemIdx = 0;
        for (var chunkId = 0; chunkId < _chunks.Count; chunkId++)
        {
            token.ThrowIfCancellationRequested();

            var chunk = _chunks[chunkId];
            LoadChunkIfNeeded(chunkId);

            for (var z = 0; z < chunk.Depth - 1; z++)
            {
                var globalZ = chunk.StartZ + z;
                for (var y = 0; y < chunk.Height - 1; y++)
                for (var x = 0; x < chunk.Width - 1; x++)
                {
                    if (chunk.Labels[x, y, z] == 0) continue;

                    var n0 = (globalZ * chunk.Height + y) * chunk.Width + x;
                    var n1 = n0 + 1;
                    var n2 = (globalZ * chunk.Height + y + 1) * chunk.Width + x + 1;
                    var n3 = (globalZ * chunk.Height + y + 1) * chunk.Width + x;
                    var n4 = ((globalZ + 1) * chunk.Height + y) * chunk.Width + x;
                    var n5 = n4 + 1;
                    var n6 = ((globalZ + 1) * chunk.Height + y + 1) * chunk.Width + x + 1;
                    var n7 = ((globalZ + 1) * chunk.Height + y + 1) * chunk.Width + x;

                    _elementNodes[elemIdx * 8 + 0] = n0;
                    _elementNodes[elemIdx * 8 + 1] = n1;
                    _elementNodes[elemIdx * 8 + 2] = n2;
                    _elementNodes[elemIdx * 8 + 3] = n3;
                    _elementNodes[elemIdx * 8 + 4] = n4;
                    _elementNodes[elemIdx * 8 + 5] = n5;
                    _elementNodes[elemIdx * 8 + 6] = n6;
                    _elementNodes[elemIdx * 8 + 7] = n7;

                    _elementE[elemIdx] = _params.YoungModulus * 1e6f;
                    _elementNu[elemIdx] = _params.PoissonRatio;

                    elemIdx++;
                }
            }

            if (chunkId % 10 == 0)
                progress?.Report(0.15f + 0.10f * chunkId / _chunks.Count);
        }

        _displacement = new float[_numDOFs];
        _force = new float[_numDOFs];
        _isDirichletDOF = new bool[_numDOFs];
        _dirichletValue = new float[_numDOFs];

        AssembleGlobalStiffnessMatrix();
    }

    // Original assembly method preserved - no changes to physics
    private void AssembleGlobalStiffnessMatrix()
    {
        var cooRow = new List<int>();
        var cooCol = new List<int>();
        var cooVal = new List<float>();

        var gp = 1.0f / MathF.Sqrt(3.0f);
        var gaussPoints = new (float xi, float eta, float zeta, float weight)[]
        {
            (-gp, -gp, -gp, 1.0f), (+gp, -gp, -gp, 1.0f),
            (+gp, +gp, -gp, 1.0f), (-gp, +gp, -gp, 1.0f),
            (-gp, -gp, +gp, 1.0f), (+gp, -gp, +gp, 1.0f),
            (+gp, +gp, +gp, 1.0f), (-gp, +gp, +gp, 1.0f)
        };

        for (var e = 0; e < _numElements; e++)
        {
            var nodes = new int[8];
            for (var i = 0; i < 8; i++)
                nodes[i] = _elementNodes[e * 8 + i];

            float[] ex = new float[8], ey = new float[8], ez = new float[8];
            for (var i = 0; i < 8; i++)
            {
                ex[i] = _nodeX[nodes[i]];
                ey[i] = _nodeY[nodes[i]];
                ez[i] = _nodeZ[nodes[i]];
            }

            var E = _elementE[e];
            var nu = _elementNu[e];
            var D = ComputeElasticityMatrix(E, nu);
            var Ke = new float[24, 24];

            foreach (var (xi, eta, zeta, w) in gaussPoints)
            {
                var dN_dxi = ComputeShapeFunctionDerivatives(xi, eta, zeta);
                var J = ComputeJacobian(dN_dxi, ex, ey, ez);
                var detJ = Determinant3x3(J);

                if (detJ <= 0)
                    throw new Exception($"Negative Jacobian in element {e}: detJ = {detJ}. Mesh quality issue!");

                var Jinv = Inverse3x3(J);
                var dN_dx = MatrixMultiply(Jinv, dN_dxi);
                var B = ComputeStrainDisplacementMatrix(dN_dx);

                AddToElementStiffness(Ke, B, D, detJ * w);
            }

            for (var i = 0; i < 8; i++)
            for (var j = 0; j < 8; j++)
            for (var di = 0; di < 3; di++)
            for (var dj = 0; dj < 3; dj++)
            {
                var globalI = _nodeToDOF[nodes[i]] + di;
                var globalJ = _nodeToDOF[nodes[j]] + dj;
                var localI = i * 3 + di;
                var localJ = j * 3 + dj;

                var value = Ke[localI, localJ];
                if (MathF.Abs(value) > 1e-12f)
                {
                    cooRow.Add(globalI);
                    cooCol.Add(globalJ);
                    cooVal.Add(value);
                }
            }
        }

        ConvertCOOtoCSR(cooRow, cooCol, cooVal);
    }

    // All original FEM methods preserved exactly
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float[,] ComputeElasticityMatrix(float E, float nu)
    {
        var lambda = E * nu / ((1.0f + nu) * (1.0f - 2.0f * nu));
        var mu = E / (2.0f * (1.0f + nu));
        var lambda_plus_2mu = lambda + 2.0f * mu;

        var D = new float[6, 6];
        D[0, 0] = lambda_plus_2mu;
        D[0, 1] = lambda;
        D[0, 2] = lambda;
        D[1, 0] = lambda;
        D[1, 1] = lambda_plus_2mu;
        D[1, 2] = lambda;
        D[2, 0] = lambda;
        D[2, 1] = lambda;
        D[2, 2] = lambda_plus_2mu;
        D[3, 3] = mu;
        D[4, 4] = mu;
        D[5, 5] = mu;

        return D;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float[,] ComputeShapeFunctionDerivatives(float xi, float eta, float zeta)
    {
        var dN = new float[3, 8];
        var naturalCoords = new float[8, 3]
        {
            { -1, -1, -1 }, { +1, -1, -1 }, { +1, +1, -1 }, { -1, +1, -1 },
            { -1, -1, +1 }, { +1, -1, +1 }, { +1, +1, +1 }, { -1, +1, +1 }
        };

        for (var i = 0; i < 8; i++)
        {
            var xi_i = naturalCoords[i, 0];
            var eta_i = naturalCoords[i, 1];
            var zeta_i = naturalCoords[i, 2];

            dN[0, i] = 0.125f * xi_i * (1.0f + eta_i * eta) * (1.0f + zeta_i * zeta);
            dN[1, i] = 0.125f * (1.0f + xi_i * xi) * eta_i * (1.0f + zeta_i * zeta);
            dN[2, i] = 0.125f * (1.0f + xi_i * xi) * (1.0f + eta_i * eta) * zeta_i;
        }

        return dN;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float[,] ComputeJacobian(float[,] dN_dxi, float[] ex, float[] ey, float[] ez)
    {
        var J = new float[3, 3];

        for (var i = 0; i < 8; i++)
        {
            J[0, 0] += dN_dxi[0, i] * ex[i];
            J[0, 1] += dN_dxi[0, i] * ey[i];
            J[0, 2] += dN_dxi[0, i] * ez[i];
            J[1, 0] += dN_dxi[1, i] * ex[i];
            J[1, 1] += dN_dxi[1, i] * ey[i];
            J[1, 2] += dN_dxi[1, i] * ez[i];
            J[2, 0] += dN_dxi[2, i] * ex[i];
            J[2, 1] += dN_dxi[2, i] * ey[i];
            J[2, 2] += dN_dxi[2, i] * ez[i];
        }

        return J;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float[,] ComputeStrainDisplacementMatrix(float[,] dN_dx)
    {
        var B = new float[6, 24];

        for (var i = 0; i < 8; i++)
        {
            var dNi_dx = dN_dx[0, i];
            var dNi_dy = dN_dx[1, i];
            var dNi_dz = dN_dx[2, i];
            var col = i * 3;

            B[0, col + 0] = dNi_dx;
            B[1, col + 1] = dNi_dy;
            B[2, col + 2] = dNi_dz;
            B[3, col + 0] = dNi_dy;
            B[3, col + 1] = dNi_dx;
            B[4, col + 0] = dNi_dz;
            B[4, col + 2] = dNi_dx;
            B[5, col + 1] = dNi_dz;
            B[5, col + 2] = dNi_dy;
        }

        return B;
    }

    private void AddToElementStiffness(float[,] Ke, float[,] B, float[,] D, float factor)
    {
        var DB = new float[6, 24];
        for (var i = 0; i < 6; i++)
        for (var j = 0; j < 24; j++)
        {
            float sum = 0;
            for (var k = 0; k < 6; k++)
                sum += D[i, k] * B[k, j];
            DB[i, j] = sum;
        }

        for (var i = 0; i < 24; i++)
        for (var j = 0; j < 24; j++)
        {
            float sum = 0;
            for (var k = 0; k < 6; k++)
                sum += B[k, i] * DB[k, j];
            Ke[i, j] += sum * factor;
        }
    }

    private void ApplyBoundaryConditionsAndLoading(byte[,,] labels)
    {
        var extent = _params.SimulationExtent;
        var w = extent.Width;
        var h = extent.Height;
        var d = extent.Depth;

        // Convert to Pa
        var sigma1_Pa = _params.Sigma1 * 1e6f;
        var sigma2_Pa = _params.Sigma2 * 1e6f;
        var sigma3_Pa = _params.Sigma3 * 1e6f;

        // Apply pore pressure effects
        if (_params.UsePorePressure)
        {
            var pp_Pa = _params.PorePressure * 1e6f;
            var alpha = _params.BiotCoefficient;
            sigma1_Pa -= alpha * pp_Pa;
            sigma2_Pa -= alpha * pp_Pa;
            sigma3_Pa -= alpha * pp_Pa;
        }

        var dx = _params.PixelSize / 1e6f;
        var elementFaceArea = dx * dx;

        Logger.Log("[GeomechCPU] Applying boundary conditions with proper tributary areas");

        // Helper to check if a node is on the surface and get its tributary area
        float GetNodalTributaryArea(int x, int y, int z, int normalDir)
        {
            // Count how many surface element faces touch this node
            var touchCount = 0;

            // Check neighboring elements based on the surface normal direction
            for (var dy = -1; dy <= 0; dy++)
            for (var dx = -1; dx <= 0; dx++)
            {
                var ex = x + dx;
                var ey = y + dy;
                var ez = z;

                if (normalDir == 0)
                {
                    ex = x;
                    ey = y + dy;
                    ez = z + dx;
                } // YZ face

                if (normalDir == 1)
                {
                    ex = x + dx;
                    ey = y;
                    ez = z + dy;
                } // XZ face

                // normalDir == 2: XY face (default setup above)
                if (ex >= 0 && ex < w - 1 && ey >= 0 && ey < h - 1 && ez >= 0 && ez < d - 1)
                    if (labels[ex, ey, ez] != 0)
                        touchCount++;
            }

            // Each element face contributes area/4 to each of its 4 corner nodes
            return touchCount * elementFaceArea / 4.0f;
        }

        // Apply σ1 (axial load) on top surface (Z+)
        Logger.Log($"[GeomechCPU] Applying σ₁ = {_params.Sigma1} MPa on top surface (Z+)");
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            // Check if this node has material beneath it (is on actual surface)
            if (y > 0 && x > 0 && y < h - 1 && x < w - 1)
            {
                var hasMatBelow = labels[x - 1, y - 1, d - 2] != 0 ||
                                  labels[x, y - 1, d - 2] != 0 ||
                                  labels[x - 1, y, d - 2] != 0 ||
                                  labels[x, y, d - 2] != 0;

                if (!hasMatBelow) continue;
            }

            var nodeIdx = ((d - 1) * h + y) * w + x;
            var dofZ = _nodeToDOF[nodeIdx] + 2;

            var tributaryArea = GetNodalTributaryArea(x, y, d - 1, 2);
            if (tributaryArea > 0) _force[dofZ] -= sigma1_Pa * tributaryArea; // FIX: Proper force calculation
        }

        // Fix bottom surface (Z=0) to prevent rigid body motion
        Logger.Log("[GeomechCPU] Fixing bottom surface (Z=0) in Z direction");
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var nodeIdx = (0 * h + y) * w + x;
            var dofZ = _nodeToDOF[nodeIdx] + 2;
            _isDirichletDOF[dofZ] = true;
            _dirichletValue[dofZ] = 0.0f;
        }

        // Apply σ2 (lateral load) on Y faces
        Logger.Log($"[GeomechCPU] Applying σ₂ = {_params.Sigma2} MPa on Y faces");
        for (var z = 0; z < d; z++)
        for (var x = 0; x < w; x++)
        {
            // Y- face (y=0)
            if (z > 0 && x > 0 && z < d - 1 && x < w - 1)
            {
                var hasMat = labels[x - 1, 0, z - 1] != 0 || labels[x, 0, z - 1] != 0 ||
                             labels[x - 1, 0, z] != 0 || labels[x, 0, z] != 0;

                if (hasMat)
                {
                    var nodeIdx1 = (z * h + 0) * w + x;
                    var dofY1 = _nodeToDOF[nodeIdx1] + 1;
                    var tributaryArea = GetNodalTributaryArea(x, 0, z, 1);
                    if (tributaryArea > 0 && !_isDirichletDOF[dofY1])
                        _force[dofY1] += sigma2_Pa * tributaryArea; // FIX: Proper force
                }
            }

            // Y+ face (y=h-1)
            if (z > 0 && x > 0 && z < d - 1 && x < w - 1)
            {
                var hasMat = labels[x - 1, h - 2, z - 1] != 0 || labels[x, h - 2, z - 1] != 0 ||
                             labels[x - 1, h - 2, z] != 0 || labels[x, h - 2, z] != 0;

                if (hasMat)
                {
                    var nodeIdx2 = (z * h + (h - 1)) * w + x;
                    var dofY2 = _nodeToDOF[nodeIdx2] + 1;
                    var tributaryArea = GetNodalTributaryArea(x, h - 1, z, 1);
                    if (tributaryArea > 0 && !_isDirichletDOF[dofY2])
                        _force[dofY2] -= sigma2_Pa * tributaryArea; // FIX: Proper force
                }
            }
        }

        // Apply σ3 (confining pressure) on X faces
        Logger.Log($"[GeomechCPU] Applying σ₃ = {_params.Sigma3} MPa on X faces");
        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        {
            // Fix X- face (x=0) to prevent rigid body motion
            var nodeIdx1 = (z * h + y) * w + 0;
            var dofX1 = _nodeToDOF[nodeIdx1] + 0;
            _isDirichletDOF[dofX1] = true;
            _dirichletValue[dofX1] = 0.0f;

            // X+ face (x=w-1) - apply confining pressure
            if (z > 0 && y > 0 && z < d - 1 && y < h - 1)
            {
                var hasMat = labels[w - 2, y - 1, z - 1] != 0 || labels[w - 2, y, z - 1] != 0 ||
                             labels[w - 2, y - 1, z] != 0 || labels[w - 2, y, z] != 0;

                if (hasMat)
                {
                    var nodeIdx2 = (z * h + y) * w + (w - 1);
                    var dofX2 = _nodeToDOF[nodeIdx2] + 0;
                    var tributaryArea = GetNodalTributaryArea(w - 1, y, z, 0);
                    if (tributaryArea > 0 && !_isDirichletDOF[dofX2])
                        _force[dofX2] -= sigma3_Pa * tributaryArea; // FIX: Proper force
                }
            }
        }

        // Fix one corner node completely to eliminate rigid body rotation
        var cornerNode = 0;
        for (var i = 0; i < 3; i++)
        {
            _isDirichletDOF[_nodeToDOF[cornerNode] + i] = true;
            _dirichletValue[_nodeToDOF[cornerNode] + i] = 0.0f;
        }

        var totalForce = _force.Sum(f => Math.Abs(f));
        var fixedDOFs = _isDirichletDOF.Count(b => b);
        Logger.Log($"[GeomechCPU] Total applied force magnitude: {totalForce / 1e6f:F2} MN");
        Logger.Log($"[GeomechCPU] Fixed DOFs: {fixedDOFs} / {_numDOFs}");
    }

    private bool SolveDisplacements(IProgress<float> progress, CancellationToken token)
    {
        var maxIter = _params.MaxIterations;
        var tolerance = _params.Tolerance;

        // FIX: Use tighter default tolerance
        if (tolerance > 1e-4f)
            tolerance = 1e-6f;

        ApplyDirichletBC();
        Array.Clear(_displacement, 0, _numDOFs);

        for (var i = 0; i < _numDOFs; i++)
            if (_isDirichletDOF[i])
                _displacement[i] = _dirichletValue[i];

        var r = new float[_numDOFs];
        var Ku = new float[_numDOFs];
        MatrixVectorMultiply(Ku, _displacement);

        for (var i = 0; i < _numDOFs; i++)
            r[i] = _force[i] - Ku[i];

        var M_inv = new float[_numDOFs];
        for (var i = 0; i < _numDOFs; i++)
        {
            var diag = GetDiagonalElement(i);
            M_inv[i] = diag > 1e-12f ? 1.0f / diag : 1.0f;
        }

        var z = new float[_numDOFs];
        for (var i = 0; i < _numDOFs; i++)
            z[i] = M_inv[i] * r[i];

        var p = new float[_numDOFs];
        Array.Copy(z, p, _numDOFs);

        var rho = DotProduct(r, z);
        var rho0 = rho;

        var converged = false;
        var iter = 0;

        Logger.Log($"[GeomechCPU] Starting PCG solver with tolerance {tolerance:E2}");

        while (iter < maxIter && !converged)
        {
            token.ThrowIfCancellationRequested();

            var q = new float[_numDOFs];
            MatrixVectorMultiply(q, p);

            var pq = DotProduct(p, q);
            if (MathF.Abs(pq) < 1e-20f)
                break;

            var alpha = rho / pq;

            for (var i = 0; i < _numDOFs; i++)
                if (!_isDirichletDOF[i])
                    _displacement[i] += alpha * p[i];

            for (var i = 0; i < _numDOFs; i++)
                r[i] -= alpha * q[i];

            var residualNorm = VectorNorm(r);
            var relativeResidual = residualNorm / MathF.Sqrt(rho0);

            if (relativeResidual < tolerance)
            {
                converged = true;
                Logger.Log(
                    $"[GeomechCPU] PCG converged at iteration {iter} with relative residual {relativeResidual:E4}");
                break;
            }

            for (var i = 0; i < _numDOFs; i++)
                z[i] = M_inv[i] * r[i];

            var rho_new = DotProduct(r, z);
            var beta = rho_new / rho;

            for (var i = 0; i < _numDOFs; i++)
                p[i] = z[i] + beta * p[i];

            rho = rho_new;
            iter++;

            if (iter % 10 == 0)
            {
                var prog = 0.35f + 0.4f * iter / maxIter;
                progress?.Report(prog);

                if (iter % 100 == 0)
                    Logger.Log($"[GeomechCPU] PCG iteration {iter}, relative residual: {relativeResidual:E4}");

                if (_isHugeDataset && iter % 50 == 0)
                {
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false);
                    UpdateMemoryUsage();
                }
            }
        }

        _iterationsPerformed = iter;

        if (!converged)
            Logger.LogWarning($"[GeomechCPU] PCG did not converge after {maxIter} iterations");

        return converged;
    }

    private void ApplyDirichletBC()
    {
        for (var i = 0; i < _numDOFs; i++)
            if (_isDirichletDOF[i])
                _force[i] = _dirichletValue[i];
    }

    private void MatrixVectorMultiply(float[] y, float[] x)
    {
        Array.Clear(y, 0, _numDOFs);

        for (var row = 0; row < _numDOFs; row++)
        {
            if (_isDirichletDOF[row])
            {
                y[row] = x[row];
                continue;
            }

            var rowStart = _rowPtr[row];
            var rowEnd = _rowPtr[row + 1];

            float sum = 0;
            for (var j = rowStart; j < rowEnd; j++)
            {
                var col = _colIdx[j];
                if (_isDirichletDOF[col])
                    continue;
                sum += _values[j] * x[col];
            }

            y[row] = sum;
        }
    }

    private float GetDiagonalElement(int row)
    {
        var rowStart = _rowPtr[row];
        var rowEnd = _rowPtr[row + 1];

        for (var j = rowStart; j < rowEnd; j++)
            if (_colIdx[j] == row)
                return _values[j];

        return 1.0f;
    }

    private GeomechanicalResults CalculateStrainsAndStressesChunked(byte[,,] labels, BoundingBox extent,
        IProgress<float> progress, CancellationToken token)
    {
        var w = extent.Width;
        var h = extent.Height;
        var d = extent.Depth;

        var results = new GeomechanicalResults
        {
            StressXX = new float[w, h, d],
            StressYY = new float[w, h, d],
            StressZZ = new float[w, h, d],
            StressXY = new float[w, h, d],
            StressXZ = new float[w, h, d],
            StressYZ = new float[w, h, d],
            StrainXX = new float[w, h, d],
            StrainYY = new float[w, h, d],
            StrainZZ = new float[w, h, d],
            StrainXY = new float[w, h, d],
            StrainXZ = new float[w, h, d],
            StrainYZ = new float[w, h, d],
            Sigma1 = new float[w, h, d],
            Sigma2 = new float[w, h, d],
            Sigma3 = new float[w, h, d],
            FailureIndex = new float[w, h, d],
            DamageField = new byte[w, h, d],
            FractureField = new bool[w, h, d],
            MaterialLabels = labels,
            Parameters = _params
        };

        Logger.Log("[GeomechCPU] Computing strains and stresses with nodal averaging");

        // FIX: Compute stresses at nodes with averaging
        var nodalStress = new float[_numNodes, 6]; // 6 stress components per node
        var nodalCount = new int[_numNodes];

        // Process each chunk
        for (var chunkId = 0; chunkId < _chunks.Count; chunkId++)
        {
            token.ThrowIfCancellationRequested();
            var chunk = _chunks[chunkId];
            LoadChunkIfNeeded(chunkId);

            ProcessChunkStrainStressNodal(chunk, nodalStress, nodalCount);

            if (chunkId % 5 == 0)
                progress?.Report(0.75f + 0.10f * chunkId / _chunks.Count);

            if (_isHugeDataset)
                EvictLRUChunksIfNeeded();
        }

        // Average nodal stresses and map to voxels
        var dx = _params.PixelSize / 1e6f;
        for (var nodeIdx = 0; nodeIdx < _numNodes; nodeIdx++)
        {
            if (nodalCount[nodeIdx] == 0) continue;

            var count = nodalCount[nodeIdx];
            var x = (int)(_nodeX[nodeIdx] / dx + 0.5f);
            var y = (int)(_nodeY[nodeIdx] / dx + 0.5f);
            var z = (int)(_nodeZ[nodeIdx] / dx + 0.5f);

            if (x >= 0 && x < w && y >= 0 && y < h && z >= 0 && z < d && labels[x, y, z] != 0)
            {
                results.StressXX[x, y, z] = nodalStress[nodeIdx, 0] / count;
                results.StressYY[x, y, z] = nodalStress[nodeIdx, 1] / count;
                results.StressZZ[x, y, z] = nodalStress[nodeIdx, 2] / count;
                results.StressXY[x, y, z] = nodalStress[nodeIdx, 3] / count;
                results.StressXZ[x, y, z] = nodalStress[nodeIdx, 4] / count;
                results.StressYZ[x, y, z] = nodalStress[nodeIdx, 5] / count;
            }
        }

        return results;
    }

    private void ProcessChunkStrainStressNodal(GeomechanicalChunk chunk, float[,] nodalStress, int[] nodalCount)
    {
        Parallel.For(0, _numElements, e =>
        {
            var nodes = new int[8];
            for (var i = 0; i < 8; i++)
                nodes[i] = _elementNodes[e * 8 + i];

            var ue = new float[24];
            for (var i = 0; i < 8; i++)
            {
                var dofBase = _nodeToDOF[nodes[i]];
                ue[i * 3 + 0] = _displacement[dofBase + 0];
                ue[i * 3 + 1] = _displacement[dofBase + 1];
                ue[i * 3 + 2] = _displacement[dofBase + 2];
            }

            float[] ex = new float[8], ey = new float[8], ez = new float[8];
            for (var i = 0; i < 8; i++)
            {
                ex[i] = _nodeX[nodes[i]];
                ey[i] = _nodeY[nodes[i]];
                ez[i] = _nodeZ[nodes[i]];
            }

            var E = _elementE[e];
            var nu = _elementNu[e];
            var D = ComputeElasticityMatrix(E, nu);

            // Compute stress at element center
            var dN_dxi = ComputeShapeFunctionDerivatives(0, 0, 0);
            var J = ComputeJacobian(dN_dxi, ex, ey, ez);
            var Jinv = Inverse3x3(J);
            var dN_dx = MatrixMultiply(Jinv, dN_dxi);
            var B = ComputeStrainDisplacementMatrix(dN_dx);

            var strain = new float[6];
            for (var i = 0; i < 6; i++)
            {
                float sum = 0;
                for (var j = 0; j < 24; j++)
                    sum += B[i, j] * ue[j];
                strain[i] = sum;
            }

            var stress = new float[6];
            for (var i = 0; i < 6; i++)
            {
                float sum = 0;
                for (var j = 0; j < 6; j++)
                    sum += D[i, j] * strain[j];
                stress[i] = sum;
            }

            // FIX: Distribute element stress to its 8 nodes
            int GetLockIndex(int nodeIdx)
            {
                return nodeIdx % NUM_STRESS_LOCKS;
            }

            for (var i = 0; i < 8; i++)
            {
                var nodeIdx = nodes[i];
                lock (_stressLocks[GetLockIndex(nodeIdx)]) // FIX: Lock striping
                {
                    for (var comp = 0; comp < 6; comp++)
                        nodalStress[nodeIdx, comp] += stress[comp];
                    nodalCount[nodeIdx]++;
                }
            }
        });
    }

    private void CalculateGlobalStatistics(GeomechanicalResults results)
    {
        Logger.Log("[GeomechCPU] Calculating global statistics");

        float sumStress = 0;
        float maxShear = 0;
        float sumVonMises = 0;
        float maxVonMises = 0;
        var validVoxels = 0;

        var w = results.StressXX.GetLength(0);
        var h = results.StressXX.GetLength(1);
        var d = results.StressXX.GetLength(2);

        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            if (results.MaterialLabels[x, y, z] == 0) continue;

            validVoxels++;

            var meanStress = (results.StressXX[x, y, z] +
                              results.StressYY[x, y, z] +
                              results.StressZZ[x, y, z]) / 3.0f;
            sumStress += meanStress;

            var s1 = results.Sigma1[x, y, z];
            var s3 = results.Sigma3[x, y, z];
            var shear = (s1 - s3) / 2.0f;
            maxShear = Math.Max(maxShear, shear);

            var vonMises = MathF.Sqrt(0.5f * (
                MathF.Pow(results.StressXX[x, y, z] - results.StressYY[x, y, z], 2) +
                MathF.Pow(results.StressYY[x, y, z] - results.StressZZ[x, y, z], 2) +
                MathF.Pow(results.StressZZ[x, y, z] - results.StressXX[x, y, z], 2) +
                6 * (MathF.Pow(results.StressXY[x, y, z], 2) +
                     MathF.Pow(results.StressXZ[x, y, z], 2) +
                     MathF.Pow(results.StressYZ[x, y, z], 2))
            ));

            sumVonMises += vonMises;
            maxVonMises = Math.Max(maxVonMises, vonMises);
        }

        results.MeanStress = validVoxels > 0 ? sumStress / validVoxels : 0;
        results.MaxShearStress = maxShear;
        results.VonMisesStress_Mean = validVoxels > 0 ? sumVonMises / validVoxels : 0;
        results.VonMisesStress_Max = maxVonMises;

        Logger.Log($"[GeomechCPU] Mean stress: {results.MeanStress / 1e6f:F2} MPa");
        Logger.Log($"[GeomechCPU] Max shear: {results.MaxShearStress / 1e6f:F2} MPa");
        Logger.Log($"[GeomechCPU] Mean von Mises: {results.VonMisesStress_Mean / 1e6f:F2} MPa");
    }

    private void ApplyPlasticCorrection(GeomechanicalResults results, byte[,,] labels,
        IProgress<float> progress, CancellationToken token)
    {
        if (!_params.EnablePlasticity) return;

        Logger.Log("[GeomechCPU] Applying elasto-plastic correction");

        var w = results.StressXX.GetLength(0);
        var h = results.StressYY.GetLength(1);
        var d = results.StressZZ.GetLength(2);

        // Plasticity parameters
        var yieldStress = _params.Cohesion * 1e6f * 2f; // σ_y = 2c (rough approximation)
        var hardeningModulus = _params.YoungModulus * 1e6f * 0.01f; // H = E/100 (typical)
        var E = _params.YoungModulus * 1e6f;
        var nu = _params.PoissonRatio;
        var mu = E / (2f * (1f + nu)); // Shear modulus

        var plasticVoxels = 0;

        Parallel.For(0, d, z =>
        {
            var localPlastic = 0;

            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                if (labels[x, y, z] == 0) continue;

                // Get stress tensor
                var sxx = results.StressXX[x, y, z];
                var syy = results.StressYY[x, y, z];
                var szz = results.StressZZ[x, y, z];
                var sxy = results.StressXY[x, y, z];
                var sxz = results.StressXZ[x, y, z];
                var syz = results.StressYZ[x, y, z];

                // Calculate mean stress (hydrostatic component)
                var p = (sxx + syy + szz) / 3f;

                // Deviatoric stress tensor
                var sxx_dev = sxx - p;
                var syy_dev = syy - p;
                var szz_dev = szz - p;
                var sxy_dev = sxy;
                var sxz_dev = sxz;
                var syz_dev = syz;

                // Von Mises equivalent stress
                var J2 = 0.5f * (sxx_dev * sxx_dev + syy_dev * syy_dev + szz_dev * szz_dev) +
                         sxy_dev * sxy_dev + sxz_dev * sxz_dev + syz_dev * syz_dev;
                var vonMises = MathF.Sqrt(3f * J2);

                // Check yield condition (assuming no prior plastic strain for simplicity)
                var yieldFunction = vonMises - yieldStress;

                if (yieldFunction > 0)
                {
                    localPlastic++;

                    // Radial return mapping (closest point projection)
                    var effectiveYield = yieldStress; // Can add hardening: + H * ε_p_eq
                    var returnFactor = effectiveYield / vonMises;

                    // Plastic correction
                    sxx_dev *= returnFactor;
                    syy_dev *= returnFactor;
                    szz_dev *= returnFactor;
                    sxy_dev *= returnFactor;
                    sxz_dev *= returnFactor;
                    syz_dev *= returnFactor;

                    // Update stress tensor (hydrostatic part unchanged)
                    results.StressXX[x, y, z] = sxx_dev + p;
                    results.StressYY[x, y, z] = syy_dev + p;
                    results.StressZZ[x, y, z] = szz_dev + p;
                    results.StressXY[x, y, z] = sxy_dev;
                    results.StressXZ[x, y, z] = sxz_dev;
                    results.StressYZ[x, y, z] = syz_dev;

                    // Calculate equivalent plastic strain increment
                    var deltaEpsilon_p = yieldFunction / (3f * mu + hardeningModulus);

                    // Store plastic strain magnitude in unused field
                    results.StrainYY[x, y, z] = deltaEpsilon_p; // Plastic strain marker
                }
            }

            lock (results)
            {
                plasticVoxels += localPlastic;
            }
        });

        Logger.Log($"[GeomechCPU] Plasticity: {plasticVoxels} voxels yielded " +
                   $"({100f * plasticVoxels / results.TotalVoxels:F2}%)");
    }

    private void ValidateChunkBoundaries()
    {
        for (var i = 0; i < _chunks.Count - 1; i++)
        {
            var currentChunk = _chunks[i];
            var nextChunk = _chunks[i + 1];

            var overlapStart = nextChunk.StartZ;
            var overlapEnd = currentChunk.StartZ + currentChunk.Depth;
            var actualOverlap = overlapEnd - overlapStart;

            if (actualOverlap < CHUNK_OVERLAP)
                Logger.LogWarning($"[Geomech] Insufficient overlap between chunks {i} and {i + 1}: " +
                                  $"{actualOverlap} slices (expected {CHUNK_OVERLAP})");
        }
    }

    private void CalculatePrincipalStressesChunked(GeomechanicalResults results, byte[,,] labels,
        IProgress<float> progress, CancellationToken token)
    {
        var w = results.StressXX.GetLength(0);
        var h = results.StressXX.GetLength(1);
        var d = results.StressXX.GetLength(2);

        Parallel.For(0, d, z =>
        {
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                if (labels[x, y, z] == 0) continue;

                var sxx = results.StressXX[x, y, z];
                var syy = results.StressYY[x, y, z];
                var szz = results.StressZZ[x, y, z];
                var sxy = results.StressXY[x, y, z];
                var sxz = results.StressXZ[x, y, z];
                var syz = results.StressYZ[x, y, z];

                var principals = CalculatePrincipalValues(sxx, syy, szz, sxy, sxz, syz);

                results.Sigma1[x, y, z] = principals.sigma1;
                results.Sigma2[x, y, z] = principals.sigma2;
                results.Sigma3[x, y, z] = principals.sigma3;
            }
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (float sigma1, float sigma2, float sigma3) CalculatePrincipalValues(
        float sxx, float syy, float szz, float sxy, float sxz, float syz)
    {
        var I1 = sxx + syy + szz;
        var I2 = sxx * syy + syy * szz + szz * sxx - sxy * sxy - sxz * sxz - syz * syz;
        var I3 = sxx * syy * szz + 2 * sxy * sxz * syz - sxx * syz * syz - syy * sxz * sxz - szz * sxy * sxy;

        var p = I2 - I1 * I1 / 3.0f;
        var q = I3 + (2.0f * I1 * I1 * I1 - 9.0f * I1 * I2) / 27.0f;

        float sigma1, sigma2, sigma3;

        if (MathF.Abs(p) < 1e-9f)
        {
            sigma1 = sigma2 = sigma3 = I1 / 3.0f;
        }
        else
        {
            var half_q = q * 0.5f;
            var term_under_sqrt = -p * p * p / 27.0f;
            if (term_under_sqrt < 0) term_under_sqrt = 0;

            var r = MathF.Sqrt(term_under_sqrt);
            var cos_phi = Math.Clamp(-half_q / r, -1.0f, 1.0f);
            var phi = MathF.Acos(cos_phi);

            var scale = 2.0f * MathF.Sqrt(-p / 3.0f);
            var offset = I1 / 3.0f;

            sigma1 = offset + scale * MathF.Cos(phi / 3.0f);
            sigma2 = offset + scale * MathF.Cos((phi + 2.0f * MathF.PI) / 3.0f);
            sigma3 = offset + scale * MathF.Cos((phi + 4.0f * MathF.PI) / 3.0f);
        }

        if (sigma1 < sigma2) (sigma1, sigma2) = (sigma2, sigma1);
        if (sigma1 < sigma3) (sigma1, sigma3) = (sigma3, sigma1);
        if (sigma2 < sigma3) (sigma2, sigma3) = (sigma3, sigma2);

        return (sigma1, sigma2, sigma3);
    }

    private void EvaluateFailureChunked(GeomechanicalResults results, byte[,,] labels,
        IProgress<float> progress, CancellationToken token)
    {
        var w = results.StressXX.GetLength(0);
        var h = results.StressXX.GetLength(1);
        var d = results.StressXX.GetLength(2);

        var cohesion_Pa = _params.Cohesion * 1e6f;
        var phi = _params.FrictionAngle * MathF.PI / 180f;
        var tensileStrength_Pa = _params.TensileStrength * 1e6f;

        // Progressive damage parameters (Mazars model inspired)
        const float DAMAGE_INITIATION = 0.7f; // Start damage at 70% of strength
        const float DAMAGE_EXPONENT = 2.0f; // Controls damage progression rate
        const float RESIDUAL_STRENGTH = 0.05f; // Retain 5% strength when fully damaged

        var failedCount = 0;
        var totalCount = 0;

        // First pass: Calculate damage and failure
        Parallel.For(0, d, z =>
        {
            var localFailed = 0;
            var localTotal = 0;

            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                if (labels[x, y, z] == 0) continue;

                localTotal++;

                var sigma1 = results.Sigma1[x, y, z];
                var sigma2 = results.Sigma2[x, y, z];
                var sigma3 = results.Sigma3[x, y, z];

                // Apply effective stress
                if (_params.UsePorePressure)
                {
                    var pp = _params.PorePressure * 1e6f;
                    var alpha = _params.BiotCoefficient;
                    sigma1 -= alpha * pp;
                    sigma2 -= alpha * pp;
                    sigma3 -= alpha * pp;
                }

                var failureIndex = CalculateFailureIndex(sigma1, sigma2, sigma3,
                    cohesion_Pa, phi, tensileStrength_Pa);

                results.FailureIndex[x, y, z] = failureIndex;

                // Progressive damage calculation
                var damage = 0f;

                if (failureIndex >= DAMAGE_INITIATION)
                {
                    if (failureIndex >= 1.0f)
                    {
                        // Complete failure - exponential softening
                        var overstress = failureIndex - 1.0f;
                        damage = 1.0f - RESIDUAL_STRENGTH * MathF.Exp(-DAMAGE_EXPONENT * overstress);
                        damage = Math.Clamp(damage, 0f, 1.0f - RESIDUAL_STRENGTH);

                        results.FractureField[x, y, z] = true;
                        localFailed++;
                    }
                    else
                    {
                        // Progressive damage before failure (0.7 to 1.0)
                        var normalizedLoad = (failureIndex - DAMAGE_INITIATION) / (1.0f - DAMAGE_INITIATION);
                        damage = MathF.Pow(normalizedLoad, DAMAGE_EXPONENT);
                        damage = Math.Clamp(damage, 0f, 0.8f); // Cap at 80% before complete failure
                    }
                }

                results.DamageField[x, y, z] = (byte)(damage * 255);

                // Calculate damaged stresses (for visualization/export)
                var degradationFactor = 1.0f - damage;
                results.StressXX[x, y, z] *= degradationFactor;
                results.StressYY[x, y, z] *= degradationFactor;
                results.StressZZ[x, y, z] *= degradationFactor;
                results.StressXY[x, y, z] *= degradationFactor;
                results.StressXZ[x, y, z] *= degradationFactor;
                results.StressYZ[x, y, z] *= degradationFactor;
            }

            lock (results)
            {
                failedCount += localFailed;
                totalCount += localTotal;
            }
        });

        // Second pass: Calculate microcrack density and orientation (optional advanced feature)
        if (_params.EnableDamageEvolution)
            Parallel.For(0, d, z =>
            {
                for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    if (!results.FractureField[x, y, z]) continue;

                    // Microcrack orientation follows maximum principal stress
                    var s1 = results.Sigma1[x, y, z];
                    var s3 = results.Sigma3[x, y, z];

                    // Crack density (simplified - more cracks under higher differential stress)
                    var crackDensity = Math.Clamp((s1 - s3) / cohesion_Pa, 0f, 5f);

                    // Store in strain field as metadata (repurpose unused field)
                    results.StrainXX[x, y, z] = crackDensity;
                }
            });

        results.FailedVoxels = failedCount;
        results.TotalVoxels = totalCount;
        results.FailedVoxelPercentage = totalCount > 0 ? 100f * failedCount / totalCount : 0;

        Logger.Log($"[GeomechCPU] Progressive damage: {failedCount} fractured, " +
                   $"avg damage = {CalculateAverageDamage(results):F2}%");
    }

    private float CalculateFailureIndex(float sigma1, float sigma2, float sigma3,
        float cohesion, float phi, float tensileStrength)
    {
        switch (_params.FailureCriterion)
        {
            case FailureCriterion.MohrCoulomb:
                var left = sigma1 - sigma3;
                var right = 2 * cohesion * MathF.Cos(phi) + (sigma1 + sigma3) * MathF.Sin(phi);
                return right > 1e-9f ? left / right : left;

            case FailureCriterion.DruckerPrager:
                var p = (sigma1 + sigma2 + sigma3) / 3;
                var q = MathF.Sqrt(0.5f * (MathF.Pow(sigma1 - sigma2, 2) +
                                           MathF.Pow(sigma2 - sigma3, 2) +
                                           MathF.Pow(sigma3 - sigma1, 2)));
                var alpha = 2 * MathF.Sin(phi) / (3 - MathF.Sin(phi));
                var k = 6 * cohesion * MathF.Cos(phi) / (3 - MathF.Sin(phi));
                return k > 1e-9f ? (q - alpha * p) / k : q - alpha * p;

            case FailureCriterion.HoekBrown:
                var ucs_Pa = 2 * cohesion * MathF.Cos(phi) / (1 - MathF.Sin(phi));
                var mb = _params.HoekBrown_mb;
                var s = _params.HoekBrown_s;
                var a = _params.HoekBrown_a;
                var strength = ucs_Pa * MathF.Pow(mb * sigma3 / ucs_Pa + s, a);
                var failure_stress = sigma3 + strength;
                return failure_stress > 1e-9f ? sigma1 / failure_stress : sigma1;

            case FailureCriterion.Griffith:
                if (sigma3 < 0)
                    return tensileStrength > 1e-9f ? -sigma3 / tensileStrength : -sigma3;
                return tensileStrength * 8 > 1e-9f ? (sigma1 - sigma3) / (8 * tensileStrength) : sigma1 - sigma3;

            default:
                return 0f;
        }
    }

    private float CalculateAverageDamage(GeomechanicalResults results)
    {
        long sum = 0;
        var count = 0;

        var w = results.DamageField.GetLength(0);
        var h = results.DamageField.GetLength(1);
        var d = results.DamageField.GetLength(2);

        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            if (results.MaterialLabels[x, y, z] != 0)
            {
                sum += results.DamageField[x, y, z];
                count++;
            }

        return count > 0 ? sum / (float)count * 100f / 255f : 0f;
    }

    private void GenerateMohrCircles(GeomechanicalResults results)
    {
        var w = results.Sigma1.GetLength(0);
        var h = results.Sigma1.GetLength(1);
        var d = results.Sigma1.GetLength(2);

        var locations = new List<(string name, int x, int y, int z)>
        {
            ("Center", w / 2, h / 2, d / 2),
            ("Top", w / 2, h / 2, d - 1),
            ("Bottom", w / 2, h / 2, 0)
        };

        var maxStressValue = float.MinValue;
        int maxX = 0, maxY = 0, maxZ = 0;
        var maxStressLocationFound = false;

        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            if (results.MaterialLabels[x, y, z] == 0) continue;

            var stress = results.Sigma1[x, y, z];
            if (stress > maxStressValue)
            {
                maxStressValue = stress;
                maxX = x;
                maxY = y;
                maxZ = z;
                maxStressLocationFound = true;
            }
        }

        if (maxStressLocationFound)
            locations.Add(("Max Stress", maxX, maxY, maxZ));

        foreach (var (name, x, y, z) in locations)
        {
            if (x < 0 || x >= w || y < 0 || y >= h || z < 0 || z >= d ||
                results.MaterialLabels[x, y, z] == 0)
                continue;

            var sigma1 = results.Sigma1[x, y, z];
            var sigma2 = results.Sigma2[x, y, z];
            var sigma3 = results.Sigma3[x, y, z];
            var hasFailed = results.FractureField[x, y, z];

            var circle = new MohrCircleData
            {
                Location = name,
                Position = new Vector3(x, y, z),
                Sigma1 = sigma1 / 1e6f,
                Sigma2 = sigma2 / 1e6f,
                Sigma3 = sigma3 / 1e6f,
                MaxShearStress = (sigma1 - sigma3) / (2 * 1e6f),
                HasFailed = hasFailed
            };

            var phi_rad = _params.FrictionAngle * MathF.PI / 180f;
            var failureAngle_rad = MathF.PI / 4 + phi_rad / 2;
            circle.FailureAngle = failureAngle_rad * 180f / MathF.PI;

            if (hasFailed)
            {
                var two_theta = 2 * failureAngle_rad;
                circle.NormalStressAtFailure =
                    ((sigma1 + sigma3) / 2 + (sigma1 - sigma3) / 2 * MathF.Cos(two_theta)) / 1e6f;
                circle.ShearStressAtFailure = (sigma1 - sigma3) / 2 * MathF.Sin(two_theta) / 1e6f;
            }

            results.MohrCircles.Add(circle);
        }
    }

    // Chunk management
    private void LoadChunkIfNeeded(int chunkId)
    {
        var chunk = _chunks[chunkId];

        if (chunk.IsOffloaded)
        {
            LoadChunk(chunk);
            TrackChunkAccess(chunkId);
        }
        else
        {
            TrackChunkAccess(chunkId);
        }
    }

    private void TrackChunkAccess(int chunkIdx)
    {
        lock (_chunkAccessLock)
        {
            if (_chunkAccessSet.Contains(chunkIdx))
            {
                var tempQueue = new Queue<int>();
                while (_chunkAccessOrder.Count > 0)
                {
                    var idx = _chunkAccessOrder.Dequeue();
                    if (idx != chunkIdx)
                        tempQueue.Enqueue(idx);
                }

                _chunkAccessOrder = tempQueue;
            }
            else
            {
                _chunkAccessSet.Add(chunkIdx);
            }

            _chunkAccessOrder.Enqueue(chunkIdx);
        }
    }

    private void EvictLRUChunksIfNeeded()
    {
        lock (_chunkAccessLock)
        {
            while (_chunkAccessOrder.Count > 0 &&
                   (_chunks.Count(c => !c.IsOffloaded) > _maxLoadedChunks ||
                    _currentMemoryUsageBytes > _maxMemoryBudgetBytes))
            {
                var lruChunkIdx = _chunkAccessOrder.Dequeue();
                _chunkAccessSet.Remove(lruChunkIdx);

                var chunk = _chunks[lruChunkIdx];
                if (!chunk.IsOffloaded)
                {
                    OffloadChunk(chunk);
                    _currentMemoryUsageBytes -= chunk.EstimateMemorySize();
                }
            }
        }
    }

    private void LoadChunk(GeomechanicalChunk chunk)
    {
        if (!chunk.IsOffloaded || string.IsNullOrEmpty(_offloadPath))
            return;

        var chunkFile = Path.Combine(_offloadPath, $"geomech_chunk_{chunk.StartZ}.dat");
        if (File.Exists(chunkFile))
        {
            using var fs = File.OpenRead(chunkFile);
            using var br = new BinaryReader(fs);

            chunk.Labels = Read3DByteArray(br, chunk.Width, chunk.Height, chunk.Depth);
            chunk.Density = Read3DFloatArray(br, chunk.Width, chunk.Height, chunk.Depth);
            chunk.IsOffloaded = false;
        }
    }

    private void OffloadChunk(GeomechanicalChunk chunk)
    {
        if (chunk.IsOffloaded || string.IsNullOrEmpty(_offloadPath))
            return;

        var chunkFile = Path.Combine(_offloadPath, $"geomech_chunk_{chunk.StartZ}.dat");
        using var fs = File.Create(chunkFile);
        using var bw = new BinaryWriter(fs);

        Write3DByteArray(bw, chunk.Labels);
        Write3DFloatArray(bw, chunk.Density);

        chunk.Labels = null;
        chunk.Density = null;
        chunk.IsOffloaded = true;
    }

    private void UpdateMemoryUsage()
    {
        _currentMemoryUsageBytes = _chunks.Where(c => !c.IsOffloaded).Sum(c => c.EstimateMemorySize());
    }

    private void Cleanup()
    {
        if (!string.IsNullOrEmpty(_offloadPath) && Directory.Exists(_offloadPath))
            try
            {
                Directory.Delete(_offloadPath, true);
            }
            catch
            {
            }
    }

    // Utility methods
    private float[,] MatrixMultiply(float[,] A, float[,] B)
    {
        var m = A.GetLength(0);
        var n = B.GetLength(1);
        var k = A.GetLength(1);
        var C = new float[m, n];

        for (var i = 0; i < m; i++)
        for (var j = 0; j < n; j++)
        for (var p = 0; p < k; p++)
            C[i, j] += A[i, p] * B[p, j];

        return C;
    }

    private float Determinant3x3(float[,] m)
    {
        return m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1])
               - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
               + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
    }

    private float[,] Inverse3x3(float[,] m)
    {
        var det = Determinant3x3(m);
        if (MathF.Abs(det) < 1e-12f)
            throw new Exception("Singular matrix");

        var invDet = 1.0f / det;
        var inv = new float[3, 3];

        inv[0, 0] = (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) * invDet;
        inv[0, 1] = (m[0, 2] * m[2, 1] - m[0, 1] * m[2, 2]) * invDet;
        inv[0, 2] = (m[0, 1] * m[1, 2] - m[0, 2] * m[1, 1]) * invDet;
        inv[1, 0] = (m[1, 2] * m[2, 0] - m[1, 0] * m[2, 2]) * invDet;
        inv[1, 1] = (m[0, 0] * m[2, 2] - m[0, 2] * m[2, 0]) * invDet;
        inv[1, 2] = (m[0, 2] * m[1, 0] - m[0, 0] * m[1, 2]) * invDet;
        inv[2, 0] = (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]) * invDet;
        inv[2, 1] = (m[0, 1] * m[2, 0] - m[0, 0] * m[2, 1]) * invDet;
        inv[2, 2] = (m[0, 0] * m[1, 1] - m[0, 1] * m[1, 0]) * invDet;

        return inv;
    }

    private void ConvertCOOtoCSR(List<int> cooRow, List<int> cooCol, List<float> cooVal)
    {
        var nnz = cooRow.Count;
        var indices = Enumerable.Range(0, nnz).ToArray();
        Array.Sort(indices, (a, b) =>
        {
            var cmp = cooRow[a].CompareTo(cooRow[b]);
            if (cmp == 0) cmp = cooCol[a].CompareTo(cooCol[b]);
            return cmp;
        });

        _rowPtr = new List<int>(_numDOFs + 1);
        _colIdx = new List<int>(nnz);
        _values = new List<float>(nnz);

        var currentRow = 0;
        _rowPtr.Add(0);

        for (var i = 0; i < nnz; i++)
        {
            var idx = indices[i];
            var row = cooRow[idx];
            var col = cooCol[idx];
            var val = cooVal[idx];

            while (currentRow < row)
            {
                currentRow++;
                _rowPtr.Add(_colIdx.Count);
            }

            if (_colIdx.Count > 0 && _colIdx[_colIdx.Count - 1] == col)
            {
                _values[_values.Count - 1] += val;
            }
            else
            {
                _colIdx.Add(col);
                _values.Add(val);
            }
        }

        while (currentRow < _numDOFs)
        {
            currentRow++;
            _rowPtr.Add(_colIdx.Count);
        }
    }

    private float DotProduct(float[] a, float[] b)
    {
        float sum = 0;
        for (var i = 0; i < a.Length; i++)
            sum += a[i] * b[i];
        return sum;
    }

    private float VectorNorm(float[] v)
    {
        return MathF.Sqrt(DotProduct(v, v));
    }

    private byte[,,] Read3DByteArray(BinaryReader br, int w, int h, int d)
    {
        var arr = new byte[w, h, d];
        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            arr[x, y, z] = br.ReadByte();
        return arr;
    }

    private float[,,] Read3DFloatArray(BinaryReader br, int w, int h, int d)
    {
        var arr = new float[w, h, d];
        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            arr[x, y, z] = br.ReadSingle();
        return arr;
    }

    private void Write3DByteArray(BinaryWriter bw, byte[,,] arr)
    {
        for (var z = 0; z < arr.GetLength(2); z++)
        for (var y = 0; y < arr.GetLength(1); y++)
        for (var x = 0; x < arr.GetLength(0); x++)
            bw.Write(arr[x, y, z]);
    }

    private void Write3DFloatArray(BinaryWriter bw, float[,,] arr)
    {
        for (var z = 0; z < arr.GetLength(2); z++)
        for (var y = 0; y < arr.GetLength(1); y++)
        for (var x = 0; x < arr.GetLength(0); x++)
            bw.Write(arr[x, y, z]);
    }
}

// Chunk data structure
public class GeomechanicalChunk
{
    public int StartZ { get; set; }
    public int Depth { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public byte[,,] Labels { get; set; }
    public float[,,] Density { get; set; }
    public bool IsOffloaded { get; set; }

    public long EstimateMemorySize()
    {
        long size = 0;
        if (Labels != null)
            size += Width * Height * Depth * sizeof(byte);
        if (Density != null)
            size += Width * Height * Depth * sizeof(float);
        return size;
    }
}