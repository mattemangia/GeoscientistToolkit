// GeoscientistToolkit/Data/Image/ImageLayerToolsUI.cs

using System;
using System.Numerics;
using GeoscientistToolkit.Data.Image.Selection;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

namespace GeoscientistToolkit.Data.Image
{
    /// <summary>
    /// UI tool for layer management, drawing, and selection
    /// </summary>
    public class ImageLayerToolsUI : IDatasetTools
    {
        private ImageLayerManager _layerManager;
        private ImageSelection _selection;

        // Drawing state
        private DrawingTool _currentTool = DrawingTool.None;
        private Vector4 _currentColor = new Vector4(1, 1, 1, 1);
        private int _brushSize = 10;
        private float _brushHardness = 1.0f;
        private ImageDrawingTools.BrushShape _brushShape = ImageDrawingTools.BrushShape.Circle;

        // Selection state
        private SelectionTool _selectionTool = SelectionTool.Rectangle;
        private int _magicWandTolerance = 32;
        private bool _selectionAdditive = false;

        // Gradient state
        private ImageDrawingTools.GradientType _gradientType = ImageDrawingTools.GradientType.Linear;
        private Vector4 _gradientColor2 = new Vector4(0, 0, 0, 1);
        private Vector2? _gradientStart = null;
        private Vector2? _gradientEnd = null;

        // Drawing stroke tracking
        private Vector2? _lastDrawPoint = null;
        private bool _isDrawing = false;

        // Resize/crop state
        private int _resizeWidth = 800;
        private int _resizeHeight = 600;
        private ImageManipulation.InterpolationMode _interpolationMode = ImageManipulation.InterpolationMode.Bilinear;
        private int _cropX = 0, _cropY = 0, _cropWidth = 100, _cropHeight = 100;
        private float _rotationAngle = 0;
        private ImageManipulation.FlipMode _flipMode = ImageManipulation.FlipMode.Horizontal;

        private enum DrawingTool
        {
            None,
            Brush,
            Pencil,
            Eraser,
            Fill,
            FloodFill,
            GradientFill
        }

        private enum SelectionTool
        {
            None,
            Rectangle,
            Ellipse,
            MagicWand,
            ColorRange,
            Luminance
        }

