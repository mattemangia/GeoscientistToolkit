// GeoscientistToolkit/Data/Nerf/NerfTrainer.cs

using System.Numerics;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Nerf;

/// <summary>
/// Trainer for Neural Radiance Fields using Instant-NGP style optimization.
/// Implements ray marching, volume rendering, and gradient-based optimization.
/// </summary>
public class NerfTrainer : IDisposable
{
    private readonly NerfDataset _dataset;
    private readonly NerfTrainingConfig _config;
    private CancellationTokenSource _cancellationSource;
    private Task _trainingTask;

    // Training state
    private int _currentIteration = 0;
    private float _currentLoss = float.MaxValue;
    private float _currentPSNR = 0;
    private DateTime _startTime;

    // Adam optimizer state
    private float[][] _hashGradMomentum;
    private float[][] _hashGradVariance;
    private List<(float[] weightMomentum, float[] weightVariance, float[] biasMomentum, float[] biasVariance)> _densityOptState;
    private List<(float[] weightMomentum, float[] weightVariance, float[] biasMomentum, float[] biasVariance)> _colorOptState;

    private const float Adam_Beta1 = 0.9f;
    private const float Adam_Beta2 = 0.99f;
    private const float Adam_Epsilon = 1e-15f;

    // Random number generator for sampling
    private readonly Random _rng = new();

    // Events for progress reporting
    public event Action<int, float, float> OnIterationComplete;
    public event Action<NerfTrainingState> OnStateChanged;
    public event Action<byte[], int, int> OnPreviewReady;

    public NerfTrainer(NerfDataset dataset)
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        _config = dataset.TrainingConfig ?? new NerfTrainingConfig();
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Start training asynchronously
    /// </summary>
    public void StartTraining()
    {
        if (_trainingTask != null && !_trainingTask.IsCompleted)
        {
            Logger.LogWarning("Training is already in progress");
            return;
        }

        _cancellationSource = new CancellationTokenSource();
        _trainingTask = Task.Run(() => TrainingLoop(_cancellationSource.Token));
    }

    /// <summary>
    /// Pause training
    /// </summary>
    public void Pause()
    {
        _cancellationSource?.Cancel();
        _dataset.TrainingState = NerfTrainingState.Paused;
        OnStateChanged?.Invoke(NerfTrainingState.Paused);
    }

    /// <summary>
    /// Stop training
    /// </summary>
    public void Stop()
    {
        _cancellationSource?.Cancel();
        _cancellationSource?.Dispose();
        _cancellationSource = null;
    }

    /// <summary>
    /// Reset training to initial state
    /// </summary>
    public void Reset()
    {
        Stop();
        _currentIteration = 0;
        _currentLoss = float.MaxValue;
        _currentPSNR = 0;
        _dataset.CurrentIteration = 0;
        _dataset.CurrentLoss = float.MaxValue;
        _dataset.CurrentPSNR = 0;
        _dataset.TrainingState = NerfTrainingState.NotStarted;
        _dataset.TrainingHistory.Clear();
        _dataset.ModelData = null;

        _hashGradMomentum = null;
        _hashGradVariance = null;
        _densityOptState = null;
        _colorOptState = null;

        OnStateChanged?.Invoke(NerfTrainingState.NotStarted);
    }

