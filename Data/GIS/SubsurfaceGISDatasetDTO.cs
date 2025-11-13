// GeoscientistToolkit/Data/GIS/SubsurfaceGISDatasetDTO.cs

using System.Numerics;

namespace GeoscientistToolkit.Data.GIS;

/// <summary>
/// Data Transfer Object for SubsurfaceGISDataset serialization
/// </summary>
public class SubsurfaceGISDatasetDTO : GISDatasetDTO
{
    public List<string> SourceBoreholeNames { get; set; }
    public List<SubsurfaceVoxelDTO> VoxelGrid { get; set; }
    public List<SubsurfaceLayerBoundaryDTO> LayerBoundaries { get; set; }
    public Vector3 GridOrigin { get; set; }
    public Vector3 GridSize { get; set; }
    public Vector3 VoxelSize { get; set; }
    public int GridResolutionX { get; set; }
    public int GridResolutionY { get; set; }
    public int GridResolutionZ { get; set; }
    public float InterpolationRadius { get; set; }
    public int InterpolationMethod { get; set; }
    public float IDWPower { get; set; }
    public string HeightmapDatasetName { get; set; }
}

/// <summary>
/// DTO for SubsurfaceVoxel
/// </summary>
public class SubsurfaceVoxelDTO
{
    public Vector3 Position { get; set; }
    public string LithologyType { get; set; }
    public Dictionary<string, float> Parameters { get; set; }
    public float Confidence { get; set; }
}

/// <summary>
/// DTO for SubsurfaceLayerBoundary
/// </summary>
public class SubsurfaceLayerBoundaryDTO
{
    public string LayerName { get; set; }
    public List<Vector3> Points { get; set; }
    public float[,] ElevationGrid { get; set; }
    public BoundingBoxDTO GridBounds { get; set; }
}

/// <summary>
/// DTO for BoundingBox (for subsurface layers)
/// </summary>
public class BoundingBoxDTO
{
    public Vector2 Min { get; set; }
    public Vector2 Max { get; set; }
}