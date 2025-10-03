// GeoscientistToolkit/UI/AcousticVolume/WaveformViewer.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.AcousticVolume
{
    /// <summary>
    /// Displays acoustic waveforms from simulation results and allows WAV export
    /// </summary>
    public class WaveformViewer : IDisposable
    {
        private readonly AcousticVolumeDataset _dataset;
        private readonly ProgressBarDialog _progressDialog;
        private TextureManager _profileTexture; // For displaying the B-scan

        // Data for the profile
        private float[,] _profileData; // [time, distance]
        private float _maxAbsAmplitude;
        private bool _isCalculating = false;
        private string _statusMessage = "Select a profile line in the viewer to begin.";

        // Store line definition to regenerate if needed
        private List<Tuple<int, int, int>> _linePoints;
        private int _selectedComponent = 0; // 0=Magnitude, 1=X, 2=Y, 3=Z
        
        // --- NEW: For interactive waveform/timeseries plots ---
        private int _selectedTimeIndex = -1;
        private float[] _waveformAtTime;
        private string _waveformTitle = "Waveform at Time T";
        
        private int _selectedDistanceIndex = -1;
        private float[] _timeSeriesAtDistance;
        private string _timeSeriesTitle = "Time Series at Point X";


        public WaveformViewer(AcousticVolumeDataset dataset)
        {
            _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
            _progressDialog = new ProgressBarDialog("Extracting Profile Data");
        }

        public void Draw()
        {
            if (!ImGui.Begin("Waveform Profile Viewer"))
            {
                ImGui.End();
                return;
            }

            // Check for new line data from the viewer on every frame
            if (AcousticInteractionManager.HasNewLine)
            {
                AcousticInteractionManager.HasNewLine = false;
                _ = ExtractProfileDataAsync();
            }

            DrawControls();
            ImGui.Separator();
            DrawProfileDisplay();

            _progressDialog.Submit();

            ImGui.End();
        }

        private void DrawControls()
        {
            ImGui.TextWrapped("This tool extracts a time-series profile (B-scan) along a user-defined line from the animated volume data.");

            if (AcousticInteractionManager.InteractionMode == ViewerInteractionMode.DrawingLine)
            {
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "Drawing mode active in viewer window...");
                if (ImGui.Button("Cancel Drawing"))
                {
                    AcousticInteractionManager.CancelLineDrawing();
                }
            }
            else
            {
                if (ImGui.Button("Select Profile Line in Viewer..."))
                {
                    if (_dataset.TimeSeriesSnapshots == null || _dataset.TimeSeriesSnapshots.Count == 0)
                    {
                        Logger.LogWarning("[WaveformViewer] No time-series data available to create a profile.");
                    }
                    else
                    {
                        AcousticInteractionManager.StartLineDrawing();
                    }
                }
            }
            
            ImGui.SameLine();
            ImGui.Text("Component:");
            ImGui.SameLine();
            string[] components = { "Magnitude", "X", "Y", "Z" };
            ImGui.SetNextItemWidth(100);
            if (ImGui.Combo("##Component", ref _selectedComponent, components, components.Length))
            {
                // If the component changes, re-calculate using the last line
                if (_linePoints != null && _linePoints.Count > 0)
                {
                     _ = ExtractProfileDataAsync(useExistingLine: true);
                }
            }
            
            ImGui.TextWrapped("Left-click on the profile below to view the waveform at a specific time. Right-click to view the time series at a specific point.");
        }

        private void DrawProfileDisplay()
        {
            ImGui.Text("Time-Distance Profile (B-Scan)");
            
            var canvasPos = ImGui.GetCursorScreenPos();
            var canvasSize = ImGui.GetContentRegionAvail();
            canvasSize.Y = ImGui.GetTextLineHeight() * 15; // Set a fixed height for the B-scan
            if (canvasSize.X < 50) canvasSize.X = 50;
            if (canvasSize.Y < 50) canvasSize.Y = 50;

            if (_isCalculating)
            {
                ImGui.Text("Calculating...");
            }
            else if (_profileTexture != null && _profileTexture.IsValid)
            {
                ImGui.Image(_profileTexture.GetImGuiTextureId(), canvasSize);
                HandleProfileInteraction(canvasPos, canvasSize);
            }
            else
            {
                ImGui.Dummy(canvasSize); // Reserve space
                ImGui.TextDisabled(_statusMessage);
            }
            
            ImGui.Separator();

            if (_waveformAtTime != null)
            {
                ImGui.PlotLines(_waveformTitle, ref _waveformAtTime[0], _waveformAtTime.Length, 0,
                    "Distance ->", -_maxAbsAmplitude, _maxAbsAmplitude, new Vector2(0, 100));
            }
            
            if (_timeSeriesAtDistance != null)
            {
                ImGui.PlotLines(_timeSeriesTitle, ref _timeSeriesAtDistance[0], _timeSeriesAtDistance.Length, 0,
                    "Time ->", -_maxAbsAmplitude, _maxAbsAmplitude, new Vector2(0, 100));
            }
        }
        
        private void HandleProfileInteraction(Vector2 canvasPos, Vector2 canvasSize)
        {
            ImGui.SetCursorScreenPos(canvasPos);
            ImGui.InvisibleButton("##profile_interaction", canvasSize);
            var drawList = ImGui.GetWindowDrawList();

            if (ImGui.IsItemHovered())
            {
                var io = ImGui.GetIO();
                Vector2 mousePos = io.MousePos - canvasPos;
                
                // Left-click to select a time (horizontal line)
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    _selectedTimeIndex = (int)Math.Clamp((mousePos.Y / canvasSize.Y) * _profileData.GetLength(0), 0, _profileData.GetLength(0) - 1);
                    ExtractWaveformAtTime();
                }

                // Right-click to select a distance point (vertical line)
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                {
                    _selectedDistanceIndex = (int)Math.Clamp((mousePos.X / canvasSize.X) * _profileData.GetLength(1), 0, _profileData.GetLength(1) - 1);
                    ExtractTimeSeriesAtDistance();
                }
            }

            // Draw horizontal line for selected time
            if (_selectedTimeIndex != -1)
            {
                float y = canvasPos.Y + (float)_selectedTimeIndex / _profileData.GetLength(0) * canvasSize.Y;
                drawList.AddLine(new Vector2(canvasPos.X, y), new Vector2(canvasPos.X + canvasSize.X, y), 0xFF00FFFF, 1.0f); // Cyan
            }
            
            // Draw vertical line for selected distance
            if (_selectedDistanceIndex != -1)
            {
                float x = canvasPos.X + (float)_selectedDistanceIndex / _profileData.GetLength(1) * canvasSize.X;
                drawList.AddLine(new Vector2(x, canvasPos.Y), new Vector2(x, canvasPos.Y + canvasSize.Y), 0xFF00FF00, 1.0f); // Green
            }
        }


        private async Task ExtractProfileDataAsync(bool useExistingLine = false)
        {
            if (_isCalculating) return;
            _isCalculating = true;
            
            // Clear old plots
            _waveformAtTime = null;
            _timeSeriesAtDistance = null;
            _selectedTimeIndex = -1;
            _selectedDistanceIndex = -1;
            
            _progressDialog.Open("Extracting waveform profile...");
            
            try
            {
                await Task.Run(() =>
                {
                    if (_dataset.TimeSeriesSnapshots == null || _dataset.TimeSeriesSnapshots.Count < 2)
                    {
                        _statusMessage = "Error: Not enough time-series data for a profile.";
                        return;
                    }
                    
                    // Step 1: Get the list of points for the line
                    if (!useExistingLine)
                    {
                        _linePoints = GetLinePoints();
                    }

                    if (_linePoints == null || _linePoints.Count == 0)
                    {
                        _statusMessage = "Error: Could not define a valid line.";
                        return;
                    }
                    
                    // Step 2: Extract data for each point over time
                    int numTimeSteps = _dataset.TimeSeriesSnapshots.Count;
                    int numLinePoints = _linePoints.Count;
                    _profileData = new float[numTimeSteps, numLinePoints];
                    _maxAbsAmplitude = 0;

                    for (int t = 0; t < numTimeSteps; t++)
                    {
                        _progressDialog.Update((float)t / numTimeSteps, $"Processing snapshot {t + 1}/{numTimeSteps}");
                        var snapshot = _dataset.TimeSeriesSnapshots[t];

                        var vx = snapshot.GetVelocityField(0);
                        var vy = snapshot.GetVelocityField(1);
                        var vz = snapshot.GetVelocityField(2);

                        if (vx == null || vy == null || vz == null) continue;

                        for (int p = 0; p < numLinePoints; p++)
                        {
                            var point = _linePoints[p];
                            int x = point.Item1;
                            int y = point.Item2;
                            int z = point.Item3;
                            
                            float value = 0;
                            switch (_selectedComponent)
                            {
                                case 1: value = vx[x, y, z]; break; // X
                                case 2: value = vy[x, y, z]; break; // Y
                                case 3: value = vz[x, y, z]; break; // Z
                                case 0: // Magnitude
                                default:
                                    value = (float)Math.Sqrt(vx[x, y, z] * vx[x, y, z] +
                                                             vy[x, y, z] * vy[x, y, z] +
                                                             vz[x, y, z] * vz[x, y, z]);
                                    break;
                            }
                            
                            _profileData[t, p] = value;
                            if (Math.Abs(value) > _maxAbsAmplitude)
                            {
                                _maxAbsAmplitude = Math.Abs(value);
                            }
                        }
                    }

                    // Step 3: Create texture from data
                    UpdateProfileTexture();
                });
            }
            catch (Exception ex)
            {
                Logger.LogError($"[WaveformViewer] Failed to extract profile: {ex.Message}");
                _statusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                _isCalculating = false;
                _progressDialog.Close();
            }
        }
        
        private void ExtractWaveformAtTime()
        {
            if (_profileData == null || _selectedTimeIndex < 0 || _selectedTimeIndex >= _profileData.GetLength(0))
            {
                _waveformAtTime = null;
                return;
            }

            int distancePoints = _profileData.GetLength(1);
            _waveformAtTime = new float[distancePoints];
            for (int i = 0; i < distancePoints; i++)
            {
                _waveformAtTime[i] = _profileData[_selectedTimeIndex, i];
            }
            float time = _dataset.TimeSeriesSnapshots[_selectedTimeIndex].SimulationTime;
            _waveformTitle = $"Waveform at T = {time * 1000:F4} ms";
        }

        private void ExtractTimeSeriesAtDistance()
        {
            if (_profileData == null || _selectedDistanceIndex < 0 || _selectedDistanceIndex >= _profileData.GetLength(1))
            {
                _timeSeriesAtDistance = null;
                return;
            }

            int timePoints = _profileData.GetLength(0);
            _timeSeriesAtDistance = new float[timePoints];
            for (int i = 0; i < timePoints; i++)
            {
                _timeSeriesAtDistance[i] = _profileData[i, _selectedDistanceIndex];
            }
            _timeSeriesTitle = $"Time Series at Distance Point {_selectedDistanceIndex}";
        }


        private List<Tuple<int, int, int>> GetLinePoints()
        {
            var volume = _dataset.PWaveField ?? _dataset.CombinedWaveField;
            if (volume == null) return null;

            int x1_2d = (int)AcousticInteractionManager.LineStartPoint.X;
            int y1_2d = (int)AcousticInteractionManager.LineStartPoint.Y;
            int x2_2d = (int)AcousticInteractionManager.LineEndPoint.X;
            int y2_2d = (int)AcousticInteractionManager.LineEndPoint.Y;
            int slice_coord = AcousticInteractionManager.LineSliceIndex;
            int viewIndex = AcousticInteractionManager.LineViewIndex;
            
            var points = new List<Tuple<int, int, int>>();

            int dx = Math.Abs(x2_2d - x1_2d), sx = x1_2d < x2_2d ? 1 : -1;
            int dy = -Math.Abs(y2_2d - y1_2d), sy = y1_2d < y2_2d ? 1 : -1;
            int err = dx + dy, e2;

            while (true)
            {
                // Convert 2D view coordinates to 3D volume coordinates
                int x_vol, y_vol, z_vol;
                switch (viewIndex)
                {
                    case 0: // XY View
                        x_vol = x1_2d; y_vol = y1_2d; z_vol = slice_coord;
                        if (x_vol >= 0 && x_vol < volume.Width && y_vol >= 0 && y_vol < volume.Height && z_vol >= 0 && z_vol < volume.Depth)
                            points.Add(new Tuple<int, int, int>(x_vol, y_vol, z_vol));
                        break;
                    case 1: // XZ View
                        x_vol = x1_2d; y_vol = slice_coord; z_vol = y1_2d;
                        if (x_vol >= 0 && x_vol < volume.Width && y_vol >= 0 && y_vol < volume.Height && z_vol >= 0 && z_vol < volume.Depth)
                            points.Add(new Tuple<int, int, int>(x_vol, y_vol, z_vol));
                        break;
                    case 2: // YZ View
                        x_vol = slice_coord; y_vol = x1_2d; z_vol = y1_2d; // Corrected from y1_2d to x1_2d for y_vol
                        if (x_vol >= 0 && x_vol < volume.Width && y_vol >= 0 && y_vol < volume.Height && z_vol >= 0 && z_vol < volume.Depth)
                            points.Add(new Tuple<int, int, int>(x_vol, y_vol, z_vol));
                        break;
                }

                if (x1_2d == x2_2d && y1_2d == y2_2d) break;
                e2 = 2 * err;
                if (e2 >= dy) { err += dy; x1_2d += sx; }
                if (e2 <= dx) { err += dx; y1_2d += sy; }
            }
            return points;
        }
        
        private void UpdateProfileTexture()
        {
            if (_profileData == null) return;

            int height = _profileData.GetLength(0); // time
            int width = _profileData.GetLength(1);  // distance
            if (width == 0 || height == 0) return;

            byte[] rgbaData = new byte[width * height * 4];
            bool isSigned = false;
            if (_selectedComponent != 0) // Magnitude is always positive
            {
                 isSigned = _profileData.Cast<float>().Any(v => v < 0);
            }


            for (int y = 0; y < height; y++) // y is time
            {
                for (int x = 0; x < width; x++) // x is distance
                {
                    float value = _profileData[y, x];
                    float normalizedValue;
                    
                    // Dynamically handle normalization for signed vs. unsigned data
                    if (isSigned)
                    {
                        // Normalize [-maxAbs, maxAbs] to [0, 1], with 0 mapping to 0.5
                        normalizedValue = (_maxAbsAmplitude > 1e-9f) ? (value / _maxAbsAmplitude) * 0.5f + 0.5f : 0.5f;
                    }
                    else
                    {
                        // Normalize [0, maxAbs] to [0, 1]
                        normalizedValue = (_maxAbsAmplitude > 1e-9f) ? value / _maxAbsAmplitude : 0;
                    }

                    var color = GetSeismicColor(normalizedValue); // Use seismic for good contrast
                    int index = (y * width + x) * 4;
                    rgbaData[index] = (byte)(color.X * 255);
                    rgbaData[index + 1] = (byte)(color.Y * 255);
                    rgbaData[index + 2] = (byte)(color.Z * 255);
                    rgbaData[index + 3] = 255;
                }
            }
            
            _profileTexture?.Dispose();
            _profileTexture = TextureManager.CreateFromPixelData(rgbaData, (uint)width, (uint)height);
        }
        
        // A simple color function for the profile view
        private Vector4 GetSeismicColor(float v)
        {
            v = Math.Clamp(v, 0.0f, 1.0f);
            // Diverging: Blue -> White -> Red
            // v = 0.0 -> Blue (0,0,1)
            // v = 0.5 -> White (1,1,1)
            // v = 1.0 -> Red (1,0,0)
            if (v < 0.5f)
            {
                float t = v * 2.0f; // remaps [0, 0.5] to [0, 1]
                return new Vector4(t, t, 1.0f, 1.0f); // Interpolate from Blue to White
            }
            else
            {
                float t = (v - 0.5f) * 2.0f; // remaps [0.5, 1] to [0, 1]
                return new Vector4(1.0f, 1.0f - t, 1.0f - t, 1.0f); // Interpolate from White to Red
            }
        }

        public void Dispose()
        {
            _profileTexture?.Dispose();
        }
    }
}