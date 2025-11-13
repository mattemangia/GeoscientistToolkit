// GeoscientistToolkit/Business/Photogrammetry/PhotogrammetryData.cs

using System;
using System.Collections.Generic;
using System.Numerics;
using GeoscientistToolkit.Business.Panorama; // Reuse DetectedFeatures, KeyPoint, FeatureMatch
using GeoscientistToolkit.Business.Photogrammetry;
using GeoscientistToolkit.Data.Image;

namespace GeoscientistToolkit;

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
    public DetectedFeatures Features { get; set; }  // ORB features (legacy/fallback)
    public SiftFeatures SiftFeatures { get; set; }  // SIFT features (preferred for photogrammetry)
    
    // Camera intrinsic parameters (K matrix)
    public Matrix4x4 IntrinsicMatrix { get; set; }
    
    // Calculated global pose (Extrinsic parameters, World-to-Camera is the inverse of this)
    public Matrix4x4 GlobalPose { get; set; } = Matrix4x4.Identity;

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