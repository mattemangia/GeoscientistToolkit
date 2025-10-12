// GeoscientistToolkit/Data/Mesh3D/Mesh3DDataset.cs

using System.Globalization;
using System.Numerics;
using System.Text;
using GeoscientistToolkit.Util;

// Added

namespace GeoscientistToolkit.Data.Mesh3D;

/// <summary>
///     Dataset for 3D mesh objects (OBJ, STL files)
/// </summary>
public class Mesh3DDataset : Dataset, ISerializableDataset
{
    public Mesh3DDataset(string name, string filePath) : base(name, filePath)
    {
        Type = DatasetType.Mesh3D;
        Vertices = new List<Vector3>();
        Normals = new List<Vector3>();
        TextureCoordinates = new List<Vector2>();
        Faces = new List<int[]>();

        // Determine format from extension
        var ext = Path.GetExtension(filePath).ToLower();
        FileFormat = ext switch
        {
            ".obj" => "OBJ",
            ".stl" => "STL",
            _ => "Unknown"
        };
    }

    public int VertexCount { get; set; }
    public int FaceCount { get; set; }
    public Vector3 BoundingBoxMin { get; set; }
    public Vector3 BoundingBoxMax { get; set; }
    public Vector3 Center { get; set; }
    public float Scale { get; set; } = 1.0f;
    public string FileFormat { get; set; } // "OBJ" or "STL"

    // Mesh data
    public List<Vector3> Vertices { get; private set; }
    public List<Vector3> Normals { get; private set; }
    public List<Vector2> TextureCoordinates { get; set; }
    public List<int[]> Faces { get; private set; } // Each face is an array of vertex indices
    public bool IsLoaded { get; private set; }

