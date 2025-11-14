// GeoscientistToolkit/Data/Seismic/SeismicDataset.cs

using System.Numerics;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Seismic;

/// <summary>
/// Dataset representing seismic reflection/refraction data loaded from SEG-Y files
/// </summary>
public class SeismicDataset : Dataset
{
    // SEG-Y data
    public SegyParser SegyData { get; set; }

    // Display and processing parameters
    public float GainValue { get; set; } = 1.0f;
    public int ColorMapIndex { get; set; } = 0;
    public bool ShowWiggleTrace { get; set; } = true;
    public bool ShowVariableArea { get; set; } = true;
    public bool ShowColorMap { get; set; } = true;

    // Line packages for organizing traces
    public List<SeismicLinePackage> LinePackages { get; set; } = new();

    // Survey metadata
    public string SurveyName { get; set; } = "";
    public string LineNumber { get; set; } = "";
    public string ProcessingHistory { get; set; } = "";
    public bool IsStack { get; set; }
    public bool IsMigrated { get; set; }
    public string DataType { get; set; } = "amplitude";

    // Cache for rendered trace images
    private byte[]? _cachedImageData;
    private int _cachedImageWidth;
    private int _cachedImageHeight;

    public SeismicDataset(string name, string filePath) : base(name, filePath)
    {
        Type = DatasetType.Seismic;
    }

    public override long GetSizeInBytes()
    {
        if (SegyData == null || SegyData.Traces.Count == 0)
            return 0;

        // Estimate: header + (num traces * (header + samples))
        var headerSize = 3600L; // Textual + Binary header
        var traceHeaderSize = 240L;
        var sampleSize = 4L; // float32
        var samplesPerTrace = SegyData.Header?.NumSamples ?? 0;
        var numTraces = SegyData.Traces.Count;

        return headerSize + (numTraces * (traceHeaderSize + (samplesPerTrace * sampleSize)));
    }

    public override void Load()
    {
        if (SegyData != null)
        {
            Logger.Log($"[SeismicDataset] Data already loaded for {Name}");
            return;
        }

        // Data is loaded via the SeismicLoader
        Logger.Log($"[SeismicDataset] Load() called for {Name}");
    }

    public override void Unload()
    {
        SegyData = null;
        _cachedImageData = null;
        LinePackages.Clear();
        Logger.Log($"[SeismicDataset] Unloaded {Name}");
    }

    /// <summary>
    /// Get the number of traces in the dataset
    /// </summary>
    public int GetTraceCount()
    {
        return SegyData?.Traces?.Count ?? 0;
    }

    /// <summary>
    /// Get the number of samples per trace
    /// </summary>
    public int GetSampleCount()
    {
        return SegyData?.Header?.NumSamples ?? 0;
    }

    /// <summary>
    /// Get sample interval in milliseconds
    /// </summary>
    public float GetSampleIntervalMs()
    {
        if (SegyData?.Header == null) return 0;
        return SegyData.Header.SampleInterval / 1000.0f; // Convert from microseconds
    }

    /// <summary>
    /// Get time duration of the seismic section in seconds
    /// </summary>
    public float GetDurationSeconds()
    {
        if (SegyData?.Header == null) return 0;
        return (SegyData.Header.NumSamples * SegyData.Header.SampleInterval) / 1_000_000.0f;
    }

    /// <summary>
    /// Get a specific trace by index
    /// </summary>
    public SegyTrace? GetTrace(int index)
    {
        if (SegyData?.Traces == null || index < 0 || index >= SegyData.Traces.Count)
            return null;

        return SegyData.Traces[index];
    }

    /// <summary>
    /// Get traces within a package
    /// </summary>
    public List<SegyTrace> GetTracesInPackage(SeismicLinePackage package)
    {
        if (SegyData?.Traces == null) return new List<SegyTrace>();

        var traces = new List<SegyTrace>();
        for (int i = package.StartTrace; i <= package.EndTrace && i < SegyData.Traces.Count; i++)
        {
            traces.Add(SegyData.Traces[i]);
        }

        return traces;
    }

    /// <summary>
    /// Find which package contains a given trace index
    /// </summary>
    public SeismicLinePackage? FindPackageForTrace(int traceIndex)
    {
        return LinePackages.FirstOrDefault(pkg => pkg.ContainsTrace(traceIndex));
    }

