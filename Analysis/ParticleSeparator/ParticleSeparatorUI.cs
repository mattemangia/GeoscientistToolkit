// GAIA/Analysis/ParticleSeparator/ParticleSeparatorUI.cs

using System.Numerics;
using GAIA.Business;
using GAIA.Data.CtImageStack;
using GAIA.Util;
using ImGuiNET;

namespace GAIA.Analysis.ParticleSeparator;

/// <summary>
///     Provides the UI and logic for separating and analyzing particles in CT data.
/// </summary>
public class ParticleSeparatorUI : IDisposable
{
    private readonly ParticleSeparationFilters _filters = new();
    private readonly AcceleratedProcessor _processor;
    private CancellationTokenSource _cancellationTokenSource;
    private bool _isProcessing;
    private CtOperationHandle _labelingOperation;
    private ParticleSeparationResult _lastResult;
    private float _minParticleSize = 10.0f;
    private AccelerationType _selectedAcceleration = AccelerationType.Auto;

    // Labeling
    private bool _groupRemainingParticles = true;
    private int _maxParticleMaterials = 10;

    // UI State
    private int _selectedMaterialIndex;
    private int _selectedParticleId = -1;
    private int _selectedZSlice;
    private bool _use3D = true;

    public ParticleSeparatorUI()
    {
        _processor = new AcceleratedProcessor();
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _labelingOperation?.Cancel();
        _lastResult?.Dispose();
        _processor?.Dispose();
    }

