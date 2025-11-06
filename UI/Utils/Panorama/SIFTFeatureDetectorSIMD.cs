// GeoscientistToolkit/Business/Photogrammetry/SiftFeatureDetectorSIMD.cs

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.Panorama;
using GeoscientistToolkit.Data.Image;

namespace GeoscientistToolkit;

public class SiftFeatures
{
    public List<KeyPoint> KeyPoints { get; set; } = new();
    public List<float[]> Descriptors { get; set; } = new();
    public int DescriptorSize => 128;
}

public class SiftFeatureDetectorSIMD : IDisposable
{
    public bool EnableDiagnostics { get; set; }
    public Action<string> DiagnosticLogger { get; set; }
    
    // Memory pool for reusable arrays
    private readonly ArrayPool<float> _arrayPool = ArrayPool<float>.Shared;
    private readonly List<float[]> _rentedArrays = new();

    public SiftFeatureDetectorSIMD()
    {
        Log("Initialized CPU-based SIFT detector (SIMD-accelerated with memory pooling).");
    }

    public Task<SiftFeatures> DetectAsync(ImageDataset dataset, CancellationToken token)
    {
        return Task.Run(() => DetectOnCpu(dataset, token), token);
    }

    private SiftFeatures DetectOnCpu(ImageDataset dataset, CancellationToken token)
    {
        try
        {
            Log($"Starting SIFT detection on CPU for {dataset.Width}x{dataset.Height} image.");
            var stopwatch = Stopwatch.StartNew();

            // 1. Convert to Grayscale Float
            Log("Converting image to grayscale float representation...");
            var grayImage = ConvertToGrayscaleFloat(dataset.ImageData, dataset.Width, dataset.Height);
            Log($"Conversion complete in {stopwatch.ElapsedMilliseconds} ms.");

            // 2. Build Scale Space (with memory-efficient pyramid)
            stopwatch.Restart();
            Log("Building Difference-of-Gaussians (DoG) scale space...");
            var (dogPyramid, gaussianLevels) = BuildDoGPyramidMemoryEfficient(grayImage, dataset.Width, dataset.Height);
            Log($"DoG pyramid built in {stopwatch.ElapsedMilliseconds} ms.");

            // 3. Find Keypoint Extrema
            stopwatch.Restart();
            Log("Locating keypoint extrema in scale space...");
            var keypoints = FindExtremaWithConcurrentBag(dogPyramid, dataset.Width, dataset.Height);
            Log($"Found {keypoints.Count} raw keypoints in {stopwatch.ElapsedMilliseconds} ms.");

            // 4. Compute Descriptors (using appropriate Gaussian level)
            stopwatch.Restart();
            Log("Computing SIFT descriptors for keypoints...");
            var features = ComputeDescriptorsWithLevels(keypoints, gaussianLevels, dataset.Width, dataset.Height);
            Log($"Computed {features.Descriptors.Count} descriptors in {stopwatch.ElapsedMilliseconds} ms.");

            // Clean up DoG pyramid arrays
            foreach (var dog in dogPyramid)
            {
                ReturnArray(dog);
            }
            
            // Clean up Gaussian levels (except the first one if it's the original gray image)
            for (int i = 0; i < gaussianLevels.Count; i++)
            {
                if (gaussianLevels[i] != grayImage)
                {
                    ReturnArray(gaussianLevels[i]);
                }
            }

            // Force garbage collection after heavy memory operations
            if (dataset.Width * dataset.Height > 1_000_000) // For large images
            {
                GC.Collect(2, GCCollectionMode.Optimized);
            }

            return features;
        }
        catch (Exception ex)
        {
            Log($"Error during SIFT detection: {ex.Message}");
            // Return empty features rather than throwing to prevent KeyNotFoundException
            return new SiftFeatures();
        }
        finally
        {
            // Ensure all rented arrays are returned
            CleanupRentedArrays();
        }
    }

    private float[] RentArray(int size)
    {
        var array = _arrayPool.Rent(size);
        _rentedArrays.Add(array);
        Array.Clear(array, 0, Math.Min(size, array.Length)); // Clear only what we'll use
        return array;
    }

    private void ReturnArray(float[] array)
    {
        if (array != null && _rentedArrays.Remove(array))
        {
            _arrayPool.Return(array, clearArray: false);
        }
    }

    private void CleanupRentedArrays()
    {
        foreach (var array in _rentedArrays)
        {
            _arrayPool.Return(array, clearArray: false);
        }
        _rentedArrays.Clear();
    }

