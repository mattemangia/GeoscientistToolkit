// GeoscientistToolkit/Util/TextureCacheManager.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace GeoscientistToolkit.Util
{
    /// <summary>
    /// Manages the lifecycle of textures to respect a global memory budget.
    /// Uses a reference counting and LRU (Least Recently Used) eviction policy.
    /// </summary>
    public class TextureCacheManager : IDisposable
    {
        private class CacheEntry
        {
            public TextureManager TextureManager { get; }
            public long SizeInBytes { get; }
            public int ReferenceCount { get; set; }
            public DateTime LastAccessTime { get; set; }

            public CacheEntry(TextureManager manager, long size)
            {
                TextureManager = manager;
                SizeInBytes = size;
                ReferenceCount = 0;
                LastAccessTime = DateTime.UtcNow;
            }
        }

        private readonly Dictionary<string, CacheEntry> _cache = new();
        private long _currentCacheSize = 0;
        private long _maxCacheSize; // in bytes
        private readonly object _lock = new();

        public TextureCacheManager(long maxCacheSizeBytes)
        {
            _maxCacheSize = maxCacheSizeBytes;
        }

        /// <summary>
        /// Retrieves a texture from the cache or creates it if it doesn't exist.
        /// </summary>
        /// <param name="key">A unique key for the texture (e.g., file path).</param>
        /// <param name="factory">A function that creates the TextureManager and returns its size.</param>
        /// <returns>The cached or newly created TextureManager.</returns>
        public TextureManager GetTexture(string key, Func<(TextureManager, long)> factory)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var entry))
                {
                    entry.ReferenceCount++;
                    entry.LastAccessTime = DateTime.UtcNow;
                    return entry.TextureManager;
                }

                // Texture not in cache, create it
                var (newManager, size) = factory();
                if (newManager == null || !newManager.IsValid)
                {
                    Logger.LogError($"Failed to create texture for key: {key}");
                    return null;
                }

                // Evict old textures if we're over budget
                EnsureCacheCapacity(size);
                
                var newEntry = new CacheEntry(newManager, size)
                {
                    ReferenceCount = 1,
                    LastAccessTime = DateTime.UtcNow
                };

                _cache[key] = newEntry;
                _currentCacheSize += size;
                
                Logger.Log($"Cached new texture '{key}'. Current cache size: {_currentCacheSize / 1024 / 1024} MB");

                return newManager;
            }
        }

        /// <summary>
        /// Releases a reference to a texture. When the reference count reaches zero,
        /// the texture becomes a candidate for eviction.
        /// </summary>
        public void ReleaseTexture(string key)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var entry))
                {
                    entry.ReferenceCount--;
                    if (entry.ReferenceCount < 0) entry.ReferenceCount = 0;
                }
            }
        }

        private void EnsureCacheCapacity(long sizeOfNewTexture)
        {
            if (_currentCacheSize + sizeOfNewTexture <= _maxCacheSize)
            {
                return;
            }
            
            Logger.Log("Cache size exceeds limit. Evicting old textures...");

            // Find candidates for eviction (not currently in use)
            var evictable = _cache.Where(kvp => kvp.Value.ReferenceCount == 0)
                                  .OrderBy(kvp => kvp.Value.LastAccessTime)
                                  .ToList();

            foreach (var item in evictable)
            {
                Logger.Log($"Evicting texture: {item.Key}");
                _currentCacheSize -= item.Value.SizeInBytes;
                item.Value.TextureManager.Dispose();
                _cache.Remove(item.Key);

                if (_currentCacheSize + sizeOfNewTexture <= _maxCacheSize)
                {
                    return; // Enough space has been freed
                }
            }

            if (_currentCacheSize + sizeOfNewTexture > _maxCacheSize)
            {
                Logger.LogWarning("Could not free enough space in texture cache. The cache will exceed its limit.");
            }
        }
        
        public void UpdateCacheSize(long newMaxCacheSizeBytes)
        {
            lock (_lock)
            {
                _maxCacheSize = newMaxCacheSizeBytes;
                Logger.Log($"Texture cache size updated to: {_maxCacheSize / 1024 / 1024} MB");
                // Trigger an eviction check immediately
                EnsureCacheCapacity(0);
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var entry in _cache.Values)
                {
                    entry.TextureManager.Dispose();
                }
                _cache.Clear();
                _currentCacheSize = 0;
            }
        }
    }
}