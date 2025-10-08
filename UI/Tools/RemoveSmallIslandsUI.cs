// GeoscientistToolkit/Analysis/RemoveSmallIslands/RemoveSmallIslandsUI.cs

using System.Collections.Concurrent;
using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.RemoveSmallIslands;

/// <summary>
///     Provides the UI and logic for removing small material islands.
/// </summary>
public class RemoveSmallIslandsUI : IDisposable
{
    private readonly IslandAnalysisProcessor _processor;
    private readonly string[] _unitOptions = { "cm³", "mm³", "µm³", "voxels" };
    private CancellationTokenSource _cancellationTokenSource;
    private bool _isProcessing;
    private IslandAnalysisResult _lastAnalysisResult;

    private int _selectedMaterialIndex;
    private int _selectedUnitIndex = 3;
    private bool _showPreview;
    private float _sizeThreshold = 100.0f;

    public RemoveSmallIslandsUI()
    {
        _processor = new IslandAnalysisProcessor();
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _processor?.Dispose();
    }

    public void DrawPanel(CtImageStackDataset dataset)
    {
        if (dataset == null) return;

        ImGui.Text("Target Material:");
        var materialNames = dataset.Materials.Where(m => m.ID != 0).Select(m => m.Name).ToArray();
        if (!materialNames.Any())
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
                CtImageStackTools.Update3DPreviewFromExternal(dataset, null, Vector4.Zero);
        }

        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Shows a 3D preview of particles to be removed (in red).");

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
        if (_isProcessing || !dataset.Materials.Where(m => m.ID != 0).Any()) return;
        _isProcessing = true;
        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;

        try
        {
            var targetMaterial = dataset.Materials.Where(m => m.ID != 0).ElementAt(_selectedMaterialIndex);
            var voxelThreshold = ConvertThresholdToVoxels(dataset);
            _lastAnalysisResult = await Task.Run(() => _processor.Analyze(dataset, targetMaterial, token), token);
            token.ThrowIfCancellationRequested();

            _processor.CurrentStage = "Filtering small particles";
            _processor.Progress = 0.8f;
            var smallParticleIds = new HashSet<int>(_lastAnalysisResult.Particles
                .Where(p => p.VoxelCount < voxelThreshold).Select(p => p.Id));
            token.ThrowIfCancellationRequested();

            if (isPreviewOnly)
            {
                _processor.CurrentStage = "Generating preview";
                _processor.Progress = 0.9f;
                var previewMask =
                    await Task.Run(() => GeneratePreviewMask(_lastAnalysisResult.LabelVolume, smallParticleIds), token);
                CtImageStackTools.Update3DPreviewFromExternal(dataset, previewMask, new Vector4(1, 0, 0, 0.5f));
            }
            else
            {
                CtImageStackTools.Update3DPreviewFromExternal(dataset, null, Vector4.Zero);
                _processor.CurrentStage = "Applying changes";
                _processor.Progress = 0.9f;
                await Task.Run(
                    () => ApplyCleaning(dataset.LabelData, _lastAnalysisResult.LabelVolume, smallParticleIds, token),
                    token);
                ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
                ProjectManager.Instance.HasUnsavedChanges = true;
                Logger.Log(
                    $"[RemoveSmallIslands] Cleaned {smallParticleIds.Count} particles from material '{targetMaterial.Name}'.");
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Log("[RemoveSmallIslands] Operation cancelled.");
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
        }
    }

    private long ConvertThresholdToVoxels(CtImageStackDataset dataset)
    {
        if (_selectedUnitIndex == 3) return (long)_sizeThreshold;
        double voxelVolumeUm3 = dataset.PixelSize * dataset.PixelSize * dataset.SliceThickness;
        if (voxelVolumeUm3 < 1e-9) return long.MaxValue;
        var thresholdUm3 = _sizeThreshold * _selectedUnitIndex switch { 0 => 1e12, 1 => 1e9, _ => 1.0 };
        return (long)(thresholdUm3 / voxelVolumeUm3);
    }

    private byte[] GeneratePreviewMask(int[,,] labelVolume, HashSet<int> smallParticleIds)
    {
        int w = labelVolume.GetLength(0), h = labelVolume.GetLength(1), d = labelVolume.GetLength(2);
        var mask = new byte[(long)w * h * d];
        Parallel.For(0, d, z =>
        {
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                if (smallParticleIds.Contains(labelVolume[x, y, z]))
                    mask[(long)z * w * h + (long)y * w + x] = 255;
        });
        return mask;
    }

    private void ApplyCleaning(ChunkedLabelVolume datasetLabels, int[,,] islandLabels, HashSet<int> smallParticleIds,
        CancellationToken token)
    {
        var d = islandLabels.GetLength(2);
        Parallel.For(0, d, new ParallelOptions { CancellationToken = token }, z =>
        {
            for (var y = 0; y < islandLabels.GetLength(1); y++)
            for (var x = 0; x < islandLabels.GetLength(0); x++)
                if (smallParticleIds.Contains(islandLabels[x, y, z]))
                    datasetLabels[x, y, z] = 0;

            _processor.Progress = 0.9f + 0.1f * (z + 1) / d;
        });
    }
}

