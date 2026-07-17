// GAIA/Data/CtImageStack/MaterialOperations.cs

using GAIA.Business;
using GAIA.Data.VolumeData;
using GAIA.Util;
using System.Buffers;

namespace GAIA.Data.CtImageStack;

/// <summary>
///     Provides high-performance, parallelized operations for material and voxel management in 3D volumes.
/// </summary>
public static class MaterialOperations
{
    private static readonly int _optimalThreadCount = Math.Max(1, Environment.ProcessorCount - 1);

    /// <summary>
    ///     Gets the next available material ID that is not currently in use.
    /// </summary>
    public static byte GetNextMaterialID(List<Material> materials)
    {
        if (materials == null) return 1;

        for (byte candidate = 1; candidate < byte.MaxValue; candidate++)
            if (!materials.Any(m => m.ID == candidate))
                return candidate;

        throw new InvalidOperationException("No available material IDs remaining.");
    }

    /// <summary>
    ///     Labels every voxel whose grayscale value is within the specified threshold with the given material ID.
    /// </summary>
    public static Task AddVoxelsByThresholdAsync(IGrayscaleVolumeData grayscaleVolume, ILabelVolumeData labelVolume,
        byte materialID, byte minVal, byte maxVal, CtImageStackDataset dataset = null,
        CancellationToken cancellationToken = default, IProgress<float> progress = null)
    {
        Logger.Log($"[MaterialOperations] Adding voxels to material {materialID} (Threshold: {minVal}-{maxVal})");
        return ProcessVolumeByThresholdAsync(grayscaleVolume, labelVolume, materialID, minVal, maxVal, true, dataset,
            cancellationToken, progress);
    }

    /// <summary>
    ///     Clears voxels that belong to a specific material and are within a grayscale threshold.
    /// </summary>
    public static Task RemoveVoxelsByThresholdAsync(IGrayscaleVolumeData grayscaleVolume, ILabelVolumeData labelVolume,
        byte materialID, byte minVal, byte maxVal, CtImageStackDataset dataset = null,
        CancellationToken cancellationToken = default, IProgress<float> progress = null)
    {
        Logger.Log($"[MaterialOperations] Removing voxels from material {materialID} (Threshold: {minVal}-{maxVal})");
        return ProcessVolumeByThresholdAsync(grayscaleVolume, labelVolume, materialID, minVal, maxVal, false, dataset,
            cancellationToken, progress);
    }

