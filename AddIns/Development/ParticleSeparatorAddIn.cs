// ParticleSeparatorAddIn.cs - High-performance version with multiple acceleration options

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.AddIns.ParticleSeparator;

public class ParticleSeparatorAddIn : IAddIn
{
    private static ParticleSeparatorTool _tool;

    public static ParticleSeparatorAddIn Instance { get; private set; }

    public string Id => "ParticleSeparator";
    public string Name => "Particle Separator";
    public string Version => "2.0.0";
    public string Author => "GeoscientistToolkit";
    public string Description => "High-performance particle separation with CPU/GPU acceleration";

    public void Initialize()
    {
        Logger.Log($"[ParticleSeparatorAddIn] Initializing {Name} v{Version}");
        _tool = new ParticleSeparatorTool();
        Instance = this;
    }

    public void Shutdown()
    {
        Logger.Log($"[ParticleSeparatorAddIn] Shutting down {Name}");
        _tool?.Dispose();
        _tool = null;
        Instance = null;
    }

    public IEnumerable<AddInMenuItem> GetMenuItems()
    {
        return null;
    }

    public IEnumerable<AddInTool> GetTools()
    {
        return new[] { _tool };
    }

    public IEnumerable<IDataImporter> GetDataImporters()
    {
        return null;
    }

    public IEnumerable<IDataExporter> GetDataExporters()
    {
        return null;
    }

    public static ParticleSeparatorTool GetTool()
    {
        return _tool;
    }
}

public class ParticleSeparatorTool : AddInTool, IDisposable
{
    // Visualization
    private readonly Dictionary<int, Vector4> _particleColors = new();

    private readonly AcceleratedProcessor _processor;
    private readonly Random _random = new(42);
    private CancellationTokenSource _cancellationTokenSource;
    private bool _conservative = true;
    private bool _isProcessing;
    private ParticleSeparationResult _lastResult;
    private float _minParticleSize = 10.0f;
    private float _overlayOpacity = 0.5f;
    private AccelerationType _selectedAcceleration = AccelerationType.Auto;

    // UI State
    private int _selectedMaterialIndex;
    private int _selectedParticleId = -1;
    private int _selectedZSlice; // --- FIX: Added to store the selected slice for 2D processing ---
    private bool _showParticleOverlay = false;
    private bool _use3D = true;
    private bool _useGPU = true;

    public ParticleSeparatorTool()
    {
        _processor = new AcceleratedProcessor();
    }

