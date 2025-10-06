// GeoscientistToolkit/Analysis/AcousticSimulation/RealTimeTomographyViewer.cs
using System;
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
        private int _lastSliceAxis = -1;
        private int _lastSliceIndex = -1;

        // --- NEW: Data fields for the dynamic legend ---
        private float _currentSliceMinVel;
        private float _currentSliceMaxVel;


        // Data and Processing State
        private SimulationResults _currentDataSource;
        private Vector3 _dimensions;
        private readonly VelocityTomographyGenerator _generator = new VelocityTomographyGenerator();
        private CancellationTokenSource _updateCts;
        private Task _generationTask;
        private string _statusMessage = "No data available.";
        private bool _isLive;

        // Graphics Resources
        private TextureManager _tomographyTexture;
        // --- MODIFIED: Change tuple to include min/max velocity ---
        private (byte[] pixelData, int w, int h, float minVel, float maxVel)? _pendingTextureUpdate;

        public void Show() => _isOpen = true;

        /// <summary>
        /// Sets the final simulation results as the data source for the viewer.
        /// </summary>
        public void SetFinalData(SimulationResults results, Vector3 dimensions)
        {
            _currentDataSource = results;
            _dimensions = dimensions;
            _isLive = false;
            _statusMessage = "Final Results";
            RequestUpdate();
        }

        /// <summary>
        /// Updates the viewer with live data from an ongoing simulation.
        /// </summary>
        public void UpdateLiveData(SimulationResults liveResults, Vector3 dimensions)
        {
            if (!_isOpen) return;
            _currentDataSource = liveResults;
            _dimensions = dimensions;
            _isLive = true;
            _statusMessage = "Live Simulation Data";
            RequestUpdate();
        }

        /// <summary>
        /// Draws the entire tomography window and handles UI logic.
        /// </summary>
        public void Draw()
        {
            // Safely create/update texture from the main UI thread if new data is ready.
            // --- MODIFIED: Handle the new tuple ---
            if (_pendingTextureUpdate.HasValue)
            {
                var (pixelData, w, h, minVel, maxVel) = _pendingTextureUpdate.Value;
                _tomographyTexture?.Dispose();
                _tomographyTexture = TextureManager.CreateFromPixelData(pixelData, (uint)w, (uint)h);
                
                // --- NEW: Update the state for the legend ---
                _currentSliceMinVel = minVel;
                _currentSliceMaxVel = maxVel;

                _pendingTextureUpdate = null; // Consume the update
            }

            if (!_isOpen) return;

            ImGui.SetNextWindowSize(new Vector2(600, 700), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Velocity Tomography", ref _isOpen))
            {
                if (_currentDataSource == null)
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
            // --- Status and Info Panel ---
            ImGui.Text("Data Source:");
            ImGui.SameLine();
            var statusColor = _isLive ? new Vector4(0.1f, 1.0f, 0.1f, 1.0f) : new Vector4(0.5f, 0.8f, 1.0f, 1.0f);
            ImGui.TextColored(statusColor, _statusMessage);
            
            if (_currentDataSource != null)
            {
                ImGui.Text($"Vp: {_currentDataSource.PWaveVelocity:F0} m/s | Vs: {_currentDataSource.SWaveVelocity:F0} m/s | Vp/Vs: {_currentDataSource.VpVsRatio:F3}");
            }
            ImGui.Separator();

            // --- Slice Selection ---
            ImGui.Text("Tomography Slice");
            bool changed = false;
            changed |= ImGui.RadioButton("X Axis (YZ Plane)", ref _sliceAxis, 0); ImGui.SameLine();
            changed |= ImGui.RadioButton("Y Axis (XZ Plane)", ref _sliceAxis, 1); ImGui.SameLine();
            changed |= ImGui.RadioButton("Z Axis (XY Plane)", ref _sliceAxis, 2);

            int maxSlice = _sliceAxis switch
            {
                0 => (int)_dimensions.X - 1,
                1 => (int)_dimensions.Y - 1,
                _ => (int)_dimensions.Z - 1,
            };
            _sliceIndex = Math.Clamp(_sliceIndex, 0, maxSlice);
            changed |= ImGui.SliderInt("Slice Index", ref _sliceIndex, 0, maxSlice);

            if (changed)
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
                // Display slice info
                int w = _sliceAxis switch { 0 => (int)_dimensions.Y, 1 => (int)_dimensions.X, _ => (int)_dimensions.X };
                int h = _sliceAxis switch { 0 => (int)_dimensions.Z, 1 => (int)_dimensions.Z, _ => (int)_dimensions.Y };
                ImGui.Text($"Displaying slice {_sliceIndex} on Axis {(_sliceAxis == 0 ? "X" : _sliceAxis == 1 ? "Y" : "Z")}. Image size: {w}x{h}");
                
                // Calculate aspect ratio and draw image
                var availableSize = ImGui.GetContentRegionAvail();
                availableSize.Y -= 80; // Reserve space for color bar and buttons

                if (availableSize.X > 50 && availableSize.Y > 50)
                {
                    float aspectRatio = (float)w / h;
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
            ImGui.Dummy(new Vector2(0, 10)); // Spacer
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            var region = ImGui.GetContentRegionAvail();
            float barWidth = Math.Max(200, region.X * 0.75f);
            float barHeight = 20;
            pos.X += (region.X - barWidth) / 2; // Center the bar
        
            // --- NEW: Add a title for the legend ---
            string title = "Velocity Magnitude (m/s)";
            var titleSize = ImGui.CalcTextSize(title);
            ImGui.SetCursorScreenPos(pos - new Vector2((titleSize.X - barWidth) / 2, titleSize.Y + 2));
            ImGui.Text(title);
        
            // Draw the gradient bar
            for (int i = 0; i < barWidth; i++)
            {
                float value = i / barWidth;
                var color = _generator.GetJetColor(value);
                uint col32 = ImGui.GetColorU32(color);
                drawList.AddRectFilled(new Vector2(pos.X + i, pos.Y), new Vector2(pos.X + i + 1, pos.Y + barHeight), col32);
            }
        
            // Draw labels
            string minLabel = $"{_currentSliceMinVel:F2}";
            string maxLabel = $"{_currentSliceMaxVel:F2}";

            ImGui.SetCursorScreenPos(pos + new Vector2(0, barHeight + 5));
            float barStartX = ImGui.GetCursorPosX(); // Get local X at the start of the bar
            
            ImGui.Text(minLabel); // Draw min label at the start

            float maxLabelWidth = ImGui.CalcTextSize(maxLabel).X;
            
            // --- FIX: Use local coordinates for SameLine to correctly right-align the max label ---
            ImGui.SameLine(barStartX + barWidth - maxLabelWidth); 
            ImGui.Text(maxLabel); // Draw max label
        }


        /// <summary>
        /// Cancels any ongoing generation and starts a new one for the current slice settings.
        /// </summary>
        private void RequestUpdate()
        {
            // If there's no data source, there's nothing to do.
            if (_currentDataSource == null) return;

            // If the view hasn't changed, don't re-render.
            if (_sliceAxis == _lastSliceAxis && _sliceIndex == _lastSliceIndex && !_isLive) return;

            // Cancel the previous task if it's still running.
            _updateCts?.Cancel();
            
            // Create a new cancellation token for the new task.
            _updateCts = new CancellationTokenSource();
            
            _lastSliceAxis = _sliceAxis;
            _lastSliceIndex = _sliceIndex;

            // Start the new generation task in the background.
            _generationTask = GenerateSliceAsync(_updateCts.Token);
        }

        /// <summary>
        /// The background task that generates the tomography pixel data.
        /// </summary>
        private async Task GenerateSliceAsync(CancellationToken token)
        {
            try
            {
                // --- MODIFIED: Update the tuple type here ---
                (byte[] pixelData, int w, int h, float minVel, float maxVel)? result = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var generationResult = _generator.Generate2DTomography(_currentDataSource, _sliceAxis, _sliceIndex);
                    if (!generationResult.HasValue) return null;

                    // Unpack the result from the generator
                    var (pixelData, minVelocity, maxVelocity) = generationResult.Value;

                    int w = _sliceAxis switch { 0 => (int)_dimensions.Y, 1 => (int)_dimensions.X, _ => (int)_dimensions.X };
                    int h = _sliceAxis switch { 0 => (int)_dimensions.Z, 1 => (int)_dimensions.Z, _ => (int)_dimensions.Y };
                    
                    // --- MODIFIED: Return the full tuple ---
                    return ((byte[] pixelData, int w, int h, float minVel, float maxVel)?)(pixelData, w, h, minVelocity, maxVelocity);
                }, token);

                // If the task was cancelled while running, this line will not be reached.
                // If it completes, queue the result for the main thread to process.
                if (result.HasValue)
                {
                    _pendingTextureUpdate = result;
                }
            }
            catch (OperationCanceledException)
            {
                // This is expected and normal. It means a new request has superseded this one.
                // We simply let the task end silently.
            }
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