// GeoscientistToolkit/Data/Seismic/SeismicCube.cs

using System.Numerics;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Seismic;

/// <summary>
/// Represents a 3D seismic cube constructed from multiple intersecting 2D seismic lines.
/// Supports perpendicular and oblique line intersections with amplitude normalization.
/// </summary>
public class SeismicCubeDataset : Dataset, ISerializableDataset
{
    /// <summary>
    /// Collection of seismic lines that form this cube
    /// </summary>
    public List<SeismicCubeLine> Lines { get; set; } = new();

    /// <summary>
    /// Detected intersections between lines
    /// </summary>
    public List<LineIntersection> Intersections { get; set; } = new();

    /// <summary>
    /// Segmented seismic packages/horizons across the cube
    /// </summary>
    public List<SeismicCubePackage> Packages { get; set; } = new();

    /// <summary>
    /// 3D bounding box of the cube in world coordinates
    /// </summary>
    public CubeBounds Bounds { get; set; } = new();

    /// <summary>
    /// Grid parameters for the regularized cube
    /// </summary>
    public CubeGridParameters GridParameters { get; set; } = new();

    /// <summary>
    /// Normalization settings applied to the cube
    /// </summary>
    public CubeNormalizationSettings NormalizationSettings { get; set; } = new();

    /// <summary>
    /// Regularized 3D amplitude volume (null until BuildRegularizedVolume is called)
    /// </summary>
    public float[,,]? RegularizedVolume { get; private set; }

