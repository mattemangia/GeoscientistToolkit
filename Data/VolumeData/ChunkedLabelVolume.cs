// GAIA/Data/VolumeData/ChunkedLabelVolume.cs

using System.IO.MemoryMappedFiles;
using System.Collections.Concurrent;
using System.Buffers;
using GAIA.Data.CtImageStack;
using GAIA.Util;

namespace GAIA.Data.VolumeData;

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
    private const int HEADER_SIZE = 28; // Width, height, depth, chunk dim and three chunk counts
    private readonly ConcurrentDictionary<int, byte> _dirtyChunks = new();
    private volatile VirtualThresholdLabelRule[] _virtualRules = Array.Empty<VirtualThresholdLabelRule>();
    private IGrayscaleVolumeData _ruleGrayscale;
    public int DirtyChunkCount => _dirtyChunks.Count;
    public int AllocatedChunkCount => _useMemoryMapping ? ChunkCountX * ChunkCountY * ChunkCountZ :
        _chunks?.Count(chunk => chunk != null) ?? 0;
    public bool IsMemoryMapped => _useMemoryMapping;

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
    public ChunkedLabelVolume(int width, int height, int depth, int chunkDim, MemoryMappedFile mmf,
        string filePath = null)
    {
        ValidateDimensions(width, height, depth, chunkDim);

        Width = width;
        Height = height;
        Depth = depth;
        ChunkDim = chunkDim;
        _useMemoryMapping = true;
        FilePath = filePath;

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
                    return ApplyVirtualRules(_viewAccessor.ReadByte(globalOffset), x, y, z);
                }

                return ApplyVirtualRules(_chunks[chunkIndex]?[offset] ?? 0, x, y, z);
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
                    var chunk = _chunks[chunkIndex];
                    if (chunk == null)
                    {
                        if (value == 0) return;
                        var created = new byte[ChunkDim * ChunkDim * ChunkDim];
                        chunk = Interlocked.CompareExchange(ref _chunks[chunkIndex], created, null) ?? created;
                    }
                    if (chunk[offset] == value) return;
                    chunk[offset] = value;
                    _dirtyChunks[chunkIndex] = 0;
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
        if (buffer == null || buffer.Length < Width * Height)
            throw new ArgumentException("Buffer size must match slice dimensions (Width * Height).");

        ValidateCoordinates(0, 0, z);
        Array.Clear(buffer);

        var cz = z / ChunkDim;
        var lz = z % ChunkDim;
        var planeSize = ChunkDim * ChunkDim;
        var mappedPlane = _useMemoryMapping ? ArrayPool<byte>.Shared.Rent(planeSize) : null;

        try
        {
        for (var cy = 0; cy < ChunkCountY; cy++)
        {
            for (var cx = 0; cx < ChunkCountX; cx++)
            {
                var chunkIndex = GetChunkIndex(cx, cy, cz);

                var xStart = cx * ChunkDim;
                var yStart = cy * ChunkDim;
                var xEnd = Math.Min(xStart + ChunkDim, Width);
                var yEnd = Math.Min(yStart + ChunkDim, Height);

                if (_useMemoryMapping)
                {
                    _viewAccessor.ReadArray(CalculateGlobalOffset(chunkIndex, lz * planeSize),
                        mappedPlane, 0, planeSize);
                }
                var chunk = _useMemoryMapping ? mappedPlane : _chunks[chunkIndex];
                if (chunk == null) continue;
                var planeBase = _useMemoryMapping ? 0 : lz * planeSize;
                for (var y = yStart; y < yEnd; y++)
                    Array.Copy(chunk, planeBase + (y - yStart) * ChunkDim, buffer,
                        y * Width + xStart, xEnd - xStart);
            }
        }
        }
        finally { if (mappedPlane != null) ArrayPool<byte>.Shared.Return(mappedPlane); }
        ApplyVirtualRules(z, buffer);
    }

    public void SetVirtualThresholdRules(IGrayscaleVolumeData grayscale,
        IEnumerable<VirtualThresholdLabelRule> rules)
    {
        _ruleGrayscale = grayscale;
        _virtualRules = rules?.ToArray() ?? Array.Empty<VirtualThresholdLabelRule>();
    }

    private byte ApplyVirtualRules(byte stored, int x, int y, int z)
    {
        var rules = _virtualRules;
        if (rules.Length == 0 || _ruleGrayscale == null) return stored;
        var density = _ruleGrayscale[x, y, z];
        var value = stored;
        foreach (var rule in rules)
            if (density >= rule.Min && density <= rule.Max && (rule.Add || value == rule.MaterialId))
                value = rule.Add ? rule.MaterialId : (byte)0;
        return value;
    }

    private void ApplyVirtualRules(int z, byte[] labels)
    {
        var rules = _virtualRules;
        if (rules.Length == 0 || _ruleGrayscale == null) return;
        var length = checked(Width * Height);
        var grayscale = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            _ruleGrayscale.ReadSliceZ(z, grayscale);
            foreach (var rule in rules)
                for (var i = 0; i < length; i++)
                    if (grayscale[i] >= rule.Min && grayscale[i] <= rule.Max &&
                        (rule.Add || labels[i] == rule.MaterialId))
                        labels[i] = rule.Add ? rule.MaterialId : (byte)0;
        }
        finally { ArrayPool<byte>.Shared.Return(grayscale); }
    }

    public unsafe void ReadSliceXZ(int y, byte[] destination)
    {
        if (y < 0 || y >= Height || destination == null || destination.Length < Width * Depth)
            throw new ArgumentException("Invalid XZ slice buffer.");
        byte* pointer = null;
        if (_useMemoryMapping) _viewAccessor.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
        try
        {
            if (pointer != null) pointer += _viewAccessor.PointerOffset;
            var cy = y / ChunkDim; var localY = y % ChunkDim;
            for (var z = 0; z < Depth; z++)
            {
                var cz = z / ChunkDim; var localZ = z % ChunkDim;
                for (var cx = 0; cx < ChunkCountX; cx++)
                {
                    var xStart = cx * ChunkDim; var length = Math.Min(ChunkDim, Width - xStart);
                    var chunkIndex = GetChunkIndex(cx, cy, cz);
                    var localOffset = localZ * ChunkDim * ChunkDim + localY * ChunkDim;
                    if (pointer != null)
                        new ReadOnlySpan<byte>(pointer + CalculateGlobalOffset(chunkIndex, localOffset), length)
                            .CopyTo(destination.AsSpan(z * Width + xStart, length));
                    else
                    {
                        var chunk = _chunks[chunkIndex];
                        if (chunk != null) Array.Copy(chunk, localOffset, destination, z * Width + xStart, length);
                    }
                }
            }
        }
        finally { if (_useMemoryMapping) _viewAccessor.SafeMemoryMappedViewHandle.ReleasePointer(); }
        ApplyVirtualRulesOrthogonal(1, y, destination);
    }

    public unsafe void ReadSliceYZ(int x, byte[] destination)
    {
        if (x < 0 || x >= Width || destination == null || destination.Length < Height * Depth)
            throw new ArgumentException("Invalid YZ slice buffer.");
        byte* pointer = null;
        if (_useMemoryMapping) _viewAccessor.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
        try
        {
            if (pointer != null) pointer += _viewAccessor.PointerOffset;
            var cx = x / ChunkDim; var localX = x % ChunkDim;
            for (var z = 0; z < Depth; z++)
            for (var y = 0; y < Height; y++)
            {
                var cz = z / ChunkDim; var cy = y / ChunkDim;
                var localOffset = (z % ChunkDim) * ChunkDim * ChunkDim + (y % ChunkDim) * ChunkDim + localX;
                var chunkIndex = GetChunkIndex(cx, cy, cz);
                destination[z * Height + y] = pointer != null
                    ? *(pointer + CalculateGlobalOffset(chunkIndex, localOffset))
                    : _chunks[chunkIndex]?[localOffset] ?? 0;
            }
        }
        finally { if (_useMemoryMapping) _viewAccessor.SafeMemoryMappedViewHandle.ReleasePointer(); }
        ApplyVirtualRulesOrthogonal(2, x, destination);
    }

    private void ApplyVirtualRulesOrthogonal(int view, int slice, byte[] labels)
    {
        var rules = _virtualRules;
        if (rules.Length == 0 || _ruleGrayscale is not ChunkedVolume grayscaleVolume) return;
        var length = view == 1 ? Width * Depth : Height * Depth;
        var grayscale = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            if (view == 1) grayscaleVolume.ReadSliceXZ(slice, grayscale);
            else grayscaleVolume.ReadSliceYZ(slice, grayscale);
            foreach (var rule in rules)
                for (var i = 0; i < length; i++)
                    if (grayscale[i] >= rule.Min && grayscale[i] <= rule.Max &&
                        (rule.Add || labels[i] == rule.MaterialId))
                        labels[i] = rule.Add ? rule.MaterialId : (byte)0;
        }
        finally { ArrayPool<byte>.Shared.Return(grayscale); }
    }

    /// <summary>Writes explicitly changed chunk planes using one contiguous MMF call per chunk.</summary>
    public void WriteSliceZChangedChunks(int z, byte[] buffer, bool[] changedChunks)
    {
        if (buffer == null || buffer.Length < Width * Height) throw new ArgumentException("Invalid slice buffer.");
        if (changedChunks == null || changedChunks.Length < ChunkCountX * ChunkCountY)
            throw new ArgumentException("Changed-chunk map is too small.", nameof(changedChunks));
        ValidateCoordinates(0, 0, z);
        var cz = z / ChunkDim; var lz = z % ChunkDim; var planeSize = ChunkDim * ChunkDim;
        var plane = _useMemoryMapping ? ArrayPool<byte>.Shared.Rent(planeSize) : null;
        try
        {
            for (var cy = 0; cy < ChunkCountY; cy++)
            for (var cx = 0; cx < ChunkCountX; cx++)
            {
                if (!changedChunks[cy * ChunkCountX + cx]) continue;
                var chunkIndex = GetChunkIndex(cx, cy, cz);
                var xStart = cx * ChunkDim; var yStart = cy * ChunkDim;
                var xEnd = Math.Min(xStart + ChunkDim, Width);
                var yEnd = Math.Min(yStart + ChunkDim, Height);
                if (_useMemoryMapping)
                {
                    plane.AsSpan(0, planeSize).Clear();
                    for (var y = yStart; y < yEnd; y++)
                        buffer.AsSpan(y * Width + xStart, xEnd - xStart)
                            .CopyTo(plane.AsSpan((y - yStart) * ChunkDim));
                    _viewAccessor.WriteArray(CalculateGlobalOffset(chunkIndex, lz * planeSize), plane, 0, planeSize);
                }
                else
                {
                    var chunk = _chunks[chunkIndex] ??= new byte[ChunkDim * ChunkDim * ChunkDim];
                    for (var y = yStart; y < yEnd; y++)
                        Array.Copy(buffer, y * Width + xStart, chunk,
                            lz * planeSize + (y - yStart) * ChunkDim, xEnd - xStart);
                    _dirtyChunks[chunkIndex] = 0;
                }
            }
        }
        finally { if (plane != null) ArrayPool<byte>.Shared.Return(plane); }
    }

    /// <summary>
    ///     Writes an entire Z-slice from the provided buffer efficiently.
    /// </summary>
    public void WriteSliceZ(int z, byte[] buffer)
    {
        if (buffer == null || buffer.Length < Width * Height)
            throw new ArgumentException("Buffer size must match slice dimensions (Width * Height).");

        ValidateCoordinates(0, 0, z);

        var cz = z / ChunkDim;
        var lz = z % ChunkDim;

        var planeSize = ChunkDim * ChunkDim;
        var mappedPlane = _useMemoryMapping ? ArrayPool<byte>.Shared.Rent(planeSize) : null;
        try
        {
        for (var cy = 0; cy < ChunkCountY; cy++)
        {
            for (var cx = 0; cx < ChunkCountX; cx++)
            {
                var chunkIndex = GetChunkIndex(cx, cy, cz);
                var chunkModified = false;

                var xStart = cx * ChunkDim;
                var yStart = cy * ChunkDim;
                var xEnd = Math.Min(xStart + ChunkDim, Width);
                var yEnd = Math.Min(yStart + ChunkDim, Height);

                byte[] chunk;
                var planeBase = lz * planeSize;
                if (_useMemoryMapping)
                {
                    _viewAccessor.ReadArray(CalculateGlobalOffset(chunkIndex, planeBase),
                        mappedPlane, 0, planeSize);
                    chunk = mappedPlane;
                    planeBase = 0;
                }
                else
                    chunk = _chunks[chunkIndex];

                for (var y = yStart; y < yEnd; y++)
                {
                    var srcOffset = y * Width + xStart;
                    var dstOffsetInChunk = planeBase + (y - yStart) * ChunkDim;
                    var length = xEnd - xStart;

                    if (chunk == null)
                    {
                        if (buffer.AsSpan(srcOffset, length).IndexOfAnyExcept((byte)0) < 0) continue;
                        var created = new byte[ChunkDim * ChunkDim * ChunkDim];
                        chunk = Interlocked.CompareExchange(ref _chunks[chunkIndex], created, null) ?? created;
                    }
                    if (!chunk.AsSpan(dstOffsetInChunk, length).SequenceEqual(buffer.AsSpan(srcOffset, length)))
                    {
                        Array.Copy(buffer, srcOffset, chunk, dstOffsetInChunk, length);
                        chunkModified = true;
                    }
                }
                if (chunkModified)
                {
                    if (_useMemoryMapping)
                        _viewAccessor.WriteArray(CalculateGlobalOffset(chunkIndex, lz * planeSize),
                            mappedPlane, 0, planeSize);
                    else _dirtyChunks[chunkIndex] = 0;
                }
            }
        }
        }
        finally
        {
            if (mappedPlane != null) ArrayPool<byte>.Shared.Return(mappedPlane);
        }
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
                    var zeroChunk = new byte[chunkSize];
                    for (var i = 0; i < _chunks.Length; i++) bw.Write(_chunks[i] ?? zeroChunk);
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
            _dirtyChunks.Clear();
        }
        catch (Exception ex)
        {
            Logger.Log($"[ChunkedLabelVolume] Failed to save: {ex.Message}");
            throw;
        }
    }

    /// <summary>Persists only chunks changed since the previous flush.</summary>
    public void FlushDirtyChunks(string path, CancellationToken cancellationToken = default,
        IProgress<float> progress = null)
    {
        if (_useMemoryMapping)
        {
            _viewAccessor?.Flush();
            _dirtyChunks.Clear();
            progress?.Report(1f);
            return;
        }
        if (string.IsNullOrWhiteSpace(path) || _dirtyChunks.IsEmpty) return;

        var chunkSize = checked(ChunkDim * ChunkDim * ChunkDim);
        var totalChunks = ChunkCountX * ChunkCountY * ChunkCountZ;
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var isNew = !File.Exists(path);
        using var stream = new FileStream(path, isNew ? FileMode.CreateNew : FileMode.Open,
            FileAccess.ReadWrite, FileShare.Read, 4 * 1024 * 1024, FileOptions.SequentialScan);
        if (isNew || stream.Length < HEADER_SIZE)
        {
            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
            writer.Write(Width); writer.Write(Height); writer.Write(Depth); writer.Write(ChunkDim);
            writer.Write(ChunkCountX); writer.Write(ChunkCountY); writer.Write(ChunkCountZ);
            stream.SetLength(HEADER_SIZE + (long)totalChunks * chunkSize);
        }

        var dirtyChunks = _dirtyChunks.Keys.OrderBy(index => index).ToArray();
        // Aggregate adjacent chunks into multi-megabyte sequential writes. A syscall/seek per
        // chunk is extremely expensive on large volumes, especially when thresholding changes
        // most of the dataset.
        var chunksPerBatch = Math.Max(1, (8 * 1024 * 1024) / chunkSize);
        var batchBuffer = ArrayPool<byte>.Shared.Rent(checked(chunksPerBatch * chunkSize));
        try
        {
            var dirtyIndex = 0;
            while (dirtyIndex < dirtyChunks.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var firstChunk = dirtyChunks[dirtyIndex];
                var batchCount = 1;
                while (batchCount < chunksPerBatch && dirtyIndex + batchCount < dirtyChunks.Length &&
                       dirtyChunks[dirtyIndex + batchCount] == firstChunk + batchCount)
                    batchCount++;

                for (var batchIndex = 0; batchIndex < batchCount; batchIndex++)
                {
                    var chunkIndex = firstChunk + batchIndex;
                    var destination = batchBuffer.AsSpan(batchIndex * chunkSize, chunkSize);
                    var chunk = _chunks[chunkIndex];
                    if (chunk == null) destination.Clear();
                    else chunk.AsSpan().CopyTo(destination);
                }

                stream.Position = HEADER_SIZE + (long)firstChunk * chunkSize;
                stream.Write(batchBuffer, 0, batchCount * chunkSize);
                for (var batchIndex = 0; batchIndex < batchCount; batchIndex++)
                    _dirtyChunks.TryRemove(firstChunk + batchIndex, out _);

                dirtyIndex += batchCount;
                progress?.Report(dirtyIndex / (float)dirtyChunks.Length);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(batchBuffer); }
        stream.Flush(false);
        Logger.Log($"[ChunkedLabelVolume] Flushed modified chunks to {path}");
    }

    public void Clear()
    {
        if (_useMemoryMapping)
        {
            var zero = new byte[1024 * 1024];
            var remaining = (long)ChunkCountX * ChunkCountY * ChunkCountZ * ChunkDim * ChunkDim * ChunkDim;
            var offset = (long)HEADER_SIZE;
            while (remaining > 0)
            {
                var count = (int)Math.Min(zero.Length, remaining);
                _viewAccessor.WriteArray(offset, zero, 0, count);
                offset += count; remaining -= count;
            }
            _viewAccessor.Flush();
            return;
        }
        for (var i = 0; i < _chunks.Length; i++)
        {
            if (_chunks[i] == null) continue;
            _chunks[i] = null;
            _dirtyChunks[i] = 0;
        }
    }

    /// <summary>
    ///     Loads label volume from a binary file, reading the complete header.
    /// </summary>
    public static ChunkedLabelVolume LoadFromBin(string path, bool useMemoryMapping)
    {
        int width, height, depth, chunkDim;
        using (var headerStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var headerReader = new BinaryReader(headerStream))
        {
            width = headerReader.ReadInt32(); height = headerReader.ReadInt32();
            depth = headerReader.ReadInt32(); chunkDim = headerReader.ReadInt32();
            headerReader.ReadInt32(); headerReader.ReadInt32(); headerReader.ReadInt32();
        }

        if (useMemoryMapping)
        {
            // Open the existing file directly. The create-from-scratch constructor deliberately
            // truncates its target and must never be used by a load path.
            var mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0,
                MemoryMappedFileAccess.ReadWrite);
            return new ChunkedLabelVolume(width, height, depth, chunkDim, mmf, path);
        }

        var volume = new ChunkedLabelVolume(width, height, depth, chunkDim, false, path);
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var br = new BinaryReader(fs))
        {
            fs.Position = HEADER_SIZE;
            var chunkSize = chunkDim * chunkDim * chunkDim;
            var totalChunks = volume.ChunkCountX * volume.ChunkCountY * volume.ChunkCountZ;

            var chunkBuffer = new byte[chunkSize];
            for (var i = 0; i < totalChunks; i++)
            {
                Array.Clear(chunkBuffer);
                var bytesRead = br.Read(chunkBuffer, 0, chunkSize);
                if (chunkBuffer.AsSpan(0, bytesRead).IndexOfAnyExcept((byte)0) >= 0)
                    volume._chunks[i] = (byte[])chunkBuffer.Clone();
                if (bytesRead < chunkSize && fs.Position != fs.Length)
                    Logger.LogWarning(
                        $"[ChunkedLabelVolume] Read fewer bytes than expected for chunk {i}. The file might be corrupt.");
            }
        }
        return volume;
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
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? ".");
            // Create or overwrite the backing file
            using (var fs = new FileStream(FilePath, FileMode.Create, FileAccess.Write))
            {
                // Write header
                using (var bw = new BinaryWriter(fs, System.Text.Encoding.UTF8, true))
                {
                    bw.Write(Width);
                    bw.Write(Height);
                    bw.Write(Depth);
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
        Logger.Log($"[ChunkedLabelVolume] Initialized sparse storage for {totalChunks} chunks.");
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
