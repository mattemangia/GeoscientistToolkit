// GeoscientistToolkit/Analysis/Photogrammetry/GeoreferencingManager.cs

using System;
using System.Collections.Generic;
using System.Numerics;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.Photogrammetry;

/// <summary>
/// Manages georeferencing with Ground Control Points (GCP).
/// </summary>
public class GeoreferencingManager
{
    private readonly List<GroundControlPoint> _gcps = new();

    public class GroundControlPoint
    {
        public string Name { get; set; }
        public Vector3 LocalPosition { get; set; }  // Position in local model coordinates
        public Vector3 WorldPosition { get; set; }  // Position in world coordinates (lat, lon, alt or UTM)
        public double Accuracy { get; set; }        // Accuracy in meters
        public bool IsActive { get; set; } = true;
    }

    public class GeoreferenceTransform
    {
        public Matrix4x4 TransformMatrix { get; set; }
        public double Scale { get; set; }
        public Vector3 Translation { get; set; }
        public Quaternion Rotation { get; set; }
        public double RmsError { get; set; }
        public int NumGcpsUsed { get; set; }
    }

    public List<GroundControlPoint> GroundControlPoints => _gcps;

    /// <summary>
    /// Add a ground control point.
    /// </summary>
    public void AddGcp(string name, Vector3 localPos, Vector3 worldPos, double accuracy = 1.0)
    {
        var gcp = new GroundControlPoint
        {
            Name = name,
            LocalPosition = localPos,
            WorldPosition = worldPos,
            Accuracy = accuracy,
            IsActive = true
        };

        _gcps.Add(gcp);
        Logger.Log($"GeoreferencingManager: Added GCP '{name}' at local {localPos}, world {worldPos}");
    }

