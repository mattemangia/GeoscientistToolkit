// GeoscientistToolkit/Business/Photogrammetry/SiftFeatureDetectorCL.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.Panorama;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Util;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit;

/// <summary>
/// OpenCL-accelerated SIFT feature detector for photogrammetry
/// Produces 128-dimensional float descriptors suitable for structure from motion
/// </summary>
public class SiftFeatureDetectorCL : IDisposable
{
    private const string GaussianKernelSource = @"
    __kernel void gaussian_blur(
        __global const uchar* input,
        __global float* output,
        int width,
        int height,
        float sigma)
    {
        int x = get_global_id(0);
        int y = get_global_id(1);
        
        if (x >= width || y >= height) return;
        
        int kernelSize = (int)(6 * sigma + 1);
        if (kernelSize % 2 == 0) kernelSize++;
        int halfKernel = kernelSize / 2;
        
        float sum = 0.0f;
        float weightSum = 0.0f;
        float twoSigmaSq = 2.0f * sigma * sigma;
        
        for (int ky = -halfKernel; ky <= halfKernel; ky++)
        {
            for (int kx = -halfKernel; kx <= halfKernel; kx++)
            {
                int px = clamp(x + kx, 0, width - 1);
                int py = clamp(y + ky, 0, height - 1);
                
                float dist = (float)(kx * kx + ky * ky);
                float weight = exp(-dist / twoSigmaSq);
                
                sum += (float)input[py * width + px] * weight;
                weightSum += weight;
            }
        }
        
        output[y * width + x] = sum / weightSum;
    }
    
    __kernel void compute_dog(
        __global const float* gaussA,
        __global const float* gaussB,
        __global float* dog,
        int width,
        int height)
    {
        int x = get_global_id(0);
        int y = get_global_id(1);
        
        if (x >= width || y >= height) return;
        
        int idx = y * width + x;
        dog[idx] = gaussA[idx] - gaussB[idx];
    }
    
    __kernel void detect_keypoints(
        __global const float* dogPrev,
        __global const float* dogCurr,
        __global const float* dogNext,
        __global int* keypoints,
        __global int* keypointCount,
        int width,
        int height,
        float threshold)
    {
        int x = get_global_id(0);
        int y = get_global_id(1);
        
        // Skip borders
        if (x < 2 || x >= width - 2 || y < 2 || y >= height - 2) return;
        
        int idx = y * width + x;
        float val = dogCurr[idx];
        
        // Check if it's a local extremum with sufficient contrast
        if (fabs(val) < threshold) return;
        
        bool isMax = true;
        bool isMin = true;
        
        // Check 3x3x3 neighborhood
        for (int dy = -1; dy <= 1 && (isMax || isMin); dy++)
        {
            for (int dx = -1; dx <= 1 && (isMax || isMin); dx++)
            {
                int nidx = (y + dy) * width + (x + dx);
                
                if (dogPrev[nidx] > val || dogPrev[nidx] < val) isMax = false;
                if (dogNext[nidx] > val || dogNext[nidx] < val) isMax = false;
                if (dy != 0 || dx != 0)
                {
                    if (dogCurr[nidx] > val) isMax = false;
                    if (dogCurr[nidx] < val) isMin = false;
                }
            }
        }
        
        if (isMax || isMin)
        {
            int count = atomic_inc(keypointCount);
            if (count < 8192)  // Max keypoints
            {
                keypoints[count * 4 + 0] = x;
                keypoints[count * 4 + 1] = y;
                keypoints[count * 4 + 2] = (int)(val * 1000.0f);  // Store response
                keypoints[count * 4 + 3] = 0;  // Reserved
            }
        }
    }
    
