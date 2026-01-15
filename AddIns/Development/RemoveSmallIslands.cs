// GeoscientistToolkit/AddIns/Development/RemoveSmallIslandsAddIn.cs

using System.Collections.Concurrent;
using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.AddIns.RemoveSmallIslands;

/// <summary>
///     Main entry point for the "Remove Small Islands" add-in.
/// </summary>
public class RemoveSmallIslandsAddIn : IAddIn
{
    private static RemoveSmallIslandsTool _tool;
    public string Id => "com.geoscientisttoolkit.removesmallislands";
    public string Name => "Remove Small Islands";
    public string Version => "1.0.0";
    public string Author => "GeoscientistToolkit";
    public string Description => "Removes particles of a material smaller than a specified size threshold.";

    public void Initialize()
    {
        _tool = new RemoveSmallIslandsTool();
    }

    public void Shutdown()
    {
        _tool?.Dispose();
        _tool = null;
    }

    public IEnumerable<AddInTool> GetTools()
    {
        return new[] { _tool };
    }

    public IEnumerable<AddInMenuItem> GetMenuItems()
    {
        return null;
    }

    public IEnumerable<IDataImporter> GetDataImporters()
    {
        return null;
    }

    public IEnumerable<IDataExporter> GetDataExporters()
    {
        return null;
    }
}

/// <summary>
///     The tool that provides the UI and logic for removing small material islands.
/// </summary>
internal class RemoveSmallIslandsTool : AddInTool, IDisposable
{
    private readonly IslandAnalysisProcessor _processor;
    private readonly string[] _unitOptions = { "cm³", "mm³", "µm³", "voxels" };
    private CancellationTokenSource _cancellationTokenSource;
    private bool _isProcessing;
    private IslandAnalysisResult _lastAnalysisResult;

    // UI State
    private int _selectedMaterialIndex;
    private int _selectedUnitIndex = 3; // Default to voxels
    private bool _showPreview;
    private float _sizeThreshold = 100.0f;

    public RemoveSmallIslandsTool()
    {
        _processor = new IslandAnalysisProcessor();
    }

