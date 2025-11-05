// GeoscientistToolkit/Business/Photogrammetry/SiftFeatureDetectorSIMD.cs
//
// ==========================================================================================
// IMPORTANT: THIS FILE HAS BEEN REWRITTEN TO USE A CPU-BASED, SIMD-ACCELERATED IMPLEMENTATION.
// All OpenCL dependencies have been removed to resolve persistent driver-related hangs.
// The public class name and method signatures are kept identical for API compatibility.
// THIS VERSION FIXES A CRITICAL BUG IN THE GAUSSIAN PYRAMID CONSTRUCTION.
// ==========================================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.Panorama;
using GeoscientistToolkit.Data.Image;

namespace GeoscientistToolkit;

/// <summary>
/// SIFT features with 128-dimensional float descriptors (public for cross-namespace access).
/// </summary>
public class SiftFeatures
{
    public List<KeyPoint> KeyPoints { get; set; } = new();
    public List<float[]> Descriptors { get; set; } = new();
    public int DescriptorSize => 128;
}

/// <summary>
/// CPU-accelerated SIFT feature detector using SIMD and multi-threading.
/// Produces 128-dimensional float descriptors suitable for structure from motion.
/// </summary>
public class SiftFeatureDetectorSIMD : IDisposable
{
    public bool EnableDiagnostics { get; set; }
    public Action<string> DiagnosticLogger { get; set; }

    public SiftFeatureDetectorSIMD()
    {
        Log("Initialized CPU-based SIFT detector (SIMD-accelerated).");
    }

    public Task<SiftFeatures> DetectAsync(ImageDataset dataset, CancellationToken token)
    {
        // Wrap the synchronous, CPU-bound operation in a Task to maintain the async interface
        return Task.Run(() => DetectOnCpu(dataset, token), token);
    }

    private SiftFeatures DetectOnCpu(ImageDataset dataset, CancellationToken token)
    {
        Log($"Starting SIFT detection on CPU for {dataset.Width}x{dataset.Height} image.");
        var stopwatch = Stopwatch.StartNew();

        // 1. Convert to Grayscale Float
        Log("Converting image to grayscale float representation...");
        var grayImage = ConvertToGrayscaleFloat(dataset.ImageData, dataset.Width, dataset.Height);
        Log($"Conversion complete in {stopwatch.ElapsedMilliseconds} ms.");

        // 2. Build Scale Space
        stopwatch.Restart();
        Log("Building Difference-of-Gaussians (DoG) scale space...");
        var dogPyramid = BuildDoGPyramid(grayImage, dataset.Width, dataset.Height);
        Log($"DoG pyramid built in {stopwatch.ElapsedMilliseconds} ms.");

        // 3. Find Keypoint Extrema
        stopwatch.Restart();
        Log("Locating keypoint extrema in scale space...");
        var keypoints = FindExtrema(dogPyramid, dataset.Width, dataset.Height);
        Log($"Found {keypoints.Count} raw keypoints in {stopwatch.ElapsedMilliseconds} ms.");
        
        // 4. Compute Descriptors
        stopwatch.Restart();
        Log("Computing SIFT descriptors for keypoints...");
        var features = ComputeDescriptors(keypoints, grayImage, dataset.Width, dataset.Height);
        Log($"Computed {features.Descriptors.Count} descriptors in {stopwatch.ElapsedMilliseconds} ms.");

        return features;
    }

