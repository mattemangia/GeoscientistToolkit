// GeoscientistToolkit/Data/Borehole/BoreholeLogCorrelation.cs

using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Borehole;

/// <summary>
/// Represents a correlation between lithology units in adjacent boreholes.
/// Each lithology can only be correlated with one unit in the previous log and one in the next log.
/// </summary>
public class LithologyCorrelation
{
    /// <summary>Unique identifier for this correlation</summary>
    public string ID { get; set; } = Guid.NewGuid().ToString();

    /// <summary>ID of the source lithology unit</summary>
    public string SourceLithologyID { get; set; }

    /// <summary>ID of the source borehole</summary>
    public string SourceBoreholeID { get; set; }

    /// <summary>ID of the target lithology unit</summary>
    public string TargetLithologyID { get; set; }

    /// <summary>ID of the target borehole</summary>
    public string TargetBoreholeID { get; set; }

    /// <summary>Confidence level of the correlation (0.0 - 1.0)</summary>
    public float Confidence { get; set; } = 1.0f;

    /// <summary>Whether this correlation was auto-generated</summary>
    public bool IsAutoCorrelated { get; set; }

    /// <summary>Color for rendering the correlation line</summary>
    public Vector4 Color { get; set; } = new(0.3f, 0.5f, 0.8f, 0.8f);

    /// <summary>Notes about this correlation</summary>
    public string Notes { get; set; } = "";

    /// <summary>Creation timestamp</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// Header information for a borehole in the correlation view
/// </summary>
public class BoreholeHeader
{
    /// <summary>Borehole dataset reference ID</summary>
    public string BoreholeID { get; set; }

    /// <summary>Display name for the header</summary>
    public string DisplayName { get; set; }

    /// <summary>Surface coordinates (X, Y)</summary>
    public Vector2 Coordinates { get; set; }

    /// <summary>Elevation in meters above sea level</summary>
    public float Elevation { get; set; }

    /// <summary>Total depth of the borehole</summary>
    public float TotalDepth { get; set; }

    /// <summary>Position index in the correlation panel (left to right)</summary>
    public int PositionIndex { get; set; }

    /// <summary>Field or project name</summary>
    public string Field { get; set; }

    /// <summary>Optional custom label</summary>
    public string CustomLabel { get; set; }
}

/// <summary>
/// Represents a correlated stratigraphic horizon across multiple boreholes
/// </summary>
public class CorrelatedHorizon
{
    /// <summary>Unique identifier for this horizon</summary>
    public string ID { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Name of the horizon</summary>
    public string Name { get; set; }

    /// <summary>Lithology type of the horizon</summary>
    public string LithologyType { get; set; }

    /// <summary>Color for rendering</summary>
    public Vector4 Color { get; set; }

    /// <summary>List of lithology unit IDs that belong to this horizon, keyed by borehole ID</summary>
    public Dictionary<string, string> LithologyUnits { get; set; } = new();

    /// <summary>Interpolated depth surface (for 3D visualization)</summary>
    [JsonIgnore]
    public List<(Vector3 Position, float Depth)> InterpolatedSurface { get; set; } = new();
}

/// <summary>
/// Main dataset for borehole log correlations
/// </summary>
public class BoreholeLogCorrelationDataset : Dataset, ISerializableDataset
{
    public BoreholeLogCorrelationDataset(string name, string filePath) : base(name, filePath)
    {
        Type = DatasetType.SubsurfaceGIS; // Reuse existing type for subsurface data
    }

    /// <summary>List of boreholes included in this correlation (in display order)</summary>
    public List<string> BoreholeOrder { get; set; } = new();

    /// <summary>Headers for each borehole</summary>
    public Dictionary<string, BoreholeHeader> Headers { get; set; } = new();

    /// <summary>All correlations between lithology units</summary>
    public List<LithologyCorrelation> Correlations { get; set; } = new();

