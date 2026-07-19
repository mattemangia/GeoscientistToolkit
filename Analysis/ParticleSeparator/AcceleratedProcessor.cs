using System.Diagnostics;
using System.Numerics;
using GAIA.Data.CtImageStack;
using GAIA.Data.VolumeData;
using GAIA.Util;

namespace GAIA.Analysis.ParticleSeparator;

public enum AccelerationType { Auto, CPU, SIMD, GPU }

/// <summary>
/// Optional streamed pre-filters applied to the binary material mask before labeling.
/// Every filter works slice-by-slice with a three-slice rolling window, so memory stays
/// bounded regardless of dataset size.
/// </summary>
public sealed class ParticleSeparationFilters
{
    /// <summary>Binary 3x3x3 (3x3 in 2D mode) majority filter that removes salt-and-pepper noise.</summary>
    public bool Despeckle { get; set; }

    /// <summary>Erosion followed by dilation; cuts thin bridges between touching particles.</summary>
    public bool MorphologicalOpening { get; set; }

    /// <summary>Opening radius in voxels (erosion/dilation iterations). Kept small on purpose:
    /// each iteration adds two streaming stages holding three slice buffers each.</summary>
    public int OpeningRadius { get; set; } = 1;

    public bool AnyEnabled => Despeckle || MorphologicalOpening;
}

/// <summary>
/// Out-of-core connected-component analysis for CT material labels. Components are represented
/// as horizontal runs, so working memory depends on two slices and component count, not voxels.
/// The volume is scanned exactly once; detected runs are spooled to a temporary file that later
/// passes (statistics, label write-back) stream instead of re-reading the volume.
/// </summary>
public sealed class AcceleratedProcessor : IDisposable
{
    private readonly object _statsLock = new();
    private float _progress;
    private string _currentStage = "";
    private double _lastProcessingTime;
    private long _voxelsPerSecond;

    public float Progress { get { lock (_statsLock) return _progress; } private set { lock (_statsLock) _progress = value; } }
    public string CurrentStage { get { lock (_statsLock) return _currentStage; } private set { lock (_statsLock) _currentStage = value; } }
    public double LastProcessingTime { get { lock (_statsLock) return _lastProcessingTime; } private set { lock (_statsLock) _lastProcessingTime = value; } }
    public long VoxelsPerSecond { get { lock (_statsLock) return _voxelsPerSecond; } private set { lock (_statsLock) _voxelsPerSecond = value; } }
    public int ThreadCount { get; set; } = Environment.ProcessorCount;
    public AccelerationType SelectedAcceleration { get; set; } = AccelerationType.Auto;

    public (string Message, Vector4 Color) GetAccelerationStatus() =>
        ("Out-of-core run-length CPU", new Vector4(.5f, 1, 0, 1));

