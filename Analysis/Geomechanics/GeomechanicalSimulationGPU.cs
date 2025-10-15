// GeoscientistToolkit/Analysis/Geomechanics/GeomechanicalSimulatorGPU.cs
// GPU-accelerated Finite Element Method using OpenCL
// 
// References:
// [1] Cecka, C. et al. (2011). "Assembly of finite element methods on graphics processors." 
//     International Journal for Numerical Methods in Engineering, 85(5), 640-669.
// [2] Goddeke, D. et al. (2007). "Using GPUs to improve multigrid solver performance on a cluster."
//     International Journal of Computational Science and Engineering, 4(1), 36-55.
// [3] Bell, N. & Garland, M. (2009). "Implementing sparse matrix-vector multiplication on 
//     throughput-oriented processors." Proceedings of SC'09.
// [4] Zienkiewicz, O.C. & Taylor, R.L. (2000). The Finite Element Method, Volume 1: The Basis.

using System.Numerics;
using System.Text;
using GeoscientistToolkit.Util;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Analysis.Geomechanics;

public unsafe partial class GeomechanicalSimulatorGPU : IDisposable
{
    private readonly CL _cl;
    private readonly GeomechanicalParameters _params;
    private readonly int _workGroupSize = 256;
    private nint _bufDirichletValue;

    // FEM system buffers
    private nint _bufDisplacement;
    private nint _bufElementE, _bufElementNu;
    private nint _bufElementNodes;
    private nint _bufFailureIndex, _bufDamage;
    private nint _bufForce;
    private nint _bufIsDirichlet;
    private nint _bufLabels;
    private nint _bufMInv; // Preconditioner

    // Mesh data buffers
    private nint _bufNodeX, _bufNodeY, _bufNodeZ;
    private nint _bufReductionTemp; // For parallel reductions

    // CG solver buffers
    private nint _bufResidual, _bufZ, _bufP, _bufQ;

    // Sparse matrix buffers (CSR format)
    private nint _bufRowPtr, _bufColIdx, _bufValues;
    private nint _bufSigma1, _bufSigma2, _bufSigma3;
    private nint _bufStrainXX, _bufStrainYY, _bufStrainZZ;
    private nint _bufStrainXY, _bufStrainXZ, _bufStrainYZ;

    // Result buffers
    private nint _bufStressXX, _bufStressYY, _bufStressZZ;
    private nint _bufStressXY, _bufStressXZ, _bufStressYZ;
    private nint _context, _queue, _program;

    private bool _initialized;
    private nint _kernelApplyBC;

    // Kernels
    private nint _kernelAssembleElement;
    private nint _kernelCalculatePrincipal;
    private nint _kernelCalculateStrains;
    private nint _kernelDotProduct;
    private nint _kernelEvaluateFailure;
    private nint _kernelSpMV;
    private nint _kernelVectorOps;
    private int _numNodes, _numElements, _numDOFs, _nnz;

    public GeomechanicalSimulatorGPU(GeomechanicalParameters parameters)
    {
        _params = parameters;
        _cl = CL.GetApi();
        InitializeOpenCL();
    }

    public void Dispose()
    {
        // Release all buffers
        if (_bufNodeX != 0) _cl.ReleaseMemObject(_bufNodeX);
        if (_bufNodeY != 0) _cl.ReleaseMemObject(_bufNodeY);
        if (_bufNodeZ != 0) _cl.ReleaseMemObject(_bufNodeZ);
        if (_bufElementNodes != 0) _cl.ReleaseMemObject(_bufElementNodes);
        if (_bufElementE != 0) _cl.ReleaseMemObject(_bufElementE);
        if (_bufElementNu != 0) _cl.ReleaseMemObject(_bufElementNu);
        if (_bufLabels != 0) _cl.ReleaseMemObject(_bufLabels);
        if (_bufDisplacement != 0) _cl.ReleaseMemObject(_bufDisplacement);
        if (_bufForce != 0) _cl.ReleaseMemObject(_bufForce);
        if (_bufIsDirichlet != 0) _cl.ReleaseMemObject(_bufIsDirichlet);
        if (_bufDirichletValue != 0) _cl.ReleaseMemObject(_bufDirichletValue);
        if (_bufRowPtr != 0) _cl.ReleaseMemObject(_bufRowPtr);
        if (_bufColIdx != 0) _cl.ReleaseMemObject(_bufColIdx);
        if (_bufValues != 0) _cl.ReleaseMemObject(_bufValues);
        if (_bufResidual != 0) _cl.ReleaseMemObject(_bufResidual);
        if (_bufZ != 0) _cl.ReleaseMemObject(_bufZ);
        if (_bufP != 0) _cl.ReleaseMemObject(_bufP);
        if (_bufQ != 0) _cl.ReleaseMemObject(_bufQ);
        if (_bufMInv != 0) _cl.ReleaseMemObject(_bufMInv);
        if (_bufReductionTemp != 0) _cl.ReleaseMemObject(_bufReductionTemp);
        if (_bufStressXX != 0) _cl.ReleaseMemObject(_bufStressXX);
        if (_bufStressYY != 0) _cl.ReleaseMemObject(_bufStressYY);
        if (_bufStressZZ != 0) _cl.ReleaseMemObject(_bufStressZZ);
        if (_bufStressXY != 0) _cl.ReleaseMemObject(_bufStressXY);
        if (_bufStressXZ != 0) _cl.ReleaseMemObject(_bufStressXZ);
        if (_bufStressYZ != 0) _cl.ReleaseMemObject(_bufStressYZ);
        if (_bufStrainXX != 0) _cl.ReleaseMemObject(_bufStrainXX);
        if (_bufStrainYY != 0) _cl.ReleaseMemObject(_bufStrainYY);
        if (_bufStrainZZ != 0) _cl.ReleaseMemObject(_bufStrainZZ);
        if (_bufStrainXY != 0) _cl.ReleaseMemObject(_bufStrainXY);
        if (_bufStrainXZ != 0) _cl.ReleaseMemObject(_bufStrainXZ);
        if (_bufStrainYZ != 0) _cl.ReleaseMemObject(_bufStrainYZ);
        if (_bufSigma1 != 0) _cl.ReleaseMemObject(_bufSigma1);
        if (_bufSigma2 != 0) _cl.ReleaseMemObject(_bufSigma2);
        if (_bufSigma3 != 0) _cl.ReleaseMemObject(_bufSigma3);
        if (_bufFailureIndex != 0) _cl.ReleaseMemObject(_bufFailureIndex);
        if (_bufDamage != 0) _cl.ReleaseMemObject(_bufDamage);

        // Release kernels
        if (_kernelAssembleElement != 0) _cl.ReleaseKernel(_kernelAssembleElement);
        if (_kernelApplyBC != 0) _cl.ReleaseKernel(_kernelApplyBC);
        if (_kernelSpMV != 0) _cl.ReleaseKernel(_kernelSpMV);
        if (_kernelDotProduct != 0) _cl.ReleaseKernel(_kernelDotProduct);
        if (_kernelVectorOps != 0) _cl.ReleaseKernel(_kernelVectorOps);
        if (_kernelCalculateStrains != 0) _cl.ReleaseKernel(_kernelCalculateStrains);
        if (_kernelCalculatePrincipal != 0) _cl.ReleaseKernel(_kernelCalculatePrincipal);
        if (_kernelEvaluateFailure != 0) _cl.ReleaseKernel(_kernelEvaluateFailure);

        // Release program, queue, context
        if (_program != 0) _cl.ReleaseProgram(_program);
        if (_queue != 0) _cl.ReleaseCommandQueue(_queue);
        if (_context != 0) _cl.ReleaseContext(_context);
    }

    public GeomechanicalResults Simulate(byte[,,] labels, float[,,] density,
        IProgress<float> progress, CancellationToken token)
    {
        if (!_initialized)
            throw new InvalidOperationException("GPU not initialized");

        var extent = _params.SimulationExtent;
        var startTime = DateTime.Now;

        try
        {
            // Step 1: Generate mesh and unpack tuple
            progress?.Report(0.05f);
            var meshResult = GenerateMeshFromVoxels(labels, density);
            _numNodes = meshResult.numNodes;
            _numElements = meshResult.numElements;
            _numDOFs = meshResult.numDOFs;
            UploadMeshData(meshResult.mesh);
            token.ThrowIfCancellationRequested();

            // Step 2: Assemble stiffness matrix
            progress?.Report(0.15f);
            AssembleStiffnessMatrixGPU();
            token.ThrowIfCancellationRequested();

            // Step 3: Apply BC - pass only mesh data
            progress?.Report(0.25f);
            ApplyBoundaryConditionsGPU(meshResult.mesh);
            token.ThrowIfCancellationRequested();

            // Step 4: Solve
            progress?.Report(0.35f);
            var converged = SolveDisplacementsGPU(progress, token);
            token.ThrowIfCancellationRequested();

            // Step 5: Calculate strains/stresses
            progress?.Report(0.75f);
            CalculateStrainsAndStressesGPU();
            token.ThrowIfCancellationRequested();

            // Step 6: Principal stresses
            progress?.Report(0.85f);
            CalculatePrincipalStressesGPU();
            token.ThrowIfCancellationRequested();

            // Step 7: Failure
            progress?.Report(0.90f);
            EvaluateFailureGPU();
            token.ThrowIfCancellationRequested();

            // Step 8: Download
            progress?.Report(0.95f);
            var results = DownloadResults(extent, labels);
            results.Converged = converged;
            results.ComputationTime = DateTime.Now - startTime;

            progress?.Report(1.0f);
            return results;
        }
        catch (Exception ex)
        {
            throw new Exception($"GPU simulation failed: {ex.Message}", ex);
        }
    }

    private void CheckError(int error, string operation)
    {
        if (error != 0)
            throw new Exception($"OpenCL error in {operation}: {(CLEnum)error}");
    }

    private void InitializeOpenCL()
    {
        try
        {
            uint numPlatforms;
            _cl.GetPlatformIDs(0, null, &numPlatforms);
            if (numPlatforms == 0)
                throw new Exception("No OpenCL platforms found");

            var platforms = stackalloc nint[(int)numPlatforms];
            _cl.GetPlatformIDs(numPlatforms, platforms, null);

            uint numDevices;
            _cl.GetDeviceIDs(platforms[0], DeviceType.Gpu, 0, null, &numDevices);
            if (numDevices == 0)
                _cl.GetDeviceIDs(platforms[0], DeviceType.All, 0, null, &numDevices);

            if (numDevices == 0)
                throw new Exception("No OpenCL devices found");

            var devices = stackalloc nint[(int)numDevices];
            _cl.GetDeviceIDs(platforms[0], DeviceType.Gpu, numDevices, devices, null);
            if (devices[0] == 0)
                _cl.GetDeviceIDs(platforms[0], DeviceType.All, numDevices, devices, null);

            int error;
            _context = _cl.CreateContext(null, 1, devices, null, null, &error);
            CheckError(error, "CreateContext");

            _queue = _cl.CreateCommandQueue(_context, devices[0], CommandQueueProperties.None, &error);
            CheckError(error, "CreateCommandQueue");

            BuildProgram();
            _initialized = true;

            Logger.Log("[GeomechanicalGPU] OpenCL initialized successfully");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[GeomechanicalGPU] Initialization failed: {ex.Message}");
            throw;
        }
    }

