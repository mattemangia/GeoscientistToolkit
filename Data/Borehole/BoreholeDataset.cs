// GeoscientistToolkit/Data/Borehole/BoreholeDataset.cs

using System.Numerics;
using System.Text.Json;
using GeoscientistToolkit.Analysis.NMR;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Borehole;

/// <summary>
///     Represents a lithological unit in the borehole with depth range
/// </summary>
public class LithologyUnit
{
    public string ID { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Unknown";
    public string LithologyType { get; set; } = "Sandstone"; // Sandstone, Shale, Limestone, etc.

    // Compatibility aliases
    public string Lithology => LithologyType; // Alias for LithologyType
    public string RockType => LithologyType; // Alias for LithologyType

    public float DepthFrom { get; set; }
    public float DepthTo { get; set; }
    public Vector4 Color { get; set; } = new(0.8f, 0.8f, 0.8f, 1.0f);
    public string Description { get; set; } = "";
    public string GrainSize { get; set; } = "Medium"; // Clay, Silt, Fine, Medium, Coarse, etc.

    // Source dataset references for parameters
    public Dictionary<string, ParameterSource> ParameterSources { get; set; } = new();

    // Cached parameter values (interpolated/averaged from sources)
    public Dictionary<string, float> Parameters { get; set; } = new();
}

/// <summary>
///     Defines the source of a petrophysical parameter
/// </summary>
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

/// <summary>
///     Defines a petrophysical parameter track for the log display
/// </summary>
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

/// <summary>
///     Symbol patterns for different lithologies
/// </summary>
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

/// <summary>
///     Represents a fracture detected in the borehole
/// </summary>
public class FractureData
{
    public float Depth { get; set; }
    public float? Strike { get; set; }
    public float? Dip { get; set; }
    public float? Aperture { get; set; }
    public string Description { get; set; } = "";
}

/// <summary>
///     Dataset representing a borehole/well log with geological and petrophysical data
/// </summary>
public class BoreholeDataset : Dataset, ISerializableDataset
{
    public BoreholeDataset(string name, string filePath) : base(name, filePath)
    {
        Type = DatasetType.Borehole;
        WellName = name;

        // Initialize default parameter tracks
        InitializeDefaultTracks();
    }

    // Core properties
    public string WellName { get; set; }
    public string Field { get; set; }
    public float TotalDepth { get; set; } = 100.0f; // meters
    public float WellDiameter { get; set; } = 0.15f; // meters
    public Vector2 SurfaceCoordinates { get; set; } // X, Y in project coordinates
    public float Elevation { get; set; } // meters above sea level


    // Alias properties for compatibility with geothermal tools
    public float Diameter => WellDiameter; // Alias for WellDiameter (in meters)
    public float WaterTableDepth { get; set; } = 5.0f; // Depth to water table in meters

    // Lithology units
    public List<LithologyUnit> LithologyUnits { get; set; } = new();

    // Alias for compatibility
    public List<LithologyUnit> Lithology => LithologyUnits;

    // Compatibility properties for serialization and metadata
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

    // Fractures collection
    public List<FractureData> Fractures { get; set; } = new();

    // Parameter tracks
    public Dictionary<string, ParameterTrack> ParameterTracks { get; set; } = new();

    // Display settings
    public float DepthScaleFactor { get; set; } = 1.0f; // pixels per meter
    public bool ShowGrid { get; set; } = true;
    public bool ShowLegend { get; set; } = true;
    public float TrackWidth { get; set; } = 150.0f; // pixels

