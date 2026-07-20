// GAIA/Analysis/VolumeCut/VolumeCutProcessor.cs

using GAIA.Data.CtImageStack;
using GAIA.Data.VolumeData;
using GAIA.Util;

namespace GAIA.Analysis.VolumeCut;

/// <summary>
///     Applies a <see cref="VolumeCutState" /> to a CT dataset out-of-core: one slice in memory
///     at a time, span fills per row, and slices that the cut cannot touch are skipped without
///     ever being read. Works identically on grayscale and label volumes.
///     When <see cref="VolumeCutState.CropToRegion" /> is set the cut also builds resized volumes
///     trimmed to the kept region's bounding box; the caller commits them via
///     <see cref="VolumeCutResult.CommitTo" /> so the swap happens on the UI thread.
/// </summary>
public static class VolumeCutProcessor
{
    /// <summary>
    ///     Applies the cut. Returns null when it edited the volumes in place; returns a
    ///     <see cref="VolumeCutResult" /> to commit when the crop-to-region option resized them.
    /// </summary>
    public static VolumeCutResult Apply(CtImageStackDataset dataset, VolumeCutState state,
        CancellationToken cancellationToken, IProgress<float> progress = null)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(state);
        var applyGrayscale = state.ApplyToGrayscale && dataset.VolumeData != null;
        var applyLabels = state.ApplyToLabels && dataset.LabelData != null;
        if (!applyGrayscale && !applyLabels)
            throw new InvalidOperationException("No target volume selected for the cut.");

        var width = dataset.Width;
        var height = dataset.Height;
        var depth = dataset.Depth;

        if (state.CropToRegion)
        {
            var (x0, y0, z0, x1, y1, z1) = state.GetCropBounds(width, height, depth);
            var cropsAnything = x0 > 0 || y0 > 0 || z0 > 0 ||
                                x1 < width - 1 || y1 < height - 1 || z1 < depth - 1;
            if (cropsAnything)
                return ApplyWithCrop(dataset, state, applyGrayscale, applyLabels,
                    x0, y0, z0, x1, y1, z1, cancellationToken, progress);
            // Nothing to trim (e.g. keep-outside, or the shape already spans the volume):
            // fall through to the in-place path.
        }