    /// <summary>
    /// Survey metadata
    /// </summary>
    public string SurveyName { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public DateTime CreationDate { get; set; } = DateTime.Now;

    public SeismicCubeDataset(string name, string filePath) : base(name, filePath)
    {
        Type = DatasetType.Seismic;
    }

    public override long GetSizeInBytes()
    {
        long totalSize = 0;
        foreach (var line in Lines)
        {
            if (line.SeismicData?.SegyData != null)
            {
                totalSize += line.SeismicData.GetSizeInBytes();
            }
        }

        if (RegularizedVolume != null)
        {
            totalSize += RegularizedVolume.Length * sizeof(float);
        }

        return totalSize;
    }

    public override void Load()
    {
        Logger.Log($"[SeismicCube] Load() called for {Name}");
    }

    public override void Unload()
    {
        foreach (var line in Lines)
        {
            line.SeismicData?.Unload();
        }
        Lines.Clear();
        Intersections.Clear();
        Packages.Clear();
        RegularizedVolume = null;
        Logger.Log($"[SeismicCube] Unloaded {Name}");
    }

    /// <summary>
    /// Add a seismic line to the cube
    /// </summary>
    public void AddLine(SeismicDataset seismicData, LineGeometry geometry)
    {
        var line = new SeismicCubeLine
        {
            Id = Guid.NewGuid().ToString(),
            Name = seismicData.Name,
            SeismicData = seismicData,
            Geometry = geometry,
            IsVisible = true
        };

        Lines.Add(line);
        UpdateBounds();
        DetectIntersections();

        Logger.Log($"[SeismicCube] Added line '{line.Name}' to cube. Total lines: {Lines.Count}");
    }

    /// <summary>
    /// Add a perpendicular line at a specific point on an existing line
    /// </summary>
    public void AddPerpendicularLine(SeismicDataset seismicData, string baseLineId, int traceIndex)
    {
        var baseLine = Lines.FirstOrDefault(l => l.Id == baseLineId);
        if (baseLine == null)
        {
            Logger.LogError($"[SeismicCube] Base line '{baseLineId}' not found");
            return;
        }

        // Calculate perpendicular geometry at the specified trace
        var baseGeometry = baseLine.Geometry;
        var intersectionPoint = baseGeometry.GetPositionAtTrace(traceIndex);
        var baseDirection = baseGeometry.GetDirectionAtTrace(traceIndex);

        // Perpendicular direction (rotate 90 degrees in XY plane)
        var perpDirection = new Vector3(-baseDirection.Y, baseDirection.X, 0);
        perpDirection = Vector3.Normalize(perpDirection);

        // Calculate line extent based on new seismic data
        int newTraceCount = seismicData.GetTraceCount();
        float traceSpacing = baseGeometry.TraceSpacing;
        float halfLength = (newTraceCount * traceSpacing) / 2.0f;

        var perpGeometry = new LineGeometry
        {
            StartPoint = intersectionPoint - perpDirection * halfLength,
            EndPoint = intersectionPoint + perpDirection * halfLength,
            TraceSpacing = traceSpacing,
            Azimuth = baseGeometry.Azimuth + 90f
        };

        var line = new SeismicCubeLine
        {
            Id = Guid.NewGuid().ToString(),
            Name = seismicData.Name,
            SeismicData = seismicData,
            Geometry = perpGeometry,
            IsVisible = true,
            IsPerpendicular = true,
            BaseLineId = baseLineId,
            BaseTraceIndex = traceIndex
        };

        Lines.Add(line);
        UpdateBounds();
        DetectIntersections();

        Logger.Log($"[SeismicCube] Added perpendicular line '{line.Name}' at trace {traceIndex} of '{baseLine.Name}'");
    }

    /// <summary>
    /// Add a line passing through a specific point with a given azimuth
    /// </summary>
    public void AddLineAtPoint(SeismicDataset seismicData, Vector3 intersectionPoint, float azimuth)
    {
        int traceCount = seismicData.GetTraceCount();
        float traceSpacing = 12.5f; // Default trace spacing in meters

        // Calculate direction from azimuth
        float azimuthRad = azimuth * MathF.PI / 180f;
        var direction = new Vector3(MathF.Sin(azimuthRad), MathF.Cos(azimuthRad), 0);

        float halfLength = (traceCount * traceSpacing) / 2.0f;

        var geometry = new LineGeometry
        {
            StartPoint = intersectionPoint - direction * halfLength,
            EndPoint = intersectionPoint + direction * halfLength,
            TraceSpacing = traceSpacing,
            Azimuth = azimuth
        };

        var line = new SeismicCubeLine
        {
            Id = Guid.NewGuid().ToString(),
            Name = seismicData.Name,
            SeismicData = seismicData,
            Geometry = geometry,
            IsVisible = true
        };

        Lines.Add(line);
        UpdateBounds();
        DetectIntersections();

        Logger.Log($"[SeismicCube] Added line '{line.Name}' at point ({intersectionPoint.X:F1}, {intersectionPoint.Y:F1}) with azimuth {azimuth}");
    }

    /// <summary>
    /// Remove a line from the cube
    /// </summary>
    public void RemoveLine(string lineId)
    {
        var line = Lines.FirstOrDefault(l => l.Id == lineId);
        if (line != null)
        {
            Lines.Remove(line);
            UpdateBounds();
            DetectIntersections();
            Logger.Log($"[SeismicCube] Removed line '{line.Name}' from cube");
        }
    }

    /// <summary>
    /// Detect all intersections between lines in the cube
    /// </summary>
    public void DetectIntersections()
    {
        Intersections.Clear();

        for (int i = 0; i < Lines.Count; i++)
        {
            for (int j = i + 1; j < Lines.Count; j++)
            {
                var intersection = FindIntersection(Lines[i], Lines[j]);
                if (intersection != null)
                {
                    Intersections.Add(intersection);
                }
            }
        }

        Logger.Log($"[SeismicCube] Detected {Intersections.Count} line intersections");
    }

    /// <summary>
    /// Find intersection between two lines
    /// </summary>
    private LineIntersection? FindIntersection(SeismicCubeLine line1, SeismicCubeLine line2)
    {
        var g1 = line1.Geometry;
        var g2 = line2.Geometry;

        // 2D line intersection (in XY plane)
        var p1 = new Vector2(g1.StartPoint.X, g1.StartPoint.Y);
        var d1 = new Vector2(g1.EndPoint.X - g1.StartPoint.X, g1.EndPoint.Y - g1.StartPoint.Y);
        var p2 = new Vector2(g2.StartPoint.X, g2.StartPoint.Y);
        var d2 = new Vector2(g2.EndPoint.X - g2.StartPoint.X, g2.EndPoint.Y - g2.StartPoint.Y);

        float cross = d1.X * d2.Y - d1.Y * d2.X;
        if (Math.Abs(cross) < 1e-6f)
        {
            // Lines are parallel
            return null;
        }

        var diff = p2 - p1;
        float t1 = (diff.X * d2.Y - diff.Y * d2.X) / cross;
        float t2 = (diff.X * d1.Y - diff.Y * d1.X) / cross;

        // Check if intersection is within both line segments
        if (t1 < 0 || t1 > 1 || t2 < 0 || t2 > 1)
        {
            return null;
        }

        // Calculate intersection point
        var intersectionPoint = new Vector3(
            p1.X + t1 * d1.X,
            p1.Y + t1 * d1.Y,
            0
        );

        // Calculate trace indices at intersection
        int traceIndex1 = (int)(t1 * line1.SeismicData.GetTraceCount());
        int traceIndex2 = (int)(t2 * line2.SeismicData.GetTraceCount());

        // Calculate intersection angle
        float angle = CalculateIntersectionAngle(d1, d2);

        return new LineIntersection
        {
            Id = Guid.NewGuid().ToString(),
            Line1Id = line1.Id,
            Line2Id = line2.Id,
            Line1Name = line1.Name,
            Line2Name = line2.Name,
            IntersectionPoint = intersectionPoint,
            Line1TraceIndex = traceIndex1,
            Line2TraceIndex = traceIndex2,
            IntersectionAngle = angle,
            IsPerpendicular = Math.Abs(angle - 90f) < 5f, // Within 5 degrees of perpendicular
            NormalizationApplied = false
        };
    }

    /// <summary>
    /// Calculate angle between two direction vectors
    /// </summary>
    private float CalculateIntersectionAngle(Vector2 d1, Vector2 d2)
    {
        d1 = Vector2.Normalize(d1);
        d2 = Vector2.Normalize(d2);
        float dot = Vector2.Dot(d1, d2);
        float angle = MathF.Acos(Math.Clamp(dot, -1f, 1f)) * 180f / MathF.PI;
        return Math.Min(angle, 180f - angle); // Return acute angle
    }

    /// <summary>
    /// Update cube bounds based on all lines
    /// </summary>
    private void UpdateBounds()
    {
        if (Lines.Count == 0)
        {
            Bounds = new CubeBounds();
            return;
        }

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        float minZ = 0, maxZ = 0;

        foreach (var line in Lines)
        {
            minX = Math.Min(minX, Math.Min(line.Geometry.StartPoint.X, line.Geometry.EndPoint.X));
            maxX = Math.Max(maxX, Math.Max(line.Geometry.StartPoint.X, line.Geometry.EndPoint.X));
            minY = Math.Min(minY, Math.Min(line.Geometry.StartPoint.Y, line.Geometry.EndPoint.Y));
            maxY = Math.Max(maxY, Math.Max(line.Geometry.StartPoint.Y, line.Geometry.EndPoint.Y));

            // Z extent from seismic data (time/depth)
            if (line.SeismicData != null)
            {
                maxZ = Math.Max(maxZ, line.SeismicData.GetDurationSeconds() * 1000f); // Convert to ms
            }
        }

        Bounds = new CubeBounds
        {
            MinX = minX,
            MaxX = maxX,
            MinY = minY,
            MaxY = maxY,
            MinZ = minZ,
            MaxZ = maxZ
        };

        Logger.Log($"[SeismicCube] Updated bounds: X[{minX:F1},{maxX:F1}] Y[{minY:F1},{maxY:F1}] Z[{minZ:F1},{maxZ:F1}]");
    }

    /// <summary>
    /// Apply normalization to match lines at intersections
    /// </summary>
    public void ApplyNormalization()
    {
        var normalizer = new SeismicLineNormalizer(NormalizationSettings);

        foreach (var intersection in Intersections)
        {
            var line1 = Lines.FirstOrDefault(l => l.Id == intersection.Line1Id);
            var line2 = Lines.FirstOrDefault(l => l.Id == intersection.Line2Id);

            if (line1?.SeismicData != null && line2?.SeismicData != null)
            {
                normalizer.NormalizeAtIntersection(
                    line1.SeismicData,
                    line2.SeismicData,
                    intersection.Line1TraceIndex,
                    intersection.Line2TraceIndex
                );
                intersection.NormalizationApplied = true;

                Logger.Log($"[SeismicCube] Normalized intersection between '{line1.Name}' and '{line2.Name}'");
            }
        }
    }

    /// <summary>
    /// Build a regularized 3D volume from the seismic lines
    /// </summary>
    public void BuildRegularizedVolume()
    {
        if (Lines.Count == 0)
        {
            Logger.LogWarning("[SeismicCube] Cannot build volume: no lines in cube");
            return;
        }

        var builder = new SeismicCubeVolumeBuilder(this);
        RegularizedVolume = builder.BuildVolume();

        Logger.Log($"[SeismicCube] Built regularized volume: {GridParameters.InlineCount}x{GridParameters.CrosslineCount}x{GridParameters.SampleCount}");
    }

    /// <summary>
    /// Get amplitude at a specific location in the cube
    /// </summary>
    public float? GetAmplitudeAt(float x, float y, float z)
    {
        if (RegularizedVolume == null)
        {
            return null;
        }

        // Convert world coordinates to grid indices
        int i = (int)((x - Bounds.MinX) / GridParameters.InlineSpacing);
        int j = (int)((y - Bounds.MinY) / GridParameters.CrosslineSpacing);
        int k = (int)(z / GridParameters.SampleInterval);

        if (i < 0 || i >= GridParameters.InlineCount ||
            j < 0 || j >= GridParameters.CrosslineCount ||
            k < 0 || k >= GridParameters.SampleCount)
        {
            return null;
        }

        return RegularizedVolume[i, j, k];
    }

    /// <summary>
    /// Extract a time slice from the cube
    /// </summary>
    public float[,]? GetTimeSlice(float timeMs)
    {
        if (RegularizedVolume == null)
        {
            return null;
        }

        int k = (int)(timeMs / GridParameters.SampleInterval);
        if (k < 0 || k >= GridParameters.SampleCount)
        {
            return null;
        }

        var slice = new float[GridParameters.InlineCount, GridParameters.CrosslineCount];
        for (int i = 0; i < GridParameters.InlineCount; i++)
        {
            for (int j = 0; j < GridParameters.CrosslineCount; j++)
            {
                slice[i, j] = RegularizedVolume[i, j, k];
            }
        }

        return slice;
    }

    /// <summary>
    /// Extract an inline section from the cube
    /// </summary>
    public float[,]? GetInlineSection(int inlineIndex)
    {
        if (RegularizedVolume == null || inlineIndex < 0 || inlineIndex >= GridParameters.InlineCount)
        {
            return null;
        }

        var section = new float[GridParameters.CrosslineCount, GridParameters.SampleCount];
        for (int j = 0; j < GridParameters.CrosslineCount; j++)
        {
            for (int k = 0; k < GridParameters.SampleCount; k++)
            {
                section[j, k] = RegularizedVolume[inlineIndex, j, k];
            }
        }

        return section;
    }

    /// <summary>
    /// Extract a crossline section from the cube
    /// </summary>
    public float[,]? GetCrosslineSection(int crosslineIndex)
    {
        if (RegularizedVolume == null || crosslineIndex < 0 || crosslineIndex >= GridParameters.CrosslineCount)
        {
            return null;
        }

        var section = new float[GridParameters.InlineCount, GridParameters.SampleCount];
        for (int i = 0; i < GridParameters.InlineCount; i++)
        {
            for (int k = 0; k < GridParameters.SampleCount; k++)
            {
                section[i, k] = RegularizedVolume[i, crosslineIndex, k];
            }
        }

        return section;
    }

    /// <summary>
    /// Add a seismic package (horizon/unit) to the cube
    /// </summary>
    public void AddPackage(SeismicCubePackage package)
    {
        Packages.Add(package);
        Logger.Log($"[SeismicCube] Added package '{package.Name}' with {package.HorizonPoints.Count} horizon points");
    }

    /// <summary>
    /// Get statistics for the cube
    /// </summary>
    public CubeStatistics GetStatistics()
    {
        return new CubeStatistics
        {
            LineCount = Lines.Count,
            IntersectionCount = Intersections.Count,
            PackageCount = Packages.Count,
            TotalTraces = Lines.Sum(l => l.SeismicData?.GetTraceCount() ?? 0),
            HasRegularizedVolume = RegularizedVolume != null,
            Bounds = Bounds,
            GridParameters = GridParameters
        };
    }

    /// <summary>
    /// Implements ISerializableDataset
    /// </summary>
    public object ToSerializableObject() => ToDTO();

    /// <summary>
    /// Convert to DTO for serialization
    /// </summary>
    public SeismicCubeDatasetDTO ToDTO()
    {
        var dto = new SeismicCubeDatasetDTO
        {
            TypeName = nameof(SeismicCubeDataset),
            Name = Name,
            FilePath = FilePath,
            SurveyName = SurveyName,
            ProjectName = ProjectName,
            CreationDate = CreationDate,
            Metadata = new DatasetMetadataDTO
            {
                SampleName = DatasetMetadata.SampleName,
                LocationName = DatasetMetadata.LocationName,
                Latitude = DatasetMetadata.Latitude,
                Longitude = DatasetMetadata.Longitude,
                Notes = DatasetMetadata.Notes
            }
        };

        // Convert lines
        dto.Lines = Lines.Select(l => new SeismicCubeLineDTO
        {
            Id = l.Id,
            Name = l.Name,
            SeismicDataFilePath = l.SeismicData?.FilePath ?? "",
            Geometry = new LineGeometryDTO
            {
                StartPoint = l.Geometry.StartPoint,
                EndPoint = l.Geometry.EndPoint,
                TraceSpacing = l.Geometry.TraceSpacing,
                Azimuth = l.Geometry.Azimuth
            },
            IsVisible = l.IsVisible,
            IsPerpendicular = l.IsPerpendicular,
            BaseLineId = l.BaseLineId,
            BaseTraceIndex = l.BaseTraceIndex,
            Color = l.Color
        }).ToList();

        // Convert intersections
        dto.Intersections = Intersections.Select(i => new LineIntersectionDTO
        {
            Id = i.Id,
            Line1Id = i.Line1Id,
            Line2Id = i.Line2Id,
            Line1Name = i.Line1Name,
            Line2Name = i.Line2Name,
            IntersectionPoint = i.IntersectionPoint,
            Line1TraceIndex = i.Line1TraceIndex,
            Line2TraceIndex = i.Line2TraceIndex,
            IntersectionAngle = i.IntersectionAngle,
            IsPerpendicular = i.IsPerpendicular,
            NormalizationApplied = i.NormalizationApplied,
            AmplitudeMismatch = i.AmplitudeMismatch,
            PhaseMismatch = i.PhaseMismatch,
            FrequencyMismatch = i.FrequencyMismatch,
            TieQuality = i.TieQuality
        }).ToList();

        // Convert packages
        dto.Packages = Packages.Select(p => new SeismicCubePackageDTO
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            Color = p.Color,
            IsVisible = p.IsVisible,
            HorizonPoints = new List<Vector3>(p.HorizonPoints),
            LithologyType = p.LithologyType,
            SeismicFacies = p.SeismicFacies,
            Confidence = p.Confidence
        }).ToList();

        // Convert bounds
        dto.Bounds = new CubeBoundsDTO
        {
            MinX = Bounds.MinX,
            MaxX = Bounds.MaxX,
            MinY = Bounds.MinY,
            MaxY = Bounds.MaxY,
            MinZ = Bounds.MinZ,
            MaxZ = Bounds.MaxZ
        };

        // Convert grid parameters
        dto.GridParameters = new CubeGridParametersDTO
        {
            InlineCount = GridParameters.InlineCount,
            CrosslineCount = GridParameters.CrosslineCount,
            SampleCount = GridParameters.SampleCount,
            InlineSpacing = GridParameters.InlineSpacing,
            CrosslineSpacing = GridParameters.CrosslineSpacing,
            SampleInterval = GridParameters.SampleInterval
        };

        // Convert normalization settings
        dto.NormalizationSettings = new CubeNormalizationSettingsDTO
        {
            NormalizeAmplitude = NormalizationSettings.NormalizeAmplitude,
            AmplitudeMethod = (int)NormalizationSettings.AmplitudeMethod,
            MatchFrequency = NormalizationSettings.MatchFrequency,
            TargetFrequencyLow = NormalizationSettings.TargetFrequencyLow,
            TargetFrequencyHigh = NormalizationSettings.TargetFrequencyHigh,
            MatchPhase = NormalizationSettings.MatchPhase,
            MatchingWindowTraces = NormalizationSettings.MatchingWindowTraces,
            MatchingWindowMs = NormalizationSettings.MatchingWindowMs,
            SmoothTransitions = NormalizationSettings.SmoothTransitions,
            TransitionZoneTraces = NormalizationSettings.TransitionZoneTraces
        };

        return dto;
    }

