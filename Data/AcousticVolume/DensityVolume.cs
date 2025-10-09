// GeoscientistToolkit/Data/AcousticVolume/DensityVolume.cs

namespace GeoscientistToolkit.Data.AcousticVolume;

public class DensityVolume : IDisposable
{
    private readonly float[] _bulkModulus; // Pa
    private readonly float[] _density; // kg/m³
    private readonly float[] _poissonRatio;
    private readonly float[] _pWaveVelocity; // m/s
    private readonly float[] _shearModulus; // Pa
    private readonly float[] _sWaveVelocity; // m/s
    private readonly float[] _youngsModulus; // Pa

    /// <summary>
    ///     Creates a new, empty DensityVolume and initializes it with default properties.
    ///     Used by the DensityCalibrationTool.
    /// </summary>
    public DensityVolume(int width, int height, int depth)
    {
        Width = width;
        Height = height;
        Depth = depth;

        var totalVoxels = width * height * depth;
        _density = new float[totalVoxels];
        _youngsModulus = new float[totalVoxels];
        _poissonRatio = new float[totalVoxels];
        _bulkModulus = new float[totalVoxels];
        _shearModulus = new float[totalVoxels];
        _pWaveVelocity = new float[totalVoxels];
        _sWaveVelocity = new float[totalVoxels];

        // Initialize with default properties (e.g., limestone)
        Parallel.For(0, totalVoxels, i =>
        {
            _density[i] = 2700f;
            _youngsModulus[i] = 35e9f; // 35 GPa in Pascals
            _poissonRatio[i] = 0.26f;
            RecalculateDerivedProperties(i);
        });
    }

