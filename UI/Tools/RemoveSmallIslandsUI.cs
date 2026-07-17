// GAIA/Analysis/RemoveSmallIslands/RemoveSmallIslandsUI.cs

using System.Collections.Concurrent;
using System.Numerics;
using GAIA.Business;
using GAIA.Data.CtImageStack;
using GAIA.Data.VolumeData;
using GAIA.Util;
using ImGuiNET;

namespace GAIA.Analysis.RemoveSmallIslands;

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
                    await Task.Run(() => _processor.GeneratePreviewMask(dataset, _lastAnalysisResult,
                        smallParticleIds, token), token);
                CtImageStackTools.UpdatePreviewVolumeFromExternal(dataset, previewMask,
                    new Vector4(1, 0, 0, 0.5f));
            }
            else
            {
                CtImageStackTools.Update3DPreviewFromExternal(dataset, null, Vector4.Zero);
                _processor.CurrentStage = "Applying changes";
                _processor.Progress = 0.9f;
                await Task.Run(
                    () => _processor.ApplyCleaning(dataset, _lastAnalysisResult, smallParticleIds, token),
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

}

internal class IslandAnalysisResult
{
    internal RunUnionFind Components;
    public byte MaterialId;
    public List<IslandParticle> Particles;
}

internal class IslandParticle
{
    public int Id;
    public long VoxelCount;
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
        CurrentStage = "Scanning connected material runs";
        Progress = 0;
        var components = new RunUnionFind();
        ScanRuns(dataset, material.ID, components, null, token,
            value => Progress = value * .55f);
        token.ThrowIfCancellationRequested();
        CurrentStage = "Counting connected components";
        var counts = new Dictionary<int, long>();
        ScanRuns(dataset, material.ID, components, run =>
        {
            var root = components.Find(run.Label);
            counts[root] = counts.GetValueOrDefault(root) + run.EndX - run.StartX;
        }, token, value => Progress = .55f + value * .25f);
        token.ThrowIfCancellationRequested();
        CurrentStage = "Complete";
        Progress = 1.0f;
        return new IslandAnalysisResult
        {
            Components = components,
            MaterialId = material.ID,
            Particles = counts.Select(pair => new IslandParticle { Id = pair.Key, VoxelCount = pair.Value }).ToList()
        };
    }

    public CtPreviewVolume GeneratePreviewMask(CtImageStackDataset dataset, IslandAnalysisResult analysis,
        HashSet<int> selectedRoots, CancellationToken token)
    {
        var slices = new Dictionary<int, byte[]>();
        ScanRuns(dataset, analysis.MaterialId, analysis.Components, run =>
        {
            if (!selectedRoots.Contains(analysis.Components.Find(run.Label))) return;
            if (!slices.TryGetValue(run.Z, out var slice))
                slices[run.Z] = slice = new byte[checked(dataset.Width * dataset.Height)];
            slice.AsSpan(run.Y * dataset.Width + run.StartX, run.EndX - run.StartX).Fill(255);
        }, token, value => Progress = .8f + value * .2f);
        return new SparseSliceCtPreviewVolume(dataset.Width, dataset.Height, dataset.Depth, slices);
    }

    public void ApplyCleaning(CtImageStackDataset dataset, IslandAnalysisResult analysis,
        HashSet<int> selectedRoots, CancellationToken token)
    {
        var currentZ = -1;
        byte[] slice = null;
        bool[] changedChunks = null;
        void FlushSlice()
        {
            if (currentZ < 0 || !changedChunks.Any(value => value)) return;
            dataset.LabelData.WriteSliceZChangedChunks(currentZ, slice, changedChunks);
        }
        ScanRuns(dataset, analysis.MaterialId, analysis.Components, run =>
        {
            if (run.Z != currentZ)
            {
                FlushSlice();
                currentZ = run.Z;
                slice = new byte[dataset.Width * dataset.Height];
                dataset.LabelData.ReadSliceZ(currentZ, slice);
                changedChunks = new bool[dataset.LabelData.ChunkCountX * dataset.LabelData.ChunkCountY];
            }
            if (!selectedRoots.Contains(analysis.Components.Find(run.Label))) return;
            slice.AsSpan(run.Y * dataset.Width + run.StartX, run.EndX - run.StartX).Clear();
            var firstChunk = run.StartX / dataset.LabelData.ChunkDim;
            var lastChunk = (run.EndX - 1) / dataset.LabelData.ChunkDim;
            var chunkY = run.Y / dataset.LabelData.ChunkDim;
            for (var cx = firstChunk; cx <= lastChunk; cx++)
                changedChunks[chunkY * dataset.LabelData.ChunkCountX + cx] = true;
        }, token, value => Progress = .9f + value * .1f);
        FlushSlice();
    }

    private readonly record struct MaterialRun(int Z, int Y, int StartX, int EndX, int Label);

    private static void ScanRuns(CtImageStackDataset dataset, byte materialId, RunUnionFind components,
        Action<MaterialRun> visitor, CancellationToken token, Action<float> progress)
    {
        var previousSlice = new List<MaterialRun>[dataset.Height];
        var nextLabel = 1;
        var slice = new byte[checked(dataset.Width * dataset.Height)];
        for (var z = 0; z < dataset.Depth; z++)
        {
            token.ThrowIfCancellationRequested();
            dataset.LabelData.ReadSliceZ(z, slice);
            var currentSlice = new List<MaterialRun>[dataset.Height];
            List<MaterialRun> previousRow = null;
            for (var y = 0; y < dataset.Height; y++)
            {
                var rowRuns = new List<MaterialRun>();
                var x = 0;
                while (x < dataset.Width)
                {
                    while (x < dataset.Width && slice[y * dataset.Width + x] != materialId) x++;
                    if (x == dataset.Width) break;
                    var start = x++;
                    while (x < dataset.Width && slice[y * dataset.Width + x] == materialId) x++;
                    var end = x;
                    var neighbors = EnumerateOverlaps(previousRow, start, end)
                        .Concat(EnumerateOverlaps(previousSlice[y], start, end)).Select(run => run.Label).ToArray();
                    int label;
                    if (neighbors.Length == 0)
                    {
                        label = nextLabel++;
                        components.Ensure(label);
                    }
                    else
                    {
                        label = neighbors.Min();
                        components.Ensure(label);
                        foreach (var neighbor in neighbors) components.Union(label, neighbor);
                    }
                    var run = new MaterialRun(z, y, start, end, label);
                    rowRuns.Add(run);
                    visitor?.Invoke(run);
                }
                currentSlice[y] = rowRuns;
                previousRow = rowRuns;
            }
            previousSlice = currentSlice;
            progress?.Invoke((z + 1f) / dataset.Depth);
        }
    }

    private static IEnumerable<MaterialRun> EnumerateOverlaps(List<MaterialRun> runs, int start, int end)
    {
        if (runs == null) yield break;
        foreach (var run in runs)
        {
            if (run.EndX <= start) continue;
            if (run.StartX >= end) yield break;
            yield return run;
        }
    }
}

internal sealed class RunUnionFind
{
    private readonly List<int> _parents = new() { 0 };
    public void Ensure(int label) { while (_parents.Count <= label) _parents.Add(_parents.Count); }
    public int Find(int label)
    {
        Ensure(label);
        var root = label;
        while (_parents[root] != root) root = _parents[root];
        while (_parents[label] != label) { var next = _parents[label]; _parents[label] = root; label = next; }
        return root;
    }
    public void Union(int a, int b)
    {
        var ra = Find(a); var rb = Find(b);
        if (ra == rb) return;
        _parents[Math.Max(ra, rb)] = Math.Min(ra, rb);
    }
}
