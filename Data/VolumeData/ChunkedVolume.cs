// GeoscientistToolkit/Data/VolumeData/ChunkedVolume.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Threading.Tasks;
using GeoscientistToolkit.Util;
using StbImageSharp;

namespace GeoscientistToolkit.Data.VolumeData
{
    /// <summary>
    /// Efficient storage for large 3D grayscale volumes using a chunked approach
    /// to overcome array limitations with support for 30GB+ datasets
    /// </summary>
    public class ChunkedVolume : IGrayscaleVolumeData
    {
        #region Fields and Properties
        // Volume dimensions
        public int Width { get; }
        public int Height { get; }
        public int Depth { get; }

        // Chunking parameters
        private readonly int _chunkDim;
        private readonly int _chunkCountX;
        private readonly int _chunkCountY;
        private readonly int _chunkCountZ;

        // Data storage
        private byte[][] _chunks; // For in-memory mode
        private long _headerSize;
        private MemoryMappedFile _mmf; // For memory-mapped mode
        private MemoryMappedViewAccessor _viewAccessor;
        private readonly bool _useMemoryMapping;

        // Metadata
        public double PixelSize { get; set; } = 1e-6; // Size in meters per pixel
        public int BitsPerPixel { get; } = 8;

        // Constants
        public const int DEFAULT_CHUNK_DIM = 256;
        private const int HEADER_SIZE = 40; // 4 ints + 1 int + 1 double + 3 ints

        // Properties for compatibility
        public int ChunkDim => _chunkDim;
        public int TotalChunks => _chunkCountX * _chunkCountY * _chunkCountZ;
        public byte[][] Chunks => _chunks;
        #endregion

        #region Constructors
        /// <summary>
        /// Creates a new in-memory volume with the specified dimensions
        /// </summary>
        public ChunkedVolume(int width, int height, int depth, int chunkDim = DEFAULT_CHUNK_DIM)
        {
            ValidateDimensions(width, height, depth, chunkDim);

            Width = width;
            Height = height;
            Depth = depth;
            _chunkDim = chunkDim;
            _useMemoryMapping = false;

            _chunkCountX = (width + chunkDim - 1) / chunkDim;
            _chunkCountY = (height + chunkDim - 1) / chunkDim;
            _chunkCountZ = (depth + chunkDim - 1) / chunkDim;

            _chunks = new byte[TotalChunks][];
            InitializeChunks();

            Logger.Log($"[ChunkedVolume] Created in-memory volume: {Width}x{Height}x{Depth}, chunkDim={_chunkDim}");
        }

        /// <summary>
        /// Creates a volume that uses memory-mapped file storage
        /// </summary>
        public ChunkedVolume(int width, int height, int depth, int chunkDim,
                            MemoryMappedFile mmf, MemoryMappedViewAccessor viewAccessor,
                            long headerSize = HEADER_SIZE)
        {
            ValidateDimensions(width, height, depth, chunkDim);

            Width = width;
            Height = height;
            Depth = depth;
            _chunkDim = chunkDim;
            _useMemoryMapping = true;

            _chunkCountX = (width + chunkDim - 1) / chunkDim;
            _chunkCountY = (height + chunkDim - 1) / chunkDim;
            _chunkCountZ = (depth + chunkDim - 1) / chunkDim;

            _mmf = mmf ?? throw new ArgumentNullException(nameof(mmf));
            _viewAccessor = viewAccessor ?? throw new ArgumentNullException(nameof(viewAccessor));
            _headerSize = headerSize;
            _chunks = null;

            Logger.Log($"[ChunkedVolume] Created memory-mapped volume: {Width}x{Height}x{Depth}, " +
                      $"chunkDim={_chunkDim}, headerSize={_headerSize}");
        }
        #endregion