    /// <summary>
    /// Restore state from DTO (seismic data must be loaded separately)
    /// </summary>
    public void FromDTO(SeismicCubeDatasetDTO dto)
    {
        SurveyName = dto.SurveyName;
        ProjectName = dto.ProjectName;
        CreationDate = dto.CreationDate;

        // Restore lines (without seismic data - that's loaded separately)
        Lines = dto.Lines.Select(l => new SeismicCubeLine
        {
            Id = l.Id,
            Name = l.Name,
            Geometry = new LineGeometry
            {
                StartPoint = l.Geometry.StartPoint,
                EndPoint = l.Geometry.EndPoint,
                TraceSpacing = l.Geometry.TraceSpacing,
                Azimuth = l.Geometry.Azimuth
            },
            IsVisible = l.IsVisible,
            IsPerpendicular = l.IsPerpendicular,
            BaseLineId = l.BaseLineId,
            BaseTraceIndex = l.BaseTraceIndex,
            Color = l.Color
        }).ToList();

        // Restore intersections
        Intersections = dto.Intersections.Select(i => new LineIntersection
        {
            Id = i.Id,
            Line1Id = i.Line1Id,
            Line2Id = i.Line2Id,
            Line1Name = i.Line1Name,
            Line2Name = i.Line2Name,
            IntersectionPoint = i.IntersectionPoint,
            Line1TraceIndex = i.Line1TraceIndex,
            Line2TraceIndex = i.Line2TraceIndex,
            IntersectionAngle = i.IntersectionAngle,
            IsPerpendicular = i.IsPerpendicular,
            NormalizationApplied = i.NormalizationApplied,
            AmplitudeMismatch = i.AmplitudeMismatch,
            PhaseMismatch = i.PhaseMismatch,
            FrequencyMismatch = i.FrequencyMismatch,
            TieQuality = i.TieQuality
        }).ToList();

        // Restore packages
        Packages = dto.Packages.Select(p => new SeismicCubePackage
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            Color = p.Color,
            IsVisible = p.IsVisible,
            HorizonPoints = new List<Vector3>(p.HorizonPoints),
            LithologyType = p.LithologyType,
            SeismicFacies = p.SeismicFacies,
            Confidence = p.Confidence
        }).ToList();

