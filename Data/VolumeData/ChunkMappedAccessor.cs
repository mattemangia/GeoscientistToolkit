using System.IO.MemoryMappedFiles;

namespace GAIA.Data.VolumeData;

/// <summary>
/// Maps only the chunk currently used by each worker. This avoids creating a view
/// whose address space is as large as the complete CT file while retaining bulk MMF IO.
/// </summary>
internal sealed class ChunkMappedAccessor : IDisposable
{
    private sealed class ThreadWindow : IDisposable
    {
        public long Index = -1;
        public MemoryMappedViewAccessor Accessor;
        public void Dispose() => Accessor?.Dispose();
    }

    private readonly MemoryMappedFile _file;
    private readonly long _dataOffset;
    private readonly long _chunkSize;
    private readonly long _capacity;
    private readonly ThreadLocal<ThreadWindow> _windows;
    private bool _disposed;

    public long Capacity => _capacity;

    public ChunkMappedAccessor(MemoryMappedFile file, long dataOffset, long chunkSize, long capacity)
    {
        _file = file ?? throw new ArgumentNullException(nameof(file));
        if (dataOffset < 0 || chunkSize <= 0 || capacity < dataOffset)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _dataOffset = dataOffset;
        _chunkSize = chunkSize;
        _capacity = capacity;
        _windows = new ThreadLocal<ThreadWindow>(() => new ThreadWindow(), true);
    }

    public byte ReadByte(long position)
    {
        var (window, local) = GetWindow(position);
        return window.ReadByte(local);
    }

    public void Write(long position, byte value)
    {
        var (window, local) = GetWindow(position);
        window.Write(local, value);
    }

    public int ReadArray(long position, byte[] buffer, int offset, int count)
    {
        ValidateBuffer(position, buffer, offset, count);
        var remaining = count;
        while (remaining > 0)
        {
            var (window, local) = GetWindow(position);
            var length = (int)Math.Min(remaining, window.Capacity - local);
            window.ReadArray(local, buffer, offset, length);
            position += length; offset += length; remaining -= length;
        }
        return count;
    }

    public void WriteArray(long position, byte[] buffer, int offset, int count)
    {
        ValidateBuffer(position, buffer, offset, count);
        var remaining = count;
        while (remaining > 0)
        {
            var (window, local) = GetWindow(position);
            var length = (int)Math.Min(remaining, window.Capacity - local);
            window.WriteArray(local, buffer, offset, length);
            position += length; offset += length; remaining -= length;
        }
    }

    public void Flush()
    {
        foreach (var window in _windows.Values) window.Accessor?.Flush();
    }

    private (MemoryMappedViewAccessor accessor, long local) GetWindow(long position)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (position < _dataOffset || position >= _capacity)
            throw new ArgumentOutOfRangeException(nameof(position));
        var index = (position - _dataOffset) / _chunkSize;
        var state = _windows.Value;
        if (state.Index != index)
        {
            state.Accessor?.Dispose();
            var start = checked(_dataOffset + index * _chunkSize);
            var length = Math.Min(_chunkSize, _capacity - start);
            state.Accessor = _file.CreateViewAccessor(start, length, MemoryMappedFileAccess.ReadWrite);
            state.Index = index;
        }
        return (state.Accessor, position - (_dataOffset + index * _chunkSize));
    }

    private void ValidateBuffer(long position, byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset > buffer.Length - count)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (position < _dataOffset || position > _capacity - count)
            throw new ArgumentOutOfRangeException(nameof(position));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var window in _windows.Values) window.Dispose();
        _windows.Dispose();
    }
}
