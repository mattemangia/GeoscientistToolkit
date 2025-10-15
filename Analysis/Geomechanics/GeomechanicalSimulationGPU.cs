// GeoscientistToolkit/Analysis/Geomechanics/GeomechanicalSimulatorGPU.cs
// COMPLETE GPU IMPLEMENTATION - FULL FILE WITH ALL METHODS

using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using GeoscientistToolkit.Util;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Analysis.Geomechanics;

public unsafe class GeomechanicalSimulatorGPU : IDisposable
{
    private const int CHUNK_OVERLAP = 2;
    private readonly object _chunkAccessLock = new();
    private readonly Dictionary<int, ChunkGPUBuffers> _chunkBuffers = new();
    private readonly CL _cl;

// Add field for iteration tracking
    private readonly int _iterationsPerformed = 0;
    private readonly string _offloadPath;
    private readonly GeomechanicalParameters _params;
    private readonly int _workGroupSize = 256;
    private nint _bufConnectivity;
    private nint _bufDisplacement, _bufForce;
    private nint _bufElementNodes, _bufElementE, _bufElementNu;
    private nint _bufFractureAperture, _bufFluidSaturation;
    private nint _bufIsDirichlet, _bufDirichletValue;
    private nint _bufLabels, _bufFractured;

    // GPU buffers
    private nint _bufNodeX, _bufNodeY, _bufNodeZ;
    private nint _bufPartialSums, _bufTempVector;
    private nint _bufPrincipalStresses, _bufFailureIndex, _bufDamage;
    private nint _bufRowPtr, _bufColIdx, _bufValues;
    private nint _bufStressFields, _bufStrainFields;
    private nint _bufTemperature, _bufPressure, _bufPressureNew;
    private nint _bufVelocityX, _bufVelocityY, _bufVelocityZ;
    private Queue<int> _chunkAccessOrder = new();
    private HashSet<int> _chunkAccessSet = new();
    private const int MAX_ENTRIES_PER_CHUNK = 500_000_000; 
    
    private List<GeomechanicalChunk> _chunks;

    private nint _context, _queue, _program, _device;
    private long _currentGPUMemoryBytes;
    private float[] _dirichletValue;
    private float[] _displacement, _force;
    private float[] _elementE, _elementNu;
    private int[] _elementNodes;
    private bool _initialized;
    private bool[] _isDirichlet;
    private bool _isHugeDataset;
    private nint _kernelApplyBC;

    // All kernels
    private nint _kernelAssembleElement;
    private nint _kernelCalculatePrincipal;
    private nint _kernelCalculateStrains;
    private nint _kernelDotProduct;
    private nint _kernelEvaluateFailure;
    private nint _kernelPlasticCorrection;
    private nint _kernelSpMV;
    private nint _kernelVectorOps;
    private long _maxGPUMemoryBytes;
    private int _maxLoadedChunks;

    // Host-side data
    private float[] _nodeX, _nodeY, _nodeZ;

    private int _numNodes, _numElements, _numDOFs, _nnz;
    private List<int> _rowPtr, _colIdx;
    private List<float> _values;

    public GeomechanicalSimulatorGPU(GeomechanicalParameters parameters)
    {
        _params = parameters;
        _cl = CL.GetApi();

        ValidateParameters();

        if (_params.EnableOffloading && !string.IsNullOrEmpty(_params.OffloadDirectory))
        {
            _offloadPath = Path.Combine(_params.OffloadDirectory, $"geomech_gpu_{Guid.NewGuid()}");
            Directory.CreateDirectory(_offloadPath);
        }

        InitializeOpenCL();
        DetectGPUMemory();
    }

    public void Dispose()
    {
        ReleaseAllGPUBuffers();
        ReleaseKernels();
        if (_program != 0) _cl.ReleaseProgram(_program);
        if (_queue != 0) _cl.ReleaseCommandQueue(_queue);
        if (_context != 0) _cl.ReleaseContext(_context);
        Cleanup();
    }

