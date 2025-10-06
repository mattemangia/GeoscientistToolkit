// GeoscientistToolkit/Data/VolumeData/LazyChunkedVolume.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.VolumeData
{
    /// <summary>
    /// A lazy-loading wrapper around ChunkedVolume that loads chunks on demand.
    /// Maintains a cache of recently used chunks to balance memory usage and performance.
    /// </summary>
    public class LazyChunkedVolume : IGrayscaleVolumeData
    {
        private string _filePath;
        private readonly Dictionary<int, byte[]> _chunkCache;
        private readonly LinkedList<int> _lruList; // For LRU cache eviction
        private readonly object _cacheLock = new object();
        private const int MAX_CACHED_CHUNKS = 32; // Adjust based on available memory
        
        // Volume metadata (loaded from file header)
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Depth { get; private set; }
        public int ChunkDim { get; private set; }
        public double PixelSize { get; private set; }
        
        private int _chunkCountX;
        private int _chunkCountY;
        private int _chunkCountZ;
        private int _chunkSize;
        private long _headerSize;
        private FileStream _fileStream;
        private BinaryReader _reader;
        private bool _disposed;

        /// <summary>
        /// Creates a lazy-loading volume from a file path.
        /// Only reads the header initially, chunks are loaded on demand.
        /// </summary>
        public static async Task<LazyChunkedVolume> CreateAsync(string filePath, IProgress<float> progress = null)
        {
            progress?.Report(0.0f);
            var volume = new LazyChunkedVolume();
            await volume.InitializeAsync(filePath);
            progress?.Report(1.0f);
            return volume;
        }

        private LazyChunkedVolume()
        {
            _chunkCache = new Dictionary<int, byte[]>();
            _lruList = new LinkedList<int>();
        }

        private async Task InitializeAsync(string filePath)
        {
            _filePath = filePath;
            
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Volume file not found: {filePath}");
            
            // Open file stream for reading
            _fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            _reader = new BinaryReader(_fileStream);
            
            // Read header
            await Task.Run(() =>
            {
                Width = _reader.ReadInt32();
                Height = _reader.ReadInt32();
                Depth = _reader.ReadInt32();
                ChunkDim = _reader.ReadInt32();
                int bitsPerPixel = _reader.ReadInt32();
                PixelSize = _reader.ReadDouble();
                _chunkCountX = _reader.ReadInt32();
                _chunkCountY = _reader.ReadInt32();
                _chunkCountZ = _reader.ReadInt32();
                
                _chunkSize = ChunkDim * ChunkDim * ChunkDim;
                _headerSize = _fileStream.Position;
            });
            
            Logger.Log($"[LazyChunkedVolume] Initialized lazy volume from {Path.GetFileName(filePath)}: {Width}x{Height}x{Depth}");
        }

        /// <summary>
        /// Indexer for accessing voxel data. Loads chunks on demand.
        /// </summary>
        public byte this[int x, int y, int z]
        {
            get
            {
                if (x < 0 || x >= Width || y < 0 || y >= Height || z < 0 || z >= Depth)
                    return 0;
                
                var (chunkIndex, offset) = CalculateChunkIndexAndOffset(x, y, z);
                var chunk = GetOrLoadChunk(chunkIndex);
                return chunk?[offset] ?? 0;
            }
            set
            {
                throw new NotSupportedException("Lazy volumes are read-only");
            }
        }

        /// <summary>
        /// Reads a complete Z-slice efficiently, loading only necessary chunks.
        /// </summary>
        public void ReadSliceZ(int z, byte[] buffer)
        {
            if (buffer == null || buffer.Length != Width * Height)
                throw new ArgumentException("Buffer size must match slice dimensions");
            
            if (z < 0 || z >= Depth)
                throw new ArgumentOutOfRangeException(nameof(z));
            
            int cz = z / ChunkDim;
            int lz = z % ChunkDim;
            
            // Pre-load all chunks needed for this slice
            var chunksNeeded = new List<int>();
            for (int cy = 0; cy < _chunkCountY; cy++)
            {
                for (int cx = 0; cx < _chunkCountX; cx++)
                {
                    chunksNeeded.Add(GetChunkIndex(cx, cy, cz));
                }
            }
            
            // Load chunks in parallel for better performance
            Parallel.ForEach(chunksNeeded, chunkIndex =>
            {
                GetOrLoadChunk(chunkIndex);
            });
            
            // Now extract the slice data
            for (int cy = 0; cy < _chunkCountY; cy++)
            {
                for (int cx = 0; cx < _chunkCountX; cx++)
                {
                    int chunkIndex = GetChunkIndex(cx, cy, cz);
                    var chunk = GetOrLoadChunk(chunkIndex);
                    
                    if (chunk == null) continue;
                    
                    int xStart = cx * ChunkDim;
                    int yStart = cy * ChunkDim;
                    int xEnd = Math.Min(xStart + ChunkDim, Width);
                    int yEnd = Math.Min(yStart + ChunkDim, Height);
                    
                    for (int y = yStart; y < yEnd; y++)
                    {
                        int ly = y % ChunkDim;
                        int srcOffset = (lz * ChunkDim * ChunkDim) + (ly * ChunkDim);
                        int dstOffset = y * Width + xStart;
                        int length = xEnd - xStart;
                        
                        Array.Copy(chunk, srcOffset, buffer, dstOffset, length);
                    }
                }
            }
        }

        public void WriteSliceZ(int z, byte[] data)
        {
            throw new NotSupportedException("Lazy volumes are read-only");
        }

        /// <summary>
        /// Gets a chunk from cache or loads it from disk.
        /// </summary>
        private byte[] GetOrLoadChunk(int chunkIndex)
        {
            lock (_cacheLock)
            {
                // Check if chunk is already in cache
                if (_chunkCache.TryGetValue(chunkIndex, out var cachedChunk))
                {
                    // Move to front of LRU list
                    _lruList.Remove(chunkIndex);
                    _lruList.AddFirst(chunkIndex);
                    return cachedChunk;
                }
                
                // Load chunk from file
                byte[] chunk = LoadChunkFromFile(chunkIndex);
                
                if (chunk != null)
                {
                    // Add to cache
                    _chunkCache[chunkIndex] = chunk;
                    _lruList.AddFirst(chunkIndex);
                    
                    // Evict oldest chunks if cache is too large
                    while (_lruList.Count > MAX_CACHED_CHUNKS)
                    {
                        var oldestChunk = _lruList.Last.Value;
                        _lruList.RemoveLast();
                        _chunkCache.Remove(oldestChunk);
                    }
                }
                
                return chunk;
            }
        }

        /// <summary>
        /// Loads a single chunk from the file.
        /// </summary>
        private byte[] LoadChunkFromFile(int chunkIndex)
        {
            try
            {
                long offset = _headerSize + (long)chunkIndex * _chunkSize;
                
                lock (_reader)
                {
                    _fileStream.Seek(offset, SeekOrigin.Begin);
                    return _reader.ReadBytes(_chunkSize);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[LazyChunkedVolume] Failed to load chunk {chunkIndex}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Pre-loads chunks for a specific region to improve performance.
        /// </summary>
        public async Task PreloadRegionAsync(int xMin, int xMax, int yMin, int yMax, int zMin, int zMax, IProgress<float> progress = null)
        {
            await Task.Run(() =>
            {
                int cxMin = xMin / ChunkDim;
                int cxMax = xMax / ChunkDim;
                int cyMin = yMin / ChunkDim;
                int cyMax = yMax / ChunkDim;
                int czMin = zMin / ChunkDim;
                int czMax = zMax / ChunkDim;
                
                var chunks = new List<int>();
                for (int cz = czMin; cz <= czMax && cz < _chunkCountZ; cz++)
                {
                    for (int cy = cyMin; cy <= cyMax && cy < _chunkCountY; cy++)
                    {
                        for (int cx = cxMin; cx <= cxMax && cx < _chunkCountX; cx++)
                        {
                            chunks.Add(GetChunkIndex(cx, cy, cz));
                        }
                    }
                }
                
                int loaded = 0;
                Parallel.ForEach(chunks, chunkIndex =>
                {
                    GetOrLoadChunk(chunkIndex);
                    var current = System.Threading.Interlocked.Increment(ref loaded);
                    progress?.Report((float)current / chunks.Count);
                });
            });
        }

        /// <summary>
        /// Clears the chunk cache to free memory.
        /// </summary>
        public void ClearCache()
        {
            lock (_cacheLock)
            {
                _chunkCache.Clear();
                _lruList.Clear();
            }
        }

        private (int chunkIndex, int offset) CalculateChunkIndexAndOffset(int x, int y, int z)
        {
            int cx = x / ChunkDim;
            int cy = y / ChunkDim;
            int cz = z / ChunkDim;
            
            int chunkIndex = GetChunkIndex(cx, cy, cz);
            
            int lx = x % ChunkDim;
            int ly = y % ChunkDim;
            int lz = z % ChunkDim;
            
            int offset = (lz * ChunkDim * ChunkDim) + (ly * ChunkDim) + lx;
            
            return (chunkIndex, offset);
        }

        private int GetChunkIndex(int cx, int cy, int cz)
        {
            return (cz * _chunkCountY + cy) * _chunkCountX + cx;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _reader?.Dispose();
                _fileStream?.Dispose();
                _chunkCache?.Clear();
                _lruList?.Clear();
                _disposed = true;
            }
        }
    }
}