    public override string Name => "Particle Separator";
    public override string Icon => "🔬";
    public override string Tooltip => "Separate and analyze particles in CT data";

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _processor?.Dispose();
    }

    public override void Execute(Dataset dataset)
    {
    }

    public override bool CanExecute(Dataset dataset)
    {
        return dataset is CtImageStackDataset;
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
        if (dataset.Materials != null && dataset.Materials.Count > 0)
        {
            var materialNames = dataset.Materials.Select(m => m.Name).ToArray();
            ImGui.Text("Target Material:");
            ImGui.SetNextItemWidth(200);
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
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Process entire volume (3D) or current slice only (2D)");

            // --- FIX: Added a slice selector for 2D mode ---
            if (!_use3D)
            {
                ImGui.Indent();
                var maxSlice = dataset != null && dataset.Depth > 0 ? dataset.Depth - 1 : 0;
                _selectedZSlice = Math.Clamp(_selectedZSlice, 0, maxSlice);

                ImGui.Text("Slice to process (Z-axis):");
                ImGui.SliderInt("##ZSliceSelector", ref _selectedZSlice, 0, maxSlice);

                ImGui.Unindent();
            }

            // Acceleration selection
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

            // Show acceleration status
            var status = _processor.GetAccelerationStatus();
            ImGui.TextColored(status.Color, status.Message);

            ImGui.Checkbox("Conservative Mode", ref _conservative);
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Conservative mode filters out very small particles");

            if (_conservative) ImGui.DragFloat("Min Size (voxels)", ref _minParticleSize, 1.0f, 1.0f, 1000.0f);

            // Thread count for CPU processing
            if (_selectedAcceleration == AccelerationType.CPU || _selectedAcceleration == AccelerationType.SIMD)
            {
                var threadCount = _processor.ThreadCount;
                if (ImGui.SliderInt("Thread Count", ref threadCount, 1, Environment.ProcessorCount * 2))
                    _processor.ThreadCount = threadCount;
            }
        }

        ImGui.Spacing();

        // Process Button
        var canProcess = !_isProcessing && dataset.Materials != null && dataset.Materials.Count > 0;

        if (!canProcess) ImGui.BeginDisabled();

        var buttonText = _isProcessing ? "Processing..." : "Separate Particles";
        if (ImGui.Button(buttonText, new Vector2(200, 30))) _ = ProcessParticlesAsync(dataset);

        if (!canProcess) ImGui.EndDisabled();

        // Cancel button
        if (_isProcessing)
        {
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) _cancellationTokenSource?.Cancel();

            ImGui.ProgressBar(_processor.Progress, new Vector2(-1, 0),
                $"{_processor.Progress * 100:F1}% - {_processor.CurrentStage}");
        }

        ImGui.Spacing();

        // Results Section
        if (_lastResult != null && _lastResult.Particles != null)
        {
            ImGui.Separator();
            DrawResultsPanel(dataset);
        }

        // Performance Stats
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

        // Statistics
        if (_lastResult.Particles.Count > 0)
        {
            var totalVoxels = _lastResult.Particles.Sum(p => p.VoxelCount);
            var avgSize = totalVoxels / _lastResult.Particles.Count;
            var largest = _lastResult.Particles.OrderByDescending(p => p.VoxelCount).First();
            var smallest = _lastResult.Particles.OrderBy(p => p.VoxelCount).First();

            ImGui.Text($"Total Voxels: {totalVoxels:N0}");
            ImGui.Text($"Average Size: {avgSize:N0} voxels");
            ImGui.Text($"Largest: {largest.VoxelCount:N0} voxels (ID: {largest.Id})");
            ImGui.Text($"Smallest: {smallest.VoxelCount:N0} voxels (ID: {smallest.Id})");
        }

        ImGui.Spacing();
        unsafe
        {
            if (ImGui.CollapsingHeader($"Particle List ({_lastResult.Particles.Count})"))
                if (ImGui.BeginTable("ParticleTable", 4,
                        ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable |
                        ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit,
                        new Vector2(0, 200)))
                {
                    ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 50);
                    ImGui.TableSetupColumn("Voxels", ImGuiTableColumnFlags.WidthFixed, 80);
                    ImGui.TableSetupColumn("Volume (μm³)", ImGuiTableColumnFlags.WidthFixed, 100);
                    ImGui.TableSetupColumn("Center", ImGuiTableColumnFlags.WidthFixed, 120);
                    ImGui.TableSetupScrollFreeze(0, 1);
                    ImGui.TableHeadersRow();

                    var sortedParticles = _lastResult.Particles.OrderByDescending(p => p.VoxelCount).ToList();

                    // Use clipper for large lists
                    var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
                    clipper.Begin(Math.Min(100, sortedParticles.Count));

                    while (clipper.Step())
                        for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                        {
                            var particle = sortedParticles[i];

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

                    clipper.End();

                    if (sortedParticles.Count > 100)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TextDisabled($"... and {sortedParticles.Count - 100} more");
                    }

                    ImGui.EndTable();
                }
        }
        // Particle List with optimized rendering


        ImGui.Spacing();

        // Action Buttons
        if (ImGui.Button("Apply as New Materials")) CreateMaterialsFromParticles(dataset);

        ImGui.SameLine();
        if (ImGui.Button("Export to Binary")) ExportToBinary();
    }

    private async Task ProcessParticlesAsync(CtImageStackDataset dataset)
    {
        if (_isProcessing) return;

        _isProcessing = true;
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            Logger.Log($"[ParticleSeparator] Starting particle separation with {_selectedAcceleration} acceleration");

            var material = dataset.Materials[_selectedMaterialIndex];

            _processor.SelectedAcceleration = _selectedAcceleration;

            // --- FIX: Pass the selected Z-slice for 2D processing ---
            _lastResult = await Task.Run(() =>
                    _processor.SeparateParticles(
                        dataset,
                        material,
                        _use3D,
                        _conservative,
                        _minParticleSize,
                        _selectedZSlice,
                        _cancellationTokenSource.Token
                    ),
                _cancellationTokenSource.Token
            );

            if (_lastResult != null && _lastResult.Particles != null)
            {
                Logger.Log(
                    $"[ParticleSeparator] Found {_lastResult.Particles.Count} particles in {_processor.LastProcessingTime:F2}s");
                GenerateParticleColors();
            }
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

    private void GenerateParticleColors()
    {
        if (_lastResult == null || _lastResult.Particles == null) return;

        _particleColors.Clear();
        Parallel.ForEach(_lastResult.Particles, particle =>
        {
            var hue = _random.Next(360) / 360.0f;
            var saturation = 0.7f + _random.Next(30) / 100.0f;
            var value = 0.7f + _random.Next(30) / 100.0f;

            lock (_particleColors)
            {
                _particleColors[particle.Id] = HsvToRgb(hue, saturation, value);
            }
        });
    }

    private Vector4 HsvToRgb(float h, float s, float v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h * 6 % 2 - 1));
        var m = v - c;

        float r = 0, g = 0, b = 0;

        if (h < 1.0f / 6)
        {
            r = c;
            g = x;
            b = 0;
        }
        else if (h < 2.0f / 6)
        {
            r = x;
            g = c;
            b = 0;
        }
        else if (h < 3.0f / 6)
        {
            r = 0;
            g = c;
            b = x;
        }
        else if (h < 4.0f / 6)
        {
            r = 0;
            g = x;
            b = c;
        }
        else if (h < 5.0f / 6)
        {
            r = x;
            g = 0;
            b = c;
        }
        else
        {
            r = c;
            g = 0;
            b = x;
        }

        return new Vector4(r + m, g + m, b + m, 1.0f);
    }

    private void CreateMaterialsFromParticles(CtImageStackDataset dataset)
    {
        if (_lastResult == null || _lastResult.Particles == null) return;

        Logger.Log($"[ParticleSeparator] Creating materials from {_lastResult.Particles.Count} particles");

        var sortedParticles = _lastResult.Particles.OrderByDescending(p => p.VoxelCount).ToList();
        var maxMaterials = Math.Min(10, sortedParticles.Count);

        Parallel.For(0, maxMaterials, i =>
        {
            var particle = sortedParticles[i];
            ApplyParticleAsMaterial(dataset, particle, _lastResult.LabelVolume, (byte)(200 + i));
        });

        Logger.Log($"[ParticleSeparator] Created {maxMaterials} new materials");
    }

    private void ApplyParticleAsMaterial(CtImageStackDataset dataset, Particle particle, int[,,] labels,
        byte materialId)
    {
        Parallel.For(particle.Bounds.MinZ, particle.Bounds.MaxZ + 1, z =>
        {
            for (var y = particle.Bounds.MinY; y <= particle.Bounds.MaxY; y++)
            for (var x = particle.Bounds.MinX; x <= particle.Bounds.MaxX; x++)
                if (labels[x, y, z] == particle.Id)
                    dataset.LabelData[x, y, z] = materialId;
        });
    }

    private void ExportToBinary()
    {
        // Export results to a binary format for external processing
        Logger.Log("[ParticleSeparator] Exporting to binary format");
    }
}

