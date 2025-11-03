// GeoscientistToolkit/Business/Panorama/OrbFeatureDetectorCL.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.Data.Image;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Business.Panorama;

/// <summary>
/// An ORB (Oriented FAST and Rotated BRIEF) feature detector.
/// It uses OpenCL for GPU acceleration when available, with a full C# implementation as a fallback for CPU-only environments.
/// </summary>
public class OrbFeatureDetectorCL : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Vector4i
    {
        public int X, Y, Z, W;
        public Vector4i(int x, int y, int z, int w) { X = x; Y = y; Z = z; W = w; }
    }

    private const string OrbKernelsSource = @"
        #pragma OPENCL EXTENSION cl_khr_byte_addressable_store : enable
        __constant sampler_t smp_clamp_nearest = CLK_NORMALIZED_COORDS_FALSE | CLK_ADDRESS_CLAMP_TO_EDGE | CLK_FILTER_NEAREST;
        __constant sampler_t smp_clamp_linear = CLK_NORMALIZED_COORDS_FALSE | CLK_ADDRESS_CLAMP_TO_EDGE | CLK_FILTER_LINEAR;

        __kernel void to_grayscale(__read_only image2d_t src, __write_only image2d_t dst) {
            int2 pos = (int2)(get_global_id(0), get_global_id(1));
            float4 pixel = read_imagef(src, smp_clamp_nearest, pos);
            float gray = dot(pixel.xyz, (float3)(0.299f, 0.587f, 0.114f));
            write_imagef(dst, pos, (float4)(gray, 0, 0, 0));
        }

        __constant int2 fast_offsets[16] = {
            (int2)(0, 3), (int2)(1, 3), (int2)(2, 2), (int2)(3, 1), (int2)(3, 0), (int2)(3, -1), (int2)(2, -2), (int2)(1, -3),
            (int2)(0, -3), (int2)(-1, -3), (int2)(-2, -2), (int2)(-3, -1), (int2)(-3, 0), (int2)(-3, 1), (int2)(-2, 2), (int2)(-1, 3)
        };

        __kernel void fast9_detect(__read_only image2d_t src, __global uint* corners, __global int* corner_count, int max_corners, float threshold) {
            int2 pos = (int2)(get_global_id(0), get_global_id(1));
            if(pos.x < 3 || pos.y < 3 || pos.x >= get_image_width(src) - 3 || pos.y >= get_image_height(src) - 3) return;
            float p = read_imagef(src, smp_clamp_nearest, pos).x;
            float upper = p + threshold;
            float lower = p - threshold;
            int continuous = 0;
            for(int i = 0; i < 25; i++) {
                float val = read_imagef(src, smp_clamp_nearest, pos + fast_offsets[i % 16]).x;
                if(val > upper || val < lower) { continuous++; } else { continuous = 0; }
                if(continuous >= 9) {
                    int index = atomic_add(corner_count, 1);
                    if (index < max_corners) { corners[index] = (pos.y << 16) | pos.x; }
                    return;
                }
            }
        }
        
        __kernel void compute_orientation(__read_only image2d_t src, __global uint* keypoints, __global float4* oriented_keypoints, int num_keypoints, int patch_size) {
            int gid = get_global_id(0);
            if (gid >= num_keypoints) return;
            uint packed_coords = keypoints[gid];
            float2 pos = (float2)(packed_coords & 0xFFFF, packed_coords >> 16);
            int half_patch = patch_size / 2;
            float m10 = 0.0f, m01 = 0.0f;
            for (int y = -half_patch; y <= half_patch; ++y) {
                for (int x = -half_patch; x <= half_patch; ++x) {
                    float val = read_imagef(src, smp_clamp_nearest, pos + (float2)(x, y)).x;
                    m10 += x * val;
                    m01 += y * val;
                }
            }
            oriented_keypoints[gid] = (float4)(pos.x, pos.y, atan2(m01, m10), 0.0f);
        }

        __kernel void compute_rbrief(__read_only image2d_t src, __global const float4* keypoints, __global ulong4* descriptors, __global const int4* patterns, int num_keypoints) {
            int gid = get_global_id(0);
            if (gid >= num_keypoints) return;
            float4 kp = keypoints[gid];
            float2 center = kp.xy;
            float angle = kp.z;
            float cos_a = cos(angle);
            float sin_a = sin(angle);
            ulong descriptor[4] = {0, 0, 0, 0};
            for (int i = 0; i < 256; ++i) {
                int4 p = patterns[i];
                float x1 = p.x * cos_a - p.y * sin_a; float y1 = p.x * sin_a + p.y * cos_a;
                float x2 = p.z * cos_a - p.w * sin_a; float y2 = p.z * sin_a + p.w * cos_a;
                float v1 = read_imagef(src, smp_clamp_linear, center + (float2)(x1, y1)).x;
                float v2 = read_imagef(src, smp_clamp_linear, center + (float2)(x2, y2)).x;
                if (v1 < v2) {
                    descriptor[i / 64] |= (1UL << (i % 64));
                }
            }
            descriptors[gid] = (ulong4)(descriptor[0], descriptor[1], descriptor[2], descriptor[3]);
        }
    ";

    private const int MaxFeatures = 2000;
    private const float FastThreshold = 0.08f;
    private const int OrientationPatchSize = 31;

    private readonly bool _useOpenCL;
    private readonly CL _cl;
    private readonly IntPtr _program;
    private readonly IntPtr _grayKernel, _fastKernel, _orientKernel, _briefKernel;
    private readonly IntPtr _patternBuffer;

    public unsafe OrbFeatureDetectorCL()
    {
        OpenCLService.Initialize();
        if (OpenCLService.IsInitialized)
        {
            _useOpenCL = true;
            _cl = OpenCLService.Cl;
            _program = OpenCLService.CreateProgram(OrbKernelsSource);
            int err;
            _grayKernel = _cl.CreateKernel(_program, "to_grayscale", &err); err.Throw();
            _fastKernel = _cl.CreateKernel(_program, "fast9_detect", &err); err.Throw();
            _orientKernel = _cl.CreateKernel(_program, "compute_orientation", &err); err.Throw();
            _briefKernel = _cl.CreateKernel(_program, "compute_rbrief", &err); err.Throw();
            
            var patterns = BriefPattern.GetPattern();
            fixed(Vector4i* p = patterns)
            {
                _patternBuffer = _cl.CreateBuffer(OpenCLService.Context, MemFlags.CopyHostPtr | MemFlags.ReadOnly, (nuint)(patterns.Length * sizeof(Vector4i)), p, &err);
                err.Throw();
            }
        }
    }

    public Task<DetectedFeatures> DetectAsync(ImageDataset image, CancellationToken token)
    {
        return _useOpenCL 
            ? Task.Run(() => DetectOnGpu(image, token), token) 
            : Task.Run(() => DetectOnCpu(image, token), token);
    }

    private unsafe DetectedFeatures DetectOnGpu(ImageDataset image, CancellationToken token)
    {
        var features = new DetectedFeatures();
        int width = image.Width;
        int height = image.Height;
        int err;
        IntPtr orientedKeypointsBuffer = IntPtr.Zero, descriptorsBuffer = IntPtr.Zero;

        var rgbaFormat = new ImageFormat(ChannelOrder.Rgba, ChannelType.UnsignedInt8);
        var grayFormat = new ImageFormat(ChannelOrder.R, ChannelType.Float);

        var pinnedHandle = GCHandle.Alloc(image.ImageData, GCHandleType.Pinned);
        var srcImage = _cl.CreateImage2D(OpenCLService.Context, MemFlags.CopyHostPtr | MemFlags.ReadOnly, &rgbaFormat, (nuint)width, (nuint)height, 0, (void*)pinnedHandle.AddrOfPinnedObject(), &err); err.Throw();
        var grayImage = _cl.CreateImage2D(OpenCLService.Context, MemFlags.ReadWrite, &grayFormat, (nuint)width, (nuint)height, 0, null, &err); err.Throw();

        var globalWorkSize = new nuint[] { (nuint)width, (nuint)height };
        _cl.SetKernelArg(_grayKernel, 0, (nuint)sizeof(IntPtr), srcImage);
        _cl.SetKernelArg(_grayKernel, 1, (nuint)sizeof(IntPtr), grayImage);
        fixed(nuint* pGlobal = globalWorkSize) { _cl.EnqueueNdrangeKernel(OpenCLService.CommandQueue, _grayKernel, 2, (nuint*)null, pGlobal, (nuint*)null, 0, null, null).Throw(); }

        var cornersBuffer = _cl.CreateBuffer(OpenCLService.Context, MemFlags.ReadWrite, (nuint)(MaxFeatures * 2 * sizeof(uint)), null, &err); err.Throw();
        var cornerCountBuffer = _cl.CreateBuffer(OpenCLService.Context, MemFlags.ReadWrite, (nuint)sizeof(int), null, &err); err.Throw();
        int zero = 0;
        _cl.EnqueueWriteBuffer(OpenCLService.CommandQueue, cornerCountBuffer, true, 0, (nuint)sizeof(int), &zero, 0, null, null).Throw();
        float fastThreshold = FastThreshold;
        int maxInitialFeatures = MaxFeatures * 2;
        _cl.SetKernelArg(_fastKernel, 0, (nuint)sizeof(IntPtr), grayImage);
        _cl.SetKernelArg(_fastKernel, 1, (nuint)sizeof(IntPtr), cornersBuffer);
        _cl.SetKernelArg(_fastKernel, 2, (nuint)sizeof(IntPtr), cornerCountBuffer);
        _cl.SetKernelArg(_fastKernel, 3, sizeof(int), &maxInitialFeatures);
        _cl.SetKernelArg(_fastKernel, 4, sizeof(float), &fastThreshold);
        fixed(nuint* pGlobal = globalWorkSize) { _cl.EnqueueNdrangeKernel(OpenCLService.CommandQueue, _fastKernel, 2, (nuint*)null, pGlobal, (nuint*)null, 0, null, null).Throw(); }

        int cornerCount = 0;
        _cl.EnqueueReadBuffer(OpenCLService.CommandQueue, cornerCountBuffer, true, 0, sizeof(int), &cornerCount, 0, null, null).Throw();
        if (cornerCount == 0) goto cleanup;
        
        uint[] packedCorners = new uint[cornerCount];
        fixed(uint* pCorners = packedCorners) { _cl.EnqueueReadBuffer(OpenCLService.CommandQueue, cornersBuffer, true, 0, (nuint)(cornerCount * sizeof(uint)), pCorners, 0, null, null).Throw(); }
        
        var finalPackedCorners = packedCorners.Distinct().Take(MaxFeatures).ToArray();
        cornerCount = finalPackedCorners.Length;
        fixed(uint* pFinalCorners = finalPackedCorners) { _cl.EnqueueWriteBuffer(OpenCLService.CommandQueue, cornersBuffer, true, 0, (nuint)(cornerCount * sizeof(uint)), pFinalCorners, 0, null, null).Throw(); }

        orientedKeypointsBuffer = _cl.CreateBuffer(OpenCLService.Context, MemFlags.ReadWrite, (nuint)(cornerCount * sizeof(Vector4)), null, &err); err.Throw();
        int patchSize = OrientationPatchSize;
        var orientWorkSize = new nuint[] {(nuint)cornerCount};
        _cl.SetKernelArg(_orientKernel, 0, (nuint)sizeof(IntPtr), grayImage);
        _cl.SetKernelArg(_orientKernel, 1, (nuint)sizeof(IntPtr), cornersBuffer);
        _cl.SetKernelArg(_orientKernel, 2, (nuint)sizeof(IntPtr), orientedKeypointsBuffer);
        _cl.SetKernelArg(_orientKernel, 3, sizeof(int), &cornerCount);
        _cl.SetKernelArg(_orientKernel, 4, sizeof(int), &patchSize);
        fixed(nuint* pOrient = orientWorkSize) { _cl.EnqueueNdrangeKernel(OpenCLService.CommandQueue, _orientKernel, 1, (nuint*)null, pOrient, (nuint*)null, 0, null, null).Throw(); }

        descriptorsBuffer = _cl.CreateBuffer(OpenCLService.Context, MemFlags.WriteOnly, (nuint)(cornerCount * 32), null, &err); err.Throw();
        var briefWorkSize = new nuint[] {(nuint)cornerCount};
        // CORRECTED: Pass the IntPtr directly, not its address.
        _cl.SetKernelArg(_briefKernel, 0, (nuint)sizeof(IntPtr), grayImage);
        _cl.SetKernelArg(_briefKernel, 1, (nuint)sizeof(IntPtr), orientedKeypointsBuffer);
        _cl.SetKernelArg(_briefKernel, 2, (nuint)sizeof(IntPtr), descriptorsBuffer);
        _cl.SetKernelArg(_briefKernel, 3, (nuint)sizeof(IntPtr), _patternBuffer);
        _cl.SetKernelArg(_briefKernel, 4, sizeof(int), &cornerCount);
        fixed(nuint* pBrief = briefWorkSize) { _cl.EnqueueNdrangeKernel(OpenCLService.CommandQueue, _briefKernel, 1, (nuint*)null, pBrief, (nuint*)null, 0, null, null).Throw(); }

        var finalKeypoints = new Vector4[cornerCount];
        var finalDescriptors = new byte[cornerCount * 32];
        fixed(void* pKeypoints = finalKeypoints, pDescriptors = finalDescriptors)
        {
            _cl.EnqueueReadBuffer(OpenCLService.CommandQueue, orientedKeypointsBuffer, true, 0, (nuint)(cornerCount * sizeof(Vector4)), pKeypoints, 0, null, null).Throw();
            _cl.EnqueueReadBuffer(OpenCLService.CommandQueue, descriptorsBuffer, true, 0, (nuint)(cornerCount * 32), pDescriptors, 0, null, null).Throw();
        }

        features.KeyPoints = finalKeypoints.Select(kp => new KeyPoint { X = kp.X, Y = kp.Y, Angle = kp.Z }).ToList();
        features.Descriptors = finalDescriptors;

    cleanup:
        _cl.ReleaseMemObject(srcImage);
        _cl.ReleaseMemObject(grayImage);
        _cl.ReleaseMemObject(cornersBuffer);
        _cl.ReleaseMemObject(cornerCountBuffer);
        if(orientedKeypointsBuffer != IntPtr.Zero) _cl.ReleaseMemObject(orientedKeypointsBuffer);
        if(descriptorsBuffer != IntPtr.Zero) _cl.ReleaseMemObject(descriptorsBuffer);
        pinnedHandle.Free();
        
        token.ThrowIfCancellationRequested();
        return features;
    }

    private DetectedFeatures DetectOnCpu(ImageDataset image, CancellationToken token)
    {
        var features = new DetectedFeatures();
        int width = image.Width; int height = image.Height;
        var grayPixels = new byte[width * height];
        for (int i = 0, j = 0; i < image.ImageData.Length; i += 4, j++)
            grayPixels[j] = (byte)(image.ImageData[i] * 0.299f + image.ImageData[i + 1] * 0.587f + image.ImageData[i + 2] * 0.114f);
        
        var fastCorners = new List<Point>();
        var fastOffsets = new Point[] {
            new(0, 3), new(1, 3), new(2, 2), new(3, 1), new(3, 0), new(3, -1), new(2, -2), new(1, -3),
            new(0, -3), new(-1, -3), new(-2, -2), new(-3, -1), new(-3, 0), new(-3, 1), new(-2, 2), new(-1, 3) };
        for (int y = 3; y < height - 3; y++) {
            for (int x = 3; x < width - 3; x++) {
                byte p = grayPixels[y * width + x];
                int upper = p + (int)(FastThreshold * 255);
                int lower = p - (int)(FastThreshold * 255);
                int continuous = 0;
                for (int i = 0; i < 25; i++) {
                    var offset = fastOffsets[i % 16];
                    byte val = grayPixels[(y + offset.Y) * width + (x + offset.X)];
                    if (val > upper || val < lower) continuous++; else continuous = 0;
                    if (continuous >= 9) { fastCorners.Add(new Point(x, y)); break; }
                }
            }
        }
        if (!fastCorners.Any()) return features;

        var scoredCorners = ScoreAndSuppressCorners(fastCorners, grayPixels, width, height, token);

        foreach (var corner in scoredCorners) {
            float m10 = 0.0f, m01 = 0.0f;
            int halfPatch = OrientationPatchSize / 2;
            for (int y = -halfPatch; y <= halfPatch; y++) {
                for (int x = -halfPatch; x <= halfPatch; x++) {
                    if (corner.X + x < 0 || corner.X + x >= width || corner.Y + y < 0 || corner.Y + y >= height) continue;
                    m10 += x * grayPixels[(corner.Y + y) * width + (corner.X + x)];
                    m01 += y * grayPixels[(corner.Y + y) * width + (corner.X + x)];
                }
            }
            features.KeyPoints.Add(new KeyPoint { X = corner.X, Y = corner.Y, Angle = (float)Math.Atan2(m01, m10) });
        }
        
        var descriptors = new List<byte>();
        var patterns = BriefPattern.GetPattern();
        foreach (var kp in features.KeyPoints) {
            token.ThrowIfCancellationRequested();
            float cosA = (float)Math.Cos(kp.Angle), sinA = (float)Math.Sin(kp.Angle);
            byte[] descriptor = new byte[32];
            for (int i = 0; i < 256; i++) {
                var p = patterns[i];
                float x1 = p.X*cosA - p.Y*sinA, y1 = p.X*sinA + p.Y*cosA;
                float x2 = p.Z*cosA - p.W*sinA, y2 = p.Z*sinA + p.W*cosA;
                if (GetInterpolatedValue(grayPixels, width, height, kp.X + x1, kp.Y + y1) < GetInterpolatedValue(grayPixels, width, height, kp.X + x2, kp.Y + y2))
                    descriptor[i / 8] |= (byte)(1 << (i % 8));
            }
            descriptors.AddRange(descriptor);
        }
        features.Descriptors = descriptors.ToArray();
        return features;
    }

    private List<Point> ScoreAndSuppressCorners(List<Point> corners, byte[] gray, int width, int height, CancellationToken token)
    {
        var scoredCorners = new List<(Point p, float score)>(corners.Count);
        float[] gradientsX = new float[width*height], gradientsY = new float[width*height];
        
        for (int y = 1; y < height - 1; y++) {
            for (int x = 1; x < width - 1; x++) {
                gradientsX[y*width+x] = (gray[(y-1)*width+x+1] + 2*gray[y*width+x+1] + gray[(y+1)*width+x+1]) - (gray[(y-1)*width+x-1] + 2*gray[y*width+x-1] + gray[(y+1)*width+x-1]);
                gradientsY[y*width+x] = (gray[(y+1)*width+x-1] + 2*gray[(y+1)*width+x] + gray[(y+1)*width+x+1]) - (gray[(y-1)*width+x-1] + 2*gray[(y-1)*width+x] + gray[(y-1)*width+x+1]);
            }
        }
        
        foreach (var c in corners) {
            token.ThrowIfCancellationRequested();
            float sumIxx = 0, sumIyy = 0, sumIxy = 0;
            for (int y = -3; y <= 3; y++) {
                for (int x = -3; x <= 3; x++) {
                    int sy = c.Y + y, sx = c.X + x;
                    if (sy < 0 || sy >= height || sx < 0 || sx >= width) continue;
                    float ix = gradientsX[sy*width+sx], iy = gradientsY[sy*width+sx];
                    sumIxx += ix * ix; sumIyy += iy * iy; sumIxy += ix * iy;
                }
            }
            float det = (sumIxx * sumIyy) - (sumIxy * sumIxy), trace = sumIxx + sumIyy;
            scoredCorners.Add((c, det - 0.04f * trace * trace));
        }

        scoredCorners.Sort((a, b) => b.score.CompareTo(a.score));
        var finalCorners = new List<Point>();
        var isSuppressedGrid = new bool[width * height];
        foreach (var (p, score) in scoredCorners) {
            token.ThrowIfCancellationRequested();
            if (!isSuppressedGrid[p.Y * width + p.X]) {
                finalCorners.Add(p);
                if (finalCorners.Count >= MaxFeatures) break;
                for (int y = -10; y <= 10; y++) {
                    for (int x = -10; x <= 10; x++) {
                        if (x*x + y*y <= 100) {
                            int sx = p.X+x, sy = p.Y+y;
                            if (sx >= 0 && sx < width && sy >= 0 && sy < height) isSuppressedGrid[sy * width + sx] = true;
                        }
                    }
                }
            }
        }
        return finalCorners;
    }

    private byte GetInterpolatedValue(byte[] s, int w, int h, float x, float y)
    {
        int xf = (int)x, yf = (int)y;
        if (xf < 0 || xf+1 >= w || yf < 0 || yf+1 >= h) return s[Math.Clamp((int)y,0,h-1)*w + Math.Clamp((int)x,0,w-1)];
        float xr = x-xf, yr = y-yf; int idx=yf*w+xf;
        float v00=s[idx], v10=s[idx+1], v01=s[idx+w], v11=s[idx+w+1];
        return (byte)((1-yr)*((1-xr)*v00+xr*v10) + yr*((1-xr)*v01+xr*v11));
    }

    public void Dispose()
    {
        if (_useOpenCL)
        {
            _cl.ReleaseMemObject(_patternBuffer);
            _cl.ReleaseKernel(_grayKernel);
            _cl.ReleaseKernel(_fastKernel);
            _cl.ReleaseKernel(_orientKernel);
            _cl.ReleaseKernel(_briefKernel);
            _cl.ReleaseProgram(_program);
        }
    }
    
    private struct Point { public int X, Y; public Point(int x, int y) { X=x; Y=y; } }

    private static class BriefPattern
    {
        private static Vector4i[] _pattern;
        public static Vector4i[] GetPattern() => _pattern ??= Generate();
        private static Vector4i[] Generate()
        {
            var p = new Vector4i[256];
            var r = new Random(12345);
            for (int i = 0; i < p.Length; i++)
                p[i] = new Vector4i(r.Next(-15, 16), r.Next(-15, 16), r.Next(-15, 16), r.Next(-15, 16));
            return p;
        }
    }
}