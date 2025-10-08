// GeoscientistToolkit/Analysis/ParticleSeparator/ParticleSeparatorUI.cs

using System.Numerics;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.ParticleSeparator;

/// <summary>
///     Provides the UI and logic for separating and analyzing particles in CT data.
/// </summary>
public class ParticleSeparatorUI : IDisposable
{
    // Visualization
    private readonly Dictionary<int, Vector4> _particleColors = new();
    private readonly AcceleratedProcessor _processor;
    private readonly Random _random = new(42);
    private CancellationTokenSource _cancellationTokenSource;
    private bool _isProcessing;
    private ParticleSeparationResult _lastResult;
    private float _minParticleSize = 10.0f;
    private AccelerationType _selectedAcceleration = AccelerationType.Auto;

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
            _lastResult =
                await Task.Run(
                    () => _processor.SeparateParticles(dataset, material, _use3D, true, _minParticleSize,
                        _selectedZSlice, _cancellationTokenSource.Token), _cancellationTokenSource.Token);

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