    public override string Name => "Remove Small Islands";
    public override string Icon => "Clean";
    public override string Tooltip => "Clean a material by removing small, disconnected particles.";

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _processor?.Dispose();
    }

    public override bool CanExecute(Dataset dataset)
    {
        return dataset is CtImageStackDataset;
    }

    public override void Execute(Dataset dataset)
    {
    }

    public void DrawPanel(CtImageStackDataset dataset)
    {
        if (dataset == null) return;

        ImGui.Text("Target Material:");
        var materialNames = dataset.Materials.Where(m => m.ID != 0).Select(m => m.Name).ToArray();
        if (materialNames.Length == 0)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "No materials available to clean.");
            return;
        }

        ImGui.SetNextItemWidth(-1);
        ImGui.Combo("##Material", ref _selectedMaterialIndex, materialNames, materialNames.Length);

        ImGui.Spacing();
        ImGui.Text("Remove particles smaller than:");
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 80);
        ImGui.InputFloat("##SizeThreshold", ref _sizeThreshold, 1.0f, 10.0f, "%.2f");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(75);
        ImGui.Combo("##Units", ref _selectedUnitIndex, _unitOptions, _unitOptions.Length);

        ImGui.Spacing();
        if (ImGui.Checkbox("Show Preview", ref _showPreview))
        {
            if (_showPreview)
                _ = ProcessIslandsAsync(dataset, true);
            else
                // --- FIX: Use the new public method to clear the preview ---
                CtImageStackTools.Update3DPreviewFromExternal(dataset, null, Vector4.Zero);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "Shows a real-time 3D preview of the particles that will be removed (highlighted in red).");

        ImGui.Spacing();

        if (_isProcessing)
        {
            ImGui.ProgressBar(_processor.Progress, new Vector2(-1, 0),
                $"{_processor.CurrentStage} ({_processor.Progress * 100:F1}%)");
            if (ImGui.Button("Cancel", new Vector2(-1, 0))) _cancellationTokenSource?.Cancel();
        }
        else
        {
            if (ImGui.Button("Clean Material", new Vector2(-1, 0))) _ = ProcessIslandsAsync(dataset, false);
        }
    }

    private async Task ProcessIslandsAsync(CtImageStackDataset dataset, bool isPreviewOnly)
    {
        if (_isProcessing) return;
        if (_selectedMaterialIndex < 0 || _selectedMaterialIndex >= dataset.Materials.Count(m => m.ID != 0)) return;

        _isProcessing = true;
        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;

        try
        {
            var targetMaterial = dataset.Materials.Where(m => m.ID != 0).ElementAt(_selectedMaterialIndex);
            var voxelThreshold = ConvertThresholdToVoxels(dataset);

            // Step 1: Analyze the material to identify all particles
            _lastAnalysisResult = await Task.Run(() =>
                _processor.Analyze(dataset, targetMaterial, token), token);

            if (token.IsCancellationRequested) return;

            // Step 2: Identify particles smaller than the threshold
            _processor.CurrentStage = "Filtering small particles";
            _processor.Progress = 0.8f; // --- FIX: Now works due to internal setter ---
            var smallParticleIds = new HashSet<int>(_lastAnalysisResult.Particles
                .Where(p => p.VoxelCount < voxelThreshold)
                .Select(p => p.Id));

            if (token.IsCancellationRequested) return;

            // Step 3: Generate preview or apply changes
            if (isPreviewOnly)
            {
                _processor.CurrentStage = "Generating preview";
                _processor.Progress = 0.9f; // --- FIX: Now works due to internal setter ---
                var previewMask = await Task.Run(() =>
                    GeneratePreviewMask(_lastAnalysisResult.LabelVolume, smallParticleIds), token);

                // --- FIX: Use the new public method to show the preview ---
                CtImageStackTools.Update3DPreviewFromExternal(dataset, previewMask, new Vector4(1, 0, 0, 0.5f));
            }
            else
            {
                // --- FIX: Use the new public method to turn off the preview ---
                CtImageStackTools.Update3DPreviewFromExternal(dataset, null, Vector4.Zero);

                _processor.CurrentStage = "Applying changes";
                _processor.Progress = 0.9f; // --- FIX: Now works due to internal setter ---
                await Task.Run(() =>
                    ApplyCleaning(dataset.LabelData, _lastAnalysisResult.LabelVolume, smallParticleIds), token);

                // Notify the application that the dataset has been modified
                ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
                ProjectManager.Instance.HasUnsavedChanges = true;
                Logger.Log(
                    $"[RemoveSmallIslands] Cleaned {smallParticleIds.Count} particles from material '{targetMaterial.Name}'.");
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Log("[RemoveSmallIslands] Operation cancelled.");
            // --- FIX: Use the new public method to clear the preview on cancel ---
            CtImageStackTools.Update3DPreviewFromExternal(dataset, null, Vector4.Zero);
        }
        catch (Exception ex)
        {
            Logger.LogError($"[RemoveSmallIslands] An error occurred: {ex.Message}");
        }
        finally
        {
            _isProcessing = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private long ConvertThresholdToVoxels(CtImageStackDataset dataset)
    {
        if (_selectedUnitIndex == 3) // voxels
            return (long)_sizeThreshold;

        double voxelVolumeUm3 = dataset.PixelSize * dataset.PixelSize * dataset.SliceThickness;
        if (voxelVolumeUm3 < 1e-9) return long.MaxValue; // Avoid division by zero

        double thresholdUm3 = _sizeThreshold;
        switch (_selectedUnitIndex)
        {
            case 0: // cm³ to µm³
                thresholdUm3 *= 1e12;
                break;
            case 1: // mm³ to µm³
                thresholdUm3 *= 1e9;
                break;
            case 2: // µm³
                // No conversion needed
                break;
        }

        return (long)(thresholdUm3 / voxelVolumeUm3);
    }

    private byte[] GeneratePreviewMask(int[,,] labelVolume, HashSet<int> smallParticleIds)
    {
        var width = labelVolume.GetLength(0);
        var height = labelVolume.GetLength(1);
        var depth = labelVolume.GetLength(2);
        var mask = new byte[width * height * depth];

        Parallel.For(0, depth, z =>
        {
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var label = labelVolume[x, y, z];
                if (label > 0 && smallParticleIds.Contains(label)) mask[z * width * height + y * width + x] = 255;
            }
        });
        return mask;
    }

    private void ApplyCleaning(ChunkedLabelVolume datasetLabels, int[,,] islandLabels, HashSet<int> smallParticleIds)
    {
        var width = islandLabels.GetLength(0);
        var height = islandLabels.GetLength(1);
        var depth = islandLabels.GetLength(2);

        Parallel.For(0, depth, z =>
        {
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var labelId = islandLabels[x, y, z];
                if (labelId > 0 && smallParticleIds.Contains(labelId))
                    datasetLabels[x, y, z] = 0; // Reassign to exterior
            }

            _processor.Progress = 0.9f + 0.1f * (z + 1) / depth; // --- FIX: Now works due to internal setter ---
        });
    }
}

#region High-Performance Processing Classes (Self-Contained)

/// <summary>
///     Result object for the island analysis process.
/// </summary>
internal class IslandAnalysisResult
{
    public int[,,] LabelVolume { get; set; }
    public List<IslandParticle> Particles { get; set; }
}

/// <summary>
///     High-performance processor with multiple acceleration methods.
/// </summary>
internal class IslandAnalysisProcessor : IDisposable
{
    // --- FIX: Changed private set to internal set ---
    public float Progress { get; internal set; }
    public string CurrentStage { get; set; } = "";
    private int ThreadCount { get; } = Environment.ProcessorCount;

    public void Dispose()
    {
    }

