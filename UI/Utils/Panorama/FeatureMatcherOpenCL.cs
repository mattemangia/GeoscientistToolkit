// GeoscientistToolkit/Business/Panorama/FeatureMatcherCL.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Business.Panorama;

public class FeatureMatcherCL : IDisposable
{
    // Kernel for brute-force matching of 32-byte (256-bit) descriptors
    // using Hamming distance (popcount of XOR).
    private const string MatcherKernelSource = @"
        int popcount_uint(uint n) {
            n = (n & 0x55555555) + ((n >> 1) & 0x55555555);
            n = (n & 0x33333333) + ((n >> 2) & 0x33333333);
            n = (n & 0x0F0F0F0F) + ((n >> 4) & 0x0F0F0F0F);
            n = (n & 0x00FF00FF) + ((n >> 8) & 0x00FF00FF);
            n = (n & 0x0000FFFF) + ((n >> 16) & 0x0000FFFF);
            return n;
        }

        __kernel void brute_force_match(
            __global const ulong4* query,
            __global const ulong4* train,
            __global int2* matches,
            __global ushort* distances,
            int num_train) 
        {
            int gid = get_global_id(0);
            ulong4 d1 = query[gid];

            ushort best_dist = 65535;
            ushort second_best_dist = 65535;
            int best_idx = -1;
            int second_best_idx = -1;

            for (int i = 0; i < num_train; ++i) {
                ulong4 d2 = train[i];
                ulong4 xor_res = d1 ^ d2;
                
                int dist = popcount_uint((uint)xor_res.s0) + popcount_uint((uint)(xor_res.s0 >> 32))
                         + popcount_uint((uint)xor_res.s1) + popcount_uint((uint)(xor_res.s1 >> 32))
                         + popcount_uint((uint)xor_res.s2) + popcount_uint((uint)(xor_res.s2 >> 32))
                         + popcount_uint((uint)xor_res.s3) + popcount_uint((uint)(xor_res.s3 >> 32));

                if (dist < best_dist) {
                    second_best_dist = best_dist;
                    second_best_idx = best_idx;
                    best_dist = (ushort)dist;
                    best_idx = i;
                } else if (dist < second_best_dist) {
                    second_best_dist = (ushort)dist;
                    second_best_idx = i;
                }
            }
            matches[gid] = (int2)(best_idx, second_best_idx);
            distances[gid] = (ushort2)(best_dist, second_best_dist);
        }";

    private readonly bool _useOpenCL;
    private readonly CL _cl;
    private readonly IntPtr _program;
    private readonly IntPtr _kernel;

    public unsafe FeatureMatcherCL()
    {
        OpenCLService.Initialize();
        if (OpenCLService.IsInitialized)
        {
            _useOpenCL = true;
            _cl = OpenCLService.Cl;
            _program = OpenCLService.CreateProgram(MatcherKernelSource);
            int err;
            _kernel = _cl.CreateKernel(_program, "brute_force_match", &err);
            err.Throw();
        }
    }

    public Task<List<FeatureMatch>> MatchFeaturesAsync(DetectedFeatures features1, DetectedFeatures features2, CancellationToken token)
    {
        return _useOpenCL
            ? Task.Run(() => MatchFeaturesOnGpu(features1, features2, token), token)
            : Task.Run(() => MatchFeaturesOnCpu(features1, features2, token), token);
    }