        ApplyInPlace(dataset, state, applyGrayscale, applyLabels, width, height, depth,
            cancellationToken, progress);
        return null;
    }

    private static void ApplyInPlace(CtImageStackDataset dataset, VolumeCutState state,
        bool applyGrayscale, bool applyLabels, int width, int height, int depth,
        CancellationToken cancellationToken, IProgress<float> progress)
    {
        var sliceLength = checked(width * height);
        var grayscaleSlice = applyGrayscale ? new byte[sliceLength] : null;
        var labelSlice = applyLabels ? new byte[sliceLength] : null;
        var zeroSlice = new byte[sliceLength];
        var keepInside = state.KeepMode == VolumeCutKeepMode.KeepInside;

        for (var z = 0; z < depth; z++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (anyInside, fullyInside) = state.ClassifySlice(z, width, height);

            if (!anyInside)
            {
                // Nothing of the shape on this slice: keep-outside leaves it untouched,
                // keep-inside clears it without reading (WriteSliceZ skips unchanged chunks).
                if (keepInside)
                {
                    if (applyGrayscale) dataset.VolumeData.WriteSliceZ(z, zeroSlice);
                    if (applyLabels) dataset.LabelData.WriteSliceZ(z, zeroSlice);
                }

                progress?.Report((z + 1f) / depth);
                continue;
            }

            if (fullyInside)
            {
                if (!keepInside)
                {
                    if (applyGrayscale) dataset.VolumeData.WriteSliceZ(z, zeroSlice);
                    if (applyLabels) dataset.LabelData.WriteSliceZ(z, zeroSlice);
                }

                progress?.Report((z + 1f) / depth);
                continue;
            }

            if (applyGrayscale) dataset.VolumeData.ReadSliceZ(z, grayscaleSlice);
            if (applyLabels) dataset.LabelData.ReadSliceZ(z, labelSlice);

            CutPartialSlice(state, grayscaleSlice, labelSlice, z, width, height, keepInside);

            if (applyGrayscale) dataset.VolumeData.WriteSliceZ(z, grayscaleSlice);
            if (applyLabels) dataset.LabelData.WriteSliceZ(z, labelSlice);
            progress?.Report((z + 1f) / depth);
        }

        Logger.Log($"[VolumeCut] Applied {state.Shape} cut ({state.KeepMode}) to " +
                   $"{(applyGrayscale ? "grayscale" : "")}{(applyGrayscale && applyLabels ? "+" : "")}" +
                   $"{(applyLabels ? "labels" : "")} on {width}x{height}x{depth}");
    }

    private static VolumeCutResult ApplyWithCrop(CtImageStackDataset dataset, VolumeCutState state,
        bool applyGrayscale, bool applyLabels, int x0, int y0, int z0, int x1, int y1, int z1,
        CancellationToken cancellationToken, IProgress<float> progress)
    {
        var width = dataset.Width;
        var height = dataset.Height;
        var sliceLength = checked(width * height);
        var keepInside = state.KeepMode == VolumeCutKeepMode.KeepInside;

        var cw = x1 - x0 + 1;
        var ch = y1 - y0 + 1;
        var cd = z1 - z0 + 1;
        var cropLength = checked(cw * ch);

        // Both channels are resized to keep the dataset dimensions consistent, even when the cut
        // itself only clears one of them: whichever exists is cropped, but only the targeted
        // channel is emptied outside the shape.
        var hasGray = dataset.VolumeData != null;
        var hasLabels = dataset.LabelData != null;
        var chunkDim = dataset.VolumeData?.ChunkDim ??
                       dataset.LabelData?.ChunkDim ?? ChunkedVolume.DEFAULT_CHUNK_DIM;

        var graySrc = hasGray ? new byte[sliceLength] : null;
        var labelSrc = hasLabels ? new byte[sliceLength] : null;
        var grayDst = hasGray ? new byte[cropLength] : null;
        var labelDst = hasLabels ? new byte[cropLength] : null;

        var newGray = hasGray
            ? new ChunkedVolume(cw, ch, cd, chunkDim) { PixelSize = dataset.VolumeData.PixelSize }
            : null;
        var newLabels = hasLabels ? new ChunkedLabelVolume(cw, ch, cd, chunkDim, false) : null;

        for (var zd = 0; zd < cd; zd++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var z = z0 + zd;

            if (hasGray) dataset.VolumeData.ReadSliceZ(z, graySrc);
            if (hasLabels) dataset.LabelData.ReadSliceZ(z, labelSrc);

            CutFullSlice(state, applyGrayscale ? graySrc : null, applyLabels ? labelSrc : null,
                z, width, height, keepInside);

            for (var yd = 0; yd < ch; yd++)
            {
                var srcRow = (y0 + yd) * width + x0;
                var dstRow = yd * cw;
                if (hasGray) Array.Copy(graySrc, srcRow, grayDst, dstRow, cw);
                if (hasLabels) Array.Copy(labelSrc, srcRow, labelDst, dstRow, cw);
            }

            if (hasGray) newGray.WriteSliceZ(zd, grayDst);
            if (hasLabels) newLabels.WriteSliceZ(zd, labelDst);
            progress?.Report((zd + 1f) / cd);
        }

        Logger.Log($"[VolumeCut] Applied {state.Shape} cut ({state.KeepMode}) with crop " +
                   $"{width}x{height}x{dataset.Depth} -> {cw}x{ch}x{cd}");
        return new VolumeCutResult(newGray, newLabels, cw, ch, cd);
    }

    /// <summary>Clears a slice already in memory so only the kept voxels of slice z survive,
    /// handling the fully-inside and fully-outside cases as well as the partial one.</summary>
    private static void CutFullSlice(VolumeCutState state, byte[] gray, byte[] label, int z,
        int width, int height, bool keepInside)
    {
        var (anyInside, fullyInside) = state.ClassifySlice(z, width, height);
        var length = width * height;
        if (!anyInside)
        {
            if (keepInside)
            {
                gray?.AsSpan(0, length).Clear();
                label?.AsSpan(0, length).Clear();
            }

            return;
        }

        if (fullyInside)
        {
            if (!keepInside)
            {
                gray?.AsSpan(0, length).Clear();
                label?.AsSpan(0, length).Clear();
            }

            return;
        }

        CutPartialSlice(state, gray, label, z, width, height, keepInside);
    }

    /// <summary>Row-by-row span clears for a slice that the shape only partially covers.</summary>
    private static void CutPartialSlice(VolumeCutState state, byte[] gray, byte[] label, int z,
        int width, int height, bool keepInside)
    {
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            if (state.TryGetRowInsideSpan(y, z, width, out var start, out var end))
            {
                if (keepInside)
                {
                    gray?.AsSpan(row, start).Clear();
                    gray?.AsSpan(row + end, width - end).Clear();
                    label?.AsSpan(row, start).Clear();
                    label?.AsSpan(row + end, width - end).Clear();
                }
                else
                {
                    gray?.AsSpan(row + start, end - start).Clear();
                    label?.AsSpan(row + start, end - start).Clear();
                }
            }
            else if (keepInside)
            {
                gray?.AsSpan(row, width).Clear();
                label?.AsSpan(row, width).Clear();
            }
        }
    }
}

/// <summary>
///     The resized volumes produced by a crop-to-region cut. Kept out of the dataset until
///     <see cref="CommitTo" /> swaps them in on the UI thread, so the viewers never observe a
///     half-applied change in dimensions.
/// </summary>
public sealed class VolumeCutResult
{
    private readonly ChunkedLabelVolume _labels;
    private readonly ChunkedVolume _volume;

    public VolumeCutResult(ChunkedVolume volume, ChunkedLabelVolume labels, int width, int height, int depth)
    {
        _volume = volume;
        _labels = labels;
        Width = width;
        Height = height;
        Depth = depth;
    }

    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }

    public void CommitTo(CtImageStackDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        var oldVolume = dataset.VolumeData;
        var oldLabels = dataset.LabelData;

        if (_volume != null) dataset.VolumeData = _volume;
        if (_labels != null) dataset.LabelData = _labels;
        dataset.Width = Width;
        dataset.Height = Height;
        dataset.Depth = Depth;
        dataset.LabelData?.SetVirtualThresholdRules(dataset.VolumeData, dataset.VirtualThresholdRules);

        if (_volume != null) oldVolume?.Dispose();
        if (_labels != null) oldLabels?.Dispose();
    }
}
