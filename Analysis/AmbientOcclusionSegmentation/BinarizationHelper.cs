// GeoscientistToolkit/Analysis/AmbientOcclusionSegmentation/BinarizationHelper.cs

using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.AmbientOcclusionSegmentation;

/// <summary>
/// Utilities for automatic threshold detection and binarization of CT volumes
/// </summary>
public static class BinarizationHelper
{
    /// <summary>
    /// Compute histogram of grayscale values in the volume
    /// </summary>
    public static int[] ComputeHistogram(CtImageStackDataset dataset, int sampleRate = 1)
    {
        var histogram = new int[256];

        int width = dataset.Width;
        int height = dataset.Height;
        int depth = dataset.Depth;

        for (int z = 0; z < depth; z += sampleRate)
        {
            for (int y = 0; y < height; y += sampleRate)
            {
                for (int x = 0; x < width; x += sampleRate)
                {
                    byte value = dataset.VolumeData[x, y, z];
                    histogram[value]++;
                }
            }
        }

        return histogram;
    }

    /// <summary>
    /// Compute histogram for a region of the volume
    /// </summary>
    public static int[] ComputeHistogram(CtImageStackDataset dataset, Region3D region, int sampleRate = 1)
    {
        var histogram = new int[256];

        for (int z = region.MinZ; z < region.MaxZ; z += sampleRate)
        {
            for (int y = region.MinY; y < region.MaxY; y += sampleRate)
            {
                for (int x = region.MinX; x < region.MaxX; x += sampleRate)
                {
                    byte value = dataset.VolumeData[x, y, z];
                    histogram[value]++;
                }
            }
        }

        return histogram;
    }

    /// <summary>
    /// Compute Otsu's threshold for automatic binarization
    /// Based on: Otsu, N. (1979). "A threshold selection method from gray-level histograms"
    /// </summary>
    public static byte ComputeOtsuThreshold(int[] histogram)
    {
        // Total number of pixels
        long total = 0;
        for (int i = 0; i < 256; i++)
            total += histogram[i];

        if (total == 0)
            return 128;

        // Calculate the mean intensity of all pixels
        double sum = 0;
        for (int i = 0; i < 256; i++)
            sum += i * histogram[i];

        double sumB = 0;
        long wB = 0;
        long wF = 0;

        double varMax = 0;
        byte threshold = 0;

        // Iterate through all possible thresholds
        for (int t = 0; t < 256; t++)
        {
            wB += histogram[t]; // Weight Background
            if (wB == 0) continue;

            wF = total - wB; // Weight Foreground
            if (wF == 0) break;

            sumB += t * histogram[t];

            double mB = sumB / wB; // Mean Background
            double mF = (sum - sumB) / wF; // Mean Foreground

            // Calculate Between Class Variance
            double varBetween = (double)wB * (double)wF * (mB - mF) * (mB - mF);

            // Check if new maximum found
            if (varBetween > varMax)
            {
                varMax = varBetween;
                threshold = (byte)t;
            }
        }

        return threshold;
    }

    /// <summary>
    /// Compute Otsu's threshold directly from dataset
    /// </summary>
    public static byte ComputeOtsuThreshold(CtImageStackDataset dataset, int sampleRate = 10)
    {
        Logger.Log($"[Binarization] Computing Otsu threshold (sample rate: 1/{sampleRate})...");
        var histogram = ComputeHistogram(dataset, sampleRate);
        var threshold = ComputeOtsuThreshold(histogram);
        Logger.Log($"[Binarization] Otsu threshold: {threshold}");
        return threshold;
    }

    /// <summary>
    /// Compute multi-level Otsu threshold (for materials with multiple phases)
    /// Returns array of thresholds separating N+1 classes
    /// </summary>
    public static byte[] ComputeMultiLevelOtsuThreshold(int[] histogram, int numThresholds)
    {
        if (numThresholds < 1 || numThresholds > 4)
            throw new ArgumentException("Number of thresholds must be between 1 and 4");

        if (numThresholds == 1)
            return new[] { ComputeOtsuThreshold(histogram) };

        // For 2+ thresholds, use simplified approach
        // Full multi-level Otsu is computationally expensive
        var thresholds = new byte[numThresholds];

        // Divide histogram into regions and apply Otsu to each
        int step = 256 / (numThresholds + 1);
        for (int i = 0; i < numThresholds; i++)
        {
            int start = i * step;
            int end = Math.Min((i + 2) * step, 256);

            var subHistogram = new int[256];
            for (int j = start; j < end; j++)
                subHistogram[j] = histogram[j];

            thresholds[i] = ComputeOtsuThreshold(subHistogram);
        }

        return thresholds;
    }