    public object ToSerializableObject()
    {
        return new Mesh3DDatasetDTO
        {
            TypeName = nameof(Mesh3DDataset),
            Name = Name,
            FilePath = FilePath,
            FileFormat = FileFormat,
            Scale = Scale,
            VertexCount = VertexCount,
            FaceCount = FaceCount,
            BoundingBoxMin = BoundingBoxMin,
            BoundingBoxMax = BoundingBoxMax,
            Center = Center
        };
    }
    /// <summary>
    ///     Create an empty mesh dataset that can be edited
    /// </summary>
    /// <summary>
    ///     Create an empty mesh dataset that can be edited
    /// </summary>
    public static Mesh3DDataset CreateEmpty(string name, string filePath)
    {
        var dataset = new Mesh3DDataset(name, filePath)
        {
            FileFormat = "OBJ",
            IsLoaded = true,
            BoundingBoxMin = Vector3.Zero,
            BoundingBoxMax = Vector3.Zero,
            Center = Vector3.Zero,
            VertexCount = 0,
            FaceCount = 0
        };

        // Initialize empty collections
        dataset.Vertices = new List<Vector3>();
        dataset.Faces = new List<int[]>();
        dataset.Normals = new List<Vector3>();
        dataset.TextureCoordinates = new List<Vector2>();

        // Create a simple initial cube so the viewer has something to display
        dataset.AddInitialCube();

        Logger.Log($"Created empty mesh: {name}");
        return dataset;
    }
    /// <summary>
    ///     Add a simple unit cube as the initial geometry
    /// </summary>
    private void AddInitialCube()
    {
        // Define 8 cube vertices (unit cube centered at origin)
        var cubeVertices = new[]
        {
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f),
            new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f),
            new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f)
        };

        Vertices.AddRange(cubeVertices);

        // Define 12 triangular faces (2 per cube face)
        var cubeFaces = new[]
        {
            // Front
            new[] { 0, 1, 2 }, new[] { 0, 2, 3 },
            // Back
            new[] { 5, 4, 7 }, new[] { 5, 7, 6 },
            // Left
            new[] { 4, 0, 3 }, new[] { 4, 3, 7 },
            // Right
            new[] { 1, 5, 6 }, new[] { 1, 6, 2 },
            // Top
            new[] { 3, 2, 6 }, new[] { 3, 6, 7 },
            // Bottom
            new[] { 4, 5, 1 }, new[] { 4, 1, 0 }
        };

        Faces.AddRange(cubeFaces);

        VertexCount = Vertices.Count;
        FaceCount = Faces.Count;

        // Generate normals
        GenerateNormals();
        CalculateBounds();
    }
    public static Mesh3DDataset CreateFromData(string name, string filePath, List<Vector3> vertices, List<int[]> faces,
        float voxelSize, string unit)
    {
        var dataset = new Mesh3DDataset(name, filePath)
        {
            Vertices = vertices,
            Faces = faces,
            FileFormat = "OBJ" // We are saving as OBJ
        };

        // Adjust vertex positions based on physical voxel size, converting to millimeters
        float scaleFactor;
        if (unit.Equals("µm", StringComparison.OrdinalIgnoreCase))
            scaleFactor = voxelSize / 1000.0f; // Convert micrometers to millimeters
        else // Assume millimeters
            scaleFactor = voxelSize;

        if (Math.Abs(scaleFactor - 1.0f) > 1e-6f)
            for (var i = 0; i < dataset.Vertices.Count; i++)
                dataset.Vertices[i] *= scaleFactor;

        dataset.VertexCount = dataset.Vertices.Count;
        dataset.FaceCount = dataset.Faces.Count;

        dataset.GenerateNormals();
        dataset.CalculateBounds();

        // Save the data to the specified file path
        dataset.WriteOBJ(filePath);

        // Mark as loaded since data is already in memory
        dataset.IsLoaded = true;

        return dataset;
    }

    private void WriteOBJ(string path)
    {
        var culture = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        // Header
        sb.AppendLine("# Generated by Geoscientist Toolkit - Surface Nets Mesher");
        sb.AppendLine($"# Vertices: {VertexCount}");
        sb.AppendLine($"# Faces: {FaceCount}");
        sb.AppendLine();

        // Vertices
        foreach (var vertex in Vertices)
            sb.AppendLine($"v {vertex.X.ToString(culture)} {vertex.Y.ToString(culture)} {vertex.Z.ToString(culture)}");
        sb.AppendLine();

        // Normals
        foreach (var normal in Normals)
            sb.AppendLine($"vn {normal.X.ToString(culture)} {normal.Y.ToString(culture)} {normal.Z.ToString(culture)}");
        sb.AppendLine();

        // Faces (OBJ is 1-based, so add 1 to each index)
        // Format: f v1//vn1 v2//vn2 v3//vn3
        foreach (var face in Faces)
            if (face.Length >= 3) // It's a triangle or quad, we handle triangles
            {
                var i0 = face[0] + 1;
                var i1 = face[1] + 1;
                var i2 = face[2] + 1;
                sb.AppendLine($"f {i0}//{i0} {i1}//{i1} {i2}//{i2}");
            }

        try
        {
            File.WriteAllText(path, sb.ToString());
            Logger.Log($"Saved generated mesh to {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to write OBJ file to '{path}': {ex.Message}");
            throw;
        }
    }

    public override long GetSizeInBytes()
    {
        if (File.Exists(FilePath)) return new FileInfo(FilePath).Length;
        return 0;
    }

    public override void Load()
    {
        if (IsLoaded) return;

        // If file doesn't exist and we already have vertices, we're a new empty mesh
        if (!File.Exists(FilePath) && Vertices != null && Vertices.Count > 0)
        {
            Logger.Log($"Empty mesh already loaded in memory: {Name}");
            IsLoaded = true;
            return;
        }

        if (!File.Exists(FilePath))
        {
            Logger.LogError($"3D model file not found: {FilePath}");
            IsMissing = true;
            return;
        }

        try
        {
            Logger.Log($"Loading 3D model: {FilePath}");

            switch (FileFormat)
            {
                case "OBJ":
                    LoadOBJ();
                    break;
                case "STL":
                    LoadSTL();
                    break;
                default:
                    throw new NotSupportedException($"Unsupported 3D file format: {FileFormat}");
            }

            CalculateBounds();
            IsLoaded = true;
            Logger.Log($"3D model loaded: {VertexCount} vertices, {FaceCount} faces");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to load 3D model: {ex.Message}");
            throw;
        }
    }
    /// <summary>
    ///     Save the current mesh to its file path
    /// </summary>
    public void Save()
    {
        if (string.IsNullOrEmpty(FilePath))
        {
            Logger.LogError("Cannot save mesh: no file path specified");
            return;
        }

        try
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Recalculate bounds before saving
            CalculateBounds();

            // Write OBJ file
            WriteOBJ(FilePath);

            Logger.Log($"Saved mesh to {FilePath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to save mesh: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    ///     Make CalculateBounds public so editor can call it
    /// </summary>
    public void CalculateBounds()
    {
        if (Vertices.Count == 0)
        {
            BoundingBoxMin = Vector3.Zero;
            BoundingBoxMax = Vector3.Zero;
            Center = Vector3.Zero;
            return;
        }

        BoundingBoxMin = new Vector3(float.MaxValue);
        BoundingBoxMax = new Vector3(float.MinValue);

        foreach (var vertex in Vertices)
        {
            BoundingBoxMin = Vector3.Min(BoundingBoxMin, vertex);
            BoundingBoxMax = Vector3.Max(BoundingBoxMax, vertex);
        }

        Center = (BoundingBoxMin + BoundingBoxMax) * 0.5f;
    }
    private void LoadOBJ()
    {
        Vertices.Clear();
        Normals.Clear();
        TextureCoordinates.Clear();
        Faces.Clear();

        var lines = File.ReadAllLines(FilePath);
        var culture = CultureInfo.InvariantCulture;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                continue;

            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            switch (parts[0])
            {
                case "v": // Vertex
                    if (parts.Length >= 4)
                    {
                        var x = float.Parse(parts[1], culture);
                        var y = float.Parse(parts[2], culture);
                        var z = float.Parse(parts[3], culture);
                        Vertices.Add(new Vector3(x, y, z));
                    }

                    break;

                case "vn": // Normal
                    if (parts.Length >= 4)
                    {
                        var nx = float.Parse(parts[1], culture);
                        var ny = float.Parse(parts[2], culture);
                        var nz = float.Parse(parts[3], culture);
                        Normals.Add(Vector3.Normalize(new Vector3(nx, ny, nz)));
                    }

                    break;

                case "vt": // Texture coordinate
                    if (parts.Length >= 3)
                    {
                        var u = float.Parse(parts[1], culture);
                        var v = float.Parse(parts[2], culture);
                        TextureCoordinates.Add(new Vector2(u, v));
                    }

                    break;

                case "f": // Face
                    var face = new List<int>();
                    for (var i = 1; i < parts.Length; i++)
                    {
                        // Face indices can be in format: vertex/texture/normal
                        var indices = parts[i].Split('/');
                        if (int.TryParse(indices[0], out var vertexIndex))
                            // OBJ uses 1-based indexing, convert to 0-based
                            face.Add(vertexIndex - 1);
                    }

                    if (face.Count >= 3) Faces.Add(face.ToArray());
                    break;
            }
        }

        VertexCount = Vertices.Count;
        FaceCount = Faces.Count;

        // Generate normals if not present
        if (Normals.Count == 0) GenerateNormals();
    }

    private void LoadSTL()
    {
        Vertices.Clear();
        Normals.Clear();
        TextureCoordinates.Clear();
        Faces.Clear();

        // Check if it's ASCII or binary STL
        var isAscii = IsAsciiSTL(FilePath);

        if (isAscii)
            LoadAsciiSTL();
        else
            LoadBinarySTL();

        VertexCount = Vertices.Count;
        FaceCount = Faces.Count;
    }

    private bool IsAsciiSTL(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var buffer = new byte[5];
            stream.Read(buffer, 0, 5);
            var start = Encoding.ASCII.GetString(buffer);
            return start == "solid";
        }
        catch
        {
            return false;
        }
    }

    private void LoadAsciiSTL()
    {
        var lines = File.ReadAllLines(FilePath);
        var culture = CultureInfo.InvariantCulture;
        Vector3? currentNormal = null;
        var currentVertices = new List<int>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("facet normal"))
            {
                var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5)
                {
                    var nx = float.Parse(parts[2], culture);
                    var ny = float.Parse(parts[3], culture);
                    var nz = float.Parse(parts[4], culture);
                    currentNormal = new Vector3(nx, ny, nz);
                }

                currentVertices.Clear();
            }
            else if (trimmed.StartsWith("vertex"))
            {
                var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4)
                {
                    var x = float.Parse(parts[1], culture);
                    var y = float.Parse(parts[2], culture);
                    var z = float.Parse(parts[3], culture);
                    currentVertices.Add(Vertices.Count);
                    Vertices.Add(new Vector3(x, y, z));
                    if (currentNormal.HasValue) Normals.Add(currentNormal.Value);
                }
            }
            else if (trimmed.StartsWith("endfacet"))
            {
                if (currentVertices.Count == 3) Faces.Add(currentVertices.ToArray());
            }
        }
    }

    private void LoadBinarySTL()
    {
        using var reader = new BinaryReader(File.Open(FilePath, FileMode.Open));

        // Skip 80-byte header
        reader.ReadBytes(80);

        var triangleCount = reader.ReadUInt32();

        for (uint i = 0; i < triangleCount; i++)
        {
            // Read normal
            var nx = reader.ReadSingle();
            var ny = reader.ReadSingle();
            var nz = reader.ReadSingle();
            var normal = new Vector3(nx, ny, nz);

            // Read vertices
            var faceIndices = new int[3];
            for (var j = 0; j < 3; j++)
            {
                var x = reader.ReadSingle();
                var y = reader.ReadSingle();
                var z = reader.ReadSingle();

                faceIndices[j] = Vertices.Count;
                Vertices.Add(new Vector3(x, y, z));
                Normals.Add(normal);
            }

            Faces.Add(faceIndices);

            // Skip attribute byte count
            reader.ReadUInt16();
        }
    }

    private void GenerateNormals()
    {
        // Initialize normals for each vertex
        Normals.Clear();
        for (var i = 0; i < Vertices.Count; i++) Normals.Add(Vector3.Zero);

        // Calculate face normals and add to vertex normals
        foreach (var face in Faces)
            if (face.Length >= 3)
            {
                var v0 = Vertices[face[0]];
                var v1 = Vertices[face[1]];
                var v2 = Vertices[face[2]];

                var edge1 = v1 - v0;
                var edge2 = v2 - v0;
                var faceNormal = Vector3.Normalize(Vector3.Cross(edge1, edge2));

                // Add face normal to each vertex of the face
                foreach (var index in face) Normals[index] += faceNormal;
            }

        // Normalize all vertex normals
        for (var i = 0; i < Normals.Count; i++)
            if (Normals[i].LengthSquared() > 0)
                Normals[i] = Vector3.Normalize(Normals[i]);
    }

    public override void Unload()
    {
        if (!IsLoaded) return;

        Vertices.Clear();
        Normals.Clear();
        TextureCoordinates.Clear();
        Faces.Clear();
        IsLoaded = false;

        Logger.Log($"3D model unloaded: {Name}");
    }
}

/// <summary>
///     Data Transfer Object for Mesh3DDataset serialization
/// </summary>
public class Mesh3DDatasetDTO : DatasetDTO
{
    public string FileFormat { get; set; }
    public float Scale { get; set; }
    public int VertexCount { get; set; }
    public int FaceCount { get; set; }
    public Vector3 BoundingBoxMin { get; set; }
    public Vector3 BoundingBoxMax { get; set; }
    public Vector3 Center { get; set; }
}