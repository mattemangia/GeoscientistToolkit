// GeoscientistToolkit/Business/GIS/RasterTools/IsolinesCreatorTool.cs

using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Business.GIS.RasterTools;

public class IsolinesCreatorTool : IDatasetTools
{
    private readonly ImGuiExportFileDialog _exportDialog;
    private bool _autoRange = true;
    private CancellationTokenSource _cts;
    private float _interval = 100f;
    private bool _isGenerating;
    private float _maxElevation = 1000f;
    private float _minElevation;
    private float _progress;
    private GISRasterLayer _selectedRasterLayer;
    private string _status;

    public IsolinesCreatorTool()
    {
        _exportDialog = new ImGuiExportFileDialog("ExportIsolines", "Save Isolines as Shapefile");
        _exportDialog.SetExtensions((".shp", "ESRI Shapefile"));
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not GISDataset gisDataset) return;

        // Handle the save dialog submission which starts the async process
        if (_exportDialog.Submit())
        {
            var path = _exportDialog.SelectedPath;
            StartIsolineGeneration(gisDataset, path);
        }

        ImGui.TextWrapped("Generate contour lines (isolines) from a raster layer and save them as a new shapefile.");

        var rasterLayers = gisDataset.Layers.OfType<GISRasterLayer>().ToList();
        if (!rasterLayers.Any())
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "No raster layers available in this dataset.");
            return;
        }

        if (_selectedRasterLayer == null || !rasterLayers.Contains(_selectedRasterLayer))
        {
            _selectedRasterLayer = rasterLayers.First();
            UpdateElevationRange();
        }

        if (ImGui.BeginCombo("Source Raster", _selectedRasterLayer.Name))
        {
            foreach (var layer in rasterLayers)
                if (ImGui.Selectable(layer.Name, layer == _selectedRasterLayer))
                {
                    _selectedRasterLayer = layer;
                    UpdateElevationRange();
                }

            ImGui.EndCombo();
        }

        ImGui.Separator();

        ImGui.InputFloat("Interval", ref _interval, 1.0f, 10.0f, "%.1f");

        ImGui.Checkbox("Auto-detect Range", ref _autoRange);
        if (_autoRange) ImGui.BeginDisabled();

        ImGui.InputFloat("Min Elevation", ref _minElevation);
        ImGui.InputFloat("Max Elevation", ref _maxElevation);

        if (_autoRange) ImGui.EndDisabled();

        ImGui.Separator();

        if (_isGenerating)
        {
            ImGui.BeginDisabled();
            ImGui.Button("Generating...", new Vector2(250, 0));
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) _cts?.Cancel();
            ImGui.ProgressBar(_progress, new Vector2(-1, 0), _status);
        }
        else
        {
            if (ImGui.Button("Generate and Export Isolines...", new Vector2(250, 0)))
                if (_selectedRasterLayer != null)
                    _exportDialog.Open($"{gisDataset.Name}_{_selectedRasterLayer.Name}_isolines");
        }
    }

    private void UpdateElevationRange()
    {
        if (_selectedRasterLayer == null || !_autoRange) return;

        var demData = _selectedRasterLayer.GetPixelData();
        _minElevation = float.MaxValue;
        _maxElevation = float.MinValue;

        for (var y = 0; y < _selectedRasterLayer.Height; y++)
        for (var x = 0; x < _selectedRasterLayer.Width; x++)
        {
            var elev = demData[x, y];
            if (elev < _minElevation) _minElevation = elev;
            if (elev > _maxElevation) _maxElevation = elev;
        }
    }

    private void StartIsolineGeneration(GISDataset parentDataset, string path)
    {
        _isGenerating = true;
        _progress = 0f;
        _status = "Starting...";
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        var progressHandler = new Progress<(float progress, string message)>(value =>
        {
            _progress = value.progress;
            _status = value.message;
        });

        Task.Run(async () =>
        {
            try
            {
                await PerformIsolineGenerationAsync(parentDataset, path, progressHandler, token);
            }
            catch (OperationCanceledException)
            {
                _status = "Operation Canceled.";
                Logger.Log("Isoline generation was canceled by the user.");
            }
            catch (Exception ex)
            {
                _status = $"Error: {ex.Message}";
                Logger.LogError($"Failed to generate or export isolines: {ex.Message}");
            }
            finally
            {
                _isGenerating = false;
                _cts.Dispose();
                _cts = null;
            }
        }, token);
    }


    private async Task PerformIsolineGenerationAsync(GISDataset parentDataset, string path,
        IProgress<(float progress, string message)> progress, CancellationToken token)
    {
        if (_selectedRasterLayer == null)
            throw new InvalidOperationException("No raster layer selected for isoline generation.");

        progress.Report((0.0f, "Starting isoline generation..."));

        var demData = _selectedRasterLayer.GetPixelData();
        var bounds = _selectedRasterLayer.Bounds;
        var cellSize = bounds.Width / _selectedRasterLayer.Width;

        await Task.Delay(100, token); // Allow UI to update
        token.ThrowIfCancellationRequested();

        progress.Report((0.1f, "Generating contour segments..."));
        var contourSegments =
            await Task.Run(
                () => GISOperationsImpl.GenerateContourLines(demData, _interval, _minElevation, _maxElevation,
                    bounds.Min, cellSize), token);

        if (!contourSegments.Any())
        {
            progress.Report((1.0f, "Completed. No features were generated."));
            Logger.LogWarning("Isoline generation resulted in no features.");
            return;
        }

        token.ThrowIfCancellationRequested();

        progress.Report((0.6f, $"Creating {contourSegments.Count} line features..."));
        var features = contourSegments.Select(segment => new GISFeature
        {
            Type = FeatureType.Line,
            Coordinates = new List<Vector2>(segment)
        }).ToList();

        var newLayer = new GISLayer
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Type = LayerType.Vector,
            Features = features
        };

        var newDataset = new GISDataset(newLayer.Name, path)
        {
            Tags = parentDataset.Tags | GISTag.Generated | GISTag.Contours,
            Projection = parentDataset.Projection
        };
        newDataset.Layers.Clear();
        newDataset.Layers.Add(newLayer);
        newDataset.UpdateBounds();

        // Create a sub-progress handler for the export step
        var exportProgress = new Progress<(float progress, string message)>(value =>
        {
            // Map the 0-1 range of the exporter to the 0.7-0.95 range of the overall process
            var overallProgress = 0.7f + value.progress * 0.25f;
            progress.Report((overallProgress, $"Exporting: {value.message}"));
        });

        await GISExporter.ExportToShapefileAsync(newDataset, path, exportProgress, token);
        token.ThrowIfCancellationRequested();

        progress.Report((0.98f, "Adding dataset to project..."));
        ProjectManager.Instance.AddDataset(newDataset);

        progress.Report((1.0f, $"Export complete: {features.Count} features."));
        Logger.Log(
            $"Successfully created and exported isoline dataset '{newDataset.Name}' with {features.Count} features.");
    }
}