    /// <summary>Named horizons grouping correlated units</summary>
    public List<CorrelatedHorizon> Horizons { get; set; } = new();

    /// <summary>Description of the correlation project</summary>
    public string Description { get; set; } = "";

    /// <summary>Author of the correlation</summary>
    public string Author { get; set; } = "";

    /// <summary>Creation date</summary>
    public DateTime CreatedDate { get; set; } = DateTime.Now;

    /// <summary>Last modified date</summary>
    public DateTime ModifiedDate { get; set; } = DateTime.Now;

    /// <summary>Display settings</summary>
    public CorrelationDisplaySettings DisplaySettings { get; set; } = new();

    /// <summary>
    /// Get all correlations for a specific lithology unit
    /// </summary>
    public List<LithologyCorrelation> GetCorrelationsForUnit(string lithologyID)
    {
        return Correlations.Where(c =>
            c.SourceLithologyID == lithologyID || c.TargetLithologyID == lithologyID).ToList();
    }

    /// <summary>
    /// Check if a lithology unit can be correlated with another unit
    /// (respects the constraint: max one correlation per direction)
    /// </summary>
    public bool CanCorrelate(string sourceLithologyID, string sourceBoreholeID,
        string targetLithologyID, string targetBoreholeID)
    {
        var sourceIndex = BoreholeOrder.IndexOf(sourceBoreholeID);
        var targetIndex = BoreholeOrder.IndexOf(targetBoreholeID);

        if (sourceIndex == -1 || targetIndex == -1)
            return false;

        // Can only correlate with adjacent boreholes
        if (Math.Abs(sourceIndex - targetIndex) != 1)
            return false;

        // Check if source already has correlation in this direction
        var isTargetToRight = targetIndex > sourceIndex;
        var existingCorrelations = GetCorrelationsForUnit(sourceLithologyID);

        foreach (var corr in existingCorrelations)
        {
            string otherBoreholeID = corr.SourceLithologyID == sourceLithologyID
                ? corr.TargetBoreholeID
                : corr.SourceBoreholeID;

            var otherIndex = BoreholeOrder.IndexOf(otherBoreholeID);
            var isOtherToRight = otherIndex > sourceIndex;

            if (isOtherToRight == isTargetToRight)
                return false; // Already has correlation in this direction
        }

        // Check if target already has correlation in the opposite direction
        var existingTargetCorrelations = GetCorrelationsForUnit(targetLithologyID);
        foreach (var corr in existingTargetCorrelations)
        {
            string otherBoreholeID = corr.SourceLithologyID == targetLithologyID
                ? corr.TargetBoreholeID
                : corr.SourceBoreholeID;

            var otherIndex = BoreholeOrder.IndexOf(otherBoreholeID);
            var isOtherToLeft = otherIndex < targetIndex;

            if (isOtherToLeft == !isTargetToRight)
                return false; // Target already has correlation from source direction
        }

        return true;
    }

    /// <summary>
    /// Add a correlation between two lithology units
    /// </summary>
    public bool AddCorrelation(string sourceLithologyID, string sourceBoreholeID,
        string targetLithologyID, string targetBoreholeID,
        float confidence = 1.0f, bool isAuto = false)
    {
        if (!CanCorrelate(sourceLithologyID, sourceBoreholeID, targetLithologyID, targetBoreholeID))
        {
            Logger.LogWarning($"Cannot add correlation: constraints not satisfied");
            return false;
        }

        var correlation = new LithologyCorrelation
        {
            SourceLithologyID = sourceLithologyID,
            SourceBoreholeID = sourceBoreholeID,
            TargetLithologyID = targetLithologyID,
            TargetBoreholeID = targetBoreholeID,
            Confidence = confidence,
            IsAutoCorrelated = isAuto
        };

        Correlations.Add(correlation);
        ModifiedDate = DateTime.Now;

        Logger.Log($"Added correlation between {sourceLithologyID} and {targetLithologyID}");
        return true;
    }

