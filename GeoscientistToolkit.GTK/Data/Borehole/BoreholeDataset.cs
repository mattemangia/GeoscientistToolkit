// GeoscientistToolkit/Data/Borehole/BoreholeDataset.cs
// Modified for GTK - Removed PNM/CT/Acoustic dependencies for compilation

using System.Numerics;
using System.Text.Json;
using GeoscientistToolkit.Util;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.AcousticVolume;

namespace GeoscientistToolkit.Data.Borehole;

public enum ContactType
{
    Sharp,
    Erosive,
    Gradational,
    Conformable,
    Unconformity,
    Faulted,
    Intrusive,
    Indistinct
}

public class LithologyUnit
{
    public string ID { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Unknown";
    public string LithologyType { get; set; } = "Sandstone";

    public string Lithology => LithologyType;
    public string RockType => LithologyType;

    public float DepthFrom { get; set; }
    public float DepthTo { get; set; }

    public ContactType UpperContactType { get; set; } = ContactType.Sharp;
    public ContactType LowerContactType { get; set; } = ContactType.Sharp;

    public Vector4 Color { get; set; } = new(0.8f, 0.8f, 0.8f, 1.0f);
    public string Description { get; set; } = "";
    public string GrainSize { get; set; } = "Medium";

    public Dictionary<string, ParameterSource> ParameterSources { get; set; } = new();
    public Dictionary<string, float> Parameters { get; set; } = new();
}

public class ParameterSource
{
    public string DatasetName { get; set; }
    public string DatasetPath { get; set; }
    public DatasetType DatasetType { get; set; }
    public float SourceDepthFrom { get; set; }
    public float SourceDepthTo { get; set; }
    public float Value { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class ParameterTrack
{
    public string Name { get; set; }
    public string Unit { get; set; }
    public float MinValue { get; set; }
    public float MaxValue { get; set; }
    public bool IsLogarithmic { get; set; }
    public Vector4 Color { get; set; }
    public bool IsVisible { get; set; } = true;
    public List<ParameterPoint> Points { get; set; } = new();
}

public class ParameterPoint
{
    public float Depth { get; set; }
    public float Value { get; set; }
    public string SourceDataset { get; set; }
}

public enum LithologyPattern
{
    Solid,
    Dots,
    HorizontalLines,
    VerticalLines,
    Crosses,
    Bricks,
    Diagonal,
    Sand,
    Clay,
    Limestone
}

public class FractureData
{
    public float Depth { get; set; }
    public float? Strike { get; set; }
    public float? Dip { get; set; }
    public float? Aperture { get; set; }
    public string Description { get; set; } = "";
}

public class BoreholeDataset : Dataset, ISerializableDataset
{
    public BoreholeDataset(string name, string filePath) : base(name, filePath)
    {
        Type = DatasetType.Borehole;
        WellName = name;

        InitializeDefaultTracks();
    }

    public string WellName { get; set; }
    public string Field { get; set; }
    public float TotalDepth { get; set; } = 100.0f;
    public float WellDiameter { get; set; } = 0.15f;
    public Vector2 SurfaceCoordinates { get; set; }
    public float Elevation { get; set; }

    public float Diameter => WellDiameter;
    public float WaterTableDepth { get; set; } = 5.0f;

    public List<LithologyUnit> LithologyUnits { get; set; } = new();

    public List<LithologyUnit> Lithology => LithologyUnits;

    public Vector2 Coordinates
    {
        get => SurfaceCoordinates;
        set => SurfaceCoordinates = value;
    }

    public float CoordinatesX
    {
        get => SurfaceCoordinates.X;
        set => SurfaceCoordinates = new Vector2(value, SurfaceCoordinates.Y);
    }

    public float CoordinatesY
    {
        get => SurfaceCoordinates.Y;
        set => SurfaceCoordinates = new Vector2(SurfaceCoordinates.X, value);
    }

    public float Depth
    {
        get => TotalDepth;
        set => TotalDepth = value;
    }

    public List<FractureData> Fractures { get; set; } = new();

    public Dictionary<string, ParameterTrack> ParameterTracks { get; set; } = new();

    public float DepthScaleFactor { get; set; } = 1.0f;
    public bool ShowGrid { get; set; } = true;
    public bool ShowLegend { get; set; } = true;
    public float TrackWidth { get; set; } = 150.0f;

    public Dictionary<string, LithologyPattern> LithologyPatterns { get; set; } = new()
    {
        ["Sandstone"] = LithologyPattern.Sand,
        ["Shale"] = LithologyPattern.HorizontalLines,
        ["Limestone"] = LithologyPattern.Limestone,
        ["Clay"] = LithologyPattern.Clay,
        ["Siltstone"] = LithologyPattern.Dots,
        ["Conglomerate"] = LithologyPattern.Crosses,
        ["Basement"] = LithologyPattern.Diagonal
    };

    public object ToSerializableObject()
    {
         var metadata = new DatasetMetadataDTO
        {
            SampleName = DatasetMetadata.SampleName,
            LocationName = DatasetMetadata.LocationName,
            Latitude = DatasetMetadata.Latitude ?? 0.0, // Handle null
            Longitude = DatasetMetadata.Longitude ?? 0.0,
            CoordinatesX = DatasetMetadata.Coordinates?.X,
            CoordinatesY = DatasetMetadata.Coordinates?.Y,
            Depth = (float)(DatasetMetadata.Depth ?? 0.0),
            SizeX = DatasetMetadata.Size?.X,
            SizeY = DatasetMetadata.Size?.Y,
            SizeZ = DatasetMetadata.Size?.Z,
            SizeUnit = DatasetMetadata.SizeUnit,
            CollectionDate = DatasetMetadata.CollectionDate ?? DateTime.Now,
            Collector = DatasetMetadata.Collector,
            Notes = DatasetMetadata.Notes,
            CustomFields = DatasetMetadata.CustomFields
        };

        return new BoreholeDatasetDTO
        {
            TypeName = "Borehole",
            Name = Name,
            FilePath = FilePath,
            Metadata = metadata,
            WellName = WellName,
            Field = Field,
            TotalDepth = TotalDepth,
            WellDiameter = WellDiameter,
            SurfaceCoordinates = SurfaceCoordinates,
            Elevation = Elevation,
            DepthScaleFactor = DepthScaleFactor,
            ShowGrid = ShowGrid,
            ShowLegend = ShowLegend,
            TrackWidth = TrackWidth,
            LithologyUnits = LithologyUnits.Select(unit => new LithologyUnitDTO
            {
                ID = unit.ID,
                Name = unit.Name,
                LithologyType = unit.LithologyType,
                DepthFrom = unit.DepthFrom,
                DepthTo = unit.DepthTo,
                Color = unit.Color,
                Description = unit.Description,
                GrainSize = unit.GrainSize,
                UpperContactType = unit.UpperContactType,
                LowerContactType = unit.LowerContactType,
                Parameters = unit.Parameters,
                ParameterSources = unit.ParameterSources
            }).ToList(),
            ParameterTracks = ParameterTracks.ToDictionary(
                kvp => kvp.Key,
                kvp => new ParameterTrackDTO
                {
                    Name = kvp.Value.Name,
                    Unit = kvp.Value.Unit,
                    MinValue = kvp.Value.MinValue,
                    MaxValue = kvp.Value.MaxValue,
                    IsLogarithmic = kvp.Value.IsLogarithmic,
                    Color = kvp.Value.Color,
                    IsVisible = kvp.Value.IsVisible,
                    Points = kvp.Value.Points
                })
        };
    }

    private void InitializeDefaultTracks()
    {
        ParameterTracks = new Dictionary<string, ParameterTrack>
        {
            ["Porosity"] = new()
            {
                Name = "Porosity",
                Unit = "%",
                MinValue = 0,
                MaxValue = 40,
                Color = new Vector4(0.2f, 0.6f, 1.0f, 1.0f),
                IsLogarithmic = false
            },
            ["Permeability"] = new()
            {
                Name = "Permeability",
                Unit = "mD",
                MinValue = 0.01f,
                MaxValue = 10000,
                Color = new Vector4(1.0f, 0.4f, 0.2f, 1.0f),
                IsLogarithmic = true
            }
        };
    }

    public void AddLithologyUnit(LithologyUnit unit)
    {
        if (unit.DepthFrom >= unit.DepthTo) return;

        foreach (var existing in LithologyUnits)
            if ((unit.DepthFrom >= existing.DepthFrom && unit.DepthFrom < existing.DepthTo) ||
                (unit.DepthTo > existing.DepthFrom && unit.DepthTo <= existing.DepthTo))
                Logger.LogWarning($"Lithology unit {unit.Name} overlaps with {existing.Name}");

        LithologyUnits.Add(unit);
        LithologyUnits.Sort((a, b) => a.DepthFrom.CompareTo(b.DepthFrom));
    }

    public void ImportParametersFromDataset(Dataset sourceDataset, float depthFrom, float depthTo,
        string[] parameterNames = null)
    {
        // Simplified import
        if (sourceDataset == null) return;

        var unit = GetLithologyUnitAtDepth((depthFrom + depthTo) / 2);
        if (unit == null)
        {
            Logger.LogWarning($"No lithology unit found at depth {(depthFrom + depthTo) / 2}m");
            return;
        }

        switch (sourceDataset)
        {
            // CT support removed

            case AcousticVolumeDataset acousticDataset:
                ImportFromAcousticDataset(acousticDataset, unit, depthFrom, depthTo, parameterNames);
                break;

            default:
                Logger.LogWarning($"Parameter import not implemented for dataset type {sourceDataset.Type}");
                break;
        }

        UpdateParameterTracks();
    }

    private void ImportFromAcousticDataset(AcousticVolumeDataset acoustic, LithologyUnit unit,
        float depthFrom, float depthTo, string[] parameters)
    {
        var source = new ParameterSource
        {
            DatasetName = acoustic.Name,
            DatasetPath = acoustic.FilePath,
            DatasetType = acoustic.Type,
            SourceDepthFrom = depthFrom,
            SourceDepthTo = depthTo,
            LastUpdated = DateTime.Now
        };

        if (parameters == null || parameters.Contains("P-Wave Velocity"))
        {
            source.Value = (float)acoustic.PWaveVelocity;
            unit.ParameterSources["P-Wave Velocity"] = source;
            unit.Parameters["P-Wave Velocity"] = source.Value;
        }

        if (parameters == null || parameters.Contains("S-Wave Velocity"))
        {
            source.Value = (float)acoustic.SWaveVelocity;
            unit.ParameterSources["S-Wave Velocity"] = source;
            unit.Parameters["S-Wave Velocity"] = source.Value;
        }

        if (parameters == null || parameters.Contains("Young's Modulus"))
        {
            source.Value = acoustic.YoungsModulusMPa / 1000.0f;
            unit.ParameterSources["Young's Modulus"] = source;
            unit.Parameters["Young's Modulus"] = source.Value;
        }

        if (parameters == null || parameters.Contains("Poisson's Ratio"))
        {
            source.Value = acoustic.PoissonRatio;
            unit.ParameterSources["Poisson's Ratio"] = source;
            unit.Parameters["Poisson's Ratio"] = source.Value;
        }
    }

    private void UpdateParameterTracks()
    {
        foreach (var track in ParameterTracks.Values)
            track.Points.Clear();

        foreach (var unit in LithologyUnits)
        foreach (var param in unit.Parameters)
            if (ParameterTracks.ContainsKey(param.Key))
            {
                var track = ParameterTracks[param.Key];
                track.Points.Add(new ParameterPoint
                {
                    Depth = unit.DepthFrom,
                    Value = param.Value,
                    SourceDataset = "Manual"
                });
                track.Points.Add(new ParameterPoint
                {
                    Depth = unit.DepthTo,
                    Value = param.Value,
                    SourceDataset = "Manual"
                });
            }

        foreach (var track in ParameterTracks.Values)
            track.Points.Sort((a, b) => a.Depth.CompareTo(b.Depth));
    }

    public LithologyUnit GetLithologyUnitAtDepth(float depth)
    {
        return LithologyUnits.FirstOrDefault(u => depth >= u.DepthFrom && depth <= u.DepthTo);
    }

    public float? GetParameterValueAtDepth(string parameterName, float depth)
    {
        if (!ParameterTracks.ContainsKey(parameterName))
            return null;

        var track = ParameterTracks[parameterName];
        if (track.Points.Count == 0)
            return null;

        ParameterPoint before = null, after = null;

        foreach (var point in track.Points)
            if (point.Depth <= depth)
                before = point;
            else if (after == null)
                after = point;

        if (before == null && after == null)
            return null;

        if (before == null)
            return after.Value;

        if (after == null)
            return before.Value;

        var t = (depth - before.Depth) / (after.Depth - before.Depth);
        return before.Value + (after.Value - before.Value) * t;
    }

    public override long GetSizeInBytes()
    {
        return 0;
    }

    public override void Load()
    {
        if (!string.IsNullOrEmpty(FilePath) && File.Exists(FilePath))
            try
            {
                var json = File.ReadAllText(FilePath);
                var dto = JsonSerializer.Deserialize<BoreholeDatasetDTO>(json);
                if (dto != null)
                    LoadFromDTO(dto);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to load borehole dataset: {ex.Message}");
                IsMissing = true;
            }
    }

    public override void Unload()
    {
        LithologyUnits.Clear();
        ParameterTracks.Clear();
    }

    public void SaveToFile(string path)
    {
        try
        {
            var dto = ToSerializableObject() as BoreholeDatasetDTO;
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(dto, options);
            File.WriteAllText(path, json);
            FilePath = path;
            Logger.Log($"Saved borehole dataset to {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to save borehole dataset: {ex.Message}");
        }
    }

    public void SaveToBinaryFile(string path) {}
    public void LoadFromBinaryFile(string path) {}

    private void LoadFromDTO(BoreholeDatasetDTO dto)
    {
        WellName = dto.WellName;
        Field = dto.Field;
        TotalDepth = dto.TotalDepth;
        WellDiameter = dto.WellDiameter;
        SurfaceCoordinates = dto.SurfaceCoordinates;
        Elevation = dto.Elevation;
        DepthScaleFactor = dto.DepthScaleFactor;
        ShowGrid = dto.ShowGrid;
        ShowLegend = dto.ShowLegend;
        TrackWidth = dto.TrackWidth;

        LithologyUnits.Clear();
        foreach (var unitDto in dto.LithologyUnits)
            LithologyUnits.Add(new LithologyUnit
            {
                ID = unitDto.ID,
                Name = unitDto.Name,
                LithologyType = unitDto.LithologyType,
                DepthFrom = unitDto.DepthFrom,
                DepthTo = unitDto.DepthTo,
                Color = unitDto.Color,
                Description = unitDto.Description,
                GrainSize = unitDto.GrainSize,
                UpperContactType = unitDto.UpperContactType,
                LowerContactType = unitDto.LowerContactType,
                Parameters = unitDto.Parameters,
                ParameterSources = unitDto.ParameterSources
            });

        ParameterTracks.Clear();
        foreach (var kvp in dto.ParameterTracks)
            ParameterTracks[kvp.Key] = new ParameterTrack
            {
                Name = kvp.Value.Name,
                Unit = kvp.Value.Unit,
                MinValue = kvp.Value.MinValue,
                MaxValue = kvp.Value.MaxValue,
                IsLogarithmic = kvp.Value.IsLogarithmic,
                Color = kvp.Value.Color,
                IsVisible = kvp.Value.IsVisible,
                Points = kvp.Value.Points
            };
    }

    public void SyncMetadata()
    {
        DatasetMetadata.SampleName = WellName;
        DatasetMetadata.LocationName = Field;
        DatasetMetadata.Coordinates = SurfaceCoordinates;
        DatasetMetadata.Elevation = Elevation;
        DatasetMetadata.Depth = TotalDepth;
    }
}