    private void TrainingLoop(CancellationToken cancellationToken)
    {
        try
        {
            _dataset.TrainingState = NerfTrainingState.Preparing;
            _dataset.TrainingStartTime = DateTime.Now;
            OnStateChanged?.Invoke(NerfTrainingState.Preparing);

            // Validate we have images with poses
            var framedWithPoses = _dataset.ImageCollection.GetFramesWithPoses().ToList();
            if (framedWithPoses.Count < 2)
            {
                Logger.LogError("Need at least 2 images with camera poses for training");
                _dataset.TrainingState = NerfTrainingState.Failed;
                OnStateChanged?.Invoke(NerfTrainingState.Failed);
                return;
            }

            Logger.Log($"Starting NeRF training with {framedWithPoses.Count} images");

            // Initialize model
            InitializeModel();

            // Initialize optimizer state
            InitializeOptimizer();

            _dataset.TrainingState = NerfTrainingState.Training;
            OnStateChanged?.Invoke(NerfTrainingState.Training);
            _startTime = DateTime.Now;

            // Main training loop
            while (_currentIteration < _dataset.TotalIterations && !cancellationToken.IsCancellationRequested)
            {
                // Sample random rays from random images
                var (rays, targetColors) = SampleRays(framedWithPoses, _config.RaysPerBatch);

                // Forward pass: render rays
                var (predictedColors, densities) = RenderRays(rays);

                // Compute loss
                float loss = ComputeLoss(predictedColors, targetColors, densities);

                // Backward pass: compute gradients (numerical approximation for now)
                ComputeGradientsAndUpdate(rays, targetColors, loss);

                // Update metrics
                _currentLoss = loss;
                _currentPSNR = -10 * MathF.Log10(loss + 1e-8f);
                _currentIteration++;

                _dataset.CurrentIteration = _currentIteration;
                _dataset.CurrentLoss = _currentLoss;
                _dataset.CurrentPSNR = _currentPSNR;

                // Log progress
                if (_currentIteration % 100 == 0)
                {
                    var elapsed = (DateTime.Now - _startTime).TotalSeconds;
                    var log = new NerfTrainingLog
                    {
                        Iteration = _currentIteration,
                        Loss = _currentLoss,
                        PSNR = _currentPSNR,
                        LearningRate = GetCurrentLearningRate(),
                        ElapsedSeconds = elapsed
                    };
                    _dataset.TrainingHistory.Add(log);

                    Logger.Log($"Iteration {_currentIteration}/{_dataset.TotalIterations}: Loss={_currentLoss:F6}, PSNR={_currentPSNR:F2}dB");
                    OnIterationComplete?.Invoke(_currentIteration, _currentLoss, _currentPSNR);
                }

                // Generate preview every 1000 iterations
                if (_currentIteration % 1000 == 0)
                {
                    GeneratePreview();
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                _dataset.TrainingState = NerfTrainingState.Paused;
                OnStateChanged?.Invoke(NerfTrainingState.Paused);
            }
            else
            {
                _dataset.TrainingState = NerfTrainingState.Completed;
                _dataset.TrainingEndTime = DateTime.Now;
                OnStateChanged?.Invoke(NerfTrainingState.Completed);
                Logger.Log($"Training completed in {_dataset.TrainingDurationSeconds:F1} seconds");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Training failed: {ex.Message}");
            _dataset.TrainingState = NerfTrainingState.Failed;
            OnStateChanged?.Invoke(NerfTrainingState.Failed);
        }
    }

    private void InitializeModel()
    {
        _dataset.ModelData = new NerfModelData();
        _dataset.ModelData.Initialize(_config);

        // Set scene parameters from image collection
        _dataset.ImageCollection.ComputeSceneBounds();
        _dataset.ModelData.SceneCenter = _dataset.ImageCollection.SceneCenter;
        _dataset.ModelData.SceneScale = 1.0f / Math.Max(_dataset.ImageCollection.SceneRadius, 0.1f);
    }

    private void InitializeOptimizer()
    {
        var model = _dataset.ModelData;

        // Initialize Adam momentum and variance for hash grids
        _hashGradMomentum = new float[model.NumLevels][];
        _hashGradVariance = new float[model.NumLevels][];
        for (int level = 0; level < model.NumLevels; level++)
        {
            _hashGradMomentum[level] = new float[model.HashTables[level].Length];
            _hashGradVariance[level] = new float[model.HashTables[level].Length];
        }

        // Initialize for density MLP
        _densityOptState = new List<(float[], float[], float[], float[])>();
        foreach (var layer in model.DensityMLP)
        {
            _densityOptState.Add((
                new float[layer.Weights.Length],
                new float[layer.Weights.Length],
                new float[layer.Biases.Length],
                new float[layer.Biases.Length]
            ));
        }

        // Initialize for color MLP
        _colorOptState = new List<(float[], float[], float[], float[])>();
        foreach (var layer in model.ColorMLP)
        {
            _colorOptState.Add((
                new float[layer.Weights.Length],
                new float[layer.Weights.Length],
                new float[layer.Biases.Length],
                new float[layer.Biases.Length]
            ));
        }
    }

    private (List<Ray> rays, List<Vector3> colors) SampleRays(List<NerfImageFrame> frames, int count)
    {
        var rays = new List<Ray>(count);
        var colors = new List<Vector3>(count);

        for (int i = 0; i < count; i++)
        {
            // Pick random frame
            var frame = frames[_rng.Next(frames.Count)];

            // Pick random pixel
            int x = _rng.Next(frame.Width);
            int y = _rng.Next(frame.Height);

            // Get ray for this pixel
            var ray = GenerateRay(frame, x, y);
            rays.Add(ray);

            // Get target color
            var color = GetPixelColor(frame, x, y);
            colors.Add(color);
        }

        return (rays, colors);
    }

    private Ray GenerateRay(NerfImageFrame frame, int pixelX, int pixelY)
    {
        // Convert pixel coordinates to normalized device coordinates
        float ndcX = (pixelX + 0.5f - frame.PrincipalPointX) / frame.FocalLengthX;
        float ndcY = (pixelY + 0.5f - frame.PrincipalPointY) / frame.FocalLengthY;

        // Direction in camera space
        var dirCamera = Vector3.Normalize(new Vector3(ndcX, -ndcY, -1.0f));

        // Transform to world space
        var dirWorld = Vector3.TransformNormal(dirCamera, frame.CameraToWorld);

        return new Ray
        {
            Origin = frame.CameraPosition,
            Direction = Vector3.Normalize(dirWorld),
            NearPlane = _config.NearPlane,
            FarPlane = _config.FarPlane
        };
    }

    private Vector3 GetPixelColor(NerfImageFrame frame, int x, int y)
    {
        if (frame.ImageData == null) return Vector3.Zero;

        int idx = (y * frame.Width + x) * frame.Channels;
        if (idx + 2 >= frame.ImageData.Length) return Vector3.Zero;

        return new Vector3(
            frame.ImageData[idx] / 255f,
            frame.ImageData[idx + 1] / 255f,
            frame.ImageData[idx + 2] / 255f
        );
    }

    private (List<Vector3> colors, List<float> densities) RenderRays(List<Ray> rays)
    {
        var colors = new List<Vector3>(rays.Count);
        var densities = new List<float>(rays.Count);

        var model = _dataset.ModelData;

        foreach (var ray in rays)
        {
            // Ray marching with stratified sampling
            var (color, density) = RenderSingleRay(ray, model);
            colors.Add(color);
            densities.Add(density);
        }

        return (colors, densities);
    }

    private (Vector3 color, float totalDensity) RenderSingleRay(Ray ray, NerfModelData model)
    {
        int numSamples = _config.SamplesPerRay;
        float tNear = ray.NearPlane;
        float tFar = ray.FarPlane;
        float stepSize = (tFar - tNear) / numSamples;

        var accumulatedColor = Vector3.Zero;
        float transmittance = 1.0f;
        float totalDensity = 0f;

        for (int i = 0; i < numSamples; i++)
        {
            // Stratified sampling: add jitter within step
            float t = tNear + (i + (float)_rng.NextDouble()) * stepSize;
            var position = ray.Origin + ray.Direction * t;

            // Query the model
            var (density, color) = model.Query(position, ray.Direction);

            if (density > 0.001f)
            {
                // Volume rendering equation
                float alpha = 1.0f - MathF.Exp(-density * stepSize);

                // Accumulate color weighted by transmittance and alpha
                accumulatedColor += transmittance * alpha * color;

                // Update transmittance
                transmittance *= (1.0f - alpha);
                totalDensity += density * stepSize;

                // Early termination
                if (transmittance < 0.001f) break;
            }
        }

        return (accumulatedColor, totalDensity);
    }

    private float ComputeLoss(List<Vector3> predicted, List<Vector3> target, List<float> densities)
    {
        float colorLoss = 0f;
        float distortionLoss = 0f;

        for (int i = 0; i < predicted.Count; i++)
        {
            // L2 loss for color
            var diff = predicted[i] - target[i];
            colorLoss += diff.X * diff.X + diff.Y * diff.Y + diff.Z * diff.Z;

            // Small regularization on density
            distortionLoss += MathF.Abs(densities[i]) * _config.DistortionLossWeight;
        }

        colorLoss /= predicted.Count;
        distortionLoss /= predicted.Count;

        return colorLoss * _config.ColorLossWeight + distortionLoss;
    }

    private void ComputeGradientsAndUpdate(List<Ray> rays, List<Vector3> targetColors, float currentLoss)
    {
        // Simplified gradient computation using finite differences
        // In a real implementation, this would use automatic differentiation
        float lr = GetCurrentLearningRate();
        float epsilon = 1e-4f;

        var model = _dataset.ModelData;

        // Update hash tables (simplified - perturb random entries)
        int hashUpdatesPerIteration = 1000;
        for (int i = 0; i < hashUpdatesPerIteration; i++)
        {
            int level = _rng.Next(model.NumLevels);
            int idx = _rng.Next(model.HashTables[level].Length);

            float original = model.HashTables[level][idx];

            // Compute approximate gradient
            model.HashTables[level][idx] = original + epsilon;
            var (colorsPlus, densitiesPlus) = RenderRays(rays.Take(10).ToList());
            float lossPlus = ComputeLoss(colorsPlus, targetColors.Take(10).ToList(), densitiesPlus);

            model.HashTables[level][idx] = original - epsilon;
            var (colorsMinus, densitiesMinus) = RenderRays(rays.Take(10).ToList());
            float lossMinus = ComputeLoss(colorsMinus, targetColors.Take(10).ToList(), densitiesMinus);

            float gradient = (lossPlus - lossMinus) / (2 * epsilon);

            // Adam update
            _hashGradMomentum[level][idx] = Adam_Beta1 * _hashGradMomentum[level][idx] + (1 - Adam_Beta1) * gradient;
            _hashGradVariance[level][idx] = Adam_Beta2 * _hashGradVariance[level][idx] + (1 - Adam_Beta2) * gradient * gradient;

            float mHat = _hashGradMomentum[level][idx] / (1 - MathF.Pow(Adam_Beta1, _currentIteration + 1));
            float vHat = _hashGradVariance[level][idx] / (1 - MathF.Pow(Adam_Beta2, _currentIteration + 1));

            model.HashTables[level][idx] = original - lr * mHat / (MathF.Sqrt(vHat) + Adam_Epsilon);
        }

        // Update MLP weights (simplified)
        UpdateMLPWeights(model.DensityMLP, _densityOptState, lr, rays, targetColors);
        UpdateMLPWeights(model.ColorMLP, _colorOptState, lr, rays, targetColors);
    }

    private void UpdateMLPWeights(List<MLPLayer> layers,
        List<(float[] wm, float[] wv, float[] bm, float[] bv)> optState,
        float lr, List<Ray> rays, List<Vector3> targetColors)
    {
        // Simplified MLP update - perturb random weights
        int updatesPerLayer = 50;
        float epsilon = 1e-4f;

        for (int layerIdx = 0; layerIdx < layers.Count; layerIdx++)
        {
            var layer = layers[layerIdx];
            var state = optState[layerIdx];

            for (int i = 0; i < updatesPerLayer && i < layer.Weights.Length; i++)
            {
                int idx = _rng.Next(layer.Weights.Length);
                float original = layer.Weights[idx];

                // Finite difference gradient
                layer.Weights[idx] = original + epsilon;
                var (colorsPlus, densitiesPlus) = RenderRays(rays.Take(5).ToList());
                float lossPlus = ComputeLoss(colorsPlus, targetColors.Take(5).ToList(), densitiesPlus);

                layer.Weights[idx] = original - epsilon;
                var (colorsMinus, densitiesMinus) = RenderRays(rays.Take(5).ToList());
                float lossMinus = ComputeLoss(colorsMinus, targetColors.Take(5).ToList(), densitiesMinus);

                float gradient = (lossPlus - lossMinus) / (2 * epsilon);

                // Adam update
                state.wm[idx] = Adam_Beta1 * state.wm[idx] + (1 - Adam_Beta1) * gradient;
                state.wv[idx] = Adam_Beta2 * state.wv[idx] + (1 - Adam_Beta2) * gradient * gradient;

                float mHat = state.wm[idx] / (1 - MathF.Pow(Adam_Beta1, _currentIteration + 1));
                float vHat = state.wv[idx] / (1 - MathF.Pow(Adam_Beta2, _currentIteration + 1));

                layer.Weights[idx] = original - lr * mHat / (MathF.Sqrt(vHat) + Adam_Epsilon);
            }
        }
    }

    private float GetCurrentLearningRate()
    {
        // Exponential decay
        float decaySteps = _dataset.TotalIterations / 5f;
        float decay = MathF.Pow(_config.LearningRateDecay, _currentIteration / decaySteps);
        return _config.LearningRate * decay;
    }

    private void GeneratePreview()
    {
        try
        {
            int width = 256;
            int height = 256;
            var previewData = new byte[width * height * 3];

            // Generate a novel view
            var frame = _dataset.ImageCollection.Frames.FirstOrDefault();
            if (frame == null) return;

            // Rotate camera slightly for preview
            float angle = MathF.PI * 0.1f;
            var rotatedPose = Matrix4x4.CreateRotationY(angle) * frame.CameraToWorld;

            var position = new Vector3(rotatedPose.M41, rotatedPose.M42, rotatedPose.M43);
            var focalLength = frame.FocalLengthX * width / frame.Width;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float ndcX = (x + 0.5f - width / 2f) / focalLength;
                    float ndcY = (y + 0.5f - height / 2f) / focalLength;

                    var dirCamera = Vector3.Normalize(new Vector3(ndcX, -ndcY, -1.0f));
                    var dirWorld = Vector3.TransformNormal(dirCamera, rotatedPose);

                    var ray = new Ray
                    {
                        Origin = position,
                        Direction = Vector3.Normalize(dirWorld),
                        NearPlane = _config.NearPlane,
                        FarPlane = _config.FarPlane
                    };

                    var (color, _) = RenderSingleRay(ray, _dataset.ModelData);

                    int idx = (y * width + x) * 3;
                    previewData[idx] = (byte)Math.Clamp(color.X * 255, 0, 255);
                    previewData[idx + 1] = (byte)Math.Clamp(color.Y * 255, 0, 255);
                    previewData[idx + 2] = (byte)Math.Clamp(color.Z * 255, 0, 255);
                }
            }

            OnPreviewReady?.Invoke(previewData, width, height);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to generate preview: {ex.Message}");
        }
    }

