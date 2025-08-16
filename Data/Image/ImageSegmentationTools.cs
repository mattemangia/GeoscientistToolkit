// GeoscientistToolkit/Data/Image/Segmentation/ImageSegmentationTools.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Data.Image.Segmentation
{
    public static class ImageSegmentationTools
    {
        /// <summary>
        /// Apply threshold segmentation
        /// </summary>
        public static void ApplyThreshold(byte[] imageData, ImageSegmentationData segmentation, 
            byte materialId, int minValue, int maxValue, bool addMode = true)
        {
            segmentation.SaveUndoState();
            
            Parallel.For(0, segmentation.Height, y =>
            {
                for (int x = 0; x < segmentation.Width; x++)
                {
                    int idx = y * segmentation.Width + x;
                    int pixelIdx = idx * 4; // RGBA
                    
                    // Convert to grayscale
                    byte gray = (byte)(0.299f * imageData[pixelIdx] + 
                                       0.587f * imageData[pixelIdx + 1] + 
                                       0.114f * imageData[pixelIdx + 2]);
                    
                    if (gray >= minValue && gray <= maxValue)
                    {
                        if (addMode)
                            segmentation.LabelData[idx] = materialId;
                        else if (segmentation.LabelData[idx] == materialId)
                            segmentation.LabelData[idx] = 0;
                    }
                }
            });
        }

        /// <summary>
        /// Apply circular brush at a point
        /// </summary>
        public static void ApplyBrush(ImageSegmentationData segmentation, int centerX, int centerY, 
            int radius, byte materialId, bool addMode = true)
        {
            int minX = Math.Max(0, centerX - radius);
            int maxX = Math.Min(segmentation.Width - 1, centerX + radius);
            int minY = Math.Max(0, centerY - radius);
            int maxY = Math.Min(segmentation.Height - 1, centerY + radius);
            
            float radiusSq = radius * radius;
            
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float distSq = (x - centerX) * (x - centerX) + (y - centerY) * (y - centerY);
                    if (distSq <= radiusSq)
                    {
                        int idx = y * segmentation.Width + x;
                        if (addMode)
                            segmentation.LabelData[idx] = materialId;
                        else
                            segmentation.LabelData[idx] = 0;
                    }
                }
            }
        }

        /// <summary>
        /// Magic wand tool - region growing from seed point
        /// </summary>
        public static void ApplyMagicWand(byte[] imageData, ImageSegmentationData segmentation,
            int seedX, int seedY, byte materialId, float tolerance, bool addMode = true)
        {
            if (seedX < 0 || seedX >= segmentation.Width || seedY < 0 || seedY >= segmentation.Height)
                return;
                
            segmentation.SaveUndoState();
            
            int seedIdx = (seedY * segmentation.Width + seedX) * 4;
            Vector3 seedColor = new Vector3(
                imageData[seedIdx] / 255f,
                imageData[seedIdx + 1] / 255f,
                imageData[seedIdx + 2] / 255f
            );
            
            bool[] visited = new bool[segmentation.Width * segmentation.Height];
            Queue<(int x, int y)> queue = new Queue<(int, int)>();
            queue.Enqueue((seedX, seedY));
            visited[seedY * segmentation.Width + seedX] = true;
            
            float toleranceSq = tolerance * tolerance;
            
            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                int idx = y * segmentation.Width + x;
                int pixelIdx = idx * 4;
                
                Vector3 pixelColor = new Vector3(
                    imageData[pixelIdx] / 255f,
                    imageData[pixelIdx + 1] / 255f,
                    imageData[pixelIdx + 2] / 255f
                );
                
                float distSq = Vector3.DistanceSquared(seedColor, pixelColor);
                
                if (distSq <= toleranceSq)
                {
                    if (addMode)
                        segmentation.LabelData[idx] = materialId;
                    else if (segmentation.LabelData[idx] == materialId)
                        segmentation.LabelData[idx] = 0;
                    
                    // Add neighbors
                    TryAddNeighbor(queue, visited, x - 1, y, segmentation.Width, segmentation.Height);
                    TryAddNeighbor(queue, visited, x + 1, y, segmentation.Width, segmentation.Height);
                    TryAddNeighbor(queue, visited, x, y - 1, segmentation.Width, segmentation.Height);
                    TryAddNeighbor(queue, visited, x, y + 1, segmentation.Width, segmentation.Height);
                }
            }
        }

        private static void TryAddNeighbor(Queue<(int, int)> queue, bool[] visited, 
            int x, int y, int width, int height)
        {
            if (x >= 0 && x < width && y >= 0 && y < height)
            {
                int idx = y * width + x;
                if (!visited[idx])
                {
                    visited[idx] = true;
                    queue.Enqueue((x, y));
                }
            }
        }

        /// <summary>
        /// Morphological top-hat operation
        /// </summary>
        public static void ApplyTopHat(byte[] imageData, ImageSegmentationData segmentation,
            byte materialId, int kernelSize, int threshold)
        {
            segmentation.SaveUndoState();
            
            // Convert to grayscale
            byte[] grayscale = new byte[segmentation.Width * segmentation.Height];
            for (int i = 0; i < grayscale.Length; i++)
            {
                int pixelIdx = i * 4;
                grayscale[i] = (byte)(0.299f * imageData[pixelIdx] + 
                                     0.587f * imageData[pixelIdx + 1] + 
                                     0.114f * imageData[pixelIdx + 2]);
            }
            
            // Apply morphological opening (erosion followed by dilation)
            byte[] opened = MorphologicalOpening(grayscale, segmentation.Width, segmentation.Height, kernelSize);
            
            // Top-hat = original - opened
            byte[] tophat = new byte[grayscale.Length];
            for (int i = 0; i < grayscale.Length; i++)
            {
                int diff = grayscale[i] - opened[i];
                tophat[i] = (byte)Math.Max(0, diff);
                
                // Apply threshold to create binary mask
                if (tophat[i] >= threshold)
                {
                    segmentation.LabelData[i] = materialId;
                }
            }
        }

        private static byte[] MorphologicalOpening(byte[] image, int width, int height, int kernelSize)
        {
            byte[] eroded = Erode(image, width, height, kernelSize);
            return Dilate(eroded, width, height, kernelSize);
        }

        private static byte[] Erode(byte[] image, int width, int height, int kernelSize)
        {
            byte[] result = new byte[image.Length];
            int halfKernel = kernelSize / 2;
            
            Parallel.For(0, height, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    byte minVal = 255;
                    
                    for (int ky = -halfKernel; ky <= halfKernel; ky++)
                    {
                        for (int kx = -halfKernel; kx <= halfKernel; kx++)
                        {
                            int nx = x + kx;
                            int ny = y + ky;
                            
                            if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                            {
                                minVal = Math.Min(minVal, image[ny * width + nx]);
                            }
                        }
                    }
                    
                    result[y * width + x] = minVal;
                }
            });
            
            return result;
        }

        private static byte[] Dilate(byte[] image, int width, int height, int kernelSize)
        {
            byte[] result = new byte[image.Length];
            int halfKernel = kernelSize / 2;
            
            Parallel.For(0, height, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    byte maxVal = 0;
                    
                    for (int ky = -halfKernel; ky <= halfKernel; ky++)
                    {
                        for (int kx = -halfKernel; kx <= halfKernel; kx++)
                        {
                            int nx = x + kx;
                            int ny = y + ky;
                            
                            if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                            {
                                maxVal = Math.Max(maxVal, image[ny * width + nx]);
                            }
                        }
                    }
                    
                    result[y * width + x] = maxVal;
                }
            });
            
            return result;
        }

        /// <summary>
        /// Simple watershed segmentation
        /// </summary>
        public static void ApplyWatershed(byte[] imageData, ImageSegmentationData segmentation,
            List<(int x, int y, byte materialId)> seeds)
        {
            if (seeds == null || seeds.Count == 0) return;
            
            segmentation.SaveUndoState();
            
            // Convert to grayscale gradient magnitude
            float[] gradient = ComputeGradientMagnitude(imageData, segmentation.Width, segmentation.Height);
            
            // Priority queue for watershed
            var pq = new SortedSet<(float priority, int x, int y, byte label)>(
                Comparer<(float, int, int, byte)>.Create((a, b) => 
                {
                    int cmp = a.Item1.CompareTo(b.Item1);
                    if (cmp != 0) return cmp;
                    cmp = a.Item2.CompareTo(b.Item2);
                    if (cmp != 0) return cmp;
                    return a.Item3.CompareTo(b.Item3);
                }));
            
            // Initialize with seed points
            bool[,] inQueue = new bool[segmentation.Width, segmentation.Height];
            
            foreach (var seed in seeds)
            {
                if (seed.x >= 0 && seed.x < segmentation.Width && 
                    seed.y >= 0 && seed.y < segmentation.Height)
                {
                    int idx = seed.y * segmentation.Width + seed.x;
                    segmentation.LabelData[idx] = seed.materialId;
                    inQueue[seed.x, seed.y] = true;
                    
                    // Add neighbors to queue
                    AddWatershedNeighbors(pq, inQueue, gradient, seed.x, seed.y, 
                        seed.materialId, segmentation.Width, segmentation.Height);
                }
            }
            
            // Process watershed
            while (pq.Count > 0)
            {
                var current = pq.Min;
                pq.Remove(current);
                
                int idx = current.y * segmentation.Width + current.x;
                if (segmentation.LabelData[idx] == 0)
                {
                    segmentation.LabelData[idx] = current.label;
                    
                    AddWatershedNeighbors(pq, inQueue, gradient, current.x, current.y,
                        current.label, segmentation.Width, segmentation.Height);
                }
            }
        }

        private static void AddWatershedNeighbors(SortedSet<(float, int, int, byte)> pq,
            bool[,] inQueue, float[] gradient, int x, int y, byte label, int width, int height)
        {
            int[] dx = { -1, 1, 0, 0 };
            int[] dy = { 0, 0, -1, 1 };
            
            for (int i = 0; i < 4; i++)
            {
                int nx = x + dx[i];
                int ny = y + dy[i];
                
                if (nx >= 0 && nx < width && ny >= 0 && ny < height && !inQueue[nx, ny])
                {
                    int idx = ny * width + nx;
                    pq.Add((gradient[idx], nx, ny, label));
                    inQueue[nx, ny] = true;
                }
            }
        }

        private static float[] ComputeGradientMagnitude(byte[] imageData, int width, int height)
        {
            float[] gradient = new float[width * height];
            
            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    int idx = y * width + x;
                    int pixelIdx = idx * 4;
                    
                    // Convert center pixel to grayscale
                    float center = 0.299f * imageData[pixelIdx] + 
                                  0.587f * imageData[pixelIdx + 1] + 
                                  0.114f * imageData[pixelIdx + 2];
                    
                    // Sobel operators
                    float gx = 0, gy = 0;
                    
                    // Left and right
                    int leftIdx = ((y) * width + (x - 1)) * 4;
                    int rightIdx = ((y) * width + (x + 1)) * 4;
                    float left = 0.299f * imageData[leftIdx] + 0.587f * imageData[leftIdx + 1] + 0.114f * imageData[leftIdx + 2];
                    float right = 0.299f * imageData[rightIdx] + 0.587f * imageData[rightIdx + 1] + 0.114f * imageData[rightIdx + 2];
                    gx = (right - left) / 2;
                    
                    // Top and bottom
                    int topIdx = ((y - 1) * width + x) * 4;
                    int bottomIdx = ((y + 1) * width + x) * 4;
                    float top = 0.299f * imageData[topIdx] + 0.587f * imageData[topIdx + 1] + 0.114f * imageData[topIdx + 2];
                    float bottom = 0.299f * imageData[bottomIdx] + 0.587f * imageData[bottomIdx + 1] + 0.114f * imageData[bottomIdx + 2];
                    gy = (bottom - top) / 2;
                    
                    gradient[idx] = (float)Math.Sqrt(gx * gx + gy * gy);
                }
            }
            
            return gradient;
        }
    }

    public class ImageSegmentationToolsUI : IDatasetTools
    {
        private ImageDataset _dataset;
        private ImageSegmentationData _segmentation;
        
        // Tool states
        private int _selectedTool = 0;
        private readonly string[] _toolNames = { "Threshold", "Brush", "Magic Wand", "Top-Hat", "Watershed" };
        
        // Material management
        private byte _selectedMaterialId = 0;
        private string _newMaterialName = "New Material";
        private Vector4 _newMaterialColor = new Vector4(1, 0, 0, 1);
        
        // Threshold tool
        private int _thresholdMin = 0;
        private int _thresholdMax = 255;
        private bool _showThresholdPreview = false;
        
        // Brush tool
        private int _brushRadius = 10;
        private bool _brushAddMode = true;
        
        // Magic wand tool
        private float _magicWandTolerance = 0.1f;
        
        // Top-hat tool
        private int _topHatKernelSize = 5;
        private int _topHatThreshold = 30;
        
        // Watershed tool
        private List<(int x, int y, byte materialId)> _watershedSeeds = new List<(int, int, byte)>();
        
        // Export/Import
        private readonly ImGuiExportFileDialog _exportDialog;
        private readonly ImGuiFileDialog _importDialog;
        
        public ImageSegmentationToolsUI()
        {
            _exportDialog = new ImGuiExportFileDialog("ExportSegmentation", "Export Segmentation");
            _exportDialog.SetExtensions(
                (".png", "PNG Image"),
                (".tiff", "TIFF Image")
            );
            
            _importDialog = new ImGuiFileDialog("ImportSegmentation", FileDialogType.OpenFile, "Import Segmentation");
        }
        
        public void Draw(Dataset dataset)
        {
            if (dataset is not ImageDataset imageDataset) return;
            
            _dataset = imageDataset;
            
            // Initialize segmentation if needed
            if (_segmentation == null && _dataset.ImageData != null)
            {
                _dataset.Load();
                _segmentation = new ImageSegmentationData(_dataset.Width, _dataset.Height);
                
                // Add some default materials
                _segmentation.AddMaterial("Region 1", new Vector4(1, 0, 0, 0.5f));
                _segmentation.AddMaterial("Region 2", new Vector4(0, 1, 0, 0.5f));
                _segmentation.AddMaterial("Region 3", new Vector4(0, 0, 1, 0.5f));
            }
            
            if (_segmentation == null) return;
            
            ImGui.Text("Image Segmentation Tools");
            ImGui.Separator();
            
            // Material selection
            DrawMaterialSelection();
            ImGui.Separator();
            
            // Tool selection
            ImGui.Text("Select Tool:");
            ImGui.SetNextItemWidth(-1);
            ImGui.Combo("##Tool", ref _selectedTool, _toolNames, _toolNames.Length);
            
            ImGui.Separator();
            
            // Draw tool-specific UI
            switch (_selectedTool)
            {
                case 0: DrawThresholdTool(); break;
                case 1: DrawBrushTool(); break;
                case 2: DrawMagicWandTool(); break;
                case 3: DrawTopHatTool(); break;
                case 4: DrawWatershedTool(); break;
            }
            
            ImGui.Separator();
            
            // General operations
            DrawGeneralOperations();
        }
        
        private void DrawMaterialSelection()
        {
            ImGui.Text("Current Material:");
            
            if (ImGui.BeginCombo("##Material", GetMaterialDisplayName(_selectedMaterialId)))
            {
                foreach (var material in _segmentation.Materials)
                {
                    bool isSelected = material.ID == _selectedMaterialId;
                    
                    ImGui.PushStyleColor(ImGuiCol.Text, material.Color);
                    if (ImGui.Selectable(material.Name, isSelected))
                    {
                        _selectedMaterialId = material.ID;
                    }
                    ImGui.PopStyleColor();
                    
                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            
            // Add new material
            if (ImGui.Button("Add Material"))
            {
                ImGui.OpenPopup("AddMaterial");
            }
            
            if (ImGui.BeginPopup("AddMaterial"))
            {
                ImGui.InputText("Name", ref _newMaterialName, 100);
                ImGui.ColorEdit4("Color", ref _newMaterialColor);
                
                if (ImGui.Button("Add"))
                {
                    var material = _segmentation.AddMaterial(_newMaterialName, _newMaterialColor);
                    _selectedMaterialId = material.ID;
                    ImGui.CloseCurrentPopup();
                }
                
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    ImGui.CloseCurrentPopup();
                }
                
                ImGui.EndPopup();
            }
        }
        
        private string GetMaterialDisplayName(byte id)
        {
            var material = _segmentation.GetMaterial(id);
            return material != null ? material.Name : "None";
        }
        
        private void DrawThresholdTool()
        {
            ImGui.Text("Threshold Segmentation");
            
            ImGui.SliderInt("Min Value", ref _thresholdMin, 0, 255);
            ImGui.SliderInt("Max Value", ref _thresholdMax, 0, 255);
            
            ImGui.Checkbox("Show Preview", ref _showThresholdPreview);
            
            if (ImGui.Button("Apply Threshold", new Vector2(-1, 0)))
            {
                _dataset.Load();
                ImageSegmentationTools.ApplyThreshold(
                    _dataset.ImageData, _segmentation,
                    _selectedMaterialId, _thresholdMin, _thresholdMax
                );
                ProjectManager.Instance.HasUnsavedChanges = true;
            }
        }
        
        private void DrawBrushTool()
        {
            ImGui.Text("Brush Tool");

            // Radius slider
            ImGui.SliderInt("Radius", ref _brushRadius, 1, 100);

            // Radio buttons expect an int group value, not a ref bool.
            // Map: 0 = Add, 1 = Erase
            int mode = _brushAddMode ? 0 : 1;

            ImGui.RadioButton("Add##brushMode", ref mode, 0);
            ImGui.SameLine();
            ImGui.RadioButton("Erase##brushMode", ref mode, 1);

            // Write back to the bool
            _brushAddMode = (mode == 0);

            ImGui.TextWrapped("Click and drag on the image to paint.");
        }

        
        private void DrawMagicWandTool()
        {
            ImGui.Text("Magic Wand Tool");
            
            ImGui.SliderFloat("Tolerance", ref _magicWandTolerance, 0.01f, 1.0f);
            
            ImGui.TextWrapped("Click on the image to select similar colors.");
        }
        
        private void DrawTopHatTool()
        {
            ImGui.Text("Top-Hat Filter");
            
            int[] kernelSizes = { 3, 5, 7, 9, 11 };
            if (ImGui.BeginCombo("Kernel Size", _topHatKernelSize.ToString()))
            {
                foreach (int size in kernelSizes)
                {
                    if (ImGui.Selectable(size.ToString(), _topHatKernelSize == size))
                    {
                        _topHatKernelSize = size;
                    }
                }
                ImGui.EndCombo();
            }
            
            ImGui.SliderInt("Threshold", ref _topHatThreshold, 1, 100);
            
            if (ImGui.Button("Apply Top-Hat", new Vector2(-1, 0)))
            {
                _dataset.Load();
                ImageSegmentationTools.ApplyTopHat(
                    _dataset.ImageData, _segmentation,
                    _selectedMaterialId, _topHatKernelSize, _topHatThreshold
                );
                ProjectManager.Instance.HasUnsavedChanges = true;
            }
        }
        
        private void DrawWatershedTool()
        {
            ImGui.Text("Watershed Segmentation");
            
            ImGui.Text($"Seeds placed: {_watershedSeeds.Count}");
            
            if (ImGui.Button("Clear Seeds"))
            {
                _watershedSeeds.Clear();
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Apply Watershed", new Vector2(-1, 0)))
            {
                if (_watershedSeeds.Count > 0)
                {
                    _dataset.Load();
                    ImageSegmentationTools.ApplyWatershed(
                        _dataset.ImageData, _segmentation, _watershedSeeds
                    );
                    _watershedSeeds.Clear();
                    ProjectManager.Instance.HasUnsavedChanges = true;
                }
            }
            
            ImGui.TextWrapped("Click on the image to place seed points for each region.");
        }
        
        private void DrawGeneralOperations()
        {
            ImGui.Text("Operations:");
            
            if (ImGui.Button("Clear All"))
            {
                _segmentation.Clear();
                ProjectManager.Instance.HasUnsavedChanges = true;
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Undo"))
            {
                _segmentation.Undo();
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Redo"))
            {
                _segmentation.Redo();
            }
            
            ImGui.Separator();
            
            if (ImGui.Button("Export Labels", new Vector2(-1, 0)))
            {
                _exportDialog.Open(_dataset.Name + "_labels");
            }
            
            if (ImGui.Button("Import Labels", new Vector2(-1, 0)))
            {
                string[] extensions = { ".png", ".tiff", ".tif" };
                _importDialog.Open(null, extensions);
            }
            
            // Handle dialogs
            if (_exportDialog.Submit())
            {
                ImageSegmentationExporter.ExportLabeledImage(_segmentation, _exportDialog.SelectedPath);
            }
            
            if (_importDialog.Submit())
            {
                var imported = ImageSegmentationExporter.ImportLabeledImage(
                    _importDialog.SelectedPath, _dataset.Width, _dataset.Height);
                
                if (imported != null)
                {
                    _segmentation = imported;
                    ProjectManager.Instance.HasUnsavedChanges = true;
                }
            }
        }
        
        // Method to handle mouse interaction from viewer
        public void HandleMouseClick(int x, int y, bool isDragging)
        {
            if (_segmentation == null) return;
            
            switch (_selectedTool)
            {
                case 1: // Brush
                    if (isDragging || ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        _segmentation.SaveUndoState();
                        ImageSegmentationTools.ApplyBrush(
                            _segmentation, x, y, _brushRadius, 
                            _selectedMaterialId, _brushAddMode
                        );
                    }
                    break;
                    
                case 2: // Magic Wand
                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        _dataset.Load();
                        ImageSegmentationTools.ApplyMagicWand(
                            _dataset.ImageData, _segmentation,
                            x, y, _selectedMaterialId, _magicWandTolerance
                        );
                    }
                    break;
                    
                case 4: // Watershed
                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        _watershedSeeds.Add((x, y, _selectedMaterialId));
                    }
                    break;
            }
        }
        
        public ImageSegmentationData GetSegmentation() => _segmentation;
    }

}
