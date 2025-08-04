// GeoscientistToolkit/UI/Volume3DDebugWindow.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI
{
    internal enum TestPattern
    {
        Sphere,
        Cube,
        Checkerboard,
        DensityRamp,
        Noise
    }

    internal class Volume3DDebugWindow
    {
        private bool _isOpen;
        private bool _isViewerOpen;
        private readonly List<string> _logs = new List<string>();
        private readonly Random _random = new Random();

        // UI State
        private TestPattern _selectedPattern = TestPattern.Sphere;
        private bool _generateLabels = false;
        private readonly ImGuiExportFileDialog _logExportDialog;

        // Debug data
        private CtImageStackDataset _editableDataset;
        private StreamingCtVolumeDataset _debugDataset;
        private CtVolume3DViewer _debugViewer;

        // Thread-safe handoff mechanism
        private (CtVolume3DViewer viewer, CtImageStackDataset editable, StreamingCtVolumeDataset streaming) _newViewerData;
        private readonly object _viewerLock = new object();
        private volatile bool _isCreating = false;

        private const int _volumeSize = 128;

        public Volume3DDebugWindow()
        {
            _logExportDialog = new ImGuiExportFileDialog("DebugLogExport", "Export Debug Log");
            _logExportDialog.SetExtensions((".log", "Log File"), (".txt", "Text File"));
        }

        public void Submit()
        {
            if (!_isOpen)
                return;

            // Perform the thread-safe swap on the main UI thread
            lock (_viewerLock)
            {
                if (_newViewerData.viewer != null)
                {
                    AddLog("Swapping to new debug viewer...");
                    _debugViewer?.Dispose();
                    _editableDataset?.Unload();
                    _debugDataset?.Unload();

                    _debugViewer = _newViewerData.viewer;
                    _editableDataset = _newViewerData.editable;
                    _debugDataset = _newViewerData.streaming;

                    _isViewerOpen = true;
                    _newViewerData = (null, null, null);
                    _isCreating = false;
                    AddLog("Swap complete.");
                }
            }

            ImGui.SetNextWindowSize(new Vector2(600, 400), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("3D Volume Debugger", ref _isOpen))
            {
                ImGui.Text("Test Pattern:");
                if (ImGui.RadioButton("Sphere", _selectedPattern == TestPattern.Sphere)) { _selectedPattern = TestPattern.Sphere; }
                ImGui.SameLine();
                if (ImGui.RadioButton("Cube", _selectedPattern == TestPattern.Cube)) { _selectedPattern = TestPattern.Cube; }
                ImGui.SameLine();
                if (ImGui.RadioButton("Checkerboard", _selectedPattern == TestPattern.Checkerboard)) { _selectedPattern = TestPattern.Checkerboard; }

                if (ImGui.RadioButton("Density Ramp (X-axis)", _selectedPattern == TestPattern.DensityRamp)) { _selectedPattern = TestPattern.DensityRamp; }
                ImGui.SameLine();
                if (ImGui.RadioButton("Noise", _selectedPattern == TestPattern.Noise)) { _selectedPattern = TestPattern.Noise; }

                ImGui.Separator();
                ImGui.Checkbox("Generate Test Labels", ref _generateLabels);
                ImGui.Separator();

                if (_isCreating)
                {
                    ImGui.BeginDisabled();
                    ImGui.Button("Creating...");
                    ImGui.EndDisabled();
                }
                else
                {
                    if (ImGui.Button("Create Debug Viewer"))
                    {
                        _isCreating = true;
                        Task.Run(async () => {
                            await CreateDebugViewer();
                        });
                    }
                }

                ImGui.SameLine();
                if (ImGui.Button("Export Log..."))
                {
                    _logExportDialog.Open($"debug_log_{DateTime.Now:yyyyMMdd_HHmmss}");
                }

                ImGui.Separator();
                ImGui.Text("Logs:");
                ImGui.BeginChild("LogRegion", Vector2.Zero, ImGuiChildFlags.Border, ImGuiWindowFlags.HorizontalScrollbar);
                lock (_logs)
                {
                    foreach (var log in _logs)
                    {
                        ImGui.TextUnformatted(log);
                    }
                }
                ImGui.EndChild();
            }
            ImGui.End();

            if (_logExportDialog.Submit())
            {
                ExportLogToFile(_logExportDialog.SelectedPath);
            }

            SubmitDebugViewer();
        }

        private void SubmitDebugViewer()
        {
            if (_debugViewer == null || !_isViewerOpen) return;

            ImGui.SetNextWindowSize(new Vector2(800, 600), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Debug 3D Viewer", ref _isViewerOpen))
            {
                _debugViewer.DrawToolbarControls();
                float zoom = 1.0f;
                Vector2 pan = Vector2.Zero;
                _debugViewer.DrawContent(ref zoom, ref pan);
            }
            ImGui.End();

            if (!_isViewerOpen)
            {
                _debugViewer.Dispose();
                _debugViewer = null;
                _editableDataset?.Unload();
                _editableDataset = null;
                _debugDataset?.Unload();
                _debugDataset = null;
                AddLog("Debug viewer closed and disposed.");
            }
        }

        public void Show()
        {
            _isOpen = true;
        }

        private void AddLog(string message)
        {
            lock (_logs)
            {
                _logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            }
            System.Diagnostics.Debug.WriteLine($"[Volume3DDebugWindow] {message}");
        }

        private void ExportLogToFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            AddLog($"Exporting log to {filePath}...");
            try
            {
                string[] logLines;
                lock (_logs) { logLines = _logs.ToArray(); }
                File.WriteAllLines(filePath, logLines);
                AddLog("Log export successful.");
            }
            catch (Exception ex)
            {
                AddLog($"ERROR: Failed to export log: {ex.Message}");
            }
        }

        private void GenerateTestVolume(ChunkedVolume volume, ChunkedLabelVolume labels, TestPattern pattern, bool generateLabels)
        {
            AddLog($"Generating '{pattern}' test volume data...");

            if (generateLabels && labels != null)
            {
                AddLog("Clearing old label data...");
                Parallel.For(0, labels.Depth, z =>
                {
                    for (int y = 0; y < labels.Height; y++)
                    {
                        for (int x = 0; x < labels.Width; x++)
                        {
                            labels[x, y, z] = 0;
                        }
                    }
                });
                AddLog("Finished clearing labels.");
            }

            switch (pattern)
            {
                case TestPattern.Sphere:
                    var center = new Vector3(_volumeSize / 2f);
                    var radius = _volumeSize / 3f;
                    var innerRadius = _volumeSize / 6f;
                    Parallel.For(0, _volumeSize, z =>
                    {
                        for (int y = 0; y < _volumeSize; y++)
                            for (int x = 0; x < _volumeSize; x++)
                            {
                                float dist = Vector3.Distance(new Vector3(x, y, z), center);
                                byte value = (dist < radius) ? (byte)(255 * (1.0f - dist / radius)) : (byte)0;
                                volume[x, y, z] = value;
                                if (generateLabels)
                                {
                                    if (dist < innerRadius) labels[x, y, z] = 1;
                                    else if (dist < radius) labels[x, y, z] = 2;
                                }
                            }
                    });
                    break;

                case TestPattern.Cube:
                    int start = _volumeSize / 4;
                    int end = _volumeSize * 3 / 4;
                    int innerStart = _volumeSize / 3;
                    int innerEnd = _volumeSize * 2 / 3;
                    for (int z = 0; z < _volumeSize; z++)
                        for (int y = 0; y < _volumeSize; y++)
                            for (int x = 0; x < _volumeSize; x++)
                            {
                                bool isInCube = (x > start && x < end && y > start && y < end && z > start && z < end);
                                volume[x, y, z] = isInCube ? (byte)200 : (byte)0;

                                if (generateLabels && isInCube)
                                {
                                    bool isInInnerCube = (x > innerStart && x < innerEnd && y > innerStart && y < innerEnd && z > innerStart && z < innerEnd);
                                    labels[x, y, z] = isInInnerCube ? (byte)1 : (byte)2;
                                }
                            }
                    break;

                // --- FIX: Restored the logic for the missing patterns ---
                case TestPattern.Checkerboard:
                    int boardSize = _volumeSize / 8;
                    for (int z = 0; z < _volumeSize; z++)
                        for (int y = 0; y < _volumeSize; y++)
                            for (int x = 0; x < _volumeSize; x++)
                            {
                                int cell = (x / boardSize) + (y / boardSize) + (z / boardSize);
                                bool isHigh = cell % 2 == 0;
                                volume[x, y, z] = isHigh ? (byte)220 : (byte)80;
                                if (generateLabels)
                                {
                                    labels[x, y, z] = isHigh ? (byte)1 : (byte)2;
                                }
                            }
                    break;

                case TestPattern.DensityRamp:
                    for (int z = 0; z < _volumeSize; z++)
                        for (int y = 0; y < _volumeSize; y++)
                            for (int x = 0; x < _volumeSize; x++)
                            {
                                volume[x, y, z] = (byte)(255 * (x / (float)(_volumeSize - 1)));
                                if (generateLabels && x > _volumeSize / 2)
                                {
                                    labels[x, y, z] = 1;
                                }
                            }
                    break;

                case TestPattern.Noise:
                    byte[] noiseData = new byte[_volumeSize * _volumeSize * _volumeSize];
                    _random.NextBytes(noiseData);
                    int i = 0;
                    for (int z = 0; z < _volumeSize; z++)
                        for (int y = 0; y < _volumeSize; y++)
                            for (int x = 0; x < _volumeSize; x++)
                            {
                                byte val = noiseData[i++];
                                volume[x, y, z] = val;
                                if (generateLabels && val > 128)
                                {
                                    labels[x, y, z] = (byte)(val > 192 ? 1 : 2);
                                }
                            }
                    break;
            }
            AddLog("Finished generating test volume data.");
        }

        private async Task CreateDebugViewer()
        {
            CtVolume3DViewer newViewer = null;
            CtImageStackDataset newEditableDataset = null;
            StreamingCtVolumeDataset newStreamingDataset = null;

            try
            {
                AddLog("BG Task: Creating debug resources...");
                var dummyVolume = new ChunkedVolume(_volumeSize, _volumeSize, _volumeSize, ChunkedVolume.DEFAULT_CHUNK_DIM);

                string tempDir = Path.Combine(Path.GetTempPath(), "GeoscientistDebug");
                Directory.CreateDirectory(tempDir);
                string datasetName = "DebugDataset";

                newEditableDataset = new CtImageStackDataset(datasetName, tempDir)
                {
                    Width = _volumeSize,
                    Height = _volumeSize,
                    Depth = _volumeSize,
                    LabelData = new ChunkedLabelVolume(_volumeSize, _volumeSize, _volumeSize, ChunkedVolume.DEFAULT_CHUNK_DIM, false)
                };

                if (_generateLabels)
                {
                    AddLog("BG Task: Adding test materials...");
                    newEditableDataset.Materials.Add(new Material(1, "Material 1 (Red)", new Vector4(1, 0.2f, 0.2f, 1)));
                    newEditableDataset.Materials.Add(new Material(2, "Material 2 (Green)", new Vector4(0.2f, 1, 0.2f, 1)));
                }

                GenerateTestVolume(dummyVolume, newEditableDataset.LabelData, _selectedPattern, _generateLabels);

                string gvtPath = Path.Combine(tempDir, $"{datasetName}.gvt");
                AddLog($"BG Task: Converting volume to GVT file at: {gvtPath}");
                await CtStackConverter.ConvertToStreamableFormat(dummyVolume, gvtPath, (p, m) => { /* quiet log */ });
                AddLog("BG Task: GVT file created.");

                dummyVolume.Dispose();

                newStreamingDataset = new StreamingCtVolumeDataset("DebugStreamingVolume", gvtPath)
                {
                    EditablePartner = newEditableDataset
                };

                AddLog("BG Task: Creating viewer instance...");
                newViewer = new CtVolume3DViewer(newStreamingDataset);

                lock (_viewerLock)
                {
                    _newViewerData = (newViewer, newEditableDataset, newStreamingDataset);
                }

                AddLog("BG Task: Handoff complete. Task finished.");
            }
            catch (Exception ex)
            {
                AddLog($"ERROR in background task: {ex.Message}");
                AddLog($"Stack trace: {ex.StackTrace}");
                newViewer?.Dispose();
                newEditableDataset?.Unload();
                newStreamingDataset?.Unload();
                _isCreating = false;
            }
        }
    }
}