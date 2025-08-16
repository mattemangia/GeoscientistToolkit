// GeoscientistToolkit/Data/Image/ImageTools.cs (Complete Implementation)
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.Image.Segmentation;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using System.Runtime.InteropServices;
using BitMiracle.LibTiff.Classic;
using GeoscientistToolkit.UI.Utils;
using SkiaSharp;
using System.IO;

namespace GeoscientistToolkit.Data.Image
{
    public class ImageTools : IDatasetTools
    {
        private float _brightness = 0;
        private float _contrast = 1;
        private ImageSegmentationToolsUI _segmentationTools;
        private int _selectedToolTab = 0;
        private TagManager _tagManager = new TagManager();

        private ImageDataset _activeAdjustmentDataset;
        private byte[] _originalImageData;

        private float _particleMinSize = 10;
        private float _particleMaxSize = 1000;
        private float _circularityThreshold = 0.5f;
        private float _thresholdValue = 128;
        private bool _useAdaptiveThreshold = false;
        private List<ParticleData> _detectedParticles = new List<ParticleData>();

        private int _gridSize = 100;
        private bool _showGrid = true;
        private bool _countingActive = false;
        private Dictionary<string, int> _mineralCounts = new Dictionary<string, int>();
        private List<PointCountData> _countedPoints = new List<PointCountData>();
        private string _selectedMineral = "Quartz";
        private List<string> _mineralTypes = new List<string>
        {
            "Quartz", "Feldspar", "Biotite", "Muscovite", "Olivine",
            "Pyroxene", "Amphibole", "Garnet", "Calcite", "Unknown"
        };

        private int _numClasses = 5;
        private float _maxIterations = 100;
        private float _convergenceThreshold = 0.01f;
        private int _redBandIndex = 0;
        private int _nirBandIndex = 3;
        private int _blueBandIndex = 2;
        private ClassificationResult _classificationResult;

        public ImageTools()
        {
            _segmentationTools = new ImageSegmentationToolsUI();
        }


