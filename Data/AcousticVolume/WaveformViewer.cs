// GeoscientistToolkit/UI/AcousticVolume/WaveformViewer.cs

using System.Numerics;
using System.Text;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using StbImageWriteSharp;

namespace GeoscientistToolkit.UI.AcousticVolume;

/// <summary>
///     Enhanced waveform viewer with B-scan visualization, interactive analysis,
///     and export capabilities for acoustic simulation results.
/// </summary>
public class WaveformViewer : IDisposable
{
    // Color maps
    private readonly string[] _colorMapNames = { "Grayscale", "Seismic", "Jet", "Hot", "Cool", "Viridis" };
    private readonly AcousticVolumeDataset _dataset;
    private readonly ImGuiExportFileDialog _exportCsvDialog;
    private readonly ImGuiExportFileDialog _exportImageDialog;
    private readonly ImGuiExportFileDialog _exportWavDialog;
    private readonly ProgressBarDialog _progressDialog;
    private float _amplitudeScale = 1.0f;
    private bool _autoNormalize = true;

    // Display settings
    private int _colorMapIndex = 2; // Default to Seismic
    private float _dominantFrequency;
    private float[] _frequencyBins;
    private float[] _frequencySpectrum;
    private bool _isCalculating;

    // Window state
    private bool _isWindowOpen = true;

    // Profile metadata
    private List<Tuple<int, int, int>> _linePoints;
    private float _manualMax = 1.0f;
    private float _manualMin = -1.0f;
    private float _maxAbsAmplitude;
    private bool _normalizeAudio = true;
    private float _nyquistFrequency;
    private float _playbackSpeedMultiplier = 1.0f;

    // Profile data
    private float[,] _profileData; // [time, distance]
    private float _profileLengthMm;
    private TextureManager _profileTexture;

    // Export settings
    private int _sampleRate = 44100; // Standard audio sample rate
    private int _selectedComponent; // 0=Magnitude, 1=X, 2=Y, 3=Z

    private int _selectedDistanceIndex = -1;

    // Interactive selection
    private int _selectedTimeIndex = -1;
    private bool _showCrosshairs = true;

    private bool _showFrequencyAnalysis;
    private bool _showHelp;
    private string _statusMessage = "";
    private float _timeRangeMs;
    private float[] _timeSeriesAtDistance;
    private string _timeSeriesTitle = "";
    private bool _useLogarithmicScale;
    private float[] _waveformAtTime;
    private string _waveformTitle = "";

    public WaveformViewer(AcousticVolumeDataset dataset)
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        _progressDialog = new ProgressBarDialog("Processing Waveform Data");
        _exportWavDialog = new ImGuiExportFileDialog("ExportWAV", "Export as WAV Audio");
        _exportWavDialog.SetExtensions((".wav", "WAV Audio File"));
        _exportCsvDialog = new ImGuiExportFileDialog("ExportCSV", "Export Profile Data");
        _exportCsvDialog.SetExtensions((".csv", "Comma Separated Values"));
        _exportImageDialog = new ImGuiExportFileDialog("ExportImage", "Export B-Scan Image");
        _exportImageDialog.SetExtensions((".png", "PNG Image"));