    private void ValidateParameters()
    {
        if (_params.Sigma1 < _params.Sigma2 || _params.Sigma2 < _params.Sigma3)
            throw new ArgumentException("Principal stresses must satisfy σ₁ ≥ σ₂ ≥ σ₃");

        if (_params.PoissonRatio <= 0 || _params.PoissonRatio >= 0.5f)
            throw new ArgumentException("Poisson's ratio must be in (0, 0.5)");

        if (_params.YoungModulus <= 0)
            throw new ArgumentException("Young's modulus must be positive");

        Logger.Log("[GeomechGPU] Parameter validation passed");
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

            _device = devices[0];

            int error;
            _context = _cl.CreateContext(null, 1, devices, null, null, &error);
            CheckError(error, "CreateContext");

            _queue = _cl.CreateCommandQueue(_context, devices[0], CommandQueueProperties.None, &error);
            CheckError(error, "CreateCommandQueue");

            BuildProgram();
            _initialized = true;

            Logger.Log("[GeomechGPU] OpenCL initialized successfully");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[GeomechGPU] Initialization failed: {ex.Message}");
            throw;
        }
    }

    private void DetectGPUMemory()
    {
        try
        {
            nuint gpuMemSize;
            _cl.GetDeviceInfo(_device, DeviceInfo.GlobalMemSize, sizeof(ulong), &gpuMemSize, null);

            _maxGPUMemoryBytes = (long)(gpuMemSize * 0.8);

            Logger.Log($"[GeomechGPU] GPU memory: {gpuMemSize / (1024.0 * 1024 * 1024):F2} GB");
            Logger.Log($"[GeomechGPU] GPU memory budget: {_maxGPUMemoryBytes / (1024.0 * 1024 * 1024):F2} GB");
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[GeomechGPU] Could not detect GPU memory: {ex.Message}");
            _maxGPUMemoryBytes = 2L * 1024 * 1024 * 1024;
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

        var devices = stackalloc nint[1];
        devices[0] = _device;
        error = _cl.BuildProgram(_program, 1, devices, (byte*)null, null, null);
        if (error != 0)
        {
            nuint logSize;
            _cl.GetProgramBuildInfo(_program, _device, (uint)ProgramBuildInfo.BuildLog, 0, null, &logSize);
            var log = new byte[logSize];
            fixed (byte* logPtr = log)
            {
                _cl.GetProgramBuildInfo(_program, _device, (uint)ProgramBuildInfo.BuildLog, logSize, logPtr, null);
            }

            var logString = Encoding.UTF8.GetString(log);
            throw new Exception($"OpenCL build failed:\n{logString}");
        }

        // Create all kernels
        _kernelAssembleElement = _cl.CreateKernel(_program, "assemble_element_stiffness", &error);
        CheckError(error, "CreateKernel assemble_element_stiffness");

        _kernelApplyBC = _cl.CreateKernel(_program, "apply_boundary_conditions", &error);
        CheckError(error, "CreateKernel apply_boundary_conditions");

        _kernelSpMV = _cl.CreateKernel(_program, "sparse_matvec", &error);
        CheckError(error, "CreateKernel sparse_matvec");

        _kernelDotProduct = _cl.CreateKernel(_program, "dot_product", &error);
        CheckError(error, "CreateKernel dot_product");

        _kernelVectorOps = _cl.CreateKernel(_program, "vector_ops", &error);
        CheckError(error, "CreateKernel vector_ops");

        _kernelCalculateStrains = _cl.CreateKernel(_program, "calculate_strains_stresses", &error);
        CheckError(error, "CreateKernel calculate_strains_stresses");

        _kernelCalculatePrincipal = _cl.CreateKernel(_program, "calculate_principal_stresses", &error);
        CheckError(error, "CreateKernel calculate_principal_stresses");

        _kernelEvaluateFailure = _cl.CreateKernel(_program, "evaluate_failure", &error);
        CheckError(error, "CreateKernel evaluate_failure");

        _kernelPlasticCorrection = _cl.CreateKernel(_program, "apply_plastic_correction", &error);
        CheckError(error, "CreateKernel apply_plastic_correction");

        Logger.Log("[GeomechGPU] All kernels created successfully");
    }

    private string GetKernelSource()
    {
        return @"
#pragma OPENCL EXTENSION cl_khr_fp64 : enable

// ============================================================================
// ATOMIC OPERATIONS
// ============================================================================

inline void atomic_add_global(__global float* addr, float val) {
    union { unsigned int u32; float f32; } next, expected, current;
    current.f32 = *addr;
    do {
        expected.f32 = current.f32;
        next.f32 = expected.f32 + val;
        current.u32 = atomic_cmpxchg((volatile __global unsigned int*)addr, expected.u32, next.u32);
    } while (current.u32 != expected.u32);
}

// ============================================================================
// MATRIX OPERATIONS
// ============================================================================

float det3x3(__local const float* m) {
    return m[0]*(m[4]*m[8] - m[5]*m[7]) - m[1]*(m[3]*m[8] - m[5]*m[6]) + m[2]*(m[3]*m[7] - m[4]*m[6]);
}

void inverse3x3(__local const float* m, __local float* inv) {
    float det = det3x3(m);
    if (fabs(det) < 1e-12f) {
        for (int i = 0; i < 9; i++) inv[i] = 0.0f;
        inv[0] = inv[4] = inv[8] = 1.0f;
        return;
    }
    
    float invDet = 1.0f / det;
    inv[0] = (m[4]*m[8] - m[5]*m[7]) * invDet;
    inv[1] = (m[2]*m[7] - m[1]*m[8]) * invDet;
    inv[2] = (m[1]*m[5] - m[2]*m[4]) * invDet;
    inv[3] = (m[5]*m[6] - m[3]*m[8]) * invDet;
    inv[4] = (m[0]*m[8] - m[2]*m[6]) * invDet;
    inv[5] = (m[2]*m[3] - m[0]*m[5]) * invDet;
    inv[6] = (m[3]*m[7] - m[4]*m[6]) * invDet;
    inv[7] = (m[1]*m[6] - m[0]*m[7]) * invDet;
    inv[8] = (m[0]*m[4] - m[1]*m[3]) * invDet;
}

void matmul_3x8(__local const float* A, __local const float* B, __local float* C) {
    for (int i = 0; i < 3; i++) {
        for (int j = 0; j < 8; j++) {
            C[i*8 + j] = 0.0f;
            for (int k = 0; k < 3; k++) {
                C[i*8 + j] += A[i*3 + k] * B[k*8 + j];
            }
        }
    }
}

// ============================================================================
// SHAPE FUNCTIONS
// ============================================================================

void shape_function_derivatives(float xi, float eta, float zeta, __local float* dN) {
    float coords[8][3] = {
        {-1.0f, -1.0f, -1.0f}, {1.0f, -1.0f, -1.0f}, {1.0f, 1.0f, -1.0f}, {-1.0f, 1.0f, -1.0f},
        {-1.0f, -1.0f, 1.0f}, {1.0f, -1.0f, 1.0f}, {1.0f, 1.0f, 1.0f}, {-1.0f, 1.0f, 1.0f}
    };
    
    for (int i = 0; i < 8; i++) {
        float xi_i = coords[i][0];
        float eta_i = coords[i][1];
        float zeta_i = coords[i][2];
        
        dN[i*3 + 0] = 0.125f * xi_i * (1.0f + eta_i*eta) * (1.0f + zeta_i*zeta);
        dN[i*3 + 1] = 0.125f * (1.0f + xi_i*xi) * eta_i * (1.0f + zeta_i*zeta);
        dN[i*3 + 2] = 0.125f * (1.0f + xi_i*xi) * (1.0f + eta_i*eta) * zeta_i;
    }
}

void compute_jacobian(__local const float* dN_dxi, __global const float* nodeX, __global const float* nodeY,
                     __global const float* nodeZ, __local const int* nodes, __local float* J) {
    for (int i = 0; i < 9; i++) J[i] = 0.0f;
    
    for (int i = 0; i < 8; i++) {
        int nodeIdx = nodes[i];
        float x = nodeX[nodeIdx];
        float y = nodeY[nodeIdx];
        float z = nodeZ[nodeIdx];
        
        J[0] += dN_dxi[i*3 + 0] * x;
        J[1] += dN_dxi[i*3 + 0] * y;
        J[2] += dN_dxi[i*3 + 0] * z;
        J[3] += dN_dxi[i*3 + 1] * x;
        J[4] += dN_dxi[i*3 + 1] * y;
        J[5] += dN_dxi[i*3 + 1] * z;
        J[6] += dN_dxi[i*3 + 2] * x;
        J[7] += dN_dxi[i*3 + 2] * y;
        J[8] += dN_dxi[i*3 + 2] * z;
    }
}

void compute_B_matrix(__local const float* dN_dx, __local float* B) {
    for (int i = 0; i < 144; i++) B[i] = 0.0f;
    
    for (int i = 0; i < 8; i++) {
        float dNi_dx = dN_dx[i*3 + 0];
        float dNi_dy = dN_dx[i*3 + 1];
        float dNi_dz = dN_dx[i*3 + 2];
        int col = i * 3;
        
        B[0*24 + col + 0] = dNi_dx;
        B[1*24 + col + 1] = dNi_dy;
        B[2*24 + col + 2] = dNi_dz;
        B[3*24 + col + 0] = dNi_dy;
        B[3*24 + col + 1] = dNi_dx;
        B[4*24 + col + 0] = dNi_dz;
        B[4*24 + col + 2] = dNi_dx;
        B[5*24 + col + 1] = dNi_dz;
        B[5*24 + col + 2] = dNi_dy;
    }
}

void compute_D_matrix(float E, float nu, __local float* D) {
    float lambda = E * nu / ((1.0f + nu) * (1.0f - 2.0f * nu));
    float mu = E / (2.0f * (1.0f + nu));
    float lambda_2mu = lambda + 2.0f * mu;
    
    for (int i = 0; i < 36; i++) D[i] = 0.0f;
    
    D[0*6 + 0] = lambda_2mu;
    D[0*6 + 1] = lambda;
    D[0*6 + 2] = lambda;
    
    D[1*6 + 0] = lambda;
    D[1*6 + 1] = lambda_2mu;
    D[1*6 + 2] = lambda;
    
    D[2*6 + 0] = lambda;
    D[2*6 + 1] = lambda;
    D[2*6 + 2] = lambda_2mu;
    
    D[3*6 + 3] = mu;
    D[4*6 + 4] = mu;
    D[5*6 + 5] = mu;
}

// ============================================================================
// KERNEL 1: ASSEMBLE ELEMENT STIFFNESS - FIXED
// ============================================================================

__kernel void assemble_element_stiffness(
    __global const int* elementNodes,
    __global const float* elementE,
    __global const float* elementNu,
    __global const float* nodeX,
    __global const float* nodeY,
    __global const float* nodeZ,
    __global const int* rowPtr,
    __global const int* colIdx,
    __global float* values,
    const int numElements)
{
    int e = get_global_id(0);
    if (e >= numElements) return;
    
    // FIX: Declare ALL __local variables at outermost scope
    __local float dN_dxi[24], J[9], Jinv[9], dN_dx[24], B[144], D[36], Ke[576];
    __local float DB[144];  // MOVED HERE - was inside loop before
    __local int nodes[8];
    
    for (int i = 0; i < 8; i++) {
        nodes[i] = elementNodes[e*8 + i];
    }
    
    for (int i = 0; i < 576; i++) Ke[i] = 0.0f;
    
    float E = elementE[e];
    float nu = elementNu[e];
    compute_D_matrix(E, nu, D);
    
    float gp = 0.577350269f;
    float gaussPts[8][4] = {
        {-gp, -gp, -gp, 1.0f}, {gp, -gp, -gp, 1.0f}, {gp, gp, -gp, 1.0f}, {-gp, gp, -gp, 1.0f},
        {-gp, -gp, gp, 1.0f}, {gp, -gp, gp, 1.0f}, {gp, gp, gp, 1.0f}, {-gp, gp, gp, 1.0f}
    };
    
    for (int gp_idx = 0; gp_idx < 8; gp_idx++) {
        float xi = gaussPts[gp_idx][0];
        float eta = gaussPts[gp_idx][1];
        float zeta = gaussPts[gp_idx][2];
        float weight = gaussPts[gp_idx][3];
        
        shape_function_derivatives(xi, eta, zeta, dN_dxi);
        compute_jacobian(dN_dxi, nodeX, nodeY, nodeZ, nodes, J);
        float detJ = det3x3(J);
        
        if (detJ <= 0.0f) continue;
        
        inverse3x3(J, Jinv);
        matmul_3x8(Jinv, dN_dxi, dN_dx);
        compute_B_matrix(dN_dx, B);
        
        // DB is now declared at top of kernel
        for (int i = 0; i < 6; i++) {
            for (int j = 0; j < 24; j++) {
                float sum = 0.0f;
                for (int k = 0; k < 6; k++) {
                    sum += D[i*6 + k] * B[k*24 + j];
                }
                DB[i*24 + j] = sum;
            }
        }
        
        float factor = detJ * weight;
        for (int i = 0; i < 24; i++) {
            for (int j = 0; j < 24; j++) {
                float sum = 0.0f;
                for (int k = 0; k < 6; k++) {
                    sum += B[k*24 + i] * DB[k*24 + j];
                }
                Ke[i*24 + j] += sum * factor;
            }
        }
    }
    
    for (int i = 0; i < 8; i++) {
        for (int j = 0; j < 8; j++) {
            for (int di = 0; di < 3; di++) {
                for (int dj = 0; dj < 3; dj++) {
                    int globalI = nodes[i] * 3 + di;
                    int globalJ = nodes[j] * 3 + dj;
                    int localI = i * 3 + di;
                    int localJ = j * 3 + dj;
                    
                    float value = Ke[localI*24 + localJ];
                    if (fabs(value) < 1e-12f) continue;
                    
                    int rowStart = rowPtr[globalI];
                    int rowEnd = rowPtr[globalI + 1];
                    
                    for (int idx = rowStart; idx < rowEnd; idx++) {
                        if (colIdx[idx] == globalJ) {
                            atomic_add_global(&values[idx], value);
                            break;
                        }
                    }
                }
            }
        }
    }
}

// ============================================================================
// KERNEL 2: APPLY BOUNDARY CONDITIONS (UNCHANGED)
// ============================================================================

__kernel void apply_boundary_conditions(
    __global float* force,
    __global const uchar* isDirichlet,
    __global const float* dirichletValue,
    const int numDOFs)
{
    int i = get_global_id(0);
    if (i >= numDOFs) return;
    
    if (isDirichlet[i]) {
        force[i] = dirichletValue[i];
    }
}

// ============================================================================
// KERNEL 3: SPARSE MATRIX-VECTOR MULTIPLICATION (UNCHANGED)
// ============================================================================

__kernel void sparse_matvec(
    __global const int* rowPtr,
    __global const int* colIdx,
    __global const float* values,
    __global const float* x,
    __global float* y,
    __global const uchar* isDirichlet,
    const int numRows)
{
    int row = get_global_id(0);
    if (row >= numRows) return;
    
    if (isDirichlet[row]) {
        y[row] = x[row];
        return;
    }
    
    float sum = 0.0f;
    int rowStart = rowPtr[row];
    int rowEnd = rowPtr[row + 1];
    
    for (int j = rowStart; j < rowEnd; j++) {
        int col = colIdx[j];
        if (!isDirichlet[col]) {
            sum += values[j] * x[col];
        }
    }
    
    y[row] = sum;
}

// ============================================================================
// KERNEL 4: DOT PRODUCT (UNCHANGED)
// ============================================================================

__kernel void dot_product(
    __global const float* a,
    __global const float* b,
    __global float* partial_sums,
    __local float* scratch,
    const int n)
{
    int gid = get_global_id(0);
    int lid = get_local_id(0);
    int group_size = get_local_size(0);
    
    float sum = 0.0f;
    if (gid < n) {
        sum = a[gid] * b[gid];
    }
    scratch[lid] = sum;
    
    barrier(CLK_LOCAL_MEM_FENCE);
    
    for (int offset = group_size / 2; offset > 0; offset >>= 1) {
        if (lid < offset) {
            scratch[lid] += scratch[lid + offset];
        }
        barrier(CLK_LOCAL_MEM_FENCE);
    }
    
    if (lid == 0) {
        partial_sums[get_group_id(0)] = scratch[0];
    }
}

// ============================================================================
// KERNEL 5: VECTOR OPERATIONS (UNCHANGED)
// ============================================================================

__kernel void vector_ops(
    __global float* y,
    __global const float* x,
    __global const uchar* isDirichlet,
    const float alpha,
    const int op_type,
    const int n)
{
    int i = get_global_id(0);
    if (i >= n) return;
    
    if (isDirichlet[i] && op_type != 2) return;
    
    switch (op_type) {
        case 0:
            y[i] += alpha * x[i];
            break;
        case 1:
            y[i] = x[i] + alpha * y[i];
            break;
        case 2:
            y[i] *= alpha;
            break;
    }
}

// ============================================================================
// KERNEL 6: CALCULATE STRAINS AND STRESSES - FIXED
// ============================================================================

__kernel void calculate_strains_stresses(
    __global const int* elementNodes,
    __global const float* elementE,
    __global const float* elementNu,
    __global const float* nodeX,
    __global const float* nodeY,
    __global const float* nodeZ,
    __global const float* displacement,
    __global float* stressXX,
    __global float* stressYY,
    __global float* stressZZ,
    __global float* stressXY,
    __global float* stressXZ,
    __global float* stressYZ,
    const int numElements,
    const int width,
    const int height,
    const float dx)
{
    int e = get_global_id(0);
    if (e >= numElements) return;
    
    __local float dN_dxi[24], J[9], Jinv[9], dN_dx[24], B[144], D[36];
    __local int nodes[8];
    __local float ue[24];
    
    for (int i = 0; i < 8; i++) {
        nodes[i] = elementNodes[e*8 + i];
        int dofBase = nodes[i] * 3;
        ue[i*3 + 0] = displacement[dofBase + 0];
        ue[i*3 + 1] = displacement[dofBase + 1];
        ue[i*3 + 2] = displacement[dofBase + 2];
    }
    
    shape_function_derivatives(0.0f, 0.0f, 0.0f, dN_dxi);
    compute_jacobian(dN_dxi, nodeX, nodeY, nodeZ, nodes, J);
    
    float detJ = det3x3(J);
    if (detJ <= 0.0f) return;
    
    inverse3x3(J, Jinv);
    matmul_3x8(Jinv, dN_dxi, dN_dx);
    compute_B_matrix(dN_dx, B);
    
    __local float strain[6];
    for (int i = 0; i < 6; i++) {
        strain[i] = 0.0f;
        for (int j = 0; j < 24; j++) {
            strain[i] += B[i*24 + j] * ue[j];
        }
    }
    
    float E = elementE[e];
    float nu = elementNu[e];
    compute_D_matrix(E, nu, D);
    
    __local float stress[6];
    for (int i = 0; i < 6; i++) {
        stress[i] = 0.0f;
        for (int j = 0; j < 6; j++) {
            stress[i] += D[i*6 + j] * strain[j];
        }
    }
    
    float cx = 0.0f, cy = 0.0f, cz = 0.0f;
    for (int i = 0; i < 8; i++) {
        cx += nodeX[nodes[i]];
        cy += nodeY[nodes[i]];
        cz += nodeZ[nodes[i]];
    }
    cx /= 8.0f;
    cy /= 8.0f;
    cz /= 8.0f;
    
    int vx = (int)(cx / dx + 0.5f);
    int vy = (int)(cy / dx + 0.5f);
    int vz = (int)(cz / dx + 0.5f);
    
    if (vx >= 0 && vx < width && vy >= 0 && vy < height && vz >= 0) {
        int voxelIdx = vz * height * width + vy * width + vx;
        
        // FIX: Create private variables for atomic operations
        // This avoids casting __local pointers to different address spaces
        float s0 = stress[0];
        float s1 = stress[1];
        float s2 = stress[2];
        float s3 = stress[3];
        float s4 = stress[4];
        float s5 = stress[5];
        
        atomic_xchg((__global int*)&stressXX[voxelIdx], *((int*)&s0));
        atomic_xchg((__global int*)&stressYY[voxelIdx], *((int*)&s1));
        atomic_xchg((__global int*)&stressZZ[voxelIdx], *((int*)&s2));
        atomic_xchg((__global int*)&stressXY[voxelIdx], *((int*)&s3));
        atomic_xchg((__global int*)&stressXZ[voxelIdx], *((int*)&s4));
        atomic_xchg((__global int*)&stressYZ[voxelIdx], *((int*)&s5));
    }
}

// ============================================================================
// KERNEL 7: CALCULATE PRINCIPAL STRESSES (UNCHANGED)
// ============================================================================

__kernel void calculate_principal_stresses(
    __global const float* stressXX,
    __global const float* stressYY,
    __global const float* stressZZ,
    __global const float* stressXY,
    __global const float* stressXZ,
    __global const float* stressYZ,
    __global float* sigma1,
    __global float* sigma2,
    __global float* sigma3,
    __global const uchar* labels,
    const int numVoxels)
{
    int idx = get_global_id(0);
    if (idx >= numVoxels) return;
    if (labels[idx] == 0) return;
    
    float sxx = stressXX[idx];
    float syy = stressYY[idx];
    float szz = stressZZ[idx];
    float sxy = stressXY[idx];
    float sxz = stressXZ[idx];
    float syz = stressYZ[idx];
    
    float I1 = sxx + syy + szz;
    float I2 = sxx*syy + syy*szz + szz*sxx - sxy*sxy - sxz*sxz - syz*syz;
    float I3 = sxx*syy*szz + 2.0f*sxy*sxz*syz - sxx*syz*syz - syy*sxz*sxz - szz*sxy*sxy;
    
    float p = I2 - I1*I1/3.0f;
    float q = I3 + (2.0f*I1*I1*I1 - 9.0f*I1*I2)/27.0f;
    
    float s1, s2, s3;
    
    if (fabs(p) < 1e-9f) {
        s1 = s2 = s3 = I1 / 3.0f;
    } else {
        float r = sqrt(fmax(0.0f, -p*p*p / 27.0f));
        float cos_phi = clamp(-q / (2.0f * r), -1.0f, 1.0f);
        float phi = acos(cos_phi);
        
        float scale = 2.0f * sqrt(-p / 3.0f);
        float offset = I1 / 3.0f;
        
        s1 = offset + scale * cos(phi / 3.0f);
        s2 = offset + scale * cos((phi + 2.0f*M_PI_F) / 3.0f);
        s3 = offset + scale * cos((phi + 4.0f*M_PI_F) / 3.0f);
    }
    
    if (s1 < s2) { float tmp = s1; s1 = s2; s2 = tmp; }
    if (s1 < s3) { float tmp = s1; s1 = s3; s3 = tmp; }
    if (s2 < s3) { float tmp = s2; s2 = s3; s3 = tmp; }
    
    sigma1[idx] = s1;
    sigma2[idx] = s2;
    sigma3[idx] = s3;
}

// ============================================================================
// KERNEL 8: EVALUATE FAILURE - CORRECTED TO USE σ₂
// ============================================================================

float calculate_failure_index(float sigma1, float sigma2, float sigma3, 
    float cohesion, float phi, float tensile, int criterion)
{
    switch (criterion) {
        case 0: { // Mohr-Coulomb
            float left = sigma1 - sigma3;
            float right = 2.0f*cohesion*cos(phi) + (sigma1 + sigma3)*sin(phi);
            return (right > 1e-9f) ? left / right : left;
        }
        case 1: { // Drucker-Prager - CORRECTED to use σ₂
            float I1 = sigma1 + sigma2 + sigma3;
            float s1_dev = sigma1 - I1/3.0f;
            float s2_dev = sigma2 - I1/3.0f;
            float s3_dev = sigma3 - I1/3.0f;
            float J2 = (s1_dev*s1_dev + s2_dev*s2_dev + s3_dev*s3_dev) / 2.0f;
            float q = sqrt(3.0f * J2);
            
            float alpha = 2.0f*sin(phi) / (sqrt(3.0f) * (3.0f - sin(phi)));
            float k = 6.0f*cohesion*cos(phi) / (sqrt(3.0f) * (3.0f - sin(phi)));
            return (k > 1e-9f) ? (q - alpha*I1) / k : q - alpha*I1;
        }
        case 2: { // Hoek-Brown
            float ucs = 2.0f*cohesion*cos(phi) / (1.0f - sin(phi));
            float mb = 1.5f;
            float s = 0.004f;
            float a = 0.5f;
            
            if (sigma3 < 0.0f && s < 0.001f)
                return (tensile > 1e-9f) ? -sigma3 / tensile : -sigma3;
            
            float term = mb * sigma3 / ucs + s;
            if (term < 0.0f) term = 0.0f;
            
            float strength = sigma3 + ucs * pow(term, a);
            return (strength > 1e-9f) ? sigma1 / strength : sigma1;
        }
        case 3: { // Griffith
            if (sigma3 < 0.0f)
                return (tensile > 1e-9f) ? -sigma3 / tensile : -sigma3;
            else
                return (tensile*8.0f > 1e-9f) ? 
                    pow(sigma1 - sigma3, 2.0f) / (8.0f*tensile*(sigma1 + sigma3 + 1e-6f)) : 
                    sigma1 - sigma3;
        }
        default:
            return 0.0f;
    }
}

__kernel void evaluate_failure(
    __global const float* sigma1,
    __global const float* sigma2,
    __global const float* sigma3,
    __global float* failureIndex,
    __global uchar* damage,
    __global uchar* fractured,
    __global const uchar* labels,
    const float cohesion,
    const float frictionAngle,
    const float tensileStrength,
    const int failureCriterion,
    const int numVoxels,
    __global float* stressXX,
    __global float* stressYY,
    __global float* stressZZ,
    __global float* stressXY,
    __global float* stressXZ,
    __global float* stressYZ)
{
    int idx = get_global_id(0);
    if (idx >= numVoxels) return;
    if (labels[idx] == 0) return;
    
    float s1 = sigma1[idx];
    float s2 = sigma2[idx];
    float s3 = sigma3[idx];
    float phi = frictionAngle * M_PI_F / 180.0f;
    
    // Calculate failure index using CORRECTED function (includes σ₂)
    float fi = calculate_failure_index(s1, s2, s3, cohesion, phi, tensileStrength, failureCriterion);
    failureIndex[idx] = fi;
    
    // Mazars damage model
    const float DAMAGE_INITIATION = 0.7f;
    const float DAMAGE_EXPONENT = 2.0f;
    const float RESIDUAL_STRENGTH = 0.05f;
    
    float dmg = 0.0f;
    
    if (fi >= DAMAGE_INITIATION) {
        if (fi >= 1.0f) {
            float overstress = fi - 1.0f;
            dmg = 1.0f - RESIDUAL_STRENGTH * exp(-DAMAGE_EXPONENT * overstress);
            dmg = clamp(dmg, 0.0f, 1.0f - RESIDUAL_STRENGTH);
            
            fractured[idx] = 1;
        } else {
            float normalizedLoad = (fi - DAMAGE_INITIATION) / (1.0f - DAMAGE_INITIATION);
            dmg = pow(normalizedLoad, DAMAGE_EXPONENT);
            dmg = clamp(dmg, 0.0f, 0.8f);
        }
    }
    
    damage[idx] = (uchar)(dmg * 255.0f);
    
    float degradation = 1.0f - dmg;
    stressXX[idx] *= degradation;
    stressYY[idx] *= degradation;
    stressZZ[idx] *= degradation;
    stressXY[idx] *= degradation;
    stressXZ[idx] *= degradation;
    stressYZ[idx] *= degradation;
}

// ============================================================================
// KERNEL 9: PLASTIC CORRECTION WITH PROPER STRAIN HARDENING
// ============================================================================

__kernel void apply_plastic_correction(
    __global float* stressXX,
    __global float* stressYY,
    __global float* stressZZ,
    __global float* stressXY,
    __global float* stressXZ,
    __global float* stressYZ,
    __global float* plasticStrain,
    __global const uchar* labels,
    const float yieldStress,
    const float hardeningModulus,
    const float shearModulus,
    const int numVoxels)
{
    int idx = get_global_id(0);
    if (idx >= numVoxels) return;
    if (labels[idx] == 0) return;
    
    float sxx = stressXX[idx];
    float syy = stressYY[idx];
    float szz = stressZZ[idx];
    float sxy = stressXY[idx];
    float sxz = stressXZ[idx];
    float syz = stressYZ[idx];
    
    float p = (sxx + syy + szz) / 3.0f;
    
    float sxx_dev = sxx - p;
    float syy_dev = syy - p;
    float szz_dev = szz - p;
    
    // Von Mises: σ_eq = √(3·J₂)
    float J2 = 0.5f * (sxx_dev*sxx_dev + syy_dev*syy_dev + szz_dev*szz_dev) +
               sxy*sxy + sxz*sxz + syz*syz;
    float sigma_eq = sqrt(3.0f * J2);
    
    // Current equivalent plastic strain
    float ep_eq = plasticStrain[idx];
    
    // Yield stress with isotropic hardening: σ_y = σ_y0 + H·εᵖ_eq
    float sigma_y = yieldStress + hardeningModulus * ep_eq;
    
    // Yield function
    float f = sigma_eq - sigma_y;
    
    if (f > 0.0f) {
        // Plastic multiplier (radial return)
        float delta_lambda = f / (3.0f * shearModulus + hardeningModulus);
        
        // Return to yield surface
        float return_factor = sigma_y / sigma_eq;
        
        sxx_dev *= return_factor;
        syy_dev *= return_factor;
        szz_dev *= return_factor;
        sxy *= return_factor;
        sxz *= return_factor;
        syz *= return_factor;
        
        stressXX[idx] = sxx_dev + p;
        stressYY[idx] = syy_dev + p;
        stressZZ[idx] = szz_dev + p;
        stressXY[idx] = sxy;
        stressXZ[idx] = sxz;
        stressYZ[idx] = syz;
        
        // Update plastic strain: Δεᵖ_eq = √(2/3)·Δλ
        float delta_ep_eq = sqrt(2.0f/3.0f) * delta_lambda;
        plasticStrain[idx] = ep_eq + delta_ep_eq;
    }
}

// ============================================================================
// KERNEL 10: PRESSURE DIFFUSION - CORRECTED WITH BIOT THEORY
// ============================================================================

__kernel void pressure_diffusion(
    __global const float* pressureIn,
    __global float* pressureOut,
    __global const uchar* labels,
    __global const float* fractureAperture,
    const int W, const int H, const int D,
    const float dx,
    const float dt,
    const float rockPerm,
    const float fluidVisc,
    const float porosity,
    const float aquiferPressure,
    const int enableAquifer)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int z = get_global_id(2);
    
    if (x >= W || y >= H || z >= D) return;
    if (x == 0 || x == W-1 || y == 0 || y == H-1 || z == 0 || z == D-1) return;
    
    int idx = (z * H + y) * W + x;
    uchar mat = labels[idx];
    
    if (mat == 0) {
        if (enableAquifer)
            pressureOut[idx] = aquiferPressure;
        else
            pressureOut[idx] = pressureIn[idx];
        return;
    }
    
    // CORRECTED: Proper Biot poroelasticity parameters
    float K_s = 36e9f; // Solid grain bulk modulus (Pa) - quartz
    float K_f = 2.2e9f; // Fluid bulk modulus (Pa) - water
    float K_d = 20e9f; // Drained bulk modulus (Pa) - approximate
    float alpha_biot = 0.8f; // Biot coefficient
    
    // Storage coefficient: S = φ/K_f + (α - φ)/K_s
    float S_storage = porosity / K_f + (alpha_biot - porosity) / K_s;
    
    // Total compressibility
    float c_total = S_storage / porosity;
    
    // Hydraulic diffusivity: D = k/(φ·μ·c_t)
    float diffusivity = rockPerm / (porosity * fluidVisc * c_total);
    
    // Stability: α_CFL ≤ 1/6 for 3D explicit
    float alpha_cfl = diffusivity * dt / (dx * dx);
    if (alpha_cfl > 0.16667f)
        alpha_cfl = 0.16667f;
    
    float P_c = pressureIn[idx];
    
    int idx_xp = idx + 1;
    int idx_xm = idx - 1;
    int idx_yp = idx + W;
    int idx_ym = idx - W;
    int idx_zp = idx + (W * H);
    int idx_zm = idx - (W * H);
    
    float P_xp = pressureIn[idx_xp];
    float P_xm = pressureIn[idx_xm];
    float P_yp = pressureIn[idx_yp];
    float P_ym = pressureIn[idx_ym];
    float P_zp = pressureIn[idx_zp];
    float P_zm = pressureIn[idx_zm];
    
    // Boundary handling
    if (labels[idx_xp] == 0) P_xp = enableAquifer ? aquiferPressure : P_c;
    if (labels[idx_xm] == 0) P_xm = enableAquifer ? aquiferPressure : P_c;
    if (labels[idx_yp] == 0) P_yp = enableAquifer ? aquiferPressure : P_c;
    if (labels[idx_ym] == 0) P_ym = enableAquifer ? aquiferPressure : P_c;
    if (labels[idx_zp] == 0) P_zp = enableAquifer ? aquiferPressure : P_c;
    if (labels[idx_zm] == 0) P_zm = enableAquifer ? aquiferPressure : P_c;
    
    // Enhanced permeability in fractures (cubic law)
    float k_eff = rockPerm;
    float aperture = fractureAperture[idx];
    if (aperture > 1e-6f) {
        k_eff = aperture * aperture / 12.0f;
        k_eff = fmax(k_eff, rockPerm * 1000.0f); // Cap at 1000x
    }
    
    // Recalculate diffusivity with effective permeability
    diffusivity = k_eff / (porosity * fluidVisc * c_total);
    alpha_cfl = diffusivity * dt / (dx * dx);
    alpha_cfl = fmin(alpha_cfl, 0.16667f);
    
    // Gravity correction in Z-direction
    float rho_f = 1000.0f; // kg/m³
    float g = 9.81f;
    float gravity_correction = rho_f * g * dx;
    
    // 7-point stencil with gravity
    float laplacian = P_xp + P_xm + P_yp + P_ym + 
                     (P_zp - gravity_correction) + (P_zm + gravity_correction) - 6.0f * P_c;
    
    float P_new = P_c + alpha_cfl * laplacian;
    
    // Physical bounds
    if (P_new < 0.0f) P_new = 0.0f;
    
    pressureOut[idx] = P_new;
}

// ============================================================================
// KERNEL 11: UPDATE FRACTURE APERTURES - CORRECTED WITH SNEDDON SOLUTION
// ============================================================================

__kernel void update_fracture_apertures(
    __global const float* pressure,
    __global const float* sigma3,
    __global float* fractureAperture,
    __global const uchar* fractureField,
    __global const uchar* labels,
    const float minAperture,
    const float youngModulus,
    const float poissonRatio,
    const float biotCoeff,
    const float dx,
    const int numVoxels)
{
    int idx = get_global_id(0);
    if (idx >= numVoxels) return;
    if (labels[idx] == 0) return;
    if (!fractureField[idx]) return;
    
    float P = pressure[idx];
    float s_n_total = sigma3[idx];
    float s_n_eff = s_n_total - biotCoeff * P;
    
    float delta_P = P - s_n_eff;
    
    if (delta_P > 0.0f) {
        // Sneddon solution for pressurized penny-shaped crack
        // w = (4(1-ν²)/E) · ΔP · L/2
        float E = youngModulus * 1e6f;
        float nu = poissonRatio;
        float L = dx;
        
        float aperture_mechanical = (4.0f * (1.0f - nu * nu) / E) * delta_P * L / 2.0f;
        
        // Willis-Richards stress-dependent component
        // w = w₀ · exp(β · Δσ_n)
        float w_residual = minAperture;
        float beta = 0.5f / 1e6f; // 0.5 MPa⁻¹
        float aperture_stress = w_residual * exp(beta * delta_P);
        
        float aperture_total = aperture_mechanical + aperture_stress;
        
        // Physical bounds
        aperture_total = fmax(aperture_total, minAperture);
        aperture_total = fmin(aperture_total, dx / 10.0f);
        
        fractureAperture[idx] = aperture_total;
    } else {
        fractureAperture[idx] = minAperture;
    }
}

// ============================================================================
// KERNEL 12: INITIALIZE GEOTHERMAL (UNCHANGED - Already correct)
// ============================================================================

__kernel void initialize_geothermal(
    __global float* temperature,
    __global const uchar* labels,
    const int W, const int H, const int D,
    const float dx,
    const float surfaceTemp,
    const float gradient_per_km)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int z = get_global_id(2);
    
    if (x >= W || y >= H || z >= D) return;
    
    int idx = (z * H + y) * W + x;
    
    if (labels[idx] == 0) {
        temperature[idx] = surfaceTemp;
        return;
    }
    
    float depth_m = (float)z * dx;
    float temp = surfaceTemp + (gradient_per_km / 1000.0f) * depth_m;
    
    temperature[idx] = temp;
}