    /// <summary>
    /// Remove a correlation
    /// </summary>
    public void RemoveCorrelation(string correlationID)
    {
        var toRemove = Correlations.FirstOrDefault(c => c.ID == correlationID);
        if (toRemove != null)
        {
            Correlations.Remove(toRemove);
            ModifiedDate = DateTime.Now;
            Logger.Log($"Removed correlation {correlationID}");
        }
    }

    /// <summary>
    /// Remove all correlations for a lithology unit
    /// </summary>
    public void RemoveCorrelationsForUnit(string lithologyID)
    {
        var toRemove = Correlations.Where(c =>
            c.SourceLithologyID == lithologyID || c.TargetLithologyID == lithologyID).ToList();

        foreach (var corr in toRemove)
            Correlations.Remove(corr);

        if (toRemove.Count > 0)
        {
            ModifiedDate = DateTime.Now;
            Logger.Log($"Removed {toRemove.Count} correlations for unit {lithologyID}");
        }
    }

    /// <summary>
    /// Build horizons from correlations
    /// </summary>
    public void BuildHorizonsFromCorrelations(Dictionary<string, BoreholeDataset> boreholes)
    {
        Horizons.Clear();
        var visited = new HashSet<string>();

        foreach (var correlation in Correlations)
        {
            if (visited.Contains(correlation.SourceLithologyID) &&
                visited.Contains(correlation.TargetLithologyID))
                continue;

            // Find or create horizon for this correlation chain
            var horizon = FindOrCreateHorizon(correlation, boreholes, visited);
            if (horizon != null && !Horizons.Contains(horizon))
                Horizons.Add(horizon);
        }

        ModifiedDate = DateTime.Now;
    }

    private CorrelatedHorizon FindOrCreateHorizon(LithologyCorrelation startCorrelation,
        Dictionary<string, BoreholeDataset> boreholes, HashSet<string> visited)
    {
        var horizon = new CorrelatedHorizon();
        var queue = new Queue<(string lithologyID, string boreholeID)>();

        queue.Enqueue((startCorrelation.SourceLithologyID, startCorrelation.SourceBoreholeID));
        queue.Enqueue((startCorrelation.TargetLithologyID, startCorrelation.TargetBoreholeID));

        while (queue.Count > 0)
        {
            var (lithologyID, boreholeID) = queue.Dequeue();

            if (visited.Contains(lithologyID))
                continue;

            visited.Add(lithologyID);
            horizon.LithologyUnits[boreholeID] = lithologyID;

            // Set horizon properties from first unit
            if (string.IsNullOrEmpty(horizon.Name) && boreholes.TryGetValue(boreholeID, out var borehole))
            {
                var unit = borehole.LithologyUnits.FirstOrDefault(u => u.ID == lithologyID);
                if (unit != null)
                {
                    horizon.Name = unit.Name;
                    horizon.LithologyType = unit.LithologyType;
                    horizon.Color = unit.Color;
                }
            }

            // Find connected units
            var related = Correlations.Where(c =>
                c.SourceLithologyID == lithologyID || c.TargetLithologyID == lithologyID);

            foreach (var corr in related)
            {
                if (corr.SourceLithologyID == lithologyID && !visited.Contains(corr.TargetLithologyID))
                    queue.Enqueue((corr.TargetLithologyID, corr.TargetBoreholeID));
                else if (corr.TargetLithologyID == lithologyID && !visited.Contains(corr.SourceLithologyID))
                    queue.Enqueue((corr.SourceLithologyID, corr.SourceBoreholeID));
            }
        }

        return horizon.LithologyUnits.Count > 0 ? horizon : null;
    }

    public override long GetSizeInBytes()
    {
        return Correlations.Count * 200 + Headers.Count * 100 + Horizons.Count * 500;
    }

