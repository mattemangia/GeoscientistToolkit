// GeoscientistToolkit/Business/Panorama/FeatureMatcherCL.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.Photogrammetry.Math;
using GeoscientistToolkit.Util;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Business.Panorama;

public class FeatureMatcherCL : IDisposable
{
    private readonly CL _cl;
    private nint _context;
    private nint _commandQueue;
    private nint _program;
    private nint _kernel;
    private bool _disposed;
    
    private const float MATCH_RATIO_THRESHOLD = 0.75f; // Standard Lowe's ratio
    
    private const string MatcherKernel = @"
// Using popcount for efficient hamming distance is better if available, but this is functionally correct.
int countSetBits(uchar n)
{
    int count = 0;
    while (n > 0) {
        n &= (n - 1);
        count++;
    }
    return count;
}

__kernel void match_features(
    __global const uchar* descriptors1,
    __global const uchar* descriptors2,
    const int count1,
    const int count2,
    const int descriptor_size,
    __global int* best_matches,
    __global float* best_distances,
    __global float* second_distances)
{
    int idx = get_global_id(0);
    if (idx >= count1) return;
    
    int desc1_offset = idx * descriptor_size;
    float best_dist = FLT_MAX;
    float second_dist = FLT_MAX;
    int best_idx = -1;
    
    for (int j = 0; j < count2; j++) {
        int desc2_offset = j * descriptor_size;
        
        int distance = 0;
        for (int k = 0; k < descriptor_size; k++) {
            uchar xor_val = descriptors1[desc1_offset + k] ^ descriptors2[desc2_offset + k];
            distance += countSetBits(xor_val);
        }
        
        float dist = (float)distance;
        if (dist < best_dist) {
            second_dist = best_dist;
            best_dist = dist;
            best_idx = j;
        } else if (dist < second_dist) {
            second_dist = dist;
        }
    }
    
    best_matches[idx] = best_idx;
    best_distances[idx] = best_dist;
    second_distances[idx] = second_dist;
}";
    
    public unsafe FeatureMatcherCL()
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
        
        var kernelPtr = Marshal.StringToCoTaskMemUTF8(MatcherKernel);
        var length = (nuint)MatcherKernel.Length;
        _program = _cl.CreateProgramWithSource(_context, 1, (byte**)&kernelPtr, &length, &error);
        CheckError(error, "Failed to create program");
        
        error = _cl.BuildProgram(_program, 1, &device, (byte*)null, null, null);
        if (error != (int)ErrorCodes.Success)
        {
            byte* buildLog = stackalloc byte[4096];
            nuint logSize;
            _cl.GetProgramBuildInfo(_program, device, ProgramBuildInfo.BuildLog, 4096, buildLog, &logSize);
            var log = Marshal.PtrToStringUTF8((nint)buildLog);
            throw new Exception($"Failed to build program: {log}");
        }
        
        var kernelNamePtr = Marshal.StringToCoTaskMemUTF8("match_features");
        _kernel = _cl.CreateKernel(_program, (byte*)kernelNamePtr, &error);
        Marshal.FreeCoTaskMem(kernelNamePtr);
        CheckError(error, "Failed to create kernel");
        
