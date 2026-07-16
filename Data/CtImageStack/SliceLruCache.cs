namespace GAIA.Data.CtImageStack;

/// <summary>A byte-budgeted LRU cache for CPU slice data.</summary>
internal sealed class SliceLruCache
{
    private readonly long _byteBudget;
    private readonly Dictionary<(int view, int slice), LinkedListNode<Entry>> _items = new();
    private readonly LinkedList<Entry> _lru = new();
    private long _bytes;

    private sealed record Entry((int view, int slice) Key, byte[] Data);

    public SliceLruCache(long byteBudget) => _byteBudget = Math.Max(1, byteBudget);

    public byte[] GetOrAdd(int view, int slice, Func<byte[]> factory)
    {
        var key = (view, slice);
        if (_items.TryGetValue(key, out var existing))
        {
            _lru.Remove(existing);
            _lru.AddFirst(existing);
            return existing.Value.Data;
        }

        var data = factory();
        if (data == null || data.LongLength > _byteBudget) return data;
        var node = _lru.AddFirst(new Entry(key, data));
        _items[key] = node;
        _bytes += data.LongLength;
        while (_bytes > _byteBudget && _lru.Last != null)
        {
            var victim = _lru.Last;
            _lru.RemoveLast();
            _items.Remove(victim.Value.Key);
            _bytes -= victim.Value.Data.LongLength;
        }
        return data;
    }

    public void Clear()
    {
        _items.Clear();
        _lru.Clear();
        _bytes = 0;
    }
}
