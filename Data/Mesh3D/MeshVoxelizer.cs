// GeoscientistToolkit/Data/Mesh3D/MeshVoxelizer.cs

using System.Numerics;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Mesh3D;

/// <summary>
///     Converts 3D mesh models to voxelized volume data
/// </summary>
public class MeshVoxelizer
{
    /// <summary>
    ///     Voxelizes a mesh and saves it as an image stack
    /// </summary>
    public async Task<string> VoxelizeToImageStackAsync(
        Mesh3DDataset mesh,
        string outputFolder,
        float voxelSize,
        Vector3 rotation,
        IProgress<(float progress, string message)> progress)
    {
        if (!mesh.IsLoaded)
            throw new InvalidOperationException("Mesh must be loaded before voxelization");

        progress?.Report((0.0f, "Calculating volume dimensions..."));

        // Apply transformations to get actual bounds
        var transformedBounds = CalculateTransformedBounds(mesh, rotation);
        var size = transformedBounds.max - transformedBounds.min;

        // Calculate volume dimensions
        var width = (int)Math.Ceiling(size.X / voxelSize) + 2; // +2 for padding
        var height = (int)Math.Ceiling(size.Y / voxelSize) + 2;
        var depth = (int)Math.Ceiling(size.Z / voxelSize) + 2;

        Logger.Log($"[MeshVoxelizer] Voxelizing mesh to {width}×{height}×{depth} volume");
        Logger.Log(
            $"[MeshVoxelizer] Voxel size: {voxelSize}mm, Bounds: {transformedBounds.min} to {transformedBounds.max}");

        // Create output folder for the stack
        var stackFolder = Path.Combine(outputFolder, $"{mesh.Name}_voxelized_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(stackFolder);

        // Create voxel grid
        progress?.Report((0.1f, "Creating voxel grid..."));
        var voxelGrid = new byte[width, height, depth];

        // Transform mesh vertices
        progress?.Report((0.2f, "Transforming mesh..."));
        var transformedVertices = TransformVertices(mesh.Vertices, mesh.Scale, rotation, mesh.Center);

        // Voxelize the mesh using triangle voxelization
        progress?.Report((0.3f, "Voxelizing triangles..."));
        await VoxelizeTrianglesAsync(
            transformedVertices,
            mesh.Faces,
            voxelGrid,
            transformedBounds.min,
            voxelSize,
            progress);

        // Fill interior voxels
        progress?.Report((0.7f, "Filling interior voxels..."));
        FillInteriorVoxels(voxelGrid);

        // Save as image stack
        progress?.Report((0.85f, "Saving image stack..."));
        await SaveAsImageStackAsync(voxelGrid, stackFolder, progress);

        // Create metadata file
        progress?.Report((0.95f, "Writing metadata..."));
        WriteMetadata(stackFolder, width, height, depth, voxelSize);

        progress?.Report((1.0f, "Voxelization complete!"));

        return stackFolder;
    }

    private (Vector3 min, Vector3 max) CalculateTransformedBounds(Mesh3DDataset mesh, Vector3 rotation)
    {
        var transformMatrix = CreateTransformMatrix(mesh.Scale, rotation, mesh.Center);

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var vertex in mesh.Vertices)
        {
            var transformed = Vector3.Transform(vertex, transformMatrix);
            min = Vector3.Min(min, transformed);
            max = Vector3.Max(max, transformed);
        }

        return (min, max);
    }

    private Matrix4x4 CreateTransformMatrix(float scale, Vector3 rotation, Vector3 center)
    {
        // Convert rotation from degrees to radians
        var rotX = rotation.X * (float)(Math.PI / 180.0);
        var rotY = rotation.Y * (float)(Math.PI / 180.0);
        var rotZ = rotation.Z * (float)(Math.PI / 180.0);

        // Create transformation matrix: translate to origin, scale, rotate, translate back
        var toOrigin = Matrix4x4.CreateTranslation(-center);
        var scaleMatrix = Matrix4x4.CreateScale(scale);
        var rotationMatrix = Matrix4x4.CreateRotationX(rotX) *
                             Matrix4x4.CreateRotationY(rotY) *
                             Matrix4x4.CreateRotationZ(rotZ);
        var fromOrigin = Matrix4x4.CreateTranslation(center);

        return toOrigin * scaleMatrix * rotationMatrix * fromOrigin;
    }

