// GeoscientistToolkit/Business/Photogrammetry/SiftFeatureMatcherCL.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.Panorama;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Business.Photogrammetry;

/// <summary>
/// OpenCL-accelerated SIFT feature matcher using L2 distance on 128-dimensional float descriptors
/// </summary>
public class SiftFeatureMatcherCL : IDisposable
{
    private const string MatcherKernelSource = @"
    __kernel void l2_match(
        __global const float* query,
        __global const float* train,
        __global int2* matches,
        __global float* distances,
        int num_train,
        int descriptor_size)
    {
        int gid = get_global_id(0);
        
        // Load query descriptor
        float best_dist = FLT_MAX;
        float second_best_dist = FLT_MAX;
        int best_idx = -1;
        int second_best_idx = -1;
        
        for (int i = 0; i < num_train; ++i)
        {
            float dist = 0.0f;
            
            // Compute L2 distance
            for (int d = 0; d < descriptor_size; d++)
            {
                float diff = query[gid * descriptor_size + d] - train[i * descriptor_size + d];
                dist += diff * diff;
            }
            
            dist = sqrt(dist);
            
            if (dist < best_dist)
            {
                second_best_dist = best_dist;
                second_best_idx = best_idx;
                best_dist = dist;
                best_idx = i;
            }
            else if (dist < second_best_dist)
            {
                second_best_dist = dist;
                second_best_idx = i;
            }
        }
        
        matches[gid] = (int2)(best_idx, second_best_idx);
        distances[gid * 2] = best_dist;
        distances[gid * 2 + 1] = second_best_dist;
    }";
    
    private readonly bool _useOpenCL;
    private readonly CL _cl;
    private readonly IntPtr _program;
    private readonly IntPtr _kernel;
    
    public unsafe SiftFeatureMatcherCL()
    {
        OpenCLService.Initialize();
        if (OpenCLService.IsInitialized)
        {
            _useOpenCL = true;
            _cl = OpenCLService.Cl;
            _program = OpenCLService.CreateProgram(MatcherKernelSource);
            int err;
            _kernel = _cl.CreateKernel(_program, "l2_match", &err);
            err.Throw();
        }
    }
    
    public Task<List<FeatureMatch>> MatchFeaturesAsync(
        SiftFeatures features1,
        SiftFeatures features2,
        CancellationToken token)
    {
        return _useOpenCL
            ? Task.Run(() => MatchFeaturesOnGpu(features1, features2, token), token)
            : Task.Run(() => MatchFeaturesOnCpu(features1, features2, token), token);
    }
    
    private unsafe List<FeatureMatch> MatchFeaturesOnGpu(
        SiftFeatures features1,
        SiftFeatures features2,
        CancellationToken token)
    {
        var goodMatches = new List<FeatureMatch>();
        int numQuery = features1.KeyPoints.Count;
        int numTrain = features2.KeyPoints.Count;
        
        if (numQuery == 0 || numTrain == 0)
            return goodMatches;
        
        const int descriptorSize = 128;
        
        // Flatten descriptors
        var queryFlat = new float[numQuery * descriptorSize];
        var trainFlat = new float[numTrain * descriptorSize];
        
        for (int i = 0; i < numQuery; i++)
            Array.Copy(features1.Descriptors[i], 0, queryFlat, i * descriptorSize, descriptorSize);
        
        for (int i = 0; i < numTrain; i++)
            Array.Copy(features2.Descriptors[i], 0, trainFlat, i * descriptorSize, descriptorSize);
        
        int err;
        fixed (float* pQuery = queryFlat, pTrain = trainFlat)
        {
            var queryBuffer = _cl.CreateBuffer(OpenCLService.Context,
                MemFlags.CopyHostPtr | MemFlags.ReadOnly,
                (nuint)(numQuery * descriptorSize * sizeof(float)), pQuery, &err);
            err.Throw();
            
            var trainBuffer = _cl.CreateBuffer(OpenCLService.Context,
                MemFlags.CopyHostPtr | MemFlags.ReadOnly,
                (nuint)(numTrain * descriptorSize * sizeof(float)), pTrain, &err);
            err.Throw();
            
            var matchesBuffer = _cl.CreateBuffer(OpenCLService.Context,
                MemFlags.WriteOnly,
                (nuint)(numQuery * sizeof(int) * 2), null, &err);
            err.Throw();
            
            var distancesBuffer = _cl.CreateBuffer(OpenCLService.Context,
                MemFlags.WriteOnly,
                (nuint)(numQuery * sizeof(float) * 2), null, &err);
            err.Throw();
            var _descriptorSize = descriptorSize;
            
            // Set kernel arguments
            _cl.SetKernelArg(_kernel, 0, (nuint)sizeof(IntPtr), queryBuffer);
            _cl.SetKernelArg(_kernel, 1, (nuint)sizeof(IntPtr), trainBuffer);
            _cl.SetKernelArg(_kernel, 2, (nuint)sizeof(IntPtr), matchesBuffer);
            _cl.SetKernelArg(_kernel, 3, (nuint)sizeof(IntPtr), distancesBuffer);
            _cl.SetKernelArg(_kernel, 4, sizeof(int), &numTrain);
            _cl.SetKernelArg(_kernel, 5, sizeof(int), &_descriptorSize);
            
            var globalWorkSize = new nuint[] { (nuint)numQuery };
            fixed (nuint* pGlobal = globalWorkSize)
            {
                _cl.EnqueueNdrangeKernel(OpenCLService.CommandQueue, _kernel, 1, null,
                    pGlobal, null, 0, null, null).Throw();
            }
            
            var matches = new int[numQuery * 2];
            var distances = new float[numQuery * 2];
            
            fixed (void* mPtr = matches, dPtr = distances)
            {
                _cl.EnqueueReadBuffer(OpenCLService.CommandQueue, matchesBuffer, true, 0,
                    (nuint)(matches.Length * sizeof(int)), mPtr, 0, null, null).Throw();
                _cl.EnqueueReadBuffer(OpenCLService.CommandQueue, distancesBuffer, true, 0,
                    (nuint)(distances.Length * sizeof(float)), dPtr, 0, null, null).Throw();
            }
            
            FilterMatches(numQuery, matches, distances, goodMatches);
            
            _cl.ReleaseMemObject(queryBuffer);
            _cl.ReleaseMemObject(trainBuffer);
            _cl.ReleaseMemObject(matchesBuffer);
            _cl.ReleaseMemObject(distancesBuffer);
        }
        
        token.ThrowIfCancellationRequested();
        return goodMatches;
    }
    