    public override void Load()
    {
        if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
        {
            IsMissing = true;
            return;
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            var dto = JsonSerializer.Deserialize<BoreholeLogCorrelationDTO>(json);
            if (dto != null)
                LoadFromDTO(dto);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to load correlation dataset: {ex.Message}");
            IsMissing = true;
        }
    }

    public override void Unload()
    {
        Correlations.Clear();
        Headers.Clear();
        Horizons.Clear();
    }

    public void SaveToFile(string path)
    {
        try
        {
            var dto = ToSerializableObject() as BoreholeLogCorrelationDTO;
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(dto, options);
            File.WriteAllText(path, json);
            FilePath = path;
            Logger.Log($"Saved correlation dataset to {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to save correlation dataset: {ex.Message}");
        }
    }

    public object ToSerializableObject()
    {
        return new BoreholeLogCorrelationDTO
        {
            TypeName = "BoreholeLogCorrelation",
            Name = Name,
            FilePath = FilePath,
            BoreholeOrder = BoreholeOrder,
            Headers = Headers.ToDictionary(kvp => kvp.Key, kvp => new BoreholeHeaderDTO
            {
                BoreholeID = kvp.Value.BoreholeID,
                DisplayName = kvp.Value.DisplayName,
                CoordinatesX = kvp.Value.Coordinates.X,
                CoordinatesY = kvp.Value.Coordinates.Y,
                Elevation = kvp.Value.Elevation,
                TotalDepth = kvp.Value.TotalDepth,
                PositionIndex = kvp.Value.PositionIndex,
                Field = kvp.Value.Field,
                CustomLabel = kvp.Value.CustomLabel
            }),
            Correlations = Correlations.Select(c => new LithologyCorrelationDTO
            {
                ID = c.ID,
                SourceLithologyID = c.SourceLithologyID,
                SourceBoreholeID = c.SourceBoreholeID,
                TargetLithologyID = c.TargetLithologyID,
                TargetBoreholeID = c.TargetBoreholeID,
                Confidence = c.Confidence,
                IsAutoCorrelated = c.IsAutoCorrelated,
                ColorR = c.Color.X,
                ColorG = c.Color.Y,
                ColorB = c.Color.Z,
                ColorA = c.Color.W,
                Notes = c.Notes,
                CreatedAt = c.CreatedAt
            }).ToList(),
            Horizons = Horizons.Select(h => new CorrelatedHorizonDTO
            {
                ID = h.ID,
                Name = h.Name,
                LithologyType = h.LithologyType,
                ColorR = h.Color.X,
                ColorG = h.Color.Y,
                ColorB = h.Color.Z,
                ColorA = h.Color.W,
                LithologyUnits = h.LithologyUnits
            }).ToList(),
            Description = Description,
            Author = Author,
            CreatedDate = CreatedDate,
            ModifiedDate = ModifiedDate,
            DisplaySettings = DisplaySettings
        };
    }

