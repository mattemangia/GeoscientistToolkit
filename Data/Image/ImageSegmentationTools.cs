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
        public static void ApplyThreshold(byte[] imageData, ImageSegmentationData segmentation,
            byte materialId, int minValue, int maxValue, bool addMode = true)
        {
            segmentation.SaveUndoState();
            Parallel.For(0, segmentation.Height, y =>
            {
                for (int x = 0; x < segmentation.Width; x++)
                {
                    int idx = y * segmentation.Width + x;
                    int pixelIdx = idx * 4;
                    byte gray = (byte)(0.299f * imageData[pixelIdx] + 0.587f * imageData[pixelIdx + 1] + 0.114f * imageData[pixelIdx + 2]);
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

        public static void ApplyBrush(
     ImageSegmentationData segmentation,
     int centerX, int centerY,
     int radius,
     byte materialId,
     bool addMode = true)
        {
            if (segmentation == null || segmentation.LabelData == null) return;
            if (radius <= 0) return;

            int w = segmentation.Width;
            int h = segmentation.Height;

            int minX = Math.Max(0, centerX - radius);
            int maxX = Math.Min(w - 1, centerX + radius);
            int minY = Math.Max(0, centerY - radius);
            int maxY = Math.Min(h - 1, centerY + radius);
            int r2 = radius * radius;

            for (int y = minY; y <= maxY; y++)
            {
                int dy = y - centerY;
                for (int x = minX; x <= maxX; x++)
                {
                    int dx = x - centerX;
                    if (dx * dx + dy * dy <= r2)
                    {
                        int idx = y * w + x;
                        segmentation.LabelData[idx] = addMode ? materialId : (byte)0;
                    }
                }
            }
        }

        public static void ApplyMagicWand(
    byte[] imageData,
    ImageSegmentationData segmentation,
    int seedX, int seedY,
    byte materialId,
    float tolerance,
    bool addMode = true)
        {
            if (imageData == null || segmentation == null) return;
            int w = segmentation.Width;
            int h = segmentation.Height;
            if (seedX < 0 || seedX >= w || seedY < 0 || seedY >= h) return;

            // One undo snapshot per click
            segmentation.SaveUndoState();

            // Seed color (normalize to 0..1)
            int seedIdxPx = (seedY * w + seedX) * 4;
            var seed = new Vector3(
                imageData[seedIdxPx] / 255f,
                imageData[seedIdxPx + 1] / 255f,
                imageData[seedIdxPx + 2] / 255f);

            float tol2 = tolerance * tolerance;
            var visited = new bool[w * h];
            var q = new Queue<(int x, int y)>();
            q.Enqueue((seedX, seedY));
            visited[seedY * w + seedX] = true;

            while (q.Count > 0)
            {
                var (x, y) = q.Dequeue();
                int idx = y * w + x;
                int pix = idx * 4;

                var col = new Vector3(
                    imageData[pix] / 255f,
                    imageData[pix + 1] / 255f,
                    imageData[pix + 2] / 255f);

                if (Vector3.DistanceSquared(seed, col) <= tol2)
                {
                    segmentation.LabelData[idx] = addMode ? materialId : (byte)0;

                    // 4-connected neighborhood
                    if (x > 0 && !visited[idx - 1]) { visited[idx - 1] = true; q.Enqueue((x - 1, y)); }
                    if (x < w - 1 && !visited[idx + 1]) { visited[idx + 1] = true; q.Enqueue((x + 1, y)); }
                    if (y > 0 && !visited[idx - w]) { visited[idx - w] = true; q.Enqueue((x, y - 1)); }
                    if (y < h - 1 && !visited[idx + w]) { visited[idx + w] = true; q.Enqueue((x, y + 1)); }
                }
            }
        }
        private static void TryAddNeighbor(Queue<(int, int)> queue, bool[] visited, int x, int y, int width, int height)
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

        public static void ApplyTopHat(byte[] imageData, ImageSegmentationData segmentation,
            byte materialId, int kernelSize, int threshold)
        {
            segmentation.SaveUndoState();
            byte[] grayscale = new byte[segmentation.Width * segmentation.Height];
            for (int i = 0; i < grayscale.Length; i++)
            {
                int pixelIdx = i * 4;
                grayscale[i] = (byte)(0.299f * imageData[pixelIdx] + 0.587f * imageData[pixelIdx + 1] + 0.114f * imageData[pixelIdx + 2]);
            }
            byte[] opened = MorphologicalOpening(grayscale, segmentation.Width, segmentation.Height, kernelSize);
            byte[] tophat = new byte[grayscale.Length];
            for (int i = 0; i < grayscale.Length; i++)
            {
                int diff = grayscale[i] - opened[i];
                tophat[i] = (byte)Math.Max(0, diff);
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

        public static void ApplyWatershed(byte[] imageData, ImageSegmentationData segmentation, List<(int x, int y, byte materialId)> seeds)
        {
            if (seeds == null || seeds.Count == 0) return;
            segmentation.SaveUndoState();
            float[] gradient = ComputeGradientMagnitude(imageData, segmentation.Width, segmentation.Height);
            var pq = new SortedSet<(float priority, int x, int y, byte label)>(
                Comparer<(float, int, int, byte)>.Create((a, b) =>
                {
                    int cmp = a.Item1.CompareTo(b.Item1);
                    if (cmp != 0) return cmp;
                    cmp = a.Item2.CompareTo(b.Item2);
                    if (cmp != 0) return cmp;
                    return a.Item3.CompareTo(b.Item3);
                }));
            bool[,] inQueue = new bool[segmentation.Width, segmentation.Height];
            foreach (var seed in seeds)
            {
                if (seed.x >= 0 && seed.x < segmentation.Width && seed.y >= 0 && seed.y < segmentation.Height)
                {
                    int idx = seed.y * segmentation.Width + seed.x;
                    segmentation.LabelData[idx] = seed.materialId;
                    inQueue[seed.x, seed.y] = true;
                    AddWatershedNeighbors(pq, inQueue, gradient, seed.x, seed.y, seed.materialId, segmentation.Width, segmentation.Height);
                }
            }
            while (pq.Count > 0)
            {
                var current = pq.Min;
                pq.Remove(current);
                int idx = current.y * segmentation.Width + current.x;
                if (segmentation.LabelData[idx] == 0)
                {
                    segmentation.LabelData[idx] = current.label;
                    AddWatershedNeighbors(pq, inQueue, gradient, current.x, current.y, current.label, segmentation.Width, segmentation.Height);
                }
            }
        }

        private static void AddWatershedNeighbors(SortedSet<(float, int, int, byte)> pq, bool[,] inQueue, float[] gradient, int x, int y, byte label, int width, int height)
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
                    float center = 0.299f * imageData[pixelIdx] + 0.587f * imageData[pixelIdx + 1] + 0.114f * imageData[pixelIdx + 2];
                    int leftIdx = (y * width + (x - 1)) * 4;
                    int rightIdx = (y * width + (x + 1)) * 4;
                    float left = 0.299f * imageData[leftIdx] + 0.587f * imageData[leftIdx + 1] + 0.114f * imageData[leftIdx + 2];
                    float right = 0.299f * imageData[rightIdx] + 0.587f * imageData[rightIdx + 1] + 0.114f * imageData[rightIdx + 2];
                    float gx = (right - left) / 2;
                    int topIdx = ((y - 1) * width + x) * 4;
                    int bottomIdx = ((y + 1) * width + x) * 4;
                    float top = 0.299f * imageData[topIdx] + 0.587f * imageData[topIdx + 1] + 0.114f * imageData[topIdx + 2];
                    float bottom = 0.299f * imageData[bottomIdx] + 0.587f * imageData[bottomIdx + 1] + 0.114f * imageData[bottomIdx + 2];
                    float gy = (bottom - top) / 2;
                    gradient[idx] = (float)Math.Sqrt(gx * gx + gy * gy);
                }
            }
            return gradient;
        }
    }


    public class ImageSegmentationToolsUI : IDatasetTools
    {
        private ImageDataset _dataset;
        private ImageDataset _lastDatasetForPreview = null;
        private int _selectedTool = 0;
        private readonly string[] _toolNames = { "Threshold", "Brush", "Magic Wand", "Top-Hat", "Watershed" };
        private byte _selectedMaterialId = 0;
        private string _newMaterialName = "New Material";
        private Vector4 _newMaterialColor = new Vector4(1, 0, 0, 1);
        private int _thresholdMin = 0;
        private int _thresholdMax = 255;
        private bool _showThresholdPreview = false;
        private bool _isPreviewingThreshold = false;
        private byte[] _previewBackupLabelData;
        private int _brushRadius = 10;
        private bool _brushAddMode = true;
        private float _magicWandTolerance = 0.1f;
        private int _topHatKernelSize = 5;
        private int _topHatThreshold = 30;
        private List<(int x, int y, byte materialId)> _watershedSeeds = new List<(int, int, byte)>();
        private readonly ImGuiExportFileDialog _exportDialog;
        private readonly ImGuiFileDialog _importDialog;
        private Action _invalidateTextureCallback;

        public ImageSegmentationToolsUI()
        {
            _exportDialog = new ImGuiExportFileDialog("ExportSegmentation", "Export Segmentation");
            _exportDialog.SetExtensions((".png", "PNG Image"), (".tiff", "TIFF Image"));
            _importDialog = new ImGuiFileDialog("ImportSegmentation", FileDialogType.OpenFile, "Import Segmentation");
        }

        public void SetInvalidateCallback(Action callback)
        {
            _invalidateTextureCallback = callback;
        }

        public void Draw(Dataset dataset)
        {
            if (dataset is not ImageDataset imageDataset) return;
            _dataset = imageDataset;
            if (_isPreviewingThreshold && (_dataset != _lastDatasetForPreview || _selectedTool != 0))
            {
                CancelThresholdPreview();
            }
            _lastDatasetForPreview = _dataset;
            _dataset.Load();
            if (_dataset.ImageData == null)
            {
                ImGui.TextDisabled("Image data must be loaded to use segmentation tools.");
                return;
            }
            var segmentation = _dataset.GetOrCreateSegmentation();
            if (segmentation == null) return;
            ImGui.Text("Image Segmentation Tools");
            ImGui.Separator();
            DrawMaterialSelection(segmentation);
            ImGui.Separator();
            ImGui.Text("Select Tool:");
            ImGui.SetNextItemWidth(-1);
            ImGui.Combo("##Tool", ref _selectedTool, _toolNames, _toolNames.Length);
            ImGui.Separator();
            switch (_selectedTool)
            {
                case 0: DrawThresholdTool(); break;
                case 1: DrawBrushTool(); break;
                case 2: DrawMagicWandTool(); break;
                case 3: DrawTopHatTool(); break;
                case 4: DrawWatershedTool(); break;
            }
            ImGui.Separator();
            DrawGeneralOperations(segmentation);
        }

        private void DrawMaterialSelection(ImageSegmentationData segmentation)
        {
            ImGui.Text("Current Material:");
            if (ImGui.BeginCombo("##Material", GetMaterialDisplayName(segmentation, _selectedMaterialId)))
            {
                foreach (var material in segmentation.Materials.Where(m => !m.IsExterior))
                {
                    bool isSelected = material.ID == _selectedMaterialId;
                    ImGui.PushStyleColor(ImGuiCol.Text, material.Color);
                    if (ImGui.Selectable(material.Name, isSelected))
                    {
                        _selectedMaterialId = material.ID;

                        // If we are previewing (threshold, etc.), re-run the preview with the new material.
                        if (_isPreviewingThreshold)
                        {
                            _dataset.ShowSegmentationOverlay = true;
                            ApplyThresholdPreview();
                        }
                        else
                        {
                            // No active preview but material change could affect next draws: refresh overlay anyway.
                            _dataset.ShowSegmentationOverlay = true;
                            InvalidateSegmentationTexture();
                        }
                    }
                    ImGui.PopStyleColor();
                    if (isSelected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            ImGui.SameLine();
            if (ImGui.Button("Manage...")) ImGui.OpenPopup("ManageMaterials");
            ImGui.SameLine();
            if (ImGui.Button("Add...")) ImGui.OpenPopup("AddMaterial");

            // Manage materials: live color editing with instant overlay refresh
            if (ImGui.BeginPopup("ManageMaterials"))
            {
                ImGui.Text("Manage Materials");
                ImGui.Separator();

                byte idToRemove = 255;

                foreach (var material in segmentation.Materials.ToList())
                {
                    if (material.IsExterior) continue;

                    ImGui.PushID(material.ID);

                    var color = material.Color;
                    // Editable color; on change -> update model and refresh overlay
                    if (ImGui.ColorEdit4($"Color##{material.ID}", ref color, ImGuiColorEditFlags.NoInputs))
                    {
                        material.Color = color;
                        _dataset.ShowSegmentationOverlay = true;
                        ProjectManager.Instance.HasUnsavedChanges = true;
                        InvalidateSegmentationTexture();
                    }

                    ImGui.SameLine();
                    ImGui.Text(material.Name);

                    ImGui.SameLine();
                    if (ImGui.SmallButton("Remove")) idToRemove = material.ID;

                    ImGui.PopID();
                }

                if (idToRemove != 255)
                {
                    segmentation.RemoveMaterial(idToRemove);
                    if (_selectedMaterialId == idToRemove)
                        _selectedMaterialId = segmentation.Materials.FirstOrDefault(m => !m.IsExterior)?.ID ?? 0;

                    _dataset.ShowSegmentationOverlay = true;
                    ProjectManager.Instance.HasUnsavedChanges = true;
                    InvalidateSegmentationTexture();
                }

                ImGui.Separator();
                if (ImGui.Button("Close")) ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }

            // Add new material
            if (ImGui.BeginPopup("AddMaterial"))
            {
                ImGui.InputText("Name", ref _newMaterialName, 100);
                ImGui.ColorEdit4("Color", ref _newMaterialColor);

                if (ImGui.Button("Add"))
                {
                    var material = segmentation.AddMaterial(_newMaterialName, _newMaterialColor);
                    _selectedMaterialId = material.ID;
                    _dataset.ShowSegmentationOverlay = true;
                    ProjectManager.Instance.HasUnsavedChanges = true;
                    _newMaterialName = "New Material";
                    InvalidateSegmentationTexture();
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }

            ImGui.Separator();
            DrawGeneralOperations(segmentation);
        }


        private string GetMaterialDisplayName(ImageSegmentationData segmentation, byte id)
        {
            return segmentation.GetMaterial(id)?.Name ?? "None";
        }

        private void DrawThresholdTool()
        {
            ImGui.Text("Threshold Segmentation");

            bool minChanged = ImGui.SliderInt("Min Value", ref _thresholdMin, 0, 255);
            bool maxChanged = ImGui.SliderInt("Max Value", ref _thresholdMax, 0, 255);

            bool previewStateChanged = ImGui.Checkbox("Show Preview", ref _showThresholdPreview);
            if (previewStateChanged)
            {
                if (_showThresholdPreview)
                {
                    _dataset.ShowSegmentationOverlay = true; // ensure visible while previewing
                    var segmentation = _dataset.GetOrCreateSegmentation();
                    _previewBackupLabelData = (byte[])segmentation.LabelData.Clone();
                    _isPreviewingThreshold = true;
                    ApplyThresholdPreview();
                }
                else
                {
                    CancelThresholdPreview();
                }
            }

            if (_isPreviewingThreshold && (minChanged || maxChanged))
                ApplyThresholdPreview();

            if (ImGui.Button("Apply Threshold", new Vector2(-1, 0)))
            {
                if (_dataset == null) return;

                // If we were previewing, restore the backup first,
                // then apply the result "for real".
                if (_isPreviewingThreshold && _previewBackupLabelData != null)
                {
                    var segmentation = _dataset.GetOrCreateSegmentation();
                    Array.Copy(_previewBackupLabelData, segmentation.LabelData, _previewBackupLabelData.Length);
                }

                _dataset.Load();
                ImageSegmentationTools.ApplyThreshold(
                    _dataset.ImageData,
                    _dataset.GetOrCreateSegmentation(),
                    _selectedMaterialId,
                    _thresholdMin,
                    _thresholdMax);

                _dataset.ShowSegmentationOverlay = true;
                ProjectManager.Instance.HasUnsavedChanges = true;

                // IMPORTANT: mark preview as finished BEFORE cancel,
                // so CancelThresholdPreview won't restore the old labels.
                _isPreviewingThreshold = false;
                _previewBackupLabelData = null;

                CancelThresholdPreview();      // now this only clears flags
                InvalidateSegmentationTexture(); // refresh overlay
            }
        }


        private void DrawBrushTool()
        {
            ImGui.Text("Brush Tool");
            ImGui.SliderInt("Radius", ref _brushRadius, 1, 200);

            int mode = _brushAddMode ? 0 : 1;
            ImGui.RadioButton("Add##brushMode", ref mode, 0); ImGui.SameLine();
            ImGui.RadioButton("Erase##brushMode", ref mode, 1);
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
                    if (ImGui.Selectable(size.ToString(), _topHatKernelSize == size)) { _topHatKernelSize = size; }
                }
                ImGui.EndCombo();
            }
            ImGui.SliderInt("Threshold", ref _topHatThreshold, 1, 100);
            if (ImGui.Button("Apply Top-Hat", new Vector2(-1, 0)))
            {
                if (_dataset == null) return;
                _dataset.Load();
                ImageSegmentationTools.ApplyTopHat(_dataset.ImageData, _dataset.GetOrCreateSegmentation(), _selectedMaterialId, _topHatKernelSize, _topHatThreshold);
                ProjectManager.Instance.HasUnsavedChanges = true;
                InvalidateSegmentationTexture();
            }
        }

        private void DrawWatershedTool()
        {
            ImGui.Text("Watershed Segmentation");
            ImGui.Text($"Seeds placed: {_watershedSeeds.Count}");
            if (ImGui.Button("Clear Seeds")) { _watershedSeeds.Clear(); }
            ImGui.SameLine();
            if (ImGui.Button("Apply Watershed", new Vector2(-1, 0)))
            {
                if (_watershedSeeds.Count > 0 && _dataset != null)
                {
                    _dataset.Load();
                    ImageSegmentationTools.ApplyWatershed(_dataset.ImageData, _dataset.GetOrCreateSegmentation(), _watershedSeeds);
                    _watershedSeeds.Clear();
                    ProjectManager.Instance.HasUnsavedChanges = true;
                    InvalidateSegmentationTexture();
                }
            }
            ImGui.TextWrapped("Click on the image to place seed points for each region.");
        }

        private void DrawGeneralOperations(ImageSegmentationData segmentation)
        {
            ImGui.Text("Operations:");
            if (_dataset != null)
            {
                bool showOverlay = _dataset.ShowSegmentationOverlay;
                if (ImGui.Checkbox("Show Labels", ref showOverlay))
                {
                    _dataset.ShowSegmentationOverlay = showOverlay;
                }
            }
            if (ImGui.Button("Clear All"))
            {
                segmentation.SaveUndoState();
                segmentation.Clear();
                ProjectManager.Instance.HasUnsavedChanges = true;
                if (_dataset != null) InvalidateSegmentationTexture();
            }
            ImGui.SameLine();
            if (ImGui.Button("Undo"))
            {
                segmentation.Undo();
                if (_dataset != null) InvalidateSegmentationTexture();
            }
            ImGui.SameLine();
            if (ImGui.Button("Redo"))
            {
                segmentation.Redo();
                if (_dataset != null) InvalidateSegmentationTexture();
            }
            ImGui.Separator();
            if (ImGui.Button("Export Labels", new Vector2(-1, 0))) { _exportDialog.Open(_dataset.Name + "_labels"); }
            if (ImGui.Button("Import Labels", new Vector2(-1, 0)))
            {
                string[] extensions = { ".png", ".tiff", ".tif" };
                _importDialog.Open(null, extensions);
            }
            if (_exportDialog.Submit()) { ImageSegmentationExporter.ExportLabeledImage(segmentation, _exportDialog.SelectedPath); }
            if (_importDialog.Submit())
            {
                if (_dataset != null)
                {
                    _dataset.LoadSegmentationFromFile(_importDialog.SelectedPath);
                    InvalidateSegmentationTexture();
                    ProjectManager.Instance.HasUnsavedChanges = true;
                }
            }
        }

        public void HandleMouseClick(int x, int y)
        {
            if (_dataset == null) return;

            var segmentation = _dataset.GetOrCreateSegmentation();
            if (segmentation == null) return;

            bool needsInvalidate = false;

            switch (_selectedTool)
            {
                case 0: // Threshold
                        // Threshold is controlled via sliders/preview/apply in the panel.
                        // Mouse click does nothing here by design.
                    break;

                case 1: // Brush (continuous painting while LMB/RMB is held)
                    {
                        // Left button = add/erase depending on _brushAddMode
                        // Right button = force erase (set to 0)
                        bool leftDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);
                        bool rightDown = ImGui.IsMouseDown(ImGuiMouseButton.Right);

                        if (leftDown || rightDown)
                        {
                            // One undo snapshot at stroke start.
                            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                                segmentation.SaveUndoState();

                            bool addMode = _brushAddMode;
                            if (rightDown) addMode = false; // RMB always erases

                            ImageSegmentationTools.ApplyBrush(
                                segmentation,
                                x, y,
                                _brushRadius,
                                _selectedMaterialId,
                                addMode);

                            _dataset.ShowSegmentationOverlay = true;
                            needsInvalidate = true;
                        }
                        break;
                    }

                case 2: // Magic Wand (single-click flood; LMB=add, RMB=erase)
                    {
                        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                        {
                            _dataset.Load(); // ensure _dataset.ImageData is present

                            bool addMode = ImGui.IsMouseClicked(ImGuiMouseButton.Left);

                            ImageSegmentationTools.ApplyMagicWand(
                                _dataset.ImageData,
                                segmentation,
                                x, y,
                                _selectedMaterialId,
                                _magicWandTolerance,
                                addMode /* true=add to material, false=erase to 0 */);

                            _dataset.ShowSegmentationOverlay = true;
                            needsInvalidate = true;
                        }
                        break;
                    }

                case 3: // Top-Hat
                        // Top-Hat is a filter with Apply button in the panel.
                        // Mouse click does nothing here by design.
                    break;

                case 4: // Watershed (LMB places a seed of current material, RMB removes last seed)
                    {
                        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                        {
                            _watershedSeeds.Add((x, y, _selectedMaterialId));
                            // Only placing seeds; overlay not changed yet.
                        }
                        else if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                        {
                            if (_watershedSeeds.Count > 0)
                                _watershedSeeds.RemoveAt(_watershedSeeds.Count - 1);
                        }
                        break;
                    }

                default:
                    // Unknown tool id: do nothing.
                    break;
            }

            if (needsInvalidate)
            {
                ProjectManager.Instance.HasUnsavedChanges = true;
                InvalidateSegmentationTexture(); // force rebuild of overlay texture
            }
        }

        private void ApplyThresholdPreview()
        {
            if (_dataset == null || !_isPreviewingThreshold || _previewBackupLabelData == null) return;

            _dataset.ShowSegmentationOverlay = true;

            var segmentation = _dataset.GetOrCreateSegmentation();
            var imageData = _dataset.ImageData;

            Array.Copy(_previewBackupLabelData, segmentation.LabelData, _previewBackupLabelData.Length);

            Parallel.For(0, segmentation.Height, y =>
            {
                for (int x = 0; x < segmentation.Width; x++)
                {
                    int idx = y * segmentation.Width + x;
                    int pixelIdx = idx * 4;

                    byte gray = (byte)(0.299f * imageData[pixelIdx] +
                                       0.587f * imageData[pixelIdx + 1] +
                                       0.114f * imageData[pixelIdx + 2]);

                    if (gray >= _thresholdMin && gray <= _thresholdMax)
                        segmentation.LabelData[idx] = _selectedMaterialId;
                }
            });

            InvalidateSegmentationTexture();
        }

        private void CancelThresholdPreview()
        {
            // If _isPreviewingThreshold was already set to false (e.g., after Apply),
            // this will NOT restore the backup.
            if (_isPreviewingThreshold && _dataset != null && _previewBackupLabelData != null)
            {
                var segmentation = _dataset.GetOrCreateSegmentation();
                Array.Copy(_previewBackupLabelData, segmentation.LabelData, _previewBackupLabelData.Length);
                InvalidateSegmentationTexture();
            }

            _previewBackupLabelData = null;
            _isPreviewingThreshold = false;
            _showThresholdPreview = false;
            _lastDatasetForPreview = null;
        }
        private void InvalidateSegmentationTexture()
        {
            if (_invalidateTextureCallback != null)
            {
                _invalidateTextureCallback.Invoke();
                return;
            }

            // Fallback: directly invalidate the cache by key if callback wasn't wired yet
            if (_dataset != null)
            {
                string key = ((_dataset.FilePath ?? string.Empty) + "_segmentation");
                GlobalPerformanceManager.Instance.TextureCache.Invalidate(key);
            }
        }
    }
}