        public void Draw(Dataset dataset)
        {
            if (dataset is not ImageDataset image) return;

            // Initialize layer manager if needed
            if (_layerManager == null || _layerManager.Width != image.Width || _layerManager.Height != image.Height)
            {
                InitializeLayerManager(image);
            }

            // Initialize selection if needed
            if (_selection == null || _selection.Width != image.Width || _selection.Height != image.Height)
            {
                _selection = new ImageSelection(image.Width, image.Height);
            }

            ImGui.SeparatorText("Layer & Drawing Tools");

            if (ImGui.BeginTabBar("LayerToolsTabs"))
            {
                if (ImGui.BeginTabItem("Layers"))
                {
                    DrawLayersTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Drawing"))
                {
                    DrawDrawingTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Selection"))
                {
                    DrawSelectionTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Transform"))
                {
                    DrawTransformTab(image);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Clipboard"))
                {
                    DrawClipboardTab();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }

        private void InitializeLayerManager(ImageDataset image)
        {
            _layerManager = new ImageLayerManager(image.Width, image.Height);

            // Create initial layer from image data
            var initialLayer = new ImageLayer("Background", image.Width, image.Height);
            if (image.ImageData != null)
            {
                Array.Copy(image.ImageData, initialLayer.Data, image.ImageData.Length);
            }

            _layerManager.AddLayer(initialLayer.ToString());
        }

        #region Layers Tab

        private void DrawLayersTab()
        {
            ImGui.Text("Layer Stack:");

            for (int i = _layerManager.Layers.Count - 1; i >= 0; i--)
            {
                var layer = _layerManager.Layers[i];
                bool isActive = i == _layerManager.ActiveLayerIndex;

                ImGui.PushID(i);

                if (isActive)
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.5f, 0.8f, 1));

                if (ImGui.Button($"{(layer.Visible ? "👁" : "  ")} {layer.Name}##layer{i}", new Vector2(-1, 0)))
                {
                    _layerManager.ActiveLayerIndex = i;
                }

                if (isActive)
                    ImGui.PopStyleColor();

                ImGui.PopID();
            }

            ImGui.Separator();

            if (_layerManager.ActiveLayer != null)
            {
                var layer = _layerManager.ActiveLayer;

                ImGui.Text($"Active: {layer.Name}");

                float opacity = layer.Opacity;
                if (ImGui.SliderFloat("Opacity", ref opacity, 0, 1))
                {
                    layer.Opacity = opacity;
                }

                int blendMode = (int)layer.BlendMode;
                if (ImGui.Combo("Blend Mode", ref blendMode, string.Join('\0', Enum.GetNames<LayerBlendMode>())))
                {
                    layer.BlendMode = (LayerBlendMode)blendMode;
                }

                bool visible = layer.Visible;
                if (ImGui.Checkbox("Visible", ref visible))
                {
                    layer.Visible = visible;
                }

                ImGui.Separator();

                if (ImGui.Button("New Layer", new Vector2(-1, 0)))
                {
                    var newLayer = new ImageLayer($"Layer {_layerManager.Layers.Count + 1}",
                        _layerManager.Width, _layerManager.Height);
                    _layerManager.AddLayer(newLayer.ToString());
                }

                if (ImGui.Button("Duplicate Layer", new Vector2(-1, 0)))
                {
                    _layerManager.DuplicateLayer(_layerManager.ActiveLayerIndex);
                }

                if (ImGui.Button("Delete Layer", new Vector2(-1, 0)) && _layerManager.Layers.Count > 1)
                {
                    _layerManager.RemoveLayer(_layerManager.ActiveLayerIndex);
                }

                ImGui.Spacing();

                if (ImGui.Button("Move Up", new Vector2(-1, 0)))
                {
                    _layerManager.MoveLayerUp(_layerManager.ActiveLayerIndex);
                }

                if (ImGui.Button("Move Down", new Vector2(-1, 0)))
                {
                    _layerManager.MoveLayerDown(_layerManager.ActiveLayerIndex);
                }

                if (ImGui.Button("Merge Down", new Vector2(-1, 0)))
                {
                    _layerManager.MergeDown(_layerManager.ActiveLayerIndex);
                }

                ImGui.Spacing();

                if (ImGui.Button("Flatten Image", new Vector2(-1, 0)))
                {
                    _layerManager.FlattenImage();
                }

                if (ImGui.Button("Apply to Dataset", new Vector2(-1, 0)))
                {
                    ApplyLayersToDataset();
                }
            }
        }

        #endregion

        #region Drawing Tab

        private void DrawDrawingTab()
        {
            ImGui.Text("Tool:");

            if (ImGui.RadioButton("None", _currentTool == DrawingTool.None))
                _currentTool = DrawingTool.None;
            if (ImGui.RadioButton("Brush", _currentTool == DrawingTool.Brush))
                _currentTool = DrawingTool.Brush;
            if (ImGui.RadioButton("Pencil", _currentTool == DrawingTool.Pencil))
                _currentTool = DrawingTool.Pencil;
            if (ImGui.RadioButton("Eraser", _currentTool == DrawingTool.Eraser))
                _currentTool = DrawingTool.Eraser;
            if (ImGui.RadioButton("Fill", _currentTool == DrawingTool.Fill))
                _currentTool = DrawingTool.Fill;
            if (ImGui.RadioButton("Flood Fill", _currentTool == DrawingTool.FloodFill))
                _currentTool = DrawingTool.FloodFill;
            if (ImGui.RadioButton("Gradient", _currentTool == DrawingTool.GradientFill))
                _currentTool = DrawingTool.GradientFill;

            ImGui.Separator();

            ImGui.ColorEdit4("Color", ref _currentColor);

            if (_currentTool == DrawingTool.Brush || _currentTool == DrawingTool.Eraser)
            {
                ImGui.SliderInt("Size", ref _brushSize, 1, 100);
                ImGui.SliderFloat("Hardness", ref _brushHardness, 0, 1);

                int shape = (int)_brushShape;
                if (ImGui.Combo("Shape", ref shape, "Circle\0Square\0Soft\0"))
                {
                    _brushShape = (ImageDrawingTools.BrushShape)shape;
                }
            }

            if (_currentTool == DrawingTool.GradientFill)
            {
                ImGui.ColorEdit4("Color 2", ref _gradientColor2);

                int gradType = (int)_gradientType;
                if (ImGui.Combo("Gradient Type", ref gradType, "Linear\0Radial\0Angular\0Diamond\0"))
                {
                    _gradientType = (ImageDrawingTools.GradientType)gradType;
                }

                if (ImGui.Button("Reset Gradient Points", new Vector2(-1, 0)))
                {
                    _gradientStart = null;
                    _gradientEnd = null;
                }
            }

            ImGui.Spacing();
            ImGui.Text("Usage: Click and drag on image to draw");
        }

        #endregion

        #region Selection Tab

        private void DrawSelectionTab()
        {
            ImGui.Text("Selection Tool:");

            if (ImGui.RadioButton("Rectangle", _selectionTool == SelectionTool.Rectangle))
                _selectionTool = SelectionTool.Rectangle;
            if (ImGui.RadioButton("Ellipse", _selectionTool == SelectionTool.Ellipse))
                _selectionTool = SelectionTool.Ellipse;
            if (ImGui.RadioButton("Magic Wand", _selectionTool == SelectionTool.MagicWand))
                _selectionTool = SelectionTool.MagicWand;
            if (ImGui.RadioButton("Color Range", _selectionTool == SelectionTool.ColorRange))
                _selectionTool = SelectionTool.ColorRange;

            ImGui.Separator();

            ImGui.Checkbox("Additive", ref _selectionAdditive);

            if (_selectionTool == SelectionTool.MagicWand || _selectionTool == SelectionTool.ColorRange)
            {
                ImGui.SliderInt("Tolerance", ref _magicWandTolerance, 0, 255);
            }

            ImGui.Spacing();

            if (_selection != null && _selection.HasSelection)
            {
                ImGui.Text($"Selection: {_selection.MaxX - _selection.MinX + 1}x{_selection.MaxY - _selection.MinY + 1}");

                if (ImGui.Button("Clear Selection", new Vector2(-1, 0)))
                {
                    _selection.Clear();
                }

                if (ImGui.Button("Select All", new Vector2(-1, 0)))
                {
                    _selection.SelectAll();
                }

                if (ImGui.Button("Invert Selection", new Vector2(-1, 0)))
                {
                    _selection.Invert();
                }

                ImGui.Spacing();

                int featherRadius = 2;
                if (ImGui.InputInt("Feather Radius", ref featherRadius))
                {
                    if (featherRadius > 0)
                        _selection.Feather(featherRadius);
                }

                int expandPixels = 5;
                if (ImGui.Button("Expand"))
                {
                    _selection.Expand(expandPixels);
                }
                ImGui.SameLine();
                ImGui.SetNextItemWidth(60);
                ImGui.InputInt("##expand", ref expandPixels);

                int contractPixels = 5;
                if (ImGui.Button("Contract"))
                {
                    _selection.Contract(contractPixels);
                }
                ImGui.SameLine();
                ImGui.SetNextItemWidth(60);
                ImGui.InputInt("##contract", ref contractPixels);
            }
            else
            {
                ImGui.Text("No selection");
            }
        }

        #endregion

        #region Transform Tab

        private void DrawTransformTab(ImageDataset image)
        {
            ImGui.SeparatorText("Resize");

            ImGui.InputInt("Width", ref _resizeWidth);
            ImGui.InputInt("Height", ref _resizeHeight);

            int interpMode = (int)_interpolationMode;
            if (ImGui.Combo("Interpolation", ref interpMode, "Nearest\0Bilinear\0Bicubic\0"))
            {
                _interpolationMode = (ImageManipulation.InterpolationMode)interpMode;
            }

            if (ImGui.Button("Resize Active Layer", new Vector2(-1, 0)))
            {
                ResizeActiveLayer(_resizeWidth, _resizeHeight, _interpolationMode);
            }

            ImGui.SeparatorText("Crop");

            ImGui.InputInt("X", ref _cropX);
            ImGui.InputInt("Y", ref _cropY);
            ImGui.InputInt("Crop Width", ref _cropWidth);
            ImGui.InputInt("Crop Height", ref _cropHeight);

            if (ImGui.Button("Crop Active Layer", new Vector2(-1, 0)))
            {
                CropActiveLayer(_cropX, _cropY, _cropWidth, _cropHeight);
            }

            ImGui.SeparatorText("Rotate");

            ImGui.SliderFloat("Angle", ref _rotationAngle, 0, 360);

            if (ImGui.Button("Rotate 90°", new Vector2(-1, 0)))
                RotateActiveLayer(90);
            if (ImGui.Button("Rotate 180°", new Vector2(-1, 0)))
                RotateActiveLayer(180);
            if (ImGui.Button("Rotate 270°", new Vector2(-1, 0)))
                RotateActiveLayer(270);

            if (ImGui.Button("Rotate Custom", new Vector2(-1, 0)))
                RotateActiveLayer(_rotationAngle);

            ImGui.SeparatorText("Flip");

            if (ImGui.Button("Flip Horizontal", new Vector2(-1, 0)))
                FlipActiveLayer(ImageManipulation.FlipMode.Horizontal);
            if (ImGui.Button("Flip Vertical", new Vector2(-1, 0)))
                FlipActiveLayer(ImageManipulation.FlipMode.Vertical);
        }

        #endregion

        #region Clipboard Tab

        private void DrawClipboardTab()
        {
            ImGui.Text($"Clipboard: {(ImageClipboard.HasData ? $"{ImageClipboard.Width}x{ImageClipboard.Height}" : "Empty")}");

            ImGui.Separator();

            if (ImGui.Button("Copy", new Vector2(-1, 0)))
            {
                if (_selection != null && _selection.HasSelection)
                    ImageClipboard.CopyLayer(_layerManager.ActiveLayer.Name, _layerManager.ActiveLayer.Data, _layerManager.Width, _layerManager.Height, _selection);
                else
                    ImageClipboard.CopyLayer(_layerManager.ActiveLayer.Name, _layerManager.ActiveLayer.Data, _layerManager.Width, _layerManager.Height);
            }

            if (ImGui.Button("Cut", new Vector2(-1, 0)))
            {
                if (_selection != null && _selection.HasSelection)
                    ImageClipboard.CutLayer(_layerManager.ActiveLayer.Name, _layerManager.ActiveLayer.Data, _layerManager.Width, _layerManager.Height, _selection);
                else
                    ImageClipboard.CutLayer(_layerManager.ActiveLayer.Name, _layerManager.ActiveLayer.Data, _layerManager.Width, _layerManager.Height);
            }

            if (ImGui.Button("Paste", new Vector2(-1, 0)) && ImageClipboard.HasData)
            {
                ImageClipboard.PasteToLayer(_layerManager.ActiveLayer.Name, _layerManager.ActiveLayer.Data, _layerManager.Width, _layerManager.Height);
            }

            if (ImGui.Button("Paste as New Layer", new Vector2(-1, 0)) && ImageClipboard.HasData)
            {
                var newLayer = ImageClipboard.PasteAsNewLayer();
                if (newLayer != null)
                    _layerManager.AddLayer(newLayer.ToString());
            }

            ImGui.Separator();

            if (ImGui.Button("Clear Clipboard", new Vector2(-1, 0)))
            {
                ImageClipboard.Clear();
            }
        }

        #endregion

        #region Tool Operations

        public void HandleClick(ImageDataset dataset, int x, int y, bool isLeftClick)
        {
            if (_layerManager?.ActiveLayer == null) return;

            var layer = _layerManager.ActiveLayer;

            // Drawing tools
            switch (_currentTool)
            {
                case DrawingTool.Brush:
                    if (isLeftClick)
                    {
                        byte r = (byte)(_currentColor.X * 255);
                        byte g = (byte)(_currentColor.Y * 255);
                        byte b = (byte)(_currentColor.Z * 255);
                        byte a = (byte)(_currentColor.W * 255);

                        ImageDrawingTools.Brush(layer.Data, _layerManager.Width, _layerManager.Height,
                            x, y, _brushSize, r, g, b, a, _brushShape, _brushHardness, _selection);

                        _lastDrawPoint = new Vector2(x, y);
                        _isDrawing = true;
                    }
                    break;

                case DrawingTool.Pencil:
                    if (isLeftClick)
                    {
                        byte r = (byte)(_currentColor.X * 255);
                        byte g = (byte)(_currentColor.Y * 255);
                        byte b = (byte)(_currentColor.Z * 255);
                        byte a = (byte)(_currentColor.W * 255);

                        ImageDrawingTools.Pencil(layer.Data, _layerManager.Width, _layerManager.Height,
                            x, y, r, g, b, a, _selection);

                        _lastDrawPoint = new Vector2(x, y);
                        _isDrawing = true;
                    }
                    break;

                case DrawingTool.Eraser:
                    if (isLeftClick)
                    {
                        ImageDrawingTools.Eraser(layer.Data, _layerManager.Width, _layerManager.Height,
                            x, y, _brushSize, _brushShape, _brushHardness, _selection);

                        _lastDrawPoint = new Vector2(x, y);
                        _isDrawing = true;
                    }
                    break;

                case DrawingTool.Fill:
                    if (isLeftClick)
                    {
                        byte r = (byte)(_currentColor.X * 255);
                        byte g = (byte)(_currentColor.Y * 255);
                        byte b = (byte)(_currentColor.Z * 255);
                        byte a = (byte)(_currentColor.W * 255);

                        ImageDrawingTools.Fill(layer.Data, _layerManager.Width, _layerManager.Height,
                            r, g, b, a, _selection);
                    }
                    break;

                case DrawingTool.FloodFill:
                    if (isLeftClick)
                    {
                        byte r = (byte)(_currentColor.X * 255);
                        byte g = (byte)(_currentColor.Y * 255);
                        byte b = (byte)(_currentColor.Z * 255);
                        byte a = (byte)(_currentColor.W * 255);

                        ImageDrawingTools.FloodFill(layer.Data, _layerManager.Width, _layerManager.Height,
                            x, y, r, g, b, a, 32, _selection);
                    }
                    break;

                case DrawingTool.GradientFill:
                    if (isLeftClick)
                    {
                        if (_gradientStart == null)
                        {
                            _gradientStart = new Vector2(x, y);
                        }
                        else if (_gradientEnd == null)
                        {
                            _gradientEnd = new Vector2(x, y);

                            byte r1 = (byte)(_currentColor.X * 255);
                            byte g1 = (byte)(_currentColor.Y * 255);
                            byte b1 = (byte)(_currentColor.Z * 255);
                            byte a1 = (byte)(_currentColor.W * 255);

                            byte r2 = (byte)(_gradientColor2.X * 255);
                            byte g2 = (byte)(_gradientColor2.Y * 255);
                            byte b2 = (byte)(_gradientColor2.Z * 255);
                            byte a2 = (byte)(_gradientColor2.W * 255);

                            ImageDrawingTools.GradientFill(layer.Data, _layerManager.Width, _layerManager.Height,
                                _gradientStart.Value, _gradientEnd.Value,
                                r1, g1, b1, a1, r2, g2, b2, a2, _gradientType, _selection);

                            _gradientStart = null;
                            _gradientEnd = null;
                        }
                    }
                    break;
            }

            // Selection tools
            switch (_selectionTool)
            {
                case SelectionTool.MagicWand:
                    if (isLeftClick && dataset.ImageData != null)
                    {
                        ImageSelectionTools.MagicWand(_selection, dataset.ImageData,
                            dataset.Width, dataset.Height, x, y, _magicWandTolerance, true, _selectionAdditive);
                    }
                    break;

                case SelectionTool.ColorRange:
                    if (isLeftClick && dataset.ImageData != null)
                    {
                        int idx = (y * dataset.Width + x) * 4;
                        byte r = dataset.ImageData[idx];
                        byte g = dataset.ImageData[idx + 1];
                        byte b = dataset.ImageData[idx + 2];

                        ImageSelectionTools.SelectByColorRange(_selection, dataset.ImageData,
                            dataset.Width, dataset.Height, r, g, b, _magicWandTolerance, _selectionAdditive);
                    }
                    break;
            }
        }

        public void HandleDrag(ImageDataset dataset, int x, int y)
        {
            if (!_isDrawing || _layerManager?.ActiveLayer == null) return;

            var layer = _layerManager.ActiveLayer;

            if (_lastDrawPoint.HasValue)
            {
                int x1 = (int)_lastDrawPoint.Value.X;
                int y1 = (int)_lastDrawPoint.Value.Y;

                switch (_currentTool)
                {
                    case DrawingTool.Brush:
                        byte r = (byte)(_currentColor.X * 255);
                        byte g = (byte)(_currentColor.Y * 255);
                        byte b = (byte)(_currentColor.Z * 255);
                        byte a = (byte)(_currentColor.W * 255);

                        ImageDrawingTools.BrushLine(layer.Data, _layerManager.Width, _layerManager.Height,
                            x1, y1, x, y, _brushSize, r, g, b, a, _brushShape, _brushHardness, _selection);
                        break;

                    case DrawingTool.Pencil:
                        byte pr = (byte)(_currentColor.X * 255);
                        byte pg = (byte)(_currentColor.Y * 255);
                        byte pb = (byte)(_currentColor.Z * 255);
                        byte pa = (byte)(_currentColor.W * 255);

                        ImageDrawingTools.PencilLine(layer.Data, _layerManager.Width, _layerManager.Height,
                            x1, y1, x, y, pr, pg, pb, pa, _selection);
                        break;

                    case DrawingTool.Eraser:
                        ImageDrawingTools.EraserLine(layer.Data, _layerManager.Width, _layerManager.Height,
                            x1, y1, x, y, _brushSize, _brushShape, _brushHardness, _selection);
                        break;
                }
            }

            _lastDrawPoint = new Vector2(x, y);
        }

        public void HandleRelease()
        {
            _isDrawing = false;
            _lastDrawPoint = null;
        }

        private void ApplyLayersToDataset()
        {
            var composited = _layerManager.CompositeAllLayers();

            // Find the ImageDataset and apply
            // This would need to be passed from the caller
            Console.WriteLine("Layer composite created - apply to dataset");
        }

        private void ResizeActiveLayer(int newWidth, int newHeight, ImageManipulation.InterpolationMode mode)
        {
            if (_layerManager?.ActiveLayer == null) return;

            var layer = _layerManager.ActiveLayer;
            var resized = ImageManipulation.Resize(layer.Data, _layerManager.Width, _layerManager.Height,
                newWidth, newHeight, mode);

            // Create new layer with resized data
            var newLayer = new ImageLayer(layer.Name + " (Resized)", newWidth, newHeight);
            newLayer.Data = resized;
            newLayer.Opacity = layer.Opacity;
            newLayer.BlendMode = layer.BlendMode;

            _layerManager.AddLayer(newLayer.ToString());
        }

        private void CropActiveLayer(int x, int y, int width, int height)
        {
            if (_layerManager?.ActiveLayer == null) return;

            var layer = _layerManager.ActiveLayer;
            var cropped = ImageManipulation.Crop(layer.Data, _layerManager.Width, _layerManager.Height,
                x, y, width, height);

            var newLayer = new ImageLayer(layer.Name + " (Cropped)", width, height);
            newLayer.Data = cropped;
            newLayer.Opacity = layer.Opacity;
            newLayer.BlendMode = layer.BlendMode;

            _layerManager.AddLayer(newLayer.ToString());
        }

        private void RotateActiveLayer(double angle)
        {
            if (_layerManager?.ActiveLayer == null) return;

            var layer = _layerManager.ActiveLayer;
            var (rotated, newWidth, newHeight) = ImageManipulation.Rotate(layer.Data,
                _layerManager.Width, _layerManager.Height, angle, _interpolationMode);

            var newLayer = new ImageLayer(layer.Name + $" (Rotated {angle}°)", newWidth, newHeight);
            newLayer.Data = rotated;
            newLayer.Opacity = layer.Opacity;
            newLayer.BlendMode = layer.BlendMode;

            _layerManager.AddLayer(newLayer.ToString());
        }

        private void FlipActiveLayer(ImageManipulation.FlipMode mode)
        {
            if (_layerManager?.ActiveLayer == null) return;

            var layer = _layerManager.ActiveLayer;
            var flipped = ImageManipulation.Flip(layer.Data, _layerManager.Width, _layerManager.Height, mode);

            layer.Data = flipped;
        }

        #endregion
    }
}