    private void LoadFromDTO(BoreholeLogCorrelationDTO dto)
    {
        BoreholeOrder = dto.BoreholeOrder ?? new List<string>();
        Description = dto.Description;
        Author = dto.Author;
        CreatedDate = dto.CreatedDate;
        ModifiedDate = dto.ModifiedDate;
        DisplaySettings = dto.DisplaySettings ?? new CorrelationDisplaySettings();

        Headers.Clear();
        if (dto.Headers != null)
        {
            foreach (var kvp in dto.Headers)
            {
                Headers[kvp.Key] = new BoreholeHeader
                {
                    BoreholeID = kvp.Value.BoreholeID,
                    DisplayName = kvp.Value.DisplayName,
                    Coordinates = new Vector2(kvp.Value.CoordinatesX, kvp.Value.CoordinatesY),
                    Elevation = kvp.Value.Elevation,
                    TotalDepth = kvp.Value.TotalDepth,
                    PositionIndex = kvp.Value.PositionIndex,
                    Field = kvp.Value.Field,
                    CustomLabel = kvp.Value.CustomLabel
                };
            }
        }

        Correlations.Clear();
        if (dto.Correlations != null)
        {
            foreach (var c in dto.Correlations)
            {
                Correlations.Add(new LithologyCorrelation
                {
                    ID = c.ID,
                    SourceLithologyID = c.SourceLithologyID,
                    SourceBoreholeID = c.SourceBoreholeID,
                    TargetLithologyID = c.TargetLithologyID,
                    TargetBoreholeID = c.TargetBoreholeID,
                    Confidence = c.Confidence,
                    IsAutoCorrelated = c.IsAutoCorrelated,
                    Color = new Vector4(c.ColorR, c.ColorG, c.ColorB, c.ColorA),
                    Notes = c.Notes,
                    CreatedAt = c.CreatedAt
                });
            }
        }

        Horizons.Clear();
        if (dto.Horizons != null)
        {
            foreach (var h in dto.Horizons)
            {
                Horizons.Add(new CorrelatedHorizon
                {
                    ID = h.ID,
                    Name = h.Name,
                    LithologyType = h.LithologyType,
                    Color = new Vector4(h.ColorR, h.ColorG, h.ColorB, h.ColorA),
                    LithologyUnits = h.LithologyUnits ?? new Dictionary<string, string>()
                });
            }
        }
    }
}

/// <summary>
/// Display settings for the correlation viewer
/// </summary>
public class CorrelationDisplaySettings
{
    public float ColumnWidth { get; set; } = 120f;
    public float ColumnSpacing { get; set; } = 60f;
    public float DepthScale { get; set; } = 3f; // pixels per meter
    public float HeaderHeight { get; set; } = 80f;
    public bool ShowCorrelationLines { get; set; } = true;
    public bool ShowLithologyNames { get; set; } = true;
    public bool ShowDepthScale { get; set; } = true;
    public bool ShowCoordinates { get; set; } = true;
    public bool AlignToZero { get; set; } = true;
    public float LineThickness { get; set; } = 2f;
}

#region DTOs for serialization

public class BoreholeLogCorrelationDTO : DatasetDTO
{
    public List<string> BoreholeOrder { get; set; }
    public Dictionary<string, BoreholeHeaderDTO> Headers { get; set; }
    public List<LithologyCorrelationDTO> Correlations { get; set; }
    public List<CorrelatedHorizonDTO> Horizons { get; set; }
    public string Description { get; set; }
    public string Author { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime ModifiedDate { get; set; }
    public CorrelationDisplaySettings DisplaySettings { get; set; }
}

public class BoreholeHeaderDTO
{
    public string BoreholeID { get; set; }
    public string DisplayName { get; set; }
    public float CoordinatesX { get; set; }
    public float CoordinatesY { get; set; }
    public float Elevation { get; set; }
    public float TotalDepth { get; set; }
    public int PositionIndex { get; set; }
    public string Field { get; set; }
    public string CustomLabel { get; set; }
}

public class LithologyCorrelationDTO
{
    public string ID { get; set; }
    public string SourceLithologyID { get; set; }
    public string SourceBoreholeID { get; set; }
    public string TargetLithologyID { get; set; }
    public string TargetBoreholeID { get; set; }
    public float Confidence { get; set; }
    public bool IsAutoCorrelated { get; set; }
    public float ColorR { get; set; }
    public float ColorG { get; set; }
    public float ColorB { get; set; }
    public float ColorA { get; set; }
    public string Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CorrelatedHorizonDTO
{
    public string ID { get; set; }
    public string Name { get; set; }
    public string LithologyType { get; set; }
    public float ColorR { get; set; }
    public float ColorG { get; set; }
    public float ColorB { get; set; }
    public float ColorA { get; set; }
    public Dictionary<string, string> LithologyUnits { get; set; }
}

#endregion
