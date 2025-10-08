// GeoscientistToolkit/Data/VolumeData/ChunkedLabelVolume.cs

using System.IO.MemoryMappedFiles;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.VolumeData;

/// <summary>
///     Provides chunked storage for volumetric label data with support for both
///     in-memory and memory-mapped file storage to handle 30GB+ datasets.
/// </summary>
public class ChunkedLabelVolume : ILabelVolumeData
{
    #region Fields and Properties

    // Volume dimensions
    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }
    public int ChunkDim { get; }

    // Chunking properties
    public int ChunkCountX { get; }
    public int ChunkCountY { get; }
    public int ChunkCountZ { get; }

    // Storage mode fields
    private byte[][] _chunks; // For in-memory mode
    private MemoryMappedFile _mmf; // For memory-mapped mode
    private MemoryMappedViewAccessor _viewAccessor;
    private readonly bool _useMemoryMapping;
    public string FilePath { get; }

    // Header size constant
    private const int HEADER_SIZE = 16; // 4 integers (ChunkDim, ChunkCountX, ChunkCountY, ChunkCountZ)

    private bool _disposed;

    #endregion

    #region Constructors

    /// <summary>
    ///     Constructor for creating a volume from scratch.
    /// </summary>
    public ChunkedLabelVolume(int width, int height, int depth, int chunkDim, bool useMemoryMapping,
        string filePath = null)
    {
        ValidateDimensions(width, height, depth, chunkDim);

        Width = width;
        Height = height;
        Depth = depth;
        ChunkDim = chunkDim;
        _useMemoryMapping = useMemoryMapping;
        FilePath = filePath;

        ChunkCountX = (width + chunkDim - 1) / chunkDim;
        ChunkCountY = (height + chunkDim - 1) / chunkDim;
        ChunkCountZ = (depth + chunkDim - 1) / chunkDim;

        Logger.Log(
            $"[ChunkedLabelVolume] Initializing volume: {Width}x{Height}x{Depth}, ChunkDim={ChunkDim}, useMM={_useMemoryMapping}");

        if (_useMemoryMapping)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path is required for memory mapping.");

            InitializeMemoryMappedFile();
        }
        else
        {
            InitializeInMemoryStorage();
        }
    }

    /// <summary>
    ///     Constructor for memory-mapped mode when an MMF is already available.
    /// </summary>
    public ChunkedLabelVolume(int width, int height, int depth, int chunkDim, MemoryMappedFile mmf)
    {
        ValidateDimensions(width, height, depth, chunkDim);

        Width = width;
        Height = height;
        Depth = depth;
        ChunkDim = chunkDim;
        _useMemoryMapping = true;

        ChunkCountX = (width + chunkDim - 1) / chunkDim;
        ChunkCountY = (height + chunkDim - 1) / chunkDim;
        ChunkCountZ = (depth + chunkDim - 1) / chunkDim;

        _mmf = mmf;
        _viewAccessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);

        Logger.Log($"[ChunkedLabelVolume] Initialized MM volume: {Width}x{Height}x{Depth}, ChunkDim={ChunkDim}");
    }

    #endregion

    #region Public Methods

    public byte[] GetAllData()
    {
        var requiredMemory = (long)Width * Height * Depth;
        Logger.LogWarning(
            $"[ChunkedLabelVolume] GetAllData() called. Allocating {requiredMemory / (1024 * 1024)} MB of RAM.");

        var fullVolume = new byte[requiredMemory];

        Parallel.For(0, Depth, z =>
        {
            var zOffset = (long)z * Width * Height;
            for (var y = 0; y < Height; y++)
            {
                var yzOffset = zOffset + (long)y * Width;
                for (var x = 0; x < Width; x++) fullVolume[yzOffset + x] = this[x, y, z];
            }
        });

        return fullVolume;
    }

    /// <summary>
    ///     Indexer for voxel data access.
    /// </summary>
    public byte this[int x, int y, int z]
    {
        get
        {
            try
            {
                ValidateCoordinates(x, y, z);
                var (chunkIndex, offset) = GetChunkIndexAndOffset(x, y, z);

                if (_useMemoryMapping)
                {
                    var globalOffset = CalculateGlobalOffset(chunkIndex, offset);
                    return _viewAccessor.ReadByte(globalOffset);
                }

                return _chunks[chunkIndex][offset];
            }
            catch (Exception ex)
            {
                Logger.Log($"[ChunkedLabelVolume] Get voxel error at ({x},{y},{z}): {ex.Message}");
                return 0;
            }
        }
        set
        {
            try
            {
                ValidateCoordinates(x, y, z);
                var (chunkIndex, offset) = GetChunkIndexAndOffset(x, y, z);

                if (_useMemoryMapping)
                {
                    var globalOffset = CalculateGlobalOffset(chunkIndex, offset);
                    _viewAccessor.Write(globalOffset, value);
                }
                else
                {
                    _chunks[chunkIndex][offset] = value;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[ChunkedLabelVolume] Set voxel error at ({x},{y},{z}): {ex.Message}");
            }
        }
    }

    /// <summary>
    ///     Reads an entire Z-slice into the provided buffer efficiently.
    /// </summary>
    public void ReadSliceZ(int z, byte[] buffer)
    {
        if (buffer == null || buffer.Length != Width * Height)
            throw new ArgumentException("Buffer size must match slice dimensions (Width * Height).");

        ValidateCoordinates(0, 0, z);

        var cz = z / ChunkDim;
        var lz = z % ChunkDim;

        Parallel.For(0, ChunkCountY, cy =>
        {
            for (var cx = 0; cx < ChunkCountX; cx++)
            {
                var chunkIndex = GetChunkIndex(cx, cy, cz);

                var xStart = cx * ChunkDim;
                var yStart = cy * ChunkDim;
                var xEnd = Math.Min(xStart + ChunkDim, Width);
                var yEnd = Math.Min(yStart + ChunkDim, Height);

                for (var y = yStart; y < yEnd; y++)
                {
                    var ly = y % ChunkDim;
                    var dstOffset = y * Width + xStart;
                    var srcOffsetInChunk = lz * ChunkDim * ChunkDim + ly * ChunkDim;
                    var length = xEnd - xStart;

                    if (_useMemoryMapping)
                    {
                        var chunkStartInFile = CalculateGlobalOffset(chunkIndex, 0);
                        _viewAccessor.ReadArray(chunkStartInFile + srcOffsetInChunk, buffer, dstOffset, length);
                    }
                    else
                    {
                        Array.Copy(_chunks[chunkIndex], srcOffsetInChunk, buffer, dstOffset, length);
                    }
                }
            }
        });
    }

    /// <summary>
    ///     Writes an entire Z-slice from the provided buffer efficiently.
    /// </summary>
    public void WriteSliceZ(int z, byte[] buffer)
    {
        if (buffer == null || buffer.Length != Width * Height)
            throw new ArgumentException("Buffer size must match slice dimensions (Width * Height).");

        ValidateCoordinates(0, 0, z);

        var cz = z / ChunkDim;
        var lz = z % ChunkDim;

        Parallel.For(0, ChunkCountY, cy =>
        {
            for (var cx = 0; cx < ChunkCountX; cx++)
            {
                var chunkIndex = GetChunkIndex(cx, cy, cz);

                var xStart = cx * ChunkDim;
                var yStart = cy * ChunkDim;
                var xEnd = Math.Min(xStart + ChunkDim, Width);
                var yEnd = Math.Min(yStart + ChunkDim, Height);

                for (var y = yStart; y < yEnd; y++)
                {
                    var ly = y % ChunkDim;
                    var srcOffset = y * Width + xStart;
                    var dstOffsetInChunk = lz * ChunkDim * ChunkDim + ly * ChunkDim;
                    var length = xEnd - xStart;

                    if (_useMemoryMapping)
                    {
                        var chunkStartInFile = CalculateGlobalOffset(chunkIndex, 0);
                        _viewAccessor.WriteArray(chunkStartInFile + dstOffsetInChunk, buffer, srcOffset, length);
                    }
                    else
                    {
                        Array.Copy(buffer, srcOffset, _chunks[chunkIndex], dstOffsetInChunk, length);
                    }
                }
            }
        });
    }

    /// <summary>
    ///     Saves the label volume to a binary file with a complete header.
    /// </summary>
    public void SaveAsBin(string path)
    {
        try
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                // Write a complete and unambiguous header
                bw.Write(Width);
                bw.Write(Height);
                bw.Write(Depth);
                bw.Write(ChunkDim);
                bw.Write(ChunkCountX);
                bw.Write(ChunkCountY);
                bw.Write(ChunkCountZ);

                // Write chunks
                var chunkSize = ChunkDim * ChunkDim * ChunkDim;
                var totalChunks = ChunkCountX * ChunkCountY * ChunkCountZ;

                if (!_useMemoryMapping)
                {
                    for (var i = 0; i < _chunks.Length; i++) bw.Write(_chunks[i]);
                }
                else
                {
                    // For memory-mapped files, read from the accessor and write to the stream
                    long dataOffset = 28; // New header size
                    var dataLength = (long)totalChunks * chunkSize;
                    var buffer = new byte[Math.Min(dataLength, 1024 * 1024)]; // Use a 1MB buffer
                    for (long i = 0; i < dataLength; i += buffer.Length)
                    {
                        var toRead = (int)Math.Min(buffer.Length, dataLength - i);
                        _viewAccessor.ReadArray(dataOffset + i, buffer, 0, toRead);
                        bw.Write(buffer, 0, toRead);
                    }
                }
            }

            Logger.Log($"[ChunkedLabelVolume] Saved label volume to {path}");
        }
        catch (Exception ex)
        {
            Logger.Log($"[ChunkedLabelVolume] Failed to save: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    ///     Loads label volume from a binary file, reading the complete header.
    /// </summary>
    public static ChunkedLabelVolume LoadFromBin(string path, bool useMemoryMapping)
    {
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var br = new BinaryReader(fs))
        {
            // Read the complete header
            var width = br.ReadInt32();
            var height = br.ReadInt32();
            var depth = br.ReadInt32();
            var chunkDim = br.ReadInt32();
            // We can ignore the saved chunk counts as they can be recalculated
            br.ReadInt32(); // cntX
            br.ReadInt32(); // cntY
            br.ReadInt32(); // cntZ

            var volume = new ChunkedLabelVolume(width, height, depth, chunkDim, useMemoryMapping, path);

            // Read chunks if not memory mapping
            if (!useMemoryMapping)
            {
                var chunkSize = chunkDim * chunkDim * chunkDim;
                var totalChunks = volume.ChunkCountX * volume.ChunkCountY * volume.ChunkCountZ;

                for (var i = 0; i < totalChunks; i++)
                {
                    var bytesRead = br.Read(volume._chunks[i], 0, chunkSize);
                    if (bytesRead < chunkSize && fs.Position != fs.Length)
                        Logger.LogWarning(
                            $"[ChunkedLabelVolume] Read fewer bytes than expected for chunk {i}. The file might be corrupt.");
                }
            }

            // For memory mapping, data is already accessible through the file.
            // The constructor handles setting up the MMF.
            return volume;
        }
    }

    /// <summary>
    ///     Converts chunk coordinates to linear chunk index.
    /// </summary>
    public int GetChunkIndex(int cx, int cy, int cz)
    {
        if (cx < 0 || cx >= ChunkCountX || cy < 0 || cy >= ChunkCountY || cz < 0 || cz >= ChunkCountZ)
            throw new ArgumentOutOfRangeException($"Chunk coordinates ({cx},{cy},{cz}) out of range");

        return (cz * ChunkCountY + cy) * ChunkCountX + cx;
    }

    #endregion

    #region Private Methods

    private void InitializeMemoryMappedFile()
    {
        var totalChunks = ChunkCountX * ChunkCountY * ChunkCountZ;
        var chunkSize = (long)ChunkDim * ChunkDim * ChunkDim;
        var totalSize = HEADER_SIZE + totalChunks * chunkSize;

        try
        {
            // Create or overwrite the backing file
            using (var fs = new FileStream(FilePath, FileMode.Create, FileAccess.Write))
            {
                // Write header
                using (var bw = new BinaryWriter(fs))
                {
                    bw.Write(ChunkDim);
                    bw.Write(ChunkCountX);
                    bw.Write(ChunkCountY);
                    bw.Write(ChunkCountZ);
                }

                // Pre-allocate the file
                fs.SetLength(totalSize);
                fs.Flush(true);
            }

            Logger.Log($"[ChunkedLabelVolume] Created file '{FilePath}' with size {totalSize:N0} bytes.");

            // Open memory-mapped file
            _mmf = MemoryMappedFile.CreateFromFile(FilePath, FileMode.Open, null, 0, MemoryMappedFileAccess.ReadWrite);
            _viewAccessor = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
        }
        catch (Exception ex)
        {
            Logger.Log($"[ChunkedLabelVolume] Error creating memory-mapped file: {ex.Message}");
            throw;
        }
    }

    private void InitializeInMemoryStorage()
    {
        var totalChunks = ChunkCountX * ChunkCountY * ChunkCountZ;
        _chunks = new byte[totalChunks][];
        var chunkSize = ChunkDim * ChunkDim * ChunkDim;

        // Initialize all chunks with zeros
        Parallel.For(0, totalChunks, i => { _chunks[i] = new byte[chunkSize]; });

        Logger.Log($"[ChunkedLabelVolume] Initialized {totalChunks} RAM chunks, each of {chunkSize} bytes.");
    }

    private (int chunkIndex, int offset) GetChunkIndexAndOffset(int x, int y, int z)
    {
        var cx = x / ChunkDim;
        var cy = y / ChunkDim;
        var cz = z / ChunkDim;

        var chunkIndex = GetChunkIndex(cx, cy, cz);

        var lx = x % ChunkDim;
        var ly = y % ChunkDim;
        var lz = z % ChunkDim;

        var offset = lz * ChunkDim * ChunkDim + ly * ChunkDim + lx;

        return (chunkIndex, offset);
    }

    private long CalculateGlobalOffset(int chunkIndex, int localOffset)
    {
        var chunkSize = (long)ChunkDim * ChunkDim * ChunkDim;
        return HEADER_SIZE + chunkIndex * chunkSize + localOffset;
    }

    private static void ValidateDimensions(int width, int height, int depth, int chunkDim)
    {
        if (width <= 0)
            throw new ArgumentException("Width must be positive", nameof(width));
        if (height <= 0)
            throw new ArgumentException("Height must be positive", nameof(height));
        if (depth <= 0)
            throw new ArgumentException("Depth must be positive", nameof(depth));
        if (chunkDim <= 0)
            throw new ArgumentException("Chunk dimension must be positive", nameof(chunkDim));
    }

    private void ValidateCoordinates(int x, int y, int z)
    {
        if (x < 0 || x >= Width)
            throw new ArgumentOutOfRangeException(nameof(x),
                $"X coordinate {x} is outside valid range [0,{Width - 1}]");
        if (y < 0 || y >= Height)
            throw new ArgumentOutOfRangeException(nameof(y),
                $"Y coordinate {y} is outside valid range [0,{Height - 1}]");
        if (z < 0 || z >= Depth)
            throw new ArgumentOutOfRangeException(nameof(z),
                $"Z coordinate {z} is outside valid range [0,{Depth - 1}]");
    }

    #endregion

    #region IDisposable Implementation

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                Logger.Log("[ChunkedLabelVolume] Disposing resources");
                _viewAccessor?.Dispose();
                _mmf?.Dispose();
                _chunks = null;
            }

            _disposed = true;
        }
    }

    ~ChunkedLabelVolume()
    {
        Dispose(false);
    }

    #endregion
}