    private void BuildProgram()
    {
        var source = GetKernelSource();
        var sourceBytes = Encoding.UTF8.GetBytes(source);

        int error;
        fixed (byte* sourcePtr = sourceBytes)
        {
            var lengths = stackalloc nuint[1];
            lengths[0] = (nuint)sourceBytes.Length;
            var sourcePtrs = stackalloc byte*[1];
            sourcePtrs[0] = sourcePtr;
            _program = _cl.CreateProgramWithSource(_context, 1, sourcePtrs, lengths, &error);
            CheckError(error, "CreateProgramWithSource");
        }

        error = _cl.BuildProgram(_program, 0, null, (byte*)null, null, null);
        if (error != 0)
        {
            nuint logSize;
            _cl.GetProgramBuildInfo(_program, 0, (uint)ProgramBuildInfo.BuildLog, 0, null, &logSize);
            var log = new byte[logSize];
            fixed (byte* logPtr = log)
            {
                _cl.GetProgramBuildInfo(_program, 0, (uint)ProgramBuildInfo.BuildLog, logSize, logPtr, null);
            }

            var logString = Encoding.UTF8.GetString(log);
            throw new Exception($"OpenCL build failed: {logString}");
        }

        // Create kernels
        _kernelAssembleElement = _cl.CreateKernel(_program, "assemble_element_stiffness", &error);
        CheckError(error, "CreateKernel assemble_element_stiffness");

        _kernelApplyBC = _cl.CreateKernel(_program, "apply_boundary_conditions", &error);
        CheckError(error, "CreateKernel apply_boundary_conditions");

        _kernelSpMV = _cl.CreateKernel(_program, "sparse_matrix_vector_multiply", &error);
        CheckError(error, "CreateKernel sparse_matrix_vector_multiply");

        _kernelDotProduct = _cl.CreateKernel(_program, "dot_product_reduction", &error);
        CheckError(error, "CreateKernel dot_product_reduction");

        _kernelVectorOps = _cl.CreateKernel(_program, "vector_operations", &error);
        CheckError(error, "CreateKernel vector_operations");

        _kernelCalculateStrains = _cl.CreateKernel(_program, "calculate_strains_stresses", &error);
        CheckError(error, "CreateKernel calculate_strains_stresses");

        _kernelCalculatePrincipal = _cl.CreateKernel(_program, "calculate_principal_stresses", &error);
        CheckError(error, "CreateKernel calculate_principal_stresses");

        _kernelEvaluateFailure = _cl.CreateKernel(_program, "evaluate_failure", &error);
        CheckError(error, "CreateKernel evaluate_failure");
    }

    private string GetKernelSource()
    {
        return @"
//==============================================================================
// OpenCL Kernels for Finite Element Geomechanical Simulation
// 
// References:
// [1] Cecka et al. (2011) - GPU FEM assembly strategies
// [2] Bell & Garland (2009) - Sparse matrix operations on GPU
// [3] Zienkiewicz & Taylor (2000) - FEM formulation
//==============================================================================

// Helper: Compute shape function derivatives in natural coordinates
// Reference: [3] Zienkiewicz, Eq 8.11
void compute_shape_derivatives(float xi, float eta, float zeta, 
                               __private float dN_dxi[3][8])
{
    // Node coordinates in natural space
    float nc[8][3] = {
        {-1, -1, -1}, {+1, -1, -1}, {+1, +1, -1}, {-1, +1, -1},
        {-1, -1, +1}, {+1, -1, +1}, {+1, +1, +1}, {-1, +1, +1}
    };
    
    for (int i = 0; i < 8; i++)
    {
        float xi_i = nc[i][0];
        float eta_i = nc[i][1];
        float zeta_i = nc[i][2];
        
        dN_dxi[0][i] = 0.125f * xi_i * (1.0f + eta_i * eta) * (1.0f + zeta_i * zeta);
        dN_dxi[1][i] = 0.125f * (1.0f + xi_i * xi) * eta_i * (1.0f + zeta_i * zeta);
        dN_dxi[2][i] = 0.125f * (1.0f + xi_i * xi) * (1.0f + eta_i * eta) * zeta_i;
    }
}

// Helper: Compute Jacobian matrix
// Reference: [3] Zienkiewicz, Section 8.3
void compute_jacobian(const __private float dN_dxi[3][8],
                     const __private float ex[8],
                     const __private float ey[8],
                     const __private float ez[8],
                     __private float J[3][3])
{
    for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
            J[i][j] = 0.0f;
    
    for (int i = 0; i < 8; i++)
    {
        J[0][0] += dN_dxi[0][i] * ex[i];
        J[0][1] += dN_dxi[0][i] * ey[i];
        J[0][2] += dN_dxi[0][i] * ez[i];
        
        J[1][0] += dN_dxi[1][i] * ex[i];
        J[1][1] += dN_dxi[1][i] * ey[i];
        J[1][2] += dN_dxi[1][i] * ez[i];
        
        J[2][0] += dN_dxi[2][i] * ex[i];
        J[2][1] += dN_dxi[2][i] * ey[i];
        J[2][2] += dN_dxi[2][i] * ez[i];
    }
}

// Helper: Compute 3x3 determinant
float determinant_3x3(const __private float m[3][3])
{
    return m[0][0] * (m[1][1] * m[2][2] - m[1][2] * m[2][1])
         - m[0][1] * (m[1][0] * m[2][2] - m[1][2] * m[2][0])
         + m[0][2] * (m[1][0] * m[2][1] - m[1][1] * m[2][0]);
}

// Helper: Compute 3x3 inverse
void inverse_3x3(const __private float m[3][3], __private float inv[3][3])
{
    float det = determinant_3x3(m);
    float invDet = 1.0f / det;
    
    inv[0][0] = (m[1][1] * m[2][2] - m[1][2] * m[2][1]) * invDet;
    inv[0][1] = (m[0][2] * m[2][1] - m[0][1] * m[2][2]) * invDet;
    inv[0][2] = (m[0][1] * m[1][2] - m[0][2] * m[1][1]) * invDet;
    inv[1][0] = (m[1][2] * m[2][0] - m[1][0] * m[2][2]) * invDet;
    inv[1][1] = (m[0][0] * m[2][2] - m[0][2] * m[2][0]) * invDet;
    inv[1][2] = (m[0][2] * m[1][0] - m[0][0] * m[1][2]) * invDet;
    inv[2][0] = (m[1][0] * m[2][1] - m[1][1] * m[2][0]) * invDet;
    inv[2][1] = (m[0][1] * m[2][0] - m[0][0] * m[2][1]) * invDet;
    inv[2][2] = (m[0][0] * m[1][1] - m[0][1] * m[1][0]) * invDet;
}

// Helper: Compute elasticity matrix D
// Reference: [5] Bower, Eq 3.2.19
void compute_elasticity_matrix(float E, float nu, __private float D[6][6])
{
    float lambda = E * nu / ((1.0f + nu) * (1.0f - 2.0f * nu));
    float mu = E / (2.0f * (1.0f + nu));
    float lambda_2mu = lambda + 2.0f * mu;
    
    // Initialize to zero
    for (int i = 0; i < 6; i++)
        for (int j = 0; j < 6; j++)
            D[i][j] = 0.0f;
    
    // Normal components
    D[0][0] = lambda_2mu; D[0][1] = lambda;      D[0][2] = lambda;
    D[1][0] = lambda;      D[1][1] = lambda_2mu; D[1][2] = lambda;
    D[2][0] = lambda;      D[2][1] = lambda;      D[2][2] = lambda_2mu;
    
    // Shear components
    D[3][3] = mu;
    D[4][4] = mu;
    D[5][5] = mu;
}

// Helper: Compute strain-displacement matrix B
// Reference: [3] Bathe, Eq 6.6
void compute_B_matrix(const __private float dN_dx[3][8], __private float B[6][24])
{
    // Initialize
    for (int i = 0; i < 6; i++)
        for (int j = 0; j < 24; j++)
            B[i][j] = 0.0f;
    
    for (int i = 0; i < 8; i++)
    {
        float dNi_dx = dN_dx[0][i];
        float dNi_dy = dN_dx[1][i];
        float dNi_dz = dN_dx[2][i];
        
        int col = i * 3;
        
        B[0][col + 0] = dNi_dx;        // εxx = ∂ux/∂x
        B[1][col + 1] = dNi_dy;        // εyy = ∂uy/∂y
        B[2][col + 2] = dNi_dz;        // εzz = ∂uz/∂z
        B[3][col + 0] = dNi_dy;        // γxy = ∂ux/∂y + ∂uy/∂x
        B[3][col + 1] = dNi_dx;
        B[4][col + 0] = dNi_dz;        // γxz = ∂ux/∂z + ∂uz/∂x
        B[4][col + 2] = dNi_dx;
        B[5][col + 1] = dNi_dz;        // γyz = ∂uy/∂z + ∂uz/∂y
        B[5][col + 2] = dNi_dy;
    }
}

//==============================================================================
// Kernel: Assemble element stiffness matrices in parallel
// Reference: [1] Cecka et al., Algorithm 1
//
// This kernel computes element stiffness matrices and assembles them into
// the global stiffness matrix using atomic operations.
//==============================================================================
__kernel void assemble_element_stiffness(
    const int numElements,
    __global const float* nodeX,
    __global const float* nodeY,
    __global const float* nodeZ,
    __global const int* elementNodes,  // [numElements * 8]
    __global const float* elementE,
    __global const float* elementNu,
    __global const int* rowPtr,        // CSR row pointers
    __global const int* colIdx,        // CSR column indices
    __global float* values)            // CSR values (output)
{
    int e = get_global_id(0);
    if (e >= numElements) return;
    
    // Get element nodes
    int nodes[8];
    for (int i = 0; i < 8; i++)
        nodes[i] = elementNodes[e * 8 + i];
    
    // Get element coordinates
    float ex[8], ey[8], ez[8];
    for (int i = 0; i < 8; i++)
    {
        ex[i] = nodeX[nodes[i]];
        ey[i] = nodeY[nodes[i]];
        ez[i] = nodeZ[nodes[i]];
    }
    
    // Material properties
    float E = elementE[e];
    float nu = elementNu[e];
    
    // Elasticity matrix
    float D[6][6];
    compute_elasticity_matrix(E, nu, D);
    
    // Element stiffness matrix (stored locally)
    float Ke[24][24];
    for (int i = 0; i < 24; i++)
        for (int j = 0; j < 24; j++)
            Ke[i][j] = 0.0f;
    
    // Gauss quadrature: 2x2x2 integration
    float gp = 1.0f / sqrt(3.0f);
    float gpts[8][4] = {
        {-gp, -gp, -gp, 1.0f}, {+gp, -gp, -gp, 1.0f},
        {+gp, +gp, -gp, 1.0f}, {-gp, +gp, -gp, 1.0f},
        {-gp, -gp, +gp, 1.0f}, {+gp, -gp, +gp, 1.0f},
        {+gp, +gp, +gp, 1.0f}, {-gp, +gp, +gp, 1.0f}
    };
    
    for (int gp_idx = 0; gp_idx < 8; gp_idx++)
    {
        float xi = gpts[gp_idx][0];
        float eta = gpts[gp_idx][1];
        float zeta = gpts[gp_idx][2];
        float w = gpts[gp_idx][3];
        
        // Shape function derivatives
        float dN_dxi[3][8];
        compute_shape_derivatives(xi, eta, zeta, dN_dxi);
        
        // Jacobian
        float J[3][3];
        compute_jacobian(dN_dxi, ex, ey, ez, J);
        float detJ = determinant_3x3(J);
        
        if (detJ <= 0.0f) continue; // Skip degenerate elements
        
        // Inverse Jacobian
        float Jinv[3][3];
        inverse_3x3(J, Jinv);
        
        // dN/dx = J^(-1) * dN/dxi
        float dN_dx[3][8];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 8; j++)
            {
                dN_dx[i][j] = 0.0f;
                for (int k = 0; k < 3; k++)
                    dN_dx[i][j] += Jinv[i][k] * dN_dxi[k][j];
            }
        
        // B matrix
        float B[6][24];
        compute_B_matrix(dN_dx, B);
        
        // Compute B^T * D * B and add to Ke
        float factor = detJ * w;
        
        // DB = D * B (6x24)
        float DB[6][24];
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 24; j++)
            {
                DB[i][j] = 0.0f;
                for (int k = 0; k < 6; k++)
                    DB[i][j] += D[i][k] * B[k][j];
            }
        
        // Ke += B^T * DB
        for (int i = 0; i < 24; i++)
            for (int j = 0; j < 24; j++)
                for (int k = 0; k < 6; k++)
                    Ke[i][j] += B[k][i] * DB[k][j] * factor;
    }
    
