using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// Provides filtering capabilities for removing unwanted blocks from slope stability models.
    /// This is essential for cleaning up meshes before simulation to prevent messy results.
    /// </summary>
    public static class BlockFilter
    {
        /// <summary>
        /// Filters blocks based on volume criteria.
        /// Removes blocks that are too small or too large for meaningful analysis.
        /// </summary>
        /// <param name="blocks">Input list of blocks</param>
        /// <param name="minVolume">Minimum volume in m³ (blocks smaller than this are removed)</param>
        /// <param name="maxVolume">Maximum volume in m³ (blocks larger than this are removed)</param>
        /// <param name="removeSmall">Enable removal of small blocks</param>
        /// <param name="removeLarge">Enable removal of large blocks</param>
        /// <returns>Filtered list of blocks</returns>
        public static List<Block> FilterByVolume(
            List<Block> blocks,
            float minVolume = 0.001f,
            float maxVolume = 1000.0f,
            bool removeSmall = true,
            bool removeLarge = false)
        {
            var filtered = new List<Block>();
            int removedSmall = 0;
            int removedLarge = 0;

            foreach (var block in blocks)
            {
                // Calculate volume if not already done
                if (block.Volume <= 0)
                {
                    block.CalculateGeometricProperties();
                }

                // Check minimum volume
                if (removeSmall && block.Volume < minVolume)
                {
                    removedSmall++;
                    continue;
                }

                // Check maximum volume
                if (removeLarge && block.Volume > maxVolume)
                {
                    removedLarge++;
                    continue;
                }

                filtered.Add(block);
            }

            Console.WriteLine($"Block filtering: Kept {filtered.Count}, Removed {removedSmall} small, {removedLarge} large");

            return filtered;
        }

        /// <summary>
        /// Filters blocks based on mass criteria.
        /// Useful when density varies across materials.
        /// </summary>
        public static List<Block> FilterByMass(
            List<Block> blocks,
            float minMass = 0.0f,
            float maxMass = float.MaxValue)
        {
            return blocks.Where(b => b.Mass >= minMass && b.Mass <= maxMass).ToList();
        }

        /// <summary>
        /// Filters blocks based on position criteria.
        /// Useful for removing blocks outside region of interest.
        /// </summary>
        public static List<Block> FilterByPosition(
            List<Block> blocks,
            Vector3? minPosition = null,
            Vector3? maxPosition = null)
        {
            var filtered = blocks;

            if (minPosition.HasValue)
            {
                filtered = filtered.Where(b =>
                    b.Position.X >= minPosition.Value.X &&
                    b.Position.Y >= minPosition.Value.Y &&
                    b.Position.Z >= minPosition.Value.Z).ToList();
            }

            if (maxPosition.HasValue)
            {
                filtered = filtered.Where(b =>
                    b.Position.X <= maxPosition.Value.X &&
                    b.Position.Y <= maxPosition.Value.Y &&
                    b.Position.Z <= maxPosition.Value.Z).ToList();
            }

            return filtered;
        }

        /// <summary>
        /// Filters blocks based on elevation (Z coordinate).
        /// Useful for analyzing specific slope sections.
        /// </summary>
        public static List<Block> FilterByElevation(
            List<Block> blocks,
            float? minElevation = null,
            float? maxElevation = null)
        {
            var filtered = blocks;

            if (minElevation.HasValue)
            {
                filtered = filtered.Where(b => b.Position.Z >= minElevation.Value).ToList();
            }

            if (maxElevation.HasValue)
            {
                filtered = filtered.Where(b => b.Position.Z <= maxElevation.Value).ToList();
            }

            return filtered;
        }

        /// <summary>
        /// Filters out degenerate blocks with invalid geometry.
        /// Removes blocks with too few vertices or faces.
        /// </summary>
        public static List<Block> FilterDegenerateGeometry(List<Block> blocks)
        {
            return blocks.Where(b =>
                b.Vertices.Count >= 4 &&
                b.Faces.Count >= 4).ToList();
        }

        /// <summary>
        /// Filters blocks based on material assignment.
        /// </summary>
        public static List<Block> FilterByMaterial(
            List<Block> blocks,
            List<int> allowedMaterialIds)
        {
            return blocks.Where(b => allowedMaterialIds.Contains(b.MaterialId)).ToList();
        }

        /// <summary>
        /// Filters out fixed blocks.
        /// Useful for analyzing only movable blocks.
        /// </summary>
        public static List<Block> FilterOutFixed(List<Block> blocks)
        {
            return blocks.Where(b => !b.IsFixed).ToList();
        }

        /// <summary>
        /// Filters blocks based on aspect ratio.
        /// Removes very thin or elongated blocks (slivers).
        /// </summary>
        /// <param name="blocks">Input list of blocks</param>
        /// <param name="maxAspectRatio">Maximum ratio of longest to shortest dimension (e.g., 10.0)</param>
        /// <returns>Filtered list of blocks</returns>
        public static List<Block> FilterByAspectRatio(
            List<Block> blocks,
            float maxAspectRatio = 10.0f)
        {
            var filtered = new List<Block>();

            foreach (var block in blocks)
            {
                // Calculate bounding box dimensions
                if (block.Vertices.Count == 0)
                    continue;

                Vector3 min = new Vector3(float.MaxValue);
                Vector3 max = new Vector3(float.MinValue);

                foreach (var vertex in block.Vertices)
                {
                    min = Vector3.Min(min, vertex);
                    max = Vector3.Max(max, vertex);
                }

                Vector3 dimensions = max - min;
                float minDim = MathF.Min(dimensions.X, MathF.Min(dimensions.Y, dimensions.Z));
                float maxDim = MathF.Max(dimensions.X, MathF.Max(dimensions.Y, dimensions.Z));

                if (minDim > 1e-6f)  // Avoid division by zero
                {
                    float aspectRatio = maxDim / minDim;
                    if (aspectRatio <= maxAspectRatio)
                    {
                        filtered.Add(block);
                    }
                }
            }

            Console.WriteLine($"Aspect ratio filtering: Kept {filtered.Count}/{blocks.Count} blocks");

            return filtered;
        }

        /// <summary>
        /// Comprehensive filtering with all common criteria.
        /// </summary>
        public static FilterResult FilterBlocks(List<Block> blocks, FilterCriteria criteria)
        {
            var result = new FilterResult
            {
                OriginalCount = blocks.Count,
                Blocks = new List<Block>(blocks)
            };

            // Volume filtering
            if (criteria.FilterByVolume)
            {
                int beforeCount = result.Blocks.Count;
                result.Blocks = FilterByVolume(
                    result.Blocks,
                    criteria.MinVolume,
                    criteria.MaxVolume,
                    criteria.RemoveSmall,
                    criteria.RemoveLarge);
                result.RemovedByVolume = beforeCount - result.Blocks.Count;
            }

            // Mass filtering
            if (criteria.FilterByMass)
            {
                int beforeCount = result.Blocks.Count;
                result.Blocks = FilterByMass(
                    result.Blocks,
                    criteria.MinMass,
                    criteria.MaxMass);
                result.RemovedByMass = beforeCount - result.Blocks.Count;
            }

            // Position filtering
            if (criteria.FilterByPosition)
            {
                int beforeCount = result.Blocks.Count;
                result.Blocks = FilterByPosition(
                    result.Blocks,
                    criteria.MinPosition,
                    criteria.MaxPosition);
                result.RemovedByPosition = beforeCount - result.Blocks.Count;
            }

            // Elevation filtering
            if (criteria.FilterByElevation)
            {
                int beforeCount = result.Blocks.Count;
                result.Blocks = FilterByElevation(
                    result.Blocks,
                    criteria.MinElevation,
                    criteria.MaxElevation);
                result.RemovedByElevation = beforeCount - result.Blocks.Count;
            }

            // Degenerate geometry filtering
            if (criteria.FilterDegenerateGeometry)
            {
                int beforeCount = result.Blocks.Count;
                result.Blocks = FilterDegenerateGeometry(result.Blocks);
                result.RemovedDegenerate = beforeCount - result.Blocks.Count;
            }

            // Aspect ratio filtering
            if (criteria.FilterByAspectRatio)
            {
                int beforeCount = result.Blocks.Count;
                result.Blocks = FilterByAspectRatio(result.Blocks, criteria.MaxAspectRatio);
                result.RemovedByAspectRatio = beforeCount - result.Blocks.Count;
            }

            // Fixed blocks filtering
            if (criteria.FilterOutFixed)
            {
                int beforeCount = result.Blocks.Count;
                result.Blocks = FilterOutFixed(result.Blocks);
                result.RemovedFixed = beforeCount - result.Blocks.Count;
            }

            result.FinalCount = result.Blocks.Count;
            result.TotalRemoved = result.OriginalCount - result.FinalCount;

            return result;
        }

        /// <summary>
        /// Gets statistics about block sizes in the collection.
        /// Useful for determining appropriate filter thresholds.
        /// </summary>
        public static BlockStatistics GetStatistics(List<Block> blocks)
        {
            if (blocks.Count == 0)
                return new BlockStatistics();

            var volumes = blocks.Select(b => b.Volume).OrderBy(v => v).ToList();
            var masses = blocks.Select(b => b.Mass).OrderBy(m => m).ToList();

            return new BlockStatistics
            {
                Count = blocks.Count,
                MinVolume = volumes.First(),
                MaxVolume = volumes.Last(),
                MedianVolume = volumes[volumes.Count / 2],
                MeanVolume = volumes.Average(),
                MinMass = masses.First(),
                MaxMass = masses.Last(),
                MedianMass = masses[masses.Count / 2],
                MeanMass = masses.Average()
            };
        }
    }

    /// <summary>
    /// Filter criteria for block filtering.
    /// </summary>
    public class FilterCriteria
    {
        public bool FilterByVolume { get; set; } = true;
        public float MinVolume { get; set; } = 0.001f;
        public float MaxVolume { get; set; } = 1000.0f;
        public bool RemoveSmall { get; set; } = true;
        public bool RemoveLarge { get; set; } = false;

        public bool FilterByMass { get; set; } = false;
        public float MinMass { get; set; } = 0.0f;
        public float MaxMass { get; set; } = float.MaxValue;

        public bool FilterByPosition { get; set; } = false;
        public Vector3? MinPosition { get; set; } = null;
        public Vector3? MaxPosition { get; set; } = null;

        public bool FilterByElevation { get; set; } = false;
        public float? MinElevation { get; set; } = null;
        public float? MaxElevation { get; set; } = null;

        public bool FilterDegenerateGeometry { get; set; } = true;

        public bool FilterByAspectRatio { get; set; } = false;
        public float MaxAspectRatio { get; set; } = 10.0f;

        public bool FilterOutFixed { get; set; } = false;
    }

    /// <summary>
    /// Result of block filtering operation.
    /// </summary>
    public class FilterResult
    {
        public int OriginalCount { get; set; }
        public int FinalCount { get; set; }
        public int TotalRemoved { get; set; }

        public int RemovedByVolume { get; set; }
        public int RemovedByMass { get; set; }
        public int RemovedByPosition { get; set; }
        public int RemovedByElevation { get; set; }
        public int RemovedDegenerate { get; set; }
        public int RemovedByAspectRatio { get; set; }
        public int RemovedFixed { get; set; }

        public List<Block> Blocks { get; set; } = new List<Block>();

        public string GetSummary()
        {
            return $"Filtered {TotalRemoved} blocks ({OriginalCount} → {FinalCount})\n" +
                   $"  By volume: {RemovedByVolume}\n" +
                   $"  By mass: {RemovedByMass}\n" +
                   $"  By position: {RemovedByPosition}\n" +
                   $"  By elevation: {RemovedByElevation}\n" +
                   $"  Degenerate: {RemovedDegenerate}\n" +
                   $"  By aspect ratio: {RemovedByAspectRatio}\n" +
                   $"  Fixed blocks: {RemovedFixed}";
        }
    }

    /// <summary>
    /// Statistics about block collection.
    /// </summary>
    public class BlockStatistics
    {
        public int Count { get; set; }
        public float MinVolume { get; set; }
        public float MaxVolume { get; set; }
        public float MedianVolume { get; set; }
        public float MeanVolume { get; set; }
        public float MinMass { get; set; }
        public float MaxMass { get; set; }
        public float MedianMass { get; set; }
        public float MeanMass { get; set; }

        public string GetSummary()
        {
            return $"Block Statistics ({Count} blocks):\n" +
                   $"  Volume: min={MinVolume:F3} m³, median={MedianVolume:F3} m³, max={MaxVolume:F3} m³\n" +
                   $"  Mass: min={MinMass:F1} kg, median={MedianMass:F1} kg, max={MaxMass:F1} kg";
        }
    }
}