    public ParticleSeparationResult SeparateParticles(CtImageStackDataset dataset, Material material, bool use3D,
        bool conservative, float minSize, int zSlice, ParticleSeparationFilters filters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(material);
        if (dataset.LabelData == null) throw new InvalidOperationException("CT label data is not loaded.");
        var stopwatch = Stopwatch.StartNew();
        var runFilePath = Path.Combine(Path.GetTempPath(), $"gaia-particle-runs-{Guid.NewGuid():N}.bin");
        try
        {
            CurrentStage = "Scanning run-length components";
            var components = new DenseUnionFind();
            using (var runStream = new FileStream(runFilePath, FileMode.Create, FileAccess.Write,
                       FileShare.None, 1 << 20, FileOptions.SequentialScan))
            using (var runWriter = new BinaryWriter(runStream))
            {
                var mask = BuildMaskPipeline(dataset, material.ID, use3D, filters);
                ScanMaterialRuns(dataset, mask, use3D, zSlice, components, run =>
                {
                    runWriter.Write(run.Z); runWriter.Write(run.Y);
                    runWriter.Write(run.StartX); runWriter.Write(run.EndX);
                    runWriter.Write(run.Label);
                }, cancellationToken, value => Progress = value * .75f);
            }

            CurrentStage = "Computing particle statistics";
            var accumulators = new Dictionary<int, StreamingParticleAccumulator>();
            StreamRuns(runFilePath, cancellationToken, value => Progress = .75f + value * .2f, run =>
            {
                var root = components.Find(run.Label);
                if (!accumulators.TryGetValue(root, out var accumulator))
                    accumulators[root] = accumulator = new StreamingParticleAccumulator(root);
                accumulator.Add(run);
            });

            var minimumSize = conservative ? Math.Max(1L, (long)minSize) : 1L;
            var voxelVolume = dataset.PixelSize * dataset.PixelSize * dataset.PixelSize;
            var nextParticleId = 1;
            var particles = accumulators.Values.Where(value => value.VoxelCount >= minimumSize)
                .OrderBy(value => value.SourceId)
                .Select(value => value.ToParticle(nextParticleId++, voxelVolume)).ToList();

            stopwatch.Stop();
            LastProcessingTime = stopwatch.Elapsed.TotalSeconds;
            var inspected = use3D ? (long)dataset.Width * dataset.Height * dataset.Depth :
                (long)dataset.Width * dataset.Height;
            VoxelsPerSecond = LastProcessingTime > 0 ? (long)(inspected / LastProcessingTime) : inspected;
            Progress = 1;
            CurrentStage = "Complete";
            return new ParticleSeparationResult
            {
                Particles = particles, Is3D = use3D, RunFilePath = runFilePath, Components = components
            };
        }
        catch (Exception ex)
        {
            try { if (File.Exists(runFilePath)) File.Delete(runFilePath); } catch { /* best effort */ }
            Logger.LogError($"[ParticleSeparator] Out-of-core analysis failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Writes particle labels back into the dataset's label volume, streaming the spooled run
    /// file slice-by-slice. <paramref name="materialBySourceLabel"/> maps
    /// <see cref="Particle.SourceLabel"/> to the destination material ID.
    /// </summary>
    public void ApplyParticleLabels(CtImageStackDataset dataset, ParticleSeparationResult result,
        IReadOnlyDictionary<int, byte> materialBySourceLabel, CancellationToken cancellationToken,
        IProgress<float> progress = null)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(materialBySourceLabel);
        if (dataset.LabelData == null) throw new InvalidOperationException("CT label data is not loaded.");
        if (result.RunFilePath == null || !File.Exists(result.RunFilePath))
            throw new InvalidOperationException("Particle run data is no longer available. Re-run the separation.");

        CurrentStage = "Writing particle labels";
        var slice = new byte[checked(dataset.Width * dataset.Height)];
        var currentZ = -1;
        var modified = false;
        StreamRuns(result.RunFilePath, cancellationToken, progress == null ? null : progress.Report, run =>
        {
            if (run.Z != currentZ)
            {
                if (modified) dataset.LabelData.WriteSliceZ(currentZ, slice);
                currentZ = run.Z;
                modified = false;
                dataset.LabelData.ReadSliceZ(currentZ, slice);
            }
            var root = result.Components.Find(run.Label);
            if (!materialBySourceLabel.TryGetValue(root, out var materialId)) return;
            slice.AsSpan(run.Y * dataset.Width + run.StartX, run.EndX - run.StartX).Fill(materialId);
            modified = true;
        });
        if (modified && currentZ >= 0) dataset.LabelData.WriteSliceZ(currentZ, slice);
        progress?.Report(1f);
        CurrentStage = "Complete";
    }

    internal readonly record struct StreamingRun(int Z, int Y, int StartX, int EndX, int Label);

    private static void StreamRuns(string runFilePath, CancellationToken token, Action<float> progress,
        Action<StreamingRun> visitor)
    {
        using var stream = new FileStream(runFilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            1 << 20, FileOptions.SequentialScan);
        using var reader = new BinaryReader(stream);
        var totalRuns = stream.Length / 20;
        for (long index = 0; index < totalRuns; index++)
        {
            if ((index & 0xFFFF) == 0)
            {
                token.ThrowIfCancellationRequested();
                progress?.Invoke(totalRuns == 0 ? 1f : index / (float)totalRuns);
            }
            visitor(new StreamingRun(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
                reader.ReadInt32(), reader.ReadInt32()));
        }
        progress?.Invoke(1f);
    }

    private static MaskStage BuildMaskPipeline(CtImageStackDataset dataset, byte materialId, bool use3D,
        ParticleSeparationFilters filters)
    {
        MaskStage stage = new MaterialMaskStage(dataset.LabelData, materialId,
            dataset.Width, dataset.Height, dataset.Depth, use3D);
        if (filters == null || !filters.AnyEnabled) return stage;
        if (filters.Despeckle)
            stage = new DespeckleStage(stage, dataset.Width, dataset.Height, dataset.Depth, use3D);
        if (filters.MorphologicalOpening)
        {
            var radius = Math.Clamp(filters.OpeningRadius, 1, 3);
            for (var i = 0; i < radius; i++)
                stage = new ErodeStage(stage, dataset.Width, dataset.Height, dataset.Depth, use3D);
            for (var i = 0; i < radius; i++)
                stage = new DilateStage(stage, dataset.Width, dataset.Height, dataset.Depth, use3D);
        }
        return stage;
    }

    private static void ScanMaterialRuns(CtImageStackDataset dataset, MaskStage mask, bool use3D, int requestedZ,
        DenseUnionFind components, Action<StreamingRun> visitor, CancellationToken token, Action<float> progress)
    {
        var firstZ = use3D ? 0 : Math.Clamp(requestedZ, 0, dataset.Depth - 1);
        var lastZ = use3D ? dataset.Depth : firstZ + 1;
        var previousSlice = new List<StreamingRun>[dataset.Height];
        var nextLabel = 1;
        for (var z = firstZ; z < lastZ; z++)
        {
            token.ThrowIfCancellationRequested();
            var slice = mask.GetSlice(z);
            var currentSlice = new List<StreamingRun>[dataset.Height];
            List<StreamingRun> previousRow = null;
            for (var y = 0; y < dataset.Height; y++)
            {
                var row = new List<StreamingRun>();
                var x = 0;
                while (x < dataset.Width)
                {
                    while (x < dataset.Width && slice[y * dataset.Width + x] == 0) x++;
                    if (x >= dataset.Width) break;
                    var start = x++;
                    while (x < dataset.Width && slice[y * dataset.Width + x] != 0) x++;
                    var end = x;
                    var neighbors = OverlappingLabels(previousRow, start, end)
                        .Concat(use3D ? OverlappingLabels(previousSlice[y], start, end) : Enumerable.Empty<int>())
                        .ToArray();
                    int label;
                    if (neighbors.Length == 0) { label = nextLabel++; components.Ensure(label); }
                    else { label = neighbors.Min(); foreach (var neighbor in neighbors) components.Union(label, neighbor); }
                    var run = new StreamingRun(z, y, start, end, label);
                    row.Add(run);
                    visitor?.Invoke(run);
                }
                currentSlice[y] = row;
                previousRow = row;
            }
            previousSlice = currentSlice;
            progress?.Invoke((z - firstZ + 1f) / (lastZ - firstZ));
        }
    }

    private static IEnumerable<int> OverlappingLabels(List<StreamingRun> runs, int start, int end)
    {
        if (runs == null) yield break;
        foreach (var run in runs)
        {
            if (run.EndX <= start) continue;
            if (run.StartX >= end) yield break;
            yield return run.Label;
        }
    }

    #region Streaming mask pipeline

    /// <summary>
    /// One stage of the streamed binary-mask pipeline. Each stage caches its last three output
    /// slices; downstream consumers sweep Z in ascending order, so every slice is computed once.
    /// </summary>
    private abstract class MaskStage
    {
        protected readonly int Width, Height, Depth;
        protected readonly bool ThreeDimensional;
        private readonly byte[][] _ring = new byte[3][];
        private readonly int[] _ringZ = { -1, -1, -1 };

        protected MaskStage(int width, int height, int depth, bool threeDimensional)
        {
            Width = width; Height = height; Depth = depth; ThreeDimensional = threeDimensional;
            for (var i = 0; i < _ring.Length; i++) _ring[i] = new byte[checked(width * height)];
        }

        public byte[] GetSlice(int z)
        {
            if (z < 0 || z >= Depth) return null;
            var slot = z % 3;
            if (_ringZ[slot] != z)
            {
                Compute(z, _ring[slot]);
                _ringZ[slot] = z;
            }
            return _ring[slot];
        }

        protected abstract void Compute(int z, byte[] destination);
    }

    private sealed class MaterialMaskStage : MaskStage
    {
        private readonly ILabelVolumeData _labels;
        private readonly byte _materialId;
        private readonly byte[] _labelBuffer;

        public MaterialMaskStage(ILabelVolumeData labels, byte materialId, int width, int height, int depth,
            bool threeDimensional) : base(width, height, depth, threeDimensional)
        {
            _labels = labels;
            _materialId = materialId;
            _labelBuffer = new byte[checked(width * height)];
        }

        protected override void Compute(int z, byte[] destination)
        {
            _labels.ReadSliceZ(z, _labelBuffer);
            for (var i = 0; i < destination.Length; i++)
                destination[i] = _labelBuffer[i] == _materialId ? (byte)1 : (byte)0;
        }
    }

    /// <summary>Binary majority (median) filter over the 3x3x3 neighborhood (3x3 in 2D mode).</summary>
    private sealed class DespeckleStage : MaskStage
    {
        private readonly MaskStage _source;

        public DespeckleStage(MaskStage source, int width, int height, int depth, bool threeDimensional)
            : base(width, height, depth, threeDimensional) => _source = source;

        protected override void Compute(int z, byte[] destination)
        {
            var below = ThreeDimensional ? _source.GetSlice(z - 1) : null;
            var center = _source.GetSlice(z);
            var above = ThreeDimensional ? _source.GetSlice(z + 1) : null;
            var planes = ThreeDimensional ? 3 : 1;
            var majority = (planes * 9) / 2 + 1;
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var count = 0;
                for (var dy = -1; dy <= 1; dy++)
                {
                    var ny = y + dy;
                    if (ny < 0 || ny >= Height) continue;
                    var rowBase = ny * Width;
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = x + dx;
                        if (nx < 0 || nx >= Width) continue;
                        var i = rowBase + nx;
                        if (below != null && below[i] != 0) count++;
                        if (center[i] != 0) count++;
                        if (above != null && above[i] != 0) count++;
                    }
                }
                destination[y * Width + x] = count >= majority ? (byte)1 : (byte)0;
            }
        }
    }

