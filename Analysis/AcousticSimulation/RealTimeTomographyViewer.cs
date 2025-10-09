// GeoscientistToolkit/Analysis/AcousticSimulation/RealTimeTomographyViewer.cs

using System.Numerics;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.AcousticSimulation;

public class RealTimeTomographyViewer : IDisposable
{
    private readonly VelocityTomographyGenerator _generator = new();

    private SimulationResults _currentDataSource;
    private float _currentSliceMaxVel;
    private float _currentSliceMinVel;
    private Vector3 _dimensions;
    private Task _generationTask;

    private bool _isLive;
    private bool _isOpen;
    private byte[,,] _labels;

    // FIX: Track when data actually changes
    private int _lastUpdateHash;

    private (byte[] pixelData, int w, int h, float minVel, float maxVel)? _pendingTextureUpdate;
    private ISet<byte> _selectedMaterialIDs;
    private bool _showOnlySelectedMaterial = true;
    private int _sliceAxis;
    private int _sliceIndex;
    private string _statusMessage = "No data available.";
    private TextureManager _tomographyTexture;
    private CancellationTokenSource _updateCts;

    public void Dispose()
    {
        _updateCts?.Cancel();
        _updateCts?.Dispose();
        _tomographyTexture?.Dispose();
        _generator?.Dispose();
    }

    public void Show()
    {
        _isOpen = true;
    }

    public void SetFinalData(SimulationResults results, Vector3 dimensions, byte[,,] labels,
        ISet<byte> selectedMaterialIDs)
    {
        _currentDataSource = results;
        _dimensions = dimensions;
        _labels = labels;
        _selectedMaterialIDs = selectedMaterialIDs;
        _isLive = false;
        _statusMessage = "Final Results";
        RequestUpdate();
    }

    public void UpdateLiveData(SimulationResults liveResults, Vector3 dimensions, byte[,,] labels,
        ISet<byte> selectedMaterialIDs)
    {
        if (!_isOpen) return;

        // FIX: Check if data actually changed
        var newHash = HashCode.Combine(
            liveResults.WaveFieldVx?.GetHashCode() ?? 0,
            dimensions.GetHashCode(),
            _sliceAxis,
            _sliceIndex
        );

        if (newHash == _lastUpdateHash) return; // No change, skip update
        _lastUpdateHash = newHash;

        _currentDataSource = liveResults;
        _dimensions = dimensions;
        _labels = labels;
        _selectedMaterialIDs = selectedMaterialIDs;
        _isLive = true;
        _statusMessage = "Live Simulation Data";
        RequestUpdate();
    }

