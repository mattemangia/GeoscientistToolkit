namespace GAIA.Data.CtImageStack;

public abstract class CtPreviewVolume
{
    protected CtPreviewVolume(int width, int height, int depth)
    { Width = width; Height = height; Depth = depth; }
    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }
    public abstract byte GetVoxel(int x, int y, int z);

    public byte[] ReadSlice(int view, int index, int width, int height)
    {
        var result = new byte[checked(width * height)];
        switch (view)
        {
            case 0:
                for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                    result[y * width + x] = GetVoxel(x, y, index);
                break;
            case 1:
                for (var z = 0; z < height; z++) for (var x = 0; x < width; x++)
                    result[z * width + x] = GetVoxel(x, index, z);
                break;
            case 2:
                for (var z = 0; z < height; z++) for (var y = 0; y < width; y++)
                    result[z * width + y] = GetVoxel(index, y, z);
                break;
        }
        return result;
    }

    public byte[] BuildLod(int width, int height, int depth)
    {
        var result = new byte[checked(width * height * depth)];
        Parallel.For(0, depth, z =>
        {
            var sourceZ = Math.Min(Depth - 1, z * Depth / depth);
            for (var y = 0; y < height; y++)
            {
                var sourceY = Math.Min(Height - 1, y * Height / height);
                for (var x = 0; x < width; x++)
                    result[(z * height + y) * width + x] = GetVoxel(
                        Math.Min(Width - 1, x * Width / width), sourceY, sourceZ);
            }
        });
        return result;
    }
}

public sealed class DenseCtPreviewVolume : CtPreviewVolume
{
    private readonly byte[] _data;
    public DenseCtPreviewVolume(int width, int height, int depth, byte[] data) : base(width, height, depth) =>
        _data = data ?? throw new ArgumentNullException(nameof(data));
    public override byte GetVoxel(int x, int y, int z)
    {
        if ((uint)x >= Width || (uint)y >= Height || (uint)z >= Depth) return 0;
        var index = ((long)z * Height + y) * Width + x;
        return index < _data.LongLength ? _data[index] : (byte)0;
    }
}

public sealed class SparseSliceCtPreviewVolume : CtPreviewVolume
{
    private readonly IReadOnlyDictionary<int, byte[]> _slices;
    public SparseSliceCtPreviewVolume(int width, int height, int depth, IReadOnlyDictionary<int, byte[]> slices)
        : base(width, height, depth) => _slices = slices;
    public override byte GetVoxel(int x, int y, int z)
    {
        if ((uint)x >= Width || (uint)y >= Height || (uint)z >= Depth ||
            !_slices.TryGetValue(z, out var slice)) return 0;
        return slice[y * Width + x];
    }
}

public sealed class FunctionalCtPreviewVolume : CtPreviewVolume
{
    private readonly Func<int, int, int, byte> _sample;
    public FunctionalCtPreviewVolume(int width, int height, int depth, Func<int, int, int, byte> sample)
        : base(width, height, depth) => _sample = sample;
    public override byte GetVoxel(int x, int y, int z) =>
        (uint)x < Width && (uint)y < Height && (uint)z < Depth ? _sample(x, y, z) : (byte)0;
}