    /// <summary>
    /// Triangle method for automatic threshold detection
    /// Good for images with one peak in the histogram
    /// </summary>
    public static byte ComputeTriangleThreshold(int[] histogram)
    {
        // Find the peak of the histogram
        int maxIdx = 0;
        int maxVal = 0;
        for (int i = 0; i < 256; i++)
        {
            if (histogram[i] > maxVal)
            {
                maxVal = histogram[i];
                maxIdx = i;
            }
        }

        // Find the rightmost non-zero value
        int endIdx = 255;
        while (endIdx > maxIdx && histogram[endIdx] == 0)
            endIdx--;

        if (endIdx == maxIdx)
            return (byte)maxIdx;

        // Calculate the line from peak to end
        double maxDist = 0;
        int threshold = maxIdx;

        for (int i = maxIdx; i <= endIdx; i++)
        {
            // Distance from point to line
            double dist = PerpendicularDistance(
                maxIdx, histogram[maxIdx],
                endIdx, histogram[endIdx],
                i, histogram[i]);

            if (dist > maxDist)
            {
                maxDist = dist;
                threshold = i;
            }
        }

        return (byte)threshold;
    }

    /// <summary>
    /// Mean-based threshold (simple but effective)
    /// </summary>
    public static byte ComputeMeanThreshold(int[] histogram)
    {
        long total = 0;
        double sum = 0;

        for (int i = 0; i < 256; i++)
        {
            total += histogram[i];
            sum += i * histogram[i];
        }

        if (total == 0)
            return 128;

        return (byte)(sum / total);
    }

    /// <summary>
    /// Isodata (iterative) threshold
    /// </summary>
    public static byte ComputeIsodataThreshold(int[] histogram, int maxIterations = 100)
    {
        // Start with mean threshold
        byte threshold = ComputeMeanThreshold(histogram);
        byte prevThreshold;

        int iteration = 0;
        do
        {
            prevThreshold = threshold;

            // Calculate mean of background and foreground
            long sumB = 0, countB = 0;
            long sumF = 0, countF = 0;

            for (int i = 0; i < 256; i++)
            {
                if (i <= threshold)
                {
                    sumB += i * histogram[i];
                    countB += histogram[i];
                }
                else
                {
                    sumF += i * histogram[i];
                    countF += histogram[i];
                }
            }

            double meanB = countB > 0 ? (double)sumB / countB : 0;
            double meanF = countF > 0 ? (double)sumF / countF : 255;

            threshold = (byte)((meanB + meanF) / 2);
            iteration++;

        } while (threshold != prevThreshold && iteration < maxIterations);

        return threshold;
    }

    /// <summary>
    /// Percentile threshold (e.g., 95th percentile)
    /// </summary>
    public static byte ComputePercentileThreshold(int[] histogram, double percentile)
    {
        long total = 0;
        for (int i = 0; i < 256; i++)
            total += histogram[i];

        long target = (long)(total * percentile / 100.0);
        long cumulative = 0;

        for (int i = 0; i < 256; i++)
        {
            cumulative += histogram[i];
            if (cumulative >= target)
                return (byte)i;
        }

        return 255;
    }

    /// <summary>
    /// Get histogram statistics
    /// </summary>
    public static HistogramStats ComputeHistogramStats(int[] histogram)
    {
        long total = 0;
        double sum = 0;
        int minValue = 256, maxValue = -1;

        for (int i = 0; i < 256; i++)
        {
            if (histogram[i] > 0)
            {
                if (i < minValue) minValue = i;
                if (i > maxValue) maxValue = i;
            }
            total += histogram[i];
            sum += i * histogram[i];
        }

        double mean = total > 0 ? sum / total : 0;

        // Calculate standard deviation
        double sumSq = 0;
        for (int i = 0; i < 256; i++)
        {
            double diff = i - mean;
            sumSq += diff * diff * histogram[i];
        }
        double stdDev = total > 0 ? Math.Sqrt(sumSq / total) : 0;

        // Find mode (most frequent value)
        int mode = 0;
        int maxCount = 0;
        for (int i = 0; i < 256; i++)
        {
            if (histogram[i] > maxCount)
            {
                maxCount = histogram[i];
                mode = i;
            }
        }

        return new HistogramStats
        {
            Min = minValue < 256 ? minValue : 0,
            Max = maxValue >= 0 ? maxValue : 255,
            Mean = mean,
            StdDev = stdDev,
            Mode = mode,
            TotalPixels = total
        };
    }

    private static double PerpendicularDistance(double x1, double y1, double x2, double y2, double x0, double y0)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        double mag = Math.Sqrt(dx * dx + dy * dy);

        if (mag < 1e-10)
            return 0;

        return Math.Abs(dy * x0 - dx * y0 + x2 * y1 - y2 * x1) / mag;
    }
}

/// <summary>
/// Statistics computed from histogram
/// </summary>
public struct HistogramStats
{
    public int Min;
    public int Max;
    public double Mean;
    public double StdDev;
    public int Mode;
    public long TotalPixels;
}

/// <summary>
/// Threshold detection method
/// </summary>
public enum ThresholdMethod
{
    Manual,
    Otsu,
    Triangle,
    Mean,
    Isodata,
    Percentile95,
    Percentile99
}
