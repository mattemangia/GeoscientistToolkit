// GAIA/Data/Loaders/AsyncScanCache.cs

using GAIA.Util;

namespace GAIA.Data.Loaders;

/// <summary>
///     Serves the result of an expensive directory scan without ever blocking the caller. Import
///     loaders enumerate a folder and stat every slice to preview a stack; the import modal polls
///     that every frame, so on a slow or network/NTFS drive the scan froze the whole UI thread.
///     This runs the scan on a background thread whenever the <em>key</em> (the inputs that define
///     it, e.g. the path) changes, returning the previous result until the new one is ready and
///     reporting <see cref="IsScanning"/> in the meantime. Only the newest scan publishes, so rapid
///     selection changes never show a stale count.
/// </summary>
public sealed class AsyncScanCache<T> where T : class, new()
{
    private volatile bool _scanning;
    private volatile T _current = new();
    private volatile string _key;

    /// <summary>True while a background scan for the current key is still running.</summary>
    public bool IsScanning => _scanning;

    /// <summary>Rescans off-thread if <paramref name="key"/> changed, then returns the newest
    /// completed result. Never touches the disk on the calling thread.</summary>
    public T Get(string key, Func<T> scan)
    {
        key ??= "";
        if (key == _key) return _current;
        _key = key;

        if (key.Length == 0)
        {
            _current = new T();
            _scanning = false;
            return _current;
        }

        _scanning = true;
        Task.Run(() =>
        {
            T result;
            try { result = scan() ?? new T(); }
            catch (Exception ex)
            {
                Logger.LogError($"[AsyncScanCache] Scan failed: {ex.Message}");
                result = new T();
            }
            if (key == _key) _current = result;
        }).ContinueWith(_ => { if (key == _key) _scanning = false; });

        return _current;
    }
}
