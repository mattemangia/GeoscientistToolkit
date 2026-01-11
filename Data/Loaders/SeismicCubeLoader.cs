// GeoscientistToolkit/Data/Loaders/SeismicCubeLoader.cs

using GeoscientistToolkit.Data.Seismic;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders;

/// <summary>
/// Loader for Seismic Cube datasets from .seiscube files
/// </summary>
public class SeismicCubeLoader : IDataLoader
{
    public string FilePath { get; set; } = "";

    public string Name => "Seismic Cube";

    public string Description => "Load a 3D seismic cube created from intersecting 2D seismic lines";

    public bool CanImport => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath) && IsValidSeismicCubeFile();

    public string ValidationMessage
    {
        get
        {
            if (string.IsNullOrEmpty(FilePath))
                return "Please select a seismic cube file (.seiscube)";

            if (!File.Exists(FilePath))
                return "Selected file does not exist";

            if (!FilePath.EndsWith(SeismicCubeSerializer.FileExtension, StringComparison.OrdinalIgnoreCase))
                return $"File must have {SeismicCubeSerializer.FileExtension} extension";

            if (!IsValidSeismicCubeFile())
                return "Invalid seismic cube file format";

            return "";
        }
    }

    public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
    {
        try
        {
            progressReporter?.Report((0.0f, "Starting seismic cube import..."));
            Logger.Log($"[SeismicCubeLoader] Loading seismic cube from: {FilePath}");

            var cube = await SeismicCubeSerializer.ImportAsync(FilePath, progressReporter);

            if (cube == null)
            {
                throw new Exception("Failed to load seismic cube");
            }

            progressReporter?.Report((1.0f, $"Loaded seismic cube with {cube.Lines.Count} lines!"));
            Logger.Log($"[SeismicCubeLoader] Successfully loaded cube '{cube.Name}' with {cube.Lines.Count} lines, " +
                       $"{cube.Intersections.Count} intersections, {cube.Packages.Count} packages");

            return cube;
        }
        catch (Exception ex)
        {
            Logger.LogError($"[SeismicCubeLoader] Error loading seismic cube: {ex.Message}");
            progressReporter?.Report((0.0f, $"Error: {ex.Message}"));
            throw;
        }
    }

    public void Reset()
    {
        FilePath = "";
    }

    private bool IsValidSeismicCubeFile()
    {
        try
        {
            if (!File.Exists(FilePath))
                return false;

            using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read);
            if (fs.Length < 8)
                return false;

            var magicBytes = new byte[8];
            fs.Read(magicBytes, 0, 8);
            var magic = System.Text.Encoding.ASCII.GetString(magicBytes);

            return magic == SeismicCubeSerializer.MagicNumber;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Loader configuration UI for seismic cube files
/// </summary>
public class SeismicCubeLoaderUI
{
    private readonly SeismicCubeLoader _loader;
    private bool _showPreview = false;
    private SeismicCubePreviewInfo? _previewInfo;

    public SeismicCubeLoaderUI(SeismicCubeLoader loader)
    {
        _loader = loader;
    }

    public void DrawUI()
    {
        // Show file info
        if (!string.IsNullOrEmpty(_loader.FilePath) && File.Exists(_loader.FilePath))
        {
            var fileInfo = new FileInfo(_loader.FilePath);
            ImGuiNET.ImGui.Text($"File: {Path.GetFileName(_loader.FilePath)}");
            ImGuiNET.ImGui.Text($"Size: {FormatBytes(fileInfo.Length)}");

            if (ImGuiNET.ImGui.Button("Load Preview"))
            {
                LoadPreviewInfo();
            }

            if (_previewInfo != null)
            {
                ImGuiNET.ImGui.Separator();
                ImGuiNET.ImGui.Text("Preview Information:");
                ImGuiNET.ImGui.BulletText($"Lines: {_previewInfo.LineCount}");
                ImGuiNET.ImGui.BulletText($"Intersections: {_previewInfo.IntersectionCount}");
                ImGuiNET.ImGui.BulletText($"Packages: {_previewInfo.PackageCount}");
                ImGuiNET.ImGui.BulletText($"Has Volume: {(_previewInfo.HasVolume ? "Yes" : "No")}");
                ImGuiNET.ImGui.BulletText($"Bounds: {_previewInfo.BoundsString}");
            }
        }
        else
        {
            ImGuiNET.ImGui.TextDisabled("Select a .seiscube file to import");
        }
    }

    private void LoadPreviewInfo()
    {
        try
        {
            using var fs = new FileStream(_loader.FilePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs);

            // Read magic
            var magic = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(8));
            if (magic != SeismicCubeSerializer.MagicNumber)
            {
                _previewInfo = null;
                return;
            }

            // Read version and flags
            reader.ReadInt32(); // version
            byte flags = reader.ReadByte();
            reader.ReadByte(); // compression
            reader.ReadBytes(16); // reserved

            // Read statistics
            int lineCount = reader.ReadInt32();
            int intersectionCount = reader.ReadInt32();
            int packageCount = reader.ReadInt32();

            // Read bounds
            float minX = reader.ReadSingle();
            float maxX = reader.ReadSingle();
            float minY = reader.ReadSingle();
            float maxY = reader.ReadSingle();
            float minZ = reader.ReadSingle();
            float maxZ = reader.ReadSingle();

            _previewInfo = new SeismicCubePreviewInfo
            {
                LineCount = lineCount,
                IntersectionCount = intersectionCount,
                PackageCount = packageCount,
                HasVolume = (flags & 0x01) != 0,
                BoundsString = $"X: {minX:F0}-{maxX:F0}, Y: {minY:F0}-{maxY:F0}, T: {minZ:F0}-{maxZ:F0}"
            };
        }
        catch
        {
            _previewInfo = null;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}

internal class SeismicCubePreviewInfo
{
    public int LineCount { get; set; }
    public int IntersectionCount { get; set; }
    public int PackageCount { get; set; }
    public bool HasVolume { get; set; }
    public string BoundsString { get; set; } = "";
}
