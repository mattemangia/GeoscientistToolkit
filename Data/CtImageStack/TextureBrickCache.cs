// GeoscientistToolkit/Data/CtImageStack/TextureBrickCache.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Concurrent;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.CtImageStack
{
    public class LoadedBrick
    {
        public int BrickID;
        public int CacheSlot;
        public byte[] Data;
    }

    public class TextureBrickCache : IDisposable
    {
        public int CacheSize { get; }
        private readonly string _gvtFilePath;
        private readonly FileStream _fileStream;
        private readonly BinaryReader _reader;

        // Cache state
        private readonly int[] _brickIdInSlot;
        private readonly int[] _lruCounterInSlot;
        private readonly Dictionary<int, int> _slotForBrickId;
        private int _lruTimestamp = 0;

        // Threading for async loading
        private readonly Thread _loaderThread;
        private readonly ConcurrentQueue<int> _loadRequestQueue = new ConcurrentQueue<int>();
        private readonly ConcurrentQueue<LoadedBrick> _loadedBrickQueue = new ConcurrentQueue<LoadedBrick>();
        private readonly HashSet<int> _pendingRequests = new HashSet<int>();
        private volatile bool _isRunning = true;

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

        public void RequestBricks(IEnumerable<int> brickIds)
        {
            foreach (var id in brickIds)
            {
                if (!_slotForBrickId.ContainsKey(id) && !_pendingRequests.Contains(id))
                {
                    lock (_pendingRequests)
                    {
                        _pendingRequests.Add(id);
                    }
                    _loadRequestQueue.Enqueue(id);
                }
                else if (_slotForBrickId.TryGetValue(id, out int slot))
                {
                    _lruCounterInSlot[slot] = ++_lruTimestamp;
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
            {
                if (_loadRequestQueue.TryDequeue(out int brickIdToLoad))
                {
                    // --- Find a slot in the cache ---
                    int cacheSlot = -1;
                    int minLru = int.MaxValue;
                    for (int i = 0; i < CacheSize; i++)
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
                    if (_brickIdInSlot[cacheSlot] != -1)
                    {
                        _slotForBrickId.Remove(_brickIdInSlot[cacheSlot]);
                    }

                    // --- Read brick data from file ---
                    // This requires knowing the file format structure (offsets, brick size)
                    // For simplicity, we assume brickId maps directly to an offset.
                    long offset = 1024 + (long)brickIdToLoad * (64 * 64 * 64); // Placeholder offset
                    byte[] data;
                    lock(_reader)
                    {
                         _fileStream.Seek(offset, SeekOrigin.Begin);
                         data = _reader.ReadBytes(64 * 64 * 64);
                    }
                   
                    // --- Update cache state ---
                    _brickIdInSlot[cacheSlot] = brickIdToLoad;
                    _slotForBrickId[brickIdToLoad] = cacheSlot;
                    _lruCounterInSlot[cacheSlot] = ++_lruTimestamp;

                    // --- Enqueue for GPU upload ---
                    _loadedBrickQueue.Enqueue(new LoadedBrick { BrickID = brickIdToLoad, CacheSlot = cacheSlot, Data = data });
                    
                    lock(_pendingRequests)
                    {
                        _pendingRequests.Remove(brickIdToLoad);
                    }
                }
                else
                {
                    Thread.Sleep(10); // Wait for new requests
                }
            }
        }

        public void Dispose()
        {
            _isRunning = false;
            _loaderThread.Join();
            _reader?.Dispose();
            _fileStream?.Dispose();
        }
    }
}