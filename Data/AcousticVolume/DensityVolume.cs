// GeoscientistToolkit/Data/AcousticVolume/DensityVolume.cs
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;
using System;
using System.Threading.Tasks;

namespace GeoscientistToolkit.Data.AcousticVolume
{
    public class DensityVolume : IDisposable
    {
        private readonly float[] _density; // kg/m³
        private readonly float[] _youngsModulus; // Pa
        private readonly float[] _poissonRatio;
        private readonly float[] _bulkModulus; // Pa
        private readonly float[] _shearModulus; // Pa
        private readonly float[] _pWaveVelocity; // m/s
        private readonly float[] _sWaveVelocity; // m/s
        
        public int Width { get; }
        public int Height { get; }
        public int Depth { get; }

        public DensityVolume(int width, int height, int depth)
        {
            Width = width;
            Height = height;
            Depth = depth;
            
            int totalVoxels = width * height * depth;
            _density = new float[totalVoxels];
            _youngsModulus = new float[totalVoxels];
            _poissonRatio = new float[totalVoxels];
            _bulkModulus = new float[totalVoxels];
            _shearModulus = new float[totalVoxels];
            _pWaveVelocity = new float[totalVoxels];
            _sWaveVelocity = new float[totalVoxels];
            
            // Initialize with limestone properties
            Parallel.For(0, totalVoxels, i =>
            {
                _density[i] = 2700f;
                _youngsModulus[i] = 35e9f; // 35 GPa
                _poissonRatio[i] = 0.26f;
                CalculateElasticProperties(i);
            });
        }

        public void SetDensity(int x, int y, int z, float density)
        {
            int index = GetIndex(x, y, z);
            _density[index] = density;
            CalculateElasticProperties(index);
        }

        public void SetMaterialProperties(int x, int y, int z, RockMaterial material)
        {
            int index = GetIndex(x, y, z);
            _density[index] = material.Density;
            _youngsModulus[index] = material.YoungsModulus * 1e9f;
            _poissonRatio[index] = material.PoissonRatio;
            _pWaveVelocity[index] = material.Vp;
            _sWaveVelocity[index] = material.Vs;
            
            // Calculate derived properties
            _bulkModulus[index] = _youngsModulus[index] / (3 * (1 - 2 * _poissonRatio[index]));
            _shearModulus[index] = _youngsModulus[index] / (2 * (1 + _poissonRatio[index]));
        }

        public void CalculateElasticProperties(int index)
        {
            // Use empirical relationships
            float density = _density[index];
            
            // Gardner's relation for velocity estimation
            _pWaveVelocity[index] = 1080 * MathF.Pow(density / 1000, 0.25f);
            _sWaveVelocity[index] = _pWaveVelocity[index] * MathF.Sqrt((1 - 2 * _poissonRatio[index]) / (2 * (1 - _poissonRatio[index])));
            
            // Calculate elastic moduli
            float mu = _sWaveVelocity[index] * _sWaveVelocity[index] * density;
            float k = _pWaveVelocity[index] * _pWaveVelocity[index] * density - 4f/3f * mu;
            
            _shearModulus[index] = mu;
            _bulkModulus[index] = k;
            _youngsModulus[index] = 9 * k * mu / (3 * k + mu);
        }

        public float GetDensity(int x, int y, int z) => _density[GetIndex(x, y, z)];
        public float GetYoungsModulus(int x, int y, int z) => _youngsModulus[GetIndex(x, y, z)];
        public float GetPoissonRatio(int x, int y, int z) => _poissonRatio[GetIndex(x, y, z)];
        public float GetPWaveVelocity(int x, int y, int z) => _pWaveVelocity[GetIndex(x, y, z)];
        public float GetSWaveVelocity(int x, int y, int z) => _sWaveVelocity[GetIndex(x, y, z)];
        public float GetBulkModulus(int x, int y, int z) => _bulkModulus[GetIndex(x, y, z)];
        public float GetShearModulus(int x, int y, int z) => _shearModulus[GetIndex(x, y, z)];

        public float GetMeanYoungsModulus()
        {
            double sum = 0;
            for (int i = 0; i < _youngsModulus.Length; i++)
                sum += _youngsModulus[i];
            return (float)(sum / _youngsModulus.Length);
        }

        public float GetMeanPoissonRatio()
        {
            double sum = 0;
            for (int i = 0; i < _poissonRatio.Length; i++)
                sum += _poissonRatio[i];
            return (float)(sum / _poissonRatio.Length);
        }

        private int GetIndex(int x, int y, int z) => z * Width * Height + y * Width + x;

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

        public void Dispose()
        {
            // Arrays will be garbage collected
        }
    }
}