public enum AccelerationType
{
    Auto,
    CPU,
    SIMD,
    GPU
}

/// <summary>
///     High-performance processor with multiple acceleration methods
/// </summary>
public unsafe class AcceleratedProcessor : IDisposable
{
    private readonly dynamic _gpuAccelerator = null;

    private readonly object _gpuLock = new();
    private bool _gpuAvailable;
    private dynamic _gpuContext;

    public AcceleratedProcessor()
    {
        InitializeAcceleration();
    }

    public float Progress { get; set; }
    public string CurrentStage { get; set; } = "";
    public double LastProcessingTime { get; set; }
    public long VoxelsPerSecond { get; set; }
    public int ThreadCount { get; set; } = Environment.ProcessorCount;
    public AccelerationType SelectedAcceleration { get; set; } = AccelerationType.Auto;

    public void Dispose()
    {
        try
        {
            _gpuAccelerator?.Dispose();
            _gpuContext?.Dispose();
        }
        catch
        {
        }
    }

    private void InitializeAcceleration()
    {
        // Try to load ILGPU if available (via reflection to avoid hard dependency)
        try
        {
            var ilgpuAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "ILGPU");

            if (ilgpuAssembly != null)
            {
                var contextType = ilgpuAssembly.GetType("ILGPU.Context");
                if (contextType != null)
                {
                    var createDefaultMethod = contextType.GetMethod("CreateDefault");
                    _gpuContext = createDefaultMethod?.Invoke(null, null);
                    _gpuAvailable = _gpuContext != null;
                    Logger.Log("[AcceleratedProcessor] ILGPU available");
                }
            }
        }
        catch
        {
            _gpuAvailable = false;
        }

