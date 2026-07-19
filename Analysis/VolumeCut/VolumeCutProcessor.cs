// GAIA/Analysis/VolumeCut/VolumeCutProcessor.cs

using GAIA.Data.CtImageStack;
using GAIA.Util;

namespace GAIA.Analysis.VolumeCut;

/// <summary>
///     Applies a <see cref="VolumeCutState" /> to a CT dataset out-of-core: one slice in memory
///     at a time, span fills per row, and slices that the cut cannot touch are skipped without
///     ever being read. Works identically on grayscale and label volumes.
/// </summary>
public static class VolumeCutProcessor
{
    public static void Apply(CtImageStackDataset dataset, VolumeCutState state,
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

            for (var y = 0; y < height; y++)
            {
                var row = y * width;
                if (state.TryGetRowInsideSpan(y, z, width, out var start, out var end))
                {
                    if (keepInside)
                    {
                        grayscaleSlice?.AsSpan(row, start).Clear();
                        grayscaleSlice?.AsSpan(row + end, width - end).Clear();
                        labelSlice?.AsSpan(row, start).Clear();
                        labelSlice?.AsSpan(row + end, width - end).Clear();
                    }
                    else
                    {
                        grayscaleSlice?.AsSpan(row + start, end - start).Clear();
                        labelSlice?.AsSpan(row + start, end - start).Clear();
                    }
                }
                else if (keepInside)
                {
                    grayscaleSlice?.AsSpan(row, width).Clear();
                    labelSlice?.AsSpan(row, width).Clear();
                }
            }

            if (applyGrayscale) dataset.VolumeData.WriteSliceZ(z, grayscaleSlice);
            if (applyLabels) dataset.LabelData.WriteSliceZ(z, labelSlice);
            progress?.Report((z + 1f) / depth);
        }

        Logger.Log($"[VolumeCut] Applied {state.Shape} cut ({state.KeepMode}) to " +
                   $"{(applyGrayscale ? "grayscale" : "")}{(applyGrayscale && applyLabels ? "+" : "")}" +
                   $"{(applyLabels ? "labels" : "")} on {width}x{height}x{depth}");
    }
}
