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
    
    // Adjusted parameters for more robust matching
    private const float MATCH_RATIO_THRESHOLD = 0.85f;
    private const float RANSAC_REPROJ_THRESHOLD = 10.0f; // Increased from 5.0f to be more tolerant
    
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
        
        // Platform and device setup... (omitted for brevity, remains unchanged)
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
    private List<FeatureMatch> CrossCheckFilter(
        int[] best12, float[] d12, int[] best21, float[] d21, float ratio)
    {
        var res = new List<FeatureMatch>();
        for (int i = 0; i < best12.Length; i++)
        {
            int j = best12[i];
            if (j < 0) continue;
            // mutual best
            if (best21[j] == i)
            {
                // Lowe ratio on both directions if second-best distances are valid
                bool pass = true;
                // Optional: only if we read second bests; here we infer via sent arrays
                if (d12[i * 2 + 1] > 0) pass &= (d12[i * 2 + 0] / d12[i * 2 + 1]) < ratio;
                if (d21[j * 2 + 1] > 0) pass &= (d21[j * 2 + 0] / d21[j * 2 + 1]) < ratio;

                if (pass)
                    res.Add(new FeatureMatch { QueryIndex = i, TrainIndex = j, Distance = d12[i * 2 + 0] });
            }
        }
        return res;
    }

    private unsafe List<FeatureMatch> MatchFeaturesGPU(DetectedFeatures features1, DetectedFeatures features2, CancellationToken token)
{
    var matches = new List<FeatureMatch>();
    if (features1.KeyPoints.Count == 0 || features2.KeyPoints.Count == 0) return matches;

    int n1 = features1.KeyPoints.Count;
    int n2 = features2.KeyPoints.Count;
    int ds = 32;

    nint d1 = 0, d2 = 0, best12Buf = 0, best12Dist = 0, sec12Dist = 0, best21Buf = 0, best21Dist = 0, sec21Dist = 0;
    try
    {
        int err;
        fixed (byte* p1 = features1.Descriptors)
        fixed (byte* p2 = features2.Descriptors)
        {
            d1 = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(n1 * ds), p1, &err); CheckError(err, "desc1");
            d2 = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(n2 * ds), p2, &err); CheckError(err, "desc2");
        }

        best12Buf = _cl.CreateBuffer(_context, MemFlags.WriteOnly, (nuint)(n1 * sizeof(int)), null, &err); CheckError(err, "best12");
        best12Dist = _cl.CreateBuffer(_context, MemFlags.WriteOnly, (nuint)(n1 * sizeof(float)), null, &err); CheckError(err, "best12d");
        sec12Dist  = _cl.CreateBuffer(_context, MemFlags.WriteOnly, (nuint)(n1 * sizeof(float)), null, &err); CheckError(err, "sec12d");

        // 1 -> 2
        _cl.SetKernelArg(_kernel, 0, (nuint)sizeof(nint), &d1);
        _cl.SetKernelArg(_kernel, 1, (nuint)sizeof(nint), &d2);
        _cl.SetKernelArg(_kernel, 2, (nuint)sizeof(int), &n1);
        _cl.SetKernelArg(_kernel, 3, (nuint)sizeof(int), &n2);
        _cl.SetKernelArg(_kernel, 4, (nuint)sizeof(int), &ds);
        _cl.SetKernelArg(_kernel, 5, (nuint)sizeof(nint), &best12Buf);
        _cl.SetKernelArg(_kernel, 6, (nuint)sizeof(nint), &best12Dist);
        _cl.SetKernelArg(_kernel, 7, (nuint)sizeof(nint), &sec12Dist);
        nuint gws = (nuint)n1;
        _cl.EnqueueNdrangeKernel(_commandQueue, _kernel, 1, null, &gws, null, 0, null, null);

        // 2 -> 1
        best21Buf = _cl.CreateBuffer(_context, MemFlags.WriteOnly, (nuint)(n2 * sizeof(int)), null, &err); CheckError(err, "best21");
        best21Dist = _cl.CreateBuffer(_context, MemFlags.WriteOnly, (nuint)(n2 * sizeof(float)), null, &err); CheckError(err, "best21d");
        sec21Dist  = _cl.CreateBuffer(_context, MemFlags.WriteOnly, (nuint)(n2 * sizeof(float)), null, &err); CheckError(err, "sec21d");

        _cl.SetKernelArg(_kernel, 0, (nuint)sizeof(nint), &d2);
        _cl.SetKernelArg(_kernel, 1, (nuint)sizeof(nint), &d1);
        _cl.SetKernelArg(_kernel, 2, (nuint)sizeof(int), &n2);
        _cl.SetKernelArg(_kernel, 3, (nuint)sizeof(int), &n1);
        _cl.SetKernelArg(_kernel, 4, (nuint)sizeof(int), &ds);
        _cl.SetKernelArg(_kernel, 5, (nuint)sizeof(nint), &best21Buf);
        _cl.SetKernelArg(_kernel, 6, (nuint)sizeof(nint), &best21Dist);
        _cl.SetKernelArg(_kernel, 7, (nuint)sizeof(nint), &sec21Dist);
        gws = (nuint)n2;
        _cl.EnqueueNdrangeKernel(_commandQueue, _kernel, 1, null, &gws, null, 0, null, null);

        // read back
        var best12 = new int[n1];   var b12 = new float[n1]; var s12 = new float[n1];
        var best21 = new int[n2];   var b21 = new float[n2]; var s21 = new float[n2];

        fixed (int* pm12 = best12) fixed (float* pd12 = b12) fixed (float* ps12 = s12)
        fixed (int* pm21 = best21) fixed (float* pd21 = b21) fixed (float* ps21 = s21)
        {
            _cl.EnqueueReadBuffer(_commandQueue, best12Buf, true, 0, (nuint)(n1 * sizeof(int)), pm12, 0, null, null);
            _cl.EnqueueReadBuffer(_commandQueue, best12Dist, true, 0, (nuint)(n1 * sizeof(float)), pd12, 0, null, null);
            _cl.EnqueueReadBuffer(_commandQueue, sec12Dist, true, 0, (nuint)(n1 * sizeof(float)), ps12, 0, null, null);

            _cl.EnqueueReadBuffer(_commandQueue, best21Buf, true, 0, (nuint)(n2 * sizeof(int)), pm21, 0, null, null);
            _cl.EnqueueReadBuffer(_commandQueue, best21Dist, true, 0, (nuint)(n2 * sizeof(float)), pd21, 0, null, null);
            _cl.EnqueueReadBuffer(_commandQueue, sec21Dist, true, 0, (nuint)(n2 * sizeof(float)), ps21, 0, null, null);
        }

        // pack (best, second) so CrossCheckFilter can do ratio both ways
        var d12Flat = new float[n1 * 2];
        for (int i = 0; i < n1; i++) { d12Flat[i * 2 + 0] = b12[i]; d12Flat[i * 2 + 1] = s12[i]; }
        var d21Flat = new float[n2 * 2];
        for (int j = 0; j < n2; j++) { d21Flat[j * 2 + 0] = b21[j]; d21Flat[j * 2 + 1] = s21[j]; }

        matches = CrossCheckFilter(best12, d12Flat, best21, d21Flat, MATCH_RATIO_THRESHOLD);
        return matches;
    }
    finally
    {
        if (d1 != 0) _cl.ReleaseMemObject(d1);
        if (d2 != 0) _cl.ReleaseMemObject(d2);
        if (best12Buf != 0) _cl.ReleaseMemObject(best12Buf);
        if (best12Dist != 0) _cl.ReleaseMemObject(best12Dist);
        if (sec12Dist  != 0) _cl.ReleaseMemObject(sec12Dist);
        if (best21Buf != 0) _cl.ReleaseMemObject(best21Buf);
        if (best21Dist != 0) _cl.ReleaseMemObject(best21Dist);
        if (sec21Dist  != 0) _cl.ReleaseMemObject(sec21Dist);
    }
}


    public static (Matrix3x3? H, List<FeatureMatch> Inliers) FindHomographyRANSAC(
    List<FeatureMatch> matches,
    List<KeyPoint> kpts1,
    List<KeyPoint> kpts2,
    int maxIters = 2000,
    float reprojThresh = 8.0f,    // <- loosened; you can tune down later
    float inlierRatioForRefit = 0.5f,
    int randomSeed = 1337)
{
    if (matches.Count < 4) return (null, new List<FeatureMatch>());

    var rnd = new Random(randomSeed);
    Matrix3x3? bestH = null;
    int bestInliers = 0;
    var bestSet = new List<FeatureMatch>();

    // cache points  (KeyPoint has X/Y, not .Point)
    var pts1 = new (float x, float y)[matches.Count];
    var pts2 = new (float x, float y)[matches.Count];
    for (int i = 0; i < matches.Count; i++)
    {
        var m  = matches[i];
        var k1 = kpts1[m.QueryIndex];
        var k2 = kpts2[m.TrainIndex];
        pts1[i] = (k1.X, k1.Y);
        pts2[i] = (k2.X, k2.Y);
    }

    // RANSAC
    for (int it = 0; it < maxIters; it++)
    {
        // sample 4 unique indices
        var pick = new HashSet<int>();
        while (pick.Count < 4) pick.Add(rnd.Next(matches.Count));
        var idx = pick.ToArray();

        // build minimal subsets
        var A = new List<(float x, float y)>(4);
        var B = new List<(float x, float y)>(4);
        for (int i = 0; i < 4; i++) { int ii = idx[i]; A.Add(pts1[ii]); B.Add(pts2[ii]); }

        if (!SolveHomographyLinearNormalized(A, B, out var Hcand)) continue;

        var inliers = ScoreHomography(Hcand, pts1, pts2, reprojThresh);
        if (inliers.Count > bestInliers)
        {
            bestInliers = inliers.Count;
            bestH = Hcand;
            bestSet.Clear();
            foreach (int ii in inliers) bestSet.Add(matches[ii]);

            if (bestInliers >= Math.Max(4, (int)(matches.Count * 0.8))) break;
        }
    }

    if (bestH == null) return (null, new List<FeatureMatch>());

    // Refit on all inliers with stable LS DLT (keeps full projective DoF => visible roll)
    var inlierIdx = new List<int>(bestSet.Count);
    for (int i = 0; i < matches.Count; i++)
        if (bestSet.Contains(matches[i])) inlierIdx.Add(i);

    if (inlierIdx.Count >= 4)
    {
        var A = new List<(float x, float y)>(inlierIdx.Count);
        var B = new List<(float x, float y)>(inlierIdx.Count);
        foreach (var ii in inlierIdx) { A.Add(pts1[ii]); B.Add(pts2[ii]); }

        if (SolveHomographyLinearNormalized(A, B, out var Hrefit))
        {
            bestH = Hrefit;
            var rescored = ScoreHomography(Hrefit, pts1, pts2, reprojThresh);
            bestSet = new List<FeatureMatch>(rescored.Count);
            foreach (int ii in rescored) bestSet.Add(matches[ii]);
        }
    }

    return (bestH, bestSet);

    // ---------- helpers ----------

    static List<int> ScoreHomography(Matrix3x3 H, (float x, float y)[] a, (float x, float y)[] b, float thr)
    {
        var ok = new List<int>(a.Length);
        float thr2 = thr * thr;
        for (int i = 0; i < a.Length; i++)
        {
            float x = a[i].x, y = a[i].y;
            float X = H.M11 * x + H.M12 * y + H.M13;
            float Y = H.M21 * x + H.M22 * y + H.M23;
            float W = H.M31 * x + H.M32 * y + H.M33;
            if (Math.Abs(W) < 1e-8f) continue;
            X /= W; Y /= W;
            float dx = X - b[i].x, dy = Y - b[i].y;
            if (dx * dx + dy * dy <= thr2) ok.Add(i);
        }
        return ok;
    }

    // Normalized DLT with h33=1 and an 8x8 linear solve (LS if >4 points)
    static bool SolveHomographyLinearNormalized(
        IReadOnlyList<(float x, float y)> src,
        IReadOnlyList<(float x, float y)> dst,
        out Matrix3x3 H)
    {
        H = default;
        int n = src.Count;
        if (n < 4) return false;

        // Hartley-like similarity normalization
        Normalize2D(src, out var Ts, out var Ns);
        Normalize2D(dst, out var Td, out var Nd);

        // Build linear system A*h = b with h=[h11 h12 h13 h21 h22 h23 h31 h32]^T (h33=1)
        // For each (x,y)->(X,Y):
        //  x' = (h11 x + h12 y + h13) / (h31 x + h32 y + 1)
        //  y' = (h21 x + h22 y + h23) / (h31 x + h32 y + 1)
        //  =>
        //  x'(h31 x + h32 y + 1) = h11 x + h12 y + h13
        //  y'(h31 x + h32 y + 1) = h21 x + h22 y + h23
        // Arrange into 2 equations per point: A (8) unknowns, b (2N)

        // If N==4 -> directly solve 8x8; else -> normal equations (A^T A) h = A^T b
        var A = new double[2 * n, 8];
        var b = new double[2 * n];

        for (int i = 0; i < n; i++)
        {
            double x = Ns[i].x, y = Ns[i].y;
            double X = Nd[i].x, Y = Nd[i].y;

            int r = 2 * i;
            // X*(h31 x + h32 y + 1) = h11 x + h12 y + h13
            A[r, 0] = x;   A[r, 1] = y;   A[r, 2] = 1;   A[r, 3] = 0;   A[r, 4] = 0;   A[r, 5] = 0;   A[r, 6] = -X * x; A[r, 7] = -X * y;
            b[r] = X;
            // Y*(h31 x + h32 y + 1) = h21 x + h22 y + h23
            A[r+1,0] = 0;  A[r+1,1] = 0;  A[r+1,2] = 0;  A[r+1,3] = x;  A[r+1,4] = y;  A[r+1,5] = 1;  A[r+1,6] = -Y * x; A[r+1,7] = -Y * y;
            b[r+1] = Y;
        }

        double[] h; // 8 unknowns
        if (n == 4)
        {
            h = Solve8(A, b);                // 8x8 exact
            if (h == null) return false;
        }
        else
        {
            // normal equations
            var AtA = new double[8, 8];
            var Atb = new double[8];
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    double acc = 0;
                    for (int k = 0; k < 2 * n; k++) acc += A[k, r] * A[k, c];
                    AtA[r, c] = acc;
                }
                double accb = 0;
                for (int k = 0; k < 2 * n; k++) accb += A[k, r] * b[k];
                Atb[r] = accb;
            }
            h = SolveSym8(AtA, Atb);
            if (h == null) return false;
        }

        // assemble normalized H (with h33=1)
        var Hn = new Matrix3x3(
            (float)h[0], (float)h[1], (float)h[2],
            (float)h[3], (float)h[4], (float)h[5],
            (float)h[6], (float)h[7], 1f);

        // denormalize: H = Td^{-1} * Hn * Ts
        if (!Matrix3x3.Invert(Td, out var Tdinv)) return false;
        H = Tdinv * Hn * Ts;

        // normalize so H[2,2]=1
        if (Math.Abs(H.M33) > 1e-12f)
        {
            float s = H.M33;
            H = new Matrix3x3(
                H.M11/s, H.M12/s, H.M13/s,
                H.M21/s, H.M22/s, H.M23/s,
                H.M31/s, H.M32/s, 1f);
        }
        return true;
    }

    static void Normalize2D(IReadOnlyList<(float x, float y)> P, out Matrix3x3 T, out List<(float x, float y)> Q)
    {
        int n = P.Count;
        double mx = 0, my = 0;
        for (int i = 0; i < n; i++) { mx += P[i].x; my += P[i].y; }
        mx /= n; my /= n;

        double meanDist = 0;
        for (int i = 0; i < n; i++)
        {
            double dx = P[i].x - mx, dy = P[i].y - my;
            meanDist += Math.Sqrt(dx * dx + dy * dy);
        }
        meanDist = (meanDist / n);
        double s = (meanDist < 1e-8) ? 1.0 : Math.Sqrt(2.0) / meanDist;

        T = new Matrix3x3(
            (float)s, 0, (float)(-s * mx),
            0, (float)s, (float)(-s * my),
            0, 0, 1);
        Q = new List<(float x, float y)>(n);
        for (int i = 0; i < n; i++)
        {
            float x = (float)(s * (P[i].x - mx));
            float y = (float)(s * (P[i].y - my));
            Q.Add((x, y));
        }
    }

    // Exact 8x8 solve via Gauss-Jordan (for N==4)
    static double[] Solve8(double[,] A, double[] b)
    {
        var M = new double[8, 9]; // [A|b]
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++) M[r, c] = A[r, c];
            M[r, 8] = b[r];
        }
        for (int col = 0; col < 8; col++)
        {
            // pivot
            int piv = col;
            for (int r = col + 1; r < 8; r++)
                if (Math.Abs(M[r, col]) > Math.Abs(M[piv, col])) piv = r;
            if (Math.Abs(M[piv, col]) < 1e-12) return null;
            if (piv != col) for (int k = col; k <= 8; k++) (M[col, k], M[piv, k]) = (M[piv, k], M[col, k]);

            // normalize
            double d = M[col, col];
            for (int k = col; k <= 8; k++) M[col, k] /= d;

            // eliminate
            for (int r = 0; r < 8; r++)
            {
                if (r == col) continue;
                double f = M[r, col];
                if (Math.Abs(f) < 1e-18) continue;
                for (int k = col; k <= 8; k++) M[r, k] -= f * M[col, k];
            }
        }
        var x = new double[8];
        for (int i = 0; i < 8; i++) x[i] = M[i, 8];
        return x;
    }

    // Symmetric 8x8 solve for normal equations (A^T A h = A^T b)
    static double[] SolveSym8(double[,] S, double[] y)
    {
        // Cholesky-like decomposition with small regularization
        const double eps = 1e-8;
        for (int i = 0; i < 8; i++) S[i, i] += eps;

        var L = new double[8, 8];
        for (int i = 0; i < 8; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double sum = S[i, j];
                for (int k = 0; k < j; k++) sum -= L[i, k] * L[j, k];
                if (i == j)
                {
                    if (sum <= 0) return null;
                    L[i, j] = Math.Sqrt(sum);
                }
                else
                {
                    L[i, j] = sum / L[j, j];
                }
            }
        }
        // solve L z = y
        var z = new double[8];
        for (int i = 0; i < 8; i++)
        {
            double sum = y[i];
            for (int k = 0; k < i; k++) sum -= L[i, k] * z[k];
            z[i] = sum / L[i, i];
        }
        // solve L^T x = z
        var x = new double[8];
        for (int i = 7; i >= 0; i--)
        {
            double sum = z[i];
            for (int k = i + 1; k < 8; k++) sum -= L[k, i] * x[k];
            x[i] = sum / L[i, i];
        }
        return x;
    }
}

    
    #region Homography Calculation
    private static Matrix3x3? ComputeHomography(Vector2[] src, Vector2[] dst)
    {
        if (src.Length < 4) return null;
        
        int n = src.Length;
        var A = new double[2 * n, 8];
        var b = new double[2 * n];
        
        for (int i = 0; i < n; i++)
        {
            double x = src[i].X, y = src[i].Y;
            double u = dst[i].X, v = dst[i].Y;
            
            A[2 * i, 0] = -x; A[2 * i, 1] = -y; A[2 * i, 2] = -1;
            A[2 * i, 3] = 0; A[2 * i, 4] = 0; A[2 * i, 5] = 0;
            A[2 * i, 6] = u * x; A[2 * i, 7] = u * y;
            b[2 * i] = -u;
            
            A[2 * i + 1, 0] = 0; A[2 * i + 1, 1] = 0; A[2 * i + 1, 2] = 0;
            A[2 * i + 1, 3] = -x; A[2 * i + 1, 4] = -y; A[2 * i + 1, 5] = -1;
            A[2 * i + 1, 6] = v * x; A[2 * i + 1, 7] = v * y;
            b[2 * i + 1] = -v;
        }
        
        if (!SolveLeastSquaresDouble(A, b, 8, out var h))
            return null;
        
        return new Matrix3x3(
            (float)h[0], (float)h[1], (float)h[2],
            (float)h[3], (float)h[4], (float)h[5],
            (float)h[6], (float)h[7], 1.0f
        );
    }
    
    private static bool SolveLeastSquaresDouble(double[,] A, double[] b, int numParams, out double[] x)
    {
        int m = b.Length;
        int n = numParams;
        x = new double[n];
        
        var ATA = new double[n, n];
        for (int i = 0; i < n; i++)
        for (int j = 0; j < n; j++)
        {
            double sum = 0;
            for (int k = 0; k < m; k++)
                sum += A[k, i] * A[k, j];
            ATA[i, j] = sum;
        }
        
        var ATb = new double[n];
        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            for (int k = 0; k < m; k++)
                sum += A[k, i] * b[k];
            ATb[i] = sum;
        }
        
        return SolveLinearSystemDouble(ATA, ATb, out x);
    }
    
    private static bool SolveLinearSystemDouble(double[,] A, double[] b, out double[] x)
    {
        int n = b.Length;
        x = new double[n];
        const double epsilon = 1e-10;

        for (int p = 0; p < n; p++)
        {
            int max = p;
            for (int i = p + 1; i < n; i++)
                if (System.Math.Abs(A[i, p]) > System.Math.Abs(A[max, p])) max = i;
            
            for (int i = 0; i < n; i++) (A[p, i], A[max, i]) = (A[max, i], A[p, i]);
            (b[p], b[max]) = (b[max], b[p]);
            
            if (System.Math.Abs(A[p, p]) <= epsilon) return false;

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
    #endregion
    
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