    private float[] ConvertToGrayscaleFloat(byte[] rgba, int width, int height)
    {
        var gray = new float[width * height];
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                gray[y * width + x] = (rgba[i] * 0.299f + rgba[i + 1] * 0.587f + rgba[i + 2] * 0.114f) / 255.0f;
            }
        });
        return gray;
    }

    private (List<float[]> dogPyramid, List<float[]> gaussianLevels) BuildDoGPyramidMemoryEfficient(
        float[] baseImage, int width, int height)
    {
        const int numScales = 5;
        const float initialSigma = 1.6f;
        const int numIntervals = 3;
        float k = MathF.Pow(2.0f, 1.0f / numIntervals);

        var gaussianLevels = new List<float[]>(numScales);
        var dogPyramid = new List<float[]>(numScales - 1);
        var sigmas = new float[numScales];

        // Reusable buffers for blur operations
        var tempBuffer1 = RentArray(width * height);

        // First level
        sigmas[0] = initialSigma;
        var firstLevel = RentArray(width * height);
        ApplySeparableGaussianBlurInPlace(baseImage, firstLevel, tempBuffer1, width, height, sigmas[0]);
        gaussianLevels.Add(firstLevel);

        // Generate subsequent levels and compute DoG immediately
        for (int i = 1; i < numScales; i++)
        {
            float prevSigma = sigmas[i - 1];
            float targetSigma = prevSigma * k;
            sigmas[i] = targetSigma;
            float additionalSigma = MathF.Sqrt(targetSigma * targetSigma - prevSigma * prevSigma);

            var currentLevel = RentArray(width * height);
            ApplySeparableGaussianBlurInPlace(gaussianLevels[i - 1], currentLevel, tempBuffer1, width, height, additionalSigma);
            gaussianLevels.Add(currentLevel);

            // Compute DoG immediately to save memory
            var dog = RentArray(width * height);
            SubtractImages_SIMD_InPlace(currentLevel, gaussianLevels[i - 1], dog, width, height);
            dogPyramid.Add(dog);
        }

        // Return temporary buffers
        ReturnArray(tempBuffer1);

        return (dogPyramid, gaussianLevels);
    }

    private void ApplySeparableGaussianBlurInPlace(
        float[] source, float[] destination, float[] tempBuffer, 
        int width, int height, float sigma)
    {
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
                    sum += source[y * width + px] * kernel[k + halfKernel];
                }
                tempBuffer[y * width + x] = sum;
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
                    sum += tempBuffer[py * width + x] * kernel[k + halfKernel];
                }
                destination[y * width + x] = sum;
            }
        });
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

    private void SubtractImages_SIMD_InPlace(float[] a, float[] b, float[] result, int width, int height)
    {
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
    }

    // FIX: Using ConcurrentBag instead of ThreadLocal with Values property
    private List<KeyPoint> FindExtremaWithConcurrentBag(List<float[]> dogPyramid, int width, int height)
    {
        var keypointBag = new ConcurrentBag<KeyPoint>();
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

                    // Check all 26 neighbors in 3x3x3 cube
                    for (int dz = -1; dz <= 1 && (isMax || isMin); dz++)
                    {
                        for (int dy = -1; dy <= 1 && (isMax || isMin); dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0 && dz == 0) continue;

                                int idx = (y + dy) * width + (x + dx);
                                float neighborVal = dz switch
                                {
                                    -1 => prev[idx],
                                    0 => curr[idx],
                                    _ => next[idx]
                                };

                                if (neighborVal >= val) isMax = false;
                                if (neighborVal <= val) isMin = false;
                                if (!isMax && !isMin) break;
                            }
                        }
                    }

                    if (isMax || isMin)
                    {
                        keypointBag.Add(new KeyPoint
                        {
                            X = x,
                            Y = y,
                            Level = level,
                            Response = val
                        });
                    }
                }
            });
        }
        
        return keypointBag.ToList();
    }

    private SiftFeatures ComputeDescriptorsWithLevels(
        List<KeyPoint> keypoints, List<float[]> gaussianLevels, int width, int height)
    {
        var features = new SiftFeatures();
        if (keypoints.Count == 0 || gaussianLevels.Count == 0)
            return features;
            
        int patchRadius = 8;

        // Use the first Gaussian level for descriptor computation
        var grayImage = gaussianLevels[0];
        var concurrentDescriptors = new ConcurrentBag<(KeyPoint kp, float[] desc)>();

        Parallel.ForEach(keypoints, kp =>
        {
            if (kp.X < patchRadius || kp.X >= width - patchRadius || 
                kp.Y < patchRadius || kp.Y >= height - patchRadius)
                return;

            var descriptor = ComputeSingleDescriptor(kp, grayImage, width, height, patchRadius);
            concurrentDescriptors.Add((kp, descriptor));
        });

        // Convert concurrent bag to lists
        foreach (var (kp, desc) in concurrentDescriptors)
        {
            features.KeyPoints.Add(kp);
            features.Descriptors.Add(desc);
        }
        
        return features;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float[] ComputeSingleDescriptor(KeyPoint kp, float[] grayImage, int width, int height, int patchRadius)
    {
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
                        int x_plus_1 = Math.Min(width - 1, px + 1);
                        int y_minus_1 = Math.Max(0, py - 1);
                        int y_plus_1 = Math.Min(height - 1, py + 1);

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

        // Normalize descriptor
        float norm = MathF.Sqrt(descriptor.Sum(d => d * d));
        if (norm > 1e-6f)
        {
            for (int i = 0; i < 128; i++) 
            {
                descriptor[i] = Math.Min(descriptor[i] / norm, 0.2f);
            }
            
            // Re-normalize after clamping
            norm = MathF.Sqrt(descriptor.Sum(d => d * d));
            if (norm > 1e-6f)
            {
                for (int i = 0; i < 128; i++) 
                {
                    descriptor[i] /= norm;
                }
            }
        }
        
        return descriptor;
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
        CleanupRentedArrays();
    }
}