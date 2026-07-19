// GAIA/UI/PNMViewer.cs - With Diffusivity Visualization

using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.InteropServices;
using GAIA.Analysis.Pnm;
using GAIA.Business;
using GAIA.Data.Pnm;
using GAIA.UI.Interfaces;
using GAIA.UI.Utils;
using GAIA.Util;
using ImGuiNET;

namespace GAIA.UI;

internal enum ThroatColorMode { Radius, Length, PressureDrop, FlowRate }

internal static class PnmThroatColorMapping
{
    public static float GetValue(ThroatColorMode mode, Throat throat, Pore first, Pore second, float voxelSize,
        IReadOnlyDictionary<int, float> pressures, IReadOnlyDictionary<int, float> flowRates)
    {
        var scale = Math.Max(voxelSize, 1e-9f);
        return mode switch
        {
            ThroatColorMode.Radius => throat.Radius * scale,
            ThroatColorMode.Length => Vector3.Distance(first.Position, second.Position) * scale,
            ThroatColorMode.PressureDrop => pressures.TryGetValue(first.ID, out var p1) &&
                                            pressures.TryGetValue(second.ID, out var p2)
                ? Math.Abs(p1 - p2) : 0f,
            ThroatColorMode.FlowRate => Math.Abs(flowRates.TryGetValue(throat.ID, out var rate) ? rate : 0f),
            _ => throat.Radius * scale
        };
    }

    public static (float min, float max) GetRange(IEnumerable<float> values)
    {
        var finite = values.Where(float.IsFinite).ToArray();
        if (finite.Length == 0) return (0f, 1f);
        var min = finite.Min();
        var max = finite.Max();
        if (Math.Abs(max - min) < 0.001f) max = min + 1f;
        return (min, max);
    }
}

public class PNMViewer : IDatasetViewer
{
    private readonly string[] _colorByOptions =
    {
        "Pore Radius",
        "Pore Connections",
        "Pore Volume",
        "Pressure (Pores)",
        "Pressure Drop (Throats)",
        "Local Tortuosity",
        "Effective Diffusivity",
        "Temperature", // NEW
        "Species Concentration", // NEW
        "Mineral Precipitation", // NEW
        "Reaction Rate" // NEW
    };

    // Dataset and OpenGL rendering state
    private readonly PNMDataset _dataset;
    private readonly OpenTkPnmRenderer _openTkRenderer;
    private readonly object _flowDataLock = new();
    private readonly string _flowLegendWindowId = "##PNMFlowLegend";
    private readonly ConcurrentBag<int> _inletPoreIds = new();

    private readonly string _legendWindowId = "##PNMLegend";

    // NEW: Diffusivity visualization data
    private readonly ConcurrentDictionary<int, float> _localDiffusivity = new();
    private readonly ConcurrentDictionary<int, float> _localTortuosity = new();

    // NEW: Reactive transport visualization data
    private volatile bool _hasReactiveTransportData;
    private string _selectedSpecies = "";
    private string _selectedMineral = "";
    private readonly List<string> _availableSpecies = new();
    private readonly List<string> _availableMinerals = new();
    private readonly ConcurrentBag<int> _outletPoreIds = new();

    // Flow visualization data
    private readonly ConcurrentDictionary<int, float> _porePressures = new();

    // Screenshot functionality
    private readonly ImGuiExportFileDialog _screenshotDialog;
    private readonly string _selectedWindowId = "##PNMSelected";

    // Stats window ID for proper layering
    private readonly string _statsWindowId = "##PNMStats";
    private readonly ConcurrentDictionary<int, float> _throatFlowRates = new();
    private float _cameraDistance = 5.0f;
    private float _cameraPitch;
    private Vector3 _cameraPosition = new(0, 0, 5);
    private Vector3 _cameraTarget = Vector3.Zero;
    private float _cameraYaw = -MathF.PI / 2f;

    // UI & Rendering State
    private int _colorByIndex;
    private int _throatColorByIndex;

    // Flow direction info
    private string _flowAxis = "Z";
    private Vector3 _flowDirection = new(0, 0, 1);
    private volatile bool _hasDiffusivityData;
    private volatile bool _hasFlowData;
    private bool _isDragging;
    private bool _isPanning;
    private Vector2 _lastMousePos;
    private string _lastScreenshotPath = "";
    private Vector2 _lastViewerScreenPos;
    private Vector2 _lastViewerSize;
    private Vector3 _modelCenter;
    private float _modelRadius = 10.0f;
    private bool _pendingGeometryRebuild;
    private int _poreInstanceCount;
    private float _poreSizeMultiplier = 1.0f;
    private float _screenshotNotificationTimer;
    private Pore _selectedPore;

    // Selection
    private int _selectedPoreId = -1;
    private bool _showFlowPath;

    // Visualization controls
    private bool _showFlowVisualization;
    private bool _showInletPores = true;
    private bool _showOutletPores = true;
    private bool _showPores = true;
    private bool _showScreenshotNotification;
    private bool _showThroats = true;
    private uint _throatVertexCount;
    private bool _useLogScaleForDiffusivity = true;

    // Camera & Interaction
    private Matrix4x4 _viewMatrix, _projMatrix;
    private float _minColorValue;
    private float _maxColorValue = 1f;
    private float _minThroatColorValue;
    private float _maxThroatColorValue = 1f;

    // HUD overlays
    private bool _showStatsOverlay = true;
    private bool _showLegendOverlay = true;

    // Derived network figures, recomputed only when the network changes rather than per frame.
    private NetworkStatistics _statistics;
    private readonly Dictionary<int, List<Throat>> _poreAdjacency = new();

    public PNMViewer(PNMDataset dataset)
    {
        _dataset = dataset;
        ProjectManager.Instance.DatasetDataChanged += d =>
        {
            if (ReferenceEquals(d, _dataset))
                OpenTkManager.ExecuteOnMainThread(() =>
                {
                    _pendingGeometryRebuild = true;
                    UpdateFlowData();
                    UpdateDiffusivityData(); // NEW
                    UpdateReactiveTransportData(); // NEW
                });
        };

        _screenshotDialog = new ImGuiExportFileDialog($"Screenshot_{dataset.Name}", "Save Screenshot");
        _screenshotDialog.SetExtensions(
            (".png", "PNG Image"),
            (".jpg", "JPEG Image")
        );

        _openTkRenderer = new OpenTkPnmRenderer();
        UpdateFlowData();
        UpdateDiffusivityData(); // NEW
        UpdateReactiveTransportData(); // NEW
        RebuildGeometryFromDataset();
        ResetCamera();
    }

    public void Dispose()
    {
        _openTkRenderer?.Dispose();
    }

    // NEW: Update diffusivity data from dataset
    private void UpdateDiffusivityData()
    {
        lock (_flowDataLock)
        {
            if (_dataset.EffectiveDiffusivity > 0 && _dataset.BulkDiffusivity > 0)
            {
                _localDiffusivity.Clear();

                // Calculate local effective diffusivity for each pore
                // Based on connectivity and local network topology
                var D0 = _dataset.BulkDiffusivity;
                var Deff_global = _dataset.EffectiveDiffusivity;
                var F_global = _dataset.FormationFactor;

                foreach (var pore in _dataset.Pores)
                {
                    // Local diffusivity based on:
                    // 1. Connectivity (more connections = less restricted)
                    // 2. Local tortuosity
                    // 3. Pore size (larger pores = less restricted)

                    var avgConnectivity = _dataset.Pores.Average(p => p.Connections);
                    var connectivityFactor = pore.Connections / Math.Max(1, avgConnectivity);

                    var localTort = 1.0f;
                    if (_localTortuosity.TryGetValue(pore.ID, out var tort))
                        localTort = tort;

                    var sizeFactor = pore.Radius / Math.Max(0.1f, _dataset.MaxPoreRadius);

                    // Local formation factor
                    var F_local = F_global / (connectivityFactor * sizeFactor * (2.0f / localTort));
                    F_local = Math.Clamp(F_local, 1.0f, F_global * 2.0f);

                    // Local effective diffusivity
                    var D_local = D0 / F_local;

                    _localDiffusivity.TryAdd(pore.ID, (float)D_local);
                }

                _hasDiffusivityData = true;
                Logger.Log($"[PNMViewer] Loaded diffusivity data for {_localDiffusivity.Count} pores");
            }
            else
            {
                _hasDiffusivityData = false;
            }
        }
    }

