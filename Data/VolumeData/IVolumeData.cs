// GeoscientistToolkit/Data/VolumeData/IVolumeData.cs
using System;

namespace GeoscientistToolkit.Data.VolumeData
{
    /// <summary>
    /// Base interface for volume data
    /// </summary>
    public interface IVolumeData : IDisposable
    {
        int Width { get; }
        int Height { get; }
        int Depth { get; }
        byte this[int x, int y, int z] { get; set; }
    }

    /// <summary>
    /// Interface for grayscale volume data
    /// </summary>
    public interface IGrayscaleVolumeData : IVolumeData
    {
        void WriteSliceZ(int z, byte[] data);
        void ReadSliceZ(int z, byte[] buffer);
    }

    /// <summary>
    /// Interface for label volume data
    /// </summary>
    public interface ILabelVolumeData : IVolumeData
    {
        void WriteSliceZ(int z, byte[] data);
        void ReadSliceZ(int z, byte[] buffer);
    }
}