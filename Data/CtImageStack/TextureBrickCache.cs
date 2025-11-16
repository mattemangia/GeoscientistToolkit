// GeoscientistToolkit/Data/CtImageStack/TextureBrickCache.cs

using System.Collections.Concurrent;

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
                var offset = 1024 + (long)brickIdToLoad * (64 * 64 * 64); // Placeholder offset
                byte[] data;
                lock (_reader)
                {
                    _fileStream.Seek(offset, SeekOrigin.Begin);
                    data = _reader.ReadBytes(64 * 64 * 64);
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
}