    /// <summary>
    /// Add a new line package
    /// </summary>
    public void AddLinePackage(SeismicLinePackage package)
    {
        LinePackages.Add(package);
        Logger.Log($"[SeismicDataset] Added package '{package.Name}' with {package.TraceCount} traces");
    }

    /// <summary>
    /// Remove a line package
    /// </summary>
    public void RemoveLinePackage(SeismicLinePackage package)
    {
        LinePackages.Remove(package);
        Logger.Log($"[SeismicDataset] Removed package '{package.Name}'");
    }

    /// <summary>
    /// Get amplitude statistics for the dataset
    /// </summary>
    public (float min, float max, float rms) GetAmplitudeStatistics()
    {
        if (SegyData?.Header == null)
            return (0, 0, 0);

        return (SegyData.Header.MinAmplitude,
                SegyData.Header.MaxAmplitude,
                SegyData.Header.RmsAmplitude);
    }

    /// <summary>
    /// Export dataset state to DTO for serialization
    /// </summary>
    public SeismicDatasetDTO ToDTO()
    {
        var dto = new SeismicDatasetDTO
        {
            TypeName = nameof(SeismicDataset),
            Name = Name,
            FilePath = FilePath,
            Metadata = new DatasetMetadataDTO
            {
                SampleName = DatasetMetadata.SampleName,
                LocationName = DatasetMetadata.LocationName,
                Latitude = DatasetMetadata.Latitude,
                Longitude = DatasetMetadata.Longitude,
                Depth = DatasetMetadata.Depth,
                CollectionDate = DatasetMetadata.CollectionDate,
                Collector = DatasetMetadata.Collector,
                Notes = DatasetMetadata.Notes
            }
        };

        if (SegyData?.Header != null)
        {
            dto.SampleFormat = SegyData.Header.SampleFormat;
            dto.NumTraces = SegyData.Traces.Count;
            dto.NumSamples = SegyData.Header.NumSamples;
            dto.SampleInterval = SegyData.Header.SampleInterval;
            dto.MinAmplitude = SegyData.Header.MinAmplitude;
            dto.MaxAmplitude = SegyData.Header.MaxAmplitude;
            dto.RmsAmplitude = SegyData.Header.RmsAmplitude;
        }

        dto.SurveyName = SurveyName;
        dto.LineNumber = LineNumber;
        dto.ProcessingHistory = ProcessingHistory;
        dto.IsStack = IsStack;
        dto.IsMigrated = IsMigrated;
        dto.DataType = DataType;
        dto.GainValue = GainValue;
        dto.ColorMapIndex = ColorMapIndex;
        dto.ShowWiggleTrace = ShowWiggleTrace;
        dto.ShowVariableArea = ShowVariableArea;

        // Convert line packages
        dto.LinePackages = LinePackages.Select(pkg => new SeismicLinePackageDTO
        {
            Name = pkg.Name,
            StartTrace = pkg.StartTrace,
            EndTrace = pkg.EndTrace,
            IsVisible = pkg.IsVisible,
            Color = pkg.Color,
            Notes = pkg.Notes
        }).ToList();

        return dto;
    }

    /// <summary>
    /// Restore dataset state from DTO
    /// </summary>
    public void FromDTO(SeismicDatasetDTO dto)
    {
        SurveyName = dto.SurveyName;
        LineNumber = dto.LineNumber;
        ProcessingHistory = dto.ProcessingHistory;
        IsStack = dto.IsStack;
        IsMigrated = dto.IsMigrated;
        DataType = dto.DataType;
        GainValue = dto.GainValue;
        ColorMapIndex = dto.ColorMapIndex;
        ShowWiggleTrace = dto.ShowWiggleTrace;
        ShowVariableArea = dto.ShowVariableArea;

        // Restore line packages
        LinePackages = dto.LinePackages.Select(pkgDto => new SeismicLinePackage
        {
            Name = pkgDto.Name,
            StartTrace = pkgDto.StartTrace,
            EndTrace = pkgDto.EndTrace,
            IsVisible = pkgDto.IsVisible,
            Color = pkgDto.Color,
            Notes = pkgDto.Notes
        }).ToList();
    }
}
