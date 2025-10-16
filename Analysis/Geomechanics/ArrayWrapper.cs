// GeoscientistToolkit/Analysis/Geomechanics/ArrayWrapper.cs
using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace GeoscientistToolkit.Analysis.Geomechanics;

/// <summary>
/// Abstract base class to provide array-like access to data that could be in memory or on disk.
/// </summary>
public abstract class ArrayWrapper<T> : IDisposable where T : struct
{
    public long Length { get; protected set; }
    public abstract T this[long index] { get; set; }
    public abstract void ReadChunk(long startIndex, T[] buffer);
    public abstract void WriteChunk(long startIndex, T[] buffer);
    public abstract void Dispose();
}

/// <summary>
/// An ArrayWrapper that uses a standard in-memory T[] array.
/// </summary>
public class MemoryBackedArray<T> : ArrayWrapper<T> where T : struct
{
    private readonly T[] _data;

    public MemoryBackedArray(long size)
    {
        Length = size;
        _data = new T[size];
    }
    
    public MemoryBackedArray(T[] existingData)
    {
        Length = existingData.Length;
        _data = existingData;
    }

    public override T this[long index]
    {
        get => _data[index];
        set => _data[index] = value;
    }

    public override void ReadChunk(long startIndex, T[] buffer)
    {
        Array.Copy(_data, startIndex, buffer, 0, buffer.Length);
    }
    
    public override void WriteChunk(long startIndex, T[] buffer)
    {
        Array.Copy(buffer, 0, _data, startIndex, buffer.Length);
    }
    
    // Nothing to dispose for in-memory array
    public override void Dispose() { }

    // Allows direct access to the underlying array when not offloading
    public T[] GetInternalArray() => _data;
}


/// <summary>
/// An ArrayWrapper that stores data in a temporary file on disk.
/// </summary>
public class DiskBackedArray<T> : ArrayWrapper<T> where T : struct
{
    private readonly string _filePath;
    private readonly FileStream _stream;
    private readonly BinaryReader _reader;
    private readonly BinaryWriter _writer;
    private readonly int _typeSize;

    public DiskBackedArray(long size, string offloadDirectory)
    {
        Length = size;
        _filePath = Path.Combine(offloadDirectory, Path.GetRandomFileName());
        _typeSize = Marshal.SizeOf<T>();

        // Ensure directory exists
        Directory.CreateDirectory(offloadDirectory);

        // Create a file of the required size
        _stream = new FileStream(_filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        _stream.SetLength(size * _typeSize);

        _reader = new BinaryReader(_stream);
        _writer = new BinaryWriter(_stream);
    }

    public override T this[long index]
    {
        get
        {
            lock (_stream)
            {
                _stream.Seek(index * _typeSize, SeekOrigin.Begin);
                // This is slow and inefficient for single reads, but provides array-like syntax.
                // In practice, ReadChunk/WriteChunk should be used for performance.
                var buffer = new byte[_typeSize];
                _stream.Read(buffer, 0, _typeSize);
                return MemoryMarshal.Read<T>(buffer);
            }
        }
        set
        {
            lock (_stream)
            {
                _stream.Seek(index * _typeSize, SeekOrigin.Begin);
                var buffer = new byte[_typeSize];
                MemoryMarshal.Write(buffer, ref value);
                _stream.Write(buffer, 0, _typeSize);
            }
        }
    }

    public override void ReadChunk(long startIndex, T[] buffer)
    {
        var bytesToRead = buffer.Length * _typeSize;
        var bytes = new byte[bytesToRead];
        lock (_stream)
        {
            _stream.Seek(startIndex * _typeSize, SeekOrigin.Begin);
            _stream.Read(bytes, 0, bytesToRead);
        }
        Buffer.BlockCopy(bytes, 0, buffer, 0, bytesToRead);
    }

    public override void WriteChunk(long startIndex, T[] buffer)
    {
        var bytesToWrite = buffer.Length * _typeSize;
        var bytes = new byte[bytesToWrite];
        Buffer.BlockCopy(buffer, 0, bytes, 0, bytesToWrite);
        lock (_stream)
        {
            _stream.Seek(startIndex * _typeSize, SeekOrigin.Begin);
            _stream.Write(bytes, 0, bytesToWrite);
        }
    }

    public override void Dispose()
    {
        _reader?.Dispose();
        _writer?.Dispose();
        _stream?.Dispose();
        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
        catch (Exception ex)
        {
            // Log error if file can't be deleted
            Console.WriteLine($"[DiskBackedArray] Failed to delete temporary file {_filePath}: {ex.Message}");
        }
    }
}