        // Restore bounds
        Bounds = new CubeBounds
        {
            MinX = dto.Bounds.MinX,
            MaxX = dto.Bounds.MaxX,
            MinY = dto.Bounds.MinY,
            MaxY = dto.Bounds.MaxY,
            MinZ = dto.Bounds.MinZ,
            MaxZ = dto.Bounds.MaxZ
        };

        // Restore grid parameters
        GridParameters = new CubeGridParameters
        {
            InlineCount = dto.GridParameters.InlineCount,
            CrosslineCount = dto.GridParameters.CrosslineCount,
            SampleCount = dto.GridParameters.SampleCount,
            InlineSpacing = dto.GridParameters.InlineSpacing,
            CrosslineSpacing = dto.GridParameters.CrosslineSpacing,
            SampleInterval = dto.GridParameters.SampleInterval
        };

        // Restore normalization settings
        NormalizationSettings = new CubeNormalizationSettings
        {
            NormalizeAmplitude = dto.NormalizationSettings.NormalizeAmplitude,
            AmplitudeMethod = (AmplitudeNormalizationMethod)dto.NormalizationSettings.AmplitudeMethod,
            MatchFrequency = dto.NormalizationSettings.MatchFrequency,
            TargetFrequencyLow = dto.NormalizationSettings.TargetFrequencyLow,
            TargetFrequencyHigh = dto.NormalizationSettings.TargetFrequencyHigh,
            MatchPhase = dto.NormalizationSettings.MatchPhase,
            MatchingWindowTraces = dto.NormalizationSettings.MatchingWindowTraces,
            MatchingWindowMs = dto.NormalizationSettings.MatchingWindowMs,
            SmoothTransitions = dto.NormalizationSettings.SmoothTransitions,
            TransitionZoneTraces = dto.NormalizationSettings.TransitionZoneTraces
        };
    }
}