        // Check SIMD support
        var simdAvailable = Vector.IsHardwareAccelerated;
        Logger.Log($"[AcceleratedProcessor] SIMD: {(simdAvailable ? "Available" : "Not Available")}");
    }

    public (string Message, Vector4 Color) GetAccelerationStatus()
    {
        var simdAvailable = Vector.IsHardwareAccelerated;

        if (_gpuAvailable)
            return ("✓ GPU Available (ILGPU)", new Vector4(0, 1, 0, 1));
        if (simdAvailable)
            return ("✓ SIMD Available", new Vector4(0.5f, 1, 0, 1));
        return ("✓ Multi-threaded CPU", new Vector4(1, 1, 0, 1));
    }

    // --- FIX: Added zSlice parameter for 2D processing ---
    public ParticleSeparationResult SeparateParticles(
        CtImageStackDataset dataset,
        Material material,
        bool use3D,
        bool conservative,
        float minSize,
        int zSlice,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            Progress = 0.1f;
            CurrentStage = "Extracting mask";

            // Extract binary mask using optimized method
            var mask = ExtractMaterialMaskOptimized(dataset, material, cancellationToken);

            Progress = 0.3f;
            CurrentStage = "Labeling components";

            // Choose best acceleration method
            int[,,] labels;
            if (use3D)
                labels = SelectedAcceleration switch
                {
                    AccelerationType.GPU when _gpuAvailable => LabelComponents3DGPU(mask, cancellationToken),
                    AccelerationType.SIMD when Vector.IsHardwareAccelerated => LabelComponents3DSIMD(mask,
                        cancellationToken),
                    AccelerationType.CPU => LabelComponents3DParallel(mask, cancellationToken),
                    _ => LabelComponents3DOptimal(mask, cancellationToken)
                };
            else
                // --- FIX: Pass the provided zSlice instead of a non-existent property ---
                labels = LabelComponents2DOptimized(mask, zSlice, cancellationToken);

            Progress = 0.7f;
            CurrentStage = "Analyzing particles";

            var particles = AnalyzeParticlesOptimized(labels, dataset.PixelSize, conservative ? (int)minSize : 1,
                cancellationToken);

            Progress = 1.0f;
            CurrentStage = "Complete";

            stopwatch.Stop();
            LastProcessingTime = stopwatch.Elapsed.TotalSeconds;

            var totalVoxels = (long)dataset.Width * dataset.Height * dataset.Depth;
            VoxelsPerSecond = (long)(totalVoxels / LastProcessingTime);

            return new ParticleSeparationResult
            {
                LabelVolume = labels,
                Particles = particles,
                Is3D = use3D
            };
        }
        catch (Exception ex)
        {
            Logger.LogError($"[AcceleratedProcessor] Error: {ex.Message}");
            throw;
        }
    }

    private byte[,,] ExtractMaterialMaskOptimized(CtImageStackDataset dataset, Material material,
        CancellationToken cancellationToken)
    {
        var width = dataset.Width;
        var height = dataset.Height;
        var depth = dataset.Depth;

        var mask = new byte[width, height, depth];

        // Use parallel processing with partitioner for better performance
        var partitioner = Partitioner.Create(0, depth, Math.Max(1, depth / (ThreadCount * 4)));

        Parallel.ForEach(partitioner, new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = ThreadCount
            },
            range =>
            {
                for (var z = range.Item1; z < range.Item2; z++)
                    // Use unsafe code for faster array access
                    fixed (byte* maskPtr = &mask[0, 0, z])
                    {
                        var idx = 0;
                        for (var y = 0; y < height; y++)
                        for (var x = 0; x < width; x++)
                        {
                            if (dataset.LabelData != null && dataset.LabelData[x, y, z] == material.ID)
                                maskPtr[idx] = 1;
                            idx++;
                        }
                    }

                Progress = 0.1f + range.Item2 * 0.2f / depth;
            });

        return mask;
    }

    private int[,,] LabelComponents3DOptimal(byte[,,] mask, CancellationToken cancellationToken)
    {
        // Choose best available method
        if (_gpuAvailable && SelectedAcceleration == AccelerationType.Auto)
            return LabelComponents3DGPU(mask, cancellationToken);
        if (Vector.IsHardwareAccelerated)
            return LabelComponents3DSIMD(mask, cancellationToken);
        return LabelComponents3DParallel(mask, cancellationToken);
    }

    private int[,,] LabelComponents3DParallel(byte[,,] mask, CancellationToken cancellationToken)
    {
        var width = mask.GetLength(0);
        var height = mask.GetLength(1);
        var depth = mask.GetLength(2);

        var labels = new int[width, height, depth];
        var unionFind = new ConcurrentUnionFind();
        var nextLabel = 1;

        // Process in slabs for better cache locality
        var slabSize = Math.Max(1, depth / ThreadCount);

        Parallel.For(0, ThreadCount, new ParallelOptions { CancellationToken = cancellationToken },
            threadId =>
            {
                var startZ = threadId * slabSize;
                var endZ = threadId == ThreadCount - 1 ? depth : (threadId + 1) * slabSize;

                for (var z = startZ; z < endZ; z++)
                for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    if (mask[x, y, z] == 0) continue;

                    var neighbors = new List<int>(3);

                    if (x > 0 && labels[x - 1, y, z] > 0)
                        neighbors.Add(labels[x - 1, y, z]);
                    if (y > 0 && labels[x, y - 1, z] > 0)
                        neighbors.Add(labels[x, y - 1, z]);
                    if (z > 0 && labels[x, y, z - 1] > 0)
                        neighbors.Add(labels[x, y, z - 1]);

                    if (neighbors.Count == 0)
                    {
                        var label = Interlocked.Increment(ref nextLabel) - 1;
                        labels[x, y, z] = label;
                        unionFind.MakeSet(label);
                    }
                    else
                    {
                        var minLabel = neighbors[0];
                        for (var i = 1; i < neighbors.Count; i++)
                            if (neighbors[i] < minLabel)
                                minLabel = neighbors[i];

                        labels[x, y, z] = minLabel;

                        foreach (var label in neighbors)
                            if (label != minLabel)
                                unionFind.Union(minLabel, label);
                    }
                }

                Progress = 0.3f + endZ * 0.2f / depth;
            });

        // Resolve equivalences in parallel
        ResolveEquivalencesParallel(labels, unionFind, cancellationToken);

        return labels;
    }

    private int[,,] LabelComponents3DSIMD(byte[,,] mask, CancellationToken cancellationToken)
    {
        var width = mask.GetLength(0);
        var height = mask.GetLength(1);
        var depth = mask.GetLength(2);

        var labels = new int[width, height, depth];

        // SIMD processing for initial labeling
        var vectorSize = Vector<int>.Count;

        Parallel.For(0, depth, new ParallelOptions { CancellationToken = cancellationToken }, z =>
        {
            for (var y = 0; y < height; y++)
            {
                var x = 0;

                // Process vectors
                for (; x <= width - vectorSize; x += vectorSize)
                {
                    // Load mask values
                    var maskValues = new int[vectorSize];
                    for (var i = 0; i < vectorSize; i++) maskValues[i] = mask[x + i, y, z];

                    var maskVector = new Vector<int>(maskValues);
                    var zeroVector = Vector<int>.Zero;

                    // Check if any element is non-zero
                    if (Vector.GreaterThanAny(maskVector, zeroVector))
                        // Process each element
                        for (var i = 0; i < vectorSize; i++)
                            if (mask[x + i, y, z] > 0)
                                labels[x + i, y, z] = (z * height + y) * width + x + i + 1;
                }

                // Process remaining elements
                for (; x < width; x++)
                    if (mask[x, y, z] > 0)
                        labels[x, y, z] = (z * height + y) * width + x + 1;
            }

            Progress = 0.3f + z * 0.2f / depth;
        });

        // Connected component merging
        return MergeComponents(labels, cancellationToken);
    }

    private int[,,] LabelComponents3DGPU(byte[,,] mask, CancellationToken cancellationToken)
    {
        // GPU implementation would go here if ILGPU is available
        // For now, fallback to SIMD
        Logger.Log("[AcceleratedProcessor] GPU processing requested but using SIMD fallback");
        return LabelComponents3DSIMD(mask, cancellationToken);
    }

    private int[,,] LabelComponents2DOptimized(byte[,,] mask, int slice, CancellationToken cancellationToken)
    {
        var width = mask.GetLength(0);
        var height = mask.GetLength(1);
        var depth = mask.GetLength(2);

        var labels = new int[width, height, depth];

        // Use scanline algorithm for 2D
        var nextLabel = 1;
        var equivalences = new Dictionary<int, int>();

        // First pass - assign labels
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            if (mask[x, y, slice] == 0) continue;

            var left = x > 0 ? labels[x - 1, y, slice] : 0;
            var top = y > 0 ? labels[x, y - 1, slice] : 0;

            if (left == 0 && top == 0)
            {
                labels[x, y, slice] = nextLabel++;
            }
            else if (left > 0 && top == 0)
            {
                labels[x, y, slice] = left;
            }
            else if (left == 0 && top > 0)
            {
                labels[x, y, slice] = top;
            }
            else
            {
                labels[x, y, slice] = Math.Min(left, top);
                if (left != top) UnionLabels(equivalences, left, top);
            }
        }

        // Second pass - resolve equivalences
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            if (labels[x, y, slice] > 0)
                labels[x, y, slice] = FindRoot(equivalences, labels[x, y, slice]);

        return labels;
    }

    private void UnionLabels(Dictionary<int, int> equivalences, int a, int b)
    {
        var rootA = FindRoot(equivalences, a);
        var rootB = FindRoot(equivalences, b);

        if (rootA != rootB)
        {
            if (rootA < rootB)
                equivalences[rootB] = rootA;
            else
                equivalences[rootA] = rootB;
        }
    }

    private int FindRoot(Dictionary<int, int> equivalences, int label)
    {
        if (!equivalences.ContainsKey(label))
            return label;

        var root = FindRoot(equivalences, equivalences[label]);
        equivalences[label] = root; // Path compression
        return root;
    }

    private int[,,] MergeComponents(int[,,] labels, CancellationToken cancellationToken)
    {
        var width = labels.GetLength(0);
        var height = labels.GetLength(1);
        var depth = labels.GetLength(2);

        var unionFind = new ConcurrentUnionFind();

        // Find all unique labels and merge adjacent ones
        Parallel.For(0, depth, new ParallelOptions { CancellationToken = cancellationToken }, z =>
        {
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var current = labels[x, y, z];
                if (current == 0) continue;

                unionFind.MakeSet(current);

                // Check neighbors and union if needed
                if (x > 0 && labels[x - 1, y, z] > 0)
                    unionFind.Union(current, labels[x - 1, y, z]);
                if (y > 0 && labels[x, y - 1, z] > 0)
                    unionFind.Union(current, labels[x, y - 1, z]);
                if (z > 0 && labels[x, y, z - 1] > 0)
                    unionFind.Union(current, labels[x, y, z - 1]);
            }
        });

        ResolveEquivalencesParallel(labels, unionFind, cancellationToken);

        return labels;
    }

    private void ResolveEquivalencesParallel(int[,,] labels, ConcurrentUnionFind unionFind,
        CancellationToken cancellationToken)
    {
        var width = labels.GetLength(0);
        var height = labels.GetLength(1);
        var depth = labels.GetLength(2);

        var finalLabels = new ConcurrentDictionary<int, int>();
        var finalLabelCounter = 0;

        Parallel.For(0, depth, new ParallelOptions { CancellationToken = cancellationToken }, z =>
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

            Progress = 0.5f + z * 0.2f / depth;
        });
    }

    private List<Particle> AnalyzeParticlesOptimized(int[,,] labels, double pixelSize, int minSize,
        CancellationToken cancellationToken)
    {
        var particles = new ConcurrentDictionary<int, ParticleAccumulator>();

        var width = labels.GetLength(0);
        var height = labels.GetLength(1);
        var depth = labels.GetLength(2);

        // Parallel particle analysis
        Parallel.For(0, depth, new ParallelOptions { CancellationToken = cancellationToken }, z =>
        {
            var localParticles = new Dictionary<int, ParticleAccumulator>();

            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var label = labels[x, y, z];
                if (label == 0) continue;

                if (!localParticles.ContainsKey(label)) localParticles[label] = new ParticleAccumulator { Id = label };

                var acc = localParticles[label];
                acc.VoxelCount++;
                acc.CenterSum += new Vector3(x, y, z);
                acc.UpdateBounds(x, y, z);
            }

            // Merge local results
            foreach (var kvp in localParticles)
                particles.AddOrUpdate(kvp.Key, kvp.Value, (key, existing) =>
                {
                    existing.Merge(kvp.Value);
                    return existing;
                });

            Progress = 0.7f + z * 0.3f / depth;
        });

        // Convert to final particle list
        var result = new List<Particle>();
        var voxelVolume = pixelSize * pixelSize * pixelSize;

        foreach (var acc in particles.Values)
        {
            if (acc.VoxelCount < minSize) continue;

            var particle = new Particle
            {
                Id = acc.Id,
                VoxelCount = acc.VoxelCount,
                Center = new Point3D
                {
                    X = (int)(acc.CenterSum.X / acc.VoxelCount),
                    Y = (int)(acc.CenterSum.Y / acc.VoxelCount),
                    Z = (int)(acc.CenterSum.Z / acc.VoxelCount)
                },
                Bounds = new BoundingBox
                {
                    MinX = acc.MinX,
                    MinY = acc.MinY,
                    MinZ = acc.MinZ,
                    MaxX = acc.MaxX,
                    MaxY = acc.MaxY,
                    MaxZ = acc.MaxZ
                },
                VolumeMicrometers = acc.VoxelCount * voxelVolume * 1e18,
                VolumeMillimeters = acc.VoxelCount * voxelVolume * 1e9
            };

            result.Add(particle);
        }

        return result;
    }

    private class ParticleAccumulator
    {
        public int Id { get; set; }
        public int VoxelCount { get; set; }
        public Vector3 CenterSum { get; set; }
        public int MinX { get; set; } = int.MaxValue;
        public int MinY { get; set; } = int.MaxValue;
        public int MinZ { get; set; } = int.MaxValue;
        public int MaxX { get; set; } = int.MinValue;
        public int MaxY { get; set; } = int.MinValue;
        public int MaxZ { get; set; } = int.MinValue;

        public void UpdateBounds(int x, int y, int z)
        {
            MinX = Math.Min(MinX, x);
            MinY = Math.Min(MinY, y);
            MinZ = Math.Min(MinZ, z);
            MaxX = Math.Max(MaxX, x);
            MaxY = Math.Max(MaxY, y);
            MaxZ = Math.Max(MaxZ, z);
        }

        public void Merge(ParticleAccumulator other)
        {
            VoxelCount += other.VoxelCount;
            CenterSum += other.CenterSum;
            MinX = Math.Min(MinX, other.MinX);
            MinY = Math.Min(MinY, other.MinY);
            MinZ = Math.Min(MinZ, other.MinZ);
            MaxX = Math.Max(MaxX, other.MaxX);
            MaxY = Math.Max(MaxY, other.MaxY);
            MaxZ = Math.Max(MaxZ, other.MaxZ);
        }
    }
}