    // NEW: Update reactive transport data from dataset
    private void UpdateReactiveTransportData()
    {
        lock (_flowDataLock)
        {
            if (_dataset.ReactiveTransportState != null)
            {
                _hasReactiveTransportData = true;

                // Get list of available species
                _availableSpecies.Clear();
                var speciesSet = new HashSet<string>();

                foreach (var poreConc in _dataset.ReactiveTransportState.PoreConcentrations.Values)
                {
                    foreach (var species in poreConc.Keys)
                        speciesSet.Add(species);
                }

                _availableSpecies.AddRange(speciesSet.OrderBy(s => s));

                if (!string.IsNullOrEmpty(_selectedSpecies) && !_availableSpecies.Contains(_selectedSpecies))
                    _selectedSpecies = _availableSpecies.FirstOrDefault() ?? "";

                if (string.IsNullOrEmpty(_selectedSpecies) && _availableSpecies.Count > 0)
                    _selectedSpecies = _availableSpecies[0];

                // Get list of available minerals
                _availableMinerals.Clear();
                var mineralSet = new HashSet<string>();

                foreach (var poreMinerals in _dataset.ReactiveTransportState.PoreMinerals.Values)
                {
                    foreach (var mineral in poreMinerals.Keys)
                        mineralSet.Add(mineral);
                }

                _availableMinerals.AddRange(mineralSet.OrderBy(m => m));

                if (!string.IsNullOrEmpty(_selectedMineral) && !_availableMinerals.Contains(_selectedMineral))
                    _selectedMineral = _availableMinerals.FirstOrDefault() ?? "";

                if (string.IsNullOrEmpty(_selectedMineral) && _availableMinerals.Count > 0)
                    _selectedMineral = _availableMinerals[0];

                Logger.Log($"[PNMViewer] Loaded reactive transport data: {_availableSpecies.Count} species, {_availableMinerals.Count} minerals");
            }
            else
            {
                _hasReactiveTransportData = false;
                _availableSpecies.Clear();
                _availableMinerals.Clear();
            }
        }
    }

    private void UpdateFlowData()
    {
        lock (_flowDataLock)
        {
            var results = AbsolutePermeability.GetLastResults();
            var flowData = AbsolutePermeability.GetLastFlowData();

            if (flowData != null && flowData.PorePressures != null && flowData.PorePressures.Count > 0)
            {
                _porePressures.Clear();
                foreach (var kvp in flowData.PorePressures)
                    _porePressures.TryAdd(kvp.Key, kvp.Value);

                _throatFlowRates.Clear();
                if (flowData.ThroatFlowRates != null)
                    foreach (var kvp in flowData.ThroatFlowRates)
                        _throatFlowRates.TryAdd(kvp.Key, kvp.Value);

                _hasFlowData = true;

                if (results != null && !string.IsNullOrEmpty(results.FlowAxis))
                {
                    _flowAxis = results.FlowAxis;
                    _flowDirection = _flowAxis switch
                    {
                        "X" => new Vector3(1, 0, 0),
                        "Y" => new Vector3(0, 1, 0),
                        "Z" => new Vector3(0, 0, 1),
                        _ => new Vector3(0, 0, 1)
                    };
                }

                if (_dataset != null && _dataset.Pores != null && _dataset.Pores.Count > 0)
                {
                    var tol = 2.0f;
                    var minX = _dataset.Pores.Min(p => p.Position.X);
                    var maxX = _dataset.Pores.Max(p => p.Position.X);
                    var minY = _dataset.Pores.Min(p => p.Position.Y);
                    var maxY = _dataset.Pores.Max(p => p.Position.Y);
                    var minZ = _dataset.Pores.Min(p => p.Position.Z);
                    var maxZ = _dataset.Pores.Max(p => p.Position.Z);

                    while (_inletPoreIds.TryTake(out _))
                    {
                    }

                    while (_outletPoreIds.TryTake(out _))
                    {
                    }

                    var axis = (_flowAxis ?? "Z").ToUpperInvariant();
                    foreach (var pore in _dataset.Pores)
                        switch (axis)
                        {
                            case "X":
                                if (pore.Position.X <= minX + tol) _inletPoreIds.Add(pore.ID);
                                if (pore.Position.X >= maxX - tol) _outletPoreIds.Add(pore.ID);
                                break;
                            case "Y":
                                if (pore.Position.Y <= minY + tol) _inletPoreIds.Add(pore.ID);
                                if (pore.Position.Y >= maxY - tol) _outletPoreIds.Add(pore.ID);
                                break;
                            default:
                                if (pore.Position.Z <= minZ + tol) _inletPoreIds.Add(pore.ID);
                                if (pore.Position.Z >= maxZ - tol) _outletPoreIds.Add(pore.ID);
                                break;
                        }

                    Logger.Log(
                        $"[PNMViewer] Identified {_inletPoreIds.Count} inlet and {_outletPoreIds.Count} outlet pores");
                }

                CalculateLocalTortuosity();
                Logger.Log($"[PNMViewer] Loaded flow data with {_porePressures.Count} pore pressures");
            }
            else
            {
                _hasFlowData = false;
                _showFlowVisualization = false;
                while (_inletPoreIds.TryTake(out _))
                {
                }

                while (_outletPoreIds.TryTake(out _))
                {
                }
            }
        }
    }

    private void CalculateLocalTortuosity()
    {
        _localTortuosity.Clear();

        var adjacency = new Dictionary<int, List<int>>();
        foreach (var throat in _dataset.Throats)
        {
            if (!adjacency.ContainsKey(throat.Pore1ID))
                adjacency[throat.Pore1ID] = new List<int>();
            if (!adjacency.ContainsKey(throat.Pore2ID))
                adjacency[throat.Pore2ID] = new List<int>();

            adjacency[throat.Pore1ID].Add(throat.Pore2ID);
            adjacency[throat.Pore2ID].Add(throat.Pore1ID);
        }

        foreach (var pore in _dataset.Pores)
        {
            if (!adjacency.ContainsKey(pore.ID))
            {
                _localTortuosity.TryAdd(pore.ID, 1.0f);
                continue;
            }

            var neighbors = adjacency[pore.ID];
            if (neighbors.Count < 2)
            {
                _localTortuosity.TryAdd(pore.ID, 1.0f);
                continue;
            }

            var totalDeviation = 0f;
            var comparisons = 0;

            for (var i = 0; i < neighbors.Count - 1; i++)
            {
                var n1 = _dataset.Pores.FirstOrDefault(p => p.ID == neighbors[i]);
                if (n1 == null) continue;

                for (var j = i + 1; j < neighbors.Count; j++)
                {
                    var n2 = _dataset.Pores.FirstOrDefault(p => p.ID == neighbors[j]);
                    if (n2 == null) continue;

                    var v1 = Vector3.Normalize(n1.Position - pore.Position);
                    var v2 = Vector3.Normalize(n2.Position - pore.Position);
                    var angle = MathF.Acos(Math.Clamp(Vector3.Dot(v1, v2), -1f, 1f));

                    var deviation = MathF.Abs(MathF.PI - angle) / MathF.PI;
                    totalDeviation += deviation;
                    comparisons++;
                }
            }

            if (comparisons > 0)
            {
                var avgDeviation = totalDeviation / comparisons;
                _localTortuosity.TryAdd(pore.ID, 1.0f + avgDeviation * 2.0f);
            }
            else
            {
                _localTortuosity.TryAdd(pore.ID, 1.0f);
            }
        }
    }


    private void DrawScreenshotNotification(Vector2 viewPos, Vector2 viewSize)
    {
        var size = new Vector2(Math.Min(320f, viewSize.X - 20f), 56f);
        var position = viewPos + new Vector2((viewSize.X - size.X) * 0.5f, 50f);
        var fade = Math.Clamp(_screenshotNotificationTimer / 3.0f, 0f, 1f);

        if (BeginOverlay("##PNMScreenshotNotification", position, size, false, 0.9f * fade))
        {
            if (_lastScreenshotPath.StartsWith("Error"))
            {
                ImGui.TextColored(new Vector4(1.0f, 0.2f, 0.2f, fade), "Screenshot failed!");
                ImGui.TextColored(new Vector4(1f, 1f, 1f, fade), _lastScreenshotPath);
            }
            else
            {
                ImGui.TextColored(new Vector4(0.2f, 1.0f, 0.2f, fade), "Screenshot saved!");
                ImGui.TextColored(new Vector4(1f, 1f, 1f, fade), Path.GetFileName(_lastScreenshotPath));
            }
        }

        EndOverlay();
    }

    private void TakeAndSaveScreenshot(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                Logger.Log($"[PNMViewer] Created directory: {directory}");
            }

            var format = Path.GetExtension(path).ToLower() switch
            {
                ".png" => ScreenshotUtility.ImageFormat.PNG,
                ".jpg" or ".jpeg" => ScreenshotUtility.ImageFormat.JPEG,
                ".bmp" => ScreenshotUtility.ImageFormat.BMP,
                ".tga" => ScreenshotUtility.ImageFormat.TGA,
                _ => ScreenshotUtility.ImageFormat.PNG
            };

            ViewerScreenshotUtility.ScheduleRegionCapture(
                _lastViewerScreenPos,
                _lastViewerSize,
                path,
                format,
                (success, filePath) =>
                {
                    OpenTkManager.ExecuteOnMainThread(() =>
                    {
                        if (success)
                        {
                            Logger.Log($"[PNMViewer] Screenshot saved to {filePath}");
                            _lastScreenshotPath = filePath;
                            _showScreenshotNotification = true;
                            _screenshotNotificationTimer = 3.0f;
                        }
                        else
                        {
                            Logger.LogError($"[PNMViewer] Failed to save screenshot to {filePath}");
                            _lastScreenshotPath = "Error: Failed to save screenshot";
                            _showScreenshotNotification = true;
                            _screenshotNotificationTimer = 3.0f;
                        }
                    });
                });