        #region Public Interface
        /// <summary>
        /// Indexer for accessing voxel data
        /// </summary>
        public byte this[int x, int y, int z]
        {
            get
            {
                if (!IsValidCoordinate(x, y, z)) return 0;

                var (chunkIndex, offset) = CalculateChunkIndexAndOffset(x, y, z);

                try
                {
                    if (_useMemoryMapping)
                    {
                        long globalOffset = CalculateGlobalOffset(chunkIndex, offset);
                        return _viewAccessor.ReadByte(globalOffset);
                    }
                    else
                    {
                        return _chunks[chunkIndex]?[offset] ?? 0;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"[ChunkedVolume] Error reading voxel at ({x},{y},{z}): {ex.Message}");
                    return 0;
                }
            }
            set
            {
                if (!IsValidCoordinate(x, y, z)) return;

                var (chunkIndex, offset) = CalculateChunkIndexAndOffset(x, y, z);

                try
                {
                    if (_useMemoryMapping)
                    {
                        long globalOffset = CalculateGlobalOffset(chunkIndex, offset);
                        _viewAccessor.Write(globalOffset, value);
                    }
                    else
                    {
                        _chunks[chunkIndex][offset] = value;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"[ChunkedVolume] Error writing voxel at ({x},{y},{z}): {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Writes a complete Z-slice of data
        /// </summary>
        public void WriteSliceZ(int z, byte[] data)
        {
            if (z < 0 || z >= Depth || data == null || data.Length != Width * Height)
                throw new ArgumentException("Invalid slice index or data size");

            int index = 0;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    this[x, y, z] = data[index++];
                }
            }
        }

        /// <summary>
        /// Reads a complete Z-slice of data
        /// </summary>
        public void ReadSliceZ(int z, byte[] buffer)
        {
            if (z < 0 || z >= Depth || buffer == null || buffer.Length != Width * Height)
                throw new ArgumentException("Invalid slice index or buffer size");

            int index = 0;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    buffer[index++] = this[x, y, z];
                }
            }
        }

        /// <summary>
        /// Creates a volume from a folder of image slices
        /// </summary>
        public static async Task<ChunkedVolume> FromFolderAsync(string folder, int chunkDim, 
            bool useMemoryMapping, IProgress<float> progress = null, string datasetName = null)
        {
            Logger.Log($"[ChunkedVolume] Loading volume from folder: {folder}");

            // Get all image files
            var imageFiles = GetImageFiles(folder);
            if (imageFiles.Count == 0)
                throw new FileNotFoundException("No supported image files found in the folder.");

            // Sort numerically
            imageFiles = SortImagesNumerically(imageFiles);

            // Get dimensions from first image
            var (width, height) = await GetImageDimensionsAsync(imageFiles[0]);
            int depth = imageFiles.Count;

            Logger.Log($"[ChunkedVolume] Volume dimensions: {width}x{height}x{depth}");

            if (!useMemoryMapping)
            {
                // Create in-memory volume
                var volume = new ChunkedVolume(width, height, depth, chunkDim);
                await ProcessImagesParallelAsync(volume, imageFiles, progress);
                return volume;
            }
            else
            {
                // Create memory-mapped volume
                string volumeName = datasetName ?? Path.GetFileName(folder);
                string volumePath = Path.Combine(folder, $"{volumeName}.Volume.bin");
                
                // Calculate file size
                int cntX = (width + chunkDim - 1) / chunkDim;
                int cntY = (height + chunkDim - 1) / chunkDim;
                int cntZ = (depth + chunkDim - 1) / chunkDim;
                long chunkSize = (long)chunkDim * chunkDim * chunkDim;
                long totalSize = HEADER_SIZE + (cntX * cntY * cntZ * chunkSize);
                
                // Create the file
                using (var fs = new FileStream(volumePath, FileMode.Create, FileAccess.Write))
                {
                    fs.SetLength(totalSize);
                    
                    // Write header
                    using (var bw = new BinaryWriter(fs))
                    {
                        bw.Write(width);
                        bw.Write(height);
                        bw.Write(depth);
                        bw.Write(chunkDim);
                        bw.Write(8); // bits per pixel
                        bw.Write(1e-6); // default pixel size
                        bw.Write(cntX);
                        bw.Write(cntY);
                        bw.Write(cntZ);
                    }
                }
                
                // Create memory mapped file
                var mmf = MemoryMappedFile.CreateFromFile(volumePath, FileMode.Open, null, 0, MemoryMappedFileAccess.ReadWrite);
                var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
                
                var volume = new ChunkedVolume(width, height, depth, chunkDim, mmf, accessor, HEADER_SIZE);
                await ProcessImagesParallelAsync(volume, imageFiles, progress);
                
                return volume;
            }
        }

        /// <summary>
        /// Saves the volume to a binary file
        /// </summary>
        public async Task SaveAsBinAsync(string path)
        {
            Logger.Log($"[ChunkedVolume] Saving volume to: {path}");

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                WriteHeader(bw);
                await WriteChunksAsync(bw);
            }