/// <summary>
/// Represents a seismic line within a cube
/// </summary>
public class SeismicCubeLine
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public SeismicDataset? SeismicData { get; set; }
    public LineGeometry Geometry { get; set; } = new();
    public bool IsVisible { get; set; } = true;
    public bool IsPerpendicular { get; set; } = false;
    public string? BaseLineId { get; set; }
    public int? BaseTraceIndex { get; set; }
    public Vector4 Color { get; set; } = new(1, 1, 0, 1);
}

/// <summary>
/// Geometry definition for a seismic line
/// </summary>
public class LineGeometry
{
    public Vector3 StartPoint { get; set; }
    public Vector3 EndPoint { get; set; }
    public float TraceSpacing { get; set; } = 12.5f; // meters
    public float Azimuth { get; set; } = 0f; // degrees from north

    public float Length => Vector3.Distance(StartPoint, EndPoint);
    public Vector3 Direction => Vector3.Normalize(EndPoint - StartPoint);

    /// <summary>
    /// Get position at a specific trace index
    /// </summary>
    public Vector3 GetPositionAtTrace(int traceIndex)
    {
        float t = traceIndex * TraceSpacing / Length;
        return Vector3.Lerp(StartPoint, EndPoint, Math.Clamp(t, 0f, 1f));
    }