    private unsafe List<FeatureMatch> MatchFeaturesOnGpu(DetectedFeatures features1, DetectedFeatures features2, CancellationToken token)
    {
        var goodMatches = new List<FeatureMatch>();
        int numQuery = features1.KeyPoints.Count;
        int numTrain = features2.KeyPoints.Count;
        if (numQuery == 0 || numTrain == 0) return goodMatches;

        int err;
        var queryBuffer = _cl.CreateBuffer(OpenCLService.Context, MemFlags.CopyHostPtr | MemFlags.ReadOnly, (nuint)(numQuery * 32), features1.Descriptors.AsSpan(), &err); err.Throw();
        var trainBuffer = _cl.CreateBuffer(OpenCLService.Context, MemFlags.CopyHostPtr | MemFlags.ReadOnly, (nuint)(numTrain * 32), features2.Descriptors.AsSpan(), &err); err.Throw();
        var matchesBuffer = _cl.CreateBuffer(OpenCLService.Context, MemFlags.WriteOnly, (nuint)(numQuery * sizeof(int) * 2), null, &err); err.Throw();
        var distancesBuffer = _cl.CreateBuffer(OpenCLService.Context, MemFlags.WriteOnly, (nuint)(numQuery * sizeof(ushort) * 2), null, &err); err.Throw();
        
        var kernelHandle = _kernel;
        _cl.SetKernelArg(kernelHandle, 0, (nuint)sizeof(IntPtr), queryBuffer);
        _cl.SetKernelArg(kernelHandle, 1, (nuint)sizeof(IntPtr), trainBuffer);
        _cl.SetKernelArg(kernelHandle, 2, (nuint)sizeof(IntPtr), matchesBuffer);
        _cl.SetKernelArg(kernelHandle, 3, (nuint)sizeof(IntPtr), distancesBuffer);
        _cl.SetKernelArg(kernelHandle, 4, sizeof(int), &numTrain);

        var globalWorkSize = new nuint[] { (nuint)numQuery };
        fixed(nuint* pGlobal = globalWorkSize) { _cl.EnqueueNdrangeKernel(OpenCLService.CommandQueue, kernelHandle, 1, null, pGlobal, null, 0, null, null).Throw(); }

        var matches = new int[numQuery * 2];
        var distances = new ushort[numQuery * 2];
        fixed(void* mPtr = matches, dPtr = distances)
        {
            _cl.EnqueueReadBuffer(OpenCLService.CommandQueue, matchesBuffer, true, 0, (nuint)(matches.Length * sizeof(int)), mPtr, 0, null, null).Throw();
            _cl.EnqueueReadBuffer(OpenCLService.CommandQueue, distancesBuffer, true, 0, (nuint)(distances.Length * sizeof(ushort)), dPtr, 0, null, null).Throw();
        }

        FilterMatches(numQuery, matches, distances, goodMatches);
        
        _cl.ReleaseMemObject(queryBuffer);
        _cl.ReleaseMemObject(trainBuffer);
        _cl.ReleaseMemObject(matchesBuffer);
        _cl.ReleaseMemObject(distancesBuffer);
        
        token.ThrowIfCancellationRequested();
        return goodMatches;
    }

    private List<FeatureMatch> MatchFeaturesOnCpu(DetectedFeatures features1, DetectedFeatures features2, CancellationToken token)
    {
        var goodMatches = new List<FeatureMatch>();
        int numQuery = features1.KeyPoints.Count;
        int numTrain = features2.KeyPoints.Count;
        if (numQuery == 0 || numTrain == 0) return goodMatches;

        var matches = new int[numQuery * 2];
        var distances = new ushort[numQuery * 2];
        
        // CORRECTED: Access the raw arrays directly to avoid capturing a Span<T> in the lambda.
        var queryDescriptors = features1.Descriptors;
        var trainDescriptors = features2.Descriptors;

        Parallel.For(0, numQuery, i =>
        {
            token.ThrowIfCancellationRequested();
            
            int queryOffset = i * 32;
            ushort bestDist = ushort.MaxValue, secondBestDist = ushort.MaxValue;
            int bestIdx = -1, secondBestIdx = -1;

            for (int j = 0; j < numTrain; j++)
            {
                int trainOffset = j * 32;
                int dist = 0;
                for(int k=0; k < 32; k++)
                {
                    dist += BitOperations.PopCount((uint)(queryDescriptors[queryOffset + k] ^ trainDescriptors[trainOffset + k]));
                }
                
                if (dist < bestDist)
                {
                    secondBestDist = bestDist;
                    secondBestIdx = bestIdx;
                    bestDist = (ushort)dist;
                    bestIdx = j;
                }
                else if (dist < secondBestDist)
                {
                    secondBestDist = (ushort)dist;
                    secondBestIdx = j;
                }
            }
            matches[i * 2] = bestIdx;
            matches[i * 2 + 1] = secondBestIdx;
            distances[i * 2] = bestDist;
            distances[i * 2 + 1] = secondBestDist;
        });
        
        FilterMatches(numQuery, matches, distances, goodMatches);
        return goodMatches;
    }