            Logger.Log("[ChunkedVolume] Volume saved successfully");
        }

        /// <summary>
        /// Saves the volume synchronously (for compatibility)
        /// </summary>
        public void SaveAsBin(string path)
        {
            SaveAsBinAsync(path).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Loads a volume from a binary file
        /// </summary>
        public static async Task<ChunkedVolume> LoadFromBinAsync(string path, bool useMemoryMapping)
        {
            Logger.Log($"[ChunkedVolume] Loading volume from: {path}");

            if (!File.Exists(path))
                throw new FileNotFoundException($"Volume file not found: {path}");

            if (!useMemoryMapping)
            {
                // Load into memory
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (var br = new BinaryReader(fs))
                {
                    var (width, height, depth, chunkDim, pixelSize) = ReadHeader(br);
                    var volume = new ChunkedVolume(width, height, depth, chunkDim)
                    {
                        PixelSize = pixelSize
                    };
                    await volume.ReadChunksAsync(br);
                    return volume;
                }
            }
            else
            {
                // Create memory-mapped volume
                var mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.ReadWrite);
                var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
                
                // Read header
                int width = accessor.ReadInt32(0);
                int height = accessor.ReadInt32(4);
                int depth = accessor.ReadInt32(8);
                int chunkDim = accessor.ReadInt32(12);
                accessor.ReadInt32(16); // bits per pixel
                double pixelSize = accessor.ReadDouble(20);
                
                var volume = new ChunkedVolume(width, height, depth, chunkDim, mmf, accessor, HEADER_SIZE)
                {
                    PixelSize = pixelSize
                };
                
                return volume;
            }
        }

        /// <summary>
        /// Write chunks to binary writer
        /// </summary>
        public void WriteChunks(BinaryWriter bw)
        {
            WriteChunksAsync(bw).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Read chunks from binary reader
        /// </summary>
        public void ReadChunks(BinaryReader br)
        {
            ReadChunksAsync(br).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Fill the volume with a specific value
        /// </summary>
        public void Fill(byte value)
        {
            if (_useMemoryMapping)
            {
                Logger.Log("[ChunkedVolume] Fill operation not implemented for memory-mapped volumes");
                return;
            }

            int chunkSize = _chunkDim * _chunkDim * _chunkDim;
            byte[] template = new byte[chunkSize];
            
            if (value != 0)
            {
                for (int i = 0; i < chunkSize; i++)
                    template[i] = value;
            }

            Parallel.For(0, _chunks.Length, i =>
            {
                _chunks[i] = (byte[])template.Clone();
            });

            Logger.Log($"[ChunkedVolume] Filled volume with value: {value}");
        }

        /// <summary>
        /// Gets the index of a chunk from its coordinates
        /// </summary>
        public int GetChunkIndex(int cx, int cy, int cz)
        {
            return (cz * _chunkCountY + cy) * _chunkCountX + cx;
        }
        #endregion

        #region Private Methods
        private void InitializeChunks()
        {
            if (_chunks == null) return;

            int chunkSize = _chunkDim * _chunkDim * _chunkDim;
            
            Parallel.For(0, _chunks.Length, i =>
            {
                _chunks[i] = new byte[chunkSize];
            });
        }

        private bool IsValidCoordinate(int x, int y, int z)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height && z >= 0 && z < Depth;
        }

        private (int chunkIndex, int offset) CalculateChunkIndexAndOffset(int x, int y, int z)
        {
            int cx = x / _chunkDim;
            int cy = y / _chunkDim;
            int cz = z / _chunkDim;

            int chunkIndex = GetChunkIndex(cx, cy, cz);

            int lx = x % _chunkDim;
            int ly = y % _chunkDim;
            int lz = z % _chunkDim;

            int offset = (lz * _chunkDim * _chunkDim) + (ly * _chunkDim) + lx;

            return (chunkIndex, offset);
        }

        private long CalculateGlobalOffset(int chunkIndex, int localOffset)
        {
            long chunkSize = (long)_chunkDim * _chunkDim * _chunkDim;
            return _headerSize + (chunkIndex * chunkSize) + localOffset;
        }

        private void WriteHeader(BinaryWriter bw)
        {
            bw.Write(Width);
            bw.Write(Height);
            bw.Write(Depth);
            bw.Write(_chunkDim);
            bw.Write(BitsPerPixel);
            bw.Write(PixelSize);
            bw.Write(_chunkCountX);
            bw.Write(_chunkCountY);
            bw.Write(_chunkCountZ);
        }

        private static (int width, int height, int depth, int chunkDim, double pixelSize) ReadHeader(BinaryReader br)
        {
            int width = br.ReadInt32();
            int height = br.ReadInt32();
            int depth = br.ReadInt32();
            int chunkDim = br.ReadInt32();
            int bitsPerPixel = br.ReadInt32();
            double pixelSize = br.ReadDouble();
            int cntX = br.ReadInt32();
            int cntY = br.ReadInt32();
            int cntZ = br.ReadInt32();

            return (width, height, depth, chunkDim, pixelSize);
        }

        private async Task WriteChunksAsync(BinaryWriter bw)
        {
            int chunkSize = _chunkDim * _chunkDim * _chunkDim;

            if (!_useMemoryMapping)
            {
                foreach (var chunk in _chunks)
                {
                    await bw.BaseStream.WriteAsync(chunk, 0, chunkSize);
                }
            }
            else
            {
                byte[] buffer = new byte[chunkSize];
                for (int i = 0; i < TotalChunks; i++)
                {
                    long offset = CalculateGlobalOffset(i, 0);
                    _viewAccessor.ReadArray(offset, buffer, 0, chunkSize);
                    await bw.BaseStream.WriteAsync(buffer, 0, chunkSize);
                }
            }
        }

        private async Task ReadChunksAsync(BinaryReader br)
        {
            int chunkSize = _chunkDim * _chunkDim * _chunkDim;

            for (int i = 0; i < _chunks.Length; i++)
            {
                _chunks[i] = new byte[chunkSize];
                await br.BaseStream.ReadAsync(_chunks[i], 0, chunkSize);
            }
        }

        private static List<string> GetImageFiles(string folder)
        {
            var extensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".gif", ".tif", ".tiff" };
            return Directory.GetFiles(folder)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
                .ToList();
        }