    public void Draw()
    {
        // FIX: Process pending updates FIRST, before anything else
        if (_pendingTextureUpdate.HasValue)
        {
            var (pixelData, w, h, minVel, maxVel) = _pendingTextureUpdate.Value;

            // Dispose old texture
            _tomographyTexture?.Dispose();
            _tomographyTexture = TextureManager.CreateFromPixelData(pixelData, (uint)w, (uint)h);

            // FIX: Always update min/max
            _currentSliceMinVel = minVel;
            _currentSliceMaxVel = maxVel;

            _pendingTextureUpdate = null;

            Logger.Log($"[TomographyViewer] Updated texture and color scale: min={minVel:E3}, max={maxVel:E3}");
        }

        if (!_isOpen) return;

        ImGui.SetNextWindowSize(new Vector2(600, 700), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Velocity Tomography", ref _isOpen))
        {
            if (_currentDataSource == null || _labels == null)
            {
                ImGui.Text(_statusMessage);
                ImGui.End();
                return;
            }

            DrawControls();
            ImGui.Separator();
            DrawTomographyView();
        }

        ImGui.End();
    }

    private void DrawControls()
    {
        ImGui.Text("Data Source:");
        ImGui.SameLine();
        var statusColor = _isLive ? new Vector4(0.1f, 1.0f, 0.1f, 1.0f) : new Vector4(0.5f, 0.8f, 1.0f, 1.0f);
        ImGui.TextColored(statusColor, _statusMessage);

        ImGui.Text(
            $"Vp={_currentDataSource.PWaveVelocity:F0} m/s | Vs={_currentDataSource.SWaveVelocity:F0} m/s | Vp/Vs={_currentDataSource.VpVsRatio:F3}");

        // FIX: Better spacing and formatting for color scale display
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Current Slice Color Scale:");
        ImGui.Indent();
        ImGui.Text($"Min: {FormatVelocity(_currentSliceMinVel)} m/s");
        ImGui.Text($"Max: {FormatVelocity(_currentSliceMaxVel)} m/s");
        ImGui.Unindent();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Tomography Slice:");
        var controlsChanged = false;

        controlsChanged |= ImGui.RadioButton("X Axis (YZ)", ref _sliceAxis, 0);
        ImGui.SameLine();
        controlsChanged |= ImGui.RadioButton("Y Axis (XZ)", ref _sliceAxis, 1);
        ImGui.SameLine();
        controlsChanged |= ImGui.RadioButton("Z Axis (XY)", ref _sliceAxis, 2);

        var maxSlice = _dimensions.X > 0
            ? _sliceAxis switch
            {
                0 => (int)_dimensions.X - 1,
                1 => (int)_dimensions.Y - 1,
                _ => (int)_dimensions.Z - 1
            }
            : 0;
        _sliceIndex = Math.Clamp(_sliceIndex, 0, maxSlice);

        ImGui.PushItemWidth(-1);
        controlsChanged |= ImGui.SliderInt("##SliceIndex", ref _sliceIndex, 0, maxSlice);
        ImGui.PopItemWidth();
        ImGui.Text($"Slice: {_sliceIndex + 1} / {maxSlice + 1}");

        ImGui.Spacing();
        ImGui.Separator();

        if (ImGui.Checkbox("Show Only Selected Material", ref _showOnlySelectedMaterial))
            controlsChanged = true;

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Hides non-selected materials.\nColor scale adjusts to visible data only.");

        if (controlsChanged)
        {
            _lastUpdateHash = 0;
            RequestUpdate();
        }
    }

    private void DrawTomographyView()
    {
        var w = _sliceAxis switch { 0 => (int)_dimensions.Y, 1 => (int)_dimensions.X, _ => (int)_dimensions.X };
        var h = _sliceAxis switch { 0 => (int)_dimensions.Z, 1 => (int)_dimensions.Z, _ => (int)_dimensions.Y };

        var availableSize = ImGui.GetContentRegionAvail();

        // FIX: Reserve more space for color bar and labels to prevent overlap
        availableSize.Y -= 120; // Increased from 100

        var imageContainerSize = new Vector2(availableSize.X, Math.Max(50, availableSize.Y));
        var imageContainerTopLeft = ImGui.GetCursorScreenPos();

        ImGui.Dummy(imageContainerSize);

        if (_generationTask != null && !_generationTask.IsCompleted)
        {
            var loadingText = "Generating slice...";
            var textSize = ImGui.CalcTextSize(loadingText);
            var textPos = imageContainerTopLeft + (imageContainerSize - textSize) * 0.5f;
            ImGui.GetWindowDrawList().AddText(textPos, ImGui.GetColorU32(ImGuiCol.Text), loadingText);
        }

        if (_tomographyTexture != null && _tomographyTexture.IsValid)
        {
            if (imageContainerSize.X > 50 && imageContainerSize.Y > 50)
            {
                var aspectRatio = w > 0 && h > 0 ? (float)w / h : 1.0f;
                Vector2 imageSize;
                if (imageContainerSize.X / imageContainerSize.Y > aspectRatio)
                    imageSize = new Vector2(imageContainerSize.Y * aspectRatio, imageContainerSize.Y);
                else
                    imageSize = new Vector2(imageContainerSize.X, imageContainerSize.X / aspectRatio);

                var imageTopLeft = imageContainerTopLeft + (imageContainerSize - imageSize) * 0.5f;
                ImGui.GetWindowDrawList().AddImage(_tomographyTexture.GetImGuiTextureId(), imageTopLeft,
                    imageTopLeft + imageSize);
            }

            // FIX: Position cursor AFTER image container, before color bar
            ImGui.SetCursorScreenPos(imageContainerTopLeft + new Vector2(0, imageContainerSize.Y + 5));

            // Color bar goes here
            DrawColorBar();

            // Info text goes AFTER color bar
            ImGui.Spacing();
            ImGui.Text(
                $"Slice {_sliceIndex + 1}/{(_sliceAxis == 0 ? (int)_dimensions.X : _sliceAxis == 1 ? (int)_dimensions.Y : (int)_dimensions.Z)} on {(_sliceAxis == 0 ? "X" : _sliceAxis == 1 ? "Y" : "Z")} axis");
            ImGui.Text($"Image: {w}×{h} pixels");
        }
        else if (_generationTask == null || _generationTask.IsCompleted)
        {
            var noImageText = "No image to display.";
            var textSize = ImGui.CalcTextSize(noImageText);
            var textPos = imageContainerTopLeft + (imageContainerSize - textSize) * 0.5f;
            ImGui.SetCursorScreenPos(textPos);
            ImGui.Text(noImageText);

            var buttonText = "Generate Slice";
            var buttonSize = ImGui.CalcTextSize(buttonText) + ImGui.GetStyle().FramePadding * 2;
            var buttonPos = new Vector2(
                imageContainerTopLeft.X + (imageContainerSize.X - buttonSize.X) * 0.5f,
                textPos.Y + textSize.Y + 10);
            ImGui.SetCursorScreenPos(buttonPos);
            if (ImGui.Button(buttonText)) RequestUpdate();
        }
    }

    private void DrawColorBar()
    {
        var drawList = ImGui.GetWindowDrawList();
        var startPos = ImGui.GetCursorScreenPos();
        var region = ImGui.GetContentRegionAvail();

        var barWidth = Math.Max(200, region.X * 0.75f);
        float barHeight = 20;
        var pos = new Vector2(startPos.X + (region.X - barWidth) / 2, startPos.Y + 30);

        // Title above color bar
        var title = "Velocity Magnitude (m/s)";
        var titleSize = ImGui.CalcTextSize(title);
        var titlePos = new Vector2(pos.X + (barWidth - titleSize.X) / 2, pos.Y - titleSize.Y - 5);

        ImGui.SetCursorScreenPos(titlePos);
        ImGui.Text(title);

        // Draw color bar
        for (var i = 0; i < barWidth; i++)
        {
            var value = i / (barWidth - 1);
            var color = _generator.GetJetColor(value);
            var col32 = ImGui.GetColorU32(color);
            drawList.AddRectFilled(
                new Vector2(pos.X + i, pos.Y),
                new Vector2(pos.X + i + 1, pos.Y + barHeight),
                col32);
        }

        // FIX: Format labels with proper scientific notation
        var minLabel = FormatVelocity(_currentSliceMinVel);
        var maxLabel = FormatVelocity(_currentSliceMaxVel);

        var minLabelSize = ImGui.CalcTextSize(minLabel);
        var maxLabelSize = ImGui.CalcTextSize(maxLabel);

        var labelYPos = pos.Y + barHeight + 5;

        // Min label (left side)
        ImGui.SetCursorScreenPos(new Vector2(pos.X - minLabelSize.X / 2, labelYPos));
        ImGui.Text(minLabel);

        // Max label (right side) - FIX: Properly positioned
        ImGui.SetCursorScreenPos(new Vector2(pos.X + barWidth - maxLabelSize.X / 2, labelYPos));
        ImGui.Text(maxLabel);

        // FIX: Move cursor past the color bar area to prevent overlap
        ImGui.SetCursorScreenPos(new Vector2(startPos.X, labelYPos + maxLabelSize.Y + 10));
        ImGui.Dummy(new Vector2(region.X, 1));
    }

    private string FormatVelocity(float velocity)
    {
        if (velocity == 0) return "0";

        // Use adaptive formatting based on magnitude
        if (velocity >= 1000.0f)
            return $"{velocity / 1000.0f:F2}k";
        if (velocity >= 1.0f)
            return $"{velocity:F2}";
        if (velocity >= 0.001f)
            return $"{velocity * 1000.0f:F2}m";
        if (velocity >= 0.000001f)
            return $"{velocity * 1000000.0f:F2}µ";
        return $"{velocity:E2}";
    }

    private void RequestUpdate()
    {
        if (_currentDataSource == null || _labels == null || _selectedMaterialIDs == null) return;

        _updateCts?.Cancel();
        _updateCts?.Dispose();
        _updateCts = new CancellationTokenSource();

        _generationTask = GenerateSliceAsync(_updateCts.Token);
    }

    private async Task GenerateSliceAsync(CancellationToken token)
    {
        try
        {
            var result = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();

                var generationResult = _generator.Generate2DTomography(
                    _currentDataSource,
                    _sliceAxis,
                    _sliceIndex,
                    _labels,
                    _selectedMaterialIDs,
                    _showOnlySelectedMaterial);

                if (!generationResult.HasValue) return null;

                var (pixelData, minVelocity, maxVelocity) = generationResult.Value;

                var w = _sliceAxis switch { 0 => (int)_dimensions.Y, 1 => (int)_dimensions.X, _ => (int)_dimensions.X };
                var h = _sliceAxis switch { 0 => (int)_dimensions.Z, 1 => (int)_dimensions.Z, _ => (int)_dimensions.Y };

                Logger.Log($"[TomographyViewer] Generated slice with min={minVelocity:E3}, max={maxVelocity:E3}");

                return ((byte[] pixelData, int w, int h, float minVel, float maxVel)?)(pixelData, w, h, minVelocity,
                    maxVelocity);
            }, token);

            if (result.HasValue && !token.IsCancellationRequested)
                _pendingTextureUpdate = result;
        }
        catch (OperationCanceledException)
        {
            /* Expected */
        }
        catch (Exception ex)
        {
            Logger.LogError($"[TomographyViewer] Failed to generate slice: {ex.Message}");
            _statusMessage = "Error generating slice.";
        }
    }
}