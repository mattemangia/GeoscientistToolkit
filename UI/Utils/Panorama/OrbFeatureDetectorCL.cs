// GeoscientistToolkit/Business/Panorama/OrbFeatureDetectorCL.cs

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Silk.NET.OpenCL;
using SkiaSharp;

namespace GeoscientistToolkit.Business.Panorama;

public class OrbFeatureDetectorCL : IDisposable
{
    private readonly CL _cl;
    private nint _context;
    private nint _commandQueue;
    private nint _program;
    private nint _fastKernel;
    private bool _disposed;
    
    // ORB parameters
    private const int FAST_THRESHOLD = 20;
    private const int MAX_FEATURES = 1000;
    private const int BRIEF_DESCRIPTOR_SIZE = 32; // 256 bits
    private const int PATCH_SIZE = 31;
    private const int HALF_PATCH_SIZE = 15;

    // Pre-generated random pattern for BRIEF descriptor for consistency.
    private static readonly (Point P1, Point P2)[] _briefPattern;

    private const string KernelSource = @"
        constant int fast_circle[16][2] = {
            {0, 3}, {1, 3}, {2, 2}, {3, 1},
            {3, 0}, {3, -1}, {2, -2}, {1, -3},
            {0, -3}, {-1, -3}, {-2, -2}, {-3, -1},
            {-3, 0}, {-3, 1}, {-2, 2}, {-1, 3}
        };
        
        __kernel void detect_fast_corners(
            __global const uchar* image,
            __global uchar* corners,
            const int width,
            const int height,
            const int threshold)
        {
            int x = get_global_id(0);
            int y = get_global_id(1);
            
            if (x < 3 || x >= width - 3 || y < 3 || y >= height - 3) {
                corners[y * width + x] = 0;
                return;
            }
            
            uchar center = image[y * width + x];
            int bright_threshold = center + threshold;
            int dark_threshold = center - threshold;
            
            int consecutive_bright = 0, consecutive_dark = 0;
            int max_consecutive_bright = 0, max_consecutive_dark = 0;
            
            for (int i = 0; i < 32; i++) {
                int idx = i % 16;
                int px = x + fast_circle[idx][0];
                int py = y + fast_circle[idx][1];
                uchar pixel = image[py * width + px];
                
                if (pixel > bright_threshold) {
                    consecutive_bright++;
                    consecutive_dark = 0;
                    max_consecutive_bright = max(max_consecutive_bright, consecutive_bright);
                } else if (pixel < dark_threshold) {
                    consecutive_dark++;
                    consecutive_bright = 0;
                    max_consecutive_dark = max(max_consecutive_dark, consecutive_dark);
                } else {
                    consecutive_bright = 0;
                    consecutive_dark = 0;
                }
            }
            
            corners[y * width + x] = (max_consecutive_bright >= 9 || max_consecutive_dark >= 9) ? 255 : 0;
        }
    ";
    
    static OrbFeatureDetectorCL()
    {
        _briefPattern = new (Point P1, Point P2)[BRIEF_DESCRIPTOR_SIZE * 8];
        var random = new Random(12345); 
        for (int i = 0; i < _briefPattern.Length; i++)
        {
            int x1 = random.Next(-HALF_PATCH_SIZE, HALF_PATCH_SIZE + 1);
            int y1 = random.Next(-HALF_PATCH_SIZE, HALF_PATCH_SIZE + 1);
            int x2 = random.Next(-HALF_PATCH_SIZE, HALF_PATCH_SIZE + 1);
            int y2 = random.Next(-HALF_PATCH_SIZE, HALF_PATCH_SIZE + 1);
            _briefPattern[i] = (new Point(x1, y1), new Point(x2, y2));
        }
    }
    