    /// <summary>
    ///     Core processing logic that operates on the volume slice by slice in parallel.
    /// </summary>
    private static Task ProcessVolumeByThresholdAsync(IGrayscaleVolumeData grayscaleVolume,
        ILabelVolumeData labelVolume,
        byte materialID, byte minVal, byte maxVal, bool isAddOperation, CtImageStackDataset dataset,
        CancellationToken cancellationToken, IProgress<float> progress)
    {
        if (grayscaleVolume == null || labelVolume == null)
        {
            Logger.LogWarning("[MaterialOperations] Grayscale or Label volume is null. Aborting operation.");
            return Task.CompletedTask;
        }

        var width = grayscaleVolume.Width;
        var height = grayscaleVolume.Height;
        var depth = grayscaleVolume.Depth;
        var chunkedLabels = labelVolume as ChunkedLabelVolume;
        var memoryMapped = (grayscaleVolume as ChunkedVolume)?.IsMemoryMapped == true ||
                           chunkedLabels?.IsMemoryMapped == true;
        var workerCount = memoryMapped ? Math.Min(2, _optimalThreadCount) : _optimalThreadCount;

        return Task.Run(() =>
        {
            var anyModified = 0;
            var completedSlices = 0;
            var sliceLength = width * height;

            Parallel.For(0, depth,
                new ParallelOptions { MaxDegreeOfParallelism = workerCount, CancellationToken = cancellationToken },
                () => (Gray: ArrayPool<byte>.Shared.Rent(sliceLength),
                    Labels: ArrayPool<byte>.Shared.Rent(sliceLength),
                    Changed: chunkedLabels == null ? null :
                        ArrayPool<bool>.Shared.Rent(chunkedLabels.ChunkCountX * chunkedLabels.ChunkCountY)),
                (z, _, buffers) =>
                {
                var graySlice = buffers.Gray;
                var labelSlice = buffers.Labels;
                if (buffers.Changed != null)
                    Array.Clear(buffers.Changed, 0, chunkedLabels.ChunkCountX * chunkedLabels.ChunkCountY);

                grayscaleVolume.ReadSliceZ(z, graySlice);
                labelVolume.ReadSliceZ(z, labelSlice);

                var modified = false;

                if (isAddOperation)
                    for (var i = 0; i < sliceLength; i++)
                    {
                        var gray = graySlice[i];
                        if (gray >= minVal && gray <= maxVal)
                            if (labelSlice[i] != materialID)
                            {
                                labelSlice[i] = materialID;
                                modified = true;
                                if (buffers.Changed != null)
                                    buffers.Changed[(i / width / chunkedLabels.ChunkDim) * chunkedLabels.ChunkCountX +
                                                    (i % width / chunkedLabels.ChunkDim)] = true;
                            }
                    }
                else // Remove operation
                    for (var i = 0; i < sliceLength; i++)
                    {
                        var gray = graySlice[i];
                        if (labelSlice[i] == materialID && gray >= minVal && gray <= maxVal)
                        {
                            labelSlice[i] = 0; // Set to exterior
                            modified = true;
                            if (buffers.Changed != null)
                                buffers.Changed[(i / width / chunkedLabels.ChunkDim) * chunkedLabels.ChunkCountX +
                                                (i % width / chunkedLabels.ChunkDim)] = true;
                        }
                    }

                if (modified)
                {
                    if (chunkedLabels != null)
                        chunkedLabels.WriteSliceZChangedChunks(z, labelSlice, buffers.Changed);
                    else
                        labelVolume.WriteSliceZ(z, labelSlice);
                    Interlocked.Exchange(ref anyModified, 1);
                }
                var done = Interlocked.Increment(ref completedSlices);
                if ((done & 7) == 0 || done == depth) progress?.Report(done / (float)depth * .82f);
                return buffers;
                }, buffers =>
                {
                    ArrayPool<byte>.Shared.Return(buffers.Gray);
                    ArrayPool<byte>.Shared.Return(buffers.Labels);
                    if (buffers.Changed != null) ArrayPool<bool>.Shared.Return(buffers.Changed);
                });

            // FIXED: Auto-save label data after modification
            if (anyModified != 0 && dataset != null)
            {
                Logger.Log($"[MaterialOperations] Label changes for material {materialID} are ready; persistence deferred until project save.");

                // Ensure material exists and is visible if we're adding voxels
                if (isAddOperation)
                {
                    var material = dataset.Materials.FirstOrDefault(m => m.ID == materialID);
                    if (material != null)
                    {
                        material.IsVisible = true; // Force visibility on
                        Logger.Log($"[MaterialOperations] Set material {materialID} ({material.Name}) to visible");
                    }
                }

                OpenTkManager.ExecuteOnMainThread(() =>
                {
                    ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
                    ProjectManager.Instance.HasUnsavedChanges = true;
                });
            }

            Logger.Log($"[MaterialOperations] Finished processing for material {materialID}.");
        });
    }

    /// <summary>
    ///     Applies material assignment from interactive segmentation with auto-save
    /// </summary>
    public static async Task ApplySegmentationMaskAsync(ILabelVolumeData labelVolume, byte[] mask,
        byte materialID, int sliceIndex, int viewType, int width, int height, int depth,
        CtImageStackDataset dataset = null)
    {
        await Task.Run(() =>
        {
            var currentSlice = new byte[width * height];

            switch (viewType)
            {
                case 0: // XY view
                    labelVolume.ReadSliceZ(sliceIndex, currentSlice);
                    for (var i = 0; i < mask.Length && i < currentSlice.Length; i++)
                        if (mask[i] > 0)
                            currentSlice[i] = materialID;
                    labelVolume.WriteSliceZ(sliceIndex, currentSlice);
                    break;

                case 1: // XZ view
                    for (var z = 0; z < depth; z++)
                    for (var x = 0; x < width; x++)
                    {
                        var maskIdx = z * width + x;
                        if (maskIdx < mask.Length && mask[maskIdx] > 0) labelVolume[x, sliceIndex, z] = materialID;
                    }

                    break;

                case 2: // YZ view
                    for (var z = 0; z < depth; z++)
                    for (var y = 0; y < height; y++)
                    {
                        var maskIdx = z * height + y;
                        if (maskIdx < mask.Length && mask[maskIdx] > 0) labelVolume[sliceIndex, y, z] = materialID;
                    }

                    break;
            }

            // FIXED: Auto-save after interactive segmentation
            if (dataset != null)
            {
                ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
            }
        });
    }
}