    // Assemble into global matrix using atomic operations
    for (int i = 0; i < 8; i++)
    {
        for (int j = 0; j < 8; j++)
        {
            for (int di = 0; di < 3; di++)
            {
                for (int dj = 0; dj < 3; dj++)
                {
                    int globalI = nodes[i] * 3 + di;
                    int globalJ = nodes[j] * 3 + dj;
                    int localI = i * 3 + di;
                    int localJ = j * 3 + dj;
                    
                    float value = Ke[localI][localJ];
                    if (fabs(value) < 1e-12f) continue;
                    
                    // Find position in CSR structure
                    int rowStart = rowPtr[globalI];
                    int rowEnd = rowPtr[globalI + 1];
                    
                    for (int idx = rowStart; idx < rowEnd; idx++)
                    {
                        if (colIdx[idx] == globalJ)
                        {
                            // Atomic add to global matrix
                            atomic_add_global(&values[idx], value);
                            break;
                        }
                    }
                }
            }
        }
    }
}

//==============================================================================
// Kernel: Apply boundary conditions to system
//==============================================================================
__kernel void apply_boundary_conditions(
    const int numDOFs,
    __global const uchar* isDirichlet,
    __global const float* dirichletValue,
    __global float* force,
    __global float* displacement)
{
    int dof = get_global_id(0);
    if (dof >= numDOFs) return;
    
    if (isDirichlet[dof])
    {
        displacement[dof] = dirichletValue[dof];
        force[dof] = dirichletValue[dof];
    }
}

//==============================================================================
// Kernel: Sparse matrix-vector multiplication (CSR format)
// Reference: [2] Bell & Garland, Algorithm 1
//
// Computes y = A * x where A is in CSR format
// Handles Dirichlet BC by treating constrained rows as identity
//==============================================================================
__kernel void sparse_matrix_vector_multiply(
    const int numRows,
    __global const int* rowPtr,
    __global const int* colIdx,
    __global const float* values,
    __global const float* x,
    __global const uchar* isDirichlet,
    __global float* y)
{
    int row = get_global_id(0);
    if (row >= numRows) return;
    
    if (isDirichlet[row])
    {
        y[row] = x[row]; // Identity for constrained DOF
        return;
    }
    
    int rowStart = rowPtr[row];
    int rowEnd = rowPtr[row + 1];
    
    float sum = 0.0f;
    for (int j = rowStart; j < rowEnd; j++)
    {
        int col = colIdx[j];
        if (!isDirichlet[col])
            sum += values[j] * x[col];
    }
    
    y[row] = sum;
}

//==============================================================================
// Kernel: Parallel reduction for dot product
// Reference: [2] Bell & Garland, Section 3.2
//
// First stage: partial sums in work groups
//==============================================================================
__kernel void dot_product_reduction(
    const int n,
    __global const float* a,
    __global const float* b,
    __global float* partialSums,
    __local float* localSums)
{
    int globalId = get_global_id(0);
    int localId = get_local_id(0);
    int localSize = get_local_size(0);
    
    // Load and compute partial products
    float sum = 0.0f;
    if (globalId < n)
        sum = a[globalId] * b[globalId];
    
    localSums[localId] = sum;
    barrier(CLK_LOCAL_MEM_FENCE);
    
    // Parallel reduction in local memory
    for (int offset = localSize / 2; offset > 0; offset /= 2)
    {
        if (localId < offset)
            localSums[localId] += localSums[localId + offset];
        barrier(CLK_LOCAL_MEM_FENCE);
    }
    
    // Write result for this work group
    if (localId == 0)
        partialSums[get_group_id(0)] = localSums[0];
}

//==============================================================================
// Kernel: Vector operations for CG solver
// op: 0 = y = a*x + y
//     1 = y = x + a*y
//     2 = y = x - a*y
//     3 = preconditioner application: y = M^(-1) * x
//==============================================================================
__kernel void vector_operations(
    const int n,
    const int op,
    const float alpha,
    __global const float* x,
    __global float* y,
    __global const float* Minv,
    __global const uchar* isDirichlet)
{
    int i = get_global_id(0);
    if (i >= n) return;
    
    if (isDirichlet[i] && op != 3)
        return; // Don't modify constrained DOFs
    
    switch (op)
    {
        case 0: // y = a*x + y
            y[i] += alpha * x[i];
            break;
        case 1: // y = x + a*y
            y[i] = x[i] + alpha * y[i];
            break;
        case 2: // y = x - a*y
            y[i] = x[i] - alpha * y[i];
            break;
        case 3: // y = M^(-1) * x (preconditioning)
            y[i] = Minv[i] * x[i];
            break;
    }
}

//==============================================================================
// Kernel: Calculate strains and stresses from displacements
// Reference: [3] Bathe, Section 6.2.3
//==============================================================================
__kernel void calculate_strains_stresses(
    const int numElements,
    __global const float* nodeX,
    __global const float* nodeY,
    __global const float* nodeZ,
    __global const int* elementNodes,
    __global const float* displacement,
    __global const float* elementE,
    __global const float* elementNu,
    __global const uchar* labels,
    const int width,
    const int height,
    const int depth,
    const float voxelSize,
    __global float* strainXX, __global float* strainYY, __global float* strainZZ,
    __global float* strainXY, __global float* strainXZ, __global float* strainYZ,
    __global float* stressXX, __global float* stressYY, __global float* stressZZ,
    __global float* stressXY, __global float* stressXZ, __global float* stressYZ)
{
    int e = get_global_id(0);
    if (e >= numElements) return;
    
    // Get element nodes and coordinates
    int nodes[8];
    float ex[8], ey[8], ez[8];
    for (int i = 0; i < 8; i++)
    {
        nodes[i] = elementNodes[e * 8 + i];
        ex[i] = nodeX[nodes[i]];
        ey[i] = nodeY[nodes[i]];
        ez[i] = nodeZ[nodes[i]];
    }
    
    // Get nodal displacements
    float ue[24];
    for (int i = 0; i < 8; i++)
    {
        int dofBase = nodes[i] * 3;
        ue[i * 3 + 0] = displacement[dofBase + 0];
        ue[i * 3 + 1] = displacement[dofBase + 1];
        ue[i * 3 + 2] = displacement[dofBase + 2];
    }
    
    // Evaluate at element center
    float dN_dxi[3][8];
    compute_shape_derivatives(0.0f, 0.0f, 0.0f, dN_dxi);
    
    float J[3][3];
    compute_jacobian(dN_dxi, ex, ey, ez, J);
    
    float Jinv[3][3];
    inverse_3x3(J, Jinv);
    
    float dN_dx[3][8];
    for (int i = 0; i < 3; i++)
        for (int j = 0; j < 8; j++)
        {
            dN_dx[i][j] = 0.0f;
            for (int k = 0; k < 3; k++)
                dN_dx[i][j] += Jinv[i][k] * dN_dxi[k][j];
        }
    
    float B[6][24];
    compute_B_matrix(dN_dx, B);
    
    // Compute strain
    float strain[6] = {0, 0, 0, 0, 0, 0};
    for (int i = 0; i < 6; i++)
        for (int j = 0; j < 24; j++)
            strain[i] += B[i][j] * ue[j];
    
    // Compute stress
    float E = elementE[e];
    float nu = elementNu[e];
    float D[6][6];
    compute_elasticity_matrix(E, nu, D);
    
    float stress[6] = {0, 0, 0, 0, 0, 0};
    for (int i = 0; i < 6; i++)
        for (int j = 0; j < 6; j++)
            stress[i] += D[i][j] * strain[j];
    
    // Find corresponding voxel
    float cx = 0, cy = 0, cz = 0;
    for (int i = 0; i < 8; i++)
    {
        cx += ex[i];
        cy += ey[i];
        cz += ez[i];
    }
    cx /= 8.0f;
    cy /= 8.0f;
    cz /= 8.0f;
    
    int vx = (int)(cx / voxelSize);
    int vy = (int)(cy / voxelSize);
    int vz = (int)(cz / voxelSize);
    
    if (vx >= 0 && vx < width && vy >= 0 && vy < height && vz >= 0 && vz < depth)
    {
        int idx = (vz * height + vy) * width + vx;
        
        strainXX[idx] = strain[0];
        strainYY[idx] = strain[1];
        strainZZ[idx] = strain[2];
        strainXY[idx] = strain[3];
        strainXZ[idx] = strain[4];
        strainYZ[idx] = strain[5];
        
        stressXX[idx] = stress[0];
        stressYY[idx] = stress[1];
        stressZZ[idx] = stress[2];
        stressXY[idx] = stress[3];
        stressXZ[idx] = stress[4];
        stressYZ[idx] = stress[5];
    }
}