    public unsafe OrbFeatureDetectorCL()
    {
        _cl = CL.GetApi();
        
        uint platformCount;
        _cl.GetPlatformIDs(0, null, &platformCount);
        if (platformCount == 0) throw new Exception("No OpenCL platforms found");
        
        var platforms = new nint[platformCount];
        fixed (nint* platformsPtr = platforms) { _cl.GetPlatformIDs(platformCount, platformsPtr, null); }
        var platform = platforms[0];
        
        uint deviceCount;
        _cl.GetDeviceIDs(platform, DeviceType.Gpu, 0, null, &deviceCount);
        if (deviceCount == 0) _cl.GetDeviceIDs(platform, DeviceType.Cpu, 0, null, &deviceCount);
        if (deviceCount == 0) throw new Exception("No OpenCL devices found");
        
        var devices = new nint[deviceCount];
        fixed (nint* devicesPtr = devices)
        {
            _cl.GetDeviceIDs(platform, DeviceType.Gpu, deviceCount, devicesPtr, null);
            if(deviceCount == 0) _cl.GetDeviceIDs(platform, DeviceType.Cpu, deviceCount, devicesPtr, null);
        }
        var device = devices[0];
        
        int error;
        var contextProperties = stackalloc nint[3] { (nint)ContextProperties.Platform, platform, 0 };
        _context = _cl.CreateContext(contextProperties, 1, &device, null, null, &error);
        CheckError(error, "Failed to create context");
        
        _commandQueue = _cl.CreateCommandQueue(_context, device, CommandQueueProperties.None, &error);
        CheckError(error, "Failed to create command queue");
        
        var sourcePtr = Marshal.StringToHGlobalAnsi(KernelSource);
        try
        {
            var sourceLength = (nuint)KernelSource.Length;
            _program = _cl.CreateProgramWithSource(_context, 1, (byte**)&sourcePtr, &sourceLength, &error);
            CheckError(error, "Failed to create program");
            
            // CORRECTED: Explicitly cast the 'null' for build options to byte* to resolve ambiguity.
            error = _cl.BuildProgram(_program, 1, &device, (byte*)null, null, null);
            if (error != (int)ErrorCodes.Success)
            {
                nuint logSize;
                _cl.GetProgramBuildInfo(_program, device, ProgramBuildInfo.BuildLog, 0, null, &logSize);
                var log = new byte[logSize];
                fixed (byte* logPtr = log) { _cl.GetProgramBuildInfo(_program, device, ProgramBuildInfo.BuildLog, logSize, logPtr, null); }
                throw new Exception($"Failed to build program: {Encoding.UTF8.GetString(log)}");
            }
            
            _fastKernel = _cl.CreateKernel(_program, "detect_fast_corners", &error);
            CheckError(error, "Failed to create FAST kernel");
        }
        finally
        {
            Marshal.FreeHGlobal((nint)sourcePtr);
        }
    }
    private void NormalizeContrast(byte[] img, int w, int h)
    {
        // Simple per-image linear stretch (1%–99% robust min/max)
        int histBins = 256;
        int[] hist = new int[histBins];
        for (int i = 0; i < img.Length; i++) hist[img[i]]++;

        int total = w * h;
        int clip = (int)(0.01 * total);
        int lo = 0, hi = 255, csum = 0;
        while (lo < 255 && (csum += hist[lo]) < clip) lo++;
        csum = 0;
        while (hi > 0 && (csum += hist[hi]) < clip) hi--;

        float scale = (hi > lo) ? 255f / (hi - lo) : 1f;
        for (int i = 0; i < img.Length; i++)
        {
            int v = img[i];
            if (v <= lo) img[i] = 0;
            else if (v >= hi) img[i] = 255;
            else img[i] = (byte)((v - lo) * scale + 0.5f);
        }
    }

    private void BoxBlur3x3(byte[] img, int w, int h)
    {
        var outImg = new byte[img.Length];
        for (int y = 1; y < h - 1; y++)
        {
            int yw = y * w;
            for (int x = 1; x < w - 1; x++)
            {
                int idx = yw + x;
                int acc =
                    img[idx - w - 1] + img[idx - w] + img[idx - w + 1] +
                    img[idx - 1]     + img[idx]     + img[idx + 1] +
                    img[idx + w - 1] + img[idx + w] + img[idx + w + 1];
                outImg[idx] = (byte)(acc / 9);
            }
        }
        // keep borders unchanged
        for (int x = 0; x < w; x++) { outImg[x] = img[x]; outImg[(h - 1) * w + x] = img[(h - 1) * w + x]; }
        for (int y = 0; y < h; y++) { outImg[y * w] = img[y * w]; outImg[y * w + (w - 1)] = img[y * w + (w - 1)]; }
        Buffer.BlockCopy(outImg, 0, img, 0, img.Length);
    }

