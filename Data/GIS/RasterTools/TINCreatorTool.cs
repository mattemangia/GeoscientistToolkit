// GeoscientistToolkit/Business/GIS/RasterTools/TINCreatorTool.cs

using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Business.GIS.RasterTools;

public class TINCreatorTool : IDatasetTools
{
    private CancellationTokenSource _cts;
    private bool _isGenerating;
    private float _progress;
    private int _samplePointCount = 2000;
    private GISRasterLayer _selectedRasterLayer;
    private string _status;

    public void Draw(Dataset dataset)
    {
        if (dataset is not GISDataset gisDataset) return;

        ImGui.TextWrapped(
            "Create a Triangulated Irregular Network (TIN) from a raster layer by sampling points and performing Delaunay triangulation.");

        var rasterLayers = gisDataset.Layers.OfType<GISRasterLayer>().ToList();
        if (!rasterLayers.Any())
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "No raster layers available in this dataset.");
            return;
        }

        _selectedRasterLayer ??= rasterLayers.First();

        if (ImGui.BeginCombo("Source Raster", _selectedRasterLayer.Name))
        {
            foreach (var layer in rasterLayers)
                if (ImGui.Selectable(layer.Name, layer == _selectedRasterLayer))
                    _selectedRasterLayer = layer;

            ImGui.EndCombo();
        }

        ImGui.Separator();

        ImGui.Text("Number of Sample Points:");
        ImGui.SliderInt("##SamplePoints", ref _samplePointCount, 500, 10000);

        ImGui.Separator();

        if (_isGenerating)
        {
            ImGui.BeginDisabled();
            ImGui.Button("Generating...", new Vector2(150, 0));
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) _cts?.Cancel();
            ImGui.ProgressBar(_progress, new Vector2(-1, 0), _status);
        }
        else
        {
            if (ImGui.Button("Generate TIN Layer", new Vector2(150, 0)))
                if (_selectedRasterLayer != null)
                    StartTINGeneration(gisDataset);
        }
    }

    private void StartTINGeneration(GISDataset parentDataset)
    {
        _isGenerating = true;
        _progress = 0f;
        _status = "Starting...";
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(() =>
        {
            try
            {
                // Step 1: Sample the most significant points from the DEM
                _status = "Analyzing terrain significance...";
                _progress = 0.05f;

                var demData = _selectedRasterLayer.GetPixelData();
                var bounds = _selectedRasterLayer.Bounds;
                var width = _selectedRasterLayer.Width;
                var height = _selectedRasterLayer.Height;
                var cellWidth = bounds.Width / width;
                var cellHeight = bounds.Height / height;

                // Use a PriorityQueue to efficiently find the N most significant points.
                // It stores points with the lowest significance at the top to easily discard them.
                var topPointsQueue = new PriorityQueue<PointWithSignificance, float>();

                for (var y = 1; y < height - 1; y++)
                {
                    // Update progress periodically and check for cancellation
                    if (y % 100 == 0)
                    {
                        token.ThrowIfCancellationRequested();
                        _progress = 0.05f + 0.4f * (y / (float)height);
                        _status = $"Scanning DEM... (Row {y}/{height})";
                    }

                    for (var x = 1; x < width - 1; x++)
                    {
                        var centerElev = demData[x, y];

                        // Calculate significance based on the maximum vertical difference to neighbors.
                        // This is a simple but effective heuristic to find peaks, pits, and points 
                        // along sharp breaks in slope.
                        float maxDifference = 0;
                        maxDifference = Math.Max(maxDifference, Math.Abs(centerElev - demData[x - 1, y - 1]));
                        maxDifference = Math.Max(maxDifference, Math.Abs(centerElev - demData[x, y - 1]));
                        maxDifference = Math.Max(maxDifference, Math.Abs(centerElev - demData[x + 1, y - 1]));
                        maxDifference = Math.Max(maxDifference, Math.Abs(centerElev - demData[x - 1, y]));
                        maxDifference = Math.Max(maxDifference, Math.Abs(centerElev - demData[x + 1, y]));
                        maxDifference = Math.Max(maxDifference, Math.Abs(centerElev - demData[x - 1, y + 1]));
                        maxDifference = Math.Max(maxDifference, Math.Abs(centerElev - demData[x, y + 1]));
                        maxDifference = Math.Max(maxDifference, Math.Abs(centerElev - demData[x + 1, y + 1]));

                        var significance = maxDifference;

                        // If the queue isn't full, add the point.
                        // If it is full, only add the new point if it's more significant
                        // than the least significant point currently in the queue.
                        if (topPointsQueue.Count < _samplePointCount)
                        {
                            topPointsQueue.Enqueue(new PointWithSignificance(x, y, significance), significance);
                        }
                        else if (significance > topPointsQueue.Peek().Significance)
                        {
                            topPointsQueue.Dequeue(); // Remove the least significant point
                            topPointsQueue.Enqueue(new PointWithSignificance(x, y, significance), significance);
                        }
                    }
                }

                token.ThrowIfCancellationRequested();

                // Convert the top points from the queue to world coordinate vertices
                var vertices = new List<Vertex>();
                var vertexIndex = 0;
                while (topPointsQueue.Count > 0)
                {
                    var p = topPointsQueue.Dequeue();
                    var worldX = bounds.Min.X + (p.X + 0.5f) * cellWidth;
                    var worldY =
                        bounds.Max.Y - (p.Y + 0.5f) * cellHeight; // Y=0 is top of raster, Max.Y is top of bounds
                    vertices.Add(new Vertex(new Vector2(worldX, worldY), vertexIndex++));
                }

                // Always include the four corners of the DEM to ensure the TIN covers the entire extent.
                vertices.Add(new Vertex(bounds.Min, vertexIndex++));
                vertices.Add(new Vertex(new Vector2(bounds.Max.X, bounds.Min.Y), vertexIndex++));
                vertices.Add(new Vertex(new Vector2(bounds.Min.X, bounds.Max.Y), vertexIndex++));
                vertices.Add(new Vertex(bounds.Max, vertexIndex++));

                // Step 2: Triangulate
                _status = "Performing Delaunay triangulation...";
                _progress = 0.5f;
                var triangles = DelaunayTriangulation.Triangulate(vertices.ToList());
                token.ThrowIfCancellationRequested();

                // Step 3: Create features
                _status = "Creating features...";
                _progress = 0.8f;
                var features = new List<GISFeature>();
                foreach (var tri in triangles)
                    features.Add(new GISFeature
                    {
                        Type = FeatureType.Polygon,
                        Coordinates = new List<Vector2>
                            { tri.V1.Position, tri.V2.Position, tri.V3.Position, tri.V1.Position }
                    });
                token.ThrowIfCancellationRequested();

                // Step 4: Add new dataset to project
                _status = "Finalizing...";
                _progress = 0.95f;
                var newLayer = new GISLayer
                {
                    Name = $"{_selectedRasterLayer.Name}_TIN",
                    Type = LayerType.Vector,
                    Features = features,
                    Color = new Vector4(0.8f, 0.5f, 1.0f, 1.0f)
                };

                var newDataset = new GISDataset(newLayer.Name, "")
                {
                    Tags = parentDataset.Tags | GISTag.Generated | GISTag.TIN,
                    Projection = parentDataset.Projection
                };
                newDataset.Layers.Clear();
                newDataset.Layers.Add(newLayer);
                newDataset.UpdateBounds();

                ProjectManager.Instance.AddDataset(newDataset);
                Logger.Log($"Successfully created TIN dataset '{newDataset.Name}' with {features.Count} triangles.");
                _status = "Complete!";
                _progress = 1.0f;
            }
            catch (OperationCanceledException)
            {
                Logger.Log("TIN generation was canceled.");
                _status = "Canceled";
            }
            catch (Exception ex)
            {
                Logger.LogError($"TIN generation failed: {ex.Message}");
                _status = $"Error: {ex.Message}";
            }
            finally
            {
                _isGenerating = false;
                _cts.Dispose();
                _cts = null;
            }
        }, token);
    }

    /// <summary>
    ///     Helper class for the priority queue to track points by their terrain significance.
    /// </summary>
    private class PointWithSignificance
    {
        public PointWithSignificance(int x, int y, float significance)
        {
            X = x;
            Y = y;
            Significance = significance;
        }

        public int X { get; }
        public int Y { get; }
        public float Significance { get; }
    }
}