//==============================================================================
// Kernel: Calculate principal stresses (same as CPU version)
//==============================================================================
__kernel void calculate_principal_stresses(
    __global const uchar* labels,
    __global const float* sxx, __global const float* syy, __global const float* szz,
    __global const float* sxy, __global const float* sxz, __global const float* syz,
    __global float* sigma1, __global float* sigma2, __global float* sigma3,
    const int width, const int height, const int depth)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int z = get_global_id(2);
    
    if (x >= width || y >= height || z >= depth) return;
    
    int idx = (z * height + y) * width + x;
    if (labels[idx] == 0) return;
    
    float s_xx = sxx[idx];
    float s_yy = syy[idx];
    float s_zz = szz[idx];
    float s_xy = sxy[idx];
    float s_xz = sxz[idx];
    float s_yz = syz[idx];
    
    // Compute invariants
    float I1 = s_xx + s_yy + s_zz;
    float I2 = s_xx*s_yy + s_yy*s_zz + s_zz*s_xx - s_xy*s_xy - s_xz*s_xz - s_yz*s_yz;
    float I3 = s_xx*s_yy*s_zz + 2*s_xy*s_xz*s_yz - s_xx*s_yz*s_yz - s_yy*s_xz*s_xz - s_zz*s_xy*s_xy;
    
    float p = I2 - I1 * I1 / 3.0f;
    float q = I3 + (2.0f * I1 * I1 * I1 - 9.0f * I1 * I2) / 27.0f;
    
    float sig1, sig2, sig3;
    
    if (fabs(p) < 1e-9f)
    {
        sig1 = sig2 = sig3 = I1 / 3.0f;
    }
    else
    {
        float half_q = q * 0.5f;
        float term = -p * p * p / 27.0f;
        if (term < 0) term = 0;
        
        float r = sqrt(term);
        float cos_phi = clamp(-half_q / r, -1.0f, 1.0f);
        float phi = acos(cos_phi);
        
        float scale = 2.0f * sqrt(-p / 3.0f);
        float offset = I1 / 3.0f;
        
        sig1 = offset + scale * cos(phi / 3.0f);
        sig2 = offset + scale * cos((phi + 2.0f * M_PI) / 3.0f);
        sig3 = offset + scale * cos((phi + 4.0f * M_PI) / 3.0f);
    }
    
    // Sort
    if (sig1 < sig2) { float tmp = sig1; sig1 = sig2; sig2 = tmp; }
    if (sig1 < sig3) { float tmp = sig1; sig1 = sig3; sig3 = tmp; }
    if (sig2 < sig3) { float tmp = sig2; sig2 = sig3; sig3 = tmp; }
    
    sigma1[idx] = sig1;
    sigma2[idx] = sig2;
    sigma3[idx] = sig3;
}