        Marshal.FreeCoTaskMem(kernelPtr);
    }

    public Task<List<FeatureMatch>> MatchFeaturesAsync(DetectedFeatures features1, DetectedFeatures features2, CancellationToken token)
    {
        return Task.Run(() => MatchFeaturesGPU(features1, features2, token), token);
    }
    
    private unsafe List<FeatureMatch> MatchFeaturesGPU(DetectedFeatures features1, DetectedFeatures features2, CancellationToken token)
    {
        var matches = new List<FeatureMatch>();
        if (features1.KeyPoints.Count == 0 || features2.KeyPoints.Count == 0) return matches;

        int n1 = features1.KeyPoints.Count;
        int n2 = features2.KeyPoints.Count;
        const int ds = 32;

        nint d1 = 0, d2 = 0, best12Buf = 0, dist12Buf = 0, sec12Buf = 0;
        try
        {
            int err;
            fixed (byte* p1 = features1.Descriptors)
            fixed (byte* p2 = features2.Descriptors)
            {
                d1 = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(n1 * ds), p1, &err); CheckError(err, "d1");
                d2 = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(n2 * ds), p2, &err); CheckError(err, "d2");
            }
            
            best12Buf = _cl.CreateBuffer(_context, MemFlags.WriteOnly, (nuint)(n1 * sizeof(int)), null, &err); CheckError(err, "best12");
            dist12Buf = _cl.CreateBuffer(_context, MemFlags.WriteOnly, (nuint)(n1 * sizeof(float)), null, &err); CheckError(err, "dist12");
            sec12Buf = _cl.CreateBuffer(_context, MemFlags.WriteOnly, (nuint)(n1 * sizeof(float)), null, &err); CheckError(err, "sec12");
            var _ds = ds;
            _cl.SetKernelArg(_kernel, 0, (nuint)sizeof(nint), &d1);
            _cl.SetKernelArg(_kernel, 1, (nuint)sizeof(nint), &d2);
            _cl.SetKernelArg(_kernel, 2, (nuint)sizeof(int), &n1);
            _cl.SetKernelArg(_kernel, 3, (nuint)sizeof(int), &n2);
            _cl.SetKernelArg(_kernel, 4, (nuint)sizeof(int), &_ds);
            _cl.SetKernelArg(_kernel, 5, (nuint)sizeof(nint), &best12Buf);
            _cl.SetKernelArg(_kernel, 6, (nuint)sizeof(nint), &dist12Buf);
            _cl.SetKernelArg(_kernel, 7, (nuint)sizeof(nint), &sec12Buf);
            
            nuint gws = (nuint)n1;
            _cl.EnqueueNdrangeKernel(_commandQueue, _kernel, 1, null, &gws, null, 0, null, null);

            var bestMatches = new int[n1];
            var bestDists = new float[n1];
            var secondDists = new float[n1];
            
            fixed (int* pBest = bestMatches) fixed (float* pBdist = bestDists) fixed (float* pSdist = secondDists)
            {
                _cl.EnqueueReadBuffer(_commandQueue, best12Buf, true, 0, (nuint)(n1 * sizeof(int)), pBest, 0, null, null);
                _cl.EnqueueReadBuffer(_commandQueue, dist12Buf, true, 0, (nuint)(n1 * sizeof(float)), pBdist, 0, null, null);
                _cl.EnqueueReadBuffer(_commandQueue, sec12Buf, true, 0, (nuint)(n1 * sizeof(float)), pSdist, 0, null, null);
            }
            _cl.Finish(_commandQueue);
            
            for (int i = 0; i < n1; i++)
            {
                if (bestMatches[i] >= 0 && bestDists[i] < secondDists[i] * MATCH_RATIO_THRESHOLD)
                {
                    matches.Add(new FeatureMatch { QueryIndex = i, TrainIndex = bestMatches[i], Distance = bestDists[i] });
                }
            }
            return matches;
        }
        finally
        {
            if (d1 != 0) _cl.ReleaseMemObject(d1);
            if (d2 != 0) _cl.ReleaseMemObject(d2);
            if (best12Buf != 0) _cl.ReleaseMemObject(best12Buf);
            if (dist12Buf != 0) _cl.ReleaseMemObject(dist12Buf);
            if (sec12Buf != 0) _cl.ReleaseMemObject(sec12Buf);
        }
    }

    private static bool ArePointsDegenerate(IReadOnlyList<(float x, float y)> points)
    {
        const float colinearityThreshold = 1e-3f;
        if (points.Count < 3) return false;

        bool IsCollinear((float x, float y) p1, (float x, float y) p2, (float x, float y) p3)
        {
            float area = Math.Abs(p1.x * (p2.y - p3.y) + p2.x * (p3.y - p1.y) + p3.x * (p1.y - p2.y));
            return area < colinearityThreshold;
        }

        if (points.Count >= 4)
        {
            // Check if any three of the four points are collinear
            return IsCollinear(points[0], points[1], points[2]) ||
                   IsCollinear(points[0], points[1], points[3]) ||
                   IsCollinear(points[0], points[2], points[3]) ||
                   IsCollinear(points[1], points[2], points[3]);
        }
        return false;
    }
    
    public static (Matrix3x3? H, List<FeatureMatch> Inliers) FindHomographyRANSAC(
        List<FeatureMatch> matches,
        List<KeyPoint> kpts1,
        List<KeyPoint> kpts2,
        int maxIters = 2000,
        float reprojThresh = 5.0f) 
    {
        if (matches.Count < 4) return (null, new List<FeatureMatch>());

        var rnd = new Random();
        Matrix3x3? bestH = null;
        List<FeatureMatch> bestInliers = new List<FeatureMatch>();

        var pts1 = matches.Select(m => (kpts1[m.QueryIndex].X, kpts1[m.QueryIndex].Y)).ToArray();
        var pts2 = matches.Select(m => (kpts2[m.TrainIndex].X, kpts2[m.TrainIndex].Y)).ToArray();

        for (int it = 0; it < maxIters; it++)
        {
            var sampleIndices = new HashSet<int>();
            while (sampleIndices.Count < 4) sampleIndices.Add(rnd.Next(matches.Count));
            
            var srcSample = new List<(float x, float y)>(4);
            var dstSample = new List<(float x, float y)>(4);
            foreach (int idx in sampleIndices)
            {
                // Note: Tuples created with .Select have default names Item1, Item2
                srcSample.Add((pts1[idx].Item1, pts1[idx].Item2));
                dstSample.Add((pts2[idx].Item1, pts2[idx].Item2));
            }
            
            if (ArePointsDegenerate(srcSample) || ArePointsDegenerate(dstSample)) continue;
            
            if (!SolveHomographyLinearNormalized(srcSample, dstSample, out var H_cand)) continue;
            
            var det = Matrix3x3.Determinant(H_cand);
            if (float.IsNaN(det) || Math.Abs(det) < 0.01f || Math.Abs(det) > 100.0f) continue;

            var currentInliers = new List<FeatureMatch>();
            float reprojThreshSq = reprojThresh * reprojThresh;

            for (int i = 0; i < matches.Count; i++)
            {
                // *** FIX: Access tuple elements with .Item1 and .Item2 ***
                float sx = pts1[i].Item1, sy = pts1[i].Item2;
                float w = H_cand.M31 * sx + H_cand.M32 * sy + H_cand.M33;
                if (Math.Abs(w) < 1e-8f) continue;
                
                // *** FIX: Access tuple elements with .Item1 and .Item2 ***
                float dx = (H_cand.M11 * sx + H_cand.M12 * sy + H_cand.M13) / w - pts2[i].Item1;
                float dy = (H_cand.M21 * sx + H_cand.M22 * sy + H_cand.M23) / w - pts2[i].Item2;

                if (dx * dx + dy * dy < reprojThreshSq)
                {
                    currentInliers.Add(matches[i]);
                }
            }

            if (currentInliers.Count > bestInliers.Count)
            {
                bestInliers = currentInliers;
                bestH = H_cand;
            }
        }

        if (bestInliers.Count >= 4)
        {
            var srcAllInliers = bestInliers.Select(m => (kpts1[m.QueryIndex].X, kpts1[m.QueryIndex].Y)).ToList();
            var dstAllInliers = bestInliers.Select(m => (kpts2[m.TrainIndex].X, kpts2[m.TrainIndex].Y)).ToList();
            if (SolveHomographyLinearNormalized(srcAllInliers, dstAllInliers, out var H_refit))
            {
                return (H_refit, bestInliers);
            }
        }
        
        return (bestH, bestInliers);
    }
    
    internal static bool SolveHomographyLinearNormalized(
        IReadOnlyList<(float x, float y)> src,
        IReadOnlyList<(float x, float y)> dst,
        out Matrix3x3 H)
    {
        H = default;
        int n = src.Count;
        if (n < 4) return false;
        
        Normalize2D(src, out var Ts, out var Ns);
        Normalize2D(dst, out var Td, out var Nd);
        
        var A = new double[2 * n, 8];
        var b = new double[2 * n];

        for (int i = 0; i < n; i++)
        {
            double x = Ns[i].x, y = Ns[i].y;
            double X = Nd[i].x, Y = Nd[i].y;

            int r = 2 * i;
            A[r, 0] = x;   A[r, 1] = y;   A[r, 2] = 1;   A[r, 3] = 0;   A[r, 4] = 0;   A[r, 5] = 0;   A[r, 6] = -X * x; A[r, 7] = -X * y;
            b[r] = X;
            A[r+1,0] = 0;  A[r+1,1] = 0;  A[r+1,2] = 0;  A[r+1,3] = x;  A[r+1,4] = y;  A[r+1,5] = 1;  A[r+1,6] = -Y * x; A[r+1,7] = -Y * y;
            b[r+1] = Y;
        }

        if (!SolveLeastSquares(A, b, out var h)) return false;

        var Hn = new Matrix3x3(
            (float)h[0], (float)h[1], (float)h[2],
            (float)h[3], (float)h[4], (float)h[5],
            (float)h[6], (float)h[7], 1f);

        if (!Matrix3x3.Invert(Td, out var Tdinv)) return false;
        H = Tdinv * Hn * Ts;
        
        if (Math.Abs(H.M33) > 1e-9f)
        {
            var s = 1.0f / H.M33;
            H = new Matrix3x3(H.M11*s, H.M12*s, H.M13*s, H.M21*s, H.M22*s, H.M23*s, H.M31*s, H.M32*s, 1f);
        }

        return true;
    }

    private static void Normalize2D(IReadOnlyList<(float x, float y)> P, out Matrix3x3 T, out List<(float x, float y)> Q)
    {
        int n = P.Count;
        double mx = 0, my = 0;
        foreach (var p in P) { mx += p.x; my += p.y; }
        mx /= n; my /= n;

        double meanDist = P.Select(p => { double dx = p.x - mx, dy = p.y - my; return Math.Sqrt(dx * dx + dy * dy); }).Average();
        double s = (meanDist < 1e-8) ? 1.0 : Math.Sqrt(2.0) / meanDist;

        T = new Matrix3x3((float)s, 0, (float)(-s * mx), 0, (float)s, (float)(-s * my), 0, 0, 1);
        Q = P.Select(p => ((float)(s * (p.x - mx)), (float)(s * (p.y - my)))).ToList();
    }
    
    private static bool SolveLeastSquares(double[,] A, double[] b, out double[] x)
    {
        int rows = A.GetLength(0);
        int cols = A.GetLength(1);
        x = new double[cols];
        
        var AtA = new double[cols, cols];
        for (int i = 0; i < cols; i++)
        for (int j = 0; j < cols; j++)
        {
            double sum = 0;
            for (int k = 0; k < rows; k++) sum += A[k, i] * A[k, j];
            AtA[i, j] = sum;
        }
        
        var Atb = new double[cols];
        for (int i = 0; i < cols; i++)
        {
            double sum = 0;
            for (int k = 0; k < rows; k++) sum += A[k, i] * b[k];
            Atb[i] = sum;
        }

        return SolveLinearSystem(AtA, Atb, out x);
    }
    
    private static bool SolveLinearSystem(double[,] A, double[] b, out double[] x)
    {
        int n = b.Length;
        x = new double[n];
        const double epsilon = 1e-10;

        for (int p = 0; p < n; p++)
        {
            int max = p;
            for (int i = p + 1; i < n; i++)
                if (Math.Abs(A[i, p]) > Math.Abs(A[max, p])) max = i;
            
            (A, b) = SwapRows(A, b, p, max);
            if (Math.Abs(A[p, p]) <= epsilon) return false;

            for (int i = p + 1; i < n; i++)
            {
                double alpha = A[i, p] / A[p, p];
                b[i] -= alpha * b[p];
                for (int j = p; j < n; j++) A[i, j] -= alpha * A[p, j];
            }
        }

        for (int i = n - 1; i >= 0; i--)
        {
            double sum = 0.0;
            for (int j = i + 1; j < n; j++) sum += A[i, j] * x[j];
            x[i] = (b[i] - sum) / A[i, i];
        }
        return true;
    }
    
    private static (double[,], double[]) SwapRows(double[,] A, double[] b, int r1, int r2) {
        if (r1 == r2) return (A, b);
        int cols = A.GetLength(1);
        for (int i = 0; i < cols; i++) (A[r1, i], A[r2, i]) = (A[r2, i], A[r1, i]);
        (b[r1], b[r2]) = (b[r2], b[r1]);
        return (A, b);
    }
    
    private void CheckError(int error, string message)
    {
        if (error != (int)ErrorCodes.Success)
            throw new Exception($"{message}. Error code: {(ErrorCodes)error}");
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        
        if(_kernel != 0) _cl.ReleaseKernel(_kernel);
        if(_program != 0) _cl.ReleaseProgram(_program);
        if(_commandQueue != 0) _cl.ReleaseCommandQueue(_commandQueue);
        if(_context != 0) _cl.ReleaseContext(_context);
        
        _disposed = true;
    }
}