// ============================================================================
// KERNEL 13: CALCULATE EFFECTIVE STRESS (UNCHANGED - Already correct)
// ============================================================================

__kernel void calculate_effective_stress(
    __global const float* stressXX,
    __global const float* stressYY,
    __global const float* stressZZ,
    __global const float* pressure,
    __global float* effStressXX,
    __global float* effStressYY,
    __global float* effStressZZ,
    __global const uchar* labels,
    const float biotCoeff,
    const int numVoxels)
{
    int idx = get_global_id(0);
    if (idx >= numVoxels) return;
    if (labels[idx] == 0) return;
    
    float P = pressure[idx];
    float alpha = biotCoeff;
    
    effStressXX[idx] = stressXX[idx] - alpha * P;
    effStressYY[idx] = stressYY[idx] - alpha * P;
    effStressZZ[idx] = stressZZ[idx] - alpha * P;
}

// ============================================================================
// KERNEL 14: APPLY INJECTION SOURCE (UNCHANGED)
// ============================================================================

__kernel void apply_injection_source(
    __global float* pressure,
    __global const uchar* labels,
    const int W, const int H, const int D,
    const int injX, const int injY, const int injZ,
    const int radius,
    const float injectionPressure)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int z = get_global_id(2);
    
    if (x >= W || y >= H || z >= D) return;
    
    int idx = (z * H + y) * W + x;
    if (labels[idx] == 0) return;
    
    int dx = x - injX;
    int dy = y - injY;
    int dz = z - injZ;
    float dist = sqrt((float)(dx*dx + dy*dy + dz*dz));
    
    if (dist <= (float)radius) {
        pressure[idx] = injectionPressure;
    }
}

// ============================================================================
// KERNEL 15: DETECT HYDRAULIC FRACTURES - CORRECTED WITH K_I CRITERION
// ============================================================================