    public Task<DetectedFeatures> DetectFeaturesAsync(SKBitmap bitmap, CancellationToken token)
    {
        return Task.Run(() => DetectFeaturesGPU(bitmap, token), token);
    }
    
    private unsafe DetectedFeatures DetectFeaturesGPU(SKBitmap bitmap, CancellationToken token)
    {
        byte[] grayImage = ConvertToGrayscale(bitmap);
        
        int width = bitmap.Width;
        int height = bitmap.Height;
        NormalizeContrast(grayImage, width, height);
        BoxBlur3x3(grayImage, width, height);

        var keypoints = new List<KeyPoint>();
        var descriptors = new List<byte[]>();
        
        var corners = DetectFastCornersGPU(grayImage, width, height);
        
        var candidates = new List<(int x, int y, float response)>();
        for (int y = HALF_PATCH_SIZE; y < height - HALF_PATCH_SIZE; y++)
        {
            for (int x = HALF_PATCH_SIZE; x < width - HALF_PATCH_SIZE; x++)
            {
                if (corners[y * width + x] > 0)
                {
                    float response = ComputeHarrisResponse(grayImage, x, y, width, height);
                    candidates.Add((x, y, response));
                }
            }
        }
        
        candidates.Sort((a, b) => b.response.CompareTo(a.response));
        
        var selectedKeypoints = new List<KeyPoint>();
        var suppressionRadiusSq = 10.0f * 10.0f; 
        
        foreach (var (x, y, response) in candidates)
        {
            bool suppressed = false;
            foreach (var kp in selectedKeypoints)
            {
                float dx = x - kp.X;
                float dy = y - kp.Y;
                if (dx * dx + dy * dy < suppressionRadiusSq)
                {
                    suppressed = true;
                    break;
                }
            }
            
            if (!suppressed)
            {
                selectedKeypoints.Add(new KeyPoint { X = x, Y = y });
                if (selectedKeypoints.Count >= MAX_FEATURES) break;
            }
        }
        
        foreach (var kp in selectedKeypoints)
        {
            float angle = ComputeOrientation(grayImage, (int)kp.X, (int)kp.Y, width, height);
            
            var descriptor = ComputeBriefDescriptor(grayImage, (int)kp.X, (int)kp.Y, width, height, angle);
            
            keypoints.Add(new KeyPoint { X = kp.X, Y = kp.Y, Angle = angle });
            descriptors.Add(descriptor);
        }
        
        var descriptorArray = new byte[descriptors.Count * BRIEF_DESCRIPTOR_SIZE];
        for (int i = 0; i < descriptors.Count; i++)
        {
            Buffer.BlockCopy(descriptors[i], 0, descriptorArray, i * BRIEF_DESCRIPTOR_SIZE, BRIEF_DESCRIPTOR_SIZE);
        }
        
        return new DetectedFeatures
        {
            KeyPoints = keypoints,
            Descriptors = descriptorArray
        };
    }
    
    private float ComputeOrientation(byte[] image, int x, int y, int width, int height)
    {
        float m01 = 0, m10 = 0;
        
        for (int dy = -HALF_PATCH_SIZE; dy <= HALF_PATCH_SIZE; dy++)
        {
            for (int dx = -HALF_PATCH_SIZE; dx <= HALF_PATCH_SIZE; dx++)
            {
                if (dx * dx + dy * dy <= HALF_PATCH_SIZE * HALF_PATCH_SIZE)
                {
                    int sampleX = Math.Clamp(x + dx, 0, width - 1);
                    int sampleY = Math.Clamp(y + dy, 0, height - 1);
                    
                    byte intensity = image[sampleY * width + sampleX];
                    m10 += dx * intensity;
                    m01 += dy * intensity;
                }
            }
        }
        
        return (float)Math.Atan2(m01, m10);
    }

