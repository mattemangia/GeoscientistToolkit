// GeoscientistToolkit/Analysis/AcousticSimulation/WaveFieldChunk.cs

using System.IO;

namespace GeoscientistToolkit.Analysis.AcousticSimulation;

/// <summary>
///     Represents a chunk of the wave field for memory-efficient processing.
/// </summary>
public class WaveFieldChunk : IDisposable
{
    public int StartZ { get; }
    public int Depth { get; }
    public int Width { get; }
    public int Height { get; }
    public bool IsOffloaded { get; set; }
    
    // Velocity fields
    public float[,,] Vx { get; private set; }
    public float[,,] Vy { get; private set; }
    public float[,,] Vz { get; private set; }
    
    // Stress fields
    public float[,,] Sxx { get; private set; }
    public float[,,] Syy { get; private set; }
    public float[,,] Szz { get; private set; }
    public float[,,] Sxy { get; private set; }
    public float[,,] Sxz { get; private set; }
    public float[,,] Syz { get; private set; }
    
    public long MemorySize =>
        (long)Width * Height * Depth * sizeof(float) * 9; // 3 velocity + 6 stress components

    public WaveFieldChunk(int startZ, int depth, int width, int height)
    {
        StartZ = startZ;
        Depth = depth;
        Width = width;
        Height = height;
        Initialize();
    }

    public void Initialize()
    {
        Vx = new float[Width, Height, Depth];
        Vy = new float[Width, Height, Depth];
        Vz = new float[Width, Height, Depth];
        Sxx = new float[Width, Height, Depth];
        Syy = new float[Width, Height, Depth];
        Szz = new float[Width, Height, Depth];
        Sxy = new float[Width, Height, Depth];
        Sxz = new float[Width, Height, Depth];
        Syz = new float[Width, Height, Depth];
    }

    public void SaveToFile(string path)
    {
        using var writer = new BinaryWriter(File.Create(path));
        
        // Write header
        writer.Write(Width);
        writer.Write(Height);
        writer.Write(Depth);
        writer.Write(StartZ);
        
        // Write velocity fields
        WriteField(writer, Vx);
        WriteField(writer, Vy);
        WriteField(writer, Vz);
        
        // Write stress fields
        WriteField(writer, Sxx);
        WriteField(writer, Syy);
        WriteField(writer, Szz);
        WriteField(writer, Sxy);
        WriteField(writer, Sxz);
        WriteField(writer, Syz);
    }

    public void LoadFromFile(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        
        // Read and verify header
        int w = reader.ReadInt32();
        int h = reader.ReadInt32();
        int d = reader.ReadInt32();
        int sz = reader.ReadInt32();
        
        if (w != Width || h != Height || d != Depth || sz != StartZ)
            throw new InvalidDataException("Chunk file header mismatch");
        
        // Allocate if needed
        if (Vx == null) Initialize();
        
        // Read velocity fields
        ReadField(reader, Vx);
        ReadField(reader, Vy);
        ReadField(reader, Vz);
        
        // Read stress fields
        ReadField(reader, Sxx);
        ReadField(reader, Syy);
        ReadField(reader, Szz);
        ReadField(reader, Sxy);
        ReadField(reader, Sxz);
        ReadField(reader, Syz);
    }

    private void WriteField(BinaryWriter writer, float[,,] field)
    {
        var buffer = new byte[field.Length * sizeof(float)];
        Buffer.BlockCopy(field, 0, buffer, 0, buffer.Length);
        writer.Write(buffer);
    }

    private void ReadField(BinaryReader reader, float[,,] field)
    {
        var buffer = new byte[field.Length * sizeof(float)];
        reader.Read(buffer, 0, buffer.Length);
        Buffer.BlockCopy(buffer, 0, field, 0, buffer.Length);
    }

    public void Dispose()
    {
        Vx = null;
        Vy = null;
        Vz = null;
        Sxx = null;
        Syy = null;
        Szz = null;
        Sxy = null;
        Sxz = null;
        Syz = null;
    }
}