//==============================================================================
// Kernel: Evaluate failure criteria (same as CPU version)
//==============================================================================
__kernel void evaluate_failure(
    __global const uchar* labels,
    __global const float* sigma1_buf, __global const float* sigma3_buf,
    __global float* failureIndex, __global uchar* damage,
    const int width, const int height, const int depth,
    const int criterion,
    const float cohesion_Pa, const float phi_rad, const float tensile_Pa,
    const float porePressure_Pa, const float biot,
    const float damageThreshold,
    const float hb_mb, const float hb_s, const float hb_a)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int z = get_global_id(2);
    
    if (x >= width || y >= height || z >= depth) return;
    
    int idx = (z * height + y) * width + x;
    if (labels[idx] == 0) return;
    
    float sig1 = sigma1_buf[idx];
    float sig3 = sigma3_buf[idx];
    
    if (porePressure_Pa > 0)
    {
        sig1 -= biot * porePressure_Pa;
        sig3 -= biot * porePressure_Pa;
    }
    
    float failure = 0.0f;
    
    if (criterion == 0) // Mohr-Coulomb
    {
        float left = sig1 - sig3;
        float right = 2.0f * cohesion_Pa * cos(phi_rad) + (sig1 + sig3) * sin(phi_rad);
        failure = right > 1e-9f ? left / right : left;
    }
    else if (criterion == 1) // Drucker-Prager
    {
        float p = (sig1 + sig3) / 2.0f;
        float q = (sig1 - sig3) / 2.0f;
        float alpha = 2.0f * sin(phi_rad) / (3.0f - sin(phi_rad));
        float k = 6.0f * cohesion_Pa * cos(phi_rad) / (3.0f - sin(phi_rad));
        failure = k > 1e-9f ? (q - alpha * p) / k : (q - alpha * p);
    }
    else if (criterion == 2) // Hoek-Brown
    {
        float ucs_Pa = 2.0f * cohesion_Pa * cos(phi_rad) / (1.0f - sin(phi_rad));
        if (ucs_Pa < 1e-6f) ucs_Pa = 1e-6f;
        float strength = ucs_Pa * pow(hb_mb * sig3 / ucs_Pa + hb_s, hb_a);
        float failure_stress = sig3 + strength;
        failure = failure_stress > 1e-9f ? sig1 / failure_stress : sig1;
    }
    else if (criterion == 3) // Griffith
    {
        if (sig3 < 0)
            failure = tensile_Pa > 1e-9f ? -sig3 / tensile_Pa : -sig3;
        else
            failure = (tensile_Pa * 8.0f) > 1e-9f ? (sig1 - sig3) / (8.0f * tensile_Pa) : (sig1 - sig3);
    }
    
    failureIndex[idx] = failure;
    
    if (failure >= 1.0f)
        damage[idx] = 255;
    else if (failure >= damageThreshold)
    {
        float dmg = (failure - damageThreshold) / (1.0f - damageThreshold);
        damage[idx] = (uchar)(dmg * 255.0f);
    }
    else
        damage[idx] = 0;
}
";
    }

    // CPU mesh generation - same as before
    private (int numNodes, int numElements, int numDOFs, MeshData mesh) GenerateMeshFromVoxels(
        byte[,,] labels, float[,,] density)
    {
        var extent = _params.SimulationExtent;
        var w = extent.Width;
        var h = extent.Height;
        var d = extent.Depth;
        var dx = _params.PixelSize / 1e6f;

        var elementCount = 0;
        for (var z = 0; z < d - 1; z++)
        for (var y = 0; y < h - 1; y++)
        for (var x = 0; x < w - 1; x++)
            if (labels[x, y, z] != 0)
                elementCount++;

        _numElements = elementCount;
        _numNodes = w * h * d;
        _numDOFs = _numNodes * 3;

        var mesh = new MeshData
        {
            nodeX = new float[_numNodes],
            nodeY = new float[_numNodes],
            nodeZ = new float[_numNodes],
            elementNodes = new int[_numElements * 8],
            elementE = new float[_numElements],
            elementNu = new float[_numElements],
            labels = new byte[w * h * d]
        };

        var nodeIdx = 0;
        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            mesh.nodeX[nodeIdx] = x * dx;
            mesh.nodeY[nodeIdx] = y * dx;
            mesh.nodeZ[nodeIdx] = z * dx;
            nodeIdx++;
        }

        var elemIdx = 0;
        for (var z = 0; z < d - 1; z++)
        for (var y = 0; y < h - 1; y++)
        for (var x = 0; x < w - 1; x++)
        {
            if (labels[x, y, z] == 0) continue;

            var n0 = (z * h + y) * w + x;
            var n1 = (z * h + y) * w + x + 1;
            var n2 = (z * h + y + 1) * w + x + 1;
            var n3 = (z * h + y + 1) * w + x;
            var n4 = ((z + 1) * h + y) * w + x;
            var n5 = ((z + 1) * h + y) * w + x + 1;
            var n6 = ((z + 1) * h + y + 1) * w + x + 1;
            var n7 = ((z + 1) * h + y + 1) * w + x;

            mesh.elementNodes[elemIdx * 8 + 0] = n0;
            mesh.elementNodes[elemIdx * 8 + 1] = n1;
            mesh.elementNodes[elemIdx * 8 + 2] = n2;
            mesh.elementNodes[elemIdx * 8 + 3] = n3;
            mesh.elementNodes[elemIdx * 8 + 4] = n4;
            mesh.elementNodes[elemIdx * 8 + 5] = n5;
            mesh.elementNodes[elemIdx * 8 + 6] = n6;
            mesh.elementNodes[elemIdx * 8 + 7] = n7;

            mesh.elementE[elemIdx] = _params.YoungModulus * 1e6f;
            mesh.elementNu[elemIdx] = _params.PoissonRatio;

            elemIdx++;
        }

        // Flatten labels
        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            mesh.labels[(z * h + y) * w + x] = labels[x, y, z];

        return (_numNodes, _numElements, _numDOFs, mesh);
    }

    private void UploadMeshData(MeshData mesh)
    {
        int error;

        // Node coordinates
        _bufNodeX = CreateAndUploadBuffer(mesh.nodeX, MemFlags.ReadOnly, out error);
        CheckError(error, "Upload nodeX");
        _bufNodeY = CreateAndUploadBuffer(mesh.nodeY, MemFlags.ReadOnly, out error);
        CheckError(error, "Upload nodeY");
        _bufNodeZ = CreateAndUploadBuffer(mesh.nodeZ, MemFlags.ReadOnly, out error);
        CheckError(error, "Upload nodeZ");

        // Element connectivity
        _bufElementNodes = CreateAndUploadBuffer(mesh.elementNodes, MemFlags.ReadOnly, out error);
        CheckError(error, "Upload elementNodes");

        // Material properties
        _bufElementE = CreateAndUploadBuffer(mesh.elementE, MemFlags.ReadOnly, out error);
        CheckError(error, "Upload elementE");
        _bufElementNu = CreateAndUploadBuffer(mesh.elementNu, MemFlags.ReadOnly, out error);
        CheckError(error, "Upload elementNu");

        // Labels
        _bufLabels = CreateAndUploadBuffer(mesh.labels, MemFlags.ReadOnly, out error);
        CheckError(error, "Upload labels");

        // DOF arrays
        _bufDisplacement = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
            (nuint)(_numDOFs * sizeof(float)), null, &error);
        CheckError(error, "Create displacement buffer");

        _bufForce = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
            (nuint)(_numDOFs * sizeof(float)), null, &error);
        CheckError(error, "Create force buffer");

        _bufIsDirichlet = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
            (nuint)_numDOFs, null, &error);
        CheckError(error, "Create isDirichlet buffer");

        _bufDirichletValue = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
            (nuint)(_numDOFs * sizeof(float)), null, &error);
        CheckError(error, "Create dirichletValue buffer");

        // CG solver buffers
        _bufResidual = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
            (nuint)(_numDOFs * sizeof(float)), null, &error);
        _bufZ = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
            (nuint)(_numDOFs * sizeof(float)), null, &error);
        _bufP = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
            (nuint)(_numDOFs * sizeof(float)), null, &error);
        _bufQ = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
            (nuint)(_numDOFs * sizeof(float)), null, &error);
        _bufMInv = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
            (nuint)(_numDOFs * sizeof(float)), null, &error);

        var numWorkGroups = (_numDOFs + _workGroupSize - 1) / _workGroupSize;
        _bufReductionTemp = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
            (nuint)(numWorkGroups * sizeof(float)), null, &error);
    }

    private nint CreateAndUploadBuffer<T>(T[] data, MemFlags flags, out int error) where T : unmanaged
    {
        var size = (nuint)(data.Length * sizeof(T));

        // FIX: Use local variable for pointer operation
        int err;
        var buf = _cl.CreateBuffer(_context, flags, size, null, &err);
        error = err; // Assign to out parameter

        if (err == 0)
            fixed (T* ptr = data)
            {
                err = _cl.EnqueueWriteBuffer(_queue, buf, true, 0, size, ptr, 0, null, null);
                if (err != 0) error = err; // Update error if write fails
            }

        return buf;
    }


    /// <summary>
    ///     Assemble global stiffness matrix on GPU.
    ///     Uses element-by-element assembly with atomic operations.
    ///     Reference: [1] Cecka et al. (2011) - GPU assembly strategies
    /// </summary>
    private void AssembleStiffnessMatrixGPU()
    {
        var (rowPtr, colIdx) = BuildCSRStructure();
        _nnz = colIdx.Count;

        int error;

        _bufRowPtr = CreateAndUploadBuffer(rowPtr.ToArray(), MemFlags.ReadOnly, out error);
        CheckError(error, "Upload rowPtr");

        _bufColIdx = CreateAndUploadBuffer(colIdx.ToArray(), MemFlags.ReadOnly, out error);
        CheckError(error, "Upload colIdx");

        var values = new float[_nnz];
        _bufValues = CreateAndUploadBuffer(values, MemFlags.ReadWrite, out error);
        CheckError(error, "Create values buffer");

        var globalSize = stackalloc nuint[1];
        globalSize[0] = (nuint)_numElements;

        // FIX: Create local variables to take address
        var numElements = _numElements;
        var bufNodeX = _bufNodeX;
        var bufNodeY = _bufNodeY;
        var bufNodeZ = _bufNodeZ;
        var bufElementNodes = _bufElementNodes;
        var bufElementE = _bufElementE;
        var bufElementNu = _bufElementNu;
        var bufRowPtr = _bufRowPtr;
        var bufColIdx = _bufColIdx;
        var bufValues = _bufValues;

        _cl.SetKernelArg(_kernelAssembleElement, 0, sizeof(int), &numElements);
        _cl.SetKernelArg(_kernelAssembleElement, 1, (nuint)IntPtr.Size, &bufNodeX);
        _cl.SetKernelArg(_kernelAssembleElement, 2, (nuint)IntPtr.Size, &bufNodeY);
        _cl.SetKernelArg(_kernelAssembleElement, 3, (nuint)IntPtr.Size, &bufNodeZ);
        _cl.SetKernelArg(_kernelAssembleElement, 4, (nuint)IntPtr.Size, &bufElementNodes);
        _cl.SetKernelArg(_kernelAssembleElement, 5, (nuint)IntPtr.Size, &bufElementE);
        _cl.SetKernelArg(_kernelAssembleElement, 6, (nuint)IntPtr.Size, &bufElementNu);
        _cl.SetKernelArg(_kernelAssembleElement, 7, (nuint)IntPtr.Size, &bufRowPtr);
        _cl.SetKernelArg(_kernelAssembleElement, 8, (nuint)IntPtr.Size, &bufColIdx);
        _cl.SetKernelArg(_kernelAssembleElement, 9, (nuint)IntPtr.Size, &bufValues);

        error = _cl.EnqueueNdrangeKernel(_queue, _kernelAssembleElement, 1, null,
            globalSize, null, 0, null, null);
        CheckError(error, "EnqueueNDRange assemble_element_stiffness");

        _cl.Finish(_queue);

        Logger.Log($"[GeomechGPU] Assembled stiffness matrix: {_numDOFs} DOFs, {_nnz} non-zeros");
    }

    /// <summary>
    ///     Build CSR (Compressed Sparse Row) structure for sparse matrix.
    ///     This determines the sparsity pattern without computing values.
    ///     Reference: [3] Bell & Garland (2009) - CSR format for GPU
    /// </summary>
    private (List<int> rowPtr, List<int> colIdx) BuildCSRStructure()
    {
        // For each DOF, determine which other DOFs it couples with
        var connectivity = new Dictionary<int, HashSet<int>>();

        for (var dof = 0; dof < _numDOFs; dof++)
            connectivity[dof] = new HashSet<int> { dof }; // Diagonal always present

        // Build connectivity from element topology
        // Each element couples all its DOFs together
        var elementNodes = new int[_numElements * 8];
        fixed (int* ptr = elementNodes)
        {
            var error = _cl.EnqueueReadBuffer(_queue, _bufElementNodes, true, 0,
                (nuint)(elementNodes.Length * sizeof(int)), ptr, 0, null, null);
            CheckError(error, "Read elementNodes");
        }

        for (var e = 0; e < _numElements; e++)
        {
            var nodes = new int[8];
            for (var i = 0; i < 8; i++)
                nodes[i] = elementNodes[e * 8 + i];

            // All DOFs in this element couple with each other
            var elementDOFs = new List<int>();
            for (var i = 0; i < 8; i++)
            for (var d = 0; d < 3; d++)
                elementDOFs.Add(nodes[i] * 3 + d);

            foreach (var dof1 in elementDOFs)
            foreach (var dof2 in elementDOFs)
                connectivity[dof1].Add(dof2);
        }

        // Build CSR structure
        var rowPtr = new List<int> { 0 };
        var colIdx = new List<int>();

        for (var row = 0; row < _numDOFs; row++)
        {
            var cols = connectivity[row].OrderBy(c => c).ToList();
            colIdx.AddRange(cols);
            rowPtr.Add(colIdx.Count);
        }

        return (rowPtr, colIdx);
    }

    /// <summary>
    ///     Apply boundary conditions and loading on GPU.
    ///     Reference: [3] Bathe, Section 8.2 - Boundary conditions
    /// </summary>
    private void ApplyBoundaryConditionsGPU(MeshData mesh)
    {
        var extent = _params.SimulationExtent;
        var w = extent.Width;
        var h = extent.Height;
        var d = extent.Depth;

        var sigma1_Pa = _params.Sigma1 * 1e6f;
        var sigma2_Pa = _params.Sigma2 * 1e6f;
        var sigma3_Pa = _params.Sigma3 * 1e6f;

        if (_params.UsePorePressure)
        {
            var pp_Pa = _params.PorePressure * 1e6f;
            var alpha = _params.BiotCoefficient;
            sigma1_Pa -= alpha * pp_Pa;
            sigma2_Pa -= alpha * pp_Pa;
            sigma3_Pa -= alpha * pp_Pa;
        }

        var force = new float[_numDOFs];
        var isDirichlet = new byte[_numDOFs];
        var dirichletValue = new float[_numDOFs];

        var dx = _params.PixelSize / 1e6f;
        var faceArea = dx * dx;
        var loadFactor = 0.25f;

        // Top face (z = d-1): Apply σ1
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var nodeIdx = ((d - 1) * h + y) * w + x;
            var dofZ = nodeIdx * 3 + 2;
            force[dofZ] -= sigma1_Pa * faceArea * loadFactor;
        }

        // Bottom face (z = 0): Fix z
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var nodeIdx = (0 * h + y) * w + x;
            var dofZ = nodeIdx * 3 + 2;
            isDirichlet[dofZ] = 1;
            dirichletValue[dofZ] = 0.0f;
        }

        // Y-direction faces
        for (var z = 0; z < d; z++)
        for (var x = 0; x < w; x++)
        {
            var nodeIdx1 = (z * h + 0) * w + x;
            var dofY1 = nodeIdx1 * 3 + 1;
            if (isDirichlet[dofY1] == 0)
                force[dofY1] += sigma2_Pa * faceArea * loadFactor;

            var nodeIdx2 = (z * h + (h - 1)) * w + x;
            var dofY2 = nodeIdx2 * 3 + 1;
            if (isDirichlet[dofY2] == 0)
                force[dofY2] -= sigma2_Pa * faceArea * loadFactor;
        }

        // X-direction
        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        {
            var nodeIdx1 = (z * h + y) * w + 0;
            var dofX1 = nodeIdx1 * 3 + 0;
            isDirichlet[dofX1] = 1;
            dirichletValue[dofX1] = 0.0f;

            var nodeIdx2 = (z * h + y) * w + (w - 1);
            var dofX2 = nodeIdx2 * 3 + 0;
            if (isDirichlet[dofX2] == 0)
                force[dofX2] -= sigma3_Pa * faceArea * loadFactor;
        }

        // Fix corner
        var cornerNode = 0;
        for (var i = 0; i < 3; i++)
        {
            isDirichlet[cornerNode * 3 + i] = 1;
            dirichletValue[cornerNode * 3 + i] = 0.0f;
        }

        // Upload
        int error;
        fixed (float* pForce = force)
        {
            error = _cl.EnqueueWriteBuffer(_queue, _bufForce, true, 0,
                (nuint)(_numDOFs * sizeof(float)), pForce, 0, null, null);
            CheckError(error, "Upload force");
        }

        fixed (byte* pIsDirichlet = isDirichlet)
        {
            error = _cl.EnqueueWriteBuffer(_queue, _bufIsDirichlet, true, 0,
                (nuint)_numDOFs, pIsDirichlet, 0, null, null);
            CheckError(error, "Upload isDirichlet");
        }

        fixed (float* pDirichletValue = dirichletValue)
        {
            error = _cl.EnqueueWriteBuffer(_queue, _bufDirichletValue, true, 0,
                (nuint)(_numDOFs * sizeof(float)), pDirichletValue, 0, null, null);
            CheckError(error, "Upload dirichletValue");
        }

        // Apply BC kernel
        var globalSize = stackalloc nuint[1];
        globalSize[0] = (nuint)((_numDOFs + _workGroupSize - 1) / _workGroupSize * _workGroupSize);

        // FIX: Local variables for address
        var numDOFs = _numDOFs;
        var bufIsDirichlet = _bufIsDirichlet;
        var bufDirichletValue = _bufDirichletValue;
        var bufForce = _bufForce;
        var bufDisplacement = _bufDisplacement;

        _cl.SetKernelArg(_kernelApplyBC, 0, sizeof(int), &numDOFs);
        _cl.SetKernelArg(_kernelApplyBC, 1, (nuint)IntPtr.Size, &bufIsDirichlet);
        _cl.SetKernelArg(_kernelApplyBC, 2, (nuint)IntPtr.Size, &bufDirichletValue);
        _cl.SetKernelArg(_kernelApplyBC, 3, (nuint)IntPtr.Size, &bufForce);
        _cl.SetKernelArg(_kernelApplyBC, 4, (nuint)IntPtr.Size, &bufDisplacement);

        error = _cl.EnqueueNdrangeKernel(_queue, _kernelApplyBC, 1, null,
            globalSize, null, 0, null, null);
        CheckError(error, "EnqueueNDRange apply_boundary_conditions");

        _cl.Finish(_queue);
    }

    /// <summary>
    ///     Solve linear system K*u = F using GPU-accelerated Preconditioned Conjugate Gradient.
    ///     Reference: [2] Goddeke et al. (2007) - GPU iterative solvers
    ///     Reference: Shewchuk (1994) - Conjugate Gradient method
    /// </summary>
    private bool SolveDisplacementsGPU(IProgress<float> progress, CancellationToken token)
    {
        var maxIter = _params.MaxIterations;
        var tolerance = _params.Tolerance;

        // Compute preconditioner M^(-1) = diag(K)^(-1) (Jacobi preconditioner)
        ComputePreconditioner();

        // Initialize: u = 0 (already done in ApplyBC)

        // r = F - K*u
        SpMV_GPU(_bufDisplacement, _bufResidual);
        VectorOp_GPU(2, -1.0f, _bufForce, _bufResidual); // r = F - Ku

        // z = M^(-1) * r
        VectorOp_GPU(3, 0, _bufResidual, _bufZ); // Apply preconditioner

        // p = z
        CopyVector_GPU(_bufZ, _bufP);

        // rho = r^T * z
        var rho = DotProduct_GPU(_bufResidual, _bufZ);
        var rho0 = rho;

        var converged = false;
        var iter = 0;

        while (iter < maxIter && !converged)
        {
            token.ThrowIfCancellationRequested();

            // q = K * p
            SpMV_GPU(_bufP, _bufQ);

            // alpha = rho / (p^T * q)
            var pq = DotProduct_GPU(_bufP, _bufQ);
            if (MathF.Abs(pq) < 1e-20f)
                break;

            var alpha = rho / pq;

            // u = u + alpha * p
            VectorOp_GPU(0, alpha, _bufP, _bufDisplacement);

            // r = r - alpha * q
            VectorOp_GPU(0, -alpha, _bufQ, _bufResidual);

            // Check convergence
            var rNorm = VectorNorm_GPU(_bufResidual);
            var relativeResidual = rNorm / MathF.Sqrt(rho0);

            if (relativeResidual < tolerance)
            {
                converged = true;
                break;
            }

            // z = M^(-1) * r
            VectorOp_GPU(3, 0, _bufResidual, _bufZ);

            // rho_new = r^T * z
            var rho_new = DotProduct_GPU(_bufResidual, _bufZ);

            // beta = rho_new / rho
            var beta = rho_new / rho;

            // p = z + beta * p
            VectorOp_GPU(1, beta, _bufZ, _bufP);

            rho = rho_new;
            iter++;

            if (iter % 10 == 0)
            {
                var prog = 0.35f + 0.4f * iter / maxIter;
                progress?.Report(prog);
            }
        }

        Logger.Log($"[GeomechGPU] CG solver: {iter} iterations, converged={converged}");
        return converged;
    }

    /// <summary>
    ///     Compute Jacobi preconditioner: M^(-1) = diag(K)^(-1)
    /// </summary>
    private void ComputePreconditioner()
    {
        // Download diagonal elements
        var rowPtr = new int[_numDOFs + 1];
        var colIdx = new int[_nnz];
        var values = new float[_nnz];

        fixed (int* pRowPtr = rowPtr)
        {
            var error = _cl.EnqueueReadBuffer(_queue, _bufRowPtr, true, 0,
                (nuint)(rowPtr.Length * sizeof(int)), pRowPtr, 0, null, null);
            CheckError(error, "Read rowPtr");
        }

        fixed (int* pColIdx = colIdx)
        {
            var error = _cl.EnqueueReadBuffer(_queue, _bufColIdx, true, 0,
                (nuint)(colIdx.Length * sizeof(int)), pColIdx, 0, null, null);
            CheckError(error, "Read colIdx");
        }

        fixed (float* pValues = values)
        {
            var error = _cl.EnqueueReadBuffer(_queue, _bufValues, true, 0,
                (nuint)(values.Length * sizeof(float)), pValues, 0, null, null);
            CheckError(error, "Read values");
        }

        var Minv = new float[_numDOFs];
        for (var row = 0; row < _numDOFs; row++)
        {
            var start = rowPtr[row];
            var end = rowPtr[row + 1];

            var diag = 1.0f;
            for (var j = start; j < end; j++)
                if (colIdx[j] == row)
                {
                    diag = values[j];
                    break;
                }

            Minv[row] = diag > 1e-12f ? 1.0f / diag : 1.0f;
        }

        // Upload
        fixed (float* pMinv = Minv)
        {
            var error = _cl.EnqueueWriteBuffer(_queue, _bufMInv, true, 0,
                (nuint)(Minv.Length * sizeof(float)), pMinv, 0, null, null);
            CheckError(error, "Upload Minv");
        }
    }

    /// <summary>
    ///     Sparse matrix-vector multiplication on GPU.
    ///     Reference: [3] Bell & Garland (2009), Algorithm 1
    /// </summary>
    private void SpMV_GPU(nint x, nint y)
    {
        var globalSize = stackalloc nuint[1];
        globalSize[0] = (nuint)((_numDOFs + _workGroupSize - 1) / _workGroupSize * _workGroupSize);

        // FIX: Local variables
        var numDOFs = _numDOFs;
        var bufRowPtr = _bufRowPtr;
        var bufColIdx = _bufColIdx;
        var bufValues = _bufValues;
        var bufIsDirichlet = _bufIsDirichlet;

        _cl.SetKernelArg(_kernelSpMV, 0, sizeof(int), &numDOFs);
        _cl.SetKernelArg(_kernelSpMV, 1, (nuint)IntPtr.Size, &bufRowPtr);
        _cl.SetKernelArg(_kernelSpMV, 2, (nuint)IntPtr.Size, &bufColIdx);
        _cl.SetKernelArg(_kernelSpMV, 3, (nuint)IntPtr.Size, &bufValues);
        _cl.SetKernelArg(_kernelSpMV, 4, (nuint)IntPtr.Size, &x);
        _cl.SetKernelArg(_kernelSpMV, 5, (nuint)IntPtr.Size, &bufIsDirichlet);
        _cl.SetKernelArg(_kernelSpMV, 6, (nuint)IntPtr.Size, &y);

        var error = _cl.EnqueueNdrangeKernel(_queue, _kernelSpMV, 1, null,
            globalSize, null, 0, null, null);
        CheckError(error, "EnqueueNDRange sparse_matrix_vector_multiply");
    }

    /// <summary>
    ///     Vector operations on GPU.
    ///     Reference: [2] Goddeke et al. (2007), Section 3.1
    /// </summary>
    private void VectorOp_GPU(int op, float alpha, nint x, nint y)
    {
        var globalSize = stackalloc nuint[1];
        globalSize[0] = (nuint)((_numDOFs + _workGroupSize - 1) / _workGroupSize * _workGroupSize);

        // FIX: Local variables
        var numDOFs = _numDOFs;
        var bufMInv = _bufMInv;
        var bufIsDirichlet = _bufIsDirichlet;

        _cl.SetKernelArg(_kernelVectorOps, 0, sizeof(int), &numDOFs);
        _cl.SetKernelArg(_kernelVectorOps, 1, sizeof(int), &op);
        _cl.SetKernelArg(_kernelVectorOps, 2, sizeof(float), &alpha);
        _cl.SetKernelArg(_kernelVectorOps, 3, (nuint)IntPtr.Size, &x);
        _cl.SetKernelArg(_kernelVectorOps, 4, (nuint)IntPtr.Size, &y);
        _cl.SetKernelArg(_kernelVectorOps, 5, (nuint)IntPtr.Size, &bufMInv);
        _cl.SetKernelArg(_kernelVectorOps, 6, (nuint)IntPtr.Size, &bufIsDirichlet);

        var error = _cl.EnqueueNdrangeKernel(_queue, _kernelVectorOps, 1, null,
            globalSize, null, 0, null, null);
        CheckError(error, "EnqueueNDRange vector_operations");
    }

    /// <summary>
    ///     Dot product using parallel reduction on GPU.
    ///     Reference: [2] Bell & Garland (2009), Section 3.2 - Parallel reductions
    /// </summary>
    private float DotProduct_GPU(nint a, nint b)
    {
        var numWorkGroups = (_numDOFs + _workGroupSize - 1) / _workGroupSize;

        var globalSize = stackalloc nuint[1];
        globalSize[0] = (nuint)(numWorkGroups * _workGroupSize);

        var localSize = stackalloc nuint[1];
        localSize[0] = (nuint)_workGroupSize;

        // FIX: Local variables
        var numDOFs = _numDOFs;
        var bufPartialSums = _bufReductionTemp;
        var localMemSize = (nuint)(_workGroupSize * sizeof(float));

        _cl.SetKernelArg(_kernelDotProduct, 0, sizeof(int), &numDOFs);
        _cl.SetKernelArg(_kernelDotProduct, 1, (nuint)IntPtr.Size, &a);
        _cl.SetKernelArg(_kernelDotProduct, 2, (nuint)IntPtr.Size, &b);
        _cl.SetKernelArg(_kernelDotProduct, 3, (nuint)IntPtr.Size, &bufPartialSums);
        _cl.SetKernelArg(_kernelDotProduct, 4, localMemSize, null);

        var error = _cl.EnqueueNdrangeKernel(_queue, _kernelDotProduct, 1, null,
            globalSize, localSize, 0, null, null);
        CheckError(error, "EnqueueNDRange dot_product_reduction");

        var partialSums = new float[numWorkGroups];
        fixed (float* pPartialSums = partialSums)
        {
            error = _cl.EnqueueReadBuffer(_queue, bufPartialSums, true, 0,
                (nuint)(numWorkGroups * sizeof(float)), pPartialSums, 0, null, null);
            CheckError(error, "Read partial sums");
        }

        float result = 0;
        for (var i = 0; i < numWorkGroups; i++)
            result += partialSums[i];

        return result;
    }

    private float VectorNorm_GPU(nint vec)
    {
        return MathF.Sqrt(DotProduct_GPU(vec, vec));
    }

    private void CopyVector_GPU(nint src, nint dst)
    {
        var error = _cl.EnqueueCopyBuffer(_queue, src, dst, 0, 0,
            (nuint)(_numDOFs * sizeof(float)), 0, null, null);
        CheckError(error, "Copy vector");
    }

    /// <summary>
    ///     Calculate strains and stresses from displacement field on GPU.
    /// </summary>
    private void CalculateStrainsAndStressesGPU()
    {
        var extent = _params.SimulationExtent;
        var w = extent.Width;
        var h = extent.Height;
        var d = extent.Depth;
        var voxelSize = _params.PixelSize / 1e6f;

        int error;

        // Allocate result buffers
        var size = (nuint)(w * h * d);
        _bufStrainXX = _cl.CreateBuffer(_context, MemFlags.WriteOnly, size * sizeof(float), null, &error);
        _bufStrainYY = _cl.CreateBuffer(_context, MemFlags.WriteOnly, size * sizeof(float), null, &error);
        _bufStrainZZ = _cl.CreateBuffer(_context, MemFlags.WriteOnly, size * sizeof(float), null, &error);
        _bufStrainXY = _cl.CreateBuffer(_context, MemFlags.WriteOnly, size * sizeof(float), null, &error);
        _bufStrainXZ = _cl.CreateBuffer(_context, MemFlags.WriteOnly, size * sizeof(float), null, &error);
        _bufStrainYZ = _cl.CreateBuffer(_context, MemFlags.WriteOnly, size * sizeof(float), null, &error);

        _bufStressXX = _cl.CreateBuffer(_context, MemFlags.WriteOnly, size * sizeof(float), null, &error);
        _bufStressYY = _cl.CreateBuffer(_context, MemFlags.WriteOnly, size * sizeof(float), null, &error);
        _bufStressZZ = _cl.CreateBuffer(_context, MemFlags.WriteOnly, size * sizeof(float), null, &error);
        _bufStressXY = _cl.CreateBuffer(_context, MemFlags.WriteOnly, size * sizeof(float), null, &error);
        _bufStressXZ = _cl.CreateBuffer(_context, MemFlags.WriteOnly, size * sizeof(float), null, &error);
        _bufStressYZ = _cl.CreateBuffer(_context, MemFlags.WriteOnly, size * sizeof(float), null, &error);

        // Launch kernel
        var globalSize = stackalloc nuint[1];
        globalSize[0] = (nuint)_numElements;

        var bufNodeX = _bufNodeX;
        var bufNodeY = _bufNodeY;
        var bufNodeZ = _bufNodeZ;
        var bufElementNodes = _bufElementNodes;
        var bufDisplacement = _bufDisplacement;
        var bufElementE = _bufElementE;
        var bufElementNu = _bufElementNu;
        var bufLabels = _bufLabels;
        var bufStrainXX = _bufStrainXX;
        var bufStrainYY = _bufStrainYY;
        var bufStrainZZ = _bufStrainZZ;
        var bufStrainXY = _bufStrainXY;
        var bufStrainXZ = _bufStrainXZ;
        var bufStrainYZ = _bufStrainYZ;
        var bufStressXX = _bufStressXX;
        var bufStressYY = _bufStressYY;
        var bufStressZZ = _bufStressZZ;
        var bufStressXY = _bufStressXY;
        var bufStressXZ = _bufStressXZ;
        var bufStressYZ = _bufStressYZ;
        var numElements = _numElements;
        _cl.SetKernelArg(_kernelCalculateStrains, 0, sizeof(int), _numElements);
        _cl.SetKernelArg(_kernelCalculateStrains, 1, (nuint)IntPtr.Size, &bufNodeX);
        _cl.SetKernelArg(_kernelCalculateStrains, 2, (nuint)IntPtr.Size, &bufNodeY);
        _cl.SetKernelArg(_kernelCalculateStrains, 3, (nuint)IntPtr.Size, &bufNodeZ);
        _cl.SetKernelArg(_kernelCalculateStrains, 4, (nuint)IntPtr.Size, &bufElementNodes);
        _cl.SetKernelArg(_kernelCalculateStrains, 5, (nuint)IntPtr.Size, &bufDisplacement);
        _cl.SetKernelArg(_kernelCalculateStrains, 6, (nuint)IntPtr.Size, &bufElementE);
        _cl.SetKernelArg(_kernelCalculateStrains, 7, (nuint)IntPtr.Size, &bufElementNu);
        _cl.SetKernelArg(_kernelCalculateStrains, 8, (nuint)IntPtr.Size, &bufLabels);
        _cl.SetKernelArg(_kernelCalculateStrains, 9, sizeof(int), &w);
        _cl.SetKernelArg(_kernelCalculateStrains, 10, sizeof(int), &h);
        _cl.SetKernelArg(_kernelCalculateStrains, 11, sizeof(int), &d);
        _cl.SetKernelArg(_kernelCalculateStrains, 12, sizeof(float), &voxelSize);
        _cl.SetKernelArg(_kernelCalculateStrains, 13, (nuint)IntPtr.Size, &bufStrainXX);
        _cl.SetKernelArg(_kernelCalculateStrains, 14, (nuint)IntPtr.Size, &bufStrainYY);
        _cl.SetKernelArg(_kernelCalculateStrains, 15, (nuint)IntPtr.Size, &bufStrainZZ);
        _cl.SetKernelArg(_kernelCalculateStrains, 16, (nuint)IntPtr.Size, &bufStrainXY);
        _cl.SetKernelArg(_kernelCalculateStrains, 17, (nuint)IntPtr.Size, &bufStrainXZ);
        _cl.SetKernelArg(_kernelCalculateStrains, 18, (nuint)IntPtr.Size, &bufStrainYZ);
        _cl.SetKernelArg(_kernelCalculateStrains, 19, (nuint)IntPtr.Size, &bufStressXX);
        _cl.SetKernelArg(_kernelCalculateStrains, 20, (nuint)IntPtr.Size, &bufStressYY);
        _cl.SetKernelArg(_kernelCalculateStrains, 21, (nuint)IntPtr.Size, &bufStressZZ);
        _cl.SetKernelArg(_kernelCalculateStrains, 22, (nuint)IntPtr.Size, &bufStressXY);
        _cl.SetKernelArg(_kernelCalculateStrains, 23, (nuint)IntPtr.Size, &bufStressXZ);
        _cl.SetKernelArg(_kernelCalculateStrains, 24, (nuint)IntPtr.Size, &bufStressYZ);

        error = _cl.EnqueueNdrangeKernel(_queue, _kernelCalculateStrains, 1, null,
            globalSize, null, 0, null, null);
        CheckError(error, "EnqueueNDRange calculate_strains_stresses");

        _cl.Finish(_queue);
    }

    /// <summary>
    ///     Calculate principal stresses on GPU.
    /// </summary>
    private void CalculatePrincipalStressesGPU()
    {
        var extent = _params.SimulationExtent;
        var w = extent.Width;
        var h = extent.Height;
        var d = extent.Depth;

        int error;
        var size = (nuint)(w * h * d);

        _bufSigma1 = _cl.CreateBuffer(_context, MemFlags.WriteOnly, size * sizeof(float), null, &error);
        _bufSigma2 = _cl.CreateBuffer(_context, MemFlags.WriteOnly, size * sizeof(float), null, &error);
        _bufSigma3 = _cl.CreateBuffer(_context, MemFlags.WriteOnly, size * sizeof(float), null, &error);

        var globalSize = stackalloc nuint[3];
        globalSize[0] = (nuint)w;
        globalSize[1] = (nuint)h;
        globalSize[2] = (nuint)d;

        var bufLabels = _bufLabels;
        var bufStressXX = _bufStressXX;
        var bufStressYY = _bufStressYY;
        var bufStressZZ = _bufStressZZ;
        var bufStressXY = _bufStressXY;
        var bufStressXZ = _bufStressXZ;
        var bufStressYZ = _bufStressYZ;
        var bufSigma1 = _bufSigma1;
        var bufSigma2 = _bufSigma2;
        var bufSigma3 = _bufSigma3;

        _cl.SetKernelArg(_kernelCalculatePrincipal, 0, (nuint)IntPtr.Size, &bufLabels);
        _cl.SetKernelArg(_kernelCalculatePrincipal, 1, (nuint)IntPtr.Size, &bufStressXX);
        _cl.SetKernelArg(_kernelCalculatePrincipal, 2, (nuint)IntPtr.Size, &bufStressYY);
        _cl.SetKernelArg(_kernelCalculatePrincipal, 3, (nuint)IntPtr.Size, &bufStressZZ);
        _cl.SetKernelArg(_kernelCalculatePrincipal, 4, (nuint)IntPtr.Size, &bufStressXY);
        _cl.SetKernelArg(_kernelCalculatePrincipal, 5, (nuint)IntPtr.Size, &bufStressXZ);
        _cl.SetKernelArg(_kernelCalculatePrincipal, 6, (nuint)IntPtr.Size, &bufStressYZ);
        _cl.SetKernelArg(_kernelCalculatePrincipal, 7, (nuint)IntPtr.Size, &bufSigma1);
        _cl.SetKernelArg(_kernelCalculatePrincipal, 8, (nuint)IntPtr.Size, &bufSigma2);
        _cl.SetKernelArg(_kernelCalculatePrincipal, 9, (nuint)IntPtr.Size, &bufSigma3);
        _cl.SetKernelArg(_kernelCalculatePrincipal, 10, sizeof(int), &w);
        _cl.SetKernelArg(_kernelCalculatePrincipal, 11, sizeof(int), &h);
        _cl.SetKernelArg(_kernelCalculatePrincipal, 12, sizeof(int), &d);

        error = _cl.EnqueueNdrangeKernel(_queue, _kernelCalculatePrincipal, 3, null,
            globalSize, null, 0, null, null);
        CheckError(error, "EnqueueNDRange calculate_principal_stresses");

        _cl.Finish(_queue);
    }

    /// <summary>
    ///     Evaluate failure criteria on GPU.
    /// </summary>
    private void EvaluateFailureGPU()
    {
        var extent = _params.SimulationExtent;
        var w = extent.Width;
        var h = extent.Height;
        var d = extent.Depth;

        int error;
        var size = (nuint)(w * h * d);

        _bufFailureIndex = _cl.CreateBuffer(_context, MemFlags.WriteOnly, size * sizeof(float), null, &error);
        _bufDamage = _cl.CreateBuffer(_context, MemFlags.WriteOnly, size, null, &error);

        var globalSize = stackalloc nuint[3];
        globalSize[0] = (nuint)w;
        globalSize[1] = (nuint)h;
        globalSize[2] = (nuint)d;

        var criterion = (int)_params.FailureCriterion;
        var cohesion_Pa = _params.Cohesion * 1e6f;
        var phi_rad = _params.FrictionAngle * MathF.PI / 180f;
        var tensile_Pa = _params.TensileStrength * 1e6f;
        var porePressure_Pa = _params.UsePorePressure ? _params.PorePressure * 1e6f : 0f;
        var biot = _params.BiotCoefficient;
        var damageThreshold = _params.DamageThreshold;
        var hb_mb = _params.HoekBrown_mb;
        var hb_s = _params.HoekBrown_s;
        var hb_a = _params.HoekBrown_a;

        var bufLabels = _bufLabels;
        var bufSigma1 = _bufSigma1;
        var bufSigma3 = _bufSigma3;
        var bufFailureIndex = _bufFailureIndex;
        var bufDamage = _bufDamage;

        _cl.SetKernelArg(_kernelEvaluateFailure, 0, (nuint)IntPtr.Size, &bufLabels);
        _cl.SetKernelArg(_kernelEvaluateFailure, 1, (nuint)IntPtr.Size, &bufSigma1);
        _cl.SetKernelArg(_kernelEvaluateFailure, 2, (nuint)IntPtr.Size, &bufSigma3);
        _cl.SetKernelArg(_kernelEvaluateFailure, 3, (nuint)IntPtr.Size, &bufFailureIndex);
        _cl.SetKernelArg(_kernelEvaluateFailure, 4, (nuint)IntPtr.Size, &bufDamage);
        _cl.SetKernelArg(_kernelEvaluateFailure, 5, sizeof(int), &w);
        _cl.SetKernelArg(_kernelEvaluateFailure, 6, sizeof(int), &h);
        _cl.SetKernelArg(_kernelEvaluateFailure, 7, sizeof(int), &d);
        _cl.SetKernelArg(_kernelEvaluateFailure, 8, sizeof(int), &criterion);
        _cl.SetKernelArg(_kernelEvaluateFailure, 9, sizeof(float), &cohesion_Pa);
        _cl.SetKernelArg(_kernelEvaluateFailure, 10, sizeof(float), &phi_rad);
        _cl.SetKernelArg(_kernelEvaluateFailure, 11, sizeof(float), &tensile_Pa);
        _cl.SetKernelArg(_kernelEvaluateFailure, 12, sizeof(float), &porePressure_Pa);
        _cl.SetKernelArg(_kernelEvaluateFailure, 13, sizeof(float), &biot);
        _cl.SetKernelArg(_kernelEvaluateFailure, 14, sizeof(float), &damageThreshold);
        _cl.SetKernelArg(_kernelEvaluateFailure, 15, sizeof(float), &hb_mb);
        _cl.SetKernelArg(_kernelEvaluateFailure, 16, sizeof(float), &hb_s);
        _cl.SetKernelArg(_kernelEvaluateFailure, 17, sizeof(float), &hb_a);

        error = _cl.EnqueueNdrangeKernel(_queue, _kernelEvaluateFailure, 3, null,
            globalSize, null, 0, null, null);
        CheckError(error, "EnqueueNDRange evaluate_failure");

        _cl.Finish(_queue);
    }

    /// <summary>
    ///     Download results from GPU and construct results object.
    /// </summary>
    private GeomechanicalResults DownloadResults(BoundingBox extent, byte[,,] labels)
    {
        var w = extent.Width;
        var h = extent.Height;
        var d = extent.Depth;
        var size = w * h * d;

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

        // Download flat arrays
        var flatSize = (nuint)(size * sizeof(float));

        var stressXXFlat = new float[size];
        var stressYYFlat = new float[size];
        var stressZZFlat = new float[size];
        var stressXYFlat = new float[size];
        var stressXZFlat = new float[size];
        var stressYZFlat = new float[size];

        var strainXXFlat = new float[size];
        var strainYYFlat = new float[size];
        var strainZZFlat = new float[size];
        var strainXYFlat = new float[size];
        var strainXZFlat = new float[size];
        var strainYZFlat = new float[size];

        var sigma1Flat = new float[size];
        var sigma2Flat = new float[size];
        var sigma3Flat = new float[size];
        var failureFlat = new float[size];
        var damageFlat = new byte[size];

        fixed (float* p = stressXXFlat)
        {
            _cl.EnqueueReadBuffer(_queue, _bufStressXX, true, 0, flatSize, p, 0, null, null);
        }

        fixed (float* p = stressYYFlat)
        {
            _cl.EnqueueReadBuffer(_queue, _bufStressYY, true, 0, flatSize, p, 0, null, null);
        }

        fixed (float* p = stressZZFlat)
        {
            _cl.EnqueueReadBuffer(_queue, _bufStressZZ, true, 0, flatSize, p, 0, null, null);
        }

        fixed (float* p = stressXYFlat)
        {
            _cl.EnqueueReadBuffer(_queue, _bufStressXY, true, 0, flatSize, p, 0, null, null);
        }

        fixed (float* p = stressXZFlat)
        {
            _cl.EnqueueReadBuffer(_queue, _bufStressXZ, true, 0, flatSize, p, 0, null, null);
        }

        fixed (float* p = stressYZFlat)
        {
            _cl.EnqueueReadBuffer(_queue, _bufStressYZ, true, 0, flatSize, p, 0, null, null);
        }

        fixed (float* p = strainXXFlat)
        {
            _cl.EnqueueReadBuffer(_queue, _bufStrainXX, true, 0, flatSize, p, 0, null, null);
        }

        fixed (float* p = strainYYFlat)
        {
            _cl.EnqueueReadBuffer(_queue, _bufStrainYY, true, 0, flatSize, p, 0, null, null);
        }

        fixed (float* p = strainZZFlat)
        {
            _cl.EnqueueReadBuffer(_queue, _bufStrainZZ, true, 0, flatSize, p, 0, null, null);
        }

        fixed (float* p = strainXYFlat)
        {
            _cl.EnqueueReadBuffer(_queue, _bufStrainXY, true, 0, flatSize, p, 0, null, null);
        }

        fixed (float* p = strainXZFlat)
        {
            _cl.EnqueueReadBuffer(_queue, _bufStrainXZ, true, 0, flatSize, p, 0, null, null);
        }

        fixed (float* p = strainYZFlat)
        {
            _cl.EnqueueReadBuffer(_queue, _bufStrainYZ, true, 0, flatSize, p, 0, null, null);
        }

        fixed (float* p = sigma1Flat)
        {
            _cl.EnqueueReadBuffer(_queue, _bufSigma1, true, 0, flatSize, p, 0, null, null);
        }

        fixed (float* p = sigma2Flat)
        {
            _cl.EnqueueReadBuffer(_queue, _bufSigma2, true, 0, flatSize, p, 0, null, null);
        }

        fixed (float* p = sigma3Flat)
        {
            _cl.EnqueueReadBuffer(_queue, _bufSigma3, true, 0, flatSize, p, 0, null, null);
        }

        fixed (float* p = failureFlat)
        {
            _cl.EnqueueReadBuffer(_queue, _bufFailureIndex, true, 0, flatSize, p, 0, null, null);
        }

        fixed (byte* p = damageFlat)
        {
            _cl.EnqueueReadBuffer(_queue, _bufDamage, true, 0, (nuint)size, p, 0, null, null);
        }

        // Unflatten to 3D arrays
        Parallel.For(0, d, z =>
        {
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var idx = (z * h + y) * w + x;

                results.StressXX[x, y, z] = stressXXFlat[idx];
                results.StressYY[x, y, z] = stressYYFlat[idx];
                results.StressZZ[x, y, z] = stressZZFlat[idx];
                results.StressXY[x, y, z] = stressXYFlat[idx];
                results.StressXZ[x, y, z] = stressXZFlat[idx];
                results.StressYZ[x, y, z] = stressYZFlat[idx];

                results.StrainXX[x, y, z] = strainXXFlat[idx];
                results.StrainYY[x, y, z] = strainYYFlat[idx];
                results.StrainZZ[x, y, z] = strainZZFlat[idx];
                results.StrainXY[x, y, z] = strainXYFlat[idx];
                results.StrainXZ[x, y, z] = strainXZFlat[idx];
                results.StrainYZ[x, y, z] = strainYZFlat[idx];

                results.Sigma1[x, y, z] = sigma1Flat[idx];
                results.Sigma2[x, y, z] = sigma2Flat[idx];
                results.Sigma3[x, y, z] = sigma3Flat[idx];
                results.FailureIndex[x, y, z] = failureFlat[idx];
                results.DamageField[x, y, z] = damageFlat[idx];
                results.FractureField[x, y, z] = failureFlat[idx] >= 1.0f;
            }
        });

        // Calculate statistics
        CalculateStatistics(results);

        // Generate Mohr circles (reuse CPU implementation)
        GenerateMohrCircles(results);

        return results;
    }

    private void CalculateStatistics(GeomechanicalResults results)
    {
        var w = results.Sigma1.GetLength(0);
        var h = results.Sigma1.GetLength(1);
        var d = results.Sigma1.GetLength(2);

        double sumMeanStress = 0;
        double sumVonMises = 0;
        double maxVonMises = 0;
        double maxShear = 0;
        double sumVolStrain = 0;

        var count = 0;
        var failed = 0;

        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            if (results.MaterialLabels[x, y, z] == 0) continue;

            var s1 = results.Sigma1[x, y, z];
            var s2 = results.Sigma2[x, y, z];
            var s3 = results.Sigma3[x, y, z];

            sumMeanStress += (s1 + s2 + s3) / 3.0;

            var vonMises = MathF.Sqrt(0.5f * ((s1 - s2) * (s1 - s2) + (s2 - s3) * (s2 - s3) + (s3 - s1) * (s3 - s1)));
            sumVonMises += vonMises;
            if (vonMises > maxVonMises) maxVonMises = vonMises;

            var currentMaxShear = (s1 - s3) / 2.0f;
            if (currentMaxShear > maxShear) maxShear = currentMaxShear;

            sumVolStrain += results.StrainXX[x, y, z] + results.StrainYY[x, y, z] + results.StrainZZ[x, y, z];

            count++;

            if (results.FractureField[x, y, z])
                failed++;
        }

        if (count > 0)
        {
            results.MeanStress = (float)(sumMeanStress / count);
            results.VonMisesStress_Mean = (float)(sumVonMises / count);
            results.VonMisesStress_Max = (float)maxVonMises;
            results.MaxShearStress = (float)maxShear;
            results.VolumetricStrain = (float)(sumVolStrain / count);
            results.TotalVoxels = count;
            results.FailedVoxels = failed;
            results.FailedVoxelPercentage = 100f * failed / count;
        }
    }

    private void GenerateMohrCircles(GeomechanicalResults results)
    {
        var cpu = new GeomechanicalSimulatorCPU(_params);
        var w = results.Sigma1.GetLength(0);
        var h = results.Sigma1.GetLength(1);
        var d = results.Sigma1.GetLength(2);

        var locations = new List<(string, int, int, int)>
        {
            ("Center", w / 2, h / 2, d / 2),
            ("Top", w / 2, h / 2, d - 1),
            ("Bottom", w / 2, h / 2, 0)
        };

        var maxStressValue = float.MinValue;
        int maxX = 0, maxY = 0, maxZ = 0;
        var found = false;

        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            if (results.MaterialLabels[x, y, z] == 0) continue;
            if (results.Sigma1[x, y, z] > maxStressValue)
            {
                maxStressValue = results.Sigma1[x, y, z];
                maxX = x;
                maxY = y;
                maxZ = z;
                found = true;
            }
        }

        if (found) locations.Add(("Max Stress", maxX, maxY, maxZ));

        foreach (var (name, x, y, z) in locations)
        {
            if (x < 0 || x >= w || y < 0 || y >= h || z < 0 || z >= d ||
                results.MaterialLabels[x, y, z] == 0)
                continue;

            var sigma1 = results.Sigma1[x, y, z];
            var sigma2 = results.Sigma2[x, y, z];
            var sigma3 = results.Sigma3[x, y, z];

            var circle = new MohrCircleData
            {
                Location = name,
                Position = new Vector3(x, y, z),
                Sigma1 = sigma1 / 1e6f,
                Sigma2 = sigma2 / 1e6f,
                Sigma3 = sigma3 / 1e6f,
                MaxShearStress = (sigma1 - sigma3) / 2e6f,
                HasFailed = results.FractureField[x, y, z]
            };

            var phi = _params.FrictionAngle * MathF.PI / 180f;
            var failureAngleRad = MathF.PI / 4 + phi / 2;
            circle.FailureAngle = failureAngleRad * 180f / MathF.PI;

            if (circle.HasFailed)
            {
                var two_theta = 2 * failureAngleRad;
                circle.NormalStressAtFailure =
                    ((sigma1 + sigma3) / 2 + (sigma1 - sigma3) / 2 * MathF.Cos(two_theta)) / 1e6f;
                circle.ShearStressAtFailure = (sigma1 - sigma3) / 2 * MathF.Sin(two_theta) / 1e6f;
            }

            results.MohrCircles.Add(circle);
        }
    }

    private class MeshData
    {
        public float[] elementE, elementNu;
        public int[] elementNodes;
        public byte[] labels;
        public float[] nodeX, nodeY, nodeZ;
    }
}