    private byte[] ComputeBriefDescriptor(byte[] image, int x, int y, int width, int height, float angle)
    {
        var descriptor = new byte[BRIEF_DESCRIPTOR_SIZE];
        float cosAngle = (float)Math.Cos(angle);
        float sinAngle = (float)Math.Sin(angle);

        for (int i = 0; i < BRIEF_DESCRIPTOR_SIZE; i++)
        {
            byte value = 0;
            for (int j = 0; j < 8; j++)
            {
                var (p1, p2) = _briefPattern[i * 8 + j];

                float p1x = p1.X * cosAngle - p1.Y * sinAngle;
                float p1y = p1.X * sinAngle + p1.Y * cosAngle;
                float p2x = p2.X * cosAngle - p2.Y * sinAngle;
                float p2y = p2.X * sinAngle + p2.Y * cosAngle;

                int sampleX1 = Math.Clamp(x + (int)Math.Round(p1x), 0, width - 1);
                int sampleY1 = Math.Clamp(y + (int)Math.Round(p1y), 0, height - 1);
                int sampleX2 = Math.Clamp(x + (int)Math.Round(p2x), 0, width - 1);
                int sampleY2 = Math.Clamp(y + (int)Math.Round(p2y), 0, height - 1);
                
                if (image[sampleY1 * width + sampleX1] < image[sampleY2 * width + sampleX2])
                {
                    value |= (byte)(1 << j);
                }
            }
            descriptor[i] = value;
        }
        
        return descriptor;
    }

    private unsafe byte[] DetectFastCornersGPU(byte[] image, int width, int height)
    {
        int error;
        nint imageBuffer = 0, cornersBuffer = 0;
        var corners = new byte[width * height];
        
        try
        {
            fixed (byte* imagePtr = image)
            {
                imageBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(width * height), imagePtr, &error);
                CheckError(error, "Failed to create image buffer");
            }
            
            cornersBuffer = _cl.CreateBuffer(_context, MemFlags.WriteOnly, (nuint)(width * height), null, &error);
            CheckError(error, "Failed to create corners buffer");
            
            _cl.SetKernelArg(_fastKernel, 0, (nuint)sizeof(nint), &imageBuffer);
            _cl.SetKernelArg(_fastKernel, 1, (nuint)sizeof(nint), &cornersBuffer);
            _cl.SetKernelArg(_fastKernel, 2, (nuint)sizeof(int), &width);
            _cl.SetKernelArg(_fastKernel, 3, (nuint)sizeof(int), &height);
            int threshold = FAST_THRESHOLD;
            _cl.SetKernelArg(_fastKernel, 4, (nuint)sizeof(int), &threshold);
            
            var globalWorkSize = stackalloc nuint[2] { (nuint)width, (nuint)height };
            
            _cl.EnqueueNdrangeKernel(_commandQueue, _fastKernel, 2, null, globalWorkSize, null, 0, null, null);
            
            fixed (byte* cornersPtr = corners)
            {
                _cl.EnqueueReadBuffer(_commandQueue, cornersBuffer, true, 0, (nuint)(width * height), cornersPtr, 0, null, null);
            }
            
            _cl.Finish(_commandQueue);
        }
        finally
        {
            if (imageBuffer != 0) _cl.ReleaseMemObject(imageBuffer);
            if (cornersBuffer != 0) _cl.ReleaseMemObject(cornersBuffer);
        }
        
        return corners;
    }
    
    private byte[] ConvertToGrayscale(SKBitmap bitmap)
    {
        var grayImage = new byte[bitmap.Width * bitmap.Height];
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                grayImage[y * bitmap.Width + x] = (byte)(0.299 * pixel.Red + 0.587 * pixel.Green + 0.114 * pixel.Blue);
            }
        }
        return grayImage;
    }
    
    private float ComputeHarrisResponse(byte[] image, int x, int y, int width, int height)
    {
        float ix = (image[y * width + x + 1] - image[y * width + x - 1]) / 2.0f;
        float iy = (image[(y + 1) * width + x] - image[(y - 1) * width + x]) / 2.0f;

        float ixx = ix * ix;
        float iyy = iy * iy;
        float ixy = ix * iy;

        return (ixx * iyy - ixy * ixy) - 0.04f * (ixx + iyy) * (ixx + iyy);
    }
    
    private void CheckError(int error, string message)
    {
        if (error != (int)ErrorCodes.Success)
            throw new Exception($"{message}: {(ErrorCodes)error}");
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        
        if (_fastKernel != 0) _cl.ReleaseKernel(_fastKernel);
        if (_program != 0) _cl.ReleaseProgram(_program);
        if (_commandQueue != 0) _cl.ReleaseCommandQueue(_commandQueue);
        if (_context != 0) _cl.ReleaseContext(_context);
        
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}