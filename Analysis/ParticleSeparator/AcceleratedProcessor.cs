using System.Diagnostics;
using System.Numerics;
using GAIA.Data.CtImageStack;
using GAIA.Util;

namespace GAIA.Analysis.ParticleSeparator;

public enum AccelerationType { Auto, CPU, SIMD, GPU }

/// <summary>
/// Out-of-core connected-component analysis for CT material labels. Components are represented
/// as horizontal runs, so working memory depends on two slices and component count, not voxels.
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
        bool conservative, float minSize, int zSlice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(material);
        if (dataset.LabelData == null) throw new InvalidOperationException("CT label data is not loaded.");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            CurrentStage = "Scanning run-length components";
            var components = new DenseUnionFind();
            ScanMaterialRuns(dataset, material.ID, use3D, zSlice, components, null, cancellationToken,
                value => Progress = value * .55f);

            CurrentStage = "Computing particle statistics";
            var accumulators = new Dictionary<int, StreamingParticleAccumulator>();
            ScanMaterialRuns(dataset, material.ID, use3D, zSlice, components, run =>
            {
                var root = components.Find(run.Label);
                if (!accumulators.TryGetValue(root, out var accumulator))
                    accumulators[root] = accumulator = new StreamingParticleAccumulator(root);
                accumulator.Add(run);
            }, cancellationToken, value => Progress = .55f + value * .4f);

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
            return new ParticleSeparationResult { Particles = particles, Is3D = use3D };
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ParticleSeparator] Out-of-core analysis failed: {ex.Message}");
            throw;
        }
    }

    private readonly record struct StreamingRun(int Z, int Y, int StartX, int EndX, int Label);

    private static void ScanMaterialRuns(CtImageStackDataset dataset, byte materialId, bool use3D, int requestedZ,
        DenseUnionFind components, Action<StreamingRun> visitor, CancellationToken token, Action<float> progress)
    {
        var firstZ = use3D ? 0 : Math.Clamp(requestedZ, 0, dataset.Depth - 1);
        var lastZ = use3D ? dataset.Depth : firstZ + 1;
        var previousSlice = new List<StreamingRun>[dataset.Height];
        var slice = new byte[checked(dataset.Width * dataset.Height)];
        var nextLabel = 1;
        for (var z = firstZ; z < lastZ; z++)
        {
            token.ThrowIfCancellationRequested();
            dataset.LabelData.ReadSliceZ(z, slice);
            var currentSlice = new List<StreamingRun>[dataset.Height];
            List<StreamingRun> previousRow = null;
            for (var y = 0; y < dataset.Height; y++)
            {
                var row = new List<StreamingRun>();
                var x = 0;
                while (x < dataset.Width)
                {
                    while (x < dataset.Width && slice[y * dataset.Width + x] != materialId) x++;
                    if (x >= dataset.Width) break;
                    var start = x++;
                    while (x < dataset.Width && slice[y * dataset.Width + x] == materialId) x++;
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

    private sealed class DenseUnionFind
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
            Id = id, VoxelCount = VoxelCount,
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

public class Particle
{
    public BoundingBox Bounds;
    public Point3D Center;
    public int Id;
    public double VolumeMicrometers, VolumeMillimeters;
    public long VoxelCount;
}

public class Point3D { public int X, Y, Z; }
public class BoundingBox { public int MinX, MinY, MinZ, MaxX, MaxY, MaxZ; }

public class ParticleSeparationResult
{
    public bool Is3D;
    [Obsolete("Full-volume component labels were removed; particle statistics are streamed out-of-core.")]
    public int[,,] LabelVolume => null;
    public List<Particle> Particles;
}