        public void Draw(Dataset dataset)
        {
            if (dataset is not ImageDataset imageDataset) return;

            var availableTools = imageDataset.Tags.GetAvailableTools();

            if (ImGui.BeginTabBar("ImageToolsTabs"))
            {
                if (ImGui.BeginTabItem("Tags"))
                {
                    _selectedToolTab = -1;
                    DrawTagsTab(imageDataset);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Adjustments"))
                {
                    _selectedToolTab = 0;
                    DrawAdjustments(imageDataset);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Segmentation"))
                {
                    _selectedToolTab = 1;
                    DrawSegmentation(imageDataset);
                    ImGui.EndTabItem();
                }

                if (imageDataset.HasTag(ImageTag.SEM) || imageDataset.HasTag(ImageTag.TEM))
                {
                    if (ImGui.BeginTabItem("Particle Analysis"))
                    {
                        DrawParticleAnalysis(imageDataset);
                        ImGui.EndTabItem();
                    }
                }

                if (imageDataset.HasTag(ImageTag.ThinSection))
                {
                    if (ImGui.BeginTabItem("Point Counting"))
                    {
                        DrawPointCounting(imageDataset);
                        ImGui.EndTabItem();
                    }
                }

                if (imageDataset.HasTag(ImageTag.Drone) || imageDataset.HasTag(ImageTag.Satellite))
                {
                    if (ImGui.BeginTabItem("Remote Sensing"))
                    {
                        DrawRemoteSensingTools(imageDataset);
                        ImGui.EndTabItem();
                    }
                }

                ImGui.EndTabBar();
            }
        }

        private void DrawTagsTab(ImageDataset imageDataset)
        {
            ImGui.Text("Image Tags");
            ImGui.Separator();

            ImGui.Text("Current Tags:");
            var currentTags = imageDataset.Tags.GetFlags().ToList();
            if (currentTags.Count == 0)
            {
                ImGui.TextDisabled("No tags assigned");
            }
            else
            {
                foreach (var tag in currentTags)
                {
                    ImGui.BulletText(tag.GetDisplayName());
                    ImGui.SameLine();
                    ImGui.PushID(tag.GetHashCode());
                    if (ImGui.SmallButton("Remove"))
                    {
                        imageDataset.RemoveTag(tag);
                        ProjectManager.Instance.HasUnsavedChanges = true;
                    }
                    ImGui.PopID();
                }
            }

            ImGui.Separator();
            ImGui.Text("Add Tags:");

            if (ImGui.TreeNode("Microscopy"))
            {
                DrawTagCheckbox(imageDataset, ImageTag.SEM);
                DrawTagCheckbox(imageDataset, ImageTag.TEM);
                DrawTagCheckbox(imageDataset, ImageTag.OpticalMicroscopy);
                DrawTagCheckbox(imageDataset, ImageTag.Fluorescence);
                DrawTagCheckbox(imageDataset, ImageTag.Confocal);
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Medical Imaging"))
            {
                DrawTagCheckbox(imageDataset, ImageTag.CTSlice);
                DrawTagCheckbox(imageDataset, ImageTag.MRI);
                DrawTagCheckbox(imageDataset, ImageTag.XRay);
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Remote Sensing"))
            {
                DrawTagCheckbox(imageDataset, ImageTag.Drone);
                DrawTagCheckbox(imageDataset, ImageTag.Satellite);
                DrawTagCheckbox(imageDataset, ImageTag.Aerial);
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Geological"))
            {
                DrawTagCheckbox(imageDataset, ImageTag.ThinSection);
                DrawTagCheckbox(imageDataset, ImageTag.CorePhoto);
                DrawTagCheckbox(imageDataset, ImageTag.OutcropPhoto);
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Properties"))
            {
                DrawTagCheckbox(imageDataset, ImageTag.Calibrated);
                DrawTagCheckbox(imageDataset, ImageTag.Georeferenced);
                DrawTagCheckbox(imageDataset, ImageTag.TimeSeries);
                DrawTagCheckbox(imageDataset, ImageTag.Multispectral);
                ImGui.TreePop();
            }
        }

        private void DrawTagCheckbox(ImageDataset dataset, ImageTag tag)
        {
            bool hasTag = dataset.HasTag(tag);
            if (ImGui.Checkbox(tag.GetDisplayName(), ref hasTag))
            {
                if (hasTag)
                    dataset.AddTag(tag);
                else
                    dataset.RemoveTag(tag);
                ProjectManager.Instance.HasUnsavedChanges = true;
            }
        }

        private void DrawAdjustments(ImageDataset imageDataset)
        {
            ImGui.Text("Image Adjustments");
            ImGui.Separator();

            if (_activeAdjustmentDataset != imageDataset)
            {
                _activeAdjustmentDataset = imageDataset;
                _activeAdjustmentDataset.Load();
                if (_activeAdjustmentDataset.ImageData != null)
                {
                    _originalImageData = (byte[])_activeAdjustmentDataset.ImageData.Clone();
                }
                else
                {
                    _originalImageData = null;
                }
                _brightness = 0;
                _contrast = 1;
            }

            bool valueChanged = false;
            valueChanged |= ImGui.SliderFloat("Brightness", ref _brightness, -1.0f, 1.0f);
            valueChanged |= ImGui.SliderFloat("Contrast", ref _contrast, 0.0f, 2.0f);

            if (valueChanged)
            {
                ApplyImageAdjustments(imageDataset);
            }

            if (ImGui.Button("Reset Adjustments", new Vector2(-1, 0)))
            {
                if (_originalImageData != null)
                {
                    Array.Copy(_originalImageData, imageDataset.ImageData, _originalImageData.Length);
                    GlobalPerformanceManager.Instance.TextureCache.Invalidate(imageDataset.FilePath);
                    ProjectManager.Instance.HasUnsavedChanges = true;
                    Logger.Log($"Reset adjustments for {imageDataset.Name}");
                }
                _brightness = 0;
                _contrast = 1;
            }
        }

        private void ApplyImageAdjustments(ImageDataset imageDataset)
        {
            if (_originalImageData == null) return;

            byte[] destinationData = imageDataset.ImageData;
            if (destinationData == null) return;

            for (int i = 0; i < _originalImageData.Length; i += 4)
            {
                for (int c = 0; c < 3; c++)
                {
                    float value = _originalImageData[i + c];
                    value = (value - 128) * _contrast + 128 + _brightness * 255;
                    destinationData[i + c] = (byte)Math.Clamp(value, 0, 255);
                }
                destinationData[i + 3] = _originalImageData[i + 3];
            }

            GlobalPerformanceManager.Instance.TextureCache.Invalidate(imageDataset.FilePath);
            ProjectManager.Instance.HasUnsavedChanges = true;
        }

        private void DrawSegmentation(ImageDataset imageDataset)
        {
            _segmentationTools.Draw(imageDataset);
        }
        private ImageViewer _currentViewer;
        public void SetCurrentViewer(ImageViewer viewer)
        {
            _currentViewer = viewer;
            if (_segmentationTools != null && viewer != null)
            {
                // Set the callback to use the viewer's invalidate method
                _segmentationTools.SetInvalidateCallback(() => viewer.InvalidateSegmentationTexture());
                viewer.SetSegmentationTools(_segmentationTools);
            }
        }
        private void DrawParticleAnalysis(ImageDataset imageDataset)
        {
            ImGui.Text("Particle Analysis");
            ImGui.Separator();

            ImGui.TextWrapped("Automated particle detection and measurement for SEM/TEM images.");

            ImGui.SliderFloat("Threshold", ref _thresholdValue, 0, 255);
            ImGui.Checkbox("Use Adaptive Threshold", ref _useAdaptiveThreshold);
            ImGui.SliderFloat("Min Size (pixels)", ref _particleMinSize, 1, 100);
            ImGui.SliderFloat("Max Size (pixels)", ref _particleMaxSize, 100, 5000);
            ImGui.SliderFloat("Circularity Threshold", ref _circularityThreshold, 0, 1);

            if (ImGui.Button("Detect Particles", new Vector2(-1, 0)))
            {
                Logger.Log($"Particle analysis started for {imageDataset.Name}");
                PerformParticleAnalysis(imageDataset);
            }

            ImGui.Separator();

            if (_detectedParticles.Count > 0)
            {
                ImGui.Text($"Detected Particles: {_detectedParticles.Count}");

                var avgArea = _detectedParticles.Average(p => p.Area);
                var avgCircularity = _detectedParticles.Average(p => p.Circularity);
                var avgDiameter = _detectedParticles.Average(p => p.EquivalentDiameter);

                ImGui.Text($"Average Area: {avgArea:F2} pixels²");
                ImGui.Text($"Average Diameter: {avgDiameter:F2} pixels");
                ImGui.Text($"Average Circularity: {avgCircularity:F3}");

                if (ImGui.CollapsingHeader("Particle Details"))
                {
                    if (ImGui.BeginTable("ParticleTable", 5))
                    {
                        ImGui.TableSetupColumn("ID");
                        ImGui.TableSetupColumn("Area");
                        ImGui.TableSetupColumn("Diameter");
                        ImGui.TableSetupColumn("Circularity");
                        ImGui.TableSetupColumn("Center");
                        ImGui.TableHeadersRow();

                        for (int i = 0; i < Math.Min(_detectedParticles.Count, 100); i++)
                        {
                            var p = _detectedParticles[i];
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn(); ImGui.Text($"{p.Id}");
                            ImGui.TableNextColumn(); ImGui.Text($"{p.Area:F1}");
                            ImGui.TableNextColumn(); ImGui.Text($"{p.EquivalentDiameter:F1}");
                            ImGui.TableNextColumn(); ImGui.Text($"{p.Circularity:F3}");
                            ImGui.TableNextColumn(); ImGui.Text($"({p.CenterX:F0},{p.CenterY:F0})");
                        }

                        ImGui.EndTable();
                    }
                }

                if (ImGui.Button("Export Results"))
                {
                    ExportParticleAnalysisResults(imageDataset);
                }
            }
            else
            {
                ImGui.TextDisabled("No particles detected. Click 'Detect Particles' to start analysis.");
            }
        }

        private void DrawPointCounting(ImageDataset imageDataset)
        {
            ImGui.Text("Point Counting");
            ImGui.Separator();

            ImGui.TextWrapped("Modal analysis tool for thin sections.");

            ImGui.Checkbox("Show Grid", ref _showGrid);
            ImGui.SliderInt("Grid Size", ref _gridSize, 50, 500);

            if (ImGui.BeginCombo("Current Mineral", _selectedMineral))
            {
                foreach (var mineral in _mineralTypes)
                {
                    if (ImGui.Selectable(mineral, mineral == _selectedMineral))
                    {
                        _selectedMineral = mineral;
                    }
                }
                ImGui.EndCombo();
            }

            if (!_countingActive)
            {
                if (ImGui.Button("Start Counting", new Vector2(-1, 0)))
                {
                    Logger.Log($"Point counting started for {imageDataset.Name}");
                    StartPointCounting(imageDataset);
                }
            }
            else
            {
                if (ImGui.Button("Stop Counting", new Vector2(-1, 0)))
                {
                    _countingActive = false;
                    Logger.Log("Point counting stopped");
                }
                ImGui.Text($"Total Points: {_countedPoints.Count}");
            }

            ImGui.Separator();
            ImGui.Text("Mineral Counts:");

            if (_mineralCounts.Count > 0)
            {
                int totalPoints = _mineralCounts.Values.Sum();

                if (ImGui.BeginTable("MineralTable", 3))
                {
                    ImGui.TableSetupColumn("Mineral");
                    ImGui.TableSetupColumn("Count");
                    ImGui.TableSetupColumn("Percentage");
                    ImGui.TableHeadersRow();

                    foreach (var kvp in _mineralCounts.OrderByDescending(m => m.Value))
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn(); ImGui.Text(kvp.Key);
                        ImGui.TableNextColumn(); ImGui.Text($"{kvp.Value}");
                        ImGui.TableNextColumn();
                        float percentage = totalPoints > 0 ? (kvp.Value * 100f / totalPoints) : 0;
                        ImGui.Text($"{percentage:F1}%");
                    }

                    ImGui.EndTable();
                }

                if (ImGui.Button("Export Results"))
                {
                    ExportPointCountingResults(imageDataset);
                }
            }
            else
            {
                ImGui.TextDisabled("Click 'Start Counting' to begin");
            }
        }

        private void DrawRemoteSensingTools(ImageDataset imageDataset)
        {
            ImGui.Text("Remote Sensing Tools");
            ImGui.Separator();

            if (ImGui.CollapsingHeader("Vegetation Indices"))
            {
                if (imageDataset.HasTag(ImageTag.Multispectral))
                {
                    ImGui.Text("Band Configuration:");
                    ImGui.SliderInt("Red Band", ref _redBandIndex, 0, 7);
                    ImGui.SliderInt("NIR Band", ref _nirBandIndex, 0, 7);
                    ImGui.SliderInt("Blue Band", ref _blueBandIndex, 0, 7);
                }

                if (ImGui.Button("Calculate NDVI"))
                {
                    Logger.Log($"NDVI calculation started for {imageDataset.Name}");
                    CalculateNDVI(imageDataset);
                }

                if (ImGui.Button("Calculate EVI"))
                {
                    Logger.Log($"EVI calculation started for {imageDataset.Name}");
                    CalculateEVI(imageDataset);
                }
            }

            if (ImGui.CollapsingHeader("Classification"))
            {
                ImGui.SliderInt("Number of Classes", ref _numClasses, 2, 20);
                ImGui.SliderFloat("Max Iterations", ref _maxIterations, 10, 500);
                ImGui.SliderFloat("Convergence Threshold", ref _convergenceThreshold, 0.001f, 0.1f);

                if (ImGui.Button("Unsupervised Classification", new Vector2(-1, 0)))
                {
                    Logger.Log($"Classification started for {imageDataset.Name}");
                    PerformUnsupervisedClassification(imageDataset, _numClasses);
                }

                if (_classificationResult != null)
                {
                    ImGui.Separator();
                    ImGui.Text($"Classification Complete");
                    ImGui.Text($"Classes: {_classificationResult.NumClasses}");
                    ImGui.Text($"Iterations: {_classificationResult.Iterations}");
                    ImGui.Text($"Convergence: {_classificationResult.Convergence:F4}");

                    if (ImGui.Button("Export Classification"))
                    {
                        ExportClassificationResult(imageDataset, _classificationResult);
                    }
                }
            }
        }

        private void PerformParticleAnalysis(ImageDataset imageDataset)
        {
            if (imageDataset.ImageData == null) { imageDataset.Load(); }
            _detectedParticles.Clear();
            byte[] grayscale = ConvertToGrayscale(imageDataset.ImageData, imageDataset.Width, imageDataset.Height);
            byte[] binary = _useAdaptiveThreshold ?
                ApplyAdaptiveThreshold(grayscale, imageDataset.Width, imageDataset.Height) :
                ApplyThreshold(grayscale, _thresholdValue);
            var components = FindConnectedComponents(binary, imageDataset.Width, imageDataset.Height);
            int particleId = 1;
            foreach (var component in components)
            {
                float area = component.Count;
                if (area < _particleMinSize || area > _particleMaxSize) continue;
                var particle = AnalyzeParticle(component, particleId++, imageDataset.Width);
                if (particle.Circularity < _circularityThreshold) continue;
                _detectedParticles.Add(particle);
            }
            Logger.Log($"Detected {_detectedParticles.Count} particles");
        }

        private ParticleData AnalyzeParticle(List<int> pixels, int id, int imageWidth)
        {
            var particle = new ParticleData { Id = id };
            float sumX = 0, sumY = 0;
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (int pixel in pixels)
            {
                int x = pixel % imageWidth;
                int y = pixel / imageWidth;
                sumX += x; sumY += y;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
            particle.Area = pixels.Count;
            particle.CenterX = sumX / pixels.Count;
            particle.CenterY = sumY / pixels.Count;
            particle.EquivalentDiameter = (float)Math.Sqrt(4 * particle.Area / Math.PI);
            float perimeter = CalculatePerimeter(pixels, imageWidth);
            particle.Circularity = (float)(4 * Math.PI * particle.Area / (perimeter * perimeter));
            particle.Circularity = Math.Min(1.0f, particle.Circularity);
            float width = maxX - minX + 1;
            float height = maxY - minY + 1;
            particle.AspectRatio = width / Math.Max(height, 1);
            return particle;
        }

        private float CalculatePerimeter(List<int> pixels, int width)
        {
            HashSet<int> pixelSet = new HashSet<int>(pixels);
            float perimeter = 0;
            foreach (int pixel in pixels)
            {
                int x = pixel % width;
                int y = pixel / width;
                int neighbors = 0;
                if (pixelSet.Contains(pixel - 1)) neighbors++;
                if (pixelSet.Contains(pixel + 1)) neighbors++;
                if (pixelSet.Contains(pixel - width)) neighbors++;
                if (pixelSet.Contains(pixel + width)) neighbors++;
                if (neighbors < 4) { perimeter += (4 - neighbors); }
            }
            return perimeter;
        }

        private void StartPointCounting(ImageDataset imageDataset)
        {
            _countingActive = true;
            _mineralCounts.Clear();
            _countedPoints.Clear();
            foreach (var mineral in _mineralTypes) { _mineralCounts[mineral] = 0; }
        }

        public void HandlePointCountClick(ImageDataset imageDataset, Vector2 clickPosition)
        {
            if (!_countingActive) return;
            int gridX = (int)(clickPosition.X / _gridSize);
            int gridY = (int)(clickPosition.Y / _gridSize);
            var existingPoint = _countedPoints.FirstOrDefault(p => p.GridX == gridX && p.GridY == gridY);
            if (existingPoint != null)
            {
                _mineralCounts[existingPoint.Mineral]--;
                existingPoint.Mineral = _selectedMineral;
                _mineralCounts[_selectedMineral]++;
            }
            else
            {
                _countedPoints.Add(new PointCountData { GridX = gridX, GridY = gridY, Mineral = _selectedMineral, Position = clickPosition });
                _mineralCounts[_selectedMineral]++;
            }
        }

        private void CalculateNDVI(ImageDataset imageDataset)
        {
            if (!imageDataset.HasTag(ImageTag.Multispectral)) { Logger.LogWarning("NDVI calculation requires multispectral imagery"); return; }
            if (imageDataset.ImageData == null) { imageDataset.Load(); }
            int pixelCount = imageDataset.Width * imageDataset.Height;
            float[] ndviData = new float[pixelCount];
            for (int i = 0; i < pixelCount; i++)
            {
                int idx = i * 4;
                float red = imageDataset.ImageData[idx + _redBandIndex];
                float nir = imageDataset.ImageData[idx + _nirBandIndex];
                float denominator = nir + red;
                ndviData[i] = denominator > 0 ? (nir - red) / denominator : 0;
            }
            imageDataset.ImageMetadata["NDVI_Min"] = ndviData.Min();
            imageDataset.ImageMetadata["NDVI_Max"] = ndviData.Max();
            imageDataset.ImageMetadata["NDVI_Mean"] = ndviData.Average();
            CreateNDVIVisualization(imageDataset, ndviData);
            Logger.Log($"NDVI calculation complete. Range: [{ndviData.Min():F3}, {ndviData.Max():F3}]");
        }

        private void CalculateEVI(ImageDataset imageDataset)
        {
            if (!imageDataset.HasTag(ImageTag.Multispectral)) { Logger.LogWarning("EVI calculation requires multispectral imagery"); return; }
            if (imageDataset.ImageData == null) { imageDataset.Load(); }
            int pixelCount = imageDataset.Width * imageDataset.Height;
            float[] eviData = new float[pixelCount];
            const float G = 2.5f, C1 = 6.0f, C2 = 7.5f, L = 1.0f;
            for (int i = 0; i < pixelCount; i++)
            {
                int idx = i * 4;
                float red = imageDataset.ImageData[idx + _redBandIndex] / 255f;
                float nir = imageDataset.ImageData[idx + _nirBandIndex] / 255f;
                float blue = imageDataset.ImageData[idx + _blueBandIndex] / 255f;
                float denominator = nir + C1 * red - C2 * blue + L;
                eviData[i] = Math.Abs(denominator) > 0.001f ? G * ((nir - red) / denominator) : 0;
            }
            imageDataset.ImageMetadata["EVI_Min"] = eviData.Min();
            imageDataset.ImageMetadata["EVI_Max"] = eviData.Max();
            imageDataset.ImageMetadata["EVI_Mean"] = eviData.Average();
            Logger.Log($"EVI calculation complete. Range: [{eviData.Min():F3}, {eviData.Max():F3}]");
        }

        private void PerformUnsupervisedClassification(ImageDataset imageDataset, int numClasses)
        {
            if (imageDataset.ImageData == null) { imageDataset.Load(); }
            int pixelCount = imageDataset.Width * imageDataset.Height;
            List<Vector3> pixels = new List<Vector3>(pixelCount);
            for (int i = 0; i < pixelCount; i++)
            {
                int idx = i * 4;
                pixels.Add(new Vector3(imageDataset.ImageData[idx], imageDataset.ImageData[idx + 1], imageDataset.ImageData[idx + 2]));
            }
            var (labels, centers, iterations, convergence) = KMeansClustering(pixels, numClasses);
            _classificationResult = new ClassificationResult { NumClasses = numClasses, Labels = labels, ClassCenters = centers, Iterations = iterations, Convergence = convergence };
            CreateClassifiedImage(imageDataset, labels, centers);
            CalculateClassStatistics(imageDataset, labels, numClasses);
            Logger.Log($"Unsupervised classification complete with {numClasses} classes in {iterations} iterations");
        }

        private (int[] labels, Vector3[] centers, int iterations, float convergence) KMeansClustering(List<Vector3> data, int k)
        {
            Random rand = new Random();
            int n = data.Count;
            int[] labels = new int[n];
            Vector3[] centers = new Vector3[k];
            HashSet<int> selectedIndices = new HashSet<int>();
            for (int i = 0; i < k; i++)
            {
                int idx;
                do { idx = rand.Next(n); } while (selectedIndices.Contains(idx));
                selectedIndices.Add(idx);
                centers[i] = data[idx];
            }
            int iteration = 0;
            float previousError = float.MaxValue;
            float convergence = 1.0f;
            while (iteration < _maxIterations && convergence > _convergenceThreshold)
            {
                for (int i = 0; i < n; i++)
                {
                    float minDist = float.MaxValue;
                    for (int j = 0; j < k; j++)
                    {
                        float dist = Vector3.DistanceSquared(data[i], centers[j]);
                        if (dist < minDist) { minDist = dist; labels[i] = j; }
                    }
                }
                Vector3[] newCenters = new Vector3[k];
                int[] counts = new int[k];
                for (int i = 0; i < n; i++)
                {
                    newCenters[labels[i]] += data[i];
                    counts[labels[i]]++;
                }
                for (int j = 0; j < k; j++)
                {
                    if (counts[j] > 0) { newCenters[j] /= counts[j]; }
                    else { newCenters[j] = data[rand.Next(n)]; }
                }
                float currentError = 0;
                for (int i = 0; i < n; i++) { currentError += Vector3.DistanceSquared(data[i], newCenters[labels[i]]); }
                convergence = Math.Abs(previousError - currentError) / previousError;
                previousError = currentError;
                centers = newCenters;
                iteration++;
            }
            return (labels, centers, iteration, convergence);
        }

        private byte[] ConvertToGrayscale(byte[] imageData, int width, int height)
        {
            byte[] grayscale = new byte[width * height];
            for (int i = 0; i < width * height; i++)
            {
                int idx = i * 4;
                grayscale[i] = (byte)(0.299f * imageData[idx] + 0.587f * imageData[idx + 1] + 0.114f * imageData[idx + 2]);
            }
            return grayscale;
        }

        private byte[] ApplyThreshold(byte[] grayscale, float threshold)
        {
            byte[] binary = new byte[grayscale.Length];
            for (int i = 0; i < grayscale.Length; i++) { binary[i] = (byte)(grayscale[i] > threshold ? 255 : 0); }
            return binary;
        }

        private byte[] ApplyAdaptiveThreshold(byte[] grayscale, int width, int height)
        {
            byte[] binary = new byte[grayscale.Length];
            int windowSize = 15;
            float k = 0.1f;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    float sum = 0;
                    int count = 0;
                    for (int dy = -windowSize / 2; dy <= windowSize / 2; dy++)
                    {
                        for (int dx = -windowSize / 2; dx <= windowSize / 2; dx++)
                        {
                            int nx = x + dx;
                            int ny = y + dy;
                            if (nx >= 0 && nx < width && ny >= 0 && ny < height) { sum += grayscale[ny * width + nx]; count++; }
                        }
                    }
                    float localMean = sum / count;
                    float threshold = localMean * (1 - k);
                    binary[idx] = (byte)(grayscale[idx] > threshold ? 255 : 0);
                }
            }
            return binary;
        }

        private List<List<int>> FindConnectedComponents(byte[] binary, int width, int height)
        {
            List<List<int>> components = new List<List<int>>();
            bool[] visited = new bool[binary.Length];
            for (int i = 0; i < binary.Length; i++)
            {
                if (binary[i] == 255 && !visited[i])
                {
                    List<int> component = new List<int>();
                    Stack<int> stack = new Stack<int>();
                    stack.Push(i);
                    while (stack.Count > 0)
                    {
                        int pixel = stack.Pop();
                        if (visited[pixel]) continue;
                        visited[pixel] = true;
                        component.Add(pixel);
                        int x = pixel % width;
                        int y = pixel / width;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                int nx = x + dx;
                                int ny = y + dy;
                                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                                {
                                    int nidx = ny * width + nx;
                                    if (binary[nidx] == 255 && !visited[nidx]) { stack.Push(nidx); }
                                }
                            }
                        }
                    }
                    components.Add(component);
                }
            }
            return components;
        }

        private void CreateNDVIVisualization(ImageDataset imageDataset, float[] ndviData)
        {
            byte[] visualData = new byte[imageDataset.Width * imageDataset.Height * 4];
            for (int i = 0; i < ndviData.Length; i++)
            {
                int idx = i * 4;
                float ndvi = ndviData[i];
                if (ndvi < 0) { visualData[idx] = 0; visualData[idx + 1] = 0; visualData[idx + 2] = (byte)(128 + ndvi * 127); }
                else { float normalized = Math.Min(1.0f, ndvi); visualData[idx] = (byte)((1 - normalized) * 255); visualData[idx + 1] = (byte)(normalized * 255); visualData[idx + 2] = 0; }
                visualData[idx + 3] = 255;
            }
            imageDataset.ImageMetadata["NDVI_Visualization"] = visualData;
        }

        private void CreateClassifiedImage(ImageDataset imageDataset, int[] labels, Vector3[] centers)
        {
            byte[] classifiedData = new byte[imageDataset.Width * imageDataset.Height * 4];
            Vector3[] classColors = GenerateDistinctColors(centers.Length);
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i * 4;
                Vector3 color = classColors[labels[i]];
                classifiedData[idx] = (byte)color.X;
                classifiedData[idx + 1] = (byte)color.Y;
                classifiedData[idx + 2] = (byte)color.Z;
                classifiedData[idx + 3] = 255;
            }
            imageDataset.ImageMetadata["Classification_Result"] = classifiedData;
        }

        private Vector3[] GenerateDistinctColors(int count)
        {
            Vector3[] colors = new Vector3[count];
            float hueStep = 360f / count;
            for (int i = 0; i < count; i++) { colors[i] = HsvToRgb(i * hueStep, 0.8f, 0.9f); }
            return colors;
        }

        private Vector3 HsvToRgb(float h, float s, float v)
        {
            h /= 60f;
            float c = v * s;
            float x = c * (1 - Math.Abs(h % 2 - 1));
            float m = v - c;
            Vector3 rgb;
            if (h < 1) rgb = new Vector3(c, x, 0);
            else if (h < 2) rgb = new Vector3(x, c, 0);
            else if (h < 3) rgb = new Vector3(0, c, x);
            else if (h < 4) rgb = new Vector3(0, x, c);
            else if (h < 5) rgb = new Vector3(x, 0, c);
            else rgb = new Vector3(c, 0, x);
            return (rgb + new Vector3(m, m, m)) * 255;
        }

        private void CalculateClassStatistics(ImageDataset imageDataset, int[] labels, int numClasses)
        {
            int[] classCounts = new int[numClasses];
            foreach (int label in labels) { classCounts[label]++; }
            for (int i = 0; i < numClasses; i++)
            {
                float percentage = classCounts[i] * 100f / labels.Length;
                imageDataset.ImageMetadata[$"Class_{i}_Count"] = classCounts[i];
                imageDataset.ImageMetadata[$"Class_{i}_Percentage"] = percentage;
            }
        }

        private void ExportParticleAnalysisResults(ImageDataset imageDataset)
        {
            string outputPath = Path.ChangeExtension(imageDataset.FilePath, ".particles.csv");
            using (StreamWriter writer = new StreamWriter(outputPath))
            {
                writer.WriteLine("ID,Area,Diameter,Circularity,AspectRatio,CenterX,CenterY");
                foreach (var p in _detectedParticles)
                {
                    writer.WriteLine($"{p.Id},{p.Area},{p.EquivalentDiameter},{p.Circularity},{p.AspectRatio},{p.CenterX},{p.CenterY}");
                }
            }
            Logger.Log($"Exported particle analysis results to {outputPath}");
        }

        private readonly ImGuiExportFileDialog _exportClassDialog = new ImGuiExportFileDialog("ExportClassification", "Export Classification");
        private bool _exportClassDialogActive = false;

        private void ExportPointCountingResults(ImageDataset imageDataset)
        {
            string outputPath = Path.ChangeExtension(imageDataset.FilePath, ".pointcount.csv");
            using (StreamWriter writer = new StreamWriter(outputPath))
            {
                writer.WriteLine("Mineral,Count,Percentage");
                int total = _mineralCounts.Values.Sum();
                foreach (var kvp in _mineralCounts.OrderByDescending(m => m.Value))
                {
                    float percentage = total > 0 ? (kvp.Value * 100f / total) : 0;
                    writer.WriteLine($"{kvp.Key},{kvp.Value},{percentage:F2}");
                }
            }
            Logger.Log($"Exported point counting results to {outputPath}");
        }

        private void ExportClassificationResult(ImageDataset imageDataset, ClassificationResult result)
        {
            if (!_exportClassDialogActive)
            {
                _exportClassDialog.SetExtensions((".tiff", "TIFF (RGBA)"), (".png", "PNG"), (".jpg", "JPEG"));
                string defaultBase = Path.GetFileNameWithoutExtension(imageDataset.FilePath) + ".classification";
                string startDir = string.IsNullOrEmpty(imageDataset.FilePath) ? Directory.GetCurrentDirectory() : Path.GetDirectoryName(imageDataset.FilePath);
                _exportClassDialog.Open(defaultBase, startDir);
                _exportClassDialogActive = true;
            }

            if (!_exportClassDialog.Submit()) return;

            try
            {
                string imagePath = _exportClassDialog.SelectedPath;
                if (!imageDataset.ImageMetadata.TryGetValue("Classification_Result", out var raw) || raw is not byte[] rgba)
                {
                    Logger.LogError("Classification result image not found in metadata (key 'Classification_Result').");
                    return;
                }
                SaveRgbaImage(imagePath, rgba, imageDataset.Width, imageDataset.Height);
                string csvPath = Path.ChangeExtension(imagePath, ".csv");
                using (var writer = new StreamWriter(csvPath))
                {
                    writer.WriteLine("Class,Count,Percentage,CenterR,CenterG,CenterB");
                    for (int i = 0; i < result.NumClasses; i++)
                    {
                        int count = Convert.ToInt32(imageDataset.ImageMetadata[$"Class_{i}_Count"]);
                        float percentage = Convert.ToSingle(imageDataset.ImageMetadata[$"Class_{i}_Percentage"]);
                        var center = result.ClassCenters[i];
                        writer.WriteLine($"{i},{count},{percentage:F2},{center.X:F1},{center.Y:F1},{center.Z:F1}");
                    }
                }
                Logger.Log($"Exported classification image to {imagePath}");
                Logger.Log($"Exported classification stats to {csvPath}");
            }
            catch (Exception ex) { Logger.LogError($"Export failed: {ex.Message}"); }
            finally { _exportClassDialogActive = false; }
        }

        private void SaveRgbaImage(string path, byte[] rgba, int width, int height)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".tif" || ext == ".tiff")
            {
                int rowBytes = width * 4;
                using (var tiff = Tiff.Open(path, "w"))
                {
                    if (tiff == null) throw new IOException($"Could not open TIFF for writing: {path}");
                    tiff.SetField(TiffTag.IMAGEWIDTH, width);
                    tiff.SetField(TiffTag.IMAGELENGTH, height);
                    tiff.SetField(TiffTag.SAMPLESPERPIXEL, 4);
                    tiff.SetField(TiffTag.BITSPERSAMPLE, 8);
                    tiff.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
                    tiff.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
                    tiff.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);
                    tiff.SetField(TiffTag.EXTRASAMPLES, 1, new short[] { (short)ExtraSample.ASSOCALPHA });
                    var scanline = new byte[rowBytes];
                    for (int y = 0; y < height; y++)
                    {
                        Buffer.BlockCopy(rgba, y * rowBytes, scanline, 0, rowBytes);
                        tiff.WriteScanline(scanline, y);
                    }
                }
                return;
            }

            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var bmp = new SKBitmap(info);
            Marshal.Copy(rgba, 0, bmp.GetPixels(), rgba.Length);
            using var img = SKImage.FromBitmap(bmp);
            var fmt = ext switch { ".png" => SKEncodedImageFormat.Png, ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg, ".bmp" => SKEncodedImageFormat.Bmp, _ => SKEncodedImageFormat.Png };
            if (fmt == SKEncodedImageFormat.Png && ext != ".png") path = Path.ChangeExtension(path, ".png");
            using var fs = File.Open(path, FileMode.Create, FileAccess.Write);
            img.Encode(fmt, 95).SaveTo(fs);
        }

        public ImageSegmentationToolsUI GetSegmentationTools() => _segmentationTools;
    }

    public class ParticleData
    {
        public int Id { get; set; }
        public float Area { get; set; }
        public float EquivalentDiameter { get; set; }
        public float Circularity { get; set; }
        public float AspectRatio { get; set; }
        public float CenterX { get; set; }
        public float CenterY { get; set; }
    }

    public class PointCountData
    {
        public int GridX { get; set; }
        public int GridY { get; set; }
        public string Mineral { get; set; }
        public Vector2 Position { get; set; }
    }

    public class ClassificationResult
    {
        public int NumClasses { get; set; }
        public int[] Labels { get; set; }
        public Vector3[] ClassCenters { get; set; }
        public int Iterations { get; set; }
        public float Convergence { get; set; }
    }

    public class TagManager
    {
        public void SuggestTags(ImageDataset dataset)
        {
            string filename = dataset.Name.ToLower();
            if (filename.Contains("sem")) dataset.AddTag(ImageTag.SEM);
            if (filename.Contains("tem")) dataset.AddTag(ImageTag.TEM);
            if (filename.Contains("thin") || filename.Contains("section")) dataset.AddTag(ImageTag.ThinSection);
            if (filename.Contains("drone") || filename.Contains("uav")) dataset.AddTag(ImageTag.Drone);
            if (filename.Contains("map")) dataset.AddTag(ImageTag.Map);
            if (filename.Contains("ct")) dataset.AddTag(ImageTag.CTSlice);
            if (filename.Contains("satellite") || filename.Contains("landsat") || filename.Contains("sentinel"))
                dataset.AddTag(ImageTag.Satellite);
            if (filename.Contains("core")) dataset.AddTag(ImageTag.CorePhoto);
            if (filename.Contains("outcrop")) dataset.AddTag(ImageTag.OutcropPhoto);
            if (dataset.PixelSize > 0) dataset.AddTag(ImageTag.Calibrated);
            if (dataset.BitDepth > 24 || filename.Contains("multi") || filename.Contains("spectral"))
                dataset.AddTag(ImageTag.Multispectral);
        }
    }
}