    /// <summary>
    /// Get direction vector at a specific trace
    /// </summary>
    public Vector3 GetDirectionAtTrace(int traceIndex)
    {
        return Direction;
    }

    /// <summary>
    /// Get trace index at a specific distance from start
    /// </summary>
    public int GetTraceAtDistance(float distance)
    {
        return (int)(distance / TraceSpacing);
    }
}

/// <summary>
/// Represents an intersection between two seismic lines
/// </summary>
public class LineIntersection
{
    public string Id { get; set; } = "";
    public string Line1Id { get; set; } = "";
    public string Line2Id { get; set; } = "";
    public string Line1Name { get; set; } = "";
    public string Line2Name { get; set; } = "";
    public Vector3 IntersectionPoint { get; set; }
    public int Line1TraceIndex { get; set; }
    public int Line2TraceIndex { get; set; }
    public float IntersectionAngle { get; set; } // degrees
    public bool IsPerpendicular { get; set; }
    public bool NormalizationApplied { get; set; }

    /// <summary>
    /// Amplitude mismatch at intersection before normalization
    /// </summary>
    public float AmplitudeMismatch { get; set; }

    /// <summary>
    /// Phase mismatch at intersection (in degrees)
    /// </summary>
    public float PhaseMismatch { get; set; }

    /// <summary>
    /// Frequency content difference
    /// </summary>
    public float FrequencyMismatch { get; set; }