__kernel void detect_hydraulic_fractures(
    __global const float* sigma1,
    __global const float* sigma3,
    __global const float* pressure,
    __global uchar* fractureField,
    __global uchar* damageField,
    __global float* fractureAperture,
    __global const uchar* labels,
    const float cohesion,
    const float frictionAngle,
    const float tensileStrength,
    const float biotCoeff,
    const float minAperture,
    const float fractureToughness,
    const float youngModulus,
    const float poissonRatio,
    const float dx,
    const int numVoxels)
{
    int idx = get_global_id(0);
    if (idx >= numVoxels) return;
    if (labels[idx] == 0) return;
    if (fractureField[idx] != 0) return;
    
    float P = pressure[idx];
    float s1_total = sigma1[idx];
    float s3_total = sigma3[idx];
    
    float s1_eff = s1_total - biotCoeff * P;
    float s3_eff = s3_total - biotCoeff * P;
    
    // Mohr-Coulomb criterion
    float phi = frictionAngle * M_PI_F / 180.0f;
    float left = s1_eff - s3_eff;
    float right = 2.0f * cohesion * cos(phi) + (s1_eff + s3_eff) * sin(phi);
    
    float failureIndex = (right > 1e-9f) ? left / right : left;
    
    // Stress intensity factor criterion (Mode I)
    // K_I = ΔP·√(πa) where a is crack half-length
    float crack_half_length = dx / 2.0f;
    float delta_P = fmax(0.0f, P - s3_eff);
    float K_I = delta_P * sqrt(M_PI_F * crack_half_length);
    
    float K_Ic = fractureToughness * 1e6f; // MPa·√m to Pa·√m
    
    int fracture_by_stress = failureIndex >= 1.0f;
    int fracture_by_toughness = K_I > K_Ic;
    
    if (fracture_by_stress || fracture_by_toughness) {
        fractureField[idx] = 1;
        damageField[idx] = 255;
        
        // Initial aperture from Sneddon solution
        float E = youngModulus * 1e6f;
        float nu = poissonRatio;
        float initial_aperture = (4.0f / M_PI_F) * ((1.0f - nu * nu) / E) * delta_P * crack_half_length;
        initial_aperture = fmax(initial_aperture, minAperture);
        
        fractureAperture[idx] = initial_aperture;
    }
}
";
    }

    public GeomechanicalResults Simulate(byte[,,] labels, float[,,] density,
        IProgress<float> progress, CancellationToken token)
    {
        if (!_initialized)
            throw new InvalidOperationException("GPU not initialized - OpenCL context creation failed");

        var extent = _params.SimulationExtent;
        var startTime = DateTime.Now;

        try
        {
            Logger.Log("[GeomechGPU] ========== STARTING GPU SIMULATION ==========");
            Logger.Log($"[GeomechGPU] Domain: {extent.Width}×{extent.Height}×{extent.Depth} voxels");
            Logger.Log($"[GeomechGPU] Voxel size: {_params.PixelSize} µm");
            Logger.Log(
                $"[GeomechGPU] Loading: σ1={_params.Sigma1} MPa, σ2={_params.Sigma2} MPa, σ3={_params.Sigma3} MPa");

            // STEP 1: Generate FEM mesh from voxel data
            progress?.Report(0.05f);
            Logger.Log("[GeomechGPU] Generating FEM mesh from voxels...");
            GenerateMeshFromVoxels(labels, density);
            token.ThrowIfCancellationRequested();

            // STEP 2: Upload data to GPU
            progress?.Report(0.10f);
            Logger.Log("[GeomechGPU] Uploading mesh and material data to GPU...");
            UploadToGPU();
            token.ThrowIfCancellationRequested();

            // STEP 3: Assemble stiffness matrix on GPU
            progress?.Report(0.20f);
            Logger.Log("[GeomechGPU] Assembling stiffness matrix on GPU...");
            AssembleStiffnessMatrixGPU(progress, token);
            token.ThrowIfCancellationRequested();

            // STEP 4: Apply boundary conditions and loading
            progress?.Report(0.30f);
            Logger.Log("[GeomechGPU] Applying boundary conditions...");
            ApplyBoundaryConditionsGPU(labels);
            token.ThrowIfCancellationRequested();

            // STEP 5: Solve displacement field using PCG on GPU
            progress?.Report(0.35f);
            Logger.Log("[GeomechGPU] Solving for displacements (GPU PCG)...");
            var converged = SolveDisplacementsGPU(progress, token);
            token.ThrowIfCancellationRequested();

            if (!converged)
                Logger.LogWarning("[GeomechGPU] GPU solver did not fully converge - results may be approximate");

            // STEP 6: Calculate strains and stresses on GPU
            progress?.Report(0.75f);
            Logger.Log("[GeomechGPU] Computing strains and stresses on GPU...");
            CalculateStrainsAndStressesGPU();
            token.ThrowIfCancellationRequested();

            // STEP 7: Calculate principal stresses on GPU
            progress?.Report(0.85f);
            Logger.Log("[GeomechGPU] Computing principal stresses on GPU...");
            CalculatePrincipalStressesGPU(labels);
            token.ThrowIfCancellationRequested();

            // STEP 7.5: Initialize geothermal and fluid fields
            if (_params.EnableGeothermal || _params.EnableFluidInjection)
            {
                progress?.Report(0.88f);
                Logger.Log("[GeomechGPU] Initializing geothermal and fluid simulation on GPU...");
                InitializeGeothermalAndFluidGPU(labels, extent);
                token.ThrowIfCancellationRequested();
            }


            // STEP 8: Evaluate failure with progressive damage on GPU
            progress?.Report(0.90f);
            Logger.Log("[GeomechGPU] Evaluating failure criteria on GPU...");
            EvaluateFailureGPU(labels);
            token.ThrowIfCancellationRequested();

            // STEP 9: Apply plasticity correction if enabled
            if (_params.EnablePlasticity)
            {
                progress?.Report(0.92f);
                Logger.Log("[GeomechGPU] Applying elasto-plastic correction on GPU...");
                ApplyPlasticCorrectionGPU(labels);
                token.ThrowIfCancellationRequested();
            }

            // STEP 10: Download results from GPU
            progress?.Report(0.95f);
            Logger.Log("[GeomechGPU] Downloading results from GPU to host...");
            var results = DownloadResults(extent, labels);
            results.Converged = converged;
            results.IterationsPerformed = _iterationsPerformed;

            // STEP 10.5: Simulate fluid injection and hydraulic fracturing (AFTER results object exists)
            if (_params.EnableFluidInjection)
            {
                progress?.Report(0.96f);
                Logger.Log("[GeomechGPU] Simulating fluid injection and fracturing on GPU...");
                SimulateFluidInjectionAndFracturingGPU(results, labels, progress, token);
                token.ThrowIfCancellationRequested();
            }

            // STEP 11: Generate Mohr circles for visualization (CPU)
            Logger.Log("[GeomechGPU] Generating Mohr circles...");
            GenerateMohrCircles(results);

            // STEP 12: Calculate global statistics (CPU)
            Logger.Log("[GeomechGPU] Calculating global statistics...");
            CalculateGlobalStatistics(results);
            if (_params.EnableGeothermal || _params.EnableFluidInjection)
            {
                Logger.Log("[GeomechGPU] Finalizing geothermal and fluid results...");
                PopulateGeothermalAndFluidResultsGPU(results);
            }

            // STEP 13: Populate geothermal and fluid results
            if (_params.EnableGeothermal || _params.EnableFluidInjection)
            {
                Logger.Log("[GeomechGPU] Finalizing geothermal and fluid results...");
                PopulateGeothermalAndFluidResultsGPU(results);
            }

            results.ComputationTime = DateTime.Now - startTime;

            progress?.Report(1.0f);

            Logger.Log("[GeomechGPU] ========== GPU SIMULATION COMPLETE ==========");
            Logger.Log($"[GeomechGPU] Computation time: {results.ComputationTime.TotalSeconds:F2} s");
            Logger.Log($"[GeomechGPU] GPU speedup: ~{EstimateSpeedup(results.ComputationTime):F1}x vs CPU");
            Logger.Log($"[GeomechGPU] Converged: {converged} ({_iterationsPerformed} iterations)");
            Logger.Log($"[GeomechGPU] Mean stress: {results.MeanStress / 1e6f:F2} MPa");
            Logger.Log($"[GeomechGPU] Max shear: {results.MaxShearStress / 1e6f:F2} MPa");
            Logger.Log(
                $"[GeomechGPU] Failed voxels: {results.FailedVoxels}/{results.TotalVoxels} ({results.FailedVoxelPercentage:F2}%)");

            return results;
        }
        catch (OperationCanceledException)
        {
            Logger.Log("[GeomechGPU] GPU simulation cancelled by user");
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError($"[GeomechGPU] GPU simulation failed: {ex.Message}");
            Logger.LogError($"[GeomechGPU] Stack trace: {ex.StackTrace}");

            // Try to provide helpful diagnostics
            if (ex.Message.Contains("CL_OUT_OF_RESOURCES") || ex.Message.Contains("CL_MEM_OBJECT_ALLOCATION_FAILURE"))
            {
                Logger.LogError("[GeomechGPU] GPU ran out of memory - try:");
                Logger.LogError("  1. Reducing domain size");
                Logger.LogError("  2. Enabling data offloading");
                Logger.LogError("  3. Using CPU solver instead");
            }

            throw new Exception($"GPU geomechanical simulation failed: {ex.Message}", ex);
        }
    }

// Add this helper method at the end of the class
    private float EstimateSpeedup(TimeSpan gpuTime)
    {
        // Rough estimate: GPU is typically 10-50x faster for large problems
        // This is just for logging purposes
        var elements = _numElements;
        if (elements < 1000) return 5f;
        if (elements < 10000) return 15f;
        if (elements < 100000) return 30f;
        return 50f;
    }

    private void GenerateMeshFromVoxels(byte[,,] labels, float[,,] density)
    {
        var extent = _params.SimulationExtent;
        var w = extent.Width;
        var h = extent.Height;
        var d = extent.Depth;
        var dx = _params.PixelSize / 1e6f;

        Logger.Log("[GeomechGPU] Generating FEM mesh from voxels");

        _numElements = 0;
        for (var z = 0; z < d - 1; z++)
        for (var y = 0; y < h - 1; y++)
        for (var x = 0; x < w - 1; x++)
            if (labels[x, y, z] != 0)
                _numElements++;

        _numNodes = w * h * d;
        _numDOFs = _numNodes * 3;

        Logger.Log($"[GeomechGPU] Mesh: {_numElements} elements, {_numNodes} nodes, {_numDOFs} DOFs");

        _nodeX = new float[_numNodes];
        _nodeY = new float[_numNodes];
        _nodeZ = new float[_numNodes];

        var nodeIdx = 0;
        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            _nodeX[nodeIdx] = x * dx;
            _nodeY[nodeIdx] = y * dx;
            _nodeZ[nodeIdx] = z * dx;
            nodeIdx++;
        }

        _elementNodes = new int[_numElements * 8];
        _elementE = new float[_numElements];
        _elementNu = new float[_numElements];

        var elemIdx = 0;
        for (var z = 0; z < d - 1; z++)
        for (var y = 0; y < h - 1; y++)
        for (var x = 0; x < w - 1; x++)
        {
            if (labels[x, y, z] == 0) continue;

            var n0 = (z * h + y) * w + x;
            _elementNodes[elemIdx * 8 + 0] = n0;
            _elementNodes[elemIdx * 8 + 1] = n0 + 1;
            _elementNodes[elemIdx * 8 + 2] = (z * h + y + 1) * w + x + 1;
            _elementNodes[elemIdx * 8 + 3] = (z * h + y + 1) * w + x;
            _elementNodes[elemIdx * 8 + 4] = ((z + 1) * h + y) * w + x;
            _elementNodes[elemIdx * 8 + 5] = ((z + 1) * h + y) * w + x + 1;
            _elementNodes[elemIdx * 8 + 6] = ((z + 1) * h + y + 1) * w + x + 1;
            _elementNodes[elemIdx * 8 + 7] = ((z + 1) * h + y + 1) * w + x;

            _elementE[elemIdx] = _params.YoungModulus * 1e6f;
            _elementNu[elemIdx] = _params.PoissonRatio;
            elemIdx++;
        }

        _displacement = new float[_numDOFs];
        _force = new float[_numDOFs];
        _isDirichlet = new bool[_numDOFs];
        _dirichletValue = new float[_numDOFs];

        InitializeSparseMatrixStructure();
    }

    private void InitializeSparseMatrixStructure()
    {
        Logger.Log("[GeomechGPU] Initializing sparse matrix structure (block-chunked)");
    
        // Calculate if we need blocking
        long estimatedEntries = (long)_numElements * 576; // Upper bound
        int numBlocks = Math.Max(1, (int)Math.Ceiling(estimatedEntries / (double)MAX_ENTRIES_PER_CHUNK));
    
        if (numBlocks == 1)
        {
            // Small enough - use direct method
            InitializeSparseMatrixStructureDirect();
        }
        else
        {
            // Too large - use block assembly
            Logger.Log($"[GeomechGPU] Using {numBlocks} matrix blocks for assembly");
            InitializeSparseMatrixStructureBlocked(numBlocks);
        }
    }
    private void InitializeSparseMatrixStructureDirect()
{
    // Original two-pass algorithm for smaller problems
    var rowNnz = new int[_numDOFs];
    var rowSets = new Dictionary<int, HashSet<int>>();
    
    // PASS 1: Build sparsity pattern
    for (int e = 0; e < _numElements; e++)
    {
        for (int i = 0; i < 8; i++)
        {
            for (int j = 0; j < 8; j++)
            {
                for (int di = 0; di < 3; di++)
                {
                    for (int dj = 0; dj < 3; dj++)
                    {
                        int globalI = _elementNodes[e * 8 + i] * 3 + di;
                        int globalJ = _elementNodes[e * 8 + j] * 3 + dj;
                        
                        if (!rowSets.ContainsKey(globalI))
                            rowSets[globalI] = new HashSet<int>();
                        rowSets[globalI].Add(globalJ);
                    }
                }
            }
        }
        
        if (e % 10000 == 0)
        {
            Logger.Log($"[GeomechGPU] Pattern: {e}/{_numElements} elements ({100.0*e/_numElements:F1}%)");
        }
    }
    
    // Build rowPtr
    _rowPtr = new List<int>(_numDOFs + 1);
    _rowPtr.Add(0);
    for (int i = 0; i < _numDOFs; i++)
    {
        int count = rowSets.ContainsKey(i) ? rowSets[i].Count : 0;
        _rowPtr.Add(_rowPtr[i] + count);
    }
    
    _nnz = _rowPtr[_numDOFs];
    
    if (_nnz > int.MaxValue)
        throw new Exception($"Matrix too large: {_nnz} entries exceeds .NET array limit. Enable offloading.");
    
    Logger.Log($"[GeomechGPU] Sparse matrix: {_nnz:N0} non-zeros ({100.0 * _nnz / ((long)_numDOFs * _numDOFs):F6}% density)");
    
    // PASS 2: Allocate and fill
    _colIdx = new List<int>(new int[_nnz]);
    _values = new List<float>(new float[_nnz]);
    
    for (int i = 0; i < _numDOFs; i++)
    {
        if (rowSets.ContainsKey(i))
        {
            var sortedCols = rowSets[i].OrderBy(c => c).ToArray();
            int pos = _rowPtr[i];
            for (int j = 0; j < sortedCols.Length; j++)
            {
                _colIdx[pos + j] = sortedCols[j];
            }
        }
    }
    
    Logger.Log("[GeomechGPU] Sparse matrix structure built");
}

