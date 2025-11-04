// GeoscientistToolkit/Business/Photogrammetry/PhotogrammetryData.cs

using System;
using System.Collections.Generic;
using System.Numerics;
using GeoscientistToolkit.Business.Panorama; // Reuse DetectedFeatures, KeyPoint, FeatureMatch
using GeoscientistToolkit.Data.Image;

namespace GeoscientistToolkit.Business.Photogrammetry;

public enum PhotogrammetryState
{
    Idle,
    Initializing,
    ExtractingMetadata,
    DetectingFeatures,
    MatchingFeatures,
    AwaitingManualInput,
    ComputingSparseReconstruction,
    BuildingDenseCloud,
    BuildingMesh,
    BuildingTexture,
    BuildingOrthomosaic,
    BuildingDEM,
    Completed,
    Failed
}

public class PhotogrammetryImage
{
    public ImageDataset Dataset { get; }
    public Guid Id { get; } = Guid.NewGuid();
    public DetectedFeatures Features { get; set; }
    
    // Georeferencing data from metadata or EXIF
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? Altitude { get; set; }
    public bool IsGeoreferenced => Latitude.HasValue && Longitude.HasValue;
    
    // Camera parameters (if available from EXIF)
    public float? FocalLengthMm { get; set; }
    public float? SensorWidthMm { get; set; }
    public float? SensorHeightMm { get; set; }
    
    // Ground Control Point markers placed by user
    public List<GroundControlPoint> GroundControlPoints { get; } = new();

    public PhotogrammetryImage(ImageDataset dataset)
    {
        Dataset = dataset;
        
        // Try to extract georeferencing from metadata
        var meta = dataset.DatasetMetadata;
        if (meta != null)
        {
            Latitude = meta.Latitude;
            Longitude = meta.Longitude;
            Altitude = meta.Elevation;
        }
    }
}

/// <summary>
/// Ground Control Point - a marker placed by the user on an image with known 3D coordinates
/// </summary>
public class GroundControlPoint
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; } = "GCP";
    public Vector2 ImagePosition { get; set; } // Position in image coordinates
    public Vector3? WorldPosition { get; set; } // Known 3D position (X, Y, Z or Lat, Lon, Alt)
    public bool IsConfirmed => WorldPosition.HasValue;
}

/// <summary>
/// Represents a 3D point in the sparse or dense reconstruction
/// </summary>
public class Point3D
{
    public Vector3 Position { get; set; }
    public Vector3 Color { get; set; } // RGB color [0-1]
    public float Confidence { get; set; } = 1.0f;
    public List<Guid> ObservingImages { get; set; } = new(); // Which images can see this point
}

/// <summary>
/// Point cloud dataset for the reconstruction
/// </summary>
public class PhotogrammetryPointCloud
{
    public List<Point3D> Points { get; } = new();
    public bool IsDense { get; set; }
    public Vector3 BoundingBoxMin { get; set; }
    public Vector3 BoundingBoxMax { get; set; }
}

/// <summary>
/// Graph connecting photogrammetry images based on feature matches
/// </summary>
public class PhotogrammetryGraph
{
    private readonly Dictionary<Guid, PhotogrammetryImage> _nodes = new();
    public readonly Dictionary<Guid, List<(Guid neighbor, List<FeatureMatch> matches, Matrix4x4 relativePose)>> _adj = new();

    public PhotogrammetryGraph(IEnumerable<PhotogrammetryImage> images)
    {
        foreach (var image in images)
        {
            _nodes.Add(image.Id, image);
            _adj.Add(image.Id, new List<(Guid, List<FeatureMatch>, Matrix4x4)>());
        }
    }

    public void AddEdge(PhotogrammetryImage img1, PhotogrammetryImage img2, List<FeatureMatch> matches, Matrix4x4 relativePose)
    {
        lock (_adj)
        {
            if (!_adj.ContainsKey(img1.Id) || !_adj.ContainsKey(img2.Id)) return;
            
            _adj[img1.Id].Add((img2.Id, matches, relativePose));
            
            if (Matrix4x4.Invert(relativePose, out var invPose))
            {
                _adj[img2.Id].Add((img1.Id, matches, invPose));
            }
        }
    }
    
    public void RemoveNode(Guid nodeId)
    {
        if (!_nodes.ContainsKey(nodeId)) return;

        lock (_adj)
        {
            _nodes.Remove(nodeId);
            _adj.Remove(nodeId);

            foreach (var key in _adj.Keys)
            {
                _adj[key].RemoveAll(edge => edge.neighbor == nodeId);
            }
        }
    }
    
    public List<PhotogrammetryImageGroup> FindConnectedComponents()
    {
        var groups = new List<PhotogrammetryImageGroup>();
        var visited = new HashSet<Guid>();

        foreach (var nodeId in _nodes.Keys)
        {
            if (!visited.Contains(nodeId))
            {
                var group = new PhotogrammetryImageGroup();
                var stack = new Stack<Guid>();
                
                stack.Push(nodeId);
                visited.Add(nodeId);

                while (stack.Count > 0)
                {
                    var currentId = stack.Pop();
                    group.Images.Add(_nodes[currentId]);
                    
                    if (_adj.TryGetValue(currentId, out var neighbors))
                    {
                        foreach (var neighbor in neighbors)
                        {
                            if (!visited.Contains(neighbor.neighbor))
                            {
                                visited.Add(neighbor.neighbor);
                                stack.Push(neighbor.neighbor);
                            }
                        }
                    }
                }
                groups.Add(group);
            }
        }
        return groups;
    }
    
    public IEnumerable<PhotogrammetryImage> GetNodes() => _nodes.Values;
}

public class PhotogrammetryImageGroup
{
    public List<PhotogrammetryImage> Images { get; } = new();
}

/// <summary>
/// Configuration options for dense cloud generation
/// </summary>
public class DenseCloudOptions
{
    public int Quality { get; set; } = 2; // 0=Lowest, 1=Low, 2=Medium, 3=High, 4=Ultra
    public bool FilterOutliers { get; set; } = true;
    public float ConfidenceThreshold { get; set; } = 0.5f;
}

/// <summary>
/// Configuration options for mesh generation
/// </summary>
public class MeshOptions
{
    public enum SourceData { SparseCloud, DenseCloud }
    public SourceData Source { get; set; } = SourceData.DenseCloud;
    public int FaceCount { get; set; } = 100000; // Target number of faces
    public bool SimplifyMesh { get; set; } = true;
    public bool SmoothNormals { get; set; } = true;
}

/// <summary>
/// Configuration options for texture generation
/// </summary>
public class TextureOptions
{
    public int TextureSize { get; set; } = 4096; // Texture resolution
    public bool BlendTextures { get; set; } = true;
    public bool ColorCorrection { get; set; } = true;
}

/// <summary>
/// Configuration options for orthomosaic generation
/// </summary>
public class OrthomosaicOptions
{
    public enum SourceData { Mesh, PointCloud }
    public SourceData Source { get; set; } = SourceData.Mesh;
    public float GroundSamplingDistance { get; set; } = 0.01f; // meters per pixel
    public bool EnableBlending { get; set; } = true;
}

/// <summary>
/// Configuration options for DEM generation
/// </summary>
public class DEMOptions
{
    public enum SourceData { Mesh, PointCloud }
    public SourceData Source { get; set; } = SourceData.Mesh;
    public float Resolution { get; set; } = 1.0f; // meters per pixel
    public bool FillHoles { get; set; } = true;
    public bool SmoothSurface { get; set; } = false;
}