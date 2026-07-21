using System.Numerics;
using GAIA.Data.VolumeData;

namespace GAIA.Data.CtImageStack;

internal sealed record CtSliceTextureRequest(
    int View,
    int Slice,
    int SourceWidth,
    int SourceHeight,
    float WindowLevel,
    float WindowWidth,
    int ColorMap,
    IReadOnlyDictionary<byte, (Vector4 Color, float Opacity)> Materials,
    (bool Active, byte Min, byte Max, Vector4 Color) Threshold,
    byte[] CommittedSelection,
    byte[] LiveSelection,
    Vector4 SelectionColor,
    bool ExternalPreviewActive,
    CtPreviewVolume ExternalPreview,
    Vector4 ExternalPreviewColor);

// SourceWidth/SourceHeight are the full slice dimensions the request was built for; Width/Height
// are the (possibly downsampled) texture dimensions. The consumer must match staleness against the
// source dimensions, not the texture ones, or a downsampled slice is discarded as a size mismatch.
internal sealed record CtSliceTextureResult(
    int View, int Slice, int SourceWidth, int SourceHeight, int Width, int Height, byte[] Rgba);

/// <summary>CPU-only, cancellable slice renderer. It never touches ImGui/OpenGL.</summary>
internal static class CtSliceTexturePipeline
{
    private const int MaxTextureAxis = 2048;
    private const long MaxTexturePixels = 4L * 1024 * 1024;