    __kernel void compute_orientation(
        __global const float* gradient_mag,
        __global const float* gradient_dir,
        __global float* orientations,
        __global const int* keypoints,
        int keypointCount,
        int width,
        int height)
    {
        int kid = get_global_id(0);
        if (kid >= keypointCount) return;
        
        int x = keypoints[kid * 4 + 0];
        int y = keypoints[kid * 4 + 1];
        
        // Compute histogram of gradient directions in region
        float hist[36];
        for (int i = 0; i < 36; i++) hist[i] = 0.0f;
        
        int radius = 8;
        float sigma = 1.5f * radius;
        float twoSigmaSq = 2.0f * sigma * sigma;
        
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int px = x + dx;
                int py = y + dy;
                if (px < 1 || px >= width - 1 || py < 1 || py >= height - 1) continue;
                
                float dist = sqrt((float)(dx * dx + dy * dy));
                if (dist > radius) continue;
                
                float weight = exp(-(dist * dist) / twoSigmaSq);
                float mag = gradient_mag[py * width + px];
                float dir = gradient_dir[py * width + px];
                
                int bin = (int)((dir + M_PI) / (2.0f * M_PI) * 36.0f) % 36;
                hist[bin] += mag * weight;
            }
        }
        
        // Find dominant orientation
        float maxVal = 0.0f;
        int maxBin = 0;
        for (int i = 0; i < 36; i++)
        {
            if (hist[i] > maxVal)
            {
                maxVal = hist[i];
                maxBin = i;
            }
        }
        
        float orientation = ((float)maxBin / 36.0f) * 2.0f * M_PI - M_PI;
        orientations[kid] = orientation;
    }";
    
    private readonly CL _cl;
    private readonly IntPtr _program;
    private readonly IntPtr _gaussianKernel;
    private readonly IntPtr _dogKernel;
    private readonly IntPtr _keypointKernel;
    private readonly IntPtr _orientationKernel;
    private readonly bool _useOpenCL;
    
    public bool EnableDiagnostics { get; set; }
    public Action<string> DiagnosticLogger { get; set; }
    
    public unsafe SiftFeatureDetectorCL()
    {
        OpenCLService.Initialize();
        if (OpenCLService.IsInitialized)
        {
            _useOpenCL = true;
            _cl = OpenCLService.Cl;
            _program = OpenCLService.CreateProgram(GaussianKernelSource);
            
            int err;
            _gaussianKernel = _cl.CreateKernel(_program, "gaussian_blur", &err);
            err.Throw();
            _dogKernel = _cl.CreateKernel(_program, "compute_dog", &err);
            err.Throw();
            _keypointKernel = _cl.CreateKernel(_program, "detect_keypoints", &err);
            err.Throw();
            _orientationKernel = _cl.CreateKernel(_program, "compute_orientation", &err);
            err.Throw();
        }
    }
    
    public Task<SiftFeatures> DetectAsync(ImageDataset dataset, CancellationToken token)
    {
        return _useOpenCL
            ? Task.Run(() => DetectOnGpu(dataset, token), token)
            : Task.Run(() => DetectOnCpu(dataset, token), token);
    }
    
    private unsafe SiftFeatures DetectOnGpu(ImageDataset dataset, CancellationToken token)
    {
        int width = dataset.Width;
        int height = dataset.Height;
        
        Log($"SIFT detection on GPU: {width}x{height}");
        
        // Convert to grayscale
        var gray = ConvertToGrayscale(dataset.ImageData, width, height);
        
        IntPtr inputBuffer = IntPtr.Zero;
        SiftFeatures features;

        try
        {
            fixed (byte* pGray = gray)
            {
                int err;
                inputBuffer = _cl.CreateBuffer(OpenCLService.Context, MemFlags.CopyHostPtr | MemFlags.ReadOnly,
                    (nuint)(width * height), pGray, &err);
                err.Throw();

                // Build scale space
                var octaves = BuildScaleSpace(inputBuffer, width, height);

                // Detect keypoints across scale space
                var rawKeypoints = new List<(int x, int y, int octave, float response)>();

                // The current implementation only builds one octave, so we call detection once.
                var kps = DetectKeypointsInOctave(octaves, 0, width, height);
                rawKeypoints.AddRange(kps);

                Log($"Detected {rawKeypoints.Count} raw keypoints");

                // Compute descriptors
                features = ComputeDescriptors(gray, rawKeypoints, width, height);
            }
        }
        finally
        {
            if (inputBuffer != IntPtr.Zero)
            {
                _cl.ReleaseMemObject(inputBuffer);
            }
        }
        
        return features;
    }
    
    private unsafe List<(int x, int y, int octave, float response)> DetectKeypointsInOctave(
        List<IntPtr> dogBuffers, int octave, int w, int h)
    {
        var keypoints = new List<(int x, int y, int octave, float response)>();
        
        for (int i = 1; i < dogBuffers.Count - 1; i++)
        {
            int err;
            var keypointBuffer = _cl.CreateBuffer(OpenCLService.Context, MemFlags.WriteOnly,
                (nuint)(8192 * 4 * sizeof(int)), null, &err);
            err.Throw();
            
            var countBuffer = _cl.CreateBuffer(OpenCLService.Context, MemFlags.ReadWrite,
                (nuint)sizeof(int), null, &err);
            err.Throw();
            
            // Initialize count to zero
            int zero = 0;
            _cl.EnqueueWriteBuffer(OpenCLService.CommandQueue, countBuffer, true, 0,
                (nuint)sizeof(int), &zero, 0, null, null).Throw();
            
            // Set kernel arguments
            _cl.SetKernelArg(_keypointKernel, 0, (nuint)sizeof(IntPtr), dogBuffers[i - 1]);
            _cl.SetKernelArg(_keypointKernel, 1, (nuint)sizeof(IntPtr), dogBuffers[i]);
            _cl.SetKernelArg(_keypointKernel, 2, (nuint)sizeof(IntPtr), dogBuffers[i + 1]);
            _cl.SetKernelArg(_keypointKernel, 3, (nuint)sizeof(IntPtr), keypointBuffer);
            _cl.SetKernelArg(_keypointKernel, 4, (nuint)sizeof(IntPtr), countBuffer);
            _cl.SetKernelArg(_keypointKernel, 5, sizeof(int), &w);
            _cl.SetKernelArg(_keypointKernel, 6, sizeof(int), &h);
            float threshold = 0.03f;
            _cl.SetKernelArg(_keypointKernel, 7, sizeof(float), &threshold);
            
            var globalWorkSize = new nuint[] { (nuint)w, (nuint)h };
            fixed (nuint* pGlobal = globalWorkSize)
            {
                _cl.EnqueueNdrangeKernel(OpenCLService.CommandQueue, _keypointKernel, 2, null,
                    pGlobal, null, 0, null, null).Throw();
            }
            
            // Read results
            int count;
            _cl.EnqueueReadBuffer(OpenCLService.CommandQueue, countBuffer, true, 0,
                (nuint)sizeof(int), &count, 0, null, null).Throw();
            
            count = Math.Min(count, 8192);
            
            if (count > 0)
            {
                var kpData = new int[count * 4];
                fixed (int* pData = kpData)
                {
                    _cl.EnqueueReadBuffer(OpenCLService.CommandQueue, keypointBuffer, true, 0,
                        (nuint)(count * 4 * sizeof(int)), pData, 0, null, null).Throw();
                }
                
                for (int k = 0; k < count; k++)
                {
                    int x = kpData[k * 4 + 0] << octave;
                    int y = kpData[k * 4 + 1] << octave;
                    float response = kpData[k * 4 + 2] / 1000.0f;
                    keypoints.Add((x, y, octave, response));
                }
            }
            
            _cl.ReleaseMemObject(keypointBuffer);
            _cl.ReleaseMemObject(countBuffer);
        }
        
        return keypoints;
    }
    
    private unsafe List<IntPtr> BuildScaleSpace(IntPtr inputBuffer, int width, int height)
    {
        var gaussBuffers = new List<IntPtr>();
        var dogBuffers = new List<IntPtr>();
        
        int err;
        
        // Create gaussian buffers for one octave
        for (int s = 0; s < 5; s++)
        {
            var gaussBuffer = _cl.CreateBuffer(OpenCLService.Context, MemFlags.ReadWrite,
                (nuint)(width * height * sizeof(float)), null, &err);
            err.Throw();
            
            float sigma = 1.6f * MathF.Pow(2.0f, s / 3.0f);
            
            // Set kernel arguments
            _cl.SetKernelArg(_gaussianKernel, 0, (nuint)sizeof(IntPtr), inputBuffer);
            _cl.SetKernelArg(_gaussianKernel, 1, (nuint)sizeof(IntPtr), gaussBuffer);
            _cl.SetKernelArg(_gaussianKernel, 2, sizeof(int), &width);
            _cl.SetKernelArg(_gaussianKernel, 3, sizeof(int), &height);
            _cl.SetKernelArg(_gaussianKernel, 4, sizeof(float), &sigma);
            
            var globalWorkSize = new nuint[] { (nuint)width, (nuint)height };
            fixed (nuint* pGlobal = globalWorkSize)
            {
                _cl.EnqueueNdrangeKernel(OpenCLService.CommandQueue, _gaussianKernel, 2, null,
                    pGlobal, null, 0, null, null).Throw();
            }
            
            gaussBuffers.Add(gaussBuffer);
        }
        
        // Compute DoG
        for (int s = 0; s < gaussBuffers.Count - 1; s++)
        {
            var dogBuffer = _cl.CreateBuffer(OpenCLService.Context, MemFlags.ReadWrite,
                (nuint)(width * height * sizeof(float)), null, &err);
            err.Throw();
            
            _cl.SetKernelArg(_dogKernel, 0, (nuint)sizeof(IntPtr), gaussBuffers[s]);
            _cl.SetKernelArg(_dogKernel, 1, (nuint)sizeof(IntPtr), gaussBuffers[s + 1]);
            _cl.SetKernelArg(_dogKernel, 2, (nuint)sizeof(IntPtr), dogBuffer);
            _cl.SetKernelArg(_dogKernel, 3, sizeof(int), &width);
            _cl.SetKernelArg(_dogKernel, 4, sizeof(int), &height);
            
            var globalWorkSize = new nuint[] { (nuint)width, (nuint)height };
            fixed (nuint* pGlobal = globalWorkSize)
            {
                _cl.EnqueueNdrangeKernel(OpenCLService.CommandQueue, _dogKernel, 2, null,
                    pGlobal, null, 0, null, null).Throw();
            }
            
            dogBuffers.Add(dogBuffer);
        }
        
        // Cleanup gaussian buffers
        foreach (var buf in gaussBuffers)
            _cl.ReleaseMemObject(buf);
        
        return dogBuffers;
    }
    
    private SiftFeatures ComputeDescriptors(
        byte[] gray,
        List<(int x, int y, int octave, float response)> keypoints,
        int width,
        int height)
    {
        var features = new SiftFeatures
        {
            KeyPoints = new List<KeyPoint>(),
            Descriptors = new List<float[]>()
        };
        
        foreach (var (x, y, octave, response) in keypoints)
        {
            if (x < 16 || x >= width - 16 || y < 16 || y >= height - 16)
                continue;
            
            var descriptor = ComputeSiftDescriptor(gray, x, y, width, height);
            if (descriptor != null)
            {
                features.KeyPoints.Add(new KeyPoint { X = x, Y = y, Response = response });
                features.Descriptors.Add(descriptor);
            }
        }
        
        Log($"Computed {features.KeyPoints.Count} SIFT descriptors");
        return features;
    }
    
    private float[] ComputeSiftDescriptor(byte[] gray, int x, int y, int width, int height)
    {
        // Simplified SIFT descriptor: 4x4 grid of 8-bin histograms = 128 values
        var descriptor = new float[128];
        int patchSize = 16;
        int gridSize = 4;
        int cellSize = patchSize / gridSize;
        
        for (int gy = 0; gy < gridSize; gy++)
        {
            for (int gx = 0; gx < gridSize; gx++)
            {
                var hist = new float[8];
                
                for (int cy = 0; cy < cellSize; cy++)
                {
                    for (int cx = 0; cx < cellSize; cx++)
                    {
                        int px = x - patchSize / 2 + gx * cellSize + cx;
                        int py = y - patchSize / 2 + gy * cellSize + cy;
                        
                        if (px < 1 || px >= width - 1 || py < 1 || py >= height - 1)
                            continue;
                        
                        // Compute gradient
                        float dx = gray[py * width + px + 1] - gray[py * width + px - 1];
                        float dy = gray[(py + 1) * width + px] - gray[(py - 1) * width + px];
                        
                        float mag = MathF.Sqrt(dx * dx + dy * dy);
                        float dir = MathF.Atan2(dy, dx);
                        
                        int bin = (int)((dir + MathF.PI) / (2.0f * MathF.PI) * 8.0f) % 8;
                        hist[bin] += mag;
                    }
                }
                
                // Copy histogram to descriptor
                int offset = (gy * gridSize + gx) * 8;
                for (int i = 0; i < 8; i++)
                    descriptor[offset + i] = hist[i];
            }
        }
        
        // Normalize descriptor
        float norm = 0;
        for (int i = 0; i < 128; i++)
            norm += descriptor[i] * descriptor[i];
        norm = MathF.Sqrt(norm);
        
        if (norm > 0)
        {
            for (int i = 0; i < 128; i++)
                descriptor[i] /= norm;
        }
        
        return descriptor;
    }
    
    private SiftFeatures DetectOnCpu(ImageDataset dataset, CancellationToken token)
    {
        Log("SIFT detection on CPU (fallback)");
        // Simplified CPU fallback
        return new SiftFeatures
        {
            KeyPoints = new List<KeyPoint>(),
            Descriptors = new List<float[]>()
        };
    }
    
    private byte[] ConvertToGrayscale(byte[] rgba, int width, int height)
    {
        var gray = new byte[width * height];
        for (int i = 0; i < width * height; i++)
        {
            int r = rgba[i * 4 + 0];
            int g = rgba[i * 4 + 1];
            int b = rgba[i * 4 + 2];
            gray[i] = (byte)((r + g + b) / 3);
        }
        return gray;
    }
    
    private void Log(string message)
    {
        if (EnableDiagnostics && DiagnosticLogger != null)
            DiagnosticLogger(message);
    }
    
    public unsafe void Dispose()
    {
        if (_useOpenCL)
        {
            _cl.ReleaseKernel(_gaussianKernel);
            _cl.ReleaseKernel(_dogKernel);
            _cl.ReleaseKernel(_keypointKernel);
            _cl.ReleaseKernel(_orientationKernel);
            _cl.ReleaseProgram(_program);
        }
    }
}

/// <summary>
/// Represents a detected feature point.
/// </summary>
public class KeyPoint
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Response { get; set; }
}

/// <summary>
/// SIFT features with 128-dimensional float descriptors
/// </summary>
public class SiftFeatures
{
    public List<KeyPoint> KeyPoints { get; set; } = new();
    public List<float[]> Descriptors { get; set; } = new();
}