// GeoscientistToolkit/Analysis/TextureClassification/TextureClassifier.cs

using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using System.Text;
using GeoscientistToolkit.Util;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Analysis.TextureClassification;

public class TextureClassifier : IDisposable
{
    private readonly bool _useGPU;
    private CL _cl;

    private List<float[]> _classFeatures;
    private nint _commandQueue;
    private nint _context;

    private bool _disposed;
    private int _featureDimension;
    private bool _gpuInitialized;
    private nint _kernel;
    private nint _program;

    public TextureClassifier(bool useGPU)
    {
        _useGPU = useGPU && IsGPUAvailable();

        if (_useGPU) InitializeGPU();
    }

    public bool IsTrained { get; private set; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_gpuInitialized)
        {
            if (_kernel != IntPtr.Zero) _cl.ReleaseKernel(_kernel);
            if (_program != IntPtr.Zero) _cl.ReleaseProgram(_program);
            if (_commandQueue != IntPtr.Zero) _cl.ReleaseCommandQueue(_commandQueue);
            if (_context != IntPtr.Zero) _cl.ReleaseContext(_context);
        }
    }

    public static bool IsGPUAvailable()
    {
        try
        {
            // Use centralized device manager to check availability
            var device = GeoscientistToolkit.OpenCL.OpenCLDeviceManager.GetComputeDevice();
            return device != 0;
        }
        catch
        {
            return false;
        }
    }

    public void Train(List<TrainingPatch> patches, int patchSize, Action<float> progressCallback)
    {
        if (patches == null || patches.Count == 0)
        {
            Logger.LogWarning("[TextureClassifier] No patches provided for training");
            return;
        }

        Logger.Log($"[TextureClassifier] Training with {patches.Count} patches, size {patchSize}x{patchSize}");

        // Extract features from all patches
        _classFeatures = new List<float[]>();
        _featureDimension = patchSize * patchSize; // Simple intensity features, can be extended

        for (var i = 0; i < patches.Count; i++)
        {
            var patch = patches[i];
            var features = ExtractFeatures(patch.Data, patchSize);
            _classFeatures.Add(features);

            progressCallback?.Invoke((float)(i + 1) / patches.Count);
        }

        IsTrained = true;
        Logger.Log($"[TextureClassifier] Training completed with {_classFeatures.Count} feature vectors");
    }

    public float[] ClassifySlice(byte[] sliceData, int width, int height, int patchSize)
    {
        if (!IsTrained)
        {
            Logger.LogWarning("[TextureClassifier] Classifier not trained");
            return new float[width * height];
        }

        var predictions = new float[width * height];

        if (_useGPU && _gpuInitialized)
            ClassifySliceGPU(sliceData, width, height, patchSize, predictions);
        else
            ClassifySliceSIMD(sliceData, width, height, patchSize, predictions);

        return predictions;
    }

    private void ClassifySliceGPU(byte[] sliceData, int width, int height, int patchSize, float[] predictions)
    {
        try
        {
            unsafe
            {
                int errorCode;
                var halfSize = patchSize / 2;

                // Create buffers with correct types
                fixed (byte* slicePtr = sliceData)
                fixed (float* predPtr = predictions)
                fixed (float* featPtr = FlattenFeatures())
                {
                    var sliceBuffer = _cl.CreateBuffer(_context,
                        MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                        (nuint)(sliceData.Length * sizeof(byte)),
                        slicePtr, &errorCode);

                    if (errorCode != 0)
                        throw new Exception($"Failed to create slice buffer: {errorCode}");

                    var predBuffer = _cl.CreateBuffer(_context,
                        MemFlags.WriteOnly,
                        (nuint)(predictions.Length * sizeof(float)),
                        null, &errorCode);

                    if (errorCode != 0)
                        throw new Exception($"Failed to create prediction buffer: {errorCode}");

                    var featBuffer = _cl.CreateBuffer(_context,
                        MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                        (nuint)(_classFeatures.Count * _featureDimension * sizeof(float)),
                        featPtr, &errorCode);

                    if (errorCode != 0)
                        throw new Exception($"Failed to create feature buffer: {errorCode}");

                    // Set kernel arguments
                    _cl.SetKernelArg(_kernel, 0, (nuint)sizeof(nint), &sliceBuffer);
                    _cl.SetKernelArg(_kernel, 1, (nuint)sizeof(nint), &predBuffer);
                    _cl.SetKernelArg(_kernel, 2, (nuint)sizeof(nint), &featBuffer);
                    _cl.SetKernelArg(_kernel, 3, sizeof(int), &width);
                    _cl.SetKernelArg(_kernel, 4, sizeof(int), &height);
                    _cl.SetKernelArg(_kernel, 5, sizeof(int), &patchSize);
                    var numFeatures = _classFeatures.Count;
                    _cl.SetKernelArg(_kernel, 6, sizeof(int), &numFeatures);

                    // Execute kernel
                    var globalWorkSize = (nuint)(width * height);
                    _cl.EnqueueNdrangeKernel(_commandQueue, _kernel, 1, null, &globalWorkSize, null, 0, null, null);
                    _cl.Finish(_commandQueue);

                    // Read results
                    _cl.EnqueueReadBuffer(_commandQueue, predBuffer, true, 0,
                        (nuint)(predictions.Length * sizeof(float)),
                        predPtr, 0, null, null);

                    // Clean up
                    _cl.ReleaseMemObject(sliceBuffer);
                    _cl.ReleaseMemObject(predBuffer);
                    _cl.ReleaseMemObject(featBuffer);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[TextureClassifier] GPU classification failed: {ex.Message}, falling back to SIMD");
            ClassifySliceSIMD(sliceData, width, height, patchSize, predictions);
        }
    }

    private void ClassifySliceSIMD(byte[] sliceData, int width, int height, int patchSize, float[] predictions)
    {
        var halfSize = patchSize / 2;

        Parallel.For(halfSize, height - halfSize, y =>
        {
            for (var x = halfSize; x < width - halfSize; x++)
            {
                var patch = ExtractPatchAt(sliceData, width, height, x, y, patchSize);
                var features = ExtractFeatures(patch, patchSize);
                var confidence = ComputeConfidenceSIMD(features);
                predictions[y * width + x] = confidence;
            }
        });
    }

    private float ComputeConfidenceSIMD(float[] features)
    {
        var maxSimilarity = 0f;

        // Use hardware intrinsics for faster computation
        if (Avx2.IsSupported && features.Length >= 8)
            maxSimilarity = ComputeSimilarityAVX2(features);
        else if (AdvSimd.IsSupported && features.Length >= 4)
            maxSimilarity = ComputeSimilarityNEON(features);
        else
            maxSimilarity = ComputeSimilarityScalar(features);

        return maxSimilarity;
    }

    private float ComputeSimilarityAVX2(float[] features)
    {
        var maxSim = 0f;

        foreach (var classFeature in _classFeatures)
        {
            var similarity = 0f;
            var i = 0;

            // Process 8 floats at a time with AVX2
            for (; i <= features.Length - 8; i += 8)
            {
                var v1 = Vector256.Create(features[i], features[i + 1], features[i + 2], features[i + 3],
                    features[i + 4], features[i + 5], features[i + 6], features[i + 7]);
                var v2 = Vector256.Create(classFeature[i], classFeature[i + 1], classFeature[i + 2],
                    classFeature[i + 3],
                    classFeature[i + 4], classFeature[i + 5], classFeature[i + 6], classFeature[i + 7]);

                var diff = Avx2.Subtract(v1, v2);
                var squared = Avx2.Multiply(diff, diff);

                for (var j = 0; j < 8; j++) similarity += squared.GetElement(j);
            }

            // Handle remaining elements
            for (; i < features.Length; i++)
            {
                var diff = features[i] - classFeature[i];
                similarity += diff * diff;
            }

            similarity = 1.0f / (1.0f + (float)Math.Sqrt(similarity));
            maxSim = Math.Max(maxSim, similarity);
        }

        return maxSim;
    }

    private float ComputeSimilarityNEON(float[] features)
    {
        var maxSim = 0f;

        foreach (var classFeature in _classFeatures)
        {
            var similarity = 0f;
            var i = 0;

            // Process 4 floats at a time with NEON
            for (; i <= features.Length - 4; i += 4)
            {
                var v1 = Vector128.Create(features[i], features[i + 1], features[i + 2], features[i + 3]);
                var v2 = Vector128.Create(classFeature[i], classFeature[i + 1], classFeature[i + 2],
                    classFeature[i + 3]);

                var diff = AdvSimd.Subtract(v1, v2);
                var squared = AdvSimd.Multiply(diff, diff);

                for (var j = 0; j < 4; j++) similarity += squared.GetElement(j);
            }

            // Handle remaining elements
            for (; i < features.Length; i++)
            {
                var diff = features[i] - classFeature[i];
                similarity += diff * diff;
            }

            similarity = 1.0f / (1.0f + (float)Math.Sqrt(similarity));
            maxSim = Math.Max(maxSim, similarity);
        }

        return maxSim;
    }

    private float ComputeSimilarityScalar(float[] features)
    {
        var maxSim = 0f;

        foreach (var classFeature in _classFeatures)
        {
            var similarity = 0f;

            for (var i = 0; i < features.Length; i++)
            {
                var diff = features[i] - classFeature[i];
                similarity += diff * diff;
            }

            similarity = 1.0f / (1.0f + (float)Math.Sqrt(similarity));
            maxSim = Math.Max(maxSim, similarity);
        }

        return maxSim;
    }

    private byte[] ExtractPatchAt(byte[] sliceData, int width, int height, int centerX, int centerY, int patchSize)
    {
        var halfSize = patchSize / 2;
        var patch = new byte[patchSize * patchSize];

        for (var py = 0; py < patchSize; py++)
        for (var px = 0; px < patchSize; px++)
        {
            var sx = centerX - halfSize + px;
            var sy = centerY - halfSize + py;

            if (sx >= 0 && sx < width && sy >= 0 && sy < height)
                patch[py * patchSize + px] = sliceData[sy * width + sx];
        }

        return patch;
    }

    private float[] ExtractFeatures(byte[] patch, int patchSize)
    {
        var features = new float[patchSize * patchSize];

        // Normalize to [0, 1]
        for (var i = 0; i < patch.Length; i++) features[i] = patch[i] / 255.0f;

        return features;
    }

    private float[] FlattenFeatures()
    {
        var flattened = new float[_classFeatures.Count * _featureDimension];
        for (var i = 0; i < _classFeatures.Count; i++)
            Array.Copy(_classFeatures[i], 0, flattened, i * _featureDimension, _featureDimension);
        return flattened;
    }

    private void InitializeGPU()
    {
        try
        {
            _cl = CL.GetApi();
            unsafe
            {
                // Use centralized device manager to get the device from settings
                var device = GeoscientistToolkit.OpenCL.OpenCLDeviceManager.GetComputeDevice();

                if (device == 0)
                {
                    Logger.LogWarning("[TextureClassifier] No OpenCL device available from OpenCLDeviceManager.");
                    return;
                }

                // Get device info from the centralized manager
                var deviceInfo = GeoscientistToolkit.OpenCL.OpenCLDeviceManager.GetDeviceInfo();
                Logger.Log($"[TextureClassifier] Using device: {deviceInfo.Name} ({deviceInfo.Vendor})");

                int errorCode;
                _context = _cl.CreateContext(null, 1, &device, null, null, &errorCode);

                if (errorCode != 0)
                {
                    Logger.LogError($"[TextureClassifier] Failed to create context: {errorCode}");
                    return;
                }

                _commandQueue = _cl.CreateCommandQueue(_context, device, CommandQueueProperties.None, &errorCode);

                if (errorCode != 0)
                {
                    Logger.LogError($"[TextureClassifier] Failed to create command queue: {errorCode}");
                    return;
                }

                // Create and build program with kernel
                var kernelSource = GetKernelSource();
                var sourceLength = (nuint)kernelSource.Length;

                fixed (byte* sourceBytes = Encoding.ASCII.GetBytes(kernelSource))
                {
                    var sourcePtrs = stackalloc byte*[1];
                    sourcePtrs[0] = sourceBytes;
                    var lengths = stackalloc nuint[1];
                    lengths[0] = sourceLength;

                    _program = _cl.CreateProgramWithSource(_context, 1, sourcePtrs, lengths, &errorCode);

                    if (errorCode != 0)
                    {
                        Logger.LogError($"[TextureClassifier] Failed to create program: {errorCode}");
                        return;
                    }
                }

                // Build program using byte* version
                errorCode = _cl.BuildProgram(_program, 1, &device, (byte*)null, null, null);

                if (errorCode != 0)
                {
                    Logger.LogError($"[TextureClassifier] Failed to build program: {errorCode}");

                    // Get build log
                    nuint logSize;
                    _cl.GetProgramBuildInfo(_program, device, (uint)ProgramBuildInfo.BuildLog, 0, null, &logSize);

                    if (logSize > 0)
                    {
                        var log = stackalloc byte[(int)logSize];
                        _cl.GetProgramBuildInfo(_program, devices[0], (uint)ProgramBuildInfo.BuildLog, logSize, log,
                            null);
                        var logStr = Encoding.ASCII.GetString(log, (int)logSize);
                        Logger.LogError($"[TextureClassifier] Build log: {logStr}");
                    }

                    return;
                }

                _kernel = _cl.CreateKernel(_program, "classify_texture", &errorCode);

                if (errorCode != 0)
                {
                    Logger.LogError($"[TextureClassifier] Failed to create kernel: {errorCode}");
                    return;
                }

                _gpuInitialized = true;
                Logger.Log("[TextureClassifier] GPU initialized successfully");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[TextureClassifier] GPU initialization failed: {ex.Message}");
            _gpuInitialized = false;
        }
    }

    private string GetKernelSource()
    {
        return @"
__kernel void classify_texture(
    __global const uchar* slice,
    __global float* predictions,
    __global const float* features,
    int width,
    int height,
    int patchSize,
    int numFeatures)
{
    int gid = get_global_id(0);
    int x = gid % width;
    int y = gid / width;
    int halfSize = patchSize / 2;
    
    if (x < halfSize || x >= width - halfSize || y < halfSize || y >= height - halfSize)
    {
        predictions[gid] = 0.0f;
        return;
    }
    
    // Extract patch features
    float patchFeatures[1024]; // Max patch size 32x32
    int featureIdx = 0;
    
    for (int py = -halfSize; py <= halfSize; py++)
    {
        for (int px = -halfSize; px <= halfSize; px++)
        {
            int sx = x + px;
            int sy = y + py;
            patchFeatures[featureIdx++] = slice[sy * width + sx] / 255.0f;
        }
    }
    
    // Compare with all class features
    float maxSimilarity = 0.0f;
    int featureDim = patchSize * patchSize;
    
    for (int c = 0; c < numFeatures; c++)
    {
        float similarity = 0.0f;
        
        for (int i = 0; i < featureDim; i++)
        {
            float diff = patchFeatures[i] - features[c * featureDim + i];
            similarity += diff * diff;
        }
        
        similarity = 1.0f / (1.0f + sqrt(similarity));
        maxSimilarity = max(maxSimilarity, similarity);
    }
    
    predictions[gid] = maxSimilarity;
}
";
    }
}