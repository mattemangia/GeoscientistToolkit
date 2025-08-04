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
using GeoscientistToolkit.UI.Utils; // Added to use ImGuiExportFileDialog
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI
{
    /// <summary>
    /// An enumeration of available test patterns for the debug volume.
    /// </summary>
    internal enum TestPattern
    {
        Sphere,
        Cube,
        Checkerboard,
        DensityRamp,
        Noise
    }

    /// <summary>
    /// A debug window for creating and inspecting 3D volume viewers with various test patterns.
    /// </summary>
    internal class Volume3DDebugWindow
    {
        private bool _isOpen;
        private bool _isViewerOpen;
        private readonly List<string> _logs = new List<string>();
        private readonly Random _random = new Random();

        // UI State
        private TestPattern _selectedPattern = TestPattern.Sphere;
        private readonly ImGuiExportFileDialog _logExportDialog;

        // Debug data
        private CtImageStackDataset _editableDataset;
        private StreamingCtVolumeDataset _debugDataset;
        private CtVolume3DViewer _debugViewer;

        private const int _volumeSize = 128;

        public Volume3DDebugWindow()
        {
            // Initialize the export dialog
            _logExportDialog = new ImGuiExportFileDialog("DebugLogExport", "Export Debug Log");
            _logExportDialog.SetExtensions(
                (".log", "Log File"),
                (".txt", "Text File")
            );
        }

        public void Submit()
        {
            if (!_isOpen)
                return;

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

                if (ImGui.Button("Create Debug Viewer"))
                {
                    _ = Task.Run(async () => {
                        await CreateDebugViewer();
                    });
                }

                ImGui.SameLine();

                // Button to open the export dialog
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

            // Handle the dialog submission outside the main window Begin/End
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
                lock (_logs)
                {
                    logLines = _logs.ToArray();
                }
                File.WriteAllLines(filePath, logLines);
                AddLog("Log export successful.");
            }
            catch (Exception ex)
            {
                AddLog($"ERROR: Failed to export log: {ex.Message}");
            }
        }

        /// <summary>
        /// Generates a test volume based on the selected pattern.
        /// </summary>
        private void GenerateTestVolume(ChunkedVolume volume, TestPattern pattern)
        {
            AddLog($"Generating '{pattern}' test volume data...");

            switch (pattern)
            {
                case TestPattern.Sphere:
                    var center = new Vector3(_volumeSize / 2f);
                    var radius = _volumeSize / 3f;
                    for (int z = 0; z < _volumeSize; z++)
                        for (int y = 0; y < _volumeSize; y++)
                            for (int x = 0; x < _volumeSize; x++)
                            {
                                float dist = Vector3.Distance(new Vector3(x, y, z), center);
                                byte value = (dist < radius) ? (byte)(255 * (1.0f - dist / radius)) : (byte)0;
                                volume[x, y, z] = value;
                            }
                    break;

                case TestPattern.Cube:
                    int start = _volumeSize / 4;
                    int end = _volumeSize * 3 / 4;
                    for (int z = 0; z < _volumeSize; z++)
                        for (int y = 0; y < _volumeSize; y++)
                            for (int x = 0; x < _volumeSize; x++)
                            {
                                bool isInCube = (x > start && x < end && y > start && y < end && z > start && z < end);
                                volume[x, y, z] = isInCube ? (byte)200 : (byte)0;
                            }
                    break;

                case TestPattern.Checkerboard:
                    int boardSize = _volumeSize / 8;
                    for (int z = 0; z < _volumeSize; z++)
                        for (int y = 0; y < _volumeSize; y++)
                            for (int x = 0; x < _volumeSize; x++)
                            {
                                int cell = (x / boardSize) + (y / boardSize) + (z / boardSize);
                                volume[x, y, z] = (cell % 2 == 0) ? (byte)220 : (byte)80;
                            }
                    break;

                case TestPattern.DensityRamp:
                    for (int z = 0; z < _volumeSize; z++)
                        for (int y = 0; y < _volumeSize; y++)
                            for (int x = 0; x < _volumeSize; x++)
                            {
                                volume[x, y, z] = (byte)(255 * (x / (float)(_volumeSize - 1)));
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
                                volume[x, y, z] = noiseData[i++];
                            }
                    break;
            }
            AddLog("Finished generating test volume data.");
        }

        private async Task CreateDebugViewer()
        {
            try
            {
                _debugViewer?.Dispose();
                _debugViewer = null;

                AddLog("Creating debug viewer...");

                var dummyVolume = new ChunkedVolume(_volumeSize, _volumeSize, _volumeSize, ChunkedVolume.DEFAULT_CHUNK_DIM);
                GenerateTestVolume(dummyVolume, _selectedPattern);

                string tempDir = Path.Combine(Path.GetTempPath(), "GeoscientistDebug");
                Directory.CreateDirectory(tempDir);
                string datasetName = "DebugDataset";
                string gvtPath = Path.Combine(tempDir, $"{datasetName}.gvt");

                AddLog($"Converting volume to GVT file at: {gvtPath}");
                await CtStackConverter.ConvertToStreamableFormat(dummyVolume, gvtPath,
                    (progress, message) => AddLog($"Conversion: {progress * 100:F1}% - {message}"));
                AddLog("GVT file created successfully.");

                dummyVolume.Dispose(); // Free up memory from the in-memory volume

                _editableDataset = new CtImageStackDataset(datasetName, tempDir)
                {
                    Width = _volumeSize,
                    Height = _volumeSize,
                    Depth = _volumeSize,
                    LabelData = new ChunkedLabelVolume(_volumeSize, _volumeSize, _volumeSize, ChunkedVolume.DEFAULT_CHUNK_DIM, false)
                };

                _debugDataset = new StreamingCtVolumeDataset("DebugStreamingVolume", gvtPath)
                {
                    EditablePartner = _editableDataset
                };

                AddLog("Creating viewer with standard implementation...");
                _debugViewer = new CtVolume3DViewer(_debugDataset);
                _isViewerOpen = true;

                AddLog("Debug viewer created successfully");
            }
            catch (Exception ex)
            {
                AddLog($"ERROR: Failed to create debug viewer: {ex.Message}");
                AddLog($"Stack trace: {ex.StackTrace}");
            }
        }
    }
}