    private List<Vector3> TransformVertices(List<Vector3> vertices, float scale, Vector3 rotation, Vector3 center)
    {
        var transformMatrix = CreateTransformMatrix(scale, rotation, center);
        var transformed = new List<Vector3>(vertices.Count);

        foreach (var vertex in vertices) transformed.Add(Vector3.Transform(vertex, transformMatrix));

        return transformed;
    }

    private async Task VoxelizeTrianglesAsync(
        List<Vector3> vertices,
        List<int[]> faces,
        byte[,,] voxelGrid,
        Vector3 minBounds,
        float voxelSize,
        IProgress<(float progress, string message)> progress)
    {
        var totalFaces = faces.Count;
        var processedFaces = 0;

        // Process faces in batches for better performance
        var batchSize = Math.Max(1, totalFaces / 100);

        await Task.Run(() =>
        {
            Parallel.ForEach(faces, face =>
            {
                if (face.Length >= 3)
                    VoxelizeTriangle(
                        vertices[face[0]],
                        vertices[face[1]],
                        vertices[face[2]],
                        voxelGrid,
                        minBounds,
                        voxelSize);

                var current = Interlocked.Increment(ref processedFaces);
                if (current % batchSize == 0)
                {
                    var faceProgress = (float)current / totalFaces;
                    progress?.Report((0.3f + faceProgress * 0.4f,
                        $"Voxelizing triangles... {current}/{totalFaces}"));
                }
            });
        });
    }