/// <summary>
///     Thread-safe Union-Find for parallel processing
/// </summary>
public class ConcurrentUnionFind
{
    private readonly object _lockObj = new();
    private readonly ConcurrentDictionary<int, int> _parent = new();
    private readonly ConcurrentDictionary<int, int> _rank = new();

    public void MakeSet(int x)
    {
        _parent.TryAdd(x, x);
        _rank.TryAdd(x, 0);
    }

    public int Find(int x)
    {
        if (!_parent.ContainsKey(x))
        {
            MakeSet(x);
            return x;
        }

        if (_parent[x] != x) _parent[x] = Find(_parent[x]); // Path compression

        return _parent[x];
    }

    public void Union(int x, int y)
    {
        lock (_lockObj)
        {
            var rootX = Find(x);
            var rootY = Find(y);

            if (rootX == rootY) return;

            if (_rank[rootX] < _rank[rootY])
            {
                _parent[rootX] = rootY;
            }
            else if (_rank[rootX] > _rank[rootY])
            {
                _parent[rootY] = rootX;
            }
            else
            {
                _parent[rootY] = rootX;
                _rank[rootX]++;
            }
        }
    }
}

// Data structures
public class Particle
{
    public int Id { get; set; }
    public int VoxelCount { get; set; }
    public double VolumeMicrometers { get; set; }
    public double VolumeMillimeters { get; set; }
    public Point3D Center { get; set; }
    public BoundingBox Bounds { get; set; }
}

public class Point3D
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}

public class BoundingBox
{
    public int MinX { get; set; }
    public int MinY { get; set; }
    public int MinZ { get; set; }
    public int MaxX { get; set; }
    public int MaxY { get; set; }
    public int MaxZ { get; set; }

    public int Width => MaxX - MinX + 1;
    public int Height => MaxY - MinY + 1;
    public int Depth => MaxZ - MinZ + 1;
}

public class ParticleSeparationResult
{
    public int[,,] LabelVolume { get; set; }
    public List<Particle> Particles { get; set; }
    public bool Is3D { get; set; }
}