    public IslandAnalysisResult Analyze(CtImageStackDataset dataset, Material material, CancellationToken token)
    {
        Progress = 0.0f;
        CurrentStage = "Extracting mask";

        var mask = ExtractMaterialMask(dataset, material, token);
        token.ThrowIfCancellationRequested();

        Progress = 0.2f;
        CurrentStage = "Labeling components";
        var labels = LabelComponents3DParallel(mask, token);
        token.ThrowIfCancellationRequested();

        Progress = 0.6f;
        CurrentStage = "Analyzing particles";
        var particles = AnalyzeParticles(labels, token);
        token.ThrowIfCancellationRequested();

        Progress = 1.0f;
        CurrentStage = "Complete";

        return new IslandAnalysisResult { LabelVolume = labels, Particles = particles };
    }

    private byte[,,] ExtractMaterialMask(CtImageStackDataset dataset, Material material, CancellationToken token)
    {
        var width = dataset.Width;
        var height = dataset.Height;
        var depth = dataset.Depth;
        var mask = new byte[width, height, depth];

        Parallel.For(0, depth, new ParallelOptions { CancellationToken = token }, z =>
        {
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                if (dataset.LabelData[x, y, z] == material.ID)
                    mask[x, y, z] = 1;

            Progress = 0.2f * (z + 1) / depth;
        });

        return mask;
    }

    private int[,,] LabelComponents3DParallel(byte[,,] mask, CancellationToken token)
    {
        var width = mask.GetLength(0);
        var height = mask.GetLength(1);
        var depth = mask.GetLength(2);
        var labels = new int[width, height, depth];
        var unionFind = new ConcurrentUnionFind();
        var nextLabel = 1;

        // First Pass: Initial labeling and equivalence recording
        Parallel.For(0, depth, new ParallelOptions { CancellationToken = token }, z =>
        {
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                if (mask[x, y, z] == 0) continue;

                var neighbors = new List<int>(3);
                if (x > 0 && labels[x - 1, y, z] > 0) neighbors.Add(labels[x - 1, y, z]);
                if (y > 0 && labels[x, y - 1, z] > 0) neighbors.Add(labels[x, y - 1, z]);
                if (z > 0 && labels[x, y, z - 1] > 0) neighbors.Add(labels[x, y, z - 1]);

                if (neighbors.Count == 0)
                {
                    var label = Interlocked.Increment(ref nextLabel) - 1;
                    labels[x, y, z] = label;
                    unionFind.MakeSet(label);
                }
                else
                {
                    var minLabel = neighbors.Min();
                    labels[x, y, z] = minLabel;
                    foreach (var neighbor in neighbors) unionFind.Union(minLabel, neighbor);
                }
            }

            Progress = 0.2f + 0.3f * (z + 1) / depth;
        });

        token.ThrowIfCancellationRequested();

        // Second Pass: Resolve equivalences
        var finalLabels = new ConcurrentDictionary<int, int>();
        var finalLabelCounter = 0;

        Parallel.For(0, depth, new ParallelOptions { CancellationToken = token }, z =>
        {
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                if (labels[x, y, z] > 0)
                {
                    var root = unionFind.Find(labels[x, y, z]);
                    if (!finalLabels.ContainsKey(root))
                        finalLabels.TryAdd(root, Interlocked.Increment(ref finalLabelCounter));
                    labels[x, y, z] = finalLabels[root];
                }

            Progress = 0.5f + 0.1f * (z + 1) / depth;
        });

        return labels;
    }

    private List<IslandParticle> AnalyzeParticles(int[,,] labels, CancellationToken token)
    {
        var particles = new ConcurrentDictionary<int, int>();
        var width = labels.GetLength(0);
        var height = labels.GetLength(1);
        var depth = labels.GetLength(2);

        Parallel.For(0, depth, new ParallelOptions { CancellationToken = token }, z =>
        {
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var label = labels[x, y, z];
                if (label > 0) particles.AddOrUpdate(label, 1, (key, count) => count + 1);
            }

            Progress = 0.6f + 0.2f * (z + 1) / depth;
        });

        return particles.Select(kvp => new IslandParticle { Id = kvp.Key, VoxelCount = kvp.Value }).ToList();
    }
}

/// <summary>
///     Thread-safe Union-Find for parallel processing.
/// </summary>
internal class ConcurrentUnionFind
{
    private readonly ConcurrentDictionary<int, int> _parent = new();

    public void MakeSet(int x)
    {
        _parent.TryAdd(x, x);
    }

    public int Find(int x)
    {
        if (!_parent.TryGetValue(x, out var parent))
        {
            MakeSet(x);
            return x;
        }

        if (parent == x) return x;
        return _parent[x] = Find(parent); // Path compression
    }

    public void Union(int x, int y)
    {
        var rootX = Find(x);
        var rootY = Find(y);
        if (rootX == rootY) return;

        // Simple union by making the smaller root point to the larger one
        if (rootX < rootY)
            _parent[rootY] = rootX;
        else
            _parent[rootX] = rootY;
    }
}

/// <summary>
///     Simple data structure for particle properties.
/// </summary>
internal class IslandParticle
{
    public int Id { get; set; }
    public int VoxelCount { get; set; }
}

#endregion
