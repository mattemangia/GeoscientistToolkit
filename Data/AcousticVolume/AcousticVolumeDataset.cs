// GeoscientistToolkit/Data/AcousticVolume/AcousticVolumeDataset.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.AcousticVolume
{
    /// <summary>
    /// Dataset type for acoustic simulation results containing wave field data,
    /// velocity measurements, material properties, and damage field
    /// </summary>
    public class AcousticVolumeDataset : Dataset, ISerializableDataset
    {
        // Wave field data storage
        public ChunkedVolume PWaveField { get; set; }
        public ChunkedVolume SWaveField { get; set; }
        public ChunkedVolume CombinedWaveField { get; set; }
        public ChunkedVolume DamageField { get; set; } // NEW: Damage field from simulation
        
        // Simulation metadata
        public double PWaveVelocity { get; set; }
        public double SWaveVelocity { get; set; }
        public double VpVsRatio { get; set; }
        public int TimeSteps { get; set; }
        public TimeSpan ComputationTime { get; set; }
        
        // Material properties used in simulation
        public float YoungsModulusMPa { get; set; }
        public float PoissonRatio { get; set; }
        public float ConfiningPressureMPa { get; set; }
        public float SourceFrequencyKHz { get; set; }
        public float SourceEnergyJ { get; set; }
        
        // Damage model parameters
        public float TensileStrengthMPa { get; set; }
        public float CohesionMPa { get; set; }
        public float FailureAngleDeg { get; set; }
        public float MaxDamage { get; set; }
        
        // Reference to source dataset
        public string SourceDatasetPath { get; set; }
        public string SourceMaterialName { get; set; }
        
        public DensityVolume DensityData { get; set; } // ADD THIS LINE
        
        // Time-series data for animation
        public List<WaveFieldSnapshot> TimeSeriesSnapshots { get; set; }
        
        // Calibration data
        public CalibrationData Calibration { get; set; }
        
        public AcousticVolumeDataset(string name, string filePath) : base(name, filePath)
        {
            Type = DatasetType.AcousticVolume;
            TimeSeriesSnapshots = new List<WaveFieldSnapshot>();
            Calibration = new CalibrationData();
        }
        
        public override long GetSizeInBytes()
        {
            long size = 0;
            
            if (PWaveField != null)
                size += (long)PWaveField.Width * PWaveField.Height * PWaveField.Depth;
            if (SWaveField != null)
                size += (long)SWaveField.Width * SWaveField.Height * SWaveField.Depth;
            if (CombinedWaveField != null)
                size += (long)CombinedWaveField.Width * CombinedWaveField.Height * CombinedWaveField.Depth;
            if (DamageField != null)
                size += (long)DamageField.Width * DamageField.Height * DamageField.Depth;
                
            // Add time series data size
            foreach (var snapshot in TimeSeriesSnapshots)
            {
                size += snapshot.GetSizeInBytes();
            }
            
            return size;
        }
        
        public override void Load()
        {
            if (!Directory.Exists(FilePath))
            {
                Logger.LogError($"Acoustic volume directory not found: {FilePath}");
                IsMissing = true;
                return;
            }
            
            try
            {
                // Load wave field volumes
                string pWavePath = Path.Combine(FilePath, "PWaveField.bin");
                if (File.Exists(pWavePath))
                {
                    PWaveField = ChunkedVolume.LoadFromBinAsync(pWavePath, false).Result;
                }
                
                string sWavePath = Path.Combine(FilePath, "SWaveField.bin");
                if (File.Exists(sWavePath))
                {
                    SWaveField = ChunkedVolume.LoadFromBinAsync(sWavePath, false).Result;
                }
                
                string combinedPath = Path.Combine(FilePath, "CombinedField.bin");
                if (File.Exists(combinedPath))
                {
                    CombinedWaveField = ChunkedVolume.LoadFromBinAsync(combinedPath, false).Result;
                }
                
                // Load damage field
                string damagePath = Path.Combine(FilePath, "DamageField.bin");
                if (File.Exists(damagePath))
                {
                    DamageField = ChunkedVolume.LoadFromBinAsync(damagePath, false).Result;
                }
                
                // Load metadata
                string metadataPath = Path.Combine(FilePath, "metadata.json");
                if (File.Exists(metadataPath))
                {
                    LoadMetadata(metadataPath);
                }
                
                // Load calibration data
                string calibrationPath = Path.Combine(FilePath, "calibration.json");
                if (File.Exists(calibrationPath))
                {
                    LoadCalibration(calibrationPath);
                }
                
                // Load time series if available
                LoadTimeSeries();
                
                Logger.Log($"[AcousticVolumeDataset] Loaded acoustic volume: {Name}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[AcousticVolumeDataset] Failed to load: {ex.Message}");
                IsMissing = true;
            }
        }
        
        public override void Unload()
        {
            PWaveField?.Dispose();
            SWaveField?.Dispose();
            CombinedWaveField?.Dispose();
            DamageField?.Dispose();
            TimeSeriesSnapshots?.Clear();
            
            PWaveField = null;
            SWaveField = null;
            CombinedWaveField = null;
            DamageField = null;
            
            Logger.Log($"[AcousticVolumeDataset] Unloaded: {Name}");
        }
        
        public void SaveWaveFields()
        {
            if (!Directory.Exists(FilePath))
            {
                Directory.CreateDirectory(FilePath);
            }
            
            try
            {
                if (PWaveField != null)
                {
                    string pWavePath = Path.Combine(FilePath, "PWaveField.bin");
                    PWaveField.SaveAsBin(pWavePath);
                }
                
                if (SWaveField != null)
                {
                    string sWavePath = Path.Combine(FilePath, "SWaveField.bin");
                    SWaveField.SaveAsBin(sWavePath);
                }
                
                if (CombinedWaveField != null)
                {
                    string combinedPath = Path.Combine(FilePath, "CombinedField.bin");
                    CombinedWaveField.SaveAsBin(combinedPath);
                }
                
                if (DamageField != null)
                {
                    string damagePath = Path.Combine(FilePath, "DamageField.bin");
                    DamageField.SaveAsBin(damagePath);
                }
                
                SaveMetadata();
                SaveCalibration();
                SaveTimeSeries();
                
                Logger.Log($"[AcousticVolumeDataset] Saved wave fields to: {FilePath}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[AcousticVolumeDataset] Failed to save: {ex.Message}");
            }
        }
        
        private void LoadMetadata(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                var metadata = System.Text.Json.JsonSerializer.Deserialize<AcousticMetadata>(json);
                
                PWaveVelocity = metadata.PWaveVelocity;
                SWaveVelocity = metadata.SWaveVelocity;
                VpVsRatio = metadata.VpVsRatio;
                TimeSteps = metadata.TimeSteps;
                ComputationTime = TimeSpan.FromSeconds(metadata.ComputationTimeSeconds);
                YoungsModulusMPa = metadata.YoungsModulusMPa;
                PoissonRatio = metadata.PoissonRatio;
                ConfiningPressureMPa = metadata.ConfiningPressureMPa;
                SourceFrequencyKHz = metadata.SourceFrequencyKHz;
                SourceEnergyJ = metadata.SourceEnergyJ;
                SourceDatasetPath = metadata.SourceDatasetPath;
                SourceMaterialName = metadata.SourceMaterialName;
                TensileStrengthMPa = metadata.TensileStrengthMPa;
                CohesionMPa = metadata.CohesionMPa;
                FailureAngleDeg = metadata.FailureAngleDeg;
                MaxDamage = metadata.MaxDamage;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[AcousticVolumeDataset] Failed to load metadata: {ex.Message}");
            }
        }
        
        private void SaveMetadata()
        {
            try
            {
                var metadata = new AcousticMetadata
                {
                    PWaveVelocity = PWaveVelocity,
                    SWaveVelocity = SWaveVelocity,
                    VpVsRatio = VpVsRatio,
                    TimeSteps = TimeSteps,
                    ComputationTimeSeconds = ComputationTime.TotalSeconds,
                    YoungsModulusMPa = YoungsModulusMPa,
                    PoissonRatio = PoissonRatio,
                    ConfiningPressureMPa = ConfiningPressureMPa,
                    SourceFrequencyKHz = SourceFrequencyKHz,
                    SourceEnergyJ = SourceEnergyJ,
                    SourceDatasetPath = SourceDatasetPath,
                    SourceMaterialName = SourceMaterialName,
                    TensileStrengthMPa = TensileStrengthMPa,
                    CohesionMPa = CohesionMPa,
                    FailureAngleDeg = FailureAngleDeg,
                    MaxDamage = MaxDamage
                };
                
                string json = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                string path = Path.Combine(FilePath, "metadata.json");
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[AcousticVolumeDataset] Failed to save metadata: {ex.Message}");
            }
        }
        
        private void LoadCalibration(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                Calibration = System.Text.Json.JsonSerializer.Deserialize<CalibrationData>(json);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[AcousticVolumeDataset] Failed to load calibration: {ex.Message}");
            }
        }
        
        private void SaveCalibration()
        {
            if (Calibration == null || Calibration.Points.Count == 0)
                return;
                
            try
            {
                string json = System.Text.Json.JsonSerializer.Serialize(Calibration, 
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                string path = Path.Combine(FilePath, "calibration.json");
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[AcousticVolumeDataset] Failed to save calibration: {ex.Message}");
            }
        }
        
        private void LoadTimeSeries()
        {
            string timeSeriesDir = Path.Combine(FilePath, "TimeSeries");
            if (!Directory.Exists(timeSeriesDir))
                return;
                
            TimeSeriesSnapshots.Clear();
            
            var files = Directory.GetFiles(timeSeriesDir, "snapshot_*.bin")
                .OrderBy(f => int.Parse(Path.GetFileNameWithoutExtension(f).Replace("snapshot_", "")))
                .ToList();
                
            foreach (var file in files)
            {
                try
                {
                    var snapshot = WaveFieldSnapshot.LoadFromFile(file);
                    if (snapshot != null)
                    {
                        TimeSeriesSnapshots.Add(snapshot);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[AcousticVolumeDataset] Failed to load snapshot {file}: {ex.Message}");
                }
            }
            
            Logger.Log($"[AcousticVolumeDataset] Loaded {TimeSeriesSnapshots.Count} time series snapshots");
        }
        
        private void SaveTimeSeries()
        {
            if (TimeSeriesSnapshots.Count == 0)
                return;
                
            string timeSeriesDir = Path.Combine(FilePath, "TimeSeries");
            Directory.CreateDirectory(timeSeriesDir);
            
            for (int i = 0; i < TimeSeriesSnapshots.Count; i++)
            {
                string path = Path.Combine(timeSeriesDir, $"snapshot_{i:D6}.bin");
                TimeSeriesSnapshots[i].SaveToFile(path);
            }
        }
        
        public void AddTimeSeriesSnapshot(float[,,] vx, float[,,] vy, float[,,] vz, int timeStep, float simTime)
        {
            var snapshot = new WaveFieldSnapshot
            {
                TimeStep = timeStep,
                SimulationTime = simTime,
                Width = vx.GetLength(0),
                Height = vx.GetLength(1),
                Depth = vx.GetLength(2)
            };
            
            snapshot.SetVelocityFields(vx, vy, vz);
            TimeSeriesSnapshots.Add(snapshot);
        }
        
        public WaveFieldSnapshot GetSnapshotAtTime(float time)
        {
            if (TimeSeriesSnapshots.Count == 0)
                return null;
                
            // Find the closest snapshot
            return TimeSeriesSnapshots
                .OrderBy(s => Math.Abs(s.SimulationTime - time))
                .FirstOrDefault();
        }
        
        public object ToSerializableObject()
        {
            return new AcousticVolumeDatasetDTO
            {
                TypeName = nameof(AcousticVolumeDataset),
                Name = Name,
                FilePath = FilePath,
                PWaveVelocity = PWaveVelocity,
                SWaveVelocity = SWaveVelocity,
                VpVsRatio = VpVsRatio,
                TimeSteps = TimeSteps,
                ComputationTimeSeconds = ComputationTime.TotalSeconds,
                YoungsModulusMPa = YoungsModulusMPa,
                PoissonRatio = PoissonRatio,
                ConfiningPressureMPa = ConfiningPressureMPa,
                SourceFrequencyKHz = SourceFrequencyKHz,
                SourceEnergyJ = SourceEnergyJ,
                SourceDatasetPath = SourceDatasetPath,
                SourceMaterialName = SourceMaterialName,
                HasTimeSeries = TimeSeriesSnapshots.Count > 0,
                HasDamageField = DamageField != null,
                HasCalibration = Calibration != null && Calibration.Points.Count > 0
            };
        }
    }
    
    /// <summary>
    /// Calibration data for acoustic simulations
    /// </summary>
    public class CalibrationData
    {
        public List<CalibrationPoint> Points { get; set; } = new List<CalibrationPoint>();
        public DateTime LastUpdated { get; set; }
        public string CalibrationMethod { get; set; }
        
        public void AddPoint(CalibrationPoint point)
        {
            Points.Add(point);
            LastUpdated = DateTime.Now;
        }
        
        public CalibrationPoint GetClosestPoint(float density, float confiningPressure)
        {
            if (Points.Count == 0) return null;
            
            return Points
                .OrderBy(p => Math.Abs(p.Density - density) + Math.Abs(p.ConfiningPressureMPa - confiningPressure))
                .FirstOrDefault();
        }
        
        public (float YoungsModulus, float PoissonRatio) InterpolateParameters(float density, float confiningPressure)
        {
            if (Points.Count == 0)
                return (30000.0f, 0.25f); // Default values
            
            if (Points.Count == 1)
                return (Points[0].YoungsModulusMPa, Points[0].PoissonRatio);
            
            // Find two closest points
            var closestPoints = Points
                .OrderBy(p => Math.Abs(p.Density - density) + Math.Abs(p.ConfiningPressureMPa - confiningPressure))
                .Take(2)
                .ToList();
            
            var p1 = closestPoints[0];
            var p2 = closestPoints[1];
            
            float totalDist = Math.Abs(p1.Density - density) + Math.Abs(p2.Density - density);
            if (totalDist < 0.001f)
                return (p1.YoungsModulusMPa, p1.PoissonRatio);
            
            float weight1 = 1.0f - (Math.Abs(p1.Density - density) / totalDist);
            float weight2 = 1.0f - weight1;
            
            float E = p1.YoungsModulusMPa * weight1 + p2.YoungsModulusMPa * weight2;
            float nu = p1.PoissonRatio * weight1 + p2.PoissonRatio * weight2;
            
            return (E, nu);
        }
    }
    
    public class CalibrationPoint
    {
        public string MaterialName { get; set; }
        public byte MaterialID { get; set; }
        public float Density { get; set; }
        public float ConfiningPressureMPa { get; set; }
        public float YoungsModulusMPa { get; set; }
        public float PoissonRatio { get; set; }
        public double MeasuredVp { get; set; }
        public double MeasuredVs { get; set; }
        public double MeasuredVpVsRatio { get; set; }
        public double SimulatedVp { get; set; }
        public double SimulatedVs { get; set; }
        public double SimulatedVpVsRatio { get; set; }
        public DateTime Timestamp { get; set; }
        public string Notes { get; set; }
    }
    
    /// <summary>
    /// Metadata structure for acoustic simulation
    /// </summary>
    public class AcousticMetadata
    {
        public double PWaveVelocity { get; set; }
        public double SWaveVelocity { get; set; }
        public double VpVsRatio { get; set; }
        public int TimeSteps { get; set; }
        public double ComputationTimeSeconds { get; set; }
        public float YoungsModulusMPa { get; set; }
        public float PoissonRatio { get; set; }
        public float ConfiningPressureMPa { get; set; }
        public float SourceFrequencyKHz { get; set; }
        public float SourceEnergyJ { get; set; }
        public string SourceDatasetPath { get; set; }
        public string SourceMaterialName { get; set; }
        public float TensileStrengthMPa { get; set; }
        public float CohesionMPa { get; set; }
        public float FailureAngleDeg { get; set; }
        public float MaxDamage { get; set; }
    }
    
    /// <summary>
    /// DTO for serialization
    /// </summary>
    public class AcousticVolumeDatasetDTO : DatasetDTO
    {
        public double PWaveVelocity { get; set; }
        public double SWaveVelocity { get; set; }
        public double VpVsRatio { get; set; }
        public int TimeSteps { get; set; }
        public double ComputationTimeSeconds { get; set; }
        public float YoungsModulusMPa { get; set; }
        public float PoissonRatio { get; set; }
        public float ConfiningPressureMPa { get; set; }
        public float SourceFrequencyKHz { get; set; }
        public float SourceEnergyJ { get; set; }
        public string SourceDatasetPath { get; set; }
        public string SourceMaterialName { get; set; }
        public bool HasTimeSeries { get; set; }
        public bool HasDamageField { get; set; }
        public bool HasCalibration { get; set; }
    }
    
   public class WaveFieldSnapshot
    {
        public int TimeStep { get; set; }
        public float SimulationTime { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Depth { get; set; }
        
        // Compressed storage for velocity fields
        private byte[] _compressedVx;
        private byte[] _compressedVy;
        private byte[] _compressedVz;
        private float _minValue;
        private float _maxValue;
        
        public void SetVelocityFields(float[,,] vx, float[,,] vy, float[,,] vz)
        {
            // Find min/max for normalization
            _minValue = float.MaxValue;
            _maxValue = float.MinValue;
            
            for (int z = 0; z < Depth; z++)
            {
                for (int y = 0; y < Height; y++)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        UpdateMinMax(vx[x, y, z]);
                        UpdateMinMax(vy[x, y, z]);
                        UpdateMinMax(vz[x, y, z]);
                    }
                }
            }
            
            // Compress to bytes
            _compressedVx = CompressField(vx);
            _compressedVy = CompressField(vy);
            _compressedVz = CompressField(vz);
        }
        
        private void UpdateMinMax(float value)
        {
            if (value < _minValue) _minValue = value;
            if (value > _maxValue) _maxValue = value;
        }
        
        private byte[] CompressField(float[,,] field)
        {
            int size = Width * Height * Depth;
            byte[] compressed = new byte[size];
            float range = _maxValue - _minValue;
            if (range < 1e-6f) range = 1e-6f;
            
            int idx = 0;
            for (int z = 0; z < Depth; z++)
            {
                for (int y = 0; y < Height; y++)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        float normalized = (field[x, y, z] - _minValue) / range;
                        compressed[idx++] = (byte)(normalized * 255);
                    }
                }
            }
            
            return compressed;
        }
        
        public float[,,] GetVelocityField(int component)
        {
            byte[] compressed = component switch
            {
                0 => _compressedVx,
                1 => _compressedVy,
                2 => _compressedVz,
                _ => null
            };
            
            if (compressed == null)
                return null;
                
            return DecompressField(compressed);
        }
        
        private float[,,] DecompressField(byte[] compressed)
        {
            float[,,] field = new float[Width, Height, Depth];
            float range = _maxValue - _minValue;
            
            int idx = 0;
            for (int z = 0; z < Depth; z++)
            {
                for (int y = 0; y < Height; y++)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        float normalized = compressed[idx++] / 255f;
                        field[x, y, z] = _minValue + normalized * range;
                    }
                }
            }
            
            return field;
        }
        
        public long GetSizeInBytes()
        {
            long size = 0;
            if (_compressedVx != null) size += _compressedVx.Length;
            if (_compressedVy != null) size += _compressedVy.Length;
            if (_compressedVz != null) size += _compressedVz.Length;
            size += sizeof(int) * 4; // TimeStep, Width, Height, Depth
            size += sizeof(float) * 3; // SimulationTime, _minValue, _maxValue
            return size;
        }
        
        public void SaveToFile(string path)
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(TimeStep);
                writer.Write(SimulationTime);
                writer.Write(Width);
                writer.Write(Height);
                writer.Write(Depth);
                writer.Write(_minValue);
                writer.Write(_maxValue);
                
                writer.Write(_compressedVx?.Length ?? 0);
                if (_compressedVx != null) writer.Write(_compressedVx);
                
                writer.Write(_compressedVy?.Length ?? 0);
                if (_compressedVy != null) writer.Write(_compressedVy);
                
                writer.Write(_compressedVz?.Length ?? 0);
                if (_compressedVz != null) writer.Write(_compressedVz);
            }
        }
        
        public static WaveFieldSnapshot LoadFromFile(string path)
        {
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                var snapshot = new WaveFieldSnapshot
                {
                    TimeStep = reader.ReadInt32(),
                    SimulationTime = reader.ReadSingle(),
                    Width = reader.ReadInt32(),
                    Height = reader.ReadInt32(),
                    Depth = reader.ReadInt32(),
                    _minValue = reader.ReadSingle(),
                    _maxValue = reader.ReadSingle()
                };
                
                int vxLength = reader.ReadInt32();
                if (vxLength > 0)
                    snapshot._compressedVx = reader.ReadBytes(vxLength);
                    
                int vyLength = reader.ReadInt32();
                if (vyLength > 0)
                    snapshot._compressedVy = reader.ReadBytes(vyLength);
                    
                int vzLength = reader.ReadInt32();
                if (vzLength > 0)
                    snapshot._compressedVz = reader.ReadBytes(vzLength);
                    
                return snapshot;
            }
        }
    }
}