            Logger.Log($"[PNMViewer] Screenshot capture scheduled for {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[PNMViewer] Screenshot error: {ex.Message}");
            _lastScreenshotPath = $"Error: {ex.Message}";
            _showScreenshotNotification = true;
            _screenshotNotificationTimer = 3.0f;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Constants
    {
        public Matrix4x4 ViewProjection;
        public Vector4 CameraPosition;
        public Vector4 ColorRampInfo;
        public Vector4 SizeInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PoreInstanceData
    {
        public Vector3 Position;
        public float ColorValue;
        public float Radius;

        public PoreInstanceData(Vector3 pos, float colorVal, float rad)
        {
            Position = pos;
            ColorValue = colorVal;
            Radius = rad;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ThroatVertexData
    {
        public Vector3 Position;
        public float ColorValue;

        public ThroatVertexData(Vector3 pos, float colorVal)
        {
            Position = pos;
            ColorValue = colorVal;
        }
    }

    #region Resource Creation


    private (Vector3[] vertices, ushort[] indices) CreateSphereGeometry()
    {
        var t = (1.0f + MathF.Sqrt(5.0f)) / 2.0f;
        var vertices = new List<Vector3>
        {
            new Vector3(-1, t, 0).Normalized(),
            new Vector3(1, t, 0).Normalized(),
            new Vector3(-1, -t, 0).Normalized(),
            new Vector3(1, -t, 0).Normalized(),
            new Vector3(0, -1, t).Normalized(),
            new Vector3(0, 1, t).Normalized(),
            new Vector3(0, -1, -t).Normalized(),
            new Vector3(0, 1, -t).Normalized(),
            new Vector3(t, 0, -1).Normalized(),
            new Vector3(t, 0, 1).Normalized(),
            new Vector3(-t, 0, -1).Normalized(),
            new Vector3(-t, 0, 1).Normalized()
        };

        var indices = new List<ushort>
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
            1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
            4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
        };

        var newVertices = new List<Vector3>(vertices);
        var newIndices = new List<ushort>();
        var midpointCache = new Dictionary<(int, int), int>();

        int GetMidpoint(int v1, int v2)
        {
            var key = v1 < v2 ? (v1, v2) : (v2, v1);
            if (midpointCache.TryGetValue(key, out var mid))
                return mid;

            var midPos = (newVertices[v1] + newVertices[v2]).Normalized();
            mid = newVertices.Count;
            newVertices.Add(midPos);
            midpointCache[key] = mid;
            return mid;
        }

        for (var i = 0; i < indices.Count; i += 3)
        {
            int v1 = indices[i];
            int v2 = indices[i + 1];
            int v3 = indices[i + 2];

            var a = GetMidpoint(v1, v2);
            var b = GetMidpoint(v2, v3);
            var c = GetMidpoint(v3, v1);

            newIndices.Add((ushort)v1);
            newIndices.Add((ushort)a);
            newIndices.Add((ushort)c);
            newIndices.Add((ushort)v2);
            newIndices.Add((ushort)b);
            newIndices.Add((ushort)a);
            newIndices.Add((ushort)v3);
            newIndices.Add((ushort)c);
            newIndices.Add((ushort)b);
            newIndices.Add((ushort)a);
            newIndices.Add((ushort)b);
            newIndices.Add((ushort)c);
        }

        return (newVertices.ToArray(), newIndices.ToArray());
    }

    #endregion

    #region Drawing and Interaction

    public void DrawToolbarControls()
    {
        if (ImGui.Button("Reset Camera")) ResetCamera();
        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        ImGui.Checkbox("Pores", ref _showPores);
        ImGui.SameLine();
        ImGui.Checkbox("Throats", ref _showThroats);
        ImGui.SameLine();
        ImGui.Checkbox("Legend", ref _showLegendOverlay);
        ImGui.SameLine();
        ImGui.Checkbox("Stats", ref _showStatsOverlay);
        ImGui.SameLine();

        if (_hasFlowData)
        {
            ImGui.Separator();
            ImGui.SameLine();

            if (ImGui.Checkbox("Flow Viz", ref _showFlowVisualization))
            {
                if (_showFlowVisualization)
                {
                    _showInletPores = true;
                    _showOutletPores = true;
                }

                RebuildGeometryFromDataset();
            }

            if (_showFlowVisualization)
            {
                ImGui.SameLine();
                if (ImGui.Checkbox("Inlet", ref _showInletPores))
                    RebuildGeometryFromDataset();
                ImGui.SameLine();
                if (ImGui.Checkbox("Outlet", ref _showOutletPores))
                    RebuildGeometryFromDataset();
                ImGui.SameLine();
                if (ImGui.Checkbox("Path", ref _showFlowPath))
                    RebuildGeometryFromDataset();
            }

            ImGui.SameLine();
        }

        ImGui.Separator();
        ImGui.SameLine();

        ImGui.Text("Pore color:");
        ImGui.SameLine();

        var availableOptions = new List<string>();
        var optionIndices = new List<int>();

        availableOptions.Add("Pore Radius");
        optionIndices.Add(0);
        availableOptions.Add("Pore Connections");
        optionIndices.Add(1);
        availableOptions.Add("Pore Volume");
        optionIndices.Add(2);

        if (_hasFlowData)
        {
            availableOptions.Add("Pressure (Pores)");
            optionIndices.Add(3);
            availableOptions.Add("Pressure Drop (Throats)");
            optionIndices.Add(4);
            availableOptions.Add("Local Tortuosity");
            optionIndices.Add(5);
        }

        // NEW: Add diffusivity option if data available
        if (_hasDiffusivityData)
        {
            availableOptions.Add("Effective Diffusivity");
            optionIndices.Add(6);
        }

        // NEW: Add reactive transport options if data available
        if (_hasReactiveTransportData)
        {
            availableOptions.Add("Temperature");
            optionIndices.Add(7);
            availableOptions.Add("Species Concentration");
            optionIndices.Add(8);
            availableOptions.Add("Mineral Precipitation");
            optionIndices.Add(9);
            availableOptions.Add("Reaction Rate");
            optionIndices.Add(10);
        }

        var localIndex = optionIndices.IndexOf(_colorByIndex);
        if (localIndex < 0) localIndex = 0;

        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("##ColorBy", ref localIndex, availableOptions.ToArray(), availableOptions.Count))
        {
            _colorByIndex = optionIndices[localIndex];
            RebuildGeometryFromDataset();
        }

        ImGui.SameLine();
        ImGui.Text("Throat color:");
        ImGui.SameLine();
        var throatOptions = new List<string> { "Radius", "Length" };
        if (_hasFlowData)
        {
            throatOptions.Add("Pressure Drop");
            throatOptions.Add("Flow Rate");
        }
        if (_throatColorByIndex >= throatOptions.Count) _throatColorByIndex = 0;
        ImGui.SetNextItemWidth(145);
        if (ImGui.Combo("##ThroatColorBy", ref _throatColorByIndex, throatOptions.ToArray(), throatOptions.Count))
            RebuildGeometryFromDataset();

        // NEW: Show species/mineral selector if relevant mode is active
        if (_colorByIndex == 8 && _availableSpecies.Count > 0)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);
            var speciesIndex = Math.Max(0, _availableSpecies.IndexOf(_selectedSpecies));
            if (ImGui.Combo("##Species", ref speciesIndex, _availableSpecies.ToArray(), _availableSpecies.Count))
            {
                _selectedSpecies = _availableSpecies[speciesIndex];
                RebuildGeometryFromDataset();
            }
        }
        else if (_colorByIndex == 9 && _availableMinerals.Count > 0)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);
            var mineralIndex = Math.Max(0, _availableMinerals.IndexOf(_selectedMineral));
            if (ImGui.Combo("##Mineral", ref mineralIndex, _availableMinerals.ToArray(), _availableMinerals.Count))
            {
                _selectedMineral = _availableMinerals[mineralIndex];
                RebuildGeometryFromDataset();
            }
        }

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        ImGui.Text("Pore Size:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        if (ImGui.SliderFloat("##PoreSize", ref _poreSizeMultiplier, 0.1f, 5.0f)) UpdateConstantBuffers();

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();
        if (ImGui.Button("Screenshot...")) _screenshotDialog.Open($"{_dataset.Name}_capture");

        if (_screenshotDialog.Submit()) TakeAndSaveScreenshot(_screenshotDialog.SelectedPath);
    }

    public void DrawContent(ref float zoom, ref Vector2 pan)
    {
        if (_pendingGeometryRebuild)
        {
            UpdateFlowData();
            UpdateDiffusivityData(); // NEW
            UpdateReactiveTransportData(); // NEW
            RebuildGeometryFromDataset();
            _pendingGeometryRebuild = false;
        }

        var availableSize = ImGui.GetContentRegionAvail();
        if (availableSize.X < 2 || availableSize.Y < 2) return;
        _openTkRenderer.Resize((int)availableSize.X, (int)availableSize.Y);
        Render();

        var textureId = _openTkRenderer.TextureId;
        if (textureId == IntPtr.Zero) return;
        var imagePos = ImGui.GetCursorScreenPos();
        _lastViewerScreenPos = imagePos;
        _lastViewerSize = availableSize;
        ImGui.Image(textureId, availableSize, new Vector2(0, 1), new Vector2(1, 0));

        ImGui.SetCursorScreenPos(imagePos);
        ImGui.InvisibleButton("PNMViewInteraction", availableSize);

        if (_dataset.Pores.Count == 0)
        {
            var message = "The generated PNM contains no pores. Try a less aggressive erosion or verify the selected segmented material.";
            var drawList = ImGui.GetWindowDrawList();
            var textSize = ImGui.CalcTextSize(message, false, Math.Max(180, availableSize.X - 60));
            var textPos = imagePos + new Vector2(Math.Max(20, (availableSize.X - textSize.X) * .5f),
                Math.Max(20, (availableSize.Y - textSize.Y) * .5f));
            drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(), textPos, 0xFF66AAFF, message,
                Math.Max(180, availableSize.X - 60));
        }

        var isHovered = ImGui.IsItemHovered();
        if (isHovered)
        {
            HandleMouseInput();

            const float dragThresholdPx = 4.0f;
            var leftReleased = ImGui.IsMouseReleased(ImGuiMouseButton.Left);
            var wasDragging = ImGui.IsMouseDragging(ImGuiMouseButton.Left, dragThresholdPx);
            var shiftHeld = ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift);

            if (leftReleased && !wasDragging && !shiftHeld)
            {
                var mousePos = ImGui.GetMousePos() - imagePos;
                SelectPoreAtPosition(mousePos, availableSize);
            }
        }

        DrawOverlayWindows(imagePos, availableSize);

        if (_showScreenshotNotification && _screenshotNotificationTimer > 0)
        {
            DrawScreenshotNotification(imagePos, availableSize);
            _screenshotNotificationTimer -= ImGui.GetIO().DeltaTime;
            if (_screenshotNotificationTimer <= 0) _showScreenshotNotification = false;
        }
    }

    private void SelectPoreAtPosition(Vector2 mousePos, Vector2 viewSize)
    {
        if (_dataset == null || _dataset.Pores == null || _dataset.Pores.Count == 0)
        {
            _selectedPoreId = -1;
            _selectedPore = null;
            return;
        }

        var ndcX = mousePos.X / viewSize.X * 2.0f - 1.0f;
        var ndcY = 1.0f - mousePos.Y / viewSize.Y * 2.0f;

        var invViewProj = Matrix4x4.Identity;
        if (!Matrix4x4.Invert(_viewMatrix * _projMatrix, out invViewProj)) return;

        var nearPoint = Vector4.Transform(new Vector4(ndcX, ndcY, -1, 1), invViewProj);
        var farPoint = Vector4.Transform(new Vector4(ndcX, ndcY, 1, 1), invViewProj);

        if (nearPoint.W == 0 || farPoint.W == 0) return;

        var rayOrigin = new Vector3(nearPoint.X, nearPoint.Y, nearPoint.Z) / nearPoint.W;
        var rayEnd = new Vector3(farPoint.X, farPoint.Y, farPoint.Z) / farPoint.W;
        var rayDir = Vector3.Normalize(rayEnd - rayOrigin);

        var bestId = -1;
        Pore bestPore = null;
        var bestDistance = float.MaxValue;

        foreach (var pore in _dataset.Pores)
        {
            var toCenter = pore.Position - rayOrigin;
            var projectedDist = Vector3.Dot(toCenter, rayDir);

            if (projectedDist < 0) continue;

            var closestPoint = rayOrigin + rayDir * projectedDist;
            var distToCenter = Vector3.Distance(closestPoint, pore.Position);

            var selectRadius = pore.Radius * _poreSizeMultiplier * 0.15f;
            if (selectRadius < 0.2f) selectRadius = 0.2f;

            if (distToCenter <= selectRadius && projectedDist < bestDistance)
            {
                bestDistance = projectedDist;
                bestId = pore.ID;
                bestPore = pore;
            }
        }

        _selectedPoreId = bestId;
        _selectedPore = bestPore;

        if (_selectedPore != null) Logger.Log($"[PNMViewer] Selected pore #{_selectedPore.ID}");
    }

    private void RebuildGeometryFromDataset()
    {
        RecomputeStatistics();

        var poreInstances = new List<OpenTkPnmRenderer.PoreGpuData>();
        foreach (var p in _dataset.Pores)
        {
            var isInlet = _inletPoreIds.Contains(p.ID);
            var isOutlet = _outletPoreIds.Contains(p.ID);

            if (_showFlowVisualization)
            {
                if (isInlet && !_showInletPores) continue;
                if (isOutlet && !_showOutletPores) continue;
            }

            var colorValue = GetPoreColorValue(p);
            var radiusMultiplier = 1.0f;

            if (_showFlowVisualization && _hasFlowData && (isInlet || isOutlet))
            {
                radiusMultiplier = 1.5f;
                if (isInlet)
                    colorValue = 1.0f;
                else if (isOutlet) colorValue = 0.0f;
            }

            poreInstances.Add(new OpenTkPnmRenderer.PoreGpuData(p.Position, colorValue,
                p.Radius * radiusMultiplier));
        }

        _poreInstanceCount = poreInstances.Count;

        var poreById = _dataset.Pores.ToDictionary(p => p.ID, p => p);
        var throatVertices = new List<OpenTkPnmRenderer.ThroatGpuData>();

        foreach (var t in _dataset.Throats)
            if (poreById.TryGetValue(t.Pore1ID, out var p1) && poreById.TryGetValue(t.Pore2ID, out var p2))
            {
                var isFlowPath = false;
                if (_showFlowVisualization && _showFlowPath && _hasFlowData)
                {
                    var p1Inlet = _inletPoreIds.Contains(p1.ID);
                    var p2Inlet = _inletPoreIds.Contains(p2.ID);
                    var p1Outlet = _outletPoreIds.Contains(p1.ID);
                    var p2Outlet = _outletPoreIds.Contains(p2.ID);

                    if ((p1Inlet || p2Inlet) && (p1Outlet || p2Outlet)) isFlowPath = true;

                    if (_throatFlowRates.TryGetValue(t.ID, out var flowRate) && flowRate > 0)
                    {
                        var maxFlow = _throatFlowRates.Values.Max();
                        if (flowRate > maxFlow * 0.5f) isFlowPath = true;
                    }
                }

                var colorValue = GetThroatColorValue(t, p1, p2);
                if (isFlowPath) colorValue = 0.5f;

                throatVertices.Add(new OpenTkPnmRenderer.ThroatGpuData(p1.Position, colorValue));
                throatVertices.Add(new OpenTkPnmRenderer.ThroatGpuData(p2.Position, colorValue));
            }

        _throatVertexCount = (uint)throatVertices.Count;

        _openTkRenderer.Upload(poreInstances, throatVertices);
        UpdateConstantBuffers();
    }

    private float GetPoreColorValue(Pore p)
    {
        switch (_colorByIndex)
        {
            case 0: return p.Radius;
            case 1: return p.Connections;
            case 2: return p.VolumePhysical;
            case 3:
                if (_porePressures.TryGetValue(p.ID, out var pressure))
                    return pressure;
                return 0;
            case 4:
                return p.Radius;
            case 5:
                if (_localTortuosity.TryGetValue(p.ID, out var tort))
                    return tort;
                return 1.0f;
            case 6: // Diffusivity with LOG SCALE
                if (_localDiffusivity.TryGetValue(p.ID, out var diff))
                {
                    // Use log10 scale for better visualization
                    if (diff > 0)
                        return MathF.Log10(diff);
                    return -20f; // Floor value for zero/invalid
                }
                return -20f;
            case 7: // Temperature
                if (_hasReactiveTransportData && _dataset.ReactiveTransportState.PoreTemperatures.TryGetValue(p.ID, out var temp))
                    return temp;
                return 298.15f; // Room temperature default
            case 8: // Species Concentration
                if (_hasReactiveTransportData && !string.IsNullOrEmpty(_selectedSpecies) &&
                    _dataset.ReactiveTransportState.PoreConcentrations.TryGetValue(p.ID, out var concs) &&
                    concs.TryGetValue(_selectedSpecies, out var conc))
                    return conc;
                return 0;
            case 9: // Mineral Precipitation
                if (_hasReactiveTransportData && !string.IsNullOrEmpty(_selectedMineral) &&
                    _dataset.ReactiveTransportState.PoreMinerals.TryGetValue(p.ID, out var minerals) &&
                    minerals.TryGetValue(_selectedMineral, out var mineralVol))
                    return mineralVol;
                return 0;
            case 10: // Reaction Rate
                if (_hasReactiveTransportData && _dataset.ReactiveTransportState.ReactionRates.TryGetValue(p.ID, out var rate))
                    return Math.Abs(rate); // Use absolute value for coloring
                return 0;
            default: return p.Radius;
        }
    }

    private float GetThroatColorValue(Throat t, Pore p1, Pore p2)
    {
        return PnmThroatColorMapping.GetValue((ThroatColorMode)_throatColorByIndex, t, p1, p2,
            _dataset.VoxelSize, _porePressures, _throatFlowRates);
    }

    private void Render()
    {
        UpdateConstantBuffers();
        _openTkRenderer.Render(_viewMatrix * _projMatrix, _cameraPosition, _minColorValue,
            _maxColorValue, _minThroatColorValue, _maxThroatColorValue,
            _poreSizeMultiplier, _showPores, _showThroats);
    }

    private void UpdateConstantBuffers()
    {
        float minVal = 0, maxVal = 1;

        if (_showFlowVisualization && _hasFlowData && (_showInletPores || _showOutletPores))
        {
            minVal = 0;
            maxVal = 1;
        }
        else
        {
            switch (_colorByIndex)
            {
                case 0:
                    minVal = _dataset.MinPoreRadius;
                    maxVal = _dataset.MaxPoreRadius;
                    break;
                case 1:
                    minVal = _dataset.Pores.Any() ? _dataset.Pores.Min(p => p.Connections) : 0;
                    maxVal = _dataset.Pores.Any() ? _dataset.Pores.Max(p => p.Connections) : 1;
                    break;
                case 2:
                    minVal = _dataset.Pores.Any() ? _dataset.Pores.Min(p => p.VolumePhysical) : 0;
                    maxVal = _dataset.Pores.Any() ? _dataset.Pores.Max(p => p.VolumePhysical) : 1;
                    break;
                case 3:
                    if (_porePressures.Any())
                    {
                        minVal = _porePressures.Values.Min();
                        maxVal = _porePressures.Values.Max();
                    }

                    break;
                case 4:
                    if (_porePressures.Any())
                    {
                        float minDrop = float.MaxValue, maxDrop = 0;
                        foreach (var throat in _dataset.Throats)
                            if (_porePressures.TryGetValue(throat.Pore1ID, out var p1) &&
                                _porePressures.TryGetValue(throat.Pore2ID, out var p2))
                            {
                                var drop = Math.Abs(p1 - p2);
                                if (drop < minDrop) minDrop = drop;
                                if (drop > maxDrop) maxDrop = drop;
                            }

                        minVal = minDrop != float.MaxValue ? minDrop : 0;
                        maxVal = maxDrop;
                    }

                    break;
                case 5:
                    if (_localTortuosity.Any())
                    {
                        minVal = _localTortuosity.Values.Min();
                        maxVal = _localTortuosity.Values.Max();
                    }

                    break;
                case 6: // Diffusivity is rendered in log10 space.
                    if (_localDiffusivity.Any())
                    {
                        var positive = _localDiffusivity.Values.Where(value => value > 0).ToArray();
                        if (positive.Length > 0)
                        {
                            minVal = MathF.Log10(positive.Min());
                            maxVal = MathF.Log10(positive.Max());
                        }
                        else
                        {
                            minVal = -20f;
                            maxVal = -19f;
                        }
                    }

                    break;
                case 7: // Temperature
                case 8: // Species concentration
                case 9: // Mineral precipitation
                case 10: // Reaction rate
                    if (_dataset.Pores.Count > 0)
                    {
                        var displayedValues = _dataset.Pores.Select(GetPoreColorValue).ToArray();
                        minVal = displayedValues.Min();
                        maxVal = displayedValues.Max();
                    }
                    break;
            }
        }

        if (Math.Abs(maxVal - minVal) < 0.001f) maxVal = minVal + 1.0f;

        _minColorValue = minVal;
        _maxColorValue = maxVal;

        if (_dataset.Throats.Count == 0)
        {
            _minThroatColorValue = 0;
            _maxThroatColorValue = 1;
            return;
        }
        var poreById = _dataset.Pores.ToDictionary(p => p.ID);
        var throatValues = _dataset.Throats
            .Where(t => poreById.ContainsKey(t.Pore1ID) && poreById.ContainsKey(t.Pore2ID))
            .Select(t => GetThroatColorValue(t, poreById[t.Pore1ID], poreById[t.Pore2ID]))
            .Where(float.IsFinite)
            .ToArray();
        (_minThroatColorValue, _maxThroatColorValue) = PnmThroatColorMapping.GetRange(throatValues);
    }

    private void HandleMouseInput()
    {
        var io = ImGui.GetIO();

        if (io.MouseWheel != 0)
        {
            var maximumDistance = Math.Max(1f, _modelRadius * 10.0f);
            var candidate = _cameraDistance * MathF.Pow(0.9f, io.MouseWheel);
            if (!float.IsFinite(candidate)) candidate = maximumDistance;
            _cameraDistance = Math.Clamp(candidate, 0.1f, maximumDistance);
            UpdateCameraMatrices();
        }

        var wantRotate = (ImGui.IsMouseDown(ImGuiMouseButton.Left) && ImGui.IsKeyDown(ImGuiKey.LeftShift)) ||
                         ImGui.IsMouseDown(ImGuiMouseButton.Right);

        var wantPan = ImGui.IsMouseDown(ImGuiMouseButton.Middle);

        if (wantRotate)
        {
            if (!_isDragging)
            {
                _isDragging = true;
                _lastMousePos = io.MousePos;
            }

            var delta = io.MousePos - _lastMousePos;
            _cameraYaw -= delta.X * 0.01f;
            _cameraPitch = Math.Clamp(_cameraPitch - delta.Y * 0.01f, -MathF.PI / 2.01f, MathF.PI / 2.01f);
            _lastMousePos = io.MousePos;
            UpdateCameraMatrices();
        }
        else if (wantPan)
        {
            if (!_isPanning)
            {
                _isPanning = true;
                _lastMousePos = io.MousePos;
            }

            var delta = io.MousePos - _lastMousePos;
            Matrix4x4.Invert(_viewMatrix, out var invView);
            var right = Vector3.Normalize(new Vector3(invView.M11, invView.M12, invView.M13));
            var up = Vector3.Normalize(new Vector3(invView.M21, invView.M22, invView.M23));
            var panSpeed = _cameraDistance * 0.001f;
            _cameraTarget -= right * delta.X * panSpeed;
            _cameraTarget += up * delta.Y * panSpeed;
            _lastMousePos = io.MousePos;
            UpdateCameraMatrices();
        }
        else
        {
            _isDragging = false;
            _isPanning = false;
        }
    }

    private void UpdateCameraMatrices()
    {
        _cameraPosition = _cameraTarget + new Vector3(
            MathF.Cos(_cameraYaw) * MathF.Cos(_cameraPitch),
            MathF.Sin(_cameraPitch),
            MathF.Sin(_cameraYaw) * MathF.Cos(_cameraPitch)) * _cameraDistance;

        _viewMatrix = Matrix4x4.CreateLookAt(_cameraPosition, _cameraTarget, Vector3.UnitY);

        var aspectRatio = _openTkRenderer.Width / (float)Math.Max(1, _openTkRenderer.Height);
        // PNM coordinates are expressed in source voxels. Large CT networks routinely span well
        // beyond 1000 units, so a fixed far plane clipped the complete network and left only the
        // UI overlays visible. Scale the depth range with the fitted model and camera distance.
        var nearPlane = Math.Max(0.001f, _modelRadius * 0.0001f);
        var farPlane = Math.Max(1000f, _cameraDistance + _modelRadius * 4f);
        _projMatrix = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, aspectRatio, nearPlane, farPlane);
    }

    /// <summary>
    ///     Draws the HUD read-outs over the render target.
    ///     These are child windows of the viewer, not top-level ones: as siblings they lost the
    ///     z-order fight the instant a click focused the viewer, and the opaque render target then
    ///     hid them. A child always draws above its parent's content and clips to it.
    /// </summary>
    private void DrawOverlayWindows(Vector2 viewPos, Vector2 viewSize)
    {
        const float margin = 10f;

        if (_showLegendOverlay && viewSize.X >= 220 && viewSize.Y >= 140)
        {
            var legendWidth = Math.Clamp(viewSize.X - margin * 2, 120f, 190f);
            var legendHeight = Math.Clamp(viewSize.Y - margin * 2, 100f, 300f);
            var position = viewPos + new Vector2(viewSize.X - legendWidth - margin, margin);
            if (BeginOverlay(_legendWindowId, position, new Vector2(legendWidth, legendHeight), false))
                DrawLegendContent();
            EndOverlay();
        }

        if (_showFlowVisualization && _hasFlowData && viewSize.X >= 230 && viewSize.Y >= 310)
        {
            var position = viewPos + new Vector2(viewSize.X - 210 - margin, viewSize.Y - 140 - margin);
            if (BeginOverlay(_flowLegendWindowId, position, new Vector2(210, 140), false))
                DrawFlowLegend();
            EndOverlay();
        }

        if (_showStatsOverlay && viewSize.X >= 460 && viewSize.Y >= 260)
        {
            var statsWidth = Math.Min(560f, viewSize.X - margin * 2);
            const float statsHeight = 208f;
            var position = viewPos + new Vector2(margin, viewSize.Y - statsHeight - margin);
            if (BeginOverlay(_statsWindowId, position, new Vector2(statsWidth, statsHeight), false))
                DrawStatisticsContent();
            EndOverlay();
        }

        if (_selectedPoreId >= 0)
        {
            _selectedPore = FindPoreById(_selectedPoreId);
            if (_selectedPore == null)
            {
                _selectedPoreId = -1;
                return;
            }

            var width = Math.Min(340f, viewSize.X - margin * 2);
            var height = Math.Min(330f, viewSize.Y - margin * 2);
            // The only interactive overlay: it carries the Deselect button.
            if (BeginOverlay(_selectedWindowId, viewPos + new Vector2(margin, margin),
                    new Vector2(width, height), true))
                DrawSelectedPoreContent();
            EndOverlay();
        }
    }

    private static bool BeginOverlay(string id, Vector2 position, Vector2 size, bool interactive,
        float backgroundAlpha = 0.88f)
    {
        ImGui.SetCursorScreenPos(position);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.09f, 0.11f, backgroundAlpha));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f, 8f));

        var flags = ImGuiWindowFlags.NoSavedSettings;
        // Passive read-outs must not swallow hover, so a drag across them keeps orbiting the camera.
        // Interactive panels keep their scrollbar: their content grows with the analyses that ran.
        if (!interactive)
            flags |= ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoScrollbar |
                     ImGuiWindowFlags.NoScrollWithMouse;

        return ImGui.BeginChild(id, size, ImGuiChildFlags.Border, flags);
    }

    private static void EndOverlay()
    {
        // EndChild pairs with BeginChild regardless of its return value.
        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor();
    }

    private void DrawFlowLegend()
    {
        ImGui.Text("Flow Visualization");
        ImGui.Separator();

        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();

        if (_showInletPores)
        {
            drawList.AddCircleFilled(new Vector2(pos.X + 10, pos.Y + 8), 7,
                ImGui.GetColorU32(new Vector4(1.0f, 0.2f, 0.2f, 1.0f)));
            ImGui.SetCursorScreenPos(new Vector2(pos.X + 25, pos.Y + 2));
            ImGui.Text($"Inlet: {_inletPoreIds.Count} pores");
            ImGui.SetCursorScreenPos(new Vector2(pos.X + 25, pos.Y + 18));
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "High pressure");
            pos.Y += 40;
        }

        if (_showOutletPores)
        {
            drawList.AddCircleFilled(new Vector2(pos.X + 10, pos.Y + 8), 7,
                ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 1.0f, 1.0f)));
            ImGui.SetCursorScreenPos(new Vector2(pos.X + 25, pos.Y + 2));
            ImGui.Text($"Outlet: {_outletPoreIds.Count} pores");
            ImGui.SetCursorScreenPos(new Vector2(pos.X + 25, pos.Y + 18));
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Low pressure");
            pos.Y += 40;
        }

        if (_showFlowPath)
        {
            drawList.AddLine(new Vector2(pos.X + 5, pos.Y + 8), new Vector2(pos.X + 15, pos.Y + 8),
                ImGui.GetColorU32(new Vector4(0.2f, 1.0f, 0.2f, 1.0f)), 3);
            ImGui.SetCursorScreenPos(new Vector2(pos.X + 25, pos.Y + 2));
            ImGui.Text("High flow paths");
        }
    }

    private void DrawLegendContent()
    {
        var title = _colorByIndex < _colorByOptions.Length ? _colorByOptions[_colorByIndex] : "Unknown";
        ImGui.Text($"Pores: {title}");
        var throatTitle = _throatColorByIndex switch
        {
            0 => "Radius (μm)",
            1 => "Length (μm)",
            2 => "Pressure drop (Pa)",
            3 => "Flow rate",
            _ => "Radius (μm)"
        };
        ImGui.Text($"Throats: {throatTitle}");
        ImGui.TextDisabled($"{_minThroatColorValue:G4} — {_maxThroatColorValue:G4}");
        ImGui.Separator();

        float minVal = 0, maxVal = 1;
        float actualMin = 0, actualMax = 1, actualMid = 0.5f;
        var unit = "";
        var useActualValues = false;

        if (_showFlowVisualization && _hasFlowData && (_showInletPores || _showOutletPores))
        {
            minVal = 0;
            maxVal = 1;
            unit = " (Flow)";
            actualMin = 0;
            actualMax = 1;
            actualMid = 0.5f;
        }
        else
        {
            switch (_colorByIndex)
            {
                case 0:
                    minVal = _dataset.MinPoreRadius * _dataset.VoxelSize;
                    maxVal = _dataset.MaxPoreRadius * _dataset.VoxelSize;
                    unit = " μm";
                    actualMin = minVal;
                    actualMax = maxVal;
                    actualMid = (minVal + maxVal) / 2f;
                    break;
                case 1:
                    minVal = _dataset.Pores.Any() ? _dataset.Pores.Min(p => p.Connections) : 0;
                    maxVal = _dataset.Pores.Any() ? _dataset.Pores.Max(p => p.Connections) : 1;
                    unit = "";
                    actualMin = minVal;
                    actualMax = maxVal;
                    actualMid = (minVal + maxVal) / 2f;
                    break;
                case 2:
                    minVal = _dataset.Pores.Any() ? _dataset.Pores.Min(p => p.VolumePhysical) : 0;
                    maxVal = _dataset.Pores.Any() ? _dataset.Pores.Max(p => p.VolumePhysical) : 1;
                    unit = " μm³";
                    actualMin = minVal;
                    actualMax = maxVal;
                    actualMid = (minVal + maxVal) / 2f;
                    break;
                case 3:
                    if (_porePressures.Any())
                    {
                        minVal = _porePressures.Values.Min();
                        maxVal = _porePressures.Values.Max();
                        unit = " Pa";
                        actualMin = minVal;
                        actualMax = maxVal;
                        actualMid = (minVal + maxVal) / 2f;
                    }

                    break;
                case 4:
                    if (_throatFlowRates.Any())
                    {
                        float minDrop = float.MaxValue, maxDrop = 0;
                        foreach (var throat in _dataset.Throats)
                            if (_porePressures.TryGetValue(throat.Pore1ID, out var p1) &&
                                _porePressures.TryGetValue(throat.Pore2ID, out var p2))
                            {
                                var drop = Math.Abs(p1 - p2);
                                minDrop = Math.Min(minDrop, drop);
                                maxDrop = Math.Max(maxDrop, drop);
                            }

                        minVal = minDrop != float.MaxValue ? minDrop : 0;
                        maxVal = maxDrop;
                        unit = " Pa";
                        actualMin = minVal;
                        actualMax = maxVal;
                        actualMid = (minVal + maxVal) / 2f;
                    }

                    break;
                case 5:
                    if (_localTortuosity.Any())
                    {
                        minVal = _localTortuosity.Values.Min();
                        maxVal = _localTortuosity.Values.Max();
                        unit = "";
                        actualMin = minVal;
                        actualMax = maxVal;
                        actualMid = (minVal + maxVal) / 2f;
                    }

                    break;
                case 6: // Diffusivity with LOG SCALE
                    if (_localDiffusivity.Any())
                    {
                        actualMin = _localDiffusivity.Values.Min();
                        actualMax = _localDiffusivity.Values.Max();

                        // Use log scale for visualization
                        minVal = actualMin > 0 ? MathF.Log10(actualMin) : -20f;
                        maxVal = actualMax > 0 ? MathF.Log10(actualMax) : -10f;
                        actualMid = MathF.Pow(10, (minVal + maxVal) / 2f);

                        unit = " m²/s";
                        useActualValues = true;
                    }

                    break;
            }
        }

        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        float width = 30;
        float height = 180;

        var steps = 20;
        for (var i = 0; i < steps; i++)
        {
            var t1 = (float)(steps - i - 1) / steps;
            var t2 = (float)(steps - i) / steps;

            var c1 = GetColorForMode(t1);
            var c2 = GetColorForMode(t2);

            drawList.AddRectFilledMultiColor(
                new Vector2(pos.X, pos.Y + i * height / steps),
                new Vector2(pos.X + width, pos.Y + (i + 1) * height / steps),
                ImGui.GetColorU32(c1), ImGui.GetColorU32(c1),
                ImGui.GetColorU32(c2), ImGui.GetColorU32(c2));
        }

        // Display values
        ImGui.SetCursorScreenPos(new Vector2(pos.X + width + 5, pos.Y));
        if (useActualValues)
            ImGui.Text($"{actualMax:E2}{unit}");
        else if (_colorByIndex == 6)
            ImGui.Text($"{maxVal:E2}{unit}");
        else
            ImGui.Text($"{maxVal:F2}{unit}");

        ImGui.SetCursorScreenPos(new Vector2(pos.X + width + 5, pos.Y + height / 2 - ImGui.GetTextLineHeight() / 2));
        if (useActualValues)
            ImGui.Text($"{actualMid:E2}{unit}");
        else if (_colorByIndex == 6)
            ImGui.Text($"{(minVal + maxVal) / 2:E2}{unit}");
        else
            ImGui.Text($"{(minVal + maxVal) / 2:F2}{unit}");

        ImGui.SetCursorScreenPos(new Vector2(pos.X + width + 5, pos.Y + height - ImGui.GetTextLineHeight()));
        if (useActualValues)
            ImGui.Text($"{actualMin:E2}{unit}");
        else if (_colorByIndex == 6)
            ImGui.Text($"{minVal:E2}{unit}");
        else
            ImGui.Text($"{minVal:F2}{unit}");

        if (_colorByIndex >= 3 && _colorByIndex <= 5 && !_hasFlowData)
        {
            ImGui.Spacing();
            ImGui.TextWrapped("Run permeability calculation to enable flow visualization");
        }

        if (_colorByIndex == 6 && !_hasDiffusivityData)
        {
            ImGui.Spacing();
            ImGui.TextWrapped("Run diffusivity calculation to enable diffusivity visualization");
        }
    }

    private Vector4 GetColorForMode(float t)
    {
        if (_colorByIndex == 3 || _colorByIndex == 4) // Pressure
        {
            var r = t;
            var g = 1.0f - Math.Abs(2.0f * t - 1.0f);
            var b = 1.0f - t;
            return new Vector4(r, g, b, 1.0f);
        }

        if (_colorByIndex == 5) // Tortuosity
        {
            var r = Math.Min(1.0f, 2.0f * t);
            var g = Math.Min(1.0f, 2.0f * (1.0f - t));
            var b = 0.0f;
            return new Vector4(r, g, b, 1.0f);
        }

        if (_colorByIndex == 6) // Diffusivity - match the gradient
        {
            float r, g, b;

            if (t < 0.25f)
            {
                // Blue to Cyan
                var local_t = t / 0.25f;
                r = 0f;
                g = local_t;
                b = 1f;
            }
            else if (t < 0.5f)
            {
                // Cyan to Green
                var local_t = (t - 0.25f) / 0.25f;
                r = 0f;
                g = 1f;
                b = 1f - local_t;
            }
            else if (t < 0.75f)
            {
                // Green to Yellow
                var local_t = (t - 0.5f) / 0.25f;
                r = local_t;
                g = 1f;
                b = 0f;
            }
            else
            {
                // Yellow to Red
                var local_t = (t - 0.75f) / 0.25f;
                r = 1f;
                g = 1f - local_t;
                b = 0f;
            }

            return new Vector4(r, g, b, 1.0f);
        }

        // Default turbo colormap
        float r_default, g_default, b_default;

        if (t < 0.2f)
        {
            r_default = 0.9f + 0.1f * (t / 0.2f);
            g_default = 0.2f + 0.6f * (t / 0.2f);
            b_default = 0.1f;
        }
        else if (t < 0.4f)
        {
            r_default = 1.0f;
            g_default = 0.8f + 0.2f * ((t - 0.2f) / 0.2f);
            b_default = 0.1f + 0.2f * ((t - 0.2f) / 0.2f);
        }
        else if (t < 0.6f)
        {
            r_default = 1.0f - 0.5f * ((t - 0.4f) / 0.2f);
            g_default = 1.0f;
            b_default = 0.3f + 0.3f * ((t - 0.4f) / 0.2f);
        }
        else if (t < 0.8f)
        {
            r_default = 0.5f - 0.3f * ((t - 0.6f) / 0.2f);
            g_default = 1.0f - 0.2f * ((t - 0.6f) / 0.2f);
            b_default = 0.6f + 0.3f * ((t - 0.6f) / 0.2f);
        }
        else
        {
            r_default = 0.2f + 0.3f * ((t - 0.8f) / 0.2f);
            g_default = 0.8f - 0.3f * ((t - 0.8f) / 0.2f);
            b_default = 0.9f + 0.1f * ((t - 0.8f) / 0.2f);
        }

        return new Vector4(r_default, g_default, b_default, 1.0f);
    }

    /// <summary> Dimmed label followed by its value, so a column scans as a label/value pair. </summary>
    private static void Metric(string label, string value)
    {
        ImGui.TextDisabled(label);
        ImGui.SameLine();
        ImGui.Text(value);
    }

    /// <summary> Renders a quantity, or an em dash when the analysis producing it has not been run. </summary>
    private static void Metric(string label, float value, string format, string unit)
    {
        ImGui.TextDisabled(label);
        ImGui.SameLine();
        if (value > 0)
            ImGui.Text($"{value.ToString(format)}{unit}");
        else
            ImGui.TextDisabled("—");
    }

    private void DrawStatisticsContent()
    {
        if (_dataset == null) return;

        var stats = _statistics;

        ImGui.Text("Network Statistics");
        ImGui.SameLine();
        ImGui.TextDisabled($"— {_dataset.Name}");
        if (_dataset.ActiveFilter != null)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 0.75f, 0.2f, 1f), "[filtered]");
        }

        ImGui.Separator();
        ImGui.Columns(3, "##PNMStatsColumns", false);

        // --- Topology ---
        ImGui.TextColored(new Vector4(0.45f, 0.75f, 1f, 1f), "Topology");
        Metric("Pores:", $"{stats.PoreCount:N0}");
        Metric("Throats:", $"{stats.ThroatCount:N0}");
        var isolatedFraction = stats.PoreCount > 0 ? stats.IsolatedPoreCount / (float)stats.PoreCount : 0f;
        Metric("Isolated:", $"{stats.IsolatedPoreCount:N0} ({isolatedFraction:P1})");
        Metric("Coord. number:", $"{stats.MeanCoordination:F2}");
        Metric("  range:", $"{stats.MinCoordination:F0} – {stats.MaxCoordination:F0}");

        ImGui.NextColumn();

        // --- Geometry ---
        ImGui.TextColored(new Vector4(0.45f, 0.75f, 1f, 1f), "Geometry");
        Metric("Voxel size:", $"{_dataset.VoxelSize:F3} μm");
        Metric("Volume:", $"{_dataset.ImageWidth}×{_dataset.ImageHeight}×{_dataset.ImageDepth} vox");
        Metric("Centre extent:", $"{stats.ExtentUm.X:F0}×{stats.ExtentUm.Y:F0}×{stats.ExtentUm.Z:F0} μm");
        Metric("Porosity:", stats.BulkVolumeUm3 > 0 ? $"{stats.Porosity:P2}" : "—");
        Metric("Pore r:", $"{stats.MinPoreRadiusUm:F2} / {stats.MeanPoreRadiusUm:F2} / {stats.MaxPoreRadiusUm:F2} μm");
        Metric("Throat r:",
            $"{stats.MinThroatRadiusUm:F2} / {stats.MeanThroatRadiusUm:F2} / {stats.MaxThroatRadiusUm:F2} μm");

        ImGui.NextColumn();

        // --- Transport ---
        ImGui.TextColored(new Vector4(0.45f, 0.75f, 1f, 1f), "Transport");
        Metric("Tortuosity:", _dataset.Tortuosity, "F3", "");
        Metric("k Darcy:", _dataset.DarcyPermeability, "F2", " mD");
        Metric("k N-S:", _dataset.NavierStokesPermeability, "F2", " mD");
        Metric("k LBM:", _dataset.LatticeBoltzmannPermeability, "F2", " mD");
        if (_dataset.DarcyPermeability > 0 && _dataset.Tortuosity > 0)
            Metric("  τ² corrected:",
                $"{_dataset.DarcyPermeability / (_dataset.Tortuosity * _dataset.Tortuosity):F2} mD");
        Metric("D_eff:", _dataset.EffectiveDiffusivity, "E3", " m²/s");
        Metric("Form. factor:", _dataset.FormationFactor, "F2", "");

        ImGui.Columns(1);
    }

    private void DrawSelectedPoreContent()
    {
        if (_selectedPoreId >= 0)
            _selectedPore = FindPoreById(_selectedPoreId);

        if (_selectedPore == null)
        {
            ImGui.TextDisabled("No pore selected.");
            if (ImGui.Button("Close")) _selectedPoreId = -1;
            return;
        }

        var voxelSize = EffectiveVoxelSize;

        ImGui.Text($"Selected Pore #{_selectedPore.ID}");
        ImGui.Separator();

        var position = _selectedPore.Position;
        Metric("Position:", $"{position.X:F1}, {position.Y:F1}, {position.Z:F1} vox");
        Metric("", $"{position.X * voxelSize:F1}, {position.Y * voxelSize:F1}, {position.Z * voxelSize:F1} μm");
        Metric("Radius:", $"{_selectedPore.Radius:F3} vox ({_selectedPore.Radius * voxelSize:F2} μm)");
        Metric("Volume:", $"{_selectedPore.VolumeVoxels:F0} vox³ ({PhysicalVolume(_selectedPore):F2} μm³)");
        Metric("Surface area:", $"{_selectedPore.Area:F1} vox² ({_selectedPore.Area * voxelSize * voxelSize:F1} μm²)");
        Metric("Connections:", $"{_selectedPore.Connections}");

        var equivalentDiameter = 2f * _selectedPore.Radius * voxelSize;
        Metric("Equiv. diameter:", $"{equivalentDiameter:F2} μm");

        if (_poreAdjacency.TryGetValue(_selectedPore.ID, out var connectedThroats) && connectedThroats.Count > 0)
        {
            ImGui.Separator();
            ImGui.Text($"Connected Throats ({connectedThroats.Count})");
            if (ImGui.BeginChild("##PNMConnectedThroats", new Vector2(0, 74), ImGuiChildFlags.None))
                foreach (var throat in connectedThroats)
                {
                    var neighbour = throat.Pore1ID == _selectedPore.ID ? throat.Pore2ID : throat.Pore1ID;
                    ImGui.TextDisabled($"#{throat.ID}");
                    ImGui.SameLine();
                    ImGui.Text($"→ pore {neighbour}, r {throat.Radius * voxelSize:F2} μm");
                }

            ImGui.EndChild();
        }

        if (_hasFlowData && _porePressures.TryGetValue(_selectedPore.ID, out var pressure))
        {
            ImGui.Separator();
            ImGui.Text("Flow Properties:");
            ImGui.Text($"Pressure: {pressure:F3} Pa");

            if (_localTortuosity.TryGetValue(_selectedPore.ID, out var tort))
                ImGui.Text($"Local Tortuosity: {tort:F3}");

            if (_inletPoreIds.Contains(_selectedPore.ID))
                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "⇒ INLET PORE");
            else if (_outletPoreIds.Contains(_selectedPore.ID))
                ImGui.TextColored(new Vector4(0, 0.5f, 1, 1), "⇒ OUTLET PORE");
        }

        // NEW: Show diffusivity if available
        if (_hasDiffusivityData && _localDiffusivity.TryGetValue(_selectedPore.ID, out var diffusivity))
        {
            ImGui.Separator();
            ImGui.Text("Diffusivity:");
            ImGui.Text($"D_local: {diffusivity:E3} m²/s");

            if (_dataset.BulkDiffusivity > 0)
            {
                var restriction = 1.0f - diffusivity / _dataset.BulkDiffusivity;
                ImGui.Text($"Restriction: {restriction:P1}");
            }
        }

        if (ImGui.Button("Deselect"))
        {
            _selectedPoreId = -1;
            _selectedPore = null;
        }
    }

    private Pore FindPoreById(int id)
    {
        if (id < 0) return null;
        for (var i = 0; i < _dataset.Pores.Count; i++)
            if (_dataset.Pores[i].ID == id)
                return _dataset.Pores[i];
        return null;
    }

    public void ResetCamera()
    {
        if (_dataset.Pores.Any())
        {
            // Include the largest possible rendered pore extent (the UI scale reaches 5x and the
            // shader applies a 0.1 radius factor). Position-only bounds collapse to zero for a
            // one-pore PNM, placing the camera inside a giant sphere and making zoom's clamp invalid.
            const float maximumRenderedRadiusFactor = 0.5f;
            var min = new Vector3(
                _dataset.Pores.Min(p => p.Position.X - SafeRadius(p) * maximumRenderedRadiusFactor),
                _dataset.Pores.Min(p => p.Position.Y - SafeRadius(p) * maximumRenderedRadiusFactor),
                _dataset.Pores.Min(p => p.Position.Z - SafeRadius(p) * maximumRenderedRadiusFactor));
            var max = new Vector3(
                _dataset.Pores.Max(p => p.Position.X + SafeRadius(p) * maximumRenderedRadiusFactor),
                _dataset.Pores.Max(p => p.Position.Y + SafeRadius(p) * maximumRenderedRadiusFactor),
                _dataset.Pores.Max(p => p.Position.Z + SafeRadius(p) * maximumRenderedRadiusFactor));
            _modelCenter = (min + max) / 2.0f;
            _modelRadius = Math.Max(0.1f, Vector3.Distance(min, max) / 2.0f);
            _cameraDistance = _modelRadius * 2.5f;
        }
        else
        {
            _modelCenter = Vector3.Zero;
            _modelRadius = 10.0f;
            _cameraDistance = 25.0f;
        }

        _cameraTarget = _modelCenter;
        _cameraYaw = -MathF.PI / 4f;
        _cameraPitch = MathF.PI / 6f;
        UpdateCameraMatrices();
    }

    private static float SafeRadius(Pore pore) =>
        float.IsFinite(pore.Radius) && pore.Radius > 0 ? pore.Radius : 0.1f;

    #endregion

    #region Derived Statistics

    /// <summary>
    ///     Network figures derived from the visible pores/throats. Radii and volumes are stored in
    ///     physical units here: pores and throats carry voxel coordinates, and the conversion to
    ///     micrometres belongs at the presentation boundary.
    /// </summary>
    private readonly struct NetworkStatistics
    {
        public int PoreCount { get; init; }
        public int ThroatCount { get; init; }
        public int IsolatedPoreCount { get; init; }

        public float MinCoordination { get; init; }
        public float MeanCoordination { get; init; }
        public float MaxCoordination { get; init; }

        public float MinPoreRadiusUm { get; init; }
        public float MeanPoreRadiusUm { get; init; }
        public float MaxPoreRadiusUm { get; init; }

        public float MinThroatRadiusUm { get; init; }
        public float MeanThroatRadiusUm { get; init; }
        public float MaxThroatRadiusUm { get; init; }

        public double PoreVolumeUm3 { get; init; }
        public double BulkVolumeUm3 { get; init; }
        public double Porosity { get; init; }

        /// <summary> Bounding box of the pore centres; it excludes the pore radii at either end. </summary>
        public Vector3 ExtentUm { get; init; }
    }

    /// <summary> Voxel edge in µm, falling back to 1 so unscaled datasets read out in voxel units. </summary>
    private float EffectiveVoxelSize => _dataset.VoxelSize > 0 ? _dataset.VoxelSize : 1f;

    /// <summary>
    ///     Physical pore volume, falling back to voxel counting when the generator left it unset.
    ///     VoxelSize is the geometric mean of the three voxel edges, so VoxelSize³ equals vx·vy·vz
    ///     and this fallback agrees exactly with the VolumePhysical the generator computes.
    /// </summary>
    private double PhysicalVolume(Pore pore)
    {
        if (pore.VolumePhysical > 0) return pore.VolumePhysical;
        return pore.VolumeVoxels * Math.Pow(EffectiveVoxelSize, 3);
    }

    private void RecomputeStatistics()
    {
        _poreAdjacency.Clear();

        var pores = _dataset.Pores;
        var throats = _dataset.Throats;
        if (pores == null || pores.Count == 0)
        {
            _statistics = default;
            return;
        }

        foreach (var throat in throats ?? new List<Throat>())
        {
            if (!_poreAdjacency.TryGetValue(throat.Pore1ID, out var first))
                _poreAdjacency[throat.Pore1ID] = first = new List<Throat>();
            first.Add(throat);

            if (!_poreAdjacency.TryGetValue(throat.Pore2ID, out var second))
                _poreAdjacency[throat.Pore2ID] = second = new List<Throat>();
            second.Add(throat);
        }

        var voxelSize = EffectiveVoxelSize;
        double poreVolume = 0;
        double radiusSum = 0;
        long connectionSum = 0;
        var isolated = 0;
        var minRadius = float.MaxValue;
        var maxRadius = 0f;
        var minConnections = int.MaxValue;
        var maxConnections = 0;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var pore in pores)
        {
            // Coordination is counted from the visible throats rather than from Pore.Connections:
            // the generator sets that field to the degree in the complete network and ApplyFilter
            // never recomputes it, so under a filter it would contradict the throats on screen.
            var degree = _poreAdjacency.TryGetValue(pore.ID, out var incident) ? incident.Count : 0;

            poreVolume += PhysicalVolume(pore);
            radiusSum += pore.Radius;
            connectionSum += degree;
            if (degree == 0) isolated++;
            minRadius = Math.Min(minRadius, pore.Radius);
            maxRadius = Math.Max(maxRadius, pore.Radius);
            minConnections = Math.Min(minConnections, degree);
            maxConnections = Math.Max(maxConnections, degree);
            min = Vector3.Min(min, pore.Position);
            max = Vector3.Max(max, pore.Position);
        }

        double throatRadiusSum = 0;
        var minThroat = float.MaxValue;
        var maxThroat = 0f;
        foreach (var throat in throats ?? new List<Throat>())
        {
            throatRadiusSum += throat.Radius;
            minThroat = Math.Min(minThroat, throat.Radius);
            maxThroat = Math.Max(maxThroat, throat.Radius);
        }

        var throatCount = throats?.Count ?? 0;
        var bulkVolume = (double)_dataset.ImageWidth * _dataset.ImageHeight * _dataset.ImageDepth *
                         Math.Pow(voxelSize, 3);

        _statistics = new NetworkStatistics
        {
            PoreCount = pores.Count,
            ThroatCount = throatCount,
            IsolatedPoreCount = isolated,
            MinCoordination = minConnections == int.MaxValue ? 0 : minConnections,
            MeanCoordination = (float)connectionSum / pores.Count,
            MaxCoordination = maxConnections,
            MinPoreRadiusUm = (minRadius == float.MaxValue ? 0 : minRadius) * voxelSize,
            MeanPoreRadiusUm = (float)(radiusSum / pores.Count) * voxelSize,
            MaxPoreRadiusUm = maxRadius * voxelSize,
            MinThroatRadiusUm = (minThroat == float.MaxValue ? 0 : minThroat) * voxelSize,
            MeanThroatRadiusUm = throatCount > 0 ? (float)(throatRadiusSum / throatCount) * voxelSize : 0,
            MaxThroatRadiusUm = maxThroat * voxelSize,
            PoreVolumeUm3 = poreVolume,
            BulkVolumeUm3 = bulkVolume,
            Porosity = bulkVolume > 0 ? poreVolume / bulkVolume : 0,
            ExtentUm = (max - min) * voxelSize
        };
    }

    #endregion
}

internal static class Vector3Extensions
{
    public static Vector3 Normalized(this Vector3 v)
    {
        return Vector3.Normalize(v);
    }
}