    public static CtSliceTextureResult Build(CtImageStackDataset dataset, CtSliceTextureRequest request,
        CancellationToken token)
    {
        var grayscale = new byte[checked(request.SourceWidth * request.SourceHeight)];
        ReadGrayscale(dataset, request.View, request.Slice, grayscale);
        token.ThrowIfCancellationRequested();

        byte[] labels = null;
        if (dataset.LabelData != null && request.Materials.Count > 0)
        {
            labels = new byte[grayscale.Length];
            ReadLabels(dataset, request.View, request.Slice, labels);
        }

        var (targetWidth, targetHeight) = Fit(request.SourceWidth, request.SourceHeight);
        var rgba = new byte[checked(targetWidth * targetHeight * 4)];
        var minWindow = request.WindowLevel - request.WindowWidth * .5f;
        var windowRange = Math.Max(1e-5f, request.WindowWidth);

        Parallel.For(0, targetHeight, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
            CancellationToken = token
        }, targetY =>
        {
            var sourceY = Math.Min(request.SourceHeight - 1, targetY * request.SourceHeight / targetHeight);
            for (var targetX = 0; targetX < targetWidth; targetX++)
            {
                var sourceX = Math.Min(request.SourceWidth - 1, targetX * request.SourceWidth / targetWidth);
                var sourceIndex = sourceY * request.SourceWidth + sourceX;
                var density = grayscale[sourceIndex];
                var normalized = Math.Clamp((density - minWindow) / windowRange, 0f, 1f);
                var color = CtColorMap.Apply(normalized, request.ColorMap);

                if (labels != null && request.Materials.TryGetValue(labels[sourceIndex], out var material))
                    color = Vector4.Lerp(color, material.Color, material.Opacity);

                if (request.Threshold.Active && density >= request.Threshold.Min && density <= request.Threshold.Max)
                    color = Vector4.Lerp(color, request.Threshold.Color, .5f);

                if (request.CommittedSelection?.Length > sourceIndex && request.CommittedSelection[sourceIndex] > 0)
                    color = Vector4.Lerp(color, request.SelectionColor, .4f);
                if (request.LiveSelection?.Length > sourceIndex && request.LiveSelection[sourceIndex] > 0)
                    color = Vector4.Lerp(color, request.SelectionColor, .6f);

                if (request.ExternalPreviewActive && request.ExternalPreview != null &&
                    IsExternalPreviewSet(dataset, request, sourceX, sourceY))
                    color = Vector4.Lerp(color, request.ExternalPreviewColor, .5f);

                var output = (targetY * targetWidth + targetX) * 4;
                rgba[output] = (byte)(Math.Clamp(color.X, 0, 1) * 255);
                rgba[output + 1] = (byte)(Math.Clamp(color.Y, 0, 1) * 255);
                rgba[output + 2] = (byte)(Math.Clamp(color.Z, 0, 1) * 255);
                rgba[output + 3] = 255;
            }
        });
        return new CtSliceTextureResult(request.View, request.Slice, request.SourceWidth, request.SourceHeight,
            targetWidth, targetHeight, rgba);
    }

    private static void ReadGrayscale(CtImageStackDataset dataset, int view, int slice, byte[] destination)
    {
        var volume = dataset.VolumeData;
        switch (view)
        {
            case 0: volume.ReadSliceZ(slice, destination); break;
            case 1 when volume is ChunkedVolume chunked: chunked.ReadSliceXZ(slice, destination); break;
            case 2 when volume is ChunkedVolume chunked: chunked.ReadSliceYZ(slice, destination); break;
            case 1:
                for (var z = 0; z < dataset.Depth; z++)
                for (var x = 0; x < dataset.Width; x++) destination[z * dataset.Width + x] = volume[x, slice, z];
                break;
            case 2:
                for (var z = 0; z < dataset.Depth; z++)
                for (var y = 0; y < dataset.Height; y++) destination[z * dataset.Height + y] = volume[slice, y, z];
                break;
        }
    }

    private static void ReadLabels(CtImageStackDataset dataset, int view, int slice, byte[] destination)
    {
        var labels = dataset.LabelData;
        switch (view)
        {
            case 0: labels.ReadSliceZ(slice, destination); break;
            case 1 when labels is ChunkedLabelVolume chunked: chunked.ReadSliceXZ(slice, destination); break;
            case 2 when labels is ChunkedLabelVolume chunked: chunked.ReadSliceYZ(slice, destination); break;
            case 1:
                for (var z = 0; z < dataset.Depth; z++)
                for (var x = 0; x < dataset.Width; x++) destination[z * dataset.Width + x] = labels[x, slice, z];
                break;
            case 2:
                for (var z = 0; z < dataset.Depth; z++)
                for (var y = 0; y < dataset.Height; y++) destination[z * dataset.Height + y] = labels[slice, y, z];
                break;
        }
    }

    private static (int Width, int Height) Fit(int width, int height)
    {
        var scale = Math.Min(1d, Math.Min(MaxTextureAxis / (double)Math.Max(width, height),
            Math.Sqrt(MaxTexturePixels / (double)((long)width * height))));
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static bool IsExternalPreviewSet(CtImageStackDataset dataset, CtSliceTextureRequest request,
        int sourceX, int sourceY)
    {
        int x, y, z;
        switch (request.View)
        {
            case 0: x = sourceX; y = sourceY; z = request.Slice; break;
            case 1: x = sourceX; y = request.Slice; z = sourceY; break;
            default: x = request.Slice; y = sourceX; z = sourceY; break;
        }
        return request.ExternalPreview.GetVoxel(x, y, z) > 0;
    }
}

/// <summary>CPU counterpart of the volume shader's cmap function.</summary>
internal static class CtColorMap
{
    internal static Vector4 Apply(float value, int colorMap)
    {
        var x = Math.Clamp(value, 0f, 1f);
        return colorMap switch
        {
            1 => new Vector4(Math.Clamp(3 * x, 0, 1), Math.Clamp(3 * x - 1, 0, 1),
                Math.Clamp(3 * x - 2, 0, 1), 1),
            2 => new Vector4(x, 1 - x, 1, 1),
            3 => new Vector4(Rainbow(x, 0), Rainbow(x, 4), Rainbow(x, 2), 1),
            _ => new Vector4(x, x, x, 1)
        };
    }

    private static float Rainbow(float value, float offset)
    {
        var wrapped = (value * 6f + offset) % 6f;
        return Math.Clamp(MathF.Abs(wrapped - 3f) - 1f, 0f, 1f);
    }
}
