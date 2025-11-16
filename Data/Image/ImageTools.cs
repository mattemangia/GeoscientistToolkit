// GeoscientistToolkit/Data/Image/ImageTools.cs (Corrected)

using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using BitMiracle.LibTiff.Classic;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.Image.AISegmentation;
using GeoscientistToolkit.Data.Image.Segmentation;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.GIS.Tools;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using SkiaSharp;

namespace GeoscientistToolkit.Data.Image;

public class ImageTools : IDatasetTools
{
    // Store reference to the current dataset's viewer (if any)
    private static readonly Dictionary<ImageDataset, ImageViewer> _datasetViewers = new();
    private readonly Dictionary<ToolCategory, string> _categoryDescriptions;

    private readonly Dictionary<ToolCategory, string> _categoryNames;
    private readonly Dictionary<ToolCategory, List<ToolEntry>> _toolsByCategory;

    private ToolCategory _selectedCategory = ToolCategory.Properties;
    private int _selectedToolIndex;

    public ImageTools()
    {
        _categoryNames = new Dictionary<ToolCategory, string>
        {
            { ToolCategory.Properties, "Properties" },
            { ToolCategory.Adjustments, "Adjustments" },
            { ToolCategory.Analysis, "Analysis" },
            { ToolCategory.Editing, "Editing" },
            { ToolCategory.AI, "AI Tools" },
            { ToolCategory.Spatial, "Spatial & Remote Sensing" }
        };

        _categoryDescriptions = new Dictionary<ToolCategory, string>
        {
            { ToolCategory.Properties, "Manage descriptive tags and metadata." },
            { ToolCategory.Adjustments, "Apply basic color and brightness adjustments." },
            { ToolCategory.Analysis, "Perform segmentation, particle analysis, and point counting." },
            { ToolCategory.Editing, "Layers, drawing tools, selections, and image manipulation with SAM integration." },
            { ToolCategory.AI, "SAM-powered AI tools for image matting, object extraction, and smart cutouts." },
            { ToolCategory.Spatial, "Georeference images and perform remote sensing analysis." }
        };

        _toolsByCategory = new Dictionary<ToolCategory, List<ToolEntry>>
        {
            {
                ToolCategory.Properties, new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Tag Manager", Description = "Assign categorical tags to enable specialized tools.",
                        Tool = new TagManagerTool()
                    }
                }
            },
            {
                ToolCategory.Adjustments, new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Adjustments", Description = "Adjust brightness and contrast of the image.",
                        Tool = new AdjustmentsTool()
                    }
                }
            },
            {
                ToolCategory.Analysis, new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Segmentation", Description = "Define and paint regions of interest.",
                        Tool = new SegmentationTool()
                    },
                    new()
                    {
                        Name = "Particle Analysis", Description = "Automated particle detection for SEM/TEM images.",
                        Tool = new ParticleAnalysisTool()
                    },
                    new()
                    {
                        Name = "Point Counting", Description = "Modal analysis tool for thin sections.",
                        Tool = new PointCountingTool()
                    }
                }
            },
            {
                ToolCategory.Editing, new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Layers & Drawing", Description = "Layer management, drawing tools, selections, and transformations with SAM integration.",
                        Tool = new ImageLayerToolsUI()
                    }
                }
            },
            {
                ToolCategory.AI, new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Image Matting", Description = "Extract foreground/background with SAM-powered transparency.",
                        Tool = new ImageMattingTool()
                    },
                    new()
                    {
                        Name = "Object Extractor", Description = "Extract multiple objects as separate transparent images.",
                        Tool = new ImageObjectExtractorTool()
                    },
                    new()
                    {
                        Name = "Smart Cutout", Description = "One-click object isolation and cutout tool.",
                        Tool = new ImageSmartCutoutTool()
                    },
                    new()
                    {
                        Name = "Batch Processor", Description = "Process multiple images with SAM operations.",
                        Tool = new ImageBatchProcessorTool()
                    }
                }
            },
            {
                ToolCategory.Spatial, new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Georeferencing", Description = "Assign real-world coordinates to an image.",
                        Tool = new GeoreferencingTool()
                    },
                    new()
                    {
                        Name = "Remote Sensing",
                        Description = "Calculate vegetation indices and perform classification.",
                        Tool = new RemoteSensingTool()
                    }
                }
            }
        };
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not ImageDataset imageDataset) return;

        ConnectToViewer(imageDataset);

        DrawCompactUI(imageDataset);
    }

    private void DrawCompactUI(ImageDataset imageDataset)
    {
        // Compact category selector as dropdown
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8, 4));
        ImGui.Text("Category:");
        ImGui.SameLine();

        var currentCategoryName = _categoryNames[_selectedCategory];
        var categoryTools = _toolsByCategory[_selectedCategory];
        var preview = $"{currentCategoryName} ({categoryTools.Count})";

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.BeginCombo("##CategorySelector", preview))
        {
            foreach (var category in Enum.GetValues<ToolCategory>())
            {
                var tools = _toolsByCategory[category];
                if (tools.Count == 0) continue;

                var isSelected = _selectedCategory == category;
                var label = $"{_categoryNames[category]} ({tools.Count} tools)";

                if (ImGui.Selectable(label, isSelected))
                {
                    _selectedCategory = category;
                    _selectedToolIndex = 0;
                }

                if (ImGui.IsItemHovered()) ImGui.SetTooltip(_categoryDescriptions[category]);
            }

            ImGui.EndCombo();
        }

        ImGui.PopStyleVar();

        // Category description
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), _categoryDescriptions[_selectedCategory]);
        ImGui.Separator();
        ImGui.Spacing();

        // Tools in selected category as tabs
        if (categoryTools.Count == 0)
        {
            ImGui.TextDisabled("No tools available in this category.");
        }
        else if (ImGui.BeginTabBar($"Tools_{_selectedCategory}", ImGuiTabBarFlags.None))
        {
            for (var i = 0; i < categoryTools.Count; i++)
            {
                var entry = categoryTools[i];
                if (ImGui.BeginTabItem(entry.Name))
                {
                    _selectedToolIndex = i;

                    // Filter tools based on tags
                    var toolEnabled = IsToolEnabledForDataset(entry.Name, imageDataset);

                    if (!toolEnabled)
                    {
                        ImGui.TextColored(new Vector4(1, 1, 0, 1), "This tool requires specific image tags.");
                        ImGui.TextDisabled($"Required: {GetRequiredTagsForTool(entry.Name)}");
                    }
                    else
                    {
                        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), entry.Description);
                        ImGui.Separator();
                        ImGui.Spacing();

                        ImGui.BeginChild($"ToolContent_{entry.Name}", new Vector2(0, 0), ImGuiChildFlags.None,
                            ImGuiWindowFlags.HorizontalScrollbar);
                        entry.Tool.Draw(imageDataset);
                        ImGui.EndChild();
                    }

                    ImGui.EndTabItem();
                }
            }

            ImGui.EndTabBar();
        }
    }

    private bool IsToolEnabledForDataset(string toolName, ImageDataset dataset)
    {
        return toolName switch
        {
            "Particle Analysis" => dataset.HasTag(ImageTag.SEM) || dataset.HasTag(ImageTag.TEM),
            "Point Counting" => dataset.HasTag(ImageTag.ThinSection),
            "Remote Sensing" => dataset.HasTag(ImageTag.Drone) || dataset.HasTag(ImageTag.Satellite),
            _ => true // Default to enabled for other tools
        };
    }

    private string GetRequiredTagsForTool(string toolName)
    {
        return toolName switch
        {
            "Particle Analysis" => "SEM or TEM",
            "Point Counting" => "ThinSection",
            "Remote Sensing" => "Drone or Satellite",
            _ => "None"
        };
    }

    public IDatasetTools GetCurrentActiveTool()
    {
        if (_toolsByCategory.TryGetValue(_selectedCategory, out var tools))
            if (_selectedToolIndex >= 0 && _selectedToolIndex < tools.Count)
                return tools[_selectedToolIndex].Tool;

        return null;
    }

    private void ConnectToViewer(ImageDataset imageDataset)
    {
        if (_datasetViewers.ContainsKey(imageDataset)) return;

        foreach (var panel in BasePanel.AllPanels)
            if (panel is DatasetViewPanel dvp && dvp.Dataset == imageDataset)
            {
                var viewerField = dvp.GetType().GetField("_viewer", BindingFlags.NonPublic | BindingFlags.Instance);
                if (viewerField != null)
                    if (viewerField.GetValue(dvp) is ImageViewer viewer)
                    {
                        var segTool =
                            _toolsByCategory[ToolCategory.Analysis].FirstOrDefault(t => t.Tool is SegmentationTool)
                                ?.Tool as SegmentationTool;
                        if (segTool != null)
                        {
                            viewer.SetSegmentationTools(segTool.GetSegmentationTools());
                            segTool.GetSegmentationTools().SetInvalidateCallback(viewer.InvalidateSegmentationTexture);
                        }

                        viewer.SetImageToolsController(this);
                        _datasetViewers[imageDataset] = viewer;
                    }
            }
    }

    // --- TOOL CATEGORIES & DEFINITIONS ---
    private enum ToolCategory
    {
        Properties,
        Adjustments,
        Analysis,
        Editing,
        AI,
        Spatial
    }

    private class ToolEntry
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public IDatasetTools Tool { get; set; }
    }

    // --- NESTED TOOL CLASSES ---

    #region Tool Implementations

    private class TagManagerTool : IDatasetTools
    {
        public void Draw(Dataset dataset)
        {
            if (dataset is not ImageDataset imageDataset) return;

            ImGui.Text("Current Tags:");
            var currentTags = imageDataset.Tags.GetFlags().ToList();
            if (currentTags.Count == 0)
                ImGui.TextDisabled("No tags assigned");
            else
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
            var hasTag = dataset.HasTag(tag);
            if (ImGui.Checkbox(tag.GetDisplayName(), ref hasTag))
            {
                if (hasTag)
                    dataset.AddTag(tag);
                else
                    dataset.RemoveTag(tag);
                ProjectManager.Instance.HasUnsavedChanges = true;
            }
        }
    }

    private class AdjustmentsTool : IDatasetTools
    {
        private ImageDataset _activeAdjustmentDataset;
        private float _brightness;
        private float _contrast = 1;
        private byte[] _originalImageData;

        public void Draw(Dataset dataset)
        {
            if (dataset is not ImageDataset imageDataset) return;

            if (_activeAdjustmentDataset != imageDataset)
            {
                _activeAdjustmentDataset = imageDataset;
                _activeAdjustmentDataset.Load();
                if (_activeAdjustmentDataset.ImageData != null)
                    _originalImageData = (byte[])_activeAdjustmentDataset.ImageData.Clone();
                else
                    _originalImageData = null;
                _brightness = 0;
                _contrast = 1;
            }

            var valueChanged = false;
            valueChanged |= ImGui.SliderFloat("Brightness", ref _brightness, -1.0f, 1.0f);
            valueChanged |= ImGui.SliderFloat("Contrast", ref _contrast, 0.0f, 2.0f);

            if (valueChanged) ApplyImageAdjustments(imageDataset);

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
            var destinationData = imageDataset.ImageData;
            if (destinationData == null) return;

            for (var i = 0; i < _originalImageData.Length; i += 4)
            {
                for (var c = 0; c < 3; c++)
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
    }

    private class SegmentationTool : IDatasetTools
    {
        private readonly ImageSegmentationToolsUI _segmentationTools = new();

        public void Draw(Dataset dataset)
        {
            if (dataset is ImageDataset imageDataset) _segmentationTools.Draw(imageDataset);
        }

        public ImageSegmentationToolsUI GetSegmentationTools()
        {
            return _segmentationTools;
        }
    }

    public class PointCountingTool : IDatasetTools
    {
        private readonly List<PointCountData> _countedPoints = new();
        private readonly Dictionary<string, int> _mineralCounts = new();

        private readonly List<string> _mineralTypes = new()
        {
            "Quartz", "Feldspar", "Biotite", "Muscovite", "Olivine",
            "Pyroxene", "Amphibole", "Garnet", "Calcite", "Unknown"
        };

        private bool _countingActive;
        private int _gridSize = 100;
        private string _selectedMineral = "Quartz";
        private bool _showGrid = true;

        public void Draw(Dataset dataset)
        {
            if (dataset is not ImageDataset imageDataset) return;

            ImGui.TextWrapped("Modal analysis tool for thin sections.");

            ImGui.Checkbox("Show Grid", ref _showGrid);
            ImGui.SliderInt("Grid Size", ref _gridSize, 50, 500);

            if (ImGui.BeginCombo("Current Mineral", _selectedMineral))
            {
                foreach (var mineral in _mineralTypes)
                    if (ImGui.Selectable(mineral, mineral == _selectedMineral))
                        _selectedMineral = mineral;
                ImGui.EndCombo();
            }

            if (!_countingActive)
            {
                if (ImGui.Button("Start Counting", new Vector2(-1, 0)))
                {
                    Logger.Log($"Point counting started for {imageDataset.Name}");
                    StartPointCounting();
                }
            }
            else
            {
                if (ImGui.Button("Stop Counting", new Vector2(-1, 0)))
                {
                    _countingActive = false;
                    Logger.Log("Point counting stopped");
                }

                ImGui.Text($"Total Points: {_countedPoints.Count.ToString()}");
            }

            ImGui.Separator();
            ImGui.Text("Mineral Counts:");

            if (_mineralCounts.Count > 0)
            {
                var totalPoints = _mineralCounts.Values.Sum();
                if (ImGui.BeginTable("MineralTable", 3))
                {
                    ImGui.TableSetupColumn("Mineral");
                    ImGui.TableSetupColumn("Count");
                    ImGui.TableSetupColumn("Percentage");
                    ImGui.TableHeadersRow();
                    foreach (var kvp in _mineralCounts.OrderByDescending(m => m.Value))
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Text(kvp.Key);
                        ImGui.TableNextColumn();
                        ImGui.Text($"{kvp.Value.ToString()}");
                        ImGui.TableNextColumn();
                        var percentage = totalPoints > 0 ? kvp.Value * 100f / totalPoints : 0;
                        ImGui.Text($"{percentage.ToString("F1")}%");
                    }

                    ImGui.EndTable();
                }

                if (ImGui.Button("Export Results")) ExportPointCountingResults(imageDataset);
            }
            else
            {
                ImGui.TextDisabled("Click 'Start Counting' to begin");
            }
        }

        private void StartPointCounting()
        {
            _countingActive = true;
            _mineralCounts.Clear();
            _countedPoints.Clear();
            foreach (var mineral in _mineralTypes) _mineralCounts[mineral] = 0;
        }

        public void HandlePointCountClick(Vector2 clickPosition)
        {
            if (!_countingActive) return;
            var gridX = (int)(clickPosition.X / _gridSize);
            var gridY = (int)(clickPosition.Y / _gridSize);
            var existingPoint = _countedPoints.FirstOrDefault(p => p.GridX == gridX && p.GridY == gridY);
            if (existingPoint != null)
            {
                _mineralCounts[existingPoint.Mineral]--;
                existingPoint.Mineral = _selectedMineral;
                _mineralCounts[_selectedMineral]++;
            }
            else
            {
                _countedPoints.Add(new PointCountData
                    { GridX = gridX, GridY = gridY, Mineral = _selectedMineral, Position = clickPosition });
                _mineralCounts[_selectedMineral]++;
            }
        }

        private void ExportPointCountingResults(ImageDataset imageDataset)
        {
            var outputPath = Path.ChangeExtension(imageDataset.FilePath, ".pointcount.csv");
            using (var writer = new StreamWriter(outputPath))
            {
                writer.WriteLine("Mineral,Count,Percentage");
                var total = _mineralCounts.Values.Sum();
                foreach (var kvp in _mineralCounts.OrderByDescending(m => m.Value))
                {
                    var percentage = total > 0 ? kvp.Value * 100f / total : 0;
                    writer.WriteLine($"{kvp.Key},{kvp.Value},{percentage.ToString("F2")}");
                }
            }

            Logger.Log($"Exported point counting results to {outputPath}");
        }
    }

    private class GeoreferencingTool : IDatasetTools
    {
        private readonly GeoreferenceTool _georeferenceTool = new();

        public void Draw(Dataset dataset)
        {
            _georeferenceTool.Draw(dataset);
        }
    }

    private class ParticleAnalysisTool : IDatasetTools
    {
        private readonly List<ParticleData> _detectedParticles = new();
        private float _circularityThreshold = 0.5f;
        private float _particleMaxSize = 1000;

        private float _particleMinSize = 10;

        // Particle analysis state variables
        private float _thresholdValue = 128;
        private bool _useAdaptiveThreshold;

        public void Draw(Dataset dataset)
        {
            if (dataset is not ImageDataset imageDataset) return;

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
                ImGui.Text($"Detected Particles: {_detectedParticles.Count.ToString()}");

                var avgArea = _detectedParticles.Average(p => (double)p.Area);
                var avgCircularity = _detectedParticles.Average(p => (double)p.Circularity);
                var avgDiameter = _detectedParticles.Average(p => (double)p.EquivalentDiameter);

                ImGui.Text($"Average Area: {avgArea.ToString("F2")} pixels²");
                ImGui.Text($"Average Diameter: {avgDiameter.ToString("F2")} pixels");
                ImGui.Text($"Average Circularity: {avgCircularity.ToString("F3")}");

                if (ImGui.CollapsingHeader("Particle Details"))
                    if (ImGui.BeginTable("ParticleTable", 5))
                    {
                        ImGui.TableSetupColumn("ID");
                        ImGui.TableSetupColumn("Area");
                        ImGui.TableSetupColumn("Diameter");
                        ImGui.TableSetupColumn("Circularity");
                        ImGui.TableSetupColumn("Center");
                        ImGui.TableHeadersRow();

                        for (var i = 0; i < Math.Min(_detectedParticles.Count, 100); i++)
                        {
                            var p = _detectedParticles[i];
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGui.Text($"{p.Id.ToString()}");
                            ImGui.TableNextColumn();
                            ImGui.Text($"{p.Area.ToString("F1")}");
                            ImGui.TableNextColumn();
                            ImGui.Text($"{p.EquivalentDiameter.ToString("F1")}");
                            ImGui.TableNextColumn();
                            ImGui.Text($"{p.Circularity.ToString("F3")}");
                            ImGui.TableNextColumn();
                            ImGui.Text($"({p.CenterX.ToString("F0")},{p.CenterY.ToString("F0")})");
                        }

                        ImGui.EndTable();
                    }

                if (ImGui.Button("Export Results")) ExportParticleAnalysisResults(imageDataset);
            }
            else
            {
                ImGui.TextDisabled("No particles detected. Click 'Detect Particles' to start analysis.");
            }
        }

        private void PerformParticleAnalysis(ImageDataset imageDataset)
        {
            if (imageDataset.ImageData == null) imageDataset.Load();
            _detectedParticles.Clear();
            var grayscale = ConvertToGrayscale(imageDataset.ImageData, imageDataset.Width, imageDataset.Height);
            var binary = _useAdaptiveThreshold
                ? ApplyAdaptiveThreshold(grayscale, imageDataset.Width, imageDataset.Height)
                : ApplyThreshold(grayscale, _thresholdValue);
            var components = FindConnectedComponents(binary, imageDataset.Width, imageDataset.Height);
            var particleId = 1;
            foreach (var component in components)
            {
                float area = component.Count;
                if (area < _particleMinSize || area > _particleMaxSize) continue;
                var particle = AnalyzeParticle(component, particleId++, imageDataset.Width);
                if (particle.Circularity < _circularityThreshold) continue;
                _detectedParticles.Add(particle);
            }

            Logger.Log($"Detected {_detectedParticles.Count.ToString()} particles");
        }

        private byte[] ConvertToGrayscale(byte[] imageData, int width, int height)
        {
            var grayscale = new byte[width * height];
            for (var i = 0; i < width * height; i++)
            {
                var idx = i * 4;
                grayscale[i] = (byte)(0.299f * imageData[idx] + 0.587f * imageData[idx + 1] +
                                      0.114f * imageData[idx + 2]);
            }

            return grayscale;
        }

        private byte[] ApplyThreshold(byte[] grayscale, float threshold)
        {
            var binary = new byte[grayscale.Length];
            for (var i = 0; i < grayscale.Length; i++) binary[i] = (byte)(grayscale[i] > threshold ? 255 : 0);
            return binary;
        }

        private byte[] ApplyAdaptiveThreshold(byte[] grayscale, int width, int height)
        {
            var binary = new byte[grayscale.Length];
            var windowSize = 15;
            var k = 0.1f;
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var idx = y * width + x;
                float sum = 0;
                var count = 0;
                for (var dy = -windowSize / 2; dy <= windowSize / 2; dy++)
                for (var dx = -windowSize / 2; dx <= windowSize / 2; dx++)
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                    {
                        sum += grayscale[ny * width + nx];
                        count++;
                    }
                }

                var localMean = sum / count;
                var threshold = localMean * (1 - k);
                binary[idx] = (byte)(grayscale[idx] > threshold ? 255 : 0);
            }

            return binary;
        }

        private List<List<int>> FindConnectedComponents(byte[] binary, int width, int height)
        {
            var components = new List<List<int>>();
            var visited = new bool[binary.Length];
            for (var i = 0; i < binary.Length; i++)
                if (binary[i] == 255 && !visited[i])
                {
                    var component = new List<int>();
                    var stack = new Stack<int>();
                    stack.Push(i);
                    while (stack.Count > 0)
                    {
                        var pixel = stack.Pop();
                        if (visited[pixel]) continue;
                        visited[pixel] = true;
                        component.Add(pixel);
                        var x = pixel % width;
                        var y = pixel / width;
                        for (var dy = -1; dy <= 1; dy++)
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            var nx = x + dx;
                            var ny = y + dy;
                            if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                            {
                                var nidx = ny * width + nx;
                                if (binary[nidx] == 255 && !visited[nidx]) stack.Push(nidx);
                            }
                        }
                    }

                    components.Add(component);
                }

            return components;
        }

        private ParticleData AnalyzeParticle(List<int> pixels, int id, int imageWidth)
        {
            var particle = new ParticleData { Id = id };
            float sumX = 0, sumY = 0;
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var pixel in pixels)
            {
                var x = pixel % imageWidth;
                var y = pixel / imageWidth;
                sumX += x;
                sumY += y;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }

            particle.Area = pixels.Count;
            particle.CenterX = sumX / pixels.Count;
            particle.CenterY = sumY / pixels.Count;
            particle.EquivalentDiameter = (float)Math.Sqrt(4 * particle.Area / Math.PI);
            var perimeter = CalculatePerimeter(pixels, imageWidth);
            particle.Circularity = (float)(4 * Math.PI * particle.Area / (perimeter * perimeter));
            particle.Circularity = Math.Min(1.0f, particle.Circularity);
            var width = maxX - minX + 1;
            var height = maxY - minY + 1;
            particle.AspectRatio = width / Math.Max(height, 1);
            return particle;
        }

        private float CalculatePerimeter(List<int> pixels, int width)
        {
            var pixelSet = new HashSet<int>(pixels);
            float perimeter = 0;
            foreach (var pixel in pixels)
            {
                var x = pixel % width;
                var y = pixel / width;
                var neighbors = 0;
                if (pixelSet.Contains(pixel - 1)) neighbors++;
                if (pixelSet.Contains(pixel + 1)) neighbors++;
                if (pixelSet.Contains(pixel - width)) neighbors++;
                if (pixelSet.Contains(pixel + width)) neighbors++;
                if (neighbors < 4) perimeter += 4 - neighbors;
            }

            return perimeter;
        }

        private void ExportParticleAnalysisResults(ImageDataset imageDataset)
        {
            var outputPath = Path.ChangeExtension(imageDataset.FilePath, ".particles.csv");
            using (var writer = new StreamWriter(outputPath))
            {
                writer.WriteLine("ID,Area,Diameter,Circularity,AspectRatio,CenterX,CenterY");
                foreach (var p in _detectedParticles)
                    writer.WriteLine(
                        $"{p.Id.ToString()},{p.Area.ToString()},{p.EquivalentDiameter.ToString()},{p.Circularity.ToString()},{p.AspectRatio.ToString()},{p.CenterX.ToString()},{p.CenterY.ToString()}");
            }

            Logger.Log($"Exported particle analysis results to {outputPath}");
        }
    }

    private class RemoteSensingTool : IDatasetTools
    {
        private readonly ImGuiExportFileDialog
            _exportClassDialog = new("ExportClassification", "Export Classification");

        private int _blueBandIndex = 2;
        private ClassificationResult _classificationResult;
        private float _convergenceThreshold = 0.01f;
        private bool _exportClassDialogActive;
        private float _maxIterations = 100;
        private int _nirBandIndex = 3;

        private int _numClasses = 5;

        // Remote sensing state variables
        private int _redBandIndex;

        public void Draw(Dataset dataset)
        {
            if (dataset is not ImageDataset imageDataset) return;

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
                    ImGui.Text("Classification Complete");
                    ImGui.Text($"Classes: {_classificationResult.NumClasses.ToString()}");
                    ImGui.Text($"Iterations: {_classificationResult.Iterations.ToString()}");
                    ImGui.Text($"Convergence: {_classificationResult.Convergence.ToString("F4")}");
                    if (ImGui.Button("Export Classification"))
                        ExportClassificationResult(imageDataset, _classificationResult);
                }
            }
        }

        private void CalculateNDVI(ImageDataset imageDataset)
        {
            if (!imageDataset.HasTag(ImageTag.Multispectral))
            {
                Logger.LogWarning("NDVI calculation requires multispectral imagery");
                return;
            }

            if (imageDataset.ImageData == null) imageDataset.Load();
            var pixelCount = imageDataset.Width * imageDataset.Height;
            var ndviData = new float[pixelCount];
            for (var i = 0; i < pixelCount; i++)
            {
                var idx = i * 4;
                float red = imageDataset.ImageData[idx + _redBandIndex];
                float nir = imageDataset.ImageData[idx + _nirBandIndex];
                var denominator = nir + red;
                ndviData[i] = denominator > 0 ? (nir - red) / denominator : 0;
            }

            imageDataset.ImageMetadata["NDVI_Min"] = ndviData.Min();
            imageDataset.ImageMetadata["NDVI_Max"] = ndviData.Max();
            imageDataset.ImageMetadata["NDVI_Mean"] = ndviData.Average();
            CreateNDVIVisualization(imageDataset, ndviData);
            Logger.Log(
                $"NDVI calculation complete. Range: [{ndviData.Min().ToString("F3")}, {ndviData.Max().ToString("F3")}]");
        }

        private void CalculateEVI(ImageDataset imageDataset)
        {
            if (!imageDataset.HasTag(ImageTag.Multispectral))
            {
                Logger.LogWarning("EVI calculation requires multispectral imagery");
                return;
            }

            if (imageDataset.ImageData == null) imageDataset.Load();
            var pixelCount = imageDataset.Width * imageDataset.Height;
            var eviData = new float[pixelCount];
            const float G = 2.5f, C1 = 6.0f, C2 = 7.5f, L = 1.0f;
            for (var i = 0; i < pixelCount; i++)
            {
                var idx = i * 4;
                var red = imageDataset.ImageData[idx + _redBandIndex] / 255f;
                var nir = imageDataset.ImageData[idx + _nirBandIndex] / 255f;
                var blue = imageDataset.ImageData[idx + _blueBandIndex] / 255f;
                var denominator = nir + C1 * red - C2 * blue + L;
                eviData[i] = Math.Abs(denominator) > 0.001f ? G * ((nir - red) / denominator) : 0;
            }

            imageDataset.ImageMetadata["EVI_Min"] = eviData.Min();
            imageDataset.ImageMetadata["EVI_Max"] = eviData.Max();
            imageDataset.ImageMetadata["EVI_Mean"] = eviData.Average();
            Logger.Log(
                $"EVI calculation complete. Range: [{eviData.Min().ToString("F3")}, {eviData.Max().ToString("F3")}]");
        }

        private void CreateNDVIVisualization(ImageDataset imageDataset, float[] ndviData)
        {
            var visualData = new byte[imageDataset.Width * imageDataset.Height * 4];
            for (var i = 0; i < ndviData.Length; i++)
            {
                var idx = i * 4;
                var ndvi = ndviData[i];
                if (ndvi < 0)
                {
                    visualData[idx] = 0;
                    visualData[idx + 1] = 0;
                    visualData[idx + 2] = (byte)(128 + ndvi * 127);
                }
                else
                {
                    var normalized = MathF.Min(1.0f, ndvi);
                    visualData[idx] = (byte)((1 - normalized) * 255);
                    visualData[idx + 1] = (byte)(normalized * 255);
                    visualData[idx + 2] = 0;
                }

                visualData[idx + 3] = 255;
            }

            imageDataset.ImageMetadata["NDVI_Visualization"] = visualData;
        }

        private void PerformUnsupervisedClassification(ImageDataset imageDataset, int numClasses)
        {
            if (imageDataset.ImageData == null) imageDataset.Load();
            var pixelCount = imageDataset.Width * imageDataset.Height;
            var pixels = new List<Vector3>(pixelCount);
            for (var i = 0; i < pixelCount; i++)
            {
                var idx = i * 4;
                pixels.Add(new Vector3(imageDataset.ImageData[idx], imageDataset.ImageData[idx + 1],
                    imageDataset.ImageData[idx + 2]));
            }

            var (labels, centers, iterations, convergence) = KMeansClustering(pixels, numClasses);
            _classificationResult = new ClassificationResult
            {
                NumClasses = numClasses, Labels = labels, ClassCenters = centers, Iterations = iterations,
                Convergence = convergence
            };
            CreateClassifiedImage(imageDataset, labels, centers);
            CalculateClassStatistics(imageDataset, labels, numClasses);
            Logger.Log(
                $"Unsupervised classification complete with {numClasses.ToString()} classes in {iterations.ToString()} iterations");
        }

        private (int[] labels, Vector3[] centers, int iterations, float convergence) KMeansClustering(
            List<Vector3> data, int k)
        {
            var rand = new Random();
            var n = data.Count;
            var labels = new int[n];
            var centers = new Vector3[k];
            var selectedIndices = new HashSet<int>();
            for (var i = 0; i < k; i++)
            {
                int idx;
                do
                {
                    idx = rand.Next(n);
                } while (selectedIndices.Contains(idx));

                selectedIndices.Add(idx);
                centers[i] = data[idx];
            }

            var iteration = 0;
            var previousError = float.MaxValue;
            var convergence = 1.0f;
            while (iteration < _maxIterations && convergence > _convergenceThreshold)
            {
                for (var i = 0; i < n; i++)
                {
                    var minDist = float.MaxValue;
                    for (var j = 0; j < k; j++)
                    {
                        var dist = Vector3.DistanceSquared(data[i], centers[j]);
                        if (dist < minDist)
                        {
                            minDist = dist;
                            labels[i] = j;
                        }
                    }
                }

                var newCenters = new Vector3[k];
                var counts = new int[k];
                for (var i = 0; i < n; i++)
                {
                    newCenters[labels[i]] += data[i];
                    counts[labels[i]]++;
                }

                for (var j = 0; j < k; j++)
                    if (counts[j] > 0) newCenters[j] /= counts[j];
                    else newCenters[j] = data[rand.Next(n)];

                float currentError = 0;
                for (var i = 0; i < n; i++) currentError += Vector3.DistanceSquared(data[i], newCenters[labels[i]]);
                convergence = Math.Abs(previousError - currentError) / previousError;
                previousError = currentError;
                centers = newCenters;
                iteration++;
            }

            return (labels, centers, iteration, convergence);
        }

        private void CreateClassifiedImage(ImageDataset imageDataset, int[] labels, Vector3[] centers)
        {
            var classifiedData = new byte[imageDataset.Width * imageDataset.Height * 4];
            var classColors = GenerateDistinctColors(centers.Length);
            for (var i = 0; i < labels.Length; i++)
            {
                var idx = i * 4;
                var color = classColors[labels[i]];
                classifiedData[idx] = (byte)color.X;
                classifiedData[idx + 1] = (byte)color.Y;
                classifiedData[idx + 2] = (byte)color.Z;
                classifiedData[idx + 3] = 255;
            }

            imageDataset.ImageMetadata["Classification_Result"] = classifiedData;
        }

        private Vector3[] GenerateDistinctColors(int count)
        {
            var colors = new Vector3[count];
            var hueStep = 360f / count;
            for (var i = 0; i < count; i++) colors[i] = HsvToRgb(i * hueStep, 0.8f, 0.9f);
            return colors;
        }

        private Vector3 HsvToRgb(float h, float s, float v)
        {
            h /= 60f;
            var c = v * s;
            var x = c * (1 - Math.Abs(h % 2 - 1));
            var m = v - c;
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
            var classCounts = new int[numClasses];
            foreach (var label in labels) classCounts[label]++;
            for (var i = 0; i < numClasses; i++)
            {
                var percentage = classCounts[i] * 100f / labels.Length;
                imageDataset.ImageMetadata[$"Class_{i}_Count"] = classCounts[i];
                imageDataset.ImageMetadata[$"Class_{i}_Percentage"] = percentage;
            }
        }

        private void ExportClassificationResult(ImageDataset imageDataset, ClassificationResult result)
        {
            if (!_exportClassDialogActive)
            {
                _exportClassDialog.SetExtensions((".tiff", "TIFF (RGBA)"), (".png", "PNG"), (".jpg", "JPEG"));
                var defaultBase = Path.GetFileNameWithoutExtension(imageDataset.FilePath) + ".classification";
                var startDir = string.IsNullOrEmpty(imageDataset.FilePath)
                    ? Directory.GetCurrentDirectory()
                    : Path.GetDirectoryName(imageDataset.FilePath);
                _exportClassDialog.Open(defaultBase, startDir);
                _exportClassDialogActive = true;
            }

            if (!_exportClassDialog.Submit()) return;

            try
            {
                var imagePath = _exportClassDialog.SelectedPath;
                if (!imageDataset.ImageMetadata.TryGetValue("Classification_Result", out var raw) ||
                    raw is not byte[] rgba)
                {
                    Logger.LogError("Classification result image not found in metadata (key 'Classification_Result').");
                    return;
                }

                SaveRgbaImage(imagePath, rgba, imageDataset.Width, imageDataset.Height);
                var csvPath = Path.ChangeExtension(imagePath, ".csv");
                using (var writer = new StreamWriter(csvPath))
                {
                    writer.WriteLine("Class,Count,Percentage,CenterR,CenterG,CenterB");
                    for (var i = 0; i < result.NumClasses; i++)
                    {
                        var count = Convert.ToInt32(imageDataset.ImageMetadata[$"Class_{i}_Count"]);
                        var percentage = Convert.ToSingle(imageDataset.ImageMetadata[$"Class_{i}_Percentage"]);
                        var center = result.ClassCenters[i];
                        writer.WriteLine(
                            $"{i.ToString()},{count.ToString()},{percentage.ToString("F2")},{center.X.ToString("F1")},{center.Y.ToString("F1")},{center.Z.ToString("F1")}");
                    }
                }

                Logger.Log($"Exported classification image to {imagePath}");
                Logger.Log($"Exported classification stats to {csvPath}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Export failed: {ex.Message}");
            }
            finally
            {
                _exportClassDialogActive = false;
            }
        }

        private void SaveRgbaImage(string path, byte[] rgba, int width, int height)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".tif" || ext == ".tiff")
            {
                var rowBytes = width * 4;
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
                    tiff.SetField(TiffTag.EXTRASAMPLES, 1, new[] { (short)ExtraSample.ASSOCALPHA });
                    var scanline = new byte[rowBytes];
                    for (var y = 0; y < height; y++)
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
            var fmt = ext switch
            {
                ".png" => SKEncodedImageFormat.Png, ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
                ".bmp" => SKEncodedImageFormat.Bmp, _ => SKEncodedImageFormat.Png
            };
            if (fmt == SKEncodedImageFormat.Png && ext != ".png") path = Path.ChangeExtension(path, ".png");
            using var fs = File.Open(path, FileMode.Create, FileAccess.Write);
            img.Encode(fmt, 95).SaveTo(fs);
        }
    }

    #endregion
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