    /// <summary>Erosion with the 6-connected (4-connected in 2D) cross structuring element.</summary>
    private sealed class ErodeStage : MaskStage
    {
        private readonly MaskStage _source;

        public ErodeStage(MaskStage source, int width, int height, int depth, bool threeDimensional)
            : base(width, height, depth, threeDimensional) => _source = source;

        protected override void Compute(int z, byte[] destination)
        {
            var below = ThreeDimensional ? _source.GetSlice(z - 1) : null;
            var center = _source.GetSlice(z);
            var above = ThreeDimensional ? _source.GetSlice(z + 1) : null;
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var i = y * Width + x;
                var value = center[i];
                if (value != 0)
                {
                    if (x == 0 || center[i - 1] == 0 || x == Width - 1 || center[i + 1] == 0 ||
                        y == 0 || center[i - Width] == 0 || y == Height - 1 || center[i + Width] == 0)
                        value = 0;
                    else if (ThreeDimensional &&
                             (below == null || below[i] == 0 || above == null || above[i] == 0))
                        value = 0;
                }
                destination[i] = value;
            }
        }
    }

    /// <summary>Dilation with the 6-connected (4-connected in 2D) cross structuring element.</summary>
    private sealed class DilateStage : MaskStage
    {
        private readonly MaskStage _source;

        public DilateStage(MaskStage source, int width, int height, int depth, bool threeDimensional)
            : base(width, height, depth, threeDimensional) => _source = source;

        protected override void Compute(int z, byte[] destination)
        {
            var below = ThreeDimensional ? _source.GetSlice(z - 1) : null;
            var center = _source.GetSlice(z);
            var above = ThreeDimensional ? _source.GetSlice(z + 1) : null;
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var i = y * Width + x;
                var value = center[i];
                if (value == 0)
                {
                    if ((x > 0 && center[i - 1] != 0) || (x < Width - 1 && center[i + 1] != 0) ||
                        (y > 0 && center[i - Width] != 0) || (y < Height - 1 && center[i + Width] != 0) ||
                        (below != null && below[i] != 0) || (above != null && above[i] != 0))
                        value = 1;
                }
                destination[i] = value;
            }
        }
    }

    #endregion

    private sealed class StreamingParticleAccumulator
    {
        public readonly int SourceId;
        public long VoxelCount;
        private double _sumX, _sumY, _sumZ;
        private int _minX = int.MaxValue, _minY = int.MaxValue, _minZ = int.MaxValue;
        private int _maxX = int.MinValue, _maxY = int.MinValue, _maxZ = int.MinValue;
        public StreamingParticleAccumulator(int sourceId) => SourceId = sourceId;
        public void Add(StreamingRun run)
        {
            var count = run.EndX - run.StartX;
            VoxelCount += count;
            _sumX += (run.StartX + run.EndX - 1L) * count / 2.0;
            _sumY += (double)run.Y * count;
            _sumZ += (double)run.Z * count;
            _minX = Math.Min(_minX, run.StartX); _maxX = Math.Max(_maxX, run.EndX - 1);
            _minY = Math.Min(_minY, run.Y); _maxY = Math.Max(_maxY, run.Y);
            _minZ = Math.Min(_minZ, run.Z); _maxZ = Math.Max(_maxZ, run.Z);
        }
        public Particle ToParticle(int id, double voxelVolume) => new()
        {
            Id = id, SourceLabel = SourceId, VoxelCount = VoxelCount,
            Center = new Point3D { X = (int)(_sumX / VoxelCount), Y = (int)(_sumY / VoxelCount),
                Z = (int)(_sumZ / VoxelCount) },
            Bounds = new BoundingBox { MinX = _minX, MinY = _minY, MinZ = _minZ,
                MaxX = _maxX, MaxY = _maxY, MaxZ = _maxZ },
            VolumeMicrometers = VoxelCount * voxelVolume * 1e18,
            VolumeMillimeters = VoxelCount * voxelVolume * 1e9
        };
    }

    public void Dispose() { }
}

