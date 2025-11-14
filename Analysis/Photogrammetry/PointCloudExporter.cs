// GeoscientistToolkit/Analysis/Photogrammetry/PointCloudExporter.cs

using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.Photogrammetry;

/// <summary>
/// Export point clouds from photogrammetry pipeline.
/// </summary>
public static class PointCloudExporter
{
    public enum ExportFormat
    {
        PLY,    // Polygon File Format
        XYZ,    // Simple XYZ text format
        OBJ     // Wavefront OBJ (point cloud only)
    }

    /// <summary>
    /// Export keyframes as point cloud.
    /// </summary>
    public static bool ExportKeyframes(
        List<KeyframeManager.Keyframe> keyframes,
        string filePath,
        ExportFormat format,
        bool exportColors = true,
        GeoreferencingManager.GeoreferenceTransform geoTransform = null)
    {
        try
        {
            // Collect all 3D points from keyframes
            var points = new List<Vector3>();
            var colors = new List<Vector3>();

            foreach (var kf in keyframes)
            {
                for (int i = 0; i < kf.Points3D.Count; i++)
                {
                    var pt3D = kf.Points3D[i];
                    var point = new Vector3(pt3D.X, pt3D.Y, pt3D.Z);

                    // Apply georeferencing if available
                    if (geoTransform != null)
                    {
                        point = Vector3.Transform(point, geoTransform.TransformMatrix);
                    }

                    points.Add(point);

                    // Get color from image if requested
                    if (exportColors && kf.Image != null && i < kf.Keypoints.Count)
                    {
                        var kp = kf.Keypoints[i];
                        int x = (int)Math.Round(kp.Position.X);
                        int y = (int)Math.Round(kp.Position.Y);

                        if (x >= 0 && x < kf.Image.Width && y >= 0 && y < kf.Image.Height)
                        {
                            var color = kf.Image.At<Vec3b>(y, x);
                            colors.Add(new Vector3(color.Item2 / 255f, color.Item1 / 255f, color.Item0 / 255f)); // RGB
                        }
                        else
                        {
                            colors.Add(new Vector3(0.5f, 0.5f, 0.5f)); // Gray default
                        }
                    }
                    else
                    {
                        colors.Add(new Vector3(0.5f, 0.5f, 0.5f));
                    }
                }
            }

            Logger.Log($"Exporting {points.Count} points to {filePath}");

            return format switch
            {
                ExportFormat.PLY => ExportPLY(points, colors, filePath, exportColors),
                ExportFormat.XYZ => ExportXYZ(points, colors, filePath, exportColors),
                ExportFormat.OBJ => ExportOBJ(points, colors, filePath, exportColors),
                _ => false
            };
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export point cloud: {ex.Message}");
            return false;
        }
    }

    private static bool ExportPLY(List<Vector3> points, List<Vector3> colors, string filePath, bool exportColors)
    {
        var sb = new StringBuilder();

        // PLY header
        sb.AppendLine("ply");
        sb.AppendLine("format ascii 1.0");
        sb.AppendLine($"element vertex {points.Count}");
        sb.AppendLine("property float x");
        sb.AppendLine("property float y");
        sb.AppendLine("property float z");

        if (exportColors && colors.Count == points.Count)
        {
            sb.AppendLine("property uchar red");
            sb.AppendLine("property uchar green");
            sb.AppendLine("property uchar blue");
        }

        sb.AppendLine("end_header");

        // Vertex data
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            sb.Append($"{p.X} {p.Y} {p.Z}");

            if (exportColors && i < colors.Count)
            {
                var c = colors[i];
                int r = (int)(c.X * 255);
                int g = (int)(c.Y * 255);
                int b = (int)(c.Z * 255);
                sb.Append($" {r} {g} {b}");
            }

            sb.AppendLine();
        }

        File.WriteAllText(filePath, sb.ToString());
        Logger.Log($"Exported PLY file with {points.Count} vertices");
        return true;
    }

    private static bool ExportXYZ(List<Vector3> points, List<Vector3> colors, string filePath, bool exportColors)
    {
        var sb = new StringBuilder();

        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            sb.Append($"{p.X} {p.Y} {p.Z}");

            if (exportColors && i < colors.Count)
            {
                var c = colors[i];
                sb.Append($" {c.X} {c.Y} {c.Z}");
            }

            sb.AppendLine();
        }

        File.WriteAllText(filePath, sb.ToString());
        Logger.Log($"Exported XYZ file with {points.Count} points");
        return true;
    }

    private static bool ExportOBJ(List<Vector3> points, List<Vector3> colors, string filePath, bool exportColors)
    {
        var sb = new StringBuilder();

        // OBJ header
        sb.AppendLine("# Photogrammetry Point Cloud Export");
        sb.AppendLine($"# Points: {points.Count}");
        sb.AppendLine();

        // Vertices
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            sb.AppendLine($"v {p.X} {p.Y} {p.Z}");
        }

        // Optional: export color as separate .mtl file
        if (exportColors && colors.Count == points.Count)
        {
            var mtlPath = Path.ChangeExtension(filePath, ".mtl");
            ExportMTL(colors, mtlPath);
            sb.Insert(0, $"mtllib {Path.GetFileName(mtlPath)}\n");
        }

        File.WriteAllText(filePath, sb.ToString());
        Logger.Log($"Exported OBJ file with {points.Count} vertices");
        return true;
    }

    private static void ExportMTL(List<Vector3> colors, string mtlPath)
    {
        // This is a simplified MTL export - one material per vertex is not standard
        // Better approach would be to group points by color
        var sb = new StringBuilder();
        sb.AppendLine("# Material file for point cloud");
        sb.AppendLine("newmtl default");
        sb.AppendLine("Ka 1.0 1.0 1.0");
        sb.AppendLine("Kd 0.8 0.8 0.8");
        sb.AppendLine("Ks 0.0 0.0 0.0");

        File.WriteAllText(mtlPath, sb.ToString());
    }

    /// <summary>
    /// Export camera path as separate file.
    /// </summary>
    public static bool ExportCameraPath(
        List<KeyframeManager.Keyframe> keyframes,
        string filePath)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Camera Path Export");
            sb.AppendLine($"# Keyframes: {keyframes.Count}");
            sb.AppendLine("# Format: FrameID X Y Z QX QY QZ QW Timestamp");
            sb.AppendLine();

            foreach (var kf in keyframes)
            {
                // Extract position from pose matrix
                var pos = new Vector3(kf.Pose.M14, kf.Pose.M24, kf.Pose.M34);

                // Extract rotation as quaternion
                Matrix4x4.Decompose(kf.Pose, out var scale, out var rotation, out var translation);

                sb.AppendLine($"{kf.FrameId} {pos.X} {pos.Y} {pos.Z} " +
                             $"{rotation.X} {rotation.Y} {rotation.Z} {rotation.W} " +
                             $"{kf.Timestamp:yyyy-MM-dd HH:mm:ss.fff}");
            }

            File.WriteAllText(filePath, sb.ToString());
            Logger.Log($"Exported camera path with {keyframes.Count} keyframes");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export camera path: {ex.Message}");
            return false;
        }
    }
}