    private float[] ConvertToGrayscaleFloat(byte[] rgba, int width, int height)
    {
        var gray = new float[width * height];
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                // Using standard luminosity conversion formula for better results
                gray[y * width + x] = (rgba[i] * 0.299f + rgba[i + 1] * 0.587f + rgba[i + 2] * 0.114f) / 255.0f;
            }
        });
        return gray;
    }

    private List<float[]> BuildDoGPyramid(float[] baseImage, int width, int height)
    {
        // =================================================================
        // THE FIX IS HERE
        // =================================================================
        // The previous implementation repeatedly blurred an already-blurred image with a
        // large, absolute sigma. This destroyed high-frequency details and produced
        // useless descriptors. The correct method is to calculate the incremental
        // sigma required to get from the previous blur level to the next.
        const int numScales = 5;
        const float initialSigma = 1.6f;
        const int numIntervals = 3; // Number of intervals per octave in standard SIFT
        float k = MathF.Pow(2.0f, 1.0f / numIntervals);

        var gaussianPyramid = new List<float[]>(numScales);
        var sigmas = new float[numScales];

        // First level is the original image slightly blurred
        sigmas[0] = initialSigma;
        gaussianPyramid.Add(ApplySeparableGaussianBlur(baseImage, width, height, sigmas[0]));

        // Generate subsequent levels
        for (int i = 1; i < numScales; i++)
        {
            float prevSigma = sigmas[i-1];
            float targetSigma = prevSigma * k;
            sigmas[i] = targetSigma;

            // The additional blur needed is sqrt(target^2 - prev^2)
            float additionalSigma = MathF.Sqrt(targetSigma * targetSigma - prevSigma * prevSigma);
            
            // Apply the smaller, incremental blur to the previously blurred image
            gaussianPyramid.Add(ApplySeparableGaussianBlur(gaussianPyramid[i-1], width, height, additionalSigma));
        }

        // Create Difference-of-Gaussians (this part was already correct)
        var dogPyramid = new List<float[]>(numScales - 1);
        for (int i = 0; i < numScales - 1; i++)
        {
            dogPyramid.Add(SubtractImages_SIMD(gaussianPyramid[i + 1], gaussianPyramid[i], width, height));
        }
        return dogPyramid;
    }

    private float[] ApplySeparableGaussianBlur(float[] image, int width, int height, float sigma)
    {
        var tempImage = new float[width * height];
        var finalImage = new float[width * height];

        int kernelSize = (int)Math.Ceiling(sigma * 3) * 2 + 1;
        var kernel = CreateGaussianKernel(kernelSize, sigma);
        int halfKernel = kernelSize / 2;

        // Horizontal pass
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                float sum = 0;
                for (int k = -halfKernel; k <= halfKernel; k++)
                {
                    int px = Math.Clamp(x + k, 0, width - 1);
                    sum += image[y * width + px] * kernel[k + halfKernel];
                }
                tempImage[y * width + x] = sum;
            }
        });

        // Vertical pass
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                float sum = 0;
                for (int k = -halfKernel; k <= halfKernel; k++)
                {
                    int py = Math.Clamp(y + k, 0, height - 1);
                    sum += tempImage[py * width + x] * kernel[k + halfKernel];
                }
                finalImage[y * width + x] = sum;
            }
        });

        return finalImage;
    }

    private float[] CreateGaussianKernel(int size, float sigma)
    {
        var kernel = new float[size];
        float sum = 0;
        int halfSize = size / 2;
        float twoSigmaSq = 2 * sigma * sigma;

        for (int i = -halfSize; i <= halfSize; i++)
        {
            float val = MathF.Exp(-(i * i) / twoSigmaSq);
            kernel[i + halfSize] = val;
            sum += val;
        }
        for (int i = 0; i < size; i++)
        {
            kernel[i] /= sum;
        }
        return kernel;
    }

    private float[] SubtractImages_SIMD(float[] a, float[] b, int width, int height)
    {
        var result = new float[width * height];
        int vectorSize = Vector<float>.Count;
        int len = width * height;

        for (int i = 0; i < len; i += vectorSize)
        {
            if (i + vectorSize <= len)
            {
                var vecA = new Vector<float>(a, i);
                var vecB = new Vector<float>(b, i);
                (vecA - vecB).CopyTo(result, i);
            }
            else
            {
                for (int j = i; j < len; j++)
                {
                    result[j] = a[j] - b[j];
                }
            }
        }
        return result;
    }

    private List<KeyPoint> FindExtrema(List<float[]> dogPyramid, int width, int height)
    {
        var keypoints = new List<KeyPoint>();
        const float threshold = 0.03f;
        
        for (int level = 1; level < dogPyramid.Count - 1; level++)
        {
            var prev = dogPyramid[level - 1];
            var curr = dogPyramid[level];
            var next = dogPyramid[level + 1];

            Parallel.For(1, height - 1, y =>
            {
                for (int x = 1; x < width - 1; x++)
                {
                    float val = curr[y * width + x];
                    if (Math.Abs(val) < threshold) continue;

                    bool isMax = true;
                    bool isMin = true;

                    for (int dz = -1; dz <= 1; dz++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0 && dz == 0) continue;

                                float neighborVal = (dz == -1) ? prev[(y + dy) * width + x + dx]
                                                    : (dz == 0) ? curr[(y + dy) * width + x + dx]
                                                                : next[(y + dy) * width + x + dx];

                                if (neighborVal >= val) isMax = false;
                                if (neighborVal <= val) isMin = false;
                                if (!isMax && !isMin) goto nextPixel;
                            }
                        }
                    }

                    if (isMax || isMin)
                    {
                        lock (keypoints)
                        {
                            keypoints.Add(new KeyPoint
                            {
                                X = x, Y = y, Level = level, Response = val
                            });
                        }
                    }

                    nextPixel:;
                }
            });
        }
        return keypoints;
    }
    
    private SiftFeatures ComputeDescriptors(List<KeyPoint> keypoints, float[] grayImage, int width, int height)
    {
        var features = new SiftFeatures();
        int patchRadius = 8;

        foreach (var kp in keypoints)
        {
            if (kp.X < patchRadius || kp.X >= width - patchRadius || kp.Y < patchRadius || kp.Y >= height - patchRadius)
                continue;

            var descriptor = new float[128];
            int gridSize = 4;
            int cellSize = 4;
            
            for (int gridY = 0; gridY < gridSize; gridY++)
            {
                for (int gridX = 0; gridX < gridSize; gridX++)
                {
                    var hist = new float[8];
                    
                    int startX = (int)(kp.X - patchRadius + gridX * cellSize);
                    int startY = (int)(kp.Y - patchRadius + gridY * cellSize);

                    for (int cellY = 0; cellY < cellSize; cellY++)
                    {
                        for (int cellX = 0; cellX < cellSize; cellX++)
                        {
                            int px = startX + cellX;
                            int py = startY + cellY;

                            int x_minus_1 = Math.Max(0, px - 1);
                            int x_plus_1  = Math.Min(width - 1, px + 1);
                            int y_minus_1 = Math.Max(0, py - 1);
                            int y_plus_1  = Math.Min(height - 1, py + 1);

                            float dx = grayImage[py * width + x_plus_1] - grayImage[py * width + x_minus_1];
                            float dy = grayImage[y_plus_1 * width + px] - grayImage[y_minus_1 * width + px];
                            float mag = MathF.Sqrt(dx * dx + dy * dy);
                            float dir = MathF.Atan2(dy, dx);
                            
                            int bin = (int)(((dir + MathF.PI) / (2 * MathF.PI)) * 8.0f) % 8;
                            hist[bin] += mag;
                        }
                    }
                    
                    int offset = (gridY * gridSize + gridX) * 8;
                    Array.Copy(hist, 0, descriptor, offset, 8);
                }
            }

            float norm = MathF.Sqrt(descriptor.Sum(d => d * d));
            if (norm > 1e-6f)
            {
                for (int i = 0; i < 128; i++) descriptor[i] = Math.Min(descriptor[i] / norm, 0.2f);
            }
            
            norm = MathF.Sqrt(descriptor.Sum(d => d * d));
            if (norm > 1e-6f)
            {
                for (int i = 0; i < 128; i++) descriptor[i] /= norm;
            }
            
            features.Descriptors.Add(descriptor);
            features.KeyPoints.Add(kp);
        }
        return features;
    }

    private void Log(string message)
    {
        if (EnableDiagnostics)
        {
            var logMessage = $"[SIFT CPU] {DateTime.Now:HH:mm:ss.fff} - {message}";
            DiagnosticLogger?.Invoke(logMessage);
            Debug.WriteLine(logMessage);
        }
    }

    public void Dispose()
    {
        // No unmanaged resources to dispose in this CPU-based implementation.
    }
}