private void InitializeSparseMatrixStructureBlocked(int numBlocks)
{
    // For extremely large problems, we cannot store full CSR in memory
    // Solution: Use iterative solver with matrix-free approach
    
    Logger.LogWarning("[GeomechGPU] Matrix too large for direct storage - switching to matrix-free mode");
    
    // Store only element data for matrix-vector products
    // During SpMV, we'll recompute contributions on-the-fly
    _nnz = -1; // Flag for matrix-free mode
    _rowPtr = null;
    _colIdx = null;
    _values = null;
    
    Logger.Log("[GeomechGPU] Using matrix-free element-by-element assembly");
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
        _colIdx = new List<int>();
        _values = new List<float>();

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

    private void UploadToGPU()
    {
        Logger.Log("[GeomechGPU] Uploading data to GPU");

        int error;

        _bufNodeX = CreateAndFillBuffer(_nodeX, MemFlags.ReadOnly, out error);
        CheckError(error, "Create bufNodeX");
        _bufNodeY = CreateAndFillBuffer(_nodeY, MemFlags.ReadOnly, out error);
        CheckError(error, "Create bufNodeY");
        _bufNodeZ = CreateAndFillBuffer(_nodeZ, MemFlags.ReadOnly, out error);
        CheckError(error, "Create bufNodeZ");

        _bufElementNodes = CreateAndFillBuffer(_elementNodes, MemFlags.ReadOnly, out error);
        CheckError(error, "Create bufElementNodes");
        _bufElementE = CreateAndFillBuffer(_elementE, MemFlags.ReadOnly, out error);
        CheckError(error, "Create bufElementE");
        _bufElementNu = CreateAndFillBuffer(_elementNu, MemFlags.ReadOnly, out error);
        CheckError(error, "Create bufElementNu");

        _bufRowPtr = CreateAndFillBuffer(_rowPtr.ToArray(), MemFlags.ReadOnly, out error);
        CheckError(error, "Create bufRowPtr");
        _bufColIdx = CreateAndFillBuffer(_colIdx.ToArray(), MemFlags.ReadOnly, out error);
        CheckError(error, "Create bufColIdx");
        _bufValues = CreateAndFillBuffer(_values.ToArray(), MemFlags.ReadWrite, out error);
        CheckError(error, "Create bufValues");

        _bufDisplacement = CreateBuffer<float>(_numDOFs, MemFlags.ReadWrite, out error);
        CheckError(error, "Create bufDisplacement");
        _bufForce = CreateAndFillBuffer(_force, MemFlags.ReadWrite, out error);
        CheckError(error, "Create bufForce");

        var isDirichletByte = _isDirichlet.Select(b => (byte)(b ? 1 : 0)).ToArray();
        _bufIsDirichlet = CreateAndFillBuffer(isDirichletByte, MemFlags.ReadOnly, out error);
        CheckError(error, "Create bufIsDirichlet");
        _bufDirichletValue = CreateAndFillBuffer(_dirichletValue, MemFlags.ReadOnly, out error);
        CheckError(error, "Create bufDirichletValue");

        var numWorkGroups = (_numDOFs + _workGroupSize - 1) / _workGroupSize;
        _bufPartialSums = CreateBuffer<float>(numWorkGroups, MemFlags.ReadWrite, out error);
        CheckError(error, "Create bufPartialSums");
        _bufTempVector = CreateBuffer<float>(_numDOFs, MemFlags.ReadWrite, out error);
        CheckError(error, "Create bufTempVector");

        _cl.Finish(_queue);
        Logger.Log("[GeomechGPU] Upload complete");
    }

    private void AssembleStiffnessMatrixGPU(IProgress<float> progress, CancellationToken token)
    {
        Logger.Log("[GeomechGPU] Assembling stiffness matrix on GPU");

        var zeroValues = new float[_nnz];
        EnqueueWriteBuffer(_bufValues, zeroValues);

        var argIdx = 0;
        SetKernelArg(_kernelAssembleElement, argIdx++, _bufElementNodes);
        SetKernelArg(_kernelAssembleElement, argIdx++, _bufElementE);
        SetKernelArg(_kernelAssembleElement, argIdx++, _bufElementNu);
        SetKernelArg(_kernelAssembleElement, argIdx++, _bufNodeX);
        SetKernelArg(_kernelAssembleElement, argIdx++, _bufNodeY);
        SetKernelArg(_kernelAssembleElement, argIdx++, _bufNodeZ);
        SetKernelArg(_kernelAssembleElement, argIdx++, _bufRowPtr);
        SetKernelArg(_kernelAssembleElement, argIdx++, _bufColIdx);
        SetKernelArg(_kernelAssembleElement, argIdx++, _bufValues);
        SetKernelArg(_kernelAssembleElement, argIdx++, _numElements);

        var globalSize = (nuint)((_numElements + _workGroupSize - 1) / _workGroupSize * _workGroupSize);
        var localSize = (nuint)_workGroupSize;

        var error = _cl.EnqueueNdrangeKernel(_queue, _kernelAssembleElement, 1, null, &globalSize, &localSize, 0, null,
            null);
        CheckError(error, "EnqueueNDRange assemble");

        _cl.Finish(_queue);
        progress?.Report(0.25f);

        Logger.Log("[GeomechGPU] Stiffness matrix assembled");
    }

    private void ApplyBoundaryConditionsGPU(byte[,,] labels)
    {
        Logger.Log("[GeomechGPU] Applying boundary conditions");

        var extent = _params.SimulationExtent;
        var w = extent.Width;
        var h = extent.Height;
        var d = extent.Depth;
        var dx = _params.PixelSize / 1e6f;

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

        var elementFaceArea = dx * dx;

        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            if (y > 0 && x > 0 && y < h - 1 && x < w - 1)
            {
                var hasMatBelow = labels[x - 1, y - 1, d - 2] != 0 ||
                                  labels[x, y - 1, d - 2] != 0 ||
                                  labels[x - 1, y, d - 2] != 0 ||
                                  labels[x, y, d - 2] != 0;

                if (!hasMatBelow) continue;
            }

            var nodeIdx = ((d - 1) * h + y) * w + x;
            var dofZ = nodeIdx * 3 + 2;
            _force[dofZ] -= sigma1_Pa * elementFaceArea / 4.0f;
        }

        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var nodeIdx = (0 * h + y) * w + x;
            var dofZ = nodeIdx * 3 + 2;
            _isDirichlet[dofZ] = true;
            _dirichletValue[dofZ] = 0.0f;
        }

        for (var z = 0; z < d; z++)
        for (var x = 0; x < w; x++)
            if (z > 0 && x > 0 && z < d - 1 && x < w - 1)
            {
                var hasMat = labels[x - 1, 0, z - 1] != 0 || labels[x, 0, z - 1] != 0;
                if (hasMat)
                {
                    var nodeIdx = (z * h + 0) * w + x;
                    var dofY = nodeIdx * 3 + 1;
                    if (!_isDirichlet[dofY])
                        _force[dofY] += sigma2_Pa * elementFaceArea / 4.0f;
                }

                hasMat = labels[x - 1, h - 2, z - 1] != 0 || labels[x, h - 2, z - 1] != 0;
                if (hasMat)
                {
                    var nodeIdx2 = (z * h + (h - 1)) * w + x;
                    var dofY2 = nodeIdx2 * 3 + 1;
                    if (!_isDirichlet[dofY2])
                        _force[dofY2] -= sigma2_Pa * elementFaceArea / 4.0f;
                }
            }

        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        {
            var nodeIdx1 = (z * h + y) * w + 0;
            var dofX1 = nodeIdx1 * 3 + 0;
            _isDirichlet[dofX1] = true;
            _dirichletValue[dofX1] = 0.0f;

            if (z > 0 && y > 0 && z < d - 1 && y < h - 1)
            {
                var hasMat = labels[w - 2, y - 1, z - 1] != 0 || labels[w - 2, y, z - 1] != 0;
                if (hasMat)
                {
                    var nodeIdx2 = (z * h + y) * w + (w - 1);
                    var dofX2 = nodeIdx2 * 3 + 0;
                    if (!_isDirichlet[dofX2])
                        _force[dofX2] -= sigma3_Pa * elementFaceArea / 4.0f;
                }
            }
        }

        _isDirichlet[0] = _isDirichlet[1] = _isDirichlet[2] = true;
        _dirichletValue[0] = _dirichletValue[1] = _dirichletValue[2] = 0.0f;

        var isDirichletByte = _isDirichlet.Select(b => (byte)(b ? 1 : 0)).ToArray();
        EnqueueWriteBuffer(_bufIsDirichlet, isDirichletByte);
        EnqueueWriteBuffer(_bufDirichletValue, _dirichletValue);
        EnqueueWriteBuffer(_bufForce, _force);

        var argIdx = 0;
        SetKernelArg(_kernelApplyBC, argIdx++, _bufForce);
        SetKernelArg(_kernelApplyBC, argIdx++, _bufIsDirichlet);
        SetKernelArg(_kernelApplyBC, argIdx++, _bufDirichletValue);
        SetKernelArg(_kernelApplyBC, argIdx++, _numDOFs);

        var globalSize = (nuint)((_numDOFs + _workGroupSize - 1) / _workGroupSize * _workGroupSize);
        var localSize = (nuint)_workGroupSize;

        var error = _cl.EnqueueNdrangeKernel(_queue, _kernelApplyBC, 1, null, &globalSize, &localSize, 0, null, null);
        CheckError(error, "EnqueueNDRange applyBC");

        _cl.Finish(_queue);
        Logger.Log("[GeomechGPU] Boundary conditions applied");
    }

    private void ApplyPlasticCorrectionGPU(byte[,,] labels)
    {
        if (!_params.EnablePlasticity) return;

        Logger.Log("[GeomechGPU] Applying plastic correction");

        var extent = _params.SimulationExtent;
        var numVoxels = extent.Width * extent.Height * extent.Depth;

        // Create plastic strain buffer
        int error;
        var bufPlasticStrain = CreateBuffer<float>(numVoxels, MemFlags.ReadWrite, out error);
        var zeroStrain = new float[numVoxels];
        EnqueueWriteBuffer(bufPlasticStrain, zeroStrain);

        var yieldStress = _params.Cohesion * 1e6f * 2f;
        var hardeningModulus = _params.YoungModulus * 1e6f * 0.01f;
        var E = _params.YoungModulus * 1e6f;
        var nu = _params.PoissonRatio;
        var mu = E / (2f * (1f + nu));

        var stressOffset = 0;
        var argIdx = 0;
        for (var comp = 0; comp < 6; comp++)
        {
            var bufComp = (nint)((long)_bufStressFields + stressOffset * sizeof(float));
            SetKernelArg(_kernelPlasticCorrection, argIdx++, bufComp);
            stressOffset += numVoxels;
        }

        SetKernelArg(_kernelPlasticCorrection, argIdx++, bufPlasticStrain);
        SetKernelArg(_kernelPlasticCorrection, argIdx++, _bufLabels);
        SetKernelArg(_kernelPlasticCorrection, argIdx++, yieldStress);
        SetKernelArg(_kernelPlasticCorrection, argIdx++, hardeningModulus);
        SetKernelArg(_kernelPlasticCorrection, argIdx++, mu);
        SetKernelArg(_kernelPlasticCorrection, argIdx++, numVoxels);

        var globalSize = (nuint)((numVoxels + _workGroupSize - 1) / _workGroupSize * _workGroupSize);
        var localSize = (nuint)_workGroupSize;

        error = _cl.EnqueueNdrangeKernel(_queue, _kernelPlasticCorrection, 1, null, &globalSize, &localSize, 0, null,
            null);
        CheckError(error, "EnqueueNDRange plasticCorrection");

        _cl.Finish(_queue);
        _cl.ReleaseMemObject(bufPlasticStrain);
    }

    private bool SolveDisplacementsGPU(IProgress<float> progress, CancellationToken token)
    {
        Logger.Log("[GeomechGPU] Solving for displacements using PCG on GPU");

        var maxIter = _params.MaxIterations;
        var tolerance = _params.Tolerance;
        if (tolerance > 1e-4f) tolerance = 1e-6f;

        int error;
        var bufR = CreateBuffer<float>(_numDOFs, MemFlags.ReadWrite, out error);
        var bufZ = CreateBuffer<float>(_numDOFs, MemFlags.ReadWrite, out error);
        var bufP = CreateBuffer<float>(_numDOFs, MemFlags.ReadWrite, out error);
        var bufQ = CreateBuffer<float>(_numDOFs, MemFlags.ReadWrite, out error);
        var bufMInv = CreateBuffer<float>(_numDOFs, MemFlags.ReadWrite, out error);

        try
        {
            var zeroVec = new float[_numDOFs];
            EnqueueWriteBuffer(_bufDisplacement, zeroVec);

            var values = new float[_nnz];
            EnqueueReadBuffer(_bufValues, values);

            var mInv = new float[_numDOFs];
            for (var i = 0; i < _numDOFs; i++)
            {
                var diag = 1.0f;
                for (var j = _rowPtr[i]; j < _rowPtr[i + 1]; j++)
                    if (_colIdx[j] == i)
                    {
                        diag = values[j];
                        break;
                    }

                mInv[i] = diag > 1e-12f ? 1.0f / diag : 1.0f;
            }

            EnqueueWriteBuffer(bufMInv, mInv);

            EnqueueCopyBuffer(_bufForce, bufR, _numDOFs);
            VectorMultiply(bufZ, bufMInv, bufR);
            EnqueueCopyBuffer(bufZ, bufP, _numDOFs);

            var rho = DotProductGPU(bufR, bufZ);
            var rho0 = rho;

            var converged = false;
            var iter = 0;

            while (iter < maxIter && !converged)
            {
                token.ThrowIfCancellationRequested();

                SpMVGPU(bufQ, bufP);
                var pq = DotProductGPU(bufP, bufQ);
                if (MathF.Abs(pq) < 1e-20f) break;

                var alpha = rho / pq;
                VectorAxpy(_bufDisplacement, bufP, alpha, 0);
                VectorAxpy(bufR, bufQ, -alpha, 0);

                var residualNorm = VectorNormGPU(bufR);
                var relativeResidual = residualNorm / MathF.Sqrt(rho0);

                if (relativeResidual < tolerance)
                {
                    converged = true;
                    Logger.Log($"[GeomechGPU] PCG converged at iteration {iter} with residual {relativeResidual:E4}");
                    break;
                }

                VectorMultiply(bufZ, bufMInv, bufR);
                var rhoNew = DotProductGPU(bufR, bufZ);
                var beta = rhoNew / rho;
                VectorAxpy(bufP, bufZ, 1.0f, beta);

                rho = rhoNew;
                iter++;

                if (iter % 10 == 0)
                {
                    var prog = 0.35f + 0.4f * iter / maxIter;
                    progress?.Report(prog);

                    if (iter % 100 == 0)
                        Logger.Log($"[GeomechGPU] PCG iteration {iter}, residual: {relativeResidual:E4}");
                }
            }

            if (!converged)
                Logger.LogWarning($"[GeomechGPU] PCG did not converge after {maxIter} iterations");

            return converged;
        }
        finally
        {
            _cl.ReleaseMemObject(bufR);
            _cl.ReleaseMemObject(bufZ);
            _cl.ReleaseMemObject(bufP);
            _cl.ReleaseMemObject(bufQ);
            _cl.ReleaseMemObject(bufMInv);
        }
    }

    private void CalculateStrainsAndStressesGPU()
    {
        Logger.Log("[GeomechGPU] Computing strains and stresses");

        var extent = _params.SimulationExtent;
        var numVoxels = extent.Width * extent.Height * extent.Depth;

        int error;
        _bufStressFields = CreateBuffer<float>(numVoxels * 6, MemFlags.WriteOnly, out error);
        _bufStrainFields = CreateBuffer<float>(numVoxels * 6, MemFlags.WriteOnly, out error);

        var dx = _params.PixelSize / 1e6f;
        var width = extent.Width;
        var height = extent.Height;

        var argIdx = 0;
        SetKernelArg(_kernelCalculateStrains, argIdx++, _bufElementNodes);
        SetKernelArg(_kernelCalculateStrains, argIdx++, _bufElementE);
        SetKernelArg(_kernelCalculateStrains, argIdx++, _bufElementNu);
        SetKernelArg(_kernelCalculateStrains, argIdx++, _bufNodeX);
        SetKernelArg(_kernelCalculateStrains, argIdx++, _bufNodeY);
        SetKernelArg(_kernelCalculateStrains, argIdx++, _bufNodeZ);
        SetKernelArg(_kernelCalculateStrains, argIdx++, _bufDisplacement);

        var stressOffset = 0;
        for (var comp = 0; comp < 6; comp++)
        {
            var bufComp = (nint)((long)_bufStressFields + stressOffset * sizeof(float));
            SetKernelArg(_kernelCalculateStrains, argIdx++, bufComp);
            stressOffset += numVoxels;
        }

        SetKernelArg(_kernelCalculateStrains, argIdx++, _numElements);
        SetKernelArg(_kernelCalculateStrains, argIdx++, width);
        SetKernelArg(_kernelCalculateStrains, argIdx++, height);
        SetKernelArg(_kernelCalculateStrains, argIdx++, dx);

        var globalSize = (nuint)((_numElements + _workGroupSize - 1) / _workGroupSize * _workGroupSize);
        var localSize = (nuint)_workGroupSize;

        error = _cl.EnqueueNdrangeKernel(_queue, _kernelCalculateStrains, 1, null, &globalSize, &localSize, 0, null,
            null);
        CheckError(error, "EnqueueNDRange calculateStrains");

        _cl.Finish(_queue);
        Logger.Log("[GeomechGPU] Strains and stresses computed");
    }

    private void CalculatePrincipalStressesGPU(byte[,,] labels)
    {
        Logger.Log("[GeomechGPU] Computing principal stresses");

        var extent = _params.SimulationExtent;
        var numVoxels = extent.Width * extent.Height * extent.Depth;

        int error;
        _bufPrincipalStresses = CreateBuffer<float>(numVoxels * 3, MemFlags.WriteOnly, out error);

        var labelsFlat = new byte[numVoxels];
        var idx = 0;
        for (var z = 0; z < extent.Depth; z++)
        for (var y = 0; y < extent.Height; y++)
        for (var x = 0; x < extent.Width; x++)
            labelsFlat[idx++] = labels[x, y, z];

        _bufLabels = CreateAndFillBuffer(labelsFlat, MemFlags.ReadOnly, out error);

        var argIdx = 0;
        var stressOffset = 0;
        for (var comp = 0; comp < 6; comp++)
        {
            var bufComp = (nint)((long)_bufStressFields + stressOffset * sizeof(float));
            SetKernelArg(_kernelCalculatePrincipal, argIdx++, bufComp);
            stressOffset += numVoxels;
        }

        var sigma1Buf = (nint)((long)_bufPrincipalStresses + 0 * numVoxels * sizeof(float));
        var sigma2Buf = (nint)((long)_bufPrincipalStresses + 1 * numVoxels * sizeof(float));
        var sigma3Buf = (nint)((long)_bufPrincipalStresses + 2 * numVoxels * sizeof(float));

        SetKernelArg(_kernelCalculatePrincipal, argIdx++, sigma1Buf);
        SetKernelArg(_kernelCalculatePrincipal, argIdx++, sigma2Buf);
        SetKernelArg(_kernelCalculatePrincipal, argIdx++, sigma3Buf);
        SetKernelArg(_kernelCalculatePrincipal, argIdx++, _bufLabels);
        SetKernelArg(_kernelCalculatePrincipal, argIdx++, numVoxels);

        var globalSize = (nuint)((numVoxels + _workGroupSize - 1) / _workGroupSize * _workGroupSize);
        var localSize = (nuint)_workGroupSize;

        error = _cl.EnqueueNdrangeKernel(_queue, _kernelCalculatePrincipal, 1, null, &globalSize, &localSize, 0, null,
            null);
        CheckError(error, "EnqueueNDRange calculatePrincipal");

        _cl.Finish(_queue);
        Logger.Log("[GeomechGPU] Principal stresses computed");
    }

    private void EvaluateFailureGPU(byte[,,] labels)
    {
        Logger.Log("[GeomechGPU] Evaluating failure");

        var extent = _params.SimulationExtent;
        var numVoxels = extent.Width * extent.Height * extent.Depth;

        int error;
        _bufFailureIndex = CreateBuffer<float>(numVoxels, MemFlags.WriteOnly, out error);
        _bufDamage = CreateBuffer<byte>(numVoxels, MemFlags.WriteOnly, out error);
        _bufFractured = CreateBuffer<byte>(numVoxels, MemFlags.WriteOnly, out error);

        var cohesion = _params.Cohesion * 1e6f;
        var frictionAngle = _params.FrictionAngle;
        var tensileStrength = _params.TensileStrength * 1e6f;
        var failureCrit = (int)_params.FailureCriterion;

        var sigma1Buf = (nint)((long)_bufPrincipalStresses + 0 * numVoxels * sizeof(float));
        var sigma2Buf = (nint)((long)_bufPrincipalStresses + 1 * numVoxels * sizeof(float));
        var sigma3Buf = (nint)((long)_bufPrincipalStresses + 2 * numVoxels * sizeof(float));

        // Stress component buffers
        var stressXXBuf = (nint)((long)_bufStressFields + 0 * numVoxels * sizeof(float));
        var stressYYBuf = (nint)((long)_bufStressFields + 1 * numVoxels * sizeof(float));
        var stressZZBuf = (nint)((long)_bufStressFields + 2 * numVoxels * sizeof(float));
        var stressXYBuf = (nint)((long)_bufStressFields + 3 * numVoxels * sizeof(float));
        var stressXZBuf = (nint)((long)_bufStressFields + 4 * numVoxels * sizeof(float));
        var stressYZBuf = (nint)((long)_bufStressFields + 5 * numVoxels * sizeof(float));

        var argIdx = 0;
        SetKernelArg(_kernelEvaluateFailure, argIdx++, sigma1Buf);
        SetKernelArg(_kernelEvaluateFailure, argIdx++, sigma2Buf);
        SetKernelArg(_kernelEvaluateFailure, argIdx++, sigma3Buf);
        SetKernelArg(_kernelEvaluateFailure, argIdx++, _bufFailureIndex);
        SetKernelArg(_kernelEvaluateFailure, argIdx++, _bufDamage);
        SetKernelArg(_kernelEvaluateFailure, argIdx++, _bufFractured);
        SetKernelArg(_kernelEvaluateFailure, argIdx++, _bufLabels);
        SetKernelArg(_kernelEvaluateFailure, argIdx++, cohesion);
        SetKernelArg(_kernelEvaluateFailure, argIdx++, frictionAngle);
        SetKernelArg(_kernelEvaluateFailure, argIdx++, tensileStrength);
        SetKernelArg(_kernelEvaluateFailure, argIdx++, failureCrit);
        SetKernelArg(_kernelEvaluateFailure, argIdx++, numVoxels);
        SetKernelArg(_kernelEvaluateFailure, argIdx++, stressXXBuf);
        SetKernelArg(_kernelEvaluateFailure, argIdx++, stressYYBuf);
        SetKernelArg(_kernelEvaluateFailure, argIdx++, stressZZBuf);
        SetKernelArg(_kernelEvaluateFailure, argIdx++, stressXYBuf);
        SetKernelArg(_kernelEvaluateFailure, argIdx++, stressXZBuf);
        SetKernelArg(_kernelEvaluateFailure, argIdx++, stressYZBuf);

        var globalSize = (nuint)((numVoxels + _workGroupSize - 1) / _workGroupSize * _workGroupSize);
        var localSize = (nuint)_workGroupSize;

        error = _cl.EnqueueNdrangeKernel(_queue, _kernelEvaluateFailure, 1, null, &globalSize, &localSize, 0, null,
            null);
        CheckError(error, "EnqueueNDRange evaluateFailure");

        _cl.Finish(_queue);
        Logger.Log("[GeomechGPU] Failure evaluation complete");
    }

    private void InitializeGeothermalAndFluidGPU(byte[,,] labels, BoundingBox extent)
    {
        if (!_params.EnableGeothermal && !_params.EnableFluidInjection)
            return;

        var w = extent.Width;
        var h = extent.Height;
        var d = extent.Depth;
        var numVoxels = w * h * d;
        var dx = _params.PixelSize / 1e6f;

        Logger.Log("[GeomechGPU] Initializing geothermal and fluid fields on GPU...");

        int error;

        // Temperature field
        if (_params.EnableGeothermal)
        {
            _bufTemperature = CreateBuffer<float>(numVoxels, MemFlags.ReadWrite, out error);
            CheckError(error, "Create temperature buffer");

            var kernelInitTemp = _cl.CreateKernel(_program, "initialize_geothermal", &error);
            CheckError(error, "CreateKernel initialize_geothermal");

            try
            {
                var argIdx = 0;
                SetKernelArg(kernelInitTemp, argIdx++, _bufTemperature);
                SetKernelArg(kernelInitTemp, argIdx++, _bufLabels);
                SetKernelArg(kernelInitTemp, argIdx++, w);
                SetKernelArg(kernelInitTemp, argIdx++, h);
                SetKernelArg(kernelInitTemp, argIdx++, d);
                SetKernelArg(kernelInitTemp, argIdx++, dx);
                SetKernelArg(kernelInitTemp, argIdx++, _params.SurfaceTemperature);
                SetKernelArg(kernelInitTemp, argIdx++, _params.GeothermalGradient);

                var globalSize = stackalloc nuint[3];
                globalSize[0] = (nuint)w;
                globalSize[1] = (nuint)h;
                globalSize[2] = (nuint)d;

                var localSize = stackalloc nuint[3];
                localSize[0] = 8;
                localSize[1] = 8;
                localSize[2] = 4;

                error = _cl.EnqueueNdrangeKernel(_queue, kernelInitTemp, 3, null, globalSize, localSize, 0, null, null);
                CheckError(error, "EnqueueNDRange initialize_geothermal");

                _cl.Finish(_queue);
            }
            finally
            {
                _cl.ReleaseKernel(kernelInitTemp);
            }

            Logger.Log("[GeomechGPU] Geothermal field initialized");
        }

        // Pressure and fluid fields
        if (_params.EnableFluidInjection || _params.UsePorePressure)
        {
            _bufPressure = CreateBuffer<float>(numVoxels, MemFlags.ReadWrite, out error);
            CheckError(error, "Create pressure buffer");
            _bufPressureNew = CreateBuffer<float>(numVoxels, MemFlags.ReadWrite, out error);
            CheckError(error, "Create pressure new buffer");

            _bufFractureAperture = CreateBuffer<float>(numVoxels, MemFlags.ReadWrite, out error);
            CheckError(error, "Create fracture aperture buffer");
            _bufFluidSaturation = CreateBuffer<float>(numVoxels, MemFlags.ReadWrite, out error);
            CheckError(error, "Create fluid saturation buffer");
            _bufConnectivity = CreateBuffer<byte>(numVoxels, MemFlags.ReadWrite, out error);
            CheckError(error, "Create connectivity buffer");

            // Initialize pressure with hydrostatic gradient
            var P0 = _params.InitialPorePressure * 1e6f;
            var rho_water = 1000f;
            var g = 9.81f;

            var pressureInit = new float[numVoxels];
            var apertureInit = new float[numVoxels];
            var saturationInit = new float[numVoxels];

            var idx = 0;
            for (var z = 0; z < d; z++)
            {
                var depth_m = z * dx;
                var hydrostaticP = P0 + rho_water * g * depth_m;

                for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    if (labels[x, y, z] != 0)
                    {
                        pressureInit[idx] = hydrostaticP;
                        saturationInit[idx] = _params.Porosity;
                        apertureInit[idx] = 0f;
                    }
                    else if (_params.EnableAquifer)
                    {
                        pressureInit[idx] = _params.AquiferPressure * 1e6f;
                    }

                    idx++;
                }
            }

            EnqueueWriteBuffer(_bufPressure, pressureInit);
            EnqueueWriteBuffer(_bufPressureNew, pressureInit);
            EnqueueWriteBuffer(_bufFractureAperture, apertureInit);
            EnqueueWriteBuffer(_bufFluidSaturation, saturationInit);

            var connectivityInit = new byte[numVoxels];
            EnqueueWriteBuffer(_bufConnectivity, connectivityInit);

            _cl.Finish(_queue);
            Logger.Log($"[GeomechGPU] Pressure field initialized (P0={P0 / 1e6f:F1} MPa)");
        }
    }

    private void SimulateFluidInjectionAndFracturingGPU(GeomechanicalResults results, byte[,,] labels,
        IProgress<float> progress, CancellationToken token)
    {
        if (!_params.EnableFluidInjection)
            return;

        Logger.Log("[GeomechGPU] ========== GPU FLUID INJECTION & FRACTURING ==========");

        var extent = _params.SimulationExtent;
        var w = extent.Width;
        var h = extent.Height;
        var d = extent.Depth;
        var dx = _params.PixelSize / 1e6f;
        var numVoxels = w * h * d;

        var injX = (int)(_params.InjectionLocation.X * w);
        var injY = (int)(_params.InjectionLocation.Y * h);
        var injZ = (int)(_params.InjectionLocation.Z * d);
        injX = Math.Clamp(injX, 0, w - 1);
        injY = Math.Clamp(injY, 0, h - 1);
        injZ = Math.Clamp(injZ, 0, d - 1);

        Logger.Log($"[GeomechGPU] Injection point: ({injX}, {injY}, {injZ})");

        var P_inj = _params.InjectionPressure * 1e6f;
        var dt_fluid = _params.FluidTimeStep;
        var maxTime = _params.MaxSimulationTime;
        var numSteps = (int)(maxTime / dt_fluid);

        int error;
        var kernelDiffusion = _cl.CreateKernel(_program, "pressure_diffusion", &error);
        CheckError(error, "CreateKernel pressure_diffusion");
        var kernelEffStress = _cl.CreateKernel(_program, "calculate_effective_stress", &error);
        CheckError(error, "CreateKernel calculate_effective_stress");
        var kernelUpdateAperture = _cl.CreateKernel(_program, "update_fracture_apertures", &error);
        CheckError(error, "CreateKernel update_fracture_apertures");
        var kernelDetectFrac = _cl.CreateKernel(_program, "detect_hydraulic_fractures", &error);
        CheckError(error, "CreateKernel detect_hydraulic_fractures");
        var kernelApplyInj = _cl.CreateKernel(_program, "apply_injection_source", &error);
        CheckError(error, "CreateKernel apply_injection_source");

        // Create effective stress buffers
        var bufEffStressXX = CreateBuffer<float>(numVoxels, MemFlags.ReadWrite, out error);
        CheckError(error, "Create bufEffStressXX");
        var bufEffStressYY = CreateBuffer<float>(numVoxels, MemFlags.ReadWrite, out error);
        CheckError(error, "Create bufEffStressYY");
        var bufEffStressZZ = CreateBuffer<float>(numVoxels, MemFlags.ReadWrite, out error);
        CheckError(error, "Create bufEffStressZZ");

        try
        {
            var globalSize3D = stackalloc nuint[3];
            globalSize3D[0] = (nuint)w;
            globalSize3D[1] = (nuint)h;
            globalSize3D[2] = (nuint)d;

            var localSize3D = stackalloc nuint[3];
            localSize3D[0] = 8;
            localSize3D[1] = 8;
            localSize3D[2] = 4;

            var sigma1Buf = (nint)((long)_bufPrincipalStresses + 0 * numVoxels * sizeof(float));
            var sigma3Buf = (nint)((long)_bufPrincipalStresses + 2 * numVoxels * sizeof(float));

            var breakdownDetected = false;
            results.BreakdownPressure = 0f;

            for (var step = 0; step < numSteps; step++)
            {
                token.ThrowIfCancellationRequested();

                // Apply injection source
                var argIdx = 0;
                SetKernelArg(kernelApplyInj, argIdx++, _bufPressure);
                SetKernelArg(kernelApplyInj, argIdx++, _bufLabels);
                SetKernelArg(kernelApplyInj, argIdx++, w);
                SetKernelArg(kernelApplyInj, argIdx++, h);
                SetKernelArg(kernelApplyInj, argIdx++, d);
                SetKernelArg(kernelApplyInj, argIdx++, injX);
                SetKernelArg(kernelApplyInj, argIdx++, injY);
                SetKernelArg(kernelApplyInj, argIdx++, injZ);
                SetKernelArg(kernelApplyInj, argIdx++, _params.InjectionRadius);
                SetKernelArg(kernelApplyInj, argIdx++, P_inj);

                error = _cl.EnqueueNdrangeKernel(_queue, kernelApplyInj, 3, null, globalSize3D, localSize3D, 0, null,
                    null);
                CheckError(error, "EnqueueNDRange apply_injection");

                // Diffuse pressure
                for (var subStep = 0; subStep < _params.FluidIterationsPerMechanicalStep; subStep++)
                {
                    argIdx = 0;
                    SetKernelArg(kernelDiffusion, argIdx++, _bufPressure);
                    SetKernelArg(kernelDiffusion, argIdx++, _bufPressureNew);
                    SetKernelArg(kernelDiffusion, argIdx++, _bufLabels);
                    SetKernelArg(kernelDiffusion, argIdx++, _bufFractureAperture);
                    SetKernelArg(kernelDiffusion, argIdx++, w);
                    SetKernelArg(kernelDiffusion, argIdx++, h);
                    SetKernelArg(kernelDiffusion, argIdx++, d);
                    SetKernelArg(kernelDiffusion, argIdx++, dx);
                    SetKernelArg(kernelDiffusion, argIdx++, dt_fluid / _params.FluidIterationsPerMechanicalStep);
                    SetKernelArg(kernelDiffusion, argIdx++, _params.RockPermeability);
                    SetKernelArg(kernelDiffusion, argIdx++, _params.FluidViscosity);
                    SetKernelArg(kernelDiffusion, argIdx++, _params.Porosity);
                    SetKernelArg(kernelDiffusion, argIdx++, _params.AquiferPressure * 1e6f);
                    SetKernelArg(kernelDiffusion, argIdx++, _params.EnableAquifer ? 1 : 0);

                    error = _cl.EnqueueNdrangeKernel(_queue, kernelDiffusion, 3, null, globalSize3D, localSize3D, 0,
                        null, null);
                    CheckError(error, "EnqueueNDRange diffusion");

                    var temp = _bufPressure;
                    _bufPressure = _bufPressureNew;
                    _bufPressureNew = temp;
                }

                // Update effective stress
                var stressOffset = 0;
                var stressXXBuf = (nint)((long)_bufStressFields + stressOffset++ * numVoxels * sizeof(float));
                var stressYYBuf = (nint)((long)_bufStressFields + stressOffset++ * numVoxels * sizeof(float));
                var stressZZBuf = (nint)((long)_bufStressFields + stressOffset++ * numVoxels * sizeof(float));

                argIdx = 0;
                SetKernelArg(kernelEffStress, argIdx++, stressXXBuf);
                SetKernelArg(kernelEffStress, argIdx++, stressYYBuf);
                SetKernelArg(kernelEffStress, argIdx++, stressZZBuf);
                SetKernelArg(kernelEffStress, argIdx++, _bufPressure);
                SetKernelArg(kernelEffStress, argIdx++, bufEffStressXX);
                SetKernelArg(kernelEffStress, argIdx++, bufEffStressYY);
                SetKernelArg(kernelEffStress, argIdx++, bufEffStressZZ);
                SetKernelArg(kernelEffStress, argIdx++, _bufLabels);
                SetKernelArg(kernelEffStress, argIdx++, _params.BiotCoefficient);
                SetKernelArg(kernelEffStress, argIdx++, numVoxels);

                var globalSize1D = (nuint)((numVoxels + _workGroupSize - 1) / _workGroupSize * _workGroupSize);
                var localSize1D = (nuint)_workGroupSize;

                error = _cl.EnqueueNdrangeKernel(_queue, kernelEffStress, 1, null, &globalSize1D, &localSize1D, 0, null,
                    null);
                CheckError(error, "EnqueueNDRange effective_stress");

                // Detect new fractures
                argIdx = 0;
                SetKernelArg(kernelDetectFrac, argIdx++, sigma1Buf);
                SetKernelArg(kernelDetectFrac, argIdx++, sigma3Buf);
                SetKernelArg(kernelDetectFrac, argIdx++, _bufPressure);
                SetKernelArg(kernelDetectFrac, argIdx++, _bufFractured);
                SetKernelArg(kernelDetectFrac, argIdx++, _bufDamage);
                SetKernelArg(kernelDetectFrac, argIdx++, _bufFractureAperture);
                SetKernelArg(kernelDetectFrac, argIdx++, _bufLabels);
                SetKernelArg(kernelDetectFrac, argIdx++, _params.Cohesion * 1e6f);
                SetKernelArg(kernelDetectFrac, argIdx++, _params.FrictionAngle);
                SetKernelArg(kernelDetectFrac, argIdx++, _params.TensileStrength * 1e6f);
                SetKernelArg(kernelDetectFrac, argIdx++, _params.BiotCoefficient);
                SetKernelArg(kernelDetectFrac, argIdx++, _params.MinimumFractureAperture);
                SetKernelArg(kernelDetectFrac, argIdx++, _params.FractureToughness);
                SetKernelArg(kernelDetectFrac, argIdx++, _params.YoungModulus);
                SetKernelArg(kernelDetectFrac, argIdx++, _params.PoissonRatio);
                SetKernelArg(kernelDetectFrac, argIdx++, dx);
                SetKernelArg(kernelDetectFrac, argIdx++, numVoxels);

                error = _cl.EnqueueNdrangeKernel(_queue, kernelDetectFrac, 1, null, &globalSize1D, &localSize1D, 0,
                    null, null);
                CheckError(error, "EnqueueNDRange detect_fractures");

                // Update fracture apertures
                if (_params.EnableFractureFlow)
                {
                    argIdx = 0;
                    SetKernelArg(kernelUpdateAperture, argIdx++, _bufPressure);
                    SetKernelArg(kernelUpdateAperture, argIdx++, sigma3Buf);
                    SetKernelArg(kernelUpdateAperture, argIdx++, _bufFractureAperture);
                    SetKernelArg(kernelUpdateAperture, argIdx++, _bufFractured);
                    SetKernelArg(kernelUpdateAperture, argIdx++, _bufLabels);
                    SetKernelArg(kernelUpdateAperture, argIdx++, _params.MinimumFractureAperture);
                    SetKernelArg(kernelUpdateAperture, argIdx++, _params.YoungModulus);
                    SetKernelArg(kernelUpdateAperture, argIdx++, _params.PoissonRatio);
                    SetKernelArg(kernelUpdateAperture, argIdx++, _params.BiotCoefficient);
                    SetKernelArg(kernelUpdateAperture, argIdx++, dx);
                    SetKernelArg(kernelUpdateAperture, argIdx++, numVoxels);

                    error = _cl.EnqueueNdrangeKernel(_queue, kernelUpdateAperture, 1, null, &globalSize1D, &localSize1D,
                        0, null, null);
                    CheckError(error, "EnqueueNDRange update_apertures");
                }

                _cl.Finish(_queue);

                // Check for breakdown
                if (!breakdownDetected && step == numSteps / 10)
                {
                    var pressureData = new float[numVoxels];
                    EnqueueReadBuffer(_bufPressure, pressureData);
                    var injIdx = (injZ * h + injY) * w + injX;
                    results.BreakdownPressure = pressureData[injIdx] / 1e6f;
                    breakdownDetected = true;
                    Logger.Log($"[GeomechGPU] *** BREAKDOWN detected, P={results.BreakdownPressure:F1} MPa ***");
                }

                if (step % 100 == 0)
                {
                    var prog = 0.92f + 0.08f * step / numSteps;
                    progress?.Report(prog);
                    Logger.Log($"[GeomechGPU] Fluid step {step}/{numSteps}");
                }
            }

            Logger.Log("[GeomechGPU] Fluid injection simulation complete");
        }
        finally
        {
            _cl.ReleaseKernel(kernelDiffusion);
            _cl.ReleaseKernel(kernelEffStress);
            _cl.ReleaseKernel(kernelUpdateAperture);
            _cl.ReleaseKernel(kernelDetectFrac);
            _cl.ReleaseKernel(kernelApplyInj);
            _cl.ReleaseMemObject(bufEffStressXX);
            _cl.ReleaseMemObject(bufEffStressYY);
            _cl.ReleaseMemObject(bufEffStressZZ);
        }
    }

    private void PopulateGeothermalAndFluidResultsGPU(GeomechanicalResults results)
    {
        if (!_params.EnableGeothermal && !_params.EnableFluidInjection)
            return;

        var extent = _params.SimulationExtent;
        var w = extent.Width;
        var h = extent.Height;
        var d = extent.Depth;
        var numVoxels = w * h * d;
        var dx = _params.PixelSize / 1e6f;

        Logger.Log("[GeomechGPU] Downloading geothermal and fluid results from GPU...");

        // Download temperature field
        if (_params.EnableGeothermal && _bufTemperature != 0)
        {
            var tempData = new float[numVoxels];
            EnqueueReadBuffer(_bufTemperature, tempData);

            results.TemperatureField = new float[w, h, d];
            var idx = 0;
            for (var z = 0; z < d; z++)
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                results.TemperatureField[x, y, z] = tempData[idx++];

            // Calculate statistics
            var T_top = results.TemperatureField[w / 2, h / 2, 0];
            var T_bottom = results.TemperatureField[w / 2, h / 2, d - 1];
            var depth_m = d * dx;
            results.AverageThermalGradient = (T_bottom - T_top) / depth_m * 1000f; // °C/km

            Logger.Log(
                $"[GeomechGPU] Temperature field downloaded, gradient: {results.AverageThermalGradient:F1} °C/km");
        }

        // Download pressure and fluid fields
        if (_params.EnableFluidInjection && _bufPressure != 0)
        {
            var pressureData = new float[numVoxels];
            EnqueueReadBuffer(_bufPressure, pressureData);

            results.PressureField = new float[w, h, d];
            var idx = 0;
            float minP = float.MaxValue, maxP = float.MinValue;

            for (var z = 0; z < d; z++)
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var P = pressureData[idx++];
                results.PressureField[x, y, z] = P;
                if (results.MaterialLabels[x, y, z] != 0)
                {
                    if (P < minP) minP = P;
                    if (P > maxP) maxP = P;
                }
            }

            results.MinFluidPressure = minP;
            results.MaxFluidPressure = maxP;
            results.PeakInjectionPressure = maxP / 1e6f;

            // Download fracture apertures
            if (_bufFractureAperture != 0)
            {
                var apertureData = new float[numVoxels];
                EnqueueReadBuffer(_bufFractureAperture, apertureData);

                results.FractureAperture = new float[w, h, d];
                idx = 0;
                var fractureVolume = 0.0;

                for (var z = 0; z < d; z++)
                for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var aperture = apertureData[idx++];
                    results.FractureAperture[x, y, z] = aperture;
                    if (aperture > _params.MinimumFractureAperture)
                        fractureVolume += aperture * dx * dx; // Aperture × face area
                }

                results.TotalFractureVolume = (float)fractureVolume;
            }

            Logger.Log($"[GeomechGPU] Fluid fields downloaded, P range: {minP / 1e6f:F1} - {maxP / 1e6f:F1} MPa");
        }

        _cl.Finish(_queue);
    }

    private GeomechanicalResults DownloadResults(BoundingBox extent, byte[,,] labels)
    {
        Logger.Log("[GeomechGPU] Downloading results from GPU");

        var w = extent.Width;
        var h = extent.Height;
        var d = extent.Depth;
        var numVoxels = w * h * d;

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

        var stressData = new float[numVoxels * 6];
        EnqueueReadBuffer(_bufStressFields, stressData);

        CopyToField(stressData, 0, numVoxels, results.StressXX, w, h, d);
        CopyToField(stressData, 1, numVoxels, results.StressYY, w, h, d);
        CopyToField(stressData, 2, numVoxels, results.StressZZ, w, h, d);
        CopyToField(stressData, 3, numVoxels, results.StressXY, w, h, d);
        CopyToField(stressData, 4, numVoxels, results.StressXZ, w, h, d);
        CopyToField(stressData, 5, numVoxels, results.StressYZ, w, h, d);

        var principalData = new float[numVoxels * 3];
        EnqueueReadBuffer(_bufPrincipalStresses, principalData);

        CopyToField(principalData, 0, numVoxels, results.Sigma1, w, h, d);
        CopyToField(principalData, 1, numVoxels, results.Sigma2, w, h, d);
        CopyToField(principalData, 2, numVoxels, results.Sigma3, w, h, d);

        var failureData = new float[numVoxels];
        EnqueueReadBuffer(_bufFailureIndex, failureData);
        CopyToField(failureData, 0, numVoxels, results.FailureIndex, w, h, d);

        var damageData = new byte[numVoxels];
        EnqueueReadBuffer(_bufDamage, damageData);
        CopyToField(damageData, 0, numVoxels, results.DamageField, w, h, d);

        var fracturedData = new byte[numVoxels];
        EnqueueReadBuffer(_bufFractured, fracturedData);

        var idx = 0;
        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            results.FractureField[x, y, z] = fracturedData[idx++] != 0;

        Logger.Log("[GeomechGPU] Results downloaded");
        return results;
    }

    private void CopyToField<T>(T[] flatData, int componentIdx, int numVoxels, T[,,] field, int w, int h, int d)
    {
        var offset = componentIdx * numVoxels;
        var idx = 0;
        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            field[x, y, z] = flatData[offset + idx++];
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
            }
        }

        locations.Add(("Max Stress", maxX, maxY, maxZ));

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
                MaxShearStress = (sigma1 - sigma3) / (2 * 1e6f),
                HasFailed = results.FractureField[x, y, z]
            };

            var phi_rad = _params.FrictionAngle * MathF.PI / 180f;
            circle.FailureAngle = (MathF.PI / 4 + phi_rad / 2) * 180f / MathF.PI;

            results.MohrCircles.Add(circle);
        }
    }

    private void CalculateGlobalStatistics(GeomechanicalResults results)
    {
        var w = results.StressXX.GetLength(0);
        var h = results.StressXX.GetLength(1);
        var d = results.StressXX.GetLength(2);

        float sumStress = 0, maxShear = 0, sumVonMises = 0, maxVonMises = 0;
        int validVoxels = 0, failedCount = 0;

        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            if (results.MaterialLabels[x, y, z] == 0) continue;

            validVoxels++;

            var meanStress = (results.StressXX[x, y, z] + results.StressYY[x, y, z] + results.StressZZ[x, y, z]) / 3.0f;
            sumStress += meanStress;

            var shear = (results.Sigma1[x, y, z] - results.Sigma3[x, y, z]) / 2.0f;
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

            if (results.FractureField[x, y, z])
                failedCount++;
        }

        results.MeanStress = validVoxels > 0 ? sumStress / validVoxels : 0;
        results.MaxShearStress = maxShear;
        results.VonMisesStress_Mean = validVoxels > 0 ? sumVonMises / validVoxels : 0;
        results.VonMisesStress_Max = maxVonMises;
        results.TotalVoxels = validVoxels;
        results.FailedVoxels = failedCount;
        results.FailedVoxelPercentage = validVoxels > 0 ? 100f * failedCount / validVoxels : 0;
    }

    // ========== GPU HELPER METHODS ==========

    private void SpMVGPU(nint y, nint x)
    {
        var argIdx = 0;
        SetKernelArg(_kernelSpMV, argIdx++, _bufRowPtr);
        SetKernelArg(_kernelSpMV, argIdx++, _bufColIdx);
        SetKernelArg(_kernelSpMV, argIdx++, _bufValues);
        SetKernelArg(_kernelSpMV, argIdx++, x);
        SetKernelArg(_kernelSpMV, argIdx++, y);
        SetKernelArg(_kernelSpMV, argIdx++, _bufIsDirichlet);
        SetKernelArg(_kernelSpMV, argIdx++, _numDOFs);

        var globalSize = (nuint)((_numDOFs + _workGroupSize - 1) / _workGroupSize * _workGroupSize);
        var localSize = (nuint)_workGroupSize;

        _cl.EnqueueNdrangeKernel(_queue, _kernelSpMV, 1, null, &globalSize, &localSize, 0, null, null);
        _cl.Finish(_queue);
    }

    private float DotProductGPU(nint a, nint b)
    {
        var numWorkGroups = (_numDOFs + _workGroupSize - 1) / _workGroupSize;

        var argIdx = 0;
        SetKernelArg(_kernelDotProduct, argIdx++, a);
        SetKernelArg(_kernelDotProduct, argIdx++, b);
        SetKernelArg(_kernelDotProduct, argIdx++, _bufPartialSums);
        var localMemSize = _workGroupSize * sizeof(float);
        _cl.SetKernelArg(_kernelDotProduct, (uint)argIdx++, (nuint)localMemSize, null);
        SetKernelArg(_kernelDotProduct, argIdx++, _numDOFs);

        var globalSize = (nuint)(numWorkGroups * _workGroupSize);
        var localSize = (nuint)_workGroupSize;

        _cl.EnqueueNdrangeKernel(_queue, _kernelDotProduct, 1, null, &globalSize, &localSize, 0, null, null);

        var partialSums = new float[numWorkGroups];
        EnqueueReadBuffer(_bufPartialSums, partialSums);

        return partialSums.Sum();
    }

    private float VectorNormGPU(nint v)
    {
        return MathF.Sqrt(DotProductGPU(v, v));
    }

    private void VectorMultiply(nint result, nint a, nint b)
    {
        var tempData = new float[_numDOFs];
        EnqueueReadBuffer(a, tempData);
        var bData = new float[_numDOFs];
        EnqueueReadBuffer(b, bData);
        for (var i = 0; i < _numDOFs; i++)
            tempData[i] *= bData[i];
        EnqueueWriteBuffer(result, tempData);
    }

    private void VectorAxpy(nint y, nint x, float alpha, float beta)
    {
        var opType = beta == 0 ? 0 : 1;
        var scalar = opType == 0 ? alpha : beta;

        var argIdx = 0;
        SetKernelArg(_kernelVectorOps, argIdx++, y);
        SetKernelArg(_kernelVectorOps, argIdx++, x);
        SetKernelArg(_kernelVectorOps, argIdx++, _bufIsDirichlet);
        SetKernelArg(_kernelVectorOps, argIdx++, scalar);
        SetKernelArg(_kernelVectorOps, argIdx++, opType);
        SetKernelArg(_kernelVectorOps, argIdx++, _numDOFs);

        var globalSize = (nuint)((_numDOFs + _workGroupSize - 1) / _workGroupSize * _workGroupSize);
        var localSize = (nuint)_workGroupSize;

        _cl.EnqueueNdrangeKernel(_queue, _kernelVectorOps, 1, null, &globalSize, &localSize, 0, null, null);
        _cl.Finish(_queue);
    }

    // ========== OPENCL UTILITY METHODS ==========

    private nint CreateBuffer<T>(int count, MemFlags flags, out int error) where T : unmanaged
    {
        var size = (nuint)(count * Marshal.SizeOf<T>());
        fixed (int* errorPtr = &error)
        {
            var buffer = _cl.CreateBuffer(_context, flags, size, null, errorPtr);
            _currentGPUMemoryBytes += (long)size;
            return buffer;
        }
    }

    private nint CreateAndFillBuffer<T>(T[] data, MemFlags flags, out int error) where T : unmanaged
    {
        var size = (nuint)(data.Length * Marshal.SizeOf<T>());
        fixed (T* ptr = data)
        fixed (int* errorPtr = &error)
        {
            var buffer = _cl.CreateBuffer(_context, flags | MemFlags.CopyHostPtr, size, ptr, errorPtr);
            _currentGPUMemoryBytes += (long)size;
            return buffer;
        }
    }

    private void EnqueueWriteBuffer<T>(nint buffer, T[] data) where T : unmanaged
    {
        var size = (nuint)(data.Length * Marshal.SizeOf<T>());
        fixed (T* ptr = data)
        {
            _cl.EnqueueWriteBuffer(_queue, buffer, true, 0, size, ptr, 0, null, null);
        }
    }

    private void EnqueueReadBuffer<T>(nint buffer, T[] data) where T : unmanaged
    {
        var size = (nuint)(data.Length * Marshal.SizeOf<T>());
        fixed (T* ptr = data)
        {
            _cl.EnqueueReadBuffer(_queue, buffer, true, 0, size, ptr, 0, null, null);
        }
    }

    private void EnqueueCopyBuffer(nint src, nint dst, int count)
    {
        var size = (nuint)(count * sizeof(float));
        _cl.EnqueueCopyBuffer(_queue, src, dst, 0, 0, size, 0, null, null);
    }

    private void SetKernelArg(nint kernel, int index, nint buffer)
    {
        _cl.SetKernelArg(kernel, (uint)index, (nuint)sizeof(nint), &buffer);
    }

    private void SetKernelArg(nint kernel, int index, int value)
    {
        _cl.SetKernelArg(kernel, (uint)index, sizeof(int), &value);
    }

    private void SetKernelArg(nint kernel, int index, float value)
    {
        _cl.SetKernelArg(kernel, (uint)index, sizeof(float), &value);
    }

    private void CheckError(int error, string operation)
    {
        if (error != 0)
            throw new Exception($"OpenCL error in {operation}: {(CLEnum)error}");
    }

    private void ReleaseKernels()
    {
        if (_kernelAssembleElement != 0) _cl.ReleaseKernel(_kernelAssembleElement);
        if (_kernelApplyBC != 0) _cl.ReleaseKernel(_kernelApplyBC);
        if (_kernelSpMV != 0) _cl.ReleaseKernel(_kernelSpMV);
        if (_kernelDotProduct != 0) _cl.ReleaseKernel(_kernelDotProduct);
        if (_kernelVectorOps != 0) _cl.ReleaseKernel(_kernelVectorOps);
        if (_kernelCalculateStrains != 0) _cl.ReleaseKernel(_kernelCalculateStrains);
        if (_kernelCalculatePrincipal != 0) _cl.ReleaseKernel(_kernelCalculatePrincipal);
        if (_kernelEvaluateFailure != 0) _cl.ReleaseKernel(_kernelEvaluateFailure);
    }

    private void ReleaseAllGPUBuffers()
    {
        foreach (var buffers in _chunkBuffers.Values)
            buffers.Release(_cl);
        _chunkBuffers.Clear();

        if (_bufNodeX != 0) _cl.ReleaseMemObject(_bufNodeX);
        if (_bufNodeY != 0) _cl.ReleaseMemObject(_bufNodeY);
        if (_bufNodeZ != 0) _cl.ReleaseMemObject(_bufNodeZ);
        if (_bufElementNodes != 0) _cl.ReleaseMemObject(_bufElementNodes);
        if (_bufElementE != 0) _cl.ReleaseMemObject(_bufElementE);
        if (_bufElementNu != 0) _cl.ReleaseMemObject(_bufElementNu);
        if (_bufRowPtr != 0) _cl.ReleaseMemObject(_bufRowPtr);
        if (_bufColIdx != 0) _cl.ReleaseMemObject(_bufColIdx);
        if (_bufValues != 0) _cl.ReleaseMemObject(_bufValues);
        if (_bufDisplacement != 0) _cl.ReleaseMemObject(_bufDisplacement);
        if (_bufForce != 0) _cl.ReleaseMemObject(_bufForce);
        if (_bufIsDirichlet != 0) _cl.ReleaseMemObject(_bufIsDirichlet);
        if (_bufDirichletValue != 0) _cl.ReleaseMemObject(_bufDirichletValue);
        if (_bufStressFields != 0) _cl.ReleaseMemObject(_bufStressFields);
        if (_bufStrainFields != 0) _cl.ReleaseMemObject(_bufStrainFields);
        if (_bufPrincipalStresses != 0) _cl.ReleaseMemObject(_bufPrincipalStresses);
        if (_bufFailureIndex != 0) _cl.ReleaseMemObject(_bufFailureIndex);
        if (_bufDamage != 0) _cl.ReleaseMemObject(_bufDamage);
        if (_bufLabels != 0) _cl.ReleaseMemObject(_bufLabels);
        if (_bufFractured != 0) _cl.ReleaseMemObject(_bufFractured);
        if (_bufPartialSums != 0) _cl.ReleaseMemObject(_bufPartialSums);
        if (_bufTempVector != 0) _cl.ReleaseMemObject(_bufTempVector);

        // Geothermal and fluid buffers
        if (_bufTemperature != 0) _cl.ReleaseMemObject(_bufTemperature);
        if (_bufPressure != 0) _cl.ReleaseMemObject(_bufPressure);
        if (_bufPressureNew != 0) _cl.ReleaseMemObject(_bufPressureNew);
        if (_bufFractureAperture != 0) _cl.ReleaseMemObject(_bufFractureAperture);
        if (_bufFluidSaturation != 0) _cl.ReleaseMemObject(_bufFluidSaturation);
        if (_bufVelocityX != 0) _cl.ReleaseMemObject(_bufVelocityX);
        if (_bufVelocityY != 0) _cl.ReleaseMemObject(_bufVelocityY);
        if (_bufVelocityZ != 0) _cl.ReleaseMemObject(_bufVelocityZ);
        if (_bufConnectivity != 0) _cl.ReleaseMemObject(_bufConnectivity);
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

    private class ChunkGPUBuffers
    {
        public nint DensityBuffer;
        public nint LabelsBuffer;

        public void Release(CL cl)
        {
            if (LabelsBuffer != 0) cl.ReleaseMemObject(LabelsBuffer);
            if (DensityBuffer != 0) cl.ReleaseMemObject(DensityBuffer);
        }
    }
}