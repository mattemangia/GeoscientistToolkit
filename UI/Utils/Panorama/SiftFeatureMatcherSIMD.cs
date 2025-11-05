// GeoscientistToolkit/Business/Photogrammetry/SiftFeatureMatcherSIMD.cs
//
// ==========================================================================================
// IMPORTANT: THIS FILE HAS BEEN REWRITTEN TO USE A CPU-BASED, SIMD-ACCELERATED IMPLEMENTATION.
// All OpenCL dependencies have been removed to resolve persistent driver-related hangs.
// The public class name and method signatures are kept identical for API compatibility.
// THIS VERSION REPLACES THE UNSTABLE ADAPTIVE THRESHOLD WITH A ROBUST FIXED-RATIO TEST.
// ==========================================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace GeoscientistToolkit;

/// <summary>
/// CPU-accelerated SIFT feature matcher using L2 distance, SIMD, and multi-threading.
/// </summary>
public class SiftFeatureMatcherSIMD : IDisposable
{
    public bool EnableDiagnostics { get; set; }
    public Action<string> DiagnosticLogger { get; set; }

    public SiftFeatureMatcherSIMD()
    {
        Log("Initialized CPU-based SIFT feature matcher (SIMD-accelerated with fixed-ratio test).");
    }

    public Task<List<FeatureMatch>> MatchFeaturesAsync(
        SiftFeatures features1,
        SiftFeatures features2,
        CancellationToken token)
    {
        // Maintain the async API by running the CPU-bound work on a background thread.
        return Task.Run(() => MatchFeaturesOnCpu(features1, features2, token), token);
    }

    private List<FeatureMatch> MatchFeaturesOnCpu(
        SiftFeatures features1,
        SiftFeatures features2,
        CancellationToken token)
    {
        Log("Starting feature matching on CPU...");
        var stopwatch = Stopwatch.StartNew();

        int numQuery = features1.KeyPoints.Count;
        int numTrain = features2.KeyPoints.Count;

        if (numQuery == 0 || numTrain == 0)
            return new List<FeatureMatch>();

        const int descriptorSize = 128;
        var trainFlat = new float[numTrain * descriptorSize];
        for (int i = 0; i < numTrain; i++)
        {
            Array.Copy(features2.Descriptors[i], 0, trainFlat, i * descriptorSize, descriptorSize);
        }

        var matches = new int[numQuery * 2];
        var distances = new float[numQuery * 2];

        Parallel.For(0, numQuery, new ParallelOptions { CancellationToken = token }, i =>
        {
            float bestDistSq = float.MaxValue;
            float secondBestDistSq = float.MaxValue;
            int bestIdx = -1;
            
            var queryDesc = features1.Descriptors[i];

            for (int j = 0; j < numTrain; j++)
            {
                float distSq = CalculateL2DistanceSquared_SIMD(queryDesc, trainFlat, j * descriptorSize);
                
                if (distSq < bestDistSq)
                {
                    secondBestDistSq = bestDistSq;
                    bestDistSq = distSq;
                    bestIdx = j;
                }
                else if (distSq < secondBestDistSq)
                {
                    secondBestDistSq = distSq;
                }
            }

            matches[i * 2] = bestIdx;
            matches[i * 2 + 1] = -1; 
            distances[i * 2] = MathF.Sqrt(bestDistSq);
            distances[i * 2 + 1] = MathF.Sqrt(secondBestDistSq);
        });

        Log($"Raw matching completed in {stopwatch.ElapsedMilliseconds} ms. Applying filter...");
        
        var goodMatches = new List<FeatureMatch>();
        FilterMatches(numQuery, matches, distances, goodMatches);

        Log($"Found {goodMatches.Count} good matches. Total time: {stopwatch.ElapsedMilliseconds} ms.");
        return goodMatches;
    }
    
    private float CalculateL2DistanceSquared_SIMD(float[] query, float[] trainDb, int trainOffset)
    {
        int vectorSize = Vector<float>.Count;
        var sumVector = Vector<float>.Zero;

        for (int i = 0; i < 128; i += vectorSize)
        {
            var queryVec = new Vector<float>(query, i);
            var trainVec = new Vector<float>(trainDb, trainOffset + i);
            var diff = queryVec - trainVec;
            sumVector += diff * diff;
        }

        return Vector.Dot(sumVector, Vector<float>.One);
    }
    
    /// <summary>
    /// Filters matches using Lowe's ratio test, a standard and robust method for rejecting ambiguous matches.
    /// </summary>
    private void FilterMatches(int numQuery, int[] matches, float[] distances, List<FeatureMatch> goodMatches)
    {
        // =================================================================
        // THE FIX IS HERE
        // =================================================================
        // The previous adaptive threshold was statistically unstable and produced
        // too few matches. It has been replaced with the industry-standard
        // fixed-ratio test proposed by David Lowe in his SIFT paper.
        // A threshold of 0.75 is a good balance between robustness and quantity.
        const float ratioThreshold = 0.75f;

        for (int i = 0; i < numQuery; i++)
        {
            int bestIdx = matches[i * 2];
            float bestDist = distances[i * 2];
            float secondBestDist = distances[i * 2 + 1];
            
            // Ensure we have a valid best and second-best match to form a ratio.
            if (bestIdx < 0 || secondBestDist <= float.Epsilon)
                continue;

            // A match is kept only if its distance is significantly smaller than the second-best distance.
            if (bestDist < ratioThreshold * secondBestDist)
            {
                goodMatches.Add(new FeatureMatch
                {
                    QueryIndex = i,
                    TrainIndex = bestIdx,
                    Distance = bestDist
                });
            }
        }
        Log($"Applied fixed ratio test with threshold {ratioThreshold}.");
    }

    private void Log(string message)
    {
        if (EnableDiagnostics)
        {
            var logMessage = $"[SIFT Matcher CPU] {DateTime.Now:HH:mm:ss.fff} - {message}";
            DiagnosticLogger?.Invoke(logMessage);
            Debug.WriteLine(logMessage);
        }
    }

    public void Dispose()
    {
        // No unmanaged resources to dispose in this CPU-based implementation.
    }
}