    /// <summary>
    ///     Creates a DensityVolume from existing raw property arrays.
    ///     Used when loading an AcousticVolumeDataset.
    /// </summary>
    public DensityVolume(float[,,] density, float[,,] youngsModulus, float[,,] poissonRatio)
    {
        Width = density.GetLength(0);
        Height = density.GetLength(1);
        Depth = density.GetLength(2);

        var totalVoxels = Width * Height * Depth;
        _density = new float[totalVoxels];
        _youngsModulus = new float[totalVoxels]; // Expects Pa
        _poissonRatio = new float[totalVoxels];

        // Copy data from 3D arrays to flat 1D arrays for performance
        Buffer.BlockCopy(density, 0, _density, 0, totalVoxels * sizeof(float));
        Buffer.BlockCopy(youngsModulus, 0, _youngsModulus, 0, totalVoxels * sizeof(float));
        Buffer.BlockCopy(poissonRatio, 0, _poissonRatio, 0, totalVoxels * sizeof(float));

        // Allocate and recalculate derived properties from the loaded authoritative values
        _bulkModulus = new float[totalVoxels];
        _shearModulus = new float[totalVoxels];
        _pWaveVelocity = new float[totalVoxels];
        _sWaveVelocity = new float[totalVoxels];

        Parallel.For(0, totalVoxels, RecalculateDerivedProperties);
    }

    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }

    public void Dispose()
    {
        // Nothing to dispose, arrays are managed.
    }

    public void SetDensity(int x, int y, int z, float density)
    {
        var index = GetIndex(x, y, z);
        _density[index] = density;
        // Note: This simple setter doesn't automatically derive E and nu.
        // Full property recalculation is more complex.
    }

    public void SetMaterialProperties(int x, int y, int z, RockMaterial material)
    {
        var index = GetIndex(x, y, z);
        _density[index] = material.Density;
        _youngsModulus[index] = material.YoungsModulus * 1e9f; // Convert GPa to Pa
        _poissonRatio[index] = material.PoissonRatio;

        RecalculateDerivedProperties(index);
    }

    /// <summary>
    ///     Recalculates derived properties (velocities, moduli) for a given voxel index
    ///     based on the authoritative values of Density, Young's Modulus, and Poisson's Ratio.
    /// </summary>
    private void RecalculateDerivedProperties(int index)
    {
        var E = _youngsModulus[index]; // in Pa
        var nu = _poissonRatio[index];
        var rho = _density[index]; // in kg/m³

        if (rho > 0 && E > 0 && nu < 0.5f && nu > -1.0f)
        {
            _shearModulus[index] = E / (2f * (1f + nu));
            _bulkModulus[index] = E / (3f * (1f - 2f * nu));

            // P-wave modulus (M) = K + 4/3 * G
            var p_modulus = _bulkModulus[index] + 4f / 3f * _shearModulus[index];

            _pWaveVelocity[index] = MathF.Sqrt(p_modulus / rho);
            _sWaveVelocity[index] = MathF.Sqrt(_shearModulus[index] / rho);
        }
        else // Set to zero if inputs are invalid
        {
            _shearModulus[index] = 0;
            _bulkModulus[index] = 0;
            _pWaveVelocity[index] = 0;
            _sWaveVelocity[index] = 0;
        }
    }


    public float GetDensity(int x, int y, int z)
    {
        return _density[GetIndex(x, y, z)];
    }

    public float GetYoungsModulus(int x, int y, int z)
    {
        return _youngsModulus[GetIndex(x, y, z)];
    }

    public float GetPoissonRatio(int x, int y, int z)
    {
        return _poissonRatio[GetIndex(x, y, z)];
    }

    public float GetPWaveVelocity(int x, int y, int z)
    {
        return _pWaveVelocity[GetIndex(x, y, z)];
    }

    public float GetSWaveVelocity(int x, int y, int z)
    {
        return _sWaveVelocity[GetIndex(x, y, z)];
    }

    public float GetBulkModulus(int x, int y, int z)
    {
        return _bulkModulus[GetIndex(x, y, z)];
    }

    public float GetShearModulus(int x, int y, int z)
    {
        return _shearModulus[GetIndex(x, y, z)];
    }

    public float GetMeanYoungsModulus()
    {
        double sum = 0;
        for (var i = 0; i < _youngsModulus.Length; i++)
            sum += _youngsModulus[i];
        return (float)(sum / _youngsModulus.Length);
    }

    /// <summary>
    ///     Extracts the density array for export.
    /// </summary>
    public float[,,] ExtractDensityArray()
    {
        var result = new float[Width, Height, Depth];
        for (var z = 0; z < Depth; z++)
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
            result[x, y, z] = _density[GetIndex(x, y, z)];
        return result;
    }

    /// <summary>
    ///     Extracts the Young's modulus array for export (in Pascals).
    /// </summary>
    public float[,,] ExtractYoungsModulusArray()
    {
        var result = new float[Width, Height, Depth];
        for (var z = 0; z < Depth; z++)
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
            result[x, y, z] = _youngsModulus[GetIndex(x, y, z)];
        return result;
    }

    /// <summary>
    ///     Extracts the Poisson's ratio array for export.
    /// </summary>
    public float[,,] ExtractPoissonRatioArray()
    {
        var result = new float[Width, Height, Depth];
        for (var z = 0; z < Depth; z++)
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
            result[x, y, z] = _poissonRatio[GetIndex(x, y, z)];
        return result;
    }

    /// <summary>
    ///     Sets material properties for a voxel based on density using empirical relationships.
    ///     This provides spatially varying properties even when only density is known.
    /// </summary>
    public void SetPropertiesFromDensity(int x, int y, int z, float density)
    {
        var index = GetIndex(x, y, z);
        _density[index] = density;

        // Empirical relationships for sedimentary rocks
        var densityGcm3 = density / 1000f; // Convert kg/m³ to g/cm³

        // Gardner's relation: Vp = 1.85 * ρ^0.265 (gives Vp in km/s)
        var vpKms = 1.85f * MathF.Pow(densityGcm3, 0.265f);
        var vp = vpKms * 1000f; // Convert to m/s

        // Estimate Poisson's ratio based on density
        // Lower density (clay) -> higher nu (~0.35)
        // Higher density (limestone) -> lower nu (~0.25)
        var nu = 0.35f - (densityGcm3 - 2.0f) * 0.05f;
        nu = Math.Clamp(nu, 0.15f, 0.40f);

        // Calculate Young's modulus from Vp and Poisson's ratio
        // E = ρ * Vp² * (1+ν)(1-2ν) / (1-ν)
        var E = density * vp * vp * (1 + nu) * (1 - 2 * nu) / (1 - nu);

        _youngsModulus[index] = E;
        _poissonRatio[index] = nu;

        RecalculateDerivedProperties(index);
    }

    public float GetMeanPoissonRatio()
    {
        double sum = 0;
        for (var i = 0; i < _poissonRatio.Length; i++)
            sum += _poissonRatio[i];
        return (float)(sum / _poissonRatio.Length);
    }

    public float GetMeanDensity()
    {
        if (_density == null || _density.Length == 0) return 0;
        return _density.Average();
    }

    private int GetIndex(int x, int y, int z)
    {
        return z * Width * Height + y * Width + x;
    }

    public void Clear()
    {
        Array.Clear(_density, 0, _density.Length);
        Array.Clear(_youngsModulus, 0, _youngsModulus.Length);
        Array.Clear(_poissonRatio, 0, _poissonRatio.Length);
        Array.Clear(_bulkModulus, 0, _bulkModulus.Length);
        Array.Clear(_shearModulus, 0, _shearModulus.Length);
        Array.Clear(_pWaveVelocity, 0, _pWaveVelocity.Length);
        Array.Clear(_sWaveVelocity, 0, _sWaveVelocity.Length);
    }
}