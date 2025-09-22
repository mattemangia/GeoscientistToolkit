// GeoscientistToolkit/Analysis/ParticleSeparator/AcceleratedProcessor.cs
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Util;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace GeoscientistToolkit.Analysis.ParticleSeparator
{
    public enum AccelerationType
    {
        Auto,
        CPU,
        SIMD,
        GPU
    }
    
    public class AcceleratedProcessor : IDisposable
    {
        public float Progress { get; set; }
        public string CurrentStage { get; set; } = "";
        public double LastProcessingTime { get; set; }
        public long VoxelsPerSecond { get; set; }
        public int ThreadCount { get; set; } = Environment.ProcessorCount;
        public AccelerationType SelectedAcceleration { get; set; } = AccelerationType.Auto;
        private bool _gpuAvailable = false;

        public AcceleratedProcessor()
        {
            // Placeholder for future GPU initialization
        }

        public (string Message, Vector4 Color) GetAccelerationStatus()
        {
            bool simdAvailable = Vector.IsHardwareAccelerated;
            if (_gpuAvailable) return ("✓ GPU Available", new Vector4(0, 1, 0, 1));
            if (simdAvailable) return ("✓ SIMD Available", new Vector4(0.5f, 1, 0, 1));
            return ("✓ Multi-threaded CPU", new Vector4(1, 1, 0, 1));
        }

        public ParticleSeparationResult SeparateParticles(CtImageStackDataset dataset, Material material, bool use3D, bool conservative, float minSize, int zSlice, CancellationToken cancellationToken)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                Progress = 0.1f; CurrentStage = "Extracting mask";
                var mask = ExtractMaterialMaskOptimized(dataset, material, cancellationToken);
                Progress = 0.3f; CurrentStage = "Labeling components";
                
                int[,,] labels;
                if (use3D)
                {
                    labels = SelectedAcceleration switch
                    {
                        AccelerationType.GPU when _gpuAvailable => LabelComponents3DParallel(mask, cancellationToken), // Placeholder
                        AccelerationType.SIMD when Vector.IsHardwareAccelerated => LabelComponents3DParallel(mask, cancellationToken), // Placeholder
                        _ => LabelComponents3DParallel(mask, cancellationToken)
                    };
                }
                else
                {
                    labels = LabelComponents2DOptimized(mask, zSlice, cancellationToken);
                }
                
                Progress = 0.7f; CurrentStage = "Analyzing particles";
                var particles = AnalyzeParticlesOptimized(labels, dataset.PixelSize, conservative ? (int)minSize : 1, cancellationToken);
                Progress = 1.0f; CurrentStage = "Complete";
                
                stopwatch.Stop();
                LastProcessingTime = stopwatch.Elapsed.TotalSeconds;
                long totalVoxels = use3D ? (long)dataset.Width * dataset.Height * dataset.Depth : (long)dataset.Width * dataset.Height;
                VoxelsPerSecond = (long)(totalVoxels / LastProcessingTime);

                return new ParticleSeparationResult { LabelVolume = labels, Particles = particles, Is3D = use3D };
            }
            catch (Exception ex)
            {
                Logger.LogError($"[AcceleratedProcessor] Error: {ex.Message}");
                throw;
            }
        }
        
        private byte[,,] ExtractMaterialMaskOptimized(CtImageStackDataset dataset, Material material, CancellationToken cancellationToken)
        {
            int width = dataset.Width; int height = dataset.Height; int depth = dataset.Depth;
            byte[,,] mask = new byte[width, height, depth];
            var partitioner = Partitioner.Create(0, depth);
            Parallel.ForEach(partitioner, new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = ThreadCount }, range =>
            {
                for (int z = range.Item1; z < range.Item2; z++)
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    if (dataset.LabelData != null && dataset.LabelData[x, y, z] == material.ID)
                    {
                        mask[x, y, z] = 1;
                    }
                }
                Progress = 0.1f + ((float)range.Item2 / depth * 0.2f);
            });
            return mask;
        }

        private int[,,] LabelComponents3DParallel(byte[,,] mask, CancellationToken cancellationToken)
        {
            int width = mask.GetLength(0); int height = mask.GetLength(1); int depth = mask.GetLength(2);
            int[,,] labels = new int[width, height, depth];
            var unionFind = new ConcurrentUnionFind();
            int nextLabel = 1;

            Parallel.For(0, depth, new ParallelOptions { CancellationToken = cancellationToken }, z =>
            {
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    if (mask[x, y, z] == 0) continue;
                    var neighbors = new List<int>(3);
                    if (x > 0 && labels[x - 1, y, z] > 0) neighbors.Add(labels[x - 1, y, z]);
                    if (y > 0 && labels[x, y - 1, z] > 0) neighbors.Add(labels[x, y - 1, z]);
                    if (z > 0 && labels[x, y, z - 1] > 0) neighbors.Add(labels[x, y, z - 1]);

                    if (neighbors.Count == 0)
                    {
                        int label = Interlocked.Increment(ref nextLabel) - 1;
                        labels[x, y, z] = label;
                        unionFind.MakeSet(label);
                    }
                    else
                    {
                        int minLabel = neighbors.Min();
                        labels[x, y, z] = minLabel;
                        foreach (var label in neighbors) unionFind.Union(minLabel, label);
                    }
                }
                Progress = 0.3f + ((float)(z + 1) / depth * 0.2f);
            });

            ResolveEquivalencesParallel(labels, unionFind, cancellationToken);
            return labels;
        }

        private void ResolveEquivalencesParallel(int[,,] labels, ConcurrentUnionFind unionFind, CancellationToken token)
        {
             int width = labels.GetLength(0), height = labels.GetLength(1), depth = labels.GetLength(2);
             var finalLabels = new ConcurrentDictionary<int, int>();
             int finalLabelCounter = 0;
             Parallel.For(0, depth, new ParallelOptions { CancellationToken = token }, z =>
             {
                 for (int y = 0; y < height; y++)
                 for (int x = 0; x < width; x++)
                 {
                     if (labels[x, y, z] > 0)
                     {
                         int root = unionFind.Find(labels[x, y, z]);
                         if (!finalLabels.ContainsKey(root))
                         {
                             finalLabels.TryAdd(root, Interlocked.Increment(ref finalLabelCounter));
                         }
                         labels[x, y, z] = finalLabels[root];
                     }
                 }
                 Progress = 0.5f + ((float)(z + 1) / depth * 0.2f);
             });
        }

        private int[,,] LabelComponents2DOptimized(byte[,,] mask, int slice, CancellationToken cancellationToken)
        {
            int width = mask.GetLength(0); int height = mask.GetLength(1); int depth = mask.GetLength(2);
            int[,,] labels = new int[width, height, depth];
            int nextLabel = 1;
            var equivalences = new Dictionary<int, int>();

            for (int y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < width; x++)
                {
                    if (mask[x, y, slice] == 0) continue;
                    int left = (x > 0) ? labels[x - 1, y, slice] : 0;
                    int top = (y > 0) ? labels[x, y - 1, slice] : 0;
                    if (left == 0 && top == 0) labels[x, y, slice] = nextLabel++;
                    else if (left > 0 && top == 0) labels[x, y, slice] = left;
                    else if (left == 0 && top > 0) labels[x, y, slice] = top;
                    else
                    {
                        labels[x, y, slice] = Math.Min(left, top);
                        if (left != top) UnionLabels(equivalences, left, top);
                    }
                }
            }
            
            for (int y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < width; x++)
                {
                    if (labels[x, y, slice] > 0)
                    {
                        labels[x, y, slice] = FindRoot(equivalences, labels[x, y, slice]);
                    }
                }
            }
            return labels;
        }

        private void UnionLabels(Dictionary<int, int> equivalences, int a, int b)
        {
            int rootA = FindRoot(equivalences, a);
            int rootB = FindRoot(equivalences, b);
            if (rootA != rootB) equivalences[Math.Max(rootA, rootB)] = Math.Min(rootA, rootB);
        }

        private int FindRoot(Dictionary<int, int> equivalences, int label)
        {
            while (equivalences.ContainsKey(label)) label = equivalences[label];
            return label;
        }

        private List<Particle> AnalyzeParticlesOptimized(int[,,] labels, double pixelSize, int minSize, CancellationToken cancellationToken)
        {
            var particles = new ConcurrentDictionary<int, ParticleAccumulator>();
            int width = labels.GetLength(0); int height = labels.GetLength(1); int depth = labels.GetLength(2);
            Parallel.For(0, depth, new ParallelOptions { CancellationToken = cancellationToken }, z =>
            {
                var localParticles = new Dictionary<int, ParticleAccumulator>();
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int label = labels[x, y, z];
                    if (label == 0) continue;
                    if (!localParticles.TryGetValue(label, out var acc))
                    {
                        acc = new ParticleAccumulator { Id = label };
                        localParticles[label] = acc;
                    }
                    acc.VoxelCount++; acc.CenterSum += new Vector3(x, y, z); acc.UpdateBounds(x, y, z);
                }
                foreach (var kvp in localParticles) particles.AddOrUpdate(kvp.Key, kvp.Value, (key, existing) => { existing.Merge(kvp.Value); return existing; });
                Progress = 0.7f + ((float)(z + 1) / depth * 0.3f);
            });

            double voxelVolume = pixelSize * pixelSize * pixelSize;
            return particles.Values.Where(acc => acc.VoxelCount >= minSize)
                .Select(acc => new Particle
                {
                    Id = acc.Id, VoxelCount = acc.VoxelCount,
                    Center = new Point3D { X = (int)(acc.CenterSum.X / acc.VoxelCount), Y = (int)(acc.CenterSum.Y / acc.VoxelCount), Z = (int)(acc.CenterSum.Z / acc.VoxelCount) },
                    Bounds = new BoundingBox { MinX = acc.MinX, MinY = acc.MinY, MinZ = acc.MinZ, MaxX = acc.MaxX, MaxY = acc.MaxY, MaxZ = acc.MaxZ },
                    VolumeMicrometers = acc.VoxelCount * voxelVolume * 1e18,
                    VolumeMillimeters = acc.VoxelCount * voxelVolume * 1e9
                }).ToList();
        }

        public void Dispose() { /* Cleanup GPU resources if any */ }
        
        private class ParticleAccumulator
        {
            public int Id; public int VoxelCount; public Vector3 CenterSum;
            public int MinX = int.MaxValue, MinY = int.MaxValue, MinZ = int.MaxValue;
            public int MaxX = int.MinValue, MaxY = int.MinValue, MaxZ = int.MinValue;
            public void UpdateBounds(int x, int y, int z) { MinX=Math.Min(MinX,x); MinY=Math.Min(MinY,y); MinZ=Math.Min(MinZ,z); MaxX=Math.Max(MaxX,x); MaxY=Math.Max(MaxY,y); MaxZ=Math.Max(MaxZ,z); }
            public void Merge(ParticleAccumulator other) { VoxelCount+=other.VoxelCount; CenterSum+=other.CenterSum; UpdateBounds(other.MinX,other.MinY,other.MinZ); UpdateBounds(other.MaxX,other.MaxY,other.MaxZ); }
        }
    }
    
    public class ConcurrentUnionFind { private readonly ConcurrentDictionary<int, int> _p = new ConcurrentDictionary<int, int>(); public void MakeSet(int x) => _p.TryAdd(x, x); public int Find(int x) { if(!_p.TryGetValue(x, out int parent)) { MakeSet(x); return x; } if (parent==x) return x; return _p[x] = Find(parent); } public void Union(int x, int y) { int rX=Find(x), rY=Find(y); if(rX!=rY) _p[Math.Max(rX,rY)] = Math.Min(rX,rY); } }
    public class Particle { public int Id; public int VoxelCount; public double VolumeMicrometers, VolumeMillimeters; public Point3D Center; public BoundingBox Bounds; }
    public class Point3D { public int X, Y, Z; }
    public class BoundingBox { public int MinX, MinY, MinZ, MaxX, MaxY, MaxZ; }
    public class ParticleSeparationResult { public int[,,] LabelVolume; public List<Particle> Particles; public bool Is3D; }
}