    /// <summary>
    /// Remove a ground control point.
    /// </summary>
    public bool RemoveGcp(GroundControlPoint gcp)
    {
        if (_gcps.Remove(gcp))
        {
            Logger.Log($"GeoreferencingManager: Removed GCP '{gcp.Name}'");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Compute georeferencing transformation using similarity transform (7-parameter Helmert).
    /// Requires at least 3 GCPs.
    /// </summary>
    public GeoreferenceTransform ComputeTransform(bool refineWithAltitude = false)
    {
        var activeGcps = _gcps.FindAll(g => g.IsActive);

        if (activeGcps.Count < 3)
        {
            Logger.LogError("GeoreferencingManager: Need at least 3 active GCPs for georeferencing");
            return null;
        }

        try
        {
            // Compute similarity transform (scale, rotation, translation)
            var transform = ComputeSimilarityTransform(activeGcps);

            if (refineWithAltitude)
            {
                // Refine using altitude information
                transform = RefineWithAltitude(activeGcps, transform);
            }

            Logger.Log($"GeoreferencingManager: Computed transform with {activeGcps.Count} GCPs, " +
                           $"RMS error = {transform.RmsError:F3} m, scale = {transform.Scale:F6}");

            return transform;
        }
        catch (Exception ex)
        {
            Logger.LogError($"GeoreferencingManager: Failed to compute transform: {ex.Message}");
            return null;
        }
    }

    private GeoreferenceTransform ComputeSimilarityTransform(List<GroundControlPoint> gcps)
    {
        int n = gcps.Count;

        // Compute centroids
        Vector3 localCentroid = Vector3.Zero;
        Vector3 worldCentroid = Vector3.Zero;

        foreach (var gcp in gcps)
        {
            localCentroid += gcp.LocalPosition;
            worldCentroid += gcp.WorldPosition;
        }

        localCentroid /= n;
        worldCentroid /= n;

        // Center the points
        var localCentered = new Vector3[n];
        var worldCentered = new Vector3[n];

        for (int i = 0; i < n; i++)
        {
            localCentered[i] = gcps[i].LocalPosition - localCentroid;
            worldCentered[i] = gcps[i].WorldPosition - worldCentroid;
        }

        // Compute scale
        double localScale = 0;
        double worldScale = 0;

        foreach (var p in localCentered)
            localScale += p.LengthSquared();
        foreach (var p in worldCentered)
            worldScale += p.LengthSquared();

        double scale = Math.Sqrt(worldScale / localScale);

        // Compute rotation using Kabsch algorithm (SVD)
        var H = Matrix<double>.Build.Dense(3, 3);

        for (int i = 0; i < n; i++)
        {
            var local = localCentered[i];
            var world = worldCentered[i];

            H[0, 0] += local.X * world.X;
            H[0, 1] += local.X * world.Y;
            H[0, 2] += local.X * world.Z;
            H[1, 0] += local.Y * world.X;
            H[1, 1] += local.Y * world.Y;
            H[1, 2] += local.Y * world.Z;
            H[2, 0] += local.Z * world.X;
            H[2, 1] += local.Z * world.Y;
            H[2, 2] += local.Z * world.Z;
        }

        var svd = H.Svd();
        var R = svd.VT.Transpose() * svd.U.Transpose();

        // Ensure proper rotation (det(R) = 1)
        if (R.Determinant() < 0)
        {
            var V = svd.VT.Transpose();
            V[0, 2] *= -1;
            V[1, 2] *= -1;
            V[2, 2] *= -1;
            R = V * svd.U.Transpose();
        }

        // Convert rotation matrix to Quaternion
        var rotMat = new Matrix4x4(
            (float)R[0, 0], (float)R[0, 1], (float)R[0, 2], 0,
            (float)R[1, 0], (float)R[1, 1], (float)R[1, 2], 0,
            (float)R[2, 0], (float)R[2, 1], (float)R[2, 2], 0,
            0, 0, 0, 1
        );

        Quaternion rotation;
        try
        {
            rotation = Quaternion.CreateFromRotationMatrix(rotMat);
        }
        catch
        {
            rotation = Quaternion.Identity;
        }

        // Compute translation
        var rotatedCentroid = Vector3.Transform(localCentroid * (float)scale, rotation);
        var translation = worldCentroid - rotatedCentroid;

        // Build complete transform matrix
        var transformMatrix = Matrix4x4.CreateScale((float)scale) *
                              Matrix4x4.CreateFromQuaternion(rotation) *
                              Matrix4x4.CreateTranslation(translation);

        // Compute RMS error
        double rmsError = ComputeRmsError(gcps, transformMatrix);

        return new GeoreferenceTransform
        {
            TransformMatrix = transformMatrix,
            Scale = scale,
            Translation = translation,
            Rotation = rotation,
            RmsError = rmsError,
            NumGcpsUsed = n
        };
    }

    private GeoreferenceTransform RefineWithAltitude(List<GroundControlPoint> gcps, GeoreferenceTransform initialTransform)
    {
        // Refine the transform by adjusting the Z-component (altitude) more precisely
        // This is a simplified approach - could use weighted least squares

        double altitudeError = 0;
        int count = 0;

        foreach (var gcp in gcps)
        {
            var transformed = Vector3.Transform(gcp.LocalPosition, initialTransform.TransformMatrix);
            double error = transformed.Z - gcp.WorldPosition.Z;
            altitudeError += error;
            count++;
        }

        if (count > 0)
        {
            double meanAltitudeError = altitudeError / count;

            // Adjust translation
            var adjustedTranslation = initialTransform.Translation - new Vector3(0, 0, (float)meanAltitudeError);
            var adjustedMatrix = Matrix4x4.CreateScale((float)initialTransform.Scale) *
                                Matrix4x4.CreateFromQuaternion(initialTransform.Rotation) *
                                Matrix4x4.CreateTranslation(adjustedTranslation);

            double rmsError = ComputeRmsError(gcps, adjustedMatrix);

            Logger.Log($"GeoreferencingManager: Refined altitude, mean error reduced from " +
                           $"{meanAltitudeError:F3} m, new RMS = {rmsError:F3} m");

            return new GeoreferenceTransform
            {
                TransformMatrix = adjustedMatrix,
                Scale = initialTransform.Scale,
                Translation = adjustedTranslation,
                Rotation = initialTransform.Rotation,
                RmsError = rmsError,
                NumGcpsUsed = count
            };
        }

        return initialTransform;
    }

    private double ComputeRmsError(List<GroundControlPoint> gcps, Matrix4x4 transform)
    {
        double sumSquaredError = 0;

        foreach (var gcp in gcps)
        {
            var transformed = Vector3.Transform(gcp.LocalPosition, transform);
            var error = Vector3.Distance(transformed, gcp.WorldPosition);
            sumSquaredError += error * error;
        }

        return Math.Sqrt(sumSquaredError / gcps.Count);
    }

    /// <summary>
    /// Apply transform to a point.
    /// </summary>
    public Vector3 TransformPoint(Vector3 localPoint, GeoreferenceTransform transform)
    {
        return Vector3.Transform(localPoint, transform.TransformMatrix);
    }

    /// <summary>
    /// Apply transform to multiple points.
    /// </summary>
    public List<Vector3> TransformPoints(List<Vector3> localPoints, GeoreferenceTransform transform)
    {
        var worldPoints = new List<Vector3>(localPoints.Count);
        foreach (var pt in localPoints)
        {
            worldPoints.Add(Vector3.Transform(pt, transform.TransformMatrix));
        }
        return worldPoints;
    }

    public void Clear()
    {
        _gcps.Clear();
        Logger.Log("GeoreferencingManager: Cleared all GCPs");
    }
}
