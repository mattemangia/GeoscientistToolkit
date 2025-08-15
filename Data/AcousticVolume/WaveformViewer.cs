// GeoscientistToolkit/UI/AcousticVolume/WaveformViewer.cs
using System;
using System.Collections.Generic;
using System.IO;
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
        private readonly ImGuiExportFileDialog _exportDialog;
        private readonly ProgressBarDialog _progressDialog;
        
        // Waveform extraction settings
        private Vector3 _sourcePoint = new Vector3(0, 0.5f, 0.5f);
        private Vector3 _receiverPoint = new Vector3(1, 0.5f, 0.5f);
        private bool _useNormalizedCoords = true;
        private int _selectedComponent = 0; // 0=Magnitude, 1=X, 2=Y, 3=Z
        
        // Extracted waveform data
        private float[] _waveformData;
        private float _sampleRate = 44100.0f; // Default audio sample rate
        private float _maxAmplitude = 0;
        private float _minAmplitude = 0;
        
        // Display settings
        private float _zoomLevel = 1.0f;
        private float _panOffset = 0.0f;
        private bool _autoScale = true;
        private bool _showGrid = true;
        private bool _showEnvelope = false;
        private int _displayMode = 0; // 0=Line, 1=Filled, 2=Points
        
        // Playback (optional, if audio playback is implemented)
        private bool _isPlaying = false;
        private float _playbackPosition = 0.0f;
        
        // Export settings
        private int _bitDepth = 16; // 16 or 24 bit
        private bool _normalizeOnExport = true;
        
        public WaveformViewer(AcousticVolumeDataset dataset)
        {
            _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
            _exportDialog = new ImGuiExportFileDialog("WaveformExport", "Export Waveform as WAV");
            _exportDialog.SetExtensions((".wav", "WAV Audio File"));
            _progressDialog = new ProgressBarDialog("Extracting Waveform");
            
            // Calculate sample rate based on simulation parameters
            if (_dataset.TimeSeriesSnapshots?.Count > 1)
            {
                var firstSnapshot = _dataset.TimeSeriesSnapshots[0];
                var lastSnapshot = _dataset.TimeSeriesSnapshots[_dataset.TimeSeriesSnapshots.Count - 1];
                float duration = lastSnapshot.SimulationTime - firstSnapshot.SimulationTime;
                if (duration > 0)
                {
                    // Adjust sample rate based on source frequency
                    _sampleRate = _dataset.SourceFrequencyKHz * 1000 * 10; // 10x oversampling
                    _sampleRate = Math.Clamp(_sampleRate, 8000, 192000);
                }
            }
        }
        
        public void Draw()
        {
            if (ImGui.Begin("Waveform Viewer"))
            {
                DrawControls();
                ImGui.Separator();
                DrawWaveformDisplay();
                ImGui.Separator();
                DrawExportControls();
            }
            ImGui.End();
            
            // Handle dialogs
            HandleDialogs();
        }
        
        private void DrawControls()
        {
            // Point selection
            if (ImGui.CollapsingHeader("Waveform Extraction Points", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Checkbox("Use Normalized Coordinates (0-1)", ref _useNormalizedCoords);
                
                if (_useNormalizedCoords)
                {
                    ImGui.Text("Source Point (0-1):");
                    ImGui.DragFloat3("##SourceNorm", ref _sourcePoint, 0.01f, 0.0f, 1.0f);
                    
                    ImGui.Text("Receiver Point (0-1):");
                    ImGui.DragFloat3("##ReceiverNorm", ref _receiverPoint, 0.01f, 0.0f, 1.0f);
                }
                else
                {
                    var volume = _dataset.CombinedWaveField ?? _dataset.PWaveField ?? _dataset.SWaveField;
                    if (volume != null)
                    {
                        ImGui.Text($"Source Point (0-{volume.Width}, 0-{volume.Height}, 0-{volume.Depth}):");
                        ImGui.DragFloat3("##SourceAbs", ref _sourcePoint, 1.0f, 0.0f, Math.Max(volume.Width, Math.Max(volume.Height, volume.Depth)));
                        
                        ImGui.Text($"Receiver Point (0-{volume.Width}, 0-{volume.Height}, 0-{volume.Depth}):");
                        ImGui.DragFloat3("##ReceiverAbs", ref _receiverPoint, 1.0f, 0.0f, Math.Max(volume.Width, Math.Max(volume.Height, volume.Depth)));
                    }
                }
                
                ImGui.Spacing();
                
                ImGui.Text("Component:");
                string[] components = { "Magnitude", "X Component", "Y Component", "Z Component" };
                ImGui.Combo("##Component", ref _selectedComponent, components, components.Length);
                
                ImGui.Spacing();
                
                if (ImGui.Button("Extract Waveform", new Vector2(-1, 0)))
                {
                    _ = ExtractWaveformAsync();
                }
            }
            
            // Display settings
            if (ImGui.CollapsingHeader("Display Settings"))
            {
                ImGui.Text("Display Mode:");
                string[] modes = { "Line", "Filled", "Points" };
                ImGui.RadioButton("Line", ref _displayMode, 0);
                ImGui.SameLine();
                ImGui.RadioButton("Filled", ref _displayMode, 1);
                ImGui.SameLine();
                ImGui.RadioButton("Points", ref _displayMode, 2);
                
                ImGui.Checkbox("Auto Scale", ref _autoScale);
                ImGui.Checkbox("Show Grid", ref _showGrid);
                ImGui.Checkbox("Show Envelope", ref _showEnvelope);
                
                ImGui.DragFloat("Zoom", ref _zoomLevel, 0.01f, 0.1f, 10.0f);
                ImGui.DragFloat("Pan", ref _panOffset, 1.0f);
                
                if (ImGui.Button("Reset View"))
                {
                    _zoomLevel = 1.0f;
                    _panOffset = 0.0f;
                }
            }
        }
        
        private void DrawWaveformDisplay()
        {
            var drawList = ImGui.GetWindowDrawList();
            var canvasPos = ImGui.GetCursorScreenPos();
            var canvasSize = ImGui.GetContentRegionAvail();
            
            if (canvasSize.Y < 200)
                canvasSize.Y = 200;
            
            // Background
            drawList.AddRectFilled(canvasPos, canvasPos + canvasSize, ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 1.0f)));
            
            if (_waveformData == null || _waveformData.Length == 0)
            {
                var text = "No waveform data. Click 'Extract Waveform' to generate.";
                var textSize = ImGui.CalcTextSize(text);
                var textPos = canvasPos + (canvasSize - textSize) * 0.5f;
                drawList.AddText(textPos, ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 1.0f)), text);
                
                ImGui.InvisibleButton("##WaveformCanvas", canvasSize);
                return;
            }
            
            // Draw grid
            if (_showGrid)
            {
                DrawGrid(drawList, canvasPos, canvasSize);
            }
            
            // Draw waveform
            DrawWaveform(drawList, canvasPos, canvasSize);
            
            // Draw envelope if enabled
            if (_showEnvelope)
            {
                DrawEnvelope(drawList, canvasPos, canvasSize);
            }
            
            // Draw axes and labels
            DrawAxes(drawList, canvasPos, canvasSize);
            
            // Handle mouse interaction
            ImGui.InvisibleButton("##WaveformCanvas", canvasSize);
            if (ImGui.IsItemHovered())
            {
                var io = ImGui.GetIO();
                if (io.MouseWheel != 0)
                {
                    float zoomDelta = io.MouseWheel * 0.1f;
                    _zoomLevel = Math.Clamp(_zoomLevel + zoomDelta * _zoomLevel, 0.1f, 10.0f);
                }
                
                if (ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
                {
                    _panOffset += io.MouseDelta.X;
                }
                
                // Show value at cursor
                if (_waveformData != null)
                {
                    var mousePos = io.MousePos - canvasPos;
                    int sampleIndex = (int)((mousePos.X - _panOffset) / _zoomLevel * _waveformData.Length / canvasSize.X);
                    if (sampleIndex >= 0 && sampleIndex < _waveformData.Length)
                    {
                        float time = sampleIndex / _sampleRate;
                        float value = _waveformData[sampleIndex];
                        ImGui.SetTooltip($"Time: {time:F6} s\nValue: {value:F6}\nSample: {sampleIndex}");
                    }
                }
            }
        }
        
        private void DrawGrid(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize)
        {
            uint gridColor = ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 0.3f));
            uint gridColorMajor = ImGui.GetColorU32(new Vector4(0.4f, 0.4f, 0.4f, 0.5f));
            
            // Vertical grid lines (time)
            int divisions = 10;
            for (int i = 0; i <= divisions; i++)
            {
                float x = canvasPos.X + (canvasSize.X / divisions) * i;
                uint color = (i % 5 == 0) ? gridColorMajor : gridColor;
                drawList.AddLine(new Vector2(x, canvasPos.Y), new Vector2(x, canvasPos.Y + canvasSize.Y), color);
            }
            
            // Horizontal grid lines (amplitude)
            for (int i = 0; i <= divisions; i++)
            {
                float y = canvasPos.Y + (canvasSize.Y / divisions) * i;
                uint color = (i == divisions / 2) ? gridColorMajor : gridColor;
                drawList.AddLine(new Vector2(canvasPos.X, y), new Vector2(canvasPos.X + canvasSize.X, y), color);
            }
        }
        
        private void DrawWaveform(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize)
        {
            if (_waveformData == null || _waveformData.Length < 2) return;
            
            uint waveformColor = ImGui.GetColorU32(new Vector4(0.2f, 0.8f, 0.2f, 1.0f));
            uint fillColor = ImGui.GetColorU32(new Vector4(0.2f, 0.8f, 0.2f, 0.3f));
            
            float centerY = canvasPos.Y + canvasSize.Y * 0.5f;
            float scaleY = _autoScale ? (canvasSize.Y * 0.4f / Math.Max(Math.Abs(_maxAmplitude), Math.Abs(_minAmplitude))) : canvasSize.Y * 0.4f;
            
            int startSample = Math.Max(0, (int)(-_panOffset / _zoomLevel * _waveformData.Length / canvasSize.X));
            int endSample = Math.Min(_waveformData.Length, (int)((canvasSize.X - _panOffset) / _zoomLevel * _waveformData.Length / canvasSize.X));
            
            if (endSample <= startSample) return;
            
            int step = Math.Max(1, (endSample - startSample) / (int)(canvasSize.X * 2));
            
            List<Vector2> points = new List<Vector2>();
            
            for (int i = startSample; i < endSample; i += step)
            {
                float x = canvasPos.X + _panOffset + (float)i / _waveformData.Length * canvasSize.X * _zoomLevel;
                float y = centerY - _waveformData[i] * scaleY;
                points.Add(new Vector2(x, y));
            }
            
            switch (_displayMode)
            {
                case 0: // Line
                    for (int i = 0; i < points.Count - 1; i++)
                    {
                        drawList.AddLine(points[i], points[i + 1], waveformColor, 1.5f);
                    }
                    break;
                    
                case 1: // Filled
                    for (int i = 0; i < points.Count - 1; i++)
                    {
                        drawList.AddQuadFilled(
                            points[i],
                            points[i + 1],
                            new Vector2(points[i + 1].X, centerY),
                            new Vector2(points[i].X, centerY),
                            fillColor);
                    }
                    for (int i = 0; i < points.Count - 1; i++)
                    {
                        drawList.AddLine(points[i], points[i + 1], waveformColor, 1.0f);
                    }
                    break;
                    
                case 2: // Points
                    foreach (var point in points)
                    {
                        drawList.AddCircleFilled(point, 2.0f, waveformColor);
                    }
                    break;
            }
        }
        
        private void DrawEnvelope(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize)
        {
            if (_waveformData == null || _waveformData.Length < 2) return;
            
            uint envelopeColor = ImGui.GetColorU32(new Vector4(0.8f, 0.8f, 0.2f, 0.7f));
            
            // Calculate envelope using Hilbert transform approximation
            float[] envelope = CalculateEnvelope(_waveformData);
            
            float centerY = canvasPos.Y + canvasSize.Y * 0.5f;
            float scaleY = _autoScale ? (canvasSize.Y * 0.4f / Math.Max(Math.Abs(_maxAmplitude), Math.Abs(_minAmplitude))) : canvasSize.Y * 0.4f;
            
            int startSample = Math.Max(0, (int)(-_panOffset / _zoomLevel * envelope.Length / canvasSize.X));
            int endSample = Math.Min(envelope.Length, (int)((canvasSize.X - _panOffset) / _zoomLevel * envelope.Length / canvasSize.X));
            
            if (endSample <= startSample) return;
            
            int step = Math.Max(1, (endSample - startSample) / (int)(canvasSize.X * 2));
            
            for (int i = startSample; i < endSample - step; i += step)
            {
                float x1 = canvasPos.X + _panOffset + (float)i / envelope.Length * canvasSize.X * _zoomLevel;
                float x2 = canvasPos.X + _panOffset + (float)(i + step) / envelope.Length * canvasSize.X * _zoomLevel;
                float y1 = centerY - envelope[i] * scaleY;
                float y2 = centerY - envelope[i + step] * scaleY;
                
                drawList.AddLine(new Vector2(x1, y1), new Vector2(x2, y2), envelopeColor, 2.0f);
                
                // Also draw negative envelope
                y1 = centerY + envelope[i] * scaleY;
                y2 = centerY + envelope[i + step] * scaleY;
                drawList.AddLine(new Vector2(x1, y1), new Vector2(x2, y2), envelopeColor, 2.0f);
            }
        }
        
        private void DrawAxes(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize)
        {
            uint axisColor = ImGui.GetColorU32(new Vector4(0.7f, 0.7f, 0.7f, 1.0f));
            uint textColor = ImGui.GetColorU32(new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
            
            // Center line
            float centerY = canvasPos.Y + canvasSize.Y * 0.5f;
            drawList.AddLine(new Vector2(canvasPos.X, centerY), new Vector2(canvasPos.X + canvasSize.X, centerY), axisColor, 1.0f);
            
            // Left axis
            drawList.AddLine(canvasPos, new Vector2(canvasPos.X, canvasPos.Y + canvasSize.Y), axisColor, 1.0f);
            
            // Bottom axis
            drawList.AddLine(new Vector2(canvasPos.X, canvasPos.Y + canvasSize.Y), 
                           new Vector2(canvasPos.X + canvasSize.X, canvasPos.Y + canvasSize.Y), axisColor, 1.0f);
            
            // Labels
            if (_waveformData != null && _waveformData.Length > 0)
            {
                float duration = _waveformData.Length / _sampleRate;
                
                // Time labels
                drawList.AddText(new Vector2(canvasPos.X + 5, canvasPos.Y + canvasSize.Y - 20), textColor, "0 s");
                drawList.AddText(new Vector2(canvasPos.X + canvasSize.X - 50, canvasPos.Y + canvasSize.Y - 20), textColor, $"{duration:F3} s");
                
                // Amplitude labels
                if (_autoScale)
                {
                    drawList.AddText(new Vector2(canvasPos.X + 5, canvasPos.Y + 5), textColor, $"{_maxAmplitude:F3}");
                    drawList.AddText(new Vector2(canvasPos.X + 5, canvasPos.Y + canvasSize.Y - 20), textColor, $"{_minAmplitude:F3}");
                }
            }
        }
        
        private void DrawExportControls()
        {
            if (_waveformData == null || _waveformData.Length == 0)
            {
                ImGui.TextDisabled("Extract a waveform first to enable export");
                return;
            }
            
            ImGui.Text("Export Settings:");
            
            ImGui.Text("Sample Rate:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);
            ImGui.InputFloat("##SampleRate", ref _sampleRate, 100, 1000);
            _sampleRate = Math.Clamp(_sampleRate, 8000, 192000);
            
            ImGui.Text("Bit Depth:");
            ImGui.SameLine();
            ImGui.RadioButton("16-bit", ref _bitDepth, 16);
            ImGui.SameLine();
            ImGui.RadioButton("24-bit", ref _bitDepth, 24);
            
            ImGui.Checkbox("Normalize on Export", ref _normalizeOnExport);
            
            ImGui.Spacing();
            
            if (ImGui.Button("Export as WAV", new Vector2(-1, 0)))
            {
                _exportDialog.Open($"waveform_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
            }
        }
        
        private async Task ExtractWaveformAsync()
        {
            _progressDialog.Open("Extracting waveform from acoustic volume...");
            
            try
            {
                await Task.Run(() => ExtractWaveform());
            }
            catch (Exception ex)
            {
                Logger.LogError($"[WaveformViewer] Failed to extract waveform: {ex.Message}");
            }
            finally
            {
                _progressDialog.Close();
            }
        }
        
        private void ExtractWaveform()
        {
            if (_dataset.TimeSeriesSnapshots == null || _dataset.TimeSeriesSnapshots.Count == 0)
            {
                Logger.LogError("[WaveformViewer] No time series data available");
                return;
            }
            
            var volume = _dataset.CombinedWaveField ?? _dataset.PWaveField ?? _dataset.SWaveField;
            if (volume == null)
            {
                Logger.LogError("[WaveformViewer] No wave field data available");
                return;
            }
            
            // Convert normalized coordinates to voxel coordinates
            int sx, sy, sz, rx, ry, rz;
            if (_useNormalizedCoords)
            {
                sx = (int)(_sourcePoint.X * (volume.Width - 1));
                sy = (int)(_sourcePoint.Y * (volume.Height - 1));
                sz = (int)(_sourcePoint.Z * (volume.Depth - 1));
                rx = (int)(_receiverPoint.X * (volume.Width - 1));
                ry = (int)(_receiverPoint.Y * (volume.Height - 1));
                rz = (int)(_receiverPoint.Z * (volume.Depth - 1));
            }
            else
            {
                sx = (int)_sourcePoint.X;
                sy = (int)_sourcePoint.Y;
                sz = (int)_sourcePoint.Z;
                rx = (int)_receiverPoint.X;
                ry = (int)_receiverPoint.Y;
                rz = (int)_receiverPoint.Z;
            }
            
            // Clamp to valid range
            sx = Math.Clamp(sx, 0, volume.Width - 1);
            sy = Math.Clamp(sy, 0, volume.Height - 1);
            sz = Math.Clamp(sz, 0, volume.Depth - 1);
            rx = Math.Clamp(rx, 0, volume.Width - 1);
            ry = Math.Clamp(ry, 0, volume.Height - 1);
            rz = Math.Clamp(rz, 0, volume.Depth - 1);
            
            // Extract waveform from time series
            List<float> waveformList = new List<float>();
            _maxAmplitude = float.MinValue;
            _minAmplitude = float.MaxValue;
            
            for (int i = 0; i < _dataset.TimeSeriesSnapshots.Count; i++)
            {
                _progressDialog.Update((float)i / _dataset.TimeSeriesSnapshots.Count, 
                    $"Processing snapshot {i + 1}/{_dataset.TimeSeriesSnapshots.Count}");
                
                var snapshot = _dataset.TimeSeriesSnapshots[i];
                
                // Get velocity field for selected component
                float[,,] field = null;
                switch (_selectedComponent)
                {
                    case 1: field = snapshot.GetVelocityField(0); break; // X
                    case 2: field = snapshot.GetVelocityField(1); break; // Y
                    case 3: field = snapshot.GetVelocityField(2); break; // Z
                    case 0: // Magnitude
                    default:
                        var vx = snapshot.GetVelocityField(0);
                        var vy = snapshot.GetVelocityField(1);
                        var vz = snapshot.GetVelocityField(2);
                        if (vx != null && vy != null && vz != null)
                        {
                            field = new float[snapshot.Width, snapshot.Height, snapshot.Depth];
                            for (int z = 0; z < snapshot.Depth; z++)
                                for (int y = 0; y < snapshot.Height; y++)
                                    for (int x = 0; x < snapshot.Width; x++)
                                    {
                                        float mag = (float)Math.Sqrt(
                                            vx[x, y, z] * vx[x, y, z] +
                                            vy[x, y, z] * vy[x, y, z] +
                                            vz[x, y, z] * vz[x, y, z]);
                                        field[x, y, z] = mag;
                                    }
                        }
                        break;
                }
                
                if (field != null)
                {
                    // Extract value at receiver point
                    float value = field[rx, ry, rz];
                    waveformList.Add(value);
                    
                    _maxAmplitude = Math.Max(_maxAmplitude, value);
                    _minAmplitude = Math.Min(_minAmplitude, value);
                }
            }
            
            _waveformData = waveformList.ToArray();
            
            // Resample to target sample rate if needed
            ResampleWaveform();
            
            Logger.Log($"[WaveformViewer] Extracted waveform with {_waveformData.Length} samples");
        }
        
        private void ResampleWaveform()
        {
            if (_waveformData == null || _waveformData.Length < 2) return;
            
            // Calculate current effective sample rate
            var firstSnapshot = _dataset.TimeSeriesSnapshots[0];
            var lastSnapshot = _dataset.TimeSeriesSnapshots[_dataset.TimeSeriesSnapshots.Count - 1];
            float duration = lastSnapshot.SimulationTime - firstSnapshot.SimulationTime;
            
            if (duration <= 0) return;
            
            float currentRate = _waveformData.Length / duration;
            
            // Resample if needed
            if (Math.Abs(currentRate - _sampleRate) > 1.0f)
            {
                int newLength = (int)(duration * _sampleRate);
                float[] resampled = new float[newLength];
                
                for (int i = 0; i < newLength; i++)
                {
                    float t = (float)i / newLength * _waveformData.Length;
                    int idx = (int)t;
                    float frac = t - idx;
                    
                    if (idx < _waveformData.Length - 1)
                    {
                        resampled[i] = _waveformData[idx] * (1 - frac) + _waveformData[idx + 1] * frac;
                    }
                    else
                    {
                        resampled[i] = _waveformData[_waveformData.Length - 1];
                    }
                }
                
                _waveformData = resampled;
            }
        }
        
        private float[] CalculateEnvelope(float[] signal)
        {
            if (signal == null || signal.Length == 0) return new float[0];
            
            // Simple envelope calculation using local maxima
            float[] envelope = new float[signal.Length];
            int windowSize = Math.Max(1, signal.Length / 100);
            
            for (int i = 0; i < signal.Length; i++)
            {
                float maxVal = 0;
                int start = Math.Max(0, i - windowSize);
                int end = Math.Min(signal.Length - 1, i + windowSize);
                
                for (int j = start; j <= end; j++)
                {
                    maxVal = Math.Max(maxVal, Math.Abs(signal[j]));
                }
                
                envelope[i] = maxVal;
            }
            
            return envelope;
        }
        
        private void HandleDialogs()
        {
            _progressDialog.Submit();
            
            if (_exportDialog.Submit())
            {
                _ = ExportWaveformAsWAVAsync(_exportDialog.SelectedPath);
            }
        }
        
        private async Task ExportWaveformAsWAVAsync(string filePath)
        {
            if (_waveformData == null || _waveformData.Length == 0)
            {
                Logger.LogError("[WaveformViewer] No waveform data to export");
                return;
            }
            
            _progressDialog.Open("Exporting waveform as WAV...");
            
            try
            {
                await Task.Run(() => ExportWaveformAsWAV(filePath));
                Logger.Log($"[WaveformViewer] Exported waveform to {filePath}");
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
        
        private void ExportWaveformAsWAV(string filePath)
        {
            using (var writer = new BinaryWriter(File.Create(filePath)))
            {
                // Normalize waveform if needed
                float[] exportData = _waveformData;
                if (_normalizeOnExport)
                {
                    float maxAbs = _waveformData.Max(Math.Abs);
                    if (maxAbs > 0)
                    {
                        exportData = _waveformData.Select(v => v / maxAbs).ToArray();
                    }
                }
                
                // WAV header
                writer.Write("RIFF".ToCharArray());
                writer.Write(36 + exportData.Length * (_bitDepth / 8)); // File size
                writer.Write("WAVE".ToCharArray());
                
                // Format chunk
                writer.Write("fmt ".ToCharArray());
                writer.Write(16); // Chunk size
                writer.Write((short)1); // PCM format
                writer.Write((short)1); // Mono
                writer.Write((int)_sampleRate); // Sample rate
                writer.Write((int)(_sampleRate * _bitDepth / 8)); // Byte rate
                writer.Write((short)(_bitDepth / 8)); // Block align
                writer.Write((short)_bitDepth); // Bits per sample
                
                // Data chunk
                writer.Write("data".ToCharArray());
                writer.Write(exportData.Length * (_bitDepth / 8));
                
                // Write samples
                for (int i = 0; i < exportData.Length; i++)
                {
                    if (i % 1000 == 0)
                    {
                        _progressDialog.Update((float)i / exportData.Length, 
                            $"Writing sample {i}/{exportData.Length}");
                    }
                    
                    if (_bitDepth == 16)
                    {
                        short sample = (short)(exportData[i] * 32767);
                        writer.Write(sample);
                    }
                    else if (_bitDepth == 24)
                    {
                        int sample = (int)(exportData[i] * 8388607);
                        writer.Write((byte)(sample & 0xFF));
                        writer.Write((byte)((sample >> 8) & 0xFF));
                        writer.Write((byte)((sample >> 16) & 0xFF));
                    }
                }
            }
        }
        
        public void Dispose()
        {
            // Clean up resources if needed
        }
    }
}