    /// <summary>
    /// Quality of the tie after normalization (0-1)
    /// </summary>
    public float TieQuality { get; set; }
}

/// <summary>
/// 3D bounding box for the seismic cube
/// </summary>
public class CubeBounds
{
    public float MinX { get; set; }
    public float MaxX { get; set; }
    public float MinY { get; set; }
    public float MaxY { get; set; }
    public float MinZ { get; set; }
    public float MaxZ { get; set; }

    public float Width => MaxX - MinX;
    public float Height => MaxY - MinY;
    public float Depth => MaxZ - MinZ;

    public Vector3 Center => new Vector3(
        (MinX + MaxX) / 2,
        (MinY + MaxY) / 2,
        (MinZ + MaxZ) / 2
    );
}

/// <summary>
/// Grid parameters for the regularized cube
/// </summary>
public class CubeGridParameters
{
    public int InlineCount { get; set; } = 100;
    public int CrosslineCount { get; set; } = 100;
    public int SampleCount { get; set; } = 1000;
    public float InlineSpacing { get; set; } = 25f; // meters
    public float CrosslineSpacing { get; set; } = 25f; // meters
    public float SampleInterval { get; set; } = 4f; // milliseconds
}

/// <summary>
/// Normalization settings for joining seismic lines
/// </summary>
public class CubeNormalizationSettings
{
    /// <summary>
    /// Enable amplitude normalization
    /// </summary>
    public bool NormalizeAmplitude { get; set; } = true;