/// <summary>Dense union-find keyed by provisional run labels. Roots always take the lowest label,
/// so a re-scan of the same mask resolves to identical roots.</summary>
internal sealed class DenseUnionFind
{
    private readonly List<int> _parents = new() { 0 };
    public void Ensure(int value) { while (_parents.Count <= value) _parents.Add(_parents.Count); }
    public int Find(int value)
    {
        Ensure(value);
        var root = value;
        while (_parents[root] != root) root = _parents[root];
        while (_parents[value] != value) { var next = _parents[value]; _parents[value] = root; value = next; }
        return root;
    }
    public void Union(int a, int b)
    {
        var ra = Find(a); var rb = Find(b);
        if (ra != rb) _parents[Math.Max(ra, rb)] = Math.Min(ra, rb);
    }
}

public class Particle
{
    public BoundingBox Bounds;
    public Point3D Center;
    public int Id;
    /// <summary>Internal component root used to address this particle in streamed label write-back.</summary>
    public int SourceLabel;
    public double VolumeMicrometers, VolumeMillimeters;
    public long VoxelCount;
}

public class Point3D { public int X, Y, Z; }
public class BoundingBox { public int MinX, MinY, MinZ, MaxX, MaxY, MaxZ; }

public class ParticleSeparationResult : IDisposable
{
    public bool Is3D;
    [Obsolete("Full-volume component labels were removed; particle statistics are streamed out-of-core.")]
    public int[,,] LabelVolume => null;
    public List<Particle> Particles;
    internal string RunFilePath;
    internal DenseUnionFind Components;

    public void Dispose()
    {
        var path = RunFilePath;
        RunFilePath = null;
        try { if (path != null && File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
