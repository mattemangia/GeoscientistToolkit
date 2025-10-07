// GeoscientistToolkit/Analysis/AcousticSimulation/RealTimeTomographyViewer.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.AcousticSimulation
{
    /// <summary>
    /// A self-contained, reliable UI window for displaying real-time or final tomography results.
    /// This class manages its own state, background processing, and texture resources to prevent race conditions.
    /// </summary>
    public class RealTimeTomographyViewer : IDisposable
    {
        // UI State
        private bool _isOpen;
        private int _sliceAxis; // 0=X, 1=Y, 2=Z
        private int _sliceIndex;
        private bool _showOnlySelectedMaterial = true; // Default to showing filtered view

        // Data fields for the dynamic legend
        private float _currentSliceMinVel;
        private float _currentSliceMaxVel;

        // Data and Processing State
        private SimulationResults _currentDataSource;
        private Vector3 _dimensions;
        private byte[,,] _labels; // Material labels for filtering
        private ISet<byte> _selectedMaterialIDs; // Selected IDs for filtering
        private readonly VelocityTomographyGenerator _generator = new VelocityTomographyGenerator();
        private CancellationTokenSource _updateCts;
        private Task _generationTask;
        private string _statusMessage = "No data available.";
        private bool _isLive;

        // Graphics Resources
        private TextureManager _tomographyTexture;
        private (byte[] pixelData, int w, int h, float minVel, float maxVel)? _pendingTextureUpdate;

        public void Show() => _isOpen = true;

        /// <summary>
        /// Sets the final simulation results as the data source for the viewer.
        /// </summary>
        public void SetFinalData(SimulationResults results, Vector3 dimensions, byte[,,] labels, ISet<byte> selectedMaterialIDs)
        {
            _currentDataSource = results;
            _dimensions = dimensions;
            _labels = labels;
            _selectedMaterialIDs = selectedMaterialIDs;
            _isLive = false;
            _statusMessage = "Final Results";
            RequestUpdate();
        }

        /// <summary>
        /// Updates the viewer with live data from an ongoing simulation.
        /// </summary>
        public void UpdateLiveData(SimulationResults liveResults, Vector3 dimensions, byte[,,] labels, ISet<byte> selectedMaterialIDs)
        {
            if (!_isOpen) return;
            _currentDataSource = liveResults;
            _dimensions = dimensions;
            _labels = labels;
            _selectedMaterialIDs = selectedMaterialIDs;
            _isLive = true;
            _statusMessage = "Live Simulation Data";
            RequestUpdate();
        }

        /// <summary>
        /// Draws the entire tomography window and handles UI logic.
        /// </summary>
        public void Draw()
        {
            if (_pendingTextureUpdate.HasValue)
            {
                var (pixelData, w, h, minVel, maxVel) = _pendingTextureUpdate.Value;
                _tomographyTexture?.Dispose();
                _tomographyTexture = TextureManager.CreateFromPixelData(pixelData, (uint)w, (uint)h);
                
                _currentSliceMinVel = minVel;
                _currentSliceMaxVel = maxVel;

                _pendingTextureUpdate = null;
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
            
            ImGui.Text($"Theoretical data: Vp= {_currentDataSource.PWaveVelocity:F0} m/s | Vs= {_currentDataSource.SWaveVelocity:F0} m/s | Vp/Vs= {_currentDataSource.VpVsRatio:F3}");
            ImGui.Separator();

            ImGui.Text("Tomography Slice");
            bool controlsChanged = false;
            controlsChanged |= ImGui.RadioButton("X Axis (YZ Plane)", ref _sliceAxis, 0); ImGui.SameLine();
            controlsChanged |= ImGui.RadioButton("Y Axis (XZ Plane)", ref _sliceAxis, 1); ImGui.SameLine();
            controlsChanged |= ImGui.RadioButton("Z Axis (XY Plane)", ref _sliceAxis, 2);

            int maxSlice = _dimensions.X > 0 ? _sliceAxis switch
            {
                0 => (int)_dimensions.X - 1,
                1 => (int)_dimensions.Y - 1,
                _ => (int)_dimensions.Z - 1,
            } : 0;
            _sliceIndex = Math.Clamp(_sliceIndex, 0, maxSlice);
            controlsChanged |= ImGui.SliderInt("Slice Index", ref _sliceIndex, 0, maxSlice);

            ImGui.Separator();
            if (ImGui.Checkbox("Show Only Selected Material", ref _showOnlySelectedMaterial))
            {
                controlsChanged = true;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Hides all voxels (e.g., pores, air) that are not part of the currently selected materials.\nThe color scale will dynamically adjust to the visible materials.");
            }

            if (controlsChanged)
            {
                RequestUpdate();
            }
        }

        private void DrawTomographyView()
        {
            if (_generationTask != null && !_generationTask.IsCompleted)
            {
                ImGui.Text("Generating slice...");
            }
            
            if (_tomographyTexture != null && _tomographyTexture.IsValid)
            {
                int w = _sliceAxis switch { 0 => (int)_dimensions.Y, 1 => (int)_dimensions.X, _ => (int)_dimensions.X };
                int h = _sliceAxis switch { 0 => (int)_dimensions.Z, 1 => (int)_dimensions.Z, _ => (int)_dimensions.Y };
                ImGui.Text($"Displaying slice {_sliceIndex} on Axis {(_sliceAxis == 0 ? "X" : _sliceAxis == 1 ? "Y" : "Z")}. Image size: {w}x{h}");
                
                var availableSize = ImGui.GetContentRegionAvail();
                availableSize.Y -= 80;

                if (availableSize.X > 50 && availableSize.Y > 50)
                {
                    float aspectRatio = (w > 0 && h > 0) ? (float)w / h : 1.0f;
                    Vector2 imageSize;
                    if (availableSize.X / availableSize.Y > aspectRatio)
                        imageSize = new Vector2(availableSize.Y * aspectRatio, availableSize.Y);
                    else
                        imageSize = new Vector2(availableSize.X, availableSize.X / aspectRatio);

                    ImGui.Image(_tomographyTexture.GetImGuiTextureId(), imageSize);
                }

                DrawColorBar();
            }
            else if (_generationTask == null || _generationTask.IsCompleted)
            {
                 ImGui.Text("No image to display.");
                 if (ImGui.Button("Generate Slice"))
                 {
                     RequestUpdate();
                 }
            }
        }
        
        private void DrawColorBar()
        {
            ImGui.Dummy(new Vector2(0, 10)); 
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            var region = ImGui.GetContentRegionAvail();
            float barWidth = Math.Max(200, region.X * 0.75f);
            float barHeight = 20;
            pos.X += (region.X - barWidth) / 2;
        
            string title = "Velocity Magnitude (m/s)";
            var titleSize = ImGui.CalcTextSize(title);
            ImGui.SetCursorScreenPos(new Vector2(pos.X + (barWidth - titleSize.X) / 2, pos.Y - titleSize.Y - 4));
            ImGui.Text(title);
        
            for (int i = 0; i < barWidth; i++)
            {
                float value = (float)i / (barWidth - 1);
                var color = _generator.GetJetColor(value);
                uint col32 = ImGui.GetColorU32(color);
                drawList.AddRectFilled(new Vector2(pos.X + i, pos.Y), new Vector2(pos.X + i + 1, pos.Y + barHeight), col32);
            }
        
            string minLabel = $"{_currentSliceMinVel:F0}";
            string maxLabel = $"{_currentSliceMaxVel:F0}";
            var maxLabelSize = ImGui.CalcTextSize(maxLabel);

            ImGui.SetCursorScreenPos(pos + new Vector2(0, barHeight + 2));
            ImGui.Text(minLabel);

            // --- SYNTAX FIX ---
            // The method was being called with two arguments instead of a single Vector2.
            // This is the corrected line that will now compile.
            ImGui.SetCursorScreenPos(pos + new Vector2(barWidth - maxLabelSize.X, barHeight + 2));
            ImGui.Text(maxLabel);
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
                (byte[] pixelData, int w, int h, float minVel, float maxVel)? result = await Task.Run(() =>
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

                    int w = _sliceAxis switch { 0 => (int)_dimensions.Y, 1 => (int)_dimensions.X, _ => (int)_dimensions.X };
                    int h = _sliceAxis switch { 0 => (int)_dimensions.Z, 1 => (int)_dimensions.Z, _ => (int)_dimensions.Y };
                    
                    return ((byte[] pixelData, int w, int h, float minVel, float maxVel)?)(pixelData, w, h, minVelocity, maxVelocity);
                }, token);

                if (result.HasValue && !token.IsCancellationRequested)
                {
                    _pendingTextureUpdate = result;
                }
            }
            catch (OperationCanceledException) { /* Expected */ }
            catch (Exception ex)
            {
                Logger.LogError($"[TomographyViewer] Failed to generate slice: {ex.Message}");
                _statusMessage = "Error generating slice.";
            }
        }

        public void Dispose()
        {
            _updateCts?.Cancel();
            _updateCts?.Dispose();
            _tomographyTexture?.Dispose();
            _generator?.Dispose();
        }
    }
}