    /// <summary>
    /// Render a novel view from the trained model
    /// </summary>
    public byte[] RenderView(Matrix4x4 cameraToWorld, float focalLength, int width, int height)
    {
        if (_dataset.ModelData == null)
        {
            Logger.LogWarning("No trained model available");
            return null;
        }

        var imageData = new byte[width * height * 3];
        var position = new Vector3(cameraToWorld.M41, cameraToWorld.M42, cameraToWorld.M43);

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                float ndcX = (x + 0.5f - width / 2f) / focalLength;
                float ndcY = (y + 0.5f - height / 2f) / focalLength;

                var dirCamera = Vector3.Normalize(new Vector3(ndcX, -ndcY, -1.0f));
                var dirWorld = Vector3.TransformNormal(dirCamera, cameraToWorld);

                var ray = new Ray
                {
                    Origin = position,
                    Direction = Vector3.Normalize(dirWorld),
                    NearPlane = _config.NearPlane,
                    FarPlane = _config.FarPlane
                };

                var (color, _) = RenderSingleRay(ray, _dataset.ModelData);

                int idx = (y * width + x) * 3;
                imageData[idx] = (byte)Math.Clamp(color.X * 255, 0, 255);
                imageData[idx + 1] = (byte)Math.Clamp(color.Y * 255, 0, 255);
                imageData[idx + 2] = (byte)Math.Clamp(color.Z * 255, 0, 255);
            }
        });

        return imageData;
    }
}

/// <summary>
/// Ray structure for ray marching
/// </summary>
public struct Ray
{
    public Vector3 Origin { get; set; }
    public Vector3 Direction { get; set; }
    public float NearPlane { get; set; }
    public float FarPlane { get; set; }

    public Vector3 PointAt(float t) => Origin + Direction * t;
}
