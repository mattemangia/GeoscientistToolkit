// GeoscientistToolkit/Business/Photogrammetry/PhotogrammetryProcessingService.cs

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.Panorama;
using GeoscientistToolkit.Business.Photogrammetry;
using GeoscientistToolkit.Business.Photogrammetry.Math;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit
{
    /// <summary>
    /// Main service for photogrammetry processing pipeline
    /// </summary>
    public class PhotogrammetryProcessingService
    {
        #region Properties

        public PhotogrammetryState State { get; private set; } = PhotogrammetryState.Idle;
        public float Progress { get; private set; }
        public string StatusMessage { get; private set; }
        public ConcurrentQueue<string> Logs { get; } = new();
        public List<PhotogrammetryImage> Images { get; } = new();
        public PhotogrammetryGraph Graph { get; private set; }
        public List<PhotogrammetryImageGroup> ImageGroups => Graph?.FindConnectedComponents() ?? new List<PhotogrammetryImageGroup>();

        public PhotogrammetryPointCloud SparseCloud { get; private set; }
        public PhotogrammetryPointCloud DenseCloud { get; private set; }
        public Mesh3DDataset GeneratedMesh { get; private set; }
        public bool EnableGeoreferencing { get; set; } = true;

        #endregion

        #region Fields

        private readonly List<ImageDataset> _datasets;
        private readonly FeatureProcessor _featureProcessor;
        private readonly ReconstructionEngine _reconstructionEngine;
        private readonly GeoreferencingService _georeferencingService;
        private readonly MeshGenerator _meshGenerator;
        private readonly ProductGenerator _productGenerator;
        private CancellationTokenSource _cancellationTokenSource;

        #endregion

        #region Constructor

        public PhotogrammetryProcessingService(List<ImageDataset> datasets)
        {
            _datasets = datasets ?? throw new ArgumentNullException(nameof(datasets));
            _featureProcessor = new FeatureProcessor(this);
            _reconstructionEngine = new ReconstructionEngine(this);
            _georeferencingService = new GeoreferencingService(this);
            _meshGenerator = new MeshGenerator(this);
            _productGenerator = new ProductGenerator(this);
        }

        #endregion

        #region Public Methods - Main Processing Pipeline

        /// <summary>
        /// Starts the photogrammetry processing pipeline asynchronously
        /// </summary>
        public async Task StartProcessingAsync()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            try
            {
                await InitializeProcessingAsync(token);
                await _featureProcessor.DetectFeaturesAsync(Images, token, UpdateProgress);
                await _featureProcessor.MatchFeaturesAsync(Images, Graph, token, UpdateProgress);
                AnalyzeConnectivity();
            }
            catch (OperationCanceledException)
            {
                HandleCancellation();
            }
            catch (Exception ex)
            {
                HandleError(ex);
            }
        }

        /// <summary>
        /// Cancels the current processing operation
        /// </summary>
        public void Cancel()
        {
            _cancellationTokenSource?.Cancel();
            Log("Cancellation requested.");
        }

        #endregion

        #region Public Methods - Image Management

        /// <summary>
        /// Removes an image from the reconstruction
        /// </summary>
        public void RemoveImage(PhotogrammetryImage image)
        {
            Images.Remove(image);
            Graph?.RemoveNode(image.Id);
            Log($"Removed image: {image.Dataset.Name}");
            AnalyzeConnectivity();
        }

        /// <summary>
        /// Adds a manual link between two images
        /// </summary>
        public void AddManualLinkAndRecompute(PhotogrammetryImage img1, PhotogrammetryImage img2, 
            List<(Vector2 P1, Vector2 P2)> points)
        {
            if (points.Count < 8)
            {
                Log("Manual link failed: At least 8 point pairs are required.");
                return;
            }

            var relativePose = _featureProcessor.ComputeManualPose(img1, img2, points);
            if (relativePose.HasValue)
            {
                Graph.AddEdge(img1, img2, new List<FeatureMatch>(), relativePose.Value);
                Log($"Successfully added manual link between {img1.Dataset.Name} and {img2.Dataset.Name}.");
                AnalyzeConnectivity();
            }
            else
            {
                Log($"Failed to compute a valid link. Points may be degenerate.");
            }
        }

        #endregion

        #region Public Methods - Ground Control Points

        /// <summary>
        /// Adds or updates a ground control point
        /// </summary>
        public void AddOrUpdateGroundControlPoint(PhotogrammetryImage image, GroundControlPoint gcp)
        {
            var existing = image.GroundControlPoints.FirstOrDefault(g => g.Id == gcp.Id);
            if (existing != null)
            {
                image.GroundControlPoints.Remove(existing);
            }
            image.GroundControlPoints.Add(gcp);
            Log($"Updated GCP '{gcp.Name}' on {image.Dataset.Name}");
        }

        /// <summary>
        /// Removes a ground control point
        /// </summary>
        public void RemoveGroundControlPoint(PhotogrammetryImage image, GroundControlPoint gcp)
        {
            image.GroundControlPoints.Remove(gcp);
            Log($"Removed GCP '{gcp.Name}' from {image.Dataset.Name}");
        }

        #endregion

        #region Public Methods - Reconstruction

        /// <summary>
        /// Builds sparse point cloud from feature matches
        /// </summary>
        public async Task BuildSparseCloudAsync()
        {
            State = PhotogrammetryState.ComputingSparseReconstruction;
            Log("Building sparse point cloud from feature matches...");
            UpdateProgress(0.75f, "Computing 3D structure...");

            SparseCloud = await _reconstructionEngine.BuildSparseCloudAsync(
                Images, Graph, EnableGeoreferencing, UpdateProgress);

            if (EnableGeoreferencing)
            {
                await _georeferencingService.ApplyGeoreferencingAsync(SparseCloud, Images);
            }

            UpdateProgress(0.85f, $"Sparse cloud complete: {SparseCloud.Points.Count} points");
            State = PhotogrammetryState.Completed;
            StatusMessage = "Sparse reconstruction complete. Use Build options for further processing.";
        }

        /// <summary>
        /// Builds dense point cloud
        /// </summary>
        public async Task BuildDenseCloudAsync(DenseCloudOptions options)
        {
            if (SparseCloud == null)
            {
                Log("Error: Sparse cloud must be built first.");
                return;
            }

            State = PhotogrammetryState.BuildingDenseCloud;
            Log($"Building dense point cloud (Quality: {options.Quality})...");
            
            DenseCloud = await _reconstructionEngine.BuildDenseCloudAsync(
                SparseCloud, Images, options, UpdateProgress);
            
            UpdateProgress(1.0f, "Dense cloud complete");
            State = PhotogrammetryState.Completed;
        }

        /// <summary>
        /// Builds 3D mesh from point cloud
        /// </summary>
        public async Task BuildMeshAsync(MeshOptions options, string outputPath)
        {
            var sourceCloud = options.Source == MeshOptions.SourceData.DenseCloud ? DenseCloud : SparseCloud;
            if (sourceCloud == null)
            {
                Log("Error: Point cloud is required for mesh generation.");
                return;
            }

            State = PhotogrammetryState.BuildingMesh;
            GeneratedMesh = await _meshGenerator.BuildMeshAsync(sourceCloud, options, outputPath, UpdateProgress);
            State = PhotogrammetryState.Completed;
        }

        /// <summary>
        /// Builds texture for mesh
        /// </summary>
        public async Task BuildTextureAsync(TextureOptions options, string outputPath)
        {
            if (GeneratedMesh == null)
            {
                Log("Error: Mesh must be built before generating texture.");
                return;
            }

            State = PhotogrammetryState.BuildingTexture;
            await _meshGenerator.BuildTextureAsync(GeneratedMesh, Images, options, outputPath, UpdateProgress);
            State = PhotogrammetryState.Completed;
        }

        #endregion

        #region Public Methods - Products

        /// <summary>
        /// Builds orthomosaic from reconstructed scene
        /// </summary>
        public async Task BuildOrthomosaicAsync(OrthomosaicOptions options, string outputPath)
        {
            State = PhotogrammetryState.BuildingOrthomosaic;
            await _productGenerator.BuildOrthomosaicAsync(
                DenseCloud ?? SparseCloud, GeneratedMesh, Images, options, outputPath, UpdateProgress);
            State = PhotogrammetryState.Completed;
        }

        /// <summary>
        /// Builds digital elevation model (DEM)
        /// </summary>
        public async Task BuildDEMAsync(DEMOptions options, string outputPath)
        {
            State = PhotogrammetryState.BuildingDEM;
            await _productGenerator.BuildDEMAsync(
                DenseCloud ?? SparseCloud, GeneratedMesh, options, outputPath, UpdateProgress);
            State = PhotogrammetryState.Completed;
        }

        #endregion

        #region Internal Methods

        internal void Log(string message)
        {
            var logMsg = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Logs.Enqueue(logMsg);
            Logger.Log(logMsg);
        }

        internal void UpdateProgress(float progress, string message)
        {
            Progress = progress;
            StatusMessage = message;
        }

        #endregion

        #region Private Methods

        private async Task InitializeProcessingAsync(CancellationToken token)
        {
            Log("Starting photogrammetry processing...");
            State = PhotogrammetryState.Initializing;
            UpdateProgress(0, "Initializing...");

            Images.Clear();
            State = PhotogrammetryState.ExtractingMetadata;
            UpdateProgress(0.05f, "Extracting image metadata...");

            await Task.Run(() => LoadImages(token), token);
            
            Graph = new PhotogrammetryGraph(Images);
            ComputeIntrinsicsForAllImages();
        }

        private void LoadImages(CancellationToken token)
        {
            foreach (var ds in _datasets)
            {
                token.ThrowIfCancellationRequested();
                
                ds.Load();
                if (ds.ImageData == null)
                {
                    Log($"Warning: Could not load image data for {ds.Name}. Skipping.");
                    continue;
                }

                var pgImage = new PhotogrammetryImage(ds);
                ExtractMetadata(pgImage);
                Images.Add(pgImage);
            }

            Log($"Loaded {Images.Count} images.");
        }

        private void ExtractMetadata(PhotogrammetryImage image)
        {
            if (image.IsGeoreferenced)
            {
                Log($"{image.Dataset.Name}: Lat={image.Latitude:F6}, Lon={image.Longitude:F6}, " +
                    $"Alt={image.Altitude?.ToString("F2") ?? "N/A"}");
            }
        }

        private void ComputeIntrinsicsForAllImages()
        {
            Log("Computing camera intrinsic parameters...");
            foreach (var image in Images)
            {
                image.IntrinsicMatrix = CameraCalibration.ComputeIntrinsics(image);
            }
        }

        private void AnalyzeConnectivity()
        {
            if (Images.Count < 2)
            {
                State = PhotogrammetryState.Failed;
                StatusMessage = "Not enough images for photogrammetry.";
                Log("Error: At least two images are required.");
                return;
            }

            var imagesWithNoFeatures = Images.Where(img => img.Features?.KeyPoints.Count < 20).ToList();
            var imageGroups = ImageGroups;

            if (imagesWithNoFeatures.Any() || imageGroups.Count > 1)
            {
                State = PhotogrammetryState.AwaitingManualInput;
                StatusMessage = "User input required to proceed.";
                Log("Process paused. Please resolve unmatched images or groups.");
            }
            else
            {
                Log("All images successfully matched into a single group. Computing global camera poses...");
                _reconstructionEngine.ComputeGlobalPoses(imageGroups.First(), Images, Graph);

                State = PhotogrammetryState.ComputingSparseReconstruction;
                StatusMessage = "Ready for sparse reconstruction.";
                Log("Global camera poses computed.");
                UpdateProgress(0.7f, "Alignment complete. Ready for reconstruction.");
            }
        }

        private void HandleCancellation()
        {
            State = PhotogrammetryState.Failed;
            Log("Process was canceled by the user.");
        }

        private void HandleError(Exception ex)
        {
            State = PhotogrammetryState.Failed;
            StatusMessage = "An error occurred.";
            Log($"FATAL ERROR: {ex.Message}\n{ex.StackTrace}");
            Logger.LogError($"[PhotogrammetryService] {ex.Message}");
        }

        #endregion
    }

    /// <summary>
    /// Represents a photogrammetry processing job
    /// </summary>
    public class PhotogrammetryJob
    {
        public DatasetGroup ImageGroup { get; }
        public PhotogrammetryProcessingService Service { get; }
        public Guid Id { get; } = Guid.NewGuid();

        public PhotogrammetryJob(DatasetGroup imageGroup)
        {
            ImageGroup = imageGroup ?? throw new ArgumentNullException(nameof(imageGroup));
            Service = new PhotogrammetryProcessingService(
                imageGroup.Datasets.Cast<ImageDataset>().ToList());
        }
    }
}