    private void FilterMatches(int numQuery, int[] matches, ushort[] distances, List<FeatureMatch> goodMatches)
    {
        const float ratioThreshold = 0.75f;
        for (int i = 0; i < numQuery; i++)
        {
            if (matches[i * 2 + 1] < 0 || distances[i * 2 + 1] == 0) continue;
            if (distances[i * 2] < ratioThreshold * distances[i * 2 + 1])
            {
                goodMatches.Add(new FeatureMatch
                {
                    QueryIndex = i,
                    TrainIndex = matches[i * 2],
                    Distance = distances[i * 2]
                });
            }
        }
    }

    public static (Matrix3x2 homography, List<FeatureMatch> inliers) FindHomographyRANSAC(
        List<FeatureMatch> matches, List<KeyPoint> kp1, List<KeyPoint> kp2, 
        int iterations = 2000, float threshold = 3.0f)
    {
        if (matches.Count < 4) return (Matrix3x2.Identity, new List<FeatureMatch>());

        var bestInliers = new List<FeatureMatch>();
        Matrix3x2 bestHomography = Matrix3x2.Identity;
        var random = new Random();
        float thresholdSq = threshold * threshold;

        var srcPoints = matches.Select(m => new Vector2(kp1[m.QueryIndex].X, kp1[m.QueryIndex].Y)).ToArray();
        var dstPoints = matches.Select(m => new Vector2(kp2[m.TrainIndex].X, kp2[m.TrainIndex].Y)).ToArray();
        
        var sampleSrc = new Vector2[4];
        var sampleDst = new Vector2[4];

        for (int i = 0; i < iterations; i++)
        {
            var randomIndices = Enumerable.Range(0, matches.Count).OrderBy(x => random.Next()).Take(4).ToArray();
            for(int k=0; k < 4; k++)
            {
                sampleSrc[k] = srcPoints[randomIndices[k]];
                sampleDst[k] = dstPoints[randomIndices[k]];
            }

            var currentHomography = ComputeHomography(sampleSrc, sampleDst);
            if (!currentHomography.HasValue) continue;

            var currentInliers = new List<FeatureMatch>();
            for (int j = 0; j < matches.Count; j++)
            {
                if (Vector2.DistanceSquared(Vector2.Transform(srcPoints[j], currentHomography.Value), dstPoints[j]) < thresholdSq)
                {
                    currentInliers.Add(matches[j]);
                }
            }

            if (currentInliers.Count > bestInliers.Count)
            {
                bestInliers = currentInliers;
            }
        }
        
        if (bestInliers.Count >= 4)
        {
            // Refine with least squares using all inliers for better accuracy
            var finalSrc = bestInliers.Select(m => srcPoints[matches.IndexOf(m)]).ToArray();
            var finalDst = bestInliers.Select(m => dstPoints[matches.IndexOf(m)]).ToArray();
            var refinedHomography = ComputeHomographyLeastSquares(finalSrc, finalDst);
            bestHomography = refinedHomography ?? bestHomography;
        } else {
             bestInliers.Clear();
        }
        
        return (bestHomography, bestInliers);
    }

    private static Matrix3x2? ComputeHomography(Vector2[] src, Vector2[] dst)
    {
        if (src.Length < 3) return null;
        
        // For 4 points, use least squares directly for better stability
        if (src.Length >= 4)
            return ComputeHomographyLeastSquares(src, dst);

        float sx1 = src[0].X, sy1 = src[0].Y;
        float sx2 = src[1].X, sy2 = src[1].Y;
        float sx3 = src[2].X, sy3 = src[2].Y;
        float dx1 = dst[0].X, dy1 = dst[0].Y;
        float dx2 = dst[1].X, dy2 = dst[1].Y;
        float dx3 = dst[2].X, dy3 = dst[2].Y;

        var A = new float[6, 6] {
            { sx1, sy1, 1, 0,   0,   0 }, { 0,   0,   0, sx1, sy1, 1 },
            { sx2, sy2, 1, 0,   0,   0 }, { 0,   0,   0, sx2, sy2, 1 },
            { sx3, sy3, 1, 0,   0,   0 }, { 0,   0,   0, sx3, sy3, 1 }
        };
        var b = new float[6] { dx1, dy1, dx2, dy2, dx3, dy3 };

        if (!SolveLinearSystem(A, b, out var x)) return null;
        
        return new Matrix3x2(x[0], x[3], x[1], x[4], x[2], x[5]);
    }
    