    // Lithology patterns mapping
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
            },
            ["P-Wave Velocity"] = new()
            {
                Name = "P-Wave Velocity",
                Unit = "m/s",
                MinValue = 1500,
                MaxValue = 6000,
                Color = new Vector4(0.8f, 0.2f, 0.8f, 1.0f),
                IsLogarithmic = false
            },
            ["Thermal Conductivity"] = new()
            {
                Name = "Thermal Conductivity",
                Unit = "W/m·K",
                MinValue = 0.1f,
                MaxValue = 5,
                Color = new Vector4(1.0f, 0.6f, 0.2f, 1.0f),
                IsLogarithmic = false
            },
            ["Thermal Diffusivity"] = new() // ADDED
            {
                Name = "Thermal Diffusivity",
                Unit = "m²/s",
                MinValue = 1e-7f,
                MaxValue = 5e-6f,
                Color = new Vector4(0.0f, 0.8f, 0.8f, 1.0f), // Cyan
                IsLogarithmic = true
            },
            ["Young's Modulus"] = new()
            {
                Name = "Young's Modulus",
                Unit = "GPa",
                MinValue = 1,
                MaxValue = 100,
                Color = new Vector4(0.2f, 0.8f, 0.4f, 1.0f),
                IsLogarithmic = false
            },
            ["Poisson's Ratio"] = new()
            {
                Name = "Poisson's Ratio",
                Unit = "-",
                MinValue = 0.1f,
                MaxValue = 0.5f,
                Color = new Vector4(0.6f, 0.4f, 0.8f, 1.0f),
                IsLogarithmic = false
            }
        };
    }

    /// <summary>
    ///     Add a lithology unit to the borehole
    /// </summary>
    public void AddLithologyUnit(LithologyUnit unit)
    {
        // Validate depth range
        if (unit.DepthFrom >= unit.DepthTo)
        {
            Logger.LogWarning($"Invalid depth range for unit {unit.Name}: {unit.DepthFrom} to {unit.DepthTo}");
            return;
        }

        // Check for overlaps and adjust if necessary
        foreach (var existing in LithologyUnits)
            if ((unit.DepthFrom >= existing.DepthFrom && unit.DepthFrom < existing.DepthTo) ||
                (unit.DepthTo > existing.DepthFrom && unit.DepthTo <= existing.DepthTo))
                Logger.LogWarning($"Lithology unit {unit.Name} overlaps with {existing.Name}");
        // Could implement automatic splitting/merging here
        LithologyUnits.Add(unit);
        LithologyUnits.Sort((a, b) => a.DepthFrom.CompareTo(b.DepthFrom));

        Logger.Log($"Added lithology unit {unit.Name} from {unit.DepthFrom}m to {unit.DepthTo}m");
    }

    /// <summary>
    ///     Import parameters from another dataset for a specific depth range
    /// </summary>
    public void ImportParametersFromDataset(Dataset sourceDataset, float depthFrom, float depthTo,
        string[] parameterNames = null)
    {
        if (sourceDataset == null) return;

        var unit = GetLithologyUnitAtDepth((depthFrom + depthTo) / 2);
        if (unit == null)
        {
            Logger.LogWarning($"No lithology unit found at depth {(depthFrom + depthTo) / 2}m");
            return;
        }

        // Extract parameters based on dataset type
        switch (sourceDataset)
        {
            case CtImageStackDataset ctDataset:
                ImportFromCTDataset(ctDataset, unit, depthFrom, depthTo, parameterNames);
                break;

            case PNMDataset pnmDataset:
                ImportFromPNMDataset(pnmDataset, unit, depthFrom, depthTo, parameterNames);
                break;

            case AcousticVolumeDataset acousticDataset:
                ImportFromAcousticDataset(acousticDataset, unit, depthFrom, depthTo, parameterNames);
                break;

            default:
                Logger.LogWarning($"Parameter import not implemented for dataset type {sourceDataset.Type}");
                break;
        }

        // Update parameter tracks with new data points
        UpdateParameterTracks();
    }

    private void ImportFromCTDataset(CtImageStackDataset ct, LithologyUnit unit,
        float depthFrom, float depthTo, string[] parameters)
    {
        var source = new ParameterSource
        {
            DatasetName = ct.Name,
            DatasetPath = ct.FilePath,
            DatasetType = ct.Type,
            SourceDepthFrom = depthFrom,
            SourceDepthTo = depthTo,
            LastUpdated = DateTime.Now
        };

        // Import thermal conductivity if available
        if ((parameters == null || parameters.Contains("Thermal Conductivity")) && ct.ThermalResults != null)
        {
            source.Value = (float)ct.ThermalResults.EffectiveConductivity;
            unit.ParameterSources["Thermal Conductivity"] = source;
            unit.Parameters["Thermal Conductivity"] = source.Value;
        }

        // Import NMR-derived porosity if available
        if ((parameters == null || parameters.Contains("Porosity")) && ct.NmrResults != null)
        {
            // Use T2 distribution to estimate porosity
            var porosity = EstimatePorosityFromNMR(ct.NmrResults);
            source.Value = porosity;
            unit.ParameterSources["Porosity"] = source;
            unit.Parameters["Porosity"] = porosity;
        }
    }

    private void ImportFromPNMDataset(PNMDataset pnm, LithologyUnit unit,
        float depthFrom, float depthTo, string[] parameters)
    {
        var source = new ParameterSource
        {
            DatasetName = pnm.Name,
            DatasetPath = pnm.FilePath,
            DatasetType = pnm.Type,
            SourceDepthFrom = depthFrom,
            SourceDepthTo = depthTo,
            LastUpdated = DateTime.Now
        };

        // Import permeability
        if (parameters == null || parameters.Contains("Permeability"))
        {
            source.Value = pnm.DarcyPermeability;
            unit.ParameterSources["Permeability"] = source;
            unit.Parameters["Permeability"] = source.Value;
        }

        // Import porosity (calculated from pore volume)
        if (parameters == null || parameters.Contains("Porosity"))
        {
            var porosity = CalculatePorosityFromPNM(pnm);
            source.Value = porosity;
            unit.ParameterSources["Porosity"] = source;
            unit.Parameters["Porosity"] = porosity;
        }

        // Import tortuosity
        if (parameters == null || parameters.Contains("Tortuosity"))
        {
            source.Value = pnm.Tortuosity;
            unit.ParameterSources["Tortuosity"] = source;
            unit.Parameters["Tortuosity"] = source.Value;
        }
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

        // Import acoustic velocities
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

        // Import elastic moduli
        if (parameters == null || parameters.Contains("Young's Modulus"))
        {
            source.Value = acoustic.YoungsModulusMPa / 1000.0f; // Convert to GPa
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

    private float EstimatePorosityFromNMR(NMRResults nmr)
    {
        // Simple estimation: integrate T2 distribution
        if (nmr.T2Histogram != null && nmr.T2Histogram.Length > 0)
        {
            var totalSignal = nmr.T2Histogram.Sum();
            // Normalize to percentage (assuming calibration)
            return Math.Min(40, (float)(totalSignal * 0.1)); // Cap at 40% porosity
        }

        return 0;
    }

    private float CalculatePorosityFromPNM(PNMDataset pnm)
    {
        if (pnm.Pores == null || pnm.Pores.Count == 0) return 0;

        // Calculate total pore volume
        var totalPoreVolume = pnm.Pores.Sum(p => p.VolumePhysical);

        // Calculate bulk volume (simplified - using image dimensions)
        var bulkVolume = pnm.ImageWidth * pnm.ImageHeight * pnm.ImageDepth *
                         Math.Pow(pnm.VoxelSize, 3);

        if (bulkVolume > 0)
            return (float)(totalPoreVolume / bulkVolume * 100);

        return 0;
    }

    /// <summary>
    ///     Update parameter track points from lithology units
    /// </summary>
    private void UpdateParameterTracks()
    {
        // Clear existing points
        foreach (var track in ParameterTracks.Values)
            track.Points.Clear();

        // Add points from each lithology unit
        foreach (var unit in LithologyUnits)
        foreach (var param in unit.Parameters)
            if (ParameterTracks.ContainsKey(param.Key))
            {
                var track = ParameterTracks[param.Key];

                // Add points at unit boundaries
                track.Points.Add(new ParameterPoint
                {
                    Depth = unit.DepthFrom,
                    Value = param.Value,
                    SourceDataset = unit.ParameterSources.ContainsKey(param.Key)
                        ? unit.ParameterSources[param.Key].DatasetName
                        : "Manual"
                });

                track.Points.Add(new ParameterPoint
                {
                    Depth = unit.DepthTo,
                    Value = param.Value,
                    SourceDataset = unit.ParameterSources.ContainsKey(param.Key)
                        ? unit.ParameterSources[param.Key].DatasetName
                        : "Manual"
                });
            }

        // Sort points by depth
        foreach (var track in ParameterTracks.Values)
            track.Points.Sort((a, b) => a.Depth.CompareTo(b.Depth));
    }

    /// <summary>
    ///     Get lithology unit at specific depth
    /// </summary>
    public LithologyUnit GetLithologyUnitAtDepth(float depth)
    {
        return LithologyUnits.FirstOrDefault(u => depth >= u.DepthFrom && depth <= u.DepthTo);
    }

    /// <summary>
    ///     Interpolate parameter value at specific depth
    /// </summary>
    public float? GetParameterValueAtDepth(string parameterName, float depth)
    {
        if (!ParameterTracks.ContainsKey(parameterName))
            return null;

        var track = ParameterTracks[parameterName];
        if (track.Points.Count == 0)
            return null;

        // Find surrounding points for interpolation
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

        // Linear interpolation
        var t = (depth - before.Depth) / (after.Depth - before.Depth);
        return before.Value + (after.Value - before.Value) * t;
    }

    public override long GetSizeInBytes()
    {
        // Estimate memory usage
        long size = 0;
        size += LithologyUnits.Count * 500; // Approximate bytes per unit
        size += ParameterTracks.Count * 200; // Approximate bytes per track
        foreach (var track in ParameterTracks.Values)
            size += track.Points.Count * 20; // Approximate bytes per point
        return size;
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

    public void SaveToBinaryFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(stream);

            // Header
            writer.Write(new[] { (byte)'G', (byte)'T', (byte)'B', (byte)'H', (byte)'B' }); // Signature
            writer.Write(1); // Version

            // Well Info
            writer.Write(WellName ?? "");
            writer.Write(Field ?? "");
            writer.Write(TotalDepth);
            writer.Write(WellDiameter);
            writer.Write(SurfaceCoordinates.X);
            writer.Write(SurfaceCoordinates.Y);
            writer.Write(Elevation);

            // Display Settings
            writer.Write(DepthScaleFactor);
            writer.Write(ShowGrid);
            writer.Write(ShowLegend);
            writer.Write(TrackWidth);

            // Lithology Units
            writer.Write(LithologyUnits.Count);
            foreach (var unit in LithologyUnits)
            {
                writer.Write(unit.ID ?? "");
                writer.Write(unit.Name ?? "");
                writer.Write(unit.LithologyType ?? "");
                writer.Write(unit.DepthFrom);
                writer.Write(unit.DepthTo);
                writer.Write(unit.Color.X);
                writer.Write(unit.Color.Y);
                writer.Write(unit.Color.Z);
                writer.Write(unit.Color.W);
                writer.Write(unit.Description ?? "");
                writer.Write(unit.GrainSize ?? "");

                writer.Write(unit.Parameters.Count);
                foreach (var param in unit.Parameters)
                {
                    writer.Write(param.Key);
                    writer.Write(param.Value);
                }

                writer.Write(unit.ParameterSources.Count);
                foreach (var source in unit.ParameterSources)
                {
                    writer.Write(source.Key);
                    writer.Write(source.Value.DatasetName ?? "");
                    writer.Write(source.Value.DatasetPath ?? "");
                    writer.Write((int)source.Value.DatasetType);
                    writer.Write(source.Value.SourceDepthFrom);
                    writer.Write(source.Value.SourceDepthTo);
                    writer.Write(source.Value.Value);
                    writer.Write(source.Value.LastUpdated.ToBinary());
                }
            }

            // Parameter Tracks
            writer.Write(ParameterTracks.Count);
            foreach (var track in ParameterTracks)
            {
                writer.Write(track.Key);
                writer.Write(track.Value.Name ?? "");
                writer.Write(track.Value.Unit ?? "");
                writer.Write(track.Value.MinValue);
                writer.Write(track.Value.MaxValue);
                writer.Write(track.Value.IsLogarithmic);
                writer.Write(track.Value.Color.X);
                writer.Write(track.Value.Color.Y);
                writer.Write(track.Value.Color.Z);
                writer.Write(track.Value.Color.W);
                writer.Write(track.Value.IsVisible);

                writer.Write(track.Value.Points.Count);
                foreach (var point in track.Value.Points)
                {
                    writer.Write(point.Depth);
                    writer.Write(point.Value);
                    writer.Write(point.SourceDataset ?? "");
                }
            }

            FilePath = path;
            Logger.Log($"Saved borehole dataset to binary file {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to save borehole dataset to binary file: {ex.Message}");
        }
    }

    public void LoadFromBinaryFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(stream);

            // Header
            var signature = new string(reader.ReadChars(5));
            if (signature != "GTBHB") throw new InvalidDataException("Invalid borehole binary file signature.");
            var version = reader.ReadInt32();
            if (version != 1) throw new NotSupportedException($"Unsupported borehole binary version: {version}.");

            // Well Info
            WellName = reader.ReadString();
            Field = reader.ReadString();
            TotalDepth = reader.ReadSingle();
            WellDiameter = reader.ReadSingle();
            SurfaceCoordinates = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            Elevation = reader.ReadSingle();

            // Display Settings
            DepthScaleFactor = reader.ReadSingle();
            ShowGrid = reader.ReadBoolean();
            ShowLegend = reader.ReadBoolean();
            TrackWidth = reader.ReadSingle();

            // Lithology Units
            LithologyUnits.Clear();
            var unitCount = reader.ReadInt32();
            for (var i = 0; i < unitCount; i++)
            {
                var unit = new LithologyUnit
                {
                    ID = reader.ReadString(),
                    Name = reader.ReadString(),
                    LithologyType = reader.ReadString(),
                    DepthFrom = reader.ReadSingle(),
                    DepthTo = reader.ReadSingle(),
                    Color = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                        reader.ReadSingle()),
                    Description = reader.ReadString(),
                    GrainSize = reader.ReadString()
                };

                var paramCount = reader.ReadInt32();
                for (var j = 0; j < paramCount; j++)
                    unit.Parameters[reader.ReadString()] = reader.ReadSingle();

                var sourceCount = reader.ReadInt32();
                for (var j = 0; j < sourceCount; j++)
                {
                    var key = reader.ReadString();
                    unit.ParameterSources[key] = new ParameterSource
                    {
                        DatasetName = reader.ReadString(),
                        DatasetPath = reader.ReadString(),
                        DatasetType = (DatasetType)reader.ReadInt32(),
                        SourceDepthFrom = reader.ReadSingle(),
                        SourceDepthTo = reader.ReadSingle(),
                        Value = reader.ReadSingle(),
                        LastUpdated = DateTime.FromBinary(reader.ReadInt64())
                    };
                }

                LithologyUnits.Add(unit);
            }

            // Parameter Tracks
            ParameterTracks.Clear();
            var trackCount = reader.ReadInt32();
            for (var i = 0; i < trackCount; i++)
            {
                var key = reader.ReadString();
                var track = new ParameterTrack
                {
                    Name = reader.ReadString(),
                    Unit = reader.ReadString(),
                    MinValue = reader.ReadSingle(),
                    MaxValue = reader.ReadSingle(),
                    IsLogarithmic = reader.ReadBoolean(),
                    Color = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                        reader.ReadSingle()),
                    IsVisible = reader.ReadBoolean()
                };

                var pointCount = reader.ReadInt32();
                for (var j = 0; j < pointCount; j++)
                    track.Points.Add(new ParameterPoint
                    {
                        Depth = reader.ReadSingle(),
                        Value = reader.ReadSingle(),
                        SourceDataset = reader.ReadString()
                    });
                ParameterTracks[key] = track;
            }

            FilePath = path;
            Logger.Log($"Loaded borehole dataset from binary file {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to load borehole dataset from binary file: {ex.Message}");
            IsMissing = true;
        }
    }

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

    public object ToSerializableObject()
    {
        var metadata = new DatasetMetadataDTO
        {
            SampleName = DatasetMetadata.SampleName,
            LocationName = DatasetMetadata.LocationName,
            Latitude = DatasetMetadata.Latitude,
            Longitude = DatasetMetadata.Longitude,
            CoordinatesX = DatasetMetadata.Coordinates?.X,
            CoordinatesY = DatasetMetadata.Coordinates?.Y,
            Depth = DatasetMetadata.Depth,
            SizeX = DatasetMetadata.Size?.X,
            SizeY = DatasetMetadata.Size?.Y,
            SizeZ = DatasetMetadata.Size?.Z,
            SizeUnit = DatasetMetadata.SizeUnit,
            CollectionDate = DatasetMetadata.CollectionDate,
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

    public void SyncMetadata()
    {
        DatasetMetadata.SampleName = WellName;
        DatasetMetadata.LocationName = Field;
        DatasetMetadata.Coordinates = SurfaceCoordinates;
        DatasetMetadata.Elevation = Elevation;
        DatasetMetadata.Depth = TotalDepth;
    }
}

// DTOs for serialization
public class BoreholeDatasetDTO : DatasetDTO
{
    public string WellName { get; set; }
    public string Field { get; set; }
    public float TotalDepth { get; set; }
    public float WellDiameter { get; set; }
    public Vector2 SurfaceCoordinates { get; set; }
    public float Elevation { get; set; }
    public float DepthScaleFactor { get; set; }
    public bool ShowGrid { get; set; }
    public bool ShowLegend { get; set; }
    public float TrackWidth { get; set; }
    public List<LithologyUnitDTO> LithologyUnits { get; set; }
    public Dictionary<string, ParameterTrackDTO> ParameterTracks { get; set; }
}

public class LithologyUnitDTO
{
    public string ID { get; set; }
    public string Name { get; set; }
    public string LithologyType { get; set; }
    public float DepthFrom { get; set; }
    public float DepthTo { get; set; }
    public Vector4 Color { get; set; }
    public string Description { get; set; }
    public string GrainSize { get; set; }
    public Dictionary<string, float> Parameters { get; set; }
    public Dictionary<string, ParameterSource> ParameterSources { get; set; }
}

public class ParameterTrackDTO
{
    public string Name { get; set; }
    public string Unit { get; set; }
    public float MinValue { get; set; }
    public float MaxValue { get; set; }
    public bool IsLogarithmic { get; set; }
    public Vector4 Color { get; set; }
    public bool IsVisible { get; set; }
    public List<ParameterPoint> Points { get; set; }
}