    private List<FeatureMatch> MatchFeaturesOnCpu(
        SiftFeatures features1,
        SiftFeatures features2,
        CancellationToken token)
    {
        var goodMatches = new List<FeatureMatch>();
        int numQuery = features1.KeyPoints.Count;
        int numTrain = features2.KeyPoints.Count;
        
        if (numQuery == 0 || numTrain == 0)
            return goodMatches;
        
        var matches = new int[numQuery * 2];
        var distances = new float[numQuery * 2];
        
        Parallel.For(0, numQuery, i =>
        {
            token.ThrowIfCancellationRequested();
            
            float bestDist = float.MaxValue;
            float secondBestDist = float.MaxValue;
            int bestIdx = -1;
            int secondBestIdx = -1;
            
            var queryDesc = features1.Descriptors[i];
            
            for (int j = 0; j < numTrain; j++)
            {
                var trainDesc = features2.Descriptors[j];
                
                // Compute L2 distance
                float dist = 0;
                for (int d = 0; d < 128; d++)
                {
                    float diff = queryDesc[d] - trainDesc[d];
                    dist += diff * diff;
                }
                dist = MathF.Sqrt(dist);
                
                if (dist < bestDist)
                {
                    secondBestDist = bestDist;
                    secondBestIdx = bestIdx;
                    bestDist = dist;
                    bestIdx = j;
                }
                else if (dist < secondBestDist)
                {
                    secondBestDist = dist;
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
    
    private void FilterMatches(int numQuery, int[] matches, float[] distances, List<FeatureMatch> goodMatches)
    {
        const float ratioThreshold = 0.8f;  // Lowe's ratio test
        const float maxDistance = 0.6f;     // Maximum L2 distance
        
        for (int i = 0; i < numQuery; i++)
        {
            int bestIdx = matches[i * 2];
            int secondBestIdx = matches[i * 2 + 1];
            float bestDist = distances[i * 2];
            float secondBestDist = distances[i * 2 + 1];
            
            // Skip if no valid match
            if (bestIdx < 0)
                continue;
            
            // Apply Lowe's ratio test
            if (bestDist < ratioThreshold * secondBestDist && bestDist < maxDistance)
            {
                goodMatches.Add(new FeatureMatch
                {
                    QueryIndex = i,
                    TrainIndex = bestIdx,
                    Distance = bestDist
                });
            }
        }
    }
    
    public unsafe void Dispose()
    {
        if (_useOpenCL)
        {
            _cl.ReleaseKernel(_kernel);
            _cl.ReleaseProgram(_program);
        }
    }
}