        UpdateStatusMessage();
    }

    public void Dispose()
    {
        _profileTexture?.Dispose();
    }

    public void Draw()
    {
        if (!_isWindowOpen) return;

        ImGui.SetNextWindowSize(new Vector2(900, 600), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Waveform Profile Viewer", ref _isWindowOpen))
        {
            ImGui.End();
            return;
        }

        // Check for new line data
        if (AcousticInteractionManager.HasNewLine)
        {
            AcousticInteractionManager.HasNewLine = false;
            _ = ExtractProfileDataAsync();
        }

        DrawToolbar();
        ImGui.Separator();

        // Main content area with two columns
        ImGui.Columns(2, "WaveformColumns", true);
        ImGui.SetColumnWidth(0, ImGui.GetWindowWidth() * 0.65f);

        // Left column - B-scan display
        DrawProfileDisplay();

        ImGui.NextColumn();

        // Right column - Controls and plots
        DrawControlPanel();

        ImGui.Columns(1);

        // Handle dialogs
        HandleExportDialogs();
        _progressDialog.Submit();

        // Help overlay
        if (_showHelp) DrawHelpOverlay();

        //Frequency Analysis Window
        if (_showFrequencyAnalysis) DrawFrequencyAnalysisWindow();

        ImGui.End();
    }

    private void DrawFrequencyAnalysisWindow()
    {
        if (!_showFrequencyAnalysis) return;

        ImGui.SetNextWindowSize(new Vector2(600, 400), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Frequency Analysis", ref _showFrequencyAnalysis))
        {
            ImGui.Text("Frequency Spectrum Analysis");
            ImGui.Separator();

            if (_frequencySpectrum != null && _frequencyBins != null)
            {
                ImGui.Text($"Dominant Frequency: {_dominantFrequency:F2} Hz");
                ImGui.Text($"Nyquist Frequency: {_nyquistFrequency:F2} Hz");
                ImGui.Text($"Frequency Resolution: {_frequencyBins[1]:F2} Hz");

                ImGui.Spacing();

                // Plot the spectrum
                if (_frequencySpectrum.Length > 0)
                {
                    // Linear scale
                    ImGui.Text("Magnitude Spectrum:");
                    ImGui.PlotLines("##spectrum", ref _frequencySpectrum[0], _frequencySpectrum.Length, 0,
                        $"Peak at {_dominantFrequency:F1} Hz", 0, _frequencySpectrum.Max(),
                        new Vector2(0, 150));

                    // Logarithmic scale (dB)
                    ImGui.Text("Power Spectrum (dB):");
                    var spectrumDb = new float[_frequencySpectrum.Length];
                    var epsilon = 1e-12f;
                    for (var i = 0; i < _frequencySpectrum.Length; i++)
                        spectrumDb[i] = 20.0f * (float)Math.Log10(Math.Max(_frequencySpectrum[i], epsilon));

                    ImGui.PlotLines("##spectrum_db", ref spectrumDb[0], spectrumDb.Length, 0,
                        null, spectrumDb.Min(), Math.Max(spectrumDb.Max(), -60),
                        new Vector2(0, 150));

                    // Frequency bands analysis
                    ImGui.Spacing();
                    ImGui.Text("Frequency Bands:");
                    ImGui.Columns(3, "FreqBands", true);

                    ImGui.Text("Low (<100 Hz):");
                    ImGui.NextColumn();
                    ImGui.Text("Mid (100-1000 Hz):");
                    ImGui.NextColumn();
                    ImGui.Text("High (>1000 Hz):");
                    ImGui.NextColumn();

                    float lowPower = 0, midPower = 0, highPower = 0;
                    for (var i = 0; i < _frequencyBins.Length; i++)
                    {
                        var power = _frequencySpectrum[i] * _frequencySpectrum[i];
                        if (_frequencyBins[i] < 100) lowPower += power;
                        else if (_frequencyBins[i] < 1000) midPower += power;
                        else highPower += power;
                    }

                    var totalPower = lowPower + midPower + highPower;
                    if (totalPower > 0)
                    {
                        ImGui.Text($"{100 * lowPower / totalPower:F1}%");
                        ImGui.NextColumn();
                        ImGui.Text($"{100 * midPower / totalPower:F1}%");
                        ImGui.NextColumn();
                        ImGui.Text($"{100 * highPower / totalPower:F1}%");
                    }

                    ImGui.Columns(1);
                }

                ImGui.Separator();
                if (ImGui.Button("Export Spectrum as CSV")) ExportFrequencySpectrumCsv();
            }
            else
            {
                ImGui.TextDisabled("No frequency data available");
            }

            ImGui.End();
        }
    }

    private void DrawToolbar()
    {
        // Status bar
        ImGui.Text(_statusMessage);
        ImGui.SameLine(ImGui.GetWindowWidth() - 200);

        // Quick actions
        if (ImGui.Button("Help")) _showHelp = !_showHelp;
        ImGui.SameLine();
        if (ImGui.Button("Reset")) ResetViewer();
    }

    private void DrawControlPanel()
    {
        if (ImGui.BeginTabBar("ControlTabs"))
        {
            if (ImGui.BeginTabItem("Setup"))
            {
                DrawSetupTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Display"))
            {
                DrawDisplayTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Analysis"))
            {
                DrawAnalysisTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Export"))
            {
                DrawExportTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawSetupTab()
    {
        ImGui.TextWrapped("Extract a time-series profile along a line in the volume.");
        ImGui.Spacing();

        // Profile selection
        if (AcousticInteractionManager.InteractionMode == ViewerInteractionMode.DrawingLine)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "Drawing line in viewer...");
            if (ImGui.Button("Cancel")) AcousticInteractionManager.CancelLineDrawing();
        }
        else
        {
            if (ImGui.Button("Select New Profile Line", new Vector2(-1, 0)))
            {
                if (!CheckTimeSeriesAvailable()) return;
                AcousticInteractionManager.StartLineDrawing();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Component selection
        ImGui.Text("Velocity Component:");
        string[] components = { "Magnitude", "X (Radial)", "Y (Tangential)", "Z (Axial)" };
        if (ImGui.Combo("##Component", ref _selectedComponent, components, components.Length))
            if (_linePoints != null && _linePoints.Count > 0)
                _ = ExtractProfileDataAsync(true);

        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Select which velocity component to visualize");

        // Profile info
        if (_profileData != null)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Profile Information:");
            ImGui.Text($"• Points: {_profileData.GetLength(1)}");
            ImGui.Text($"• Time Steps: {_profileData.GetLength(0)}");
            ImGui.Text($"• Length: {_profileLengthMm:F2} mm");
            ImGui.Text($"• Duration: {_timeRangeMs:F3} ms");
            ImGui.Text($"• Max Amplitude: {_maxAbsAmplitude:E2}");
        }
    }

    private void DrawDisplayTab()
    {
        ImGui.Text("B-Scan Visualization Settings");
        ImGui.Separator();

        // Color map
        ImGui.Text("Color Map:");
        if (ImGui.Combo("##ColorMap", ref _colorMapIndex, _colorMapNames, _colorMapNames.Length))
            UpdateProfileTexture();

        // Amplitude controls
        ImGui.Text("Amplitude Scaling:");
        if (ImGui.SliderFloat("Scale", ref _amplitudeScale, 0.1f, 10.0f, "%.1fx")) UpdateProfileTexture();

        ImGui.Checkbox("Logarithmic Scale", ref _useLogarithmicScale);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Apply logarithmic scaling to enhance weak signals");

        ImGui.Checkbox("Auto-Normalize", ref _autoNormalize);

        if (!_autoNormalize)
        {
            ImGui.Text("Manual Range:");
            ImGui.PushItemWidth(100);
            ImGui.InputFloat("Min", ref _manualMin, 0.01f);
            ImGui.SameLine();
            ImGui.InputFloat("Max", ref _manualMax, 0.01f);
            ImGui.PopItemWidth();

            if (ImGui.Button("Apply Range")) UpdateProfileTexture();
        }

        ImGui.Separator();
        ImGui.Checkbox("Show Crosshairs", ref _showCrosshairs);

        // Quick presets
        ImGui.Spacing();
        ImGui.Text("Quick Presets:");
        if (ImGui.Button("Seismic", new Vector2(80, 0)))
        {
            _colorMapIndex = 1;
            _amplitudeScale = 1.0f;
            _useLogarithmicScale = false;
            UpdateProfileTexture();
        }

        ImGui.SameLine();
        if (ImGui.Button("Medical", new Vector2(80, 0)))
        {
            _colorMapIndex = 0;
            _amplitudeScale = 1.5f;
            _useLogarithmicScale = true;
            UpdateProfileTexture();
        }
    }

    private void DrawAnalysisTab()
    {
        ImGui.Text("Interactive Analysis");
        ImGui.Separator();

        if (_profileData == null)
        {
            ImGui.TextDisabled("No profile data available");
            return;
        }

        ImGui.TextWrapped("• Left-click the B-scan to select a time slice\n• Right-click to select a distance point");
        ImGui.Spacing();

        // Waveform at selected time
        if (_selectedTimeIndex >= 0 && _waveformAtTime != null)
        {
            ImGui.Text(_waveformTitle);
            ImGui.PlotLines("##waveform", ref _waveformAtTime[0], _waveformAtTime.Length, 0,
                $"Peak: {_waveformAtTime.Max():F3}", -_maxAbsAmplitude * _amplitudeScale,
                _maxAbsAmplitude * _amplitudeScale, new Vector2(0, 80));

            // Statistics
            var rms = CalculateRMS(_waveformAtTime);
            ImGui.Text($"RMS: {rms:E2}");
        }
        else
        {
            ImGui.TextDisabled("Click on B-scan to select time");
        }

        ImGui.Separator();

        // Time series at selected distance
        if (_selectedDistanceIndex >= 0 && _timeSeriesAtDistance != null)
        {
            ImGui.Text(_timeSeriesTitle);
            ImGui.PlotLines("##timeseries", ref _timeSeriesAtDistance[0], _timeSeriesAtDistance.Length, 0,
                $"Peak: {_timeSeriesAtDistance.Max():F3}", -_maxAbsAmplitude * _amplitudeScale,
                _maxAbsAmplitude * _amplitudeScale, new Vector2(0, 80));

            // Frequency analysis button
            if (ImGui.Button("Analyze Frequency")) AnalyzeFrequency(_timeSeriesAtDistance);
        }
        else
        {
            ImGui.TextDisabled("Right-click on B-scan to select point");
        }
    }

    private void DrawExportTab()
    {
        ImGui.Text("Export Options");
        ImGui.Separator();

        if (_profileData == null)
        {
            ImGui.TextDisabled("No data to export. Select a profile first.");
            return;
        }

        // WAV export settings
        ImGui.Text("Audio Export (WAV):");
        ImGui.InputInt("Sample Rate (Hz)", ref _sampleRate);
        _sampleRate = Math.Clamp(_sampleRate, 8000, 192000);

        ImGui.SliderFloat("Playback Speed", ref _playbackSpeedMultiplier, 0.1f, 10.0f, "%.1fx");
        ImGui.Checkbox("Normalize Audio", ref _normalizeAudio);

        if (ImGui.Button("Export as WAV...", new Vector2(-1, 0)))
        {
            var filename = $"waveform_{DateTime.Now:yyyyMMdd_HHmmss}";
            _exportWavDialog.Open(filename);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Data export
        ImGui.Text("Data Export:");
        if (ImGui.Button("Export Profile as CSV...", new Vector2(-1, 0)))
        {
            var filename = $"profile_{DateTime.Now:yyyyMMdd_HHmmss}";
            _exportCsvDialog.Open(filename);
        }

        if (ImGui.Button("Export B-Scan as Image...", new Vector2(-1, 0)))
        {
            var filename = $"bscan_{DateTime.Now:yyyyMMdd_HHmmss}";
            _exportImageDialog.Open(filename);
        }

        ImGui.Spacing();

        // Quick stats export
        if (ImGui.Button("Copy Statistics to Clipboard")) CopyStatisticsToClipboard();
    }

    private void DrawProfileDisplay()
    {
        ImGui.Text("B-Scan Profile (Time-Distance)");
        ImGui.Separator();

        var canvasPos = ImGui.GetCursorScreenPos();
        var canvasSize = ImGui.GetContentRegionAvail();
        canvasSize.Y = Math.Max(canvasSize.Y - 50, 200);

        if (_isCalculating)
        {
            ImGui.Dummy(canvasSize);
            var center = canvasPos + canvasSize * 0.5f;
            ImGui.SetCursorScreenPos(center - new Vector2(50, 10));
            ImGui.Text("Processing...");
        }
        else if (_profileTexture != null && _profileTexture.IsValid && _profileData != null)
        {
            // Draw the B-scan image
            ImGui.Image(_profileTexture.GetImGuiTextureId(), canvasSize);
            var drawList = ImGui.GetWindowDrawList();

            // Handle mouse interaction
            HandleProfileInteraction(canvasPos, canvasSize);

            // Draw overlays (crosshairs, axes labels)
            if (_showCrosshairs) DrawProfileOverlays(drawList, canvasPos, canvasSize);

            // Draw axis labels
            DrawAxisLabels(drawList, canvasPos, canvasSize);
        }
        else
        {
            ImGui.Dummy(canvasSize);
            var center = canvasPos + canvasSize * 0.5f;
            ImGui.SetCursorScreenPos(center - new Vector2(100, 10));
            ImGui.TextDisabled("Select a profile line to begin");
        }
    }

    private void DrawProfileOverlays(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize)
    {
        // Draw crosshairs at selected positions
        if (_selectedTimeIndex >= 0)
        {
            var y = canvasPos.Y + (float)_selectedTimeIndex / _profileData.GetLength(0) * canvasSize.Y;
            drawList.AddLine(new Vector2(canvasPos.X, y),
                new Vector2(canvasPos.X + canvasSize.X, y),
                ImGui.GetColorU32(new Vector4(0, 1, 1, 0.7f)), 1.0f);
        }

        if (_selectedDistanceIndex >= 0)
        {
            var x = canvasPos.X + (float)_selectedDistanceIndex / _profileData.GetLength(1) * canvasSize.X;
            drawList.AddLine(new Vector2(x, canvasPos.Y),
                new Vector2(x, canvasPos.Y + canvasSize.Y),
                ImGui.GetColorU32(new Vector4(0, 1, 0, 0.7f)), 1.0f);
        }
    }

    private void DrawAxisLabels(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize)
    {
        // Time axis (vertical)
        var timeLabel = $"Time (0 - {_timeRangeMs:F1} ms)";
        drawList.AddText(new Vector2(canvasPos.X - 80, canvasPos.Y + canvasSize.Y / 2),
            ImGui.GetColorU32(ImGuiCol.Text), timeLabel);

        // Distance axis (horizontal)
        var distLabel = $"Distance (0 - {_profileLengthMm:F1} mm)";
        drawList.AddText(new Vector2(canvasPos.X + canvasSize.X / 2 - 50, canvasPos.Y + canvasSize.Y + 5),
            ImGui.GetColorU32(ImGuiCol.Text), distLabel);
    }

    private void HandleProfileInteraction(Vector2 canvasPos, Vector2 canvasSize)
    {
        ImGui.SetCursorScreenPos(canvasPos);
        ImGui.InvisibleButton("##profile_interaction", canvasSize);

        if (ImGui.IsItemHovered())
        {
            var io = ImGui.GetIO();
            var mousePos = io.MousePos - canvasPos;
            var relPos = mousePos / canvasSize;

            // Show tooltip with coordinates
            var timeIdx = (int)(relPos.Y * _profileData.GetLength(0));
            var distIdx = (int)(relPos.X * _profileData.GetLength(1));

            if (timeIdx >= 0 && timeIdx < _profileData.GetLength(0) &&
                distIdx >= 0 && distIdx < _profileData.GetLength(1))
            {
                var time = timeIdx * _timeRangeMs / _profileData.GetLength(0);
                var dist = distIdx * _profileLengthMm / _profileData.GetLength(1);
                var value = _profileData[timeIdx, distIdx];

                ImGui.SetTooltip($"Time: {time:F3} ms\nDistance: {dist:F2} mm\nAmplitude: {value:E2}");
            }

            // Left-click to select time
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                _selectedTimeIndex = Math.Clamp(timeIdx, 0, _profileData.GetLength(0) - 1);
                ExtractWaveformAtTime();
            }

            // Right-click to select distance
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                _selectedDistanceIndex = Math.Clamp(distIdx, 0, _profileData.GetLength(1) - 1);
                ExtractTimeSeriesAtDistance();
            }

            // Scroll to zoom
            if (io.MouseWheel != 0)
            {
                _amplitudeScale *= 1.0f + io.MouseWheel * 0.1f;
                _amplitudeScale = Math.Clamp(_amplitudeScale, 0.1f, 10.0f);
                UpdateProfileTexture();
            }
        }
    }

    private void DrawHelpOverlay()
    {
        ImGui.SetNextWindowPos(ImGui.GetWindowPos() + new Vector2(20, 60));
        ImGui.SetNextWindowSize(new Vector2(400, 300));
        ImGui.Begin("Waveform Viewer Help", ref _showHelp,
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize);

        ImGui.Text("How to Use:");
        ImGui.Separator();
        ImGui.BulletText("Click 'Select New Profile Line' and draw a line in the main viewer");
        ImGui.BulletText("Left-click the B-scan to view waveform at that time");
        ImGui.BulletText("Right-click to view time series at that point");
        ImGui.BulletText("Scroll wheel over B-scan to adjust amplitude scale");

        ImGui.Spacing();
        ImGui.Text("Components:");
        ImGui.Separator();
        ImGui.BulletText("Magnitude: Total velocity magnitude");
        ImGui.BulletText("X/Y/Z: Individual velocity components");

        ImGui.Spacing();
        ImGui.Text("Export Options:");
        ImGui.Separator();
        ImGui.BulletText("WAV: Export as audio file for acoustic analysis");
        ImGui.BulletText("CSV: Export raw numerical data");
        ImGui.BulletText("PNG: Export B-scan visualization");

        ImGui.End();
    }

    private async Task ExtractProfileDataAsync(bool useExistingLine = false)
    {
        if (_isCalculating) return;
        _isCalculating = true;

        _selectedTimeIndex = -1;
        _selectedDistanceIndex = -1;
        _waveformAtTime = null;
        _timeSeriesAtDistance = null;

        _progressDialog.Open("Extracting waveform profile...");

        try
        {
            await Task.Run(() =>
            {
                if (!CheckTimeSeriesAvailable()) return;

                if (!useExistingLine) _linePoints = GetLinePoints();

                if (_linePoints == null || _linePoints.Count == 0)
                {
                    _statusMessage = "Error: Invalid line selection";
                    return;
                }

                ExtractProfileData();
                CalculateProfileMetadata();
                UpdateStatusMessage();
            });

            UpdateProfileTexture();
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

    private void ExtractProfileData()
    {
        var numTimeSteps = _dataset.TimeSeriesSnapshots.Count;
        var numLinePoints = _linePoints.Count;
    
        Logger.Log($"[WaveformViewer] Extracting profile: {numTimeSteps} timesteps × {numLinePoints} points");
    
        _profileData = new float[numTimeSteps, numLinePoints];
        _maxAbsAmplitude = 0;

        for (var t = 0; t < numTimeSteps; t++)
        {
            _progressDialog.Update((float)t / numTimeSteps, $"Processing frame {t + 1}/{numTimeSteps}");
            var snapshot = _dataset.TimeSeriesSnapshots[t];

            Logger.Log($"[WaveformViewer] Snapshot {t}: {snapshot.Width}x{snapshot.Height}x{snapshot.Depth}");

            var vx = snapshot.GetVelocityField(0);
            var vy = snapshot.GetVelocityField(1);
            var vz = snapshot.GetVelocityField(2);

            if (vx == null || vy == null || vz == null)
            {
                Logger.LogWarning($"[WaveformViewer] Snapshot {t} has null velocity fields!");
                continue;
            }

            for (var p = 0; p < numLinePoints; p++)
            {
                var point = _linePoints[p];
                var value = GetVelocityComponent(vx, vy, vz, point);
                _profileData[t, p] = value;

                if (Math.Abs(value) > _maxAbsAmplitude)
                    _maxAbsAmplitude = Math.Abs(value);
            }
        }
    
        Logger.Log($"[WaveformViewer] Profile extraction complete. Max amplitude: {_maxAbsAmplitude:E3}");
    }

    private float GetVelocityComponent(float[,,] vx, float[,,] vy, float[,,] vz, Tuple<int, int, int> point)
    {
        var x = point.Item1;
        var y = point.Item2;
        var z = point.Item3;

        // --- FIX START: Add boundary check ---
        var fieldWidth = vx.GetLength(0);
        var fieldHeight = vx.GetLength(1);
        var fieldDepth = vx.GetLength(2);

        if (x < 0 || x >= fieldWidth || y < 0 || y >= fieldHeight || z < 0 ||
            z >= fieldDepth) return 0.0f; // Return a safe default value if the point is out of bounds for this snapshot
        // --- FIX END ---

        return _selectedComponent switch
        {
            1 => vx[x, y, z],
            2 => vy[x, y, z],
            3 => vz[x, y, z],
            _ => (float)Math.Sqrt(vx[x, y, z] * vx[x, y, z] +
                                  vy[x, y, z] * vy[x, y, z] +
                                  vz[x, y, z] * vz[x, y, z])
        };
    }

    private void CalculateProfileMetadata()
    {
        // Calculate physical length of profile
        if (_linePoints.Count > 1)
        {
            var volume = _dataset.PWaveField ?? _dataset.CombinedWaveField;
            if (volume != null && volume.PixelSize > 0)
            {
                var p1 = _linePoints[0];
                var p2 = _linePoints[_linePoints.Count - 1];
                float dx = p2.Item1 - p1.Item1;
                float dy = p2.Item2 - p1.Item2;
                float dz = p2.Item3 - p1.Item3;
                var pixels = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                _profileLengthMm = pixels * (float)volume.PixelSize * 1000; // Convert to mm
            }
        }

        // Calculate time range
        if (_dataset.TimeSeriesSnapshots.Count > 0)
            _timeRangeMs = (_dataset.TimeSeriesSnapshots.Last().SimulationTime -
                            _dataset.TimeSeriesSnapshots.First().SimulationTime) * 1000;
    }

    private void UpdateStatusMessage()
    {
        if (_profileData != null)
            _statusMessage = $"Profile: {_profileData.GetLength(1)} points × {_profileData.GetLength(0)} frames | " +
                             $"{_profileLengthMm:F1}mm × {_timeRangeMs:F2}ms";
        else if (_dataset.TimeSeriesSnapshots == null || _dataset.TimeSeriesSnapshots.Count == 0)
            _statusMessage = "No time-series data available";
        else
            _statusMessage = "Ready - Select a profile line to begin";
    }

    private bool CheckTimeSeriesAvailable()
    {
        if (_dataset.TimeSeriesSnapshots == null || _dataset.TimeSeriesSnapshots.Count < 2)
        {
            _statusMessage = "Error: Insufficient time-series data";
            Logger.LogWarning("[WaveformViewer] No time-series data available");
            return false;
        }

        return true;
    }

    private List<Tuple<int, int, int>> GetLinePoints()
{
    var volume = _dataset.PWaveField ?? _dataset.CombinedWaveField;
    if (volume == null) return null;

    var x1_2d = (int)AcousticInteractionManager.LineStartPoint.X;
    var y1_2d = (int)AcousticInteractionManager.LineStartPoint.Y;
    var x2_2d = (int)AcousticInteractionManager.LineEndPoint.X;
    var y2_2d = (int)AcousticInteractionManager.LineEndPoint.Y;
    var slice_coord = AcousticInteractionManager.LineSliceIndex;
    var viewIndex = AcousticInteractionManager.LineViewIndex;

    var points = new List<Tuple<int, int, int>>();

    // FIX: Clamp coordinates to valid range BEFORE Bresenham
    x1_2d = Math.Clamp(x1_2d, 0, volume.Width - 1);
    y1_2d = Math.Clamp(y1_2d, 0, volume.Height - 1);
    x2_2d = Math.Clamp(x2_2d, 0, volume.Width - 1);
    y2_2d = Math.Clamp(y2_2d, 0, volume.Height - 1);
    
    Logger.Log($"[WaveformViewer] Line from ({x1_2d},{y1_2d}) to ({x2_2d},{y2_2d}) on view {viewIndex}, slice {slice_coord}");
    Logger.Log($"[WaveformViewer] Volume dimensions: {volume.Width}x{volume.Height}x{volume.Depth}");

    // Bresenham's line algorithm
    int dx = Math.Abs(x2_2d - x1_2d), sx = x1_2d < x2_2d ? 1 : -1;
    int dy = -Math.Abs(y2_2d - y1_2d), sy = y1_2d < y2_2d ? 1 : -1;
    int err = dx + dy, e2;

    while (true)
    {
        int x_vol, y_vol, z_vol;
        switch (viewIndex)
        {
            case 0: // XY View
                x_vol = x1_2d;
                y_vol = y1_2d;
                z_vol = slice_coord;
                break;
            case 1: // XZ View
                x_vol = x1_2d;
                y_vol = slice_coord;
                z_vol = y1_2d;
                break;
            case 2: // YZ View
                x_vol = slice_coord;
                y_vol = x1_2d;
                z_vol = y1_2d;
                break;
            default:
                Logger.LogError($"[WaveformViewer] Invalid view index: {viewIndex}");
                return points;
        }

        // Validate and add point
        if (x_vol >= 0 && x_vol < volume.Width &&
            y_vol >= 0 && y_vol < volume.Height &&
            z_vol >= 0 && z_vol < volume.Depth)
        {
            points.Add(new Tuple<int, int, int>(x_vol, y_vol, z_vol));
        }

        if (x1_2d == x2_2d && y1_2d == y2_2d) break;
        e2 = 2 * err;
        if (e2 >= dy) { err += dy; x1_2d += sx; }
        if (e2 <= dx) { err += dx; y1_2d += sy; }
    }

    Logger.Log($"[WaveformViewer] Extracted {points.Count} points along line");
    return points;
}

    private void ExtractWaveformAtTime()
    {
        if (_profileData == null || _selectedTimeIndex < 0) return;

        var distancePoints = _profileData.GetLength(1);
        _waveformAtTime = new float[distancePoints];

        for (var i = 0; i < distancePoints; i++) _waveformAtTime[i] = _profileData[_selectedTimeIndex, i];

        var time = _selectedTimeIndex * _timeRangeMs / _profileData.GetLength(0);
        _waveformTitle = $"Spatial Waveform at t = {time:F3} ms";
    }

    private void ExtractTimeSeriesAtDistance()
    {
        if (_profileData == null || _selectedDistanceIndex < 0) return;

        var timePoints = _profileData.GetLength(0);
        _timeSeriesAtDistance = new float[timePoints];

        for (var i = 0; i < timePoints; i++) _timeSeriesAtDistance[i] = _profileData[i, _selectedDistanceIndex];

        var dist = _selectedDistanceIndex * _profileLengthMm / _profileData.GetLength(1);
        _timeSeriesTitle = $"Time Series at {dist:F2} mm";
    }

    private void UpdateProfileTexture()
    {
        if (_profileData == null) return;

        var height = _profileData.GetLength(0);
        var width = _profileData.GetLength(1);
        var rgbaData = new byte[width * height * 4];

        var displayMax = _autoNormalize ? _maxAbsAmplitude : _manualMax;
        var displayMin = _autoNormalize ? -_maxAbsAmplitude : _manualMin;
        var range = displayMax - displayMin;

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var value = _profileData[y, x] * _amplitudeScale;

            if (_useLogarithmicScale && value != 0)
            {
                float sign = Math.Sign(value);
                value = sign * (float)Math.Log10(Math.Abs(value) + 1);
            }

            var normalized = (value - displayMin) / range;
            normalized = Math.Clamp(normalized, 0, 1);

            var color = GetColorMapValue(normalized);
            var index = (y * width + x) * 4;
            rgbaData[index] = (byte)(color.X * 255);
            rgbaData[index + 1] = (byte)(color.Y * 255);
            rgbaData[index + 2] = (byte)(color.Z * 255);
            rgbaData[index + 3] = 255;
        }

        _profileTexture?.Dispose();
        _profileTexture = TextureManager.CreateFromPixelData(rgbaData, (uint)width, (uint)height);
    }

    private Vector4 GetColorMapValue(float v)
    {
        v = Math.Clamp(v, 0, 1);

        return _colorMapIndex switch
        {
            0 => new Vector4(v, v, v, 1), // Grayscale
            1 => GetSeismicColor(v), // Seismic
            2 => GetJetColor(v), // Jet
            3 => GetHotColor(v), // Hot
            4 => GetCoolColor(v), // Cool
            5 => GetViridisColor(v), // Viridis
            _ => new Vector4(v, v, v, 1)
        };
    }

    #region Color Map Functions

    private Vector4 GetSeismicColor(float v)
    {
        // Blue -> White -> Red (good for signed data)
        if (v < 0.5f)
        {
            var t = v * 2.0f;
            return new Vector4(t, t, 1.0f, 1.0f);
        }
        else
        {
            var t = (v - 0.5f) * 2.0f;
            return new Vector4(1.0f, 1.0f - t, 1.0f - t, 1.0f);
        }
    }

    private Vector4 GetJetColor(float v)
    {
        if (v < 0.125f)
            return new Vector4(0, 0, 0.5f + 4 * v, 1);
        if (v < 0.375f)
            return new Vector4(0, 4 * (v - 0.125f), 1, 1);
        if (v < 0.625f)
            return new Vector4(4 * (v - 0.375f), 1, 1 - 4 * (v - 0.375f), 1);
        if (v < 0.875f)
            return new Vector4(1, 1 - 4 * (v - 0.625f), 0, 1);
        return new Vector4(1 - 4 * (v - 0.875f), 0, 0, 1);
    }

    private Vector4 GetHotColor(float v)
    {
        var r = Math.Clamp(v / 0.4f, 0, 1);
        var g = Math.Clamp((v - 0.4f) / 0.4f, 0, 1);
        var b = Math.Clamp((v - 0.8f) / 0.2f, 0, 1);
        return new Vector4(r, g, b, 1);
    }

    private Vector4 GetCoolColor(float v)
    {
        return new Vector4(v, 1 - v, 1, 1);
    }

    private Vector4 GetViridisColor(float t)
    {
        var r = 0.267f + t * (2.053f - t * (29.256f - t * (127.357f - t * (214.534f - t * 128.389f))));
        var g = 0.005f + t * (1.107f + t * (4.295f - t * (4.936f + t * (-7.422f + t * 4.025f))));
        var b = 0.329f + t * (0.460f - t * (5.581f + t * (27.207f - t * (50.113f + t * 28.189f))));
        return new Vector4(Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1), 1);
    }

    #endregion

    #region Export Functions

    private void HandleExportDialogs()
    {
        // WAV export
        if (_exportWavDialog.Submit()) _ = ExportAsWavAsync(_exportWavDialog.SelectedPath);

        // CSV export
        if (_exportCsvDialog.Submit()) _ = ExportAsCsvAsync(_exportCsvDialog.SelectedPath);

        // Image export
        if (_exportImageDialog.Submit()) _ = ExportAsImageAsync(_exportImageDialog.SelectedPath);
    }

    private async Task ExportAsWavAsync(string path)
    {
        if (_selectedDistanceIndex < 0 || _timeSeriesAtDistance == null)
        {
            Logger.LogWarning("[WaveformViewer] No time series selected for WAV export");
            return;
        }

        _progressDialog.Open("Exporting WAV file...");

        try
        {
            await Task.Run(() =>
            {
                // Resample data to target sample rate
                var originalSamples = _timeSeriesAtDistance.Length;
                var duration = _timeRangeMs / 1000.0f / _playbackSpeedMultiplier;
                var targetSamples = (int)(duration * _sampleRate);

                var resampled = new float[targetSamples];
                for (var i = 0; i < targetSamples; i++)
                {
                    var srcIndex = (float)i / targetSamples * originalSamples;
                    var idx = (int)srcIndex;
                    var frac = srcIndex - idx;

                    if (idx < originalSamples - 1)
                        resampled[i] = _timeSeriesAtDistance[idx] * (1 - frac) +
                                       _timeSeriesAtDistance[idx + 1] * frac;
                    else
                        resampled[i] = _timeSeriesAtDistance[originalSamples - 1];
                }

                // Normalize if requested
                if (_normalizeAudio)
                {
                    var max = resampled.Max(Math.Abs);
                    if (max > 0)
                        for (var i = 0; i < resampled.Length; i++)
                            resampled[i] /= max;
                }

                // Write WAV file
                WriteWavFile(path, resampled, _sampleRate);
            });

            Logger.Log($"[WaveformViewer] Exported WAV to {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[WaveformViewer] Failed to export WAV: {ex.Message}");
        }
        finally
        {
            _progressDialog.Close();
        }
    }

    private void WriteWavFile(string path, float[] data, int sampleRate)
    {
        using (var fs = new FileStream(path, FileMode.Create))
        using (var writer = new BinaryWriter(fs))
        {
            // WAV header
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + data.Length * 2); // File size - 8
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16); // Subchunk size
            writer.Write((short)1); // PCM format
            writer.Write((short)1); // Mono
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2); // Byte rate
            writer.Write((short)2); // Block align
            writer.Write((short)16); // Bits per sample
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(data.Length * 2);

            // Convert float to 16-bit PCM
            foreach (var sample in data)
            {
                var pcm = (short)(Math.Clamp(sample, -1, 1) * 32767);
                writer.Write(pcm);
            }
        }
    }

    private async Task ExportAsCsvAsync(string path)
    {
        if (_profileData == null) return;

        _progressDialog.Open("Exporting CSV file...");

        try
        {
            await Task.Run(() =>
            {
                using (var writer = new StreamWriter(path))
                {
                    // Header
                    writer.Write("Time_ms");
                    for (var d = 0; d < _profileData.GetLength(1); d++)
                    {
                        var dist = d * _profileLengthMm / _profileData.GetLength(1);
                        writer.Write($",Dist_{dist:F2}mm");
                    }

                    writer.WriteLine();

                    // Data
                    for (var t = 0; t < _profileData.GetLength(0); t++)
                    {
                        var time = t * _timeRangeMs / _profileData.GetLength(0);
                        writer.Write($"{time:F4}");

                        for (var d = 0; d < _profileData.GetLength(1); d++) writer.Write($",{_profileData[t, d]:E6}");
                        writer.WriteLine();

                        _progressDialog.Update((float)t / _profileData.GetLength(0),
                            $"Writing row {t + 1}/{_profileData.GetLength(0)}");
                    }
                }
            });

            Logger.Log($"[WaveformViewer] Exported CSV to {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[WaveformViewer] Failed to export CSV: {ex.Message}");
        }
        finally
        {
            _progressDialog.Close();
        }
    }

    private async Task ExportAsImageAsync(string path)
    {
        if (_profileTexture == null || !_profileTexture.IsValid || _profileData == null)
        {
            Logger.LogWarning("[WaveformViewer] No profile texture available for export");
            return;
        }

        _progressDialog.Open("Exporting B-Scan image...");

        try
        {
            await Task.Run(() =>
            {
                var width = _profileData.GetLength(1);
                var height = _profileData.GetLength(0);

                // Generate the image data
                var rgbaData = new byte[width * height * 4];

                var displayMax = _autoNormalize ? _maxAbsAmplitude : _manualMax;
                var displayMin = _autoNormalize ? -_maxAbsAmplitude : _manualMin;
                var range = displayMax - displayMin;

                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var value = _profileData[y, x] * _amplitudeScale;

                        if (_useLogarithmicScale && value != 0)
                        {
                            float sign = Math.Sign(value);
                            value = sign * (float)Math.Log10(Math.Abs(value) + 1);
                        }

                        var normalized = (value - displayMin) / range;
                        normalized = Math.Clamp(normalized, 0, 1);

                        var color = GetColorMapValue(normalized);
                        var index = (y * width + x) * 4;
                        rgbaData[index] = (byte)(color.X * 255);
                        rgbaData[index + 1] = (byte)(color.Y * 255);
                        rgbaData[index + 2] = (byte)(color.Z * 255);
                        rgbaData[index + 3] = 255;
                    }

                    _progressDialog.Update((float)y / height, $"Rendering row {y + 1}/{height}");
                }

                // Save using StbImageWriteSharp
                using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    var writer = new ImageWriter();
                    writer.WritePng(rgbaData, width, height,
                        ColorComponents.RedGreenBlueAlpha, stream);
                }
            });

            Logger.Log($"[WaveformViewer] Exported B-Scan image to {path}");

            // Optionally, also save metadata
            var metadataPath = Path.ChangeExtension(path, ".txt");
            await SaveImageMetadata(metadataPath);
        }
        catch (Exception ex)
        {
            Logger.LogError($"[WaveformViewer] Failed to export image: {ex.Message}");
        }
        finally
        {
            _progressDialog.Close();
        }
    }

    private async Task SaveImageMetadata(string path)
    {
        try
        {
            using (var writer = new StreamWriter(path))
            {
                await writer.WriteLineAsync("=== B-Scan Profile Metadata ===");
                await writer.WriteLineAsync($"Dataset: {_dataset.Name}");
                await writer.WriteLineAsync($"Export Date: {DateTime.Now}");
                await writer.WriteLineAsync($"Profile Length: {_profileLengthMm:F2} mm");
                await writer.WriteLineAsync($"Time Duration: {_timeRangeMs:F3} ms");
                await writer.WriteLineAsync($"Spatial Points: {_profileData.GetLength(1)}");
                await writer.WriteLineAsync($"Time Steps: {_profileData.GetLength(0)}");
                await writer.WriteLineAsync($"Component: {new[] { "Magnitude", "X", "Y", "Z" }[_selectedComponent]}");
                await writer.WriteLineAsync($"Color Map: {_colorMapNames[_colorMapIndex]}");
                await writer.WriteLineAsync($"Amplitude Scale: {_amplitudeScale:F2}x");
                await writer.WriteLineAsync($"Logarithmic Scale: {_useLogarithmicScale}");
                await writer.WriteLineAsync($"Max Amplitude: {_maxAbsAmplitude:E3}");

                if (_linePoints != null && _linePoints.Count > 1)
                {
                    var start = _linePoints[0];
                    var end = _linePoints[_linePoints.Count - 1];
                    await writer.WriteLineAsync($"Profile Start: ({start.Item1}, {start.Item2}, {start.Item3})");
                    await writer.WriteLineAsync($"Profile End: ({end.Item1}, {end.Item2}, {end.Item3})");
                }
            }

            Logger.Log($"[WaveformViewer] Saved image metadata to {path}");
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[WaveformViewer] Failed to save metadata: {ex.Message}");
        }
    }

    private void CopyStatisticsToClipboard()
    {
        if (_profileData == null) return;

        var sb = new StringBuilder();
        sb.AppendLine("=== Waveform Profile Statistics ===");
        sb.AppendLine($"Profile Length: {_profileLengthMm:F2} mm");
        sb.AppendLine($"Time Duration: {_timeRangeMs:F3} ms");
        sb.AppendLine($"Spatial Points: {_profileData.GetLength(1)}");
        sb.AppendLine($"Time Steps: {_profileData.GetLength(0)}");
        sb.AppendLine($"Component: {new[] { "Magnitude", "X", "Y", "Z" }[_selectedComponent]}");
        sb.AppendLine($"Max Amplitude: {_maxAbsAmplitude:E3}");

        if (_waveformAtTime != null)
        {
            sb.AppendLine("\n--- Selected Waveform ---");
            sb.AppendLine($"RMS: {CalculateRMS(_waveformAtTime):E3}");
            sb.AppendLine($"Peak: {_waveformAtTime.Max(Math.Abs):E3}");
        }

        ImGui.SetClipboardText(sb.ToString());
        Logger.Log("[WaveformViewer] Statistics copied to clipboard");
    }

    private float CalculateRMS(float[] data)
    {
        if (data == null || data.Length == 0) return 0;
        double sum = 0;
        foreach (var v in data)
            sum += v * v;
        return (float)Math.Sqrt(sum / data.Length);
    }

    private void AnalyzeFrequency(float[] timeSeries)
    {
        if (timeSeries == null || timeSeries.Length < 2)
        {
            Logger.LogWarning("[WaveformViewer] Time series too short for frequency analysis");
            return;
        }

        // Perform DFT analysis
        var n = timeSeries.Length;
        var spectrum = new Complex[n];

        // Convert to complex numbers
        var complexData = timeSeries.Select(val => new Complex(val, 0)).ToArray();

        // Perform the DFT
        for (var k = 0; k < n / 2; k++) // Only compute first half due to symmetry
        {
            spectrum[k] = new Complex(0, 0);
            for (var j = 0; j < n; j++)
            {
                var angle = 2 * Math.PI * k * j / n;
                spectrum[k] += complexData[j] * Complex.Exp(new Complex(0, -angle));
            }
        }

        // Calculate magnitude spectrum
        var halfN = n / 2;
        _frequencySpectrum = new float[halfN];
        _frequencyBins = new float[halfN];

        // Calculate frequency bins (assuming time steps are evenly spaced)
        var samplingRate = 1000.0f / (_timeRangeMs / timeSeries.Length); // Hz
        _nyquistFrequency = samplingRate / 2.0f;

        float maxMagnitude = 0;
        var dominantIndex = 0;

        for (var i = 0; i < halfN; i++)
        {
            _frequencySpectrum[i] = (float)spectrum[i].Magnitude / n;
            _frequencyBins[i] = i * samplingRate / n;

            // Skip DC component when finding dominant frequency
            if (i > 0 && _frequencySpectrum[i] > maxMagnitude)
            {
                maxMagnitude = _frequencySpectrum[i];
                dominantIndex = i;
            }
        }

        _dominantFrequency = _frequencyBins[dominantIndex];
        _showFrequencyAnalysis = true;

        Logger.Log($"[WaveformViewer] Frequency analysis complete. Dominant frequency: {_dominantFrequency:F2} Hz");
    }

    private void ExportFrequencySpectrumCsv()
    {
        if (_frequencySpectrum == null || _frequencyBins == null) return;

        var fileName = $"frequency_spectrum_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);

        try
        {
            using (var writer = new StreamWriter(path))
            {
                writer.WriteLine("Frequency_Hz,Magnitude,Phase_deg,Power_dB");
                for (var i = 0; i < _frequencySpectrum.Length; i++)
                {
                    var powerDb = 20.0f * (float)Math.Log10(Math.Max(_frequencySpectrum[i], 1e-12f));
                    writer.WriteLine($"{_frequencyBins[i]:F3},{_frequencySpectrum[i]:E6},0,{powerDb:F2}");
                }
            }

            Logger.Log($"[WaveformViewer] Exported frequency spectrum to {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[WaveformViewer] Failed to export spectrum: {ex.Message}");
        }
    }

    private void ResetViewer()
    {
        _selectedTimeIndex = -1;
        _selectedDistanceIndex = -1;
        _waveformAtTime = null;
        _timeSeriesAtDistance = null;
        _amplitudeScale = 1.0f;
        _colorMapIndex = 2;
        UpdateProfileTexture();
    }

    #endregion
}