internal class IslandAnalysisResult
{
    public int[,,] LabelVolume;
    public List<IslandParticle> Particles;
}

internal class IslandParticle
{
    public int Id;
    public int VoxelCount;
}

internal class IslandAnalysisProcessor : IDisposable
{
    public float Progress { get; internal set; }
    public string CurrentStage { get; set; } = "";

    public void Dispose()
    {
    }

    public IslandAnalysisResult Analyze(CtImageStackDataset dataset, Material material, CancellationToken token)
    {
        CurrentStage = "Extracting mask";
        Progress = 0;
        var mask = ExtractMaterialMask(dataset, material, token);
        token.ThrowIfCancellationRequested();
        CurrentStage = "Labeling components";
        Progress = 0.2f;
        var labels = LabelComponents3DParallel(mask, token);
        token.ThrowIfCancellationRequested();
        CurrentStage = "Analyzing particles";
        Progress = 0.6f;
        var particles = AnalyzeParticles(labels, token);
        token.ThrowIfCancellationRequested();
        CurrentStage = "Complete";
        Progress = 1.0f;
        return new IslandAnalysisResult { LabelVolume = labels, Particles = particles };
    }

    private byte[,,] ExtractMaterialMask(CtImageStackDataset d, Material m, CancellationToken t)
    {
        var mask = new byte[d.Width, d.Height, d.Depth];
        Parallel.For(0, d.Depth, new ParallelOptions { CancellationToken = t }, z =>
        {
            for (var y = 0; y < d.Height; y++)
            for (var x = 0; x < d.Width; x++)
                if (d.LabelData[x, y, z] == m.ID)
                    mask[x, y, z] = 1;
            Progress = 0.2f * (z + 1) / d.Depth;
        });
        return mask;
    }

    private int[,,] LabelComponents3DParallel(byte[,,] mask, CancellationToken token)
    {
        int w = mask.GetLength(0), h = mask.GetLength(1), d = mask.GetLength(2);
        var labels = new int[w, h, d];
        var uf = new ConcurrentUnionFind();
        var next = 1;
        Parallel.For(0, d, new ParallelOptions { CancellationToken = token }, z =>
        {
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                if (mask[x, y, z] == 0) continue;
                var n = new List<int>();
                if (x > 0 && labels[x - 1, y, z] > 0) n.Add(labels[x - 1, y, z]);
                if (y > 0 && labels[x, y - 1, z] > 0) n.Add(labels[x, y - 1, z]);
                if (z > 0 && labels[x, y, z - 1] > 0) n.Add(labels[x, y, z - 1]);
                if (n.Count == 0)
                {
                    var l = Interlocked.Increment(ref next) - 1;
                    labels[x, y, z] = l;
                    uf.MakeSet(l);
                }
                else
                {
                    var min = n.Min();
                    labels[x, y, z] = min;
                    foreach (var neighbor in n) uf.Union(min, neighbor);
                }
            }

            Progress = 0.2f + 0.3f * (z + 1) / d;
        });
        token.ThrowIfCancellationRequested();
        var final = new ConcurrentDictionary<int, int>();
        var counter = 0;
        Parallel.For(0, d, new ParallelOptions { CancellationToken = token }, z =>
        {
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                if (labels[x, y, z] > 0)
                {
                    var root = uf.Find(labels[x, y, z]);
                    if (!final.ContainsKey(root)) final.TryAdd(root, Interlocked.Increment(ref counter));
                    labels[x, y, z] = final[root];
                }

            Progress = 0.5f + 0.1f * (z + 1) / d;
        });
        return labels;
    }

    private List<IslandParticle> AnalyzeParticles(int[,,] labels, CancellationToken token)
    {
        var particles = new ConcurrentDictionary<int, int>();
        var d = labels.GetLength(2);
        Parallel.For(0, d, new ParallelOptions { CancellationToken = token }, z =>
        {
            for (var y = 0; y < labels.GetLength(1); y++)
            for (var x = 0; x < labels.GetLength(0); x++)
            {
                var l = labels[x, y, z];
                if (l > 0) particles.AddOrUpdate(l, 1, (k, c) => c + 1);
            }

            Progress = 0.6f + 0.2f * (z + 1) / d;
        });
        return particles.Select(kvp => new IslandParticle { Id = kvp.Key, VoxelCount = kvp.Value }).ToList();
    }

    private class ConcurrentUnionFind
    {
        private readonly ConcurrentDictionary<int, int> p = new();

        public void MakeSet(int x)
        {
            p.TryAdd(x, x);
        }

        public int Find(int x)
        {
            if (!p.TryGetValue(x, out var parent))
            {
                MakeSet(x);
                return x;
            }

            if (parent == x) return x;
            return p[x] = Find(parent);
        }

        public void Union(int x, int y)
        {
            int rX = Find(x), rY = Find(y);
            if (rX != rY) p[Math.Max(rX, rY)] = Math.Min(rX, rY);
        }
    }
}