    public void DrawPanel(CtImageStackDataset dataset)
    {
        if (dataset == null)
        {
            ImGui.Text("No dataset loaded");
            return;
        }

        ImGui.Text("Particle Separator");
        ImGui.Separator();

        // Material Selection
        if (dataset.Materials != null && dataset.Materials.Any())
        {
            var materialNames = dataset.Materials.Select(m => m.Name).ToArray();
            ImGui.Text("Target Material:");
            ImGui.SetNextItemWidth(-1);
            ImGui.Combo("##Material", ref _selectedMaterialIndex, materialNames, materialNames.Length);
        }
        else
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "No materials defined");
            return;
        }

        ImGui.Spacing();

        // Processing Options
        if (ImGui.CollapsingHeader("Processing Options", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Checkbox("3D Processing", ref _use3D);
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Process entire volume (3D) or a single slice (2D)");

            if (!_use3D)
            {
                ImGui.Indent();
                var maxSlice = dataset.Depth > 0 ? dataset.Depth - 1 : 0;
                _selectedZSlice = Math.Clamp(_selectedZSlice, 0, maxSlice);
                ImGui.Text("Slice to process (Z-axis):");
                ImGui.SliderInt("##ZSliceSelector", ref _selectedZSlice, 0, maxSlice);
                ImGui.Unindent();
            }

            ImGui.Text("Acceleration:");
            if (ImGui.RadioButton("Auto", _selectedAcceleration == AccelerationType.Auto))
                _selectedAcceleration = AccelerationType.Auto;
            ImGui.SameLine();
            if (ImGui.RadioButton("CPU", _selectedAcceleration == AccelerationType.CPU))
                _selectedAcceleration = AccelerationType.CPU;
            ImGui.SameLine();
            if (ImGui.RadioButton("SIMD", _selectedAcceleration == AccelerationType.SIMD))
                _selectedAcceleration = AccelerationType.SIMD;
            ImGui.SameLine();
            if (ImGui.RadioButton("GPU", _selectedAcceleration == AccelerationType.GPU))
                _selectedAcceleration = AccelerationType.GPU;

            var status = _processor.GetAccelerationStatus();
            ImGui.TextColored(status.Color, status.Message);

            ImGui.DragFloat("Min Size (voxels)", ref _minParticleSize, 1.0f, 1.0f, 10000.0f);
        }

        if (ImGui.CollapsingHeader("Optimization Filters", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var despeckle = _filters.Despeckle;
            if (ImGui.Checkbox("Despeckle (3D median)", ref despeckle)) _filters.Despeckle = despeckle;
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Binary 3x3x3 majority filter. Removes isolated noise voxels and fills\n" +
                                 "pinholes before labeling. Streamed slice-by-slice (out-of-core).");

            var opening = _filters.MorphologicalOpening;
            if (ImGui.Checkbox("Morphological opening", ref opening)) _filters.MorphologicalOpening = opening;
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Erosion followed by dilation. Cuts thin bridges between touching\n" +
                                 "particles so they are counted separately. Streamed slice-by-slice\n" +
                                 "(out-of-core); higher radius costs more memory and time.");

            if (_filters.MorphologicalOpening)
            {
                ImGui.Indent();
                var radius = _filters.OpeningRadius;
                if (ImGui.SliderInt("Opening radius (voxels)", ref radius, 1, 3))
                    _filters.OpeningRadius = radius;
                ImGui.Unindent();
            }
        }

        ImGui.Spacing();

        // Process Button
        var canProcess = !_isProcessing && dataset.Materials != null && dataset.Materials.Any();
        if (!canProcess) ImGui.BeginDisabled();
        if (ImGui.Button(_isProcessing ? "Processing..." : "Separate Particles", new Vector2(-1, 0)))
            _ = ProcessParticlesAsync(dataset);
        if (!canProcess) ImGui.EndDisabled();

        if (_isProcessing)
        {
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) _cancellationTokenSource?.Cancel();
            ImGui.ProgressBar(_processor.Progress, new Vector2(-1, 0),
                $"{_processor.Progress * 100:F1}% - {_processor.CurrentStage}");
        }

        ImGui.Spacing();

        if (_lastResult != null && _lastResult.Particles != null)
        {
            ImGui.Separator();
            DrawResultsPanel(dataset);
        }

        if (_processor.LastProcessingTime > 0)
        {
            ImGui.Separator();
            ImGui.Text($"Last Processing Time: {_processor.LastProcessingTime:F2}s");
            ImGui.Text($"Throughput: {_processor.VoxelsPerSecond:N0} voxels/sec");
        }
    }

    private void DrawResultsPanel(CtImageStackDataset dataset)
    {
        ImGui.Text($"Found {_lastResult.Particles.Count} particles");

        if (_lastResult.Particles.Any())
        {
            var totalVoxels = _lastResult.Particles.Sum(p => p.VoxelCount);
            var avgSize = totalVoxels / _lastResult.Particles.Count;
            var largest = _lastResult.Particles.OrderByDescending(p => p.VoxelCount).First();
            var smallest = _lastResult.Particles.OrderBy(p => p.VoxelCount).First();

            ImGui.Text($"Total Voxels: {totalVoxels:N0}");
            ImGui.Text($"Average Size: {avgSize:N0} voxels");
            ImGui.Text($"Largest Particle: {largest.VoxelCount:N0} voxels (ID: {largest.Id})");
            ImGui.Text($"Smallest Particle: {smallest.VoxelCount:N0} voxels (ID: {smallest.Id})");
        }

        ImGui.Spacing();

        // Particle List
        if (ImGui.CollapsingHeader($"Particle List ({_lastResult.Particles.Count})"))
            if (ImGui.BeginTable("ParticleTable", 4,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable |
                    ImGuiTableFlags.ScrollY, new Vector2(0, 200)))
            {
                ImGui.TableSetupColumn("ID");
                ImGui.TableSetupColumn("Voxels");
                ImGui.TableSetupColumn("Volume (µm³)");
                ImGui.TableSetupColumn("Center (X,Y,Z)");
                ImGui.TableHeadersRow();

                var sortedParticles = _lastResult.Particles.OrderByDescending(p => p.VoxelCount).ToList();

                foreach (var particle in sortedParticles)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    var isSelected = _selectedParticleId == particle.Id;
                    if (ImGui.Selectable($"{particle.Id}", isSelected, ImGuiSelectableFlags.SpanAllColumns))
                        _selectedParticleId = particle.Id;
                    ImGui.TableNextColumn();
                    ImGui.Text($"{particle.VoxelCount:N0}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{particle.VolumeMicrometers:N0}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"({particle.Center.X},{particle.Center.Y},{particle.Center.Z})");
                }

                ImGui.EndTable();
            }

        DrawLabelingSection(dataset);
    }

    private void DrawLabelingSection(CtImageStackDataset dataset)
    {
        if (!_lastResult.Particles.Any()) return;

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.7f, 1.0f, 0.7f, 1), "Labeling");
        ImGui.TextWrapped("Write the separated particles back into the dataset label volume as new " +
                          "materials. The write is streamed slice-by-slice (out-of-core).");

        var freeIds = GetFreeMaterialIds(dataset);
        if (freeIds.Count == 0)
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "No free material IDs available (255 max).");
            return;
        }

        var maxCreatable = Math.Min(freeIds.Count, _lastResult.Particles.Count);
        _maxParticleMaterials = Math.Clamp(_maxParticleMaterials, 1, Math.Max(1, maxCreatable));
        ImGui.SliderInt("Materials to create", ref _maxParticleMaterials, 1, Math.Max(1, maxCreatable));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("The largest N particles each get their own material.\n" +
                             $"Free material IDs remaining: {freeIds.Count}");

        if (_lastResult.Particles.Count > _maxParticleMaterials)
        {
            ImGui.Checkbox("Group remaining particles into one material", ref _groupRemainingParticles);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Particles beyond the N largest share a single \"Particles (rest)\" material.");
        }

        var labeling = _labelingOperation?.IsActive == true;
        if (labeling || _isProcessing) ImGui.BeginDisabled();
        if (ImGui.Button("Write Particle Labels", new Vector2(-1, 0)))
            ApplyParticleLabels(dataset);
        if (labeling || _isProcessing) ImGui.EndDisabled();

        if (labeling)
        {
            ImGui.ProgressBar(_labelingOperation.Progress, new Vector2(-1, 0), _labelingOperation.Name);
            if (ImGui.Button("Cancel label write")) _labelingOperation.Cancel();
        }
    }

    private static List<byte> GetFreeMaterialIds(CtImageStackDataset dataset)
    {
        var used = dataset.Materials.Select(m => m.ID).ToHashSet();
        var free = new List<byte>();
        for (var id = 1; id <= 254; id++)
            if (!used.Contains((byte)id))
                free.Add((byte)id);
        return free;
    }

    private void ApplyParticleLabels(CtImageStackDataset dataset)
    {
        var result = _lastResult;
        if (result?.Particles == null || !result.Particles.Any()) return;

        var freeIds = GetFreeMaterialIds(dataset);
        if (freeIds.Count == 0)
        {
            Logger.LogError("[ParticleSeparator] No free material IDs left for particle labeling.");
            return;
        }

        var sorted = result.Particles.OrderByDescending(p => p.VoxelCount).ToList();
        var directCount = Math.Min(Math.Min(_maxParticleMaterials, sorted.Count), freeIds.Count);
        var materialBySourceLabel = new Dictionary<int, byte>();
        var newMaterials = new List<Material>();

        for (var i = 0; i < directCount; i++)
        {
            var id = freeIds[i];
            materialBySourceLabel[sorted[i].SourceLabel] = id;
            newMaterials.Add(new Material(id, $"Particle {sorted[i].Id}", ParticleColor(i)));
        }

        if (_groupRemainingParticles && sorted.Count > directCount && freeIds.Count > directCount)
        {
            var restId = freeIds[directCount];
            newMaterials.Add(new Material(restId, "Particles (rest)", ParticleColor(directCount)));
            for (var i = directCount; i < sorted.Count; i++)
                materialBySourceLabel[sorted[i].SourceLabel] = restId;
        }

        _labelingOperation = CtOperationCoordinator.For(dataset).Enqueue("Writing particle labels",
            async (token, progress) =>
            {
                _processor.ApplyParticleLabels(dataset, result, materialBySourceLabel, token, progress);
                OpenTkManager.ExecuteOnMainThread(() =>
                {
                    foreach (var material in newMaterials)
                        if (dataset.Materials.All(m => m.ID != material.ID))
                            dataset.Materials.Add(material);
                    ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
                    Logger.Log($"[ParticleSeparator] Wrote labels for {materialBySourceLabel.Count} particles " +
                               $"into {newMaterials.Count} materials");
                });
            });
    }

    private static Vector4 ParticleColor(int index)
    {
        var hue = index * 137.508f % 360f / 360f;
        var h = hue * 6f;
        var sector = (int)h % 6;
        var f = h - (int)h;
        const float v = 0.9f, s = 0.75f;
        var p = v * (1 - s);
        var q = v * (1 - f * s);
        var t = v * (1 - (1 - f) * s);
        return sector switch
        {
            0 => new Vector4(v, t, p, 1),
            1 => new Vector4(q, v, p, 1),
            2 => new Vector4(p, v, t, 1),
            3 => new Vector4(p, q, v, 1),
            4 => new Vector4(t, p, v, 1),
            _ => new Vector4(v, p, q, 1)
        };
    }

    private async Task ProcessParticlesAsync(CtImageStackDataset dataset)
    {
        if (_isProcessing) return;
        _isProcessing = true;
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            var material = dataset.Materials[_selectedMaterialIndex];
            _processor.SelectedAcceleration = _selectedAcceleration;
            var result =
                await Task.Run(
                    () => _processor.SeparateParticles(dataset, material, _use3D, true, _minParticleSize,
                        _selectedZSlice, _filters, _cancellationTokenSource.Token), _cancellationTokenSource.Token);
            _lastResult?.Dispose();
            _lastResult = result;

            if (_lastResult?.Particles != null)
                Logger.Log(
                    $"[ParticleSeparator] Found {_lastResult.Particles.Count} particles in {_processor.LastProcessingTime:F2}s");
        }
        catch (OperationCanceledException)
        {
            Logger.Log("[ParticleSeparator] Operation cancelled");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ParticleSeparator] Error: {ex.Message}");
        }
        finally
        {
            _isProcessing = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }
}