    private void VoxelizeTriangle(
        Vector3 v0, Vector3 v1, Vector3 v2,
        byte[,,] voxelGrid,
        Vector3 minBounds,
        float voxelSize)
    {
        // Calculate triangle bounding box in voxel space
        var minTriangle = Vector3.Min(Vector3.Min(v0, v1), v2);
        var maxTriangle = Vector3.Max(Vector3.Max(v0, v1), v2);

        var minX = Math.Max(0, (int)((minTriangle.X - minBounds.X) / voxelSize));
        var minY = Math.Max(0, (int)((minTriangle.Y - minBounds.Y) / voxelSize));
        var minZ = Math.Max(0, (int)((minTriangle.Z - minBounds.Z) / voxelSize));

        var maxX = Math.Min(voxelGrid.GetLength(0) - 1, (int)((maxTriangle.X - minBounds.X) / voxelSize) + 1);
        var maxY = Math.Min(voxelGrid.GetLength(1) - 1, (int)((maxTriangle.Y - minBounds.Y) / voxelSize) + 1);
        var maxZ = Math.Min(voxelGrid.GetLength(2) - 1, (int)((maxTriangle.Z - minBounds.Z) / voxelSize) + 1);

        // Voxelize using conservative rasterization
        for (var z = minZ; z <= maxZ; z++)
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            var voxelCenter = new Vector3(
                minBounds.X + (x + 0.5f) * voxelSize,
                minBounds.Y + (y + 0.5f) * voxelSize,
                minBounds.Z + (z + 0.5f) * voxelSize);

            if (VoxelIntersectsTriangle(voxelCenter, voxelSize * 0.5f, v0, v1, v2)) voxelGrid[x, y, z] = 255;
        }
    }

    private bool VoxelIntersectsTriangle(Vector3 voxelCenter, float halfSize, Vector3 v0, Vector3 v1, Vector3 v2)
    {
        // Simple AABB-triangle intersection test
        // Check if voxel box intersects with triangle

        // First, check if triangle plane intersects voxel
        var normal = Vector3.Normalize(Vector3.Cross(v1 - v0, v2 - v0));
        var d = -Vector3.Dot(normal, v0);

        // Distance from voxel center to plane
        var dist = Math.Abs(Vector3.Dot(normal, voxelCenter) + d);

        // Project half-size onto normal (maximum distance from center to corner along normal)
        var projectedHalfSize = halfSize * (Math.Abs(normal.X) + Math.Abs(normal.Y) + Math.Abs(normal.Z));

        if (dist > projectedHalfSize)
            return false;


        return true;
    }

    private void FillInteriorVoxels(byte[,,] voxelGrid)
    {
        var width = voxelGrid.GetLength(0);
        var height = voxelGrid.GetLength(1);
        var depth = voxelGrid.GetLength(2);

        // Use scanline filling algorithm for each XY slice
        Parallel.For(0, depth, z =>
        {
            for (var y = 0; y < height; y++)
            {
                // Scanline fill along X axis
                var inside = false;
                var lastBoundary = -1;

                for (var x = 0; x < width; x++)
                    if (voxelGrid[x, y, z] == 255)
                    {
                        if (x > lastBoundary + 1 && inside)
                            // Fill the gap
                            for (var fillX = lastBoundary + 1; fillX < x; fillX++)
                                voxelGrid[fillX, y, z] = 128; // Use different value for filled voxels

                        lastBoundary = x;
                        inside = !inside;
                    }
            }

            // Convert filled voxels to same value as surface
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                if (voxelGrid[x, y, z] == 128)
                    voxelGrid[x, y, z] = 255;
        });
    }

    private async Task SaveAsImageStackAsync(
        byte[,,] voxelGrid,
        string outputFolder,
        IProgress<(float progress, string message)> progress)
    {
        var width = voxelGrid.GetLength(0);
        var height = voxelGrid.GetLength(1);
        var depth = voxelGrid.GetLength(2);

        await Task.Run(() =>
        {
            for (var z = 0; z < depth; z++)
            {
                // Extract slice
                var sliceData = new byte[width * height];
                for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    sliceData[y * width + x] = voxelGrid[x, y, z];

                // Save as BMP (simple format)
                var filename = Path.Combine(outputFolder, $"slice_{z:D4}.bmp");
                SaveAsBitmap(filename, sliceData, width, height);

                if (z % 10 == 0)
                {
                    var saveProgress = (float)z / depth;
                    progress?.Report((0.85f + saveProgress * 0.1f,
                        $"Saving slices... {z}/{depth}"));
                }
            }
        });

        Logger.Log($"[MeshVoxelizer] Saved {depth} image slices to {outputFolder}");
    }

    private void SaveAsBitmap(string filename, byte[] data, int width, int height)
    {
        // Calculate row padding (BMP rows must be aligned to 4-byte boundary)
        var rowPadding = (4 - width % 4) % 4;
        var rowSize = width + rowPadding;

        // BMP file header (14 bytes)
        var fileSize = 14 + 40 + 1024 + rowSize * height; // headers + palette + padded data
        var dataOffset = 14 + 40 + 1024; // after headers and palette

        using (var stream = File.Create(filename))
        using (var writer = new BinaryWriter(stream))
        {
            // File header
            writer.Write((byte)'B');
            writer.Write((byte)'M');
            writer.Write(fileSize);
            writer.Write(0); // reserved
            writer.Write(dataOffset);

            // Info header (40 bytes)
            writer.Write(40); // header size
            writer.Write(width);
            writer.Write(height);
            writer.Write((short)1); // planes
            writer.Write((short)8); // bits per pixel
            writer.Write(0); // compression
            writer.Write(rowSize * height); // image size with padding
            writer.Write(0); // x pixels per meter
            writer.Write(0); // y pixels per meter
            writer.Write(256); // colors used
            writer.Write(256); // important colors

            // Grayscale palette (256 * 4 bytes)
            for (var i = 0; i < 256; i++)
            {
                writer.Write((byte)i); // blue
                writer.Write((byte)i); // green
                writer.Write((byte)i); // red
                writer.Write((byte)0); // reserved
            }

            // Image data (bottom-up)
            for (var y = height - 1; y >= 0; y--)
            {
                for (var x = 0; x < width; x++) writer.Write(data[y * width + x]);
                // Pad to 4-byte boundary
                for (var p = 0; p < rowPadding; p++) writer.Write((byte)0);
            }
        }
    }

    private void WriteMetadata(string outputFolder, int width, int height, int depth, float voxelSize)
    {
        var metadataPath = Path.Combine(outputFolder, "metadata.txt");

        var lines = new List<string>
        {
            $"Volume Dimensions: {width} × {height} × {depth}",
            $"Voxel Size: {voxelSize} mm",
            $"Total Voxels: {(long)width * height * depth:N0}",
            $"Created: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            "Format: BMP Image Stack",
            "Bit Depth: 8-bit grayscale",
            "File Pattern: slice_*.bmp"
        };

        File.WriteAllLines(metadataPath, lines);

        Logger.Log($"[MeshVoxelizer] Metadata written to {metadataPath}");
    }
}