// GeoscientistToolkit/Data/CtImageStack/TextureBrickCache.cs

using System.Collections.Concurrent;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.CtImageStack;

public class LoadedBrick
{
    public int BrickID;
    public int CacheSlot;
    public byte[] Data;
}

public class TextureBrickCache : IDisposable
{
    // Cache state
    private readonly int[] _brickIdInSlot;
    private readonly FileStream _fileStream;
    private readonly string _gvtFilePath;
    private readonly ConcurrentQueue<LoadedBrick> _loadedBrickQueue = new();
    private readonly int _brickSize;
    private readonly int _brickByteSize;
    private readonly GvtLodInfo[] _lodInfos;
    private readonly int _activeLodIndex;

    // Threading for async loading
    private readonly Thread _loaderThread;
    private readonly ConcurrentQueue<int> _loadRequestQueue = new();
    private readonly int[] _lruCounterInSlot;
    private readonly HashSet<int> _pendingRequests = new();
    private readonly BinaryReader _reader;
    private readonly Dictionary<int, int> _slotForBrickId;
    private readonly object _cacheLock = new object(); // Lock for cache state modifications
    private volatile bool _isRunning = true;
    private int _lruTimestamp;

    public TextureBrickCache(string gvtFilePath, int cacheSize)
    {
        CacheSize = cacheSize;
        _gvtFilePath = gvtFilePath;

        _brickIdInSlot = new int[cacheSize];
        _lruCounterInSlot = new int[cacheSize];
        _slotForBrickId = new Dictionary<int, int>();
        Array.Fill(_brickIdInSlot, -1);

        _fileStream = new FileStream(_gvtFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        _reader = new BinaryReader(_fileStream);

        (_brickSize, _lodInfos) = ReadHeader(_reader);
        _brickByteSize = _brickSize * _brickSize * _brickSize;
        _activeLodIndex = 0;

        _loaderThread = new Thread(LoaderThreadLoop) { IsBackground = true, Name = "BrickLoaderThread" };
        _loaderThread.Start();
    }

    public int CacheSize { get; }

    public void Dispose()
    {
        _isRunning = false;
        _loaderThread.Join();
        _reader?.Dispose();
        _fileStream?.Dispose();
    }

    public void RequestBricks(IEnumerable<int> brickIds)
    {
        foreach (var id in brickIds)
        {
            lock (_cacheLock)
            {
                // Check if brick is already loaded or pending
                if (!_slotForBrickId.ContainsKey(id) && !_pendingRequests.Contains(id))
                {
                    _pendingRequests.Add(id);
                    _loadRequestQueue.Enqueue(id);
                }
                else if (_slotForBrickId.TryGetValue(id, out var slot))
                {
                    // Update LRU timestamp atomically
                    _lruCounterInSlot[slot] = Interlocked.Increment(ref _lruTimestamp);
                }
            }
        }
    }

    public bool TryGetNextLoadedBrick(out LoadedBrick brick)
    {
        return _loadedBrickQueue.TryDequeue(out brick);
    }

    private void LoaderThreadLoop()
    {
        while (_isRunning)
            if (_loadRequestQueue.TryDequeue(out var brickIdToLoad))
            {
                int cacheSlot;
                int evictedBrickId;

                // --- Find a slot in the cache (under lock) ---
                lock (_cacheLock)
                {
                    cacheSlot = -1;
                    var minLru = int.MaxValue;
                    for (var i = 0; i < CacheSize; i++)
                    {
                        if (_brickIdInSlot[i] == -1)
                        {
                            cacheSlot = i;
                            break;
                        }

                        if (_lruCounterInSlot[i] < minLru)
                        {
                            minLru = _lruCounterInSlot[i];
                            cacheSlot = i;
                        }
                    }

                    // --- Evict old brick if necessary ---
                    evictedBrickId = _brickIdInSlot[cacheSlot];
                    if (evictedBrickId != -1)
                    {
                        _slotForBrickId.Remove(evictedBrickId);
                    }
                }

                // --- Read brick data from file (outside cache lock to avoid blocking) ---
                var lodInfo = _lodInfos[_activeLodIndex];
                var bricksX = (lodInfo.Width + _brickSize - 1) / _brickSize;
                var bricksY = (lodInfo.Height + _brickSize - 1) / _brickSize;
                var bricksZ = (lodInfo.Depth + _brickSize - 1) / _brickSize;
                var totalBricks = bricksX * bricksY * bricksZ;

                if (brickIdToLoad < 0 || brickIdToLoad >= totalBricks)
                {
                    Logger.LogWarning($"[TextureBrickCache] Brick ID {brickIdToLoad} out of range for LOD {_activeLodIndex}");
                    continue;
                }

                var offset = lodInfo.FileOffset + (long)brickIdToLoad * _brickByteSize;
                byte[] data;
                lock (_reader)
                {
                    _fileStream.Seek(offset, SeekOrigin.Begin);
                    data = _reader.ReadBytes(_brickByteSize);
                }

                // --- Update cache state (under lock) ---
                lock (_cacheLock)
                {
                    _brickIdInSlot[cacheSlot] = brickIdToLoad;
                    _slotForBrickId[brickIdToLoad] = cacheSlot;
                    _lruCounterInSlot[cacheSlot] = Interlocked.Increment(ref _lruTimestamp);

                    // Remove from pending requests
                    _pendingRequests.Remove(brickIdToLoad);
                }

                // --- Enqueue for GPU upload (thread-safe queue, no lock needed) ---
                _loadedBrickQueue.Enqueue(new LoadedBrick
                    { BrickID = brickIdToLoad, CacheSlot = cacheSlot, Data = data });
            }
            else
            {
                Thread.Sleep(10); // Wait for new requests
            }
    }

    private static (int brickSize, GvtLodInfo[] lodInfos) ReadHeader(BinaryReader reader)
    {
        reader.BaseStream.Seek(0, SeekOrigin.Begin);
        reader.ReadInt32(); // width
        reader.ReadInt32(); // height
        reader.ReadInt32(); // depth
        var brickSize = reader.ReadInt32();
        var lodCount = reader.ReadInt32();

        var lodInfos = new GvtLodInfo[lodCount];
        for (var i = 0; i < lodCount; i++)
        {
            lodInfos[i] = new GvtLodInfo
            {
                Width = reader.ReadInt32(),
                Height = reader.ReadInt32(),
                Depth = reader.ReadInt32(),
                FileOffset = reader.ReadInt64()
            };
        }

        return (brickSize, lodInfos);
    }
}