    private static Matrix3x2? ComputeHomographyLeastSquares(Vector2[] src, Vector2[] dst)
    {
        int n = src.Length;
        var A = new float[n * 2, 6];
        var b = new float[n * 2];
        
        for (int i = 0; i < n; i++)
        {
            float sx = src[i].X, sy = src[i].Y;
            float dx = dst[i].X, dy = dst[i].Y;
            
            // Row for x-coordinate
            A[i * 2, 0] = sx;
            A[i * 2, 1] = sy;
            A[i * 2, 2] = 1;
            A[i * 2, 3] = 0;
            A[i * 2, 4] = 0;
            A[i * 2, 5] = 0;
            b[i * 2] = dx;
            
            // Row for y-coordinate
            A[i * 2 + 1, 0] = 0;
            A[i * 2 + 1, 1] = 0;
            A[i * 2 + 1, 2] = 0;
            A[i * 2 + 1, 3] = sx;
            A[i * 2 + 1, 4] = sy;
            A[i * 2 + 1, 5] = 1;
            b[i * 2 + 1] = dy;
        }
        
        if (!SolveLeastSquares(A, b, 6, out var x))
            return null;
            
        return new Matrix3x2(x[0], x[3], x[1], x[4], x[2], x[5]);
    }
    
    private static bool SolveLeastSquares(float[,] A, float[] b, int numParams, out float[] x)
    {
        // Solve using normal equations: A^T * A * x = A^T * b
        int m = b.Length; // number of equations
        int n = numParams; // number of unknowns
        
        x = new float[n];
        
        // Compute A^T * A
        var ATA = new float[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                float sum = 0;
                for (int k = 0; k < m; k++)
                    sum += A[k, i] * A[k, j];
                ATA[i, j] = sum;
            }
        }
        
        // Compute A^T * b
        var ATb = new float[n];
        for (int i = 0; i < n; i++)
        {
            float sum = 0;
            for (int k = 0; k < m; k++)
                sum += A[k, i] * b[k];
            ATb[i] = sum;
        }
        
        // Solve the system ATA * x = ATb
        return SolveLinearSystem(ATA, ATb, out x);
    }

    private static bool SolveLinearSystem(float[,] A, float[] b, out float[] x)
    {
        int n = b.Length;
        x = new float[n];
        const float epsilon = 1e-10f;

        for (int p = 0; p < n; p++)
        {
            int max = p;
            for (int i = p + 1; i < n; i++)
                if (Math.Abs(A[i, p]) > Math.Abs(A[max, p])) max = i;
            
            for (int i = 0; i < n; i++) (A[p, i], A[max, i]) = (A[max, i], A[p, i]);
            (b[p], b[max]) = (b[max], b[p]);
            
            if (Math.Abs(A[p, p]) <= epsilon) return false;

            for (int i = p + 1; i < n; i++) {
                float alpha = A[i, p] / A[p, p];
                b[i] -= alpha * b[p];
                for (int j = p; j < n; j++) A[i, j] -= alpha * A[p, j];
            }
        }

        for (int i = n - 1; i >= 0; i--) {
            float sum = 0.0f;
            for (int j = i + 1; j < n; j++) sum += A[i, j] * x[j];
            x[i] = (b[i] - sum) / A[i, i];
        }
        return true;
    }
        
    public void Dispose()
    {
        if (_useOpenCL)
        {
            _cl.ReleaseKernel(_kernel);
            _cl.ReleaseProgram(_program);
        }
    }
}