        private static List<string> SortImagesNumerically(List<string> files)
        {
            return files.OrderBy(f =>
            {
                string name = Path.GetFileNameWithoutExtension(f);
                string numbers = new string(name.Where(char.IsDigit).ToArray());
                return int.TryParse(numbers, out int n) ? n : 0;
            }).ToList();
        }

        private static async Task<(int width, int height)> GetImageDimensionsAsync(string imagePath)
        {
            return await Task.Run(() =>
            {
                using (var stream = File.OpenRead(imagePath))
                {
                    var info = ImageResult.FromStream(stream, ColorComponents.Grey);
                    return (info.Width, info.Height);
                }
            });
        }

        private static async Task ProcessImagesParallelAsync(ChunkedVolume volume, List<string> imageFiles, 
            IProgress<float> progress)
        {
            int totalImages = imageFiles.Count;
            int processed = 0;
            int maxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1);
            
            // Use semaphore to limit parallelism
            using (var semaphore = new SemaphoreSlim(maxDegreeOfParallelism))
            {
                var tasks = imageFiles.Select(async (file, index) =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        await ProcessSingleImageAsync(volume, file, index);
                        
                        var currentProgress = Interlocked.Increment(ref processed);
                        progress?.Report((float)currentProgress / totalImages);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToArray();

                await Task.WhenAll(tasks);
            }
        }

        private static async Task ProcessSingleImageAsync(ChunkedVolume volume, string imagePath, int z)
        {
            await Task.Run(() =>
            {
                using (var stream = File.OpenRead(imagePath))
                {
                    var image = ImageResult.FromStream(stream, ColorComponents.Grey);
                    
                    // Write image data to volume
                    int index = 0;
                    for (int y = 0; y < volume.Height; y++)
                    {
                        for (int x = 0; x < volume.Width; x++)
                        {
                            volume[x, y, z] = image.Data[index++];
                        }
                    }
                }
            });
        }

        private static void ValidateDimensions(int width, int height, int depth, int chunkDim)
        {
            if (width <= 0 || height <= 0 || depth <= 0)
                throw new ArgumentException("Volume dimensions must be positive");
            
            if (chunkDim <= 0 || chunkDim > 1024)
                throw new ArgumentException("Chunk dimension must be between 1 and 1024");
        }
        #endregion

        #region IDisposable Implementation
        private bool _disposed = false;

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
                    _viewAccessor?.Dispose();
                    _mmf?.Dispose();
                    _chunks = null;
                }
                _disposed = true;
            }
        }
        #endregion
    }
}