    /// <summary>
    /// Amplitude normalization method
    /// </summary>
    public AmplitudeNormalizationMethod AmplitudeMethod { get; set; } = AmplitudeNormalizationMethod.RMS;

    /// <summary>
    /// Enable frequency matching
    /// </summary>
    public bool MatchFrequency { get; set; } = true;

    /// <summary>
    /// Target frequency band low (Hz)
    /// </summary>
    public float TargetFrequencyLow { get; set; } = 10f;

    /// <summary>
    /// Target frequency band high (Hz)
    /// </summary>
    public float TargetFrequencyHigh { get; set; } = 80f;

    /// <summary>
    /// Enable phase matching
    /// </summary>
    public bool MatchPhase { get; set; } = true;

    /// <summary>
    /// Number of traces to use for matching window
    /// </summary>
    public int MatchingWindowTraces { get; set; } = 10;

    /// <summary>
    /// Time window for matching (ms)
    /// </summary>
    public float MatchingWindowMs { get; set; } = 500f;

    /// <summary>
    /// Apply smoothing at transitions
    /// </summary>
    public bool SmoothTransitions { get; set; } = true;

    /// <summary>
    /// Transition zone width in traces
    /// </summary>
    public int TransitionZoneTraces { get; set; } = 5;
}

/// <summary>
/// Amplitude normalization methods
/// </summary>
public enum AmplitudeNormalizationMethod
{
    RMS,
    Mean,
    Peak,
    Median,
    Balanced // Combines RMS and median
}

/// <summary>
/// Seismic package/horizon within the cube
/// </summary>
public class SeismicCubePackage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Package";
    public string Description { get; set; } = "";
    public Vector4 Color { get; set; } = new(1, 1, 0, 1);
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// Horizon points defining the top of this package (X, Y, Z/Time)
    /// </summary>
    public List<Vector3> HorizonPoints { get; set; } = new();

    /// <summary>
    /// Interpolated horizon grid
    /// </summary>
    public float[,]? HorizonGrid { get; set; }

    /// <summary>
    /// Lithology type for this package
    /// </summary>
    public string LithologyType { get; set; } = "";

    /// <summary>
    /// Seismic facies classification
    /// </summary>
    public string SeismicFacies { get; set; } = "";

    /// <summary>
    /// Confidence level (0-1)
    /// </summary>
    public float Confidence { get; set; } = 1.0f;
}

/// <summary>
/// Statistics for the seismic cube
/// </summary>
public class CubeStatistics
{
    public int LineCount { get; set; }
    public int IntersectionCount { get; set; }
    public int PackageCount { get; set; }
    public int TotalTraces { get; set; }
    public bool HasRegularizedVolume { get; set; }
    public CubeBounds Bounds { get; set; } = new();
    public CubeGridParameters GridParameters { get; set; } = new();
}
