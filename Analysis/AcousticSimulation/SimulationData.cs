// GeoscientistToolkit/Analysis/AcousticSimulation/SimulationData.cs

using System.Text;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.AcousticSimulation;

#region Supporting Classes

/// <summary>
///     Holds the final results of an acoustic simulation run.
/// </summary>
public class SimulationResults
{
    public float[,,] DamageField { get; set; }
    public double PWaveVelocity { get; set; }
    public double SWaveVelocity { get; set; }
    public double VpVsRatio { get; set; }
    public int PWaveTravelTime { get; set; }
    public int SWaveTravelTime { get; set; }
    public int TotalTimeSteps { get; set; }
    public TimeSpan ComputationTime { get; set; }
    public float[,,] WaveFieldVx { get; set; }
    public float[,,] WaveFieldVy { get; set; }
    public float[,,] WaveFieldVz { get; set; }
    public List<WaveFieldSnapshot> TimeSeriesSnapshots { get; set; }

    /// <summary>
    ///     A general-purpose property to hold contextual data, like the labels for tomography.
    /// </summary>
    public object Context { get; set; }
}

/// <summary>
///     Provides progress information during a simulation.
/// </summary>
public class SimulationProgressEventArgs : EventArgs
{
    public float Progress { get; set; }
    public int Step { get; set; }
    public string Message { get; set; }
}

/// <summary>
///     Provides real-time data for a single processed chunk for visualization updates.
/// </summary>
public class WaveFieldUpdateEventArgs : EventArgs
{
    /// <summary>
    ///     The velocity fields (Vx, Vy, Vz) for the updated chunk.
    /// </summary>
    public (float[,,] Vx, float[,,] Vy, float[,,] Vz) ChunkVelocityFields { get; set; }

    /// <summary>
    ///     The global starting Z-index of this chunk.
    /// </summary>
    public int ChunkStartZ { get; set; }

    /// <summary>
    ///     The depth (number of slices) of this chunk.
    /// </summary>
    public int ChunkDepth { get; set; }

    public int TimeStep { get; set; }
    public float SimTime { get; set; }
}

/// <summary>
///     Container for a single wave field snapshot in time.
/// </summary>
public class WaveFieldSnapshot
{
    /// <summary>
    ///     Time step index when snapshot was taken.
    /// </summary>
    public int TimeStep { get; set; }
    
    /// <summary>
    ///     Physical simulation time in seconds.
    /// </summary>
    public float SimulationTime { get; set; }
    
    /// <summary>
    ///     Full velocity magnitude field (for normal datasets).
    /// </summary>
    public float[,,] VelocityField { get; set; }
    
    /// <summary>
    ///     Maximum velocity field (for huge datasets - memory efficient).
    /// </summary>
    public float[,,] MaxVelocityField { get; set; }
    
    /// <summary>
    ///     Saves snapshot to a binary file.
    /// </summary>
    public void SaveToFile(string path)
    {
        using var writer = new BinaryWriter(File.Create(path));
        
        var field = VelocityField ?? MaxVelocityField;
        if (field == null) return;
        
        writer.Write(field.GetLength(0));
        writer.Write(field.GetLength(1));
        writer.Write(field.GetLength(2));
        writer.Write(TimeStep);
        writer.Write(SimulationTime);
        
        var buffer = new byte[field.Length * sizeof(float)];
        Buffer.BlockCopy(field, 0, buffer, 0, buffer.Length);
        writer.Write(buffer);
    }
    
    /// <summary>
    ///     Loads snapshot from a binary file.
    /// </summary>
    public static WaveFieldSnapshot LoadFromFile(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        
        int w = reader.ReadInt32();
        int h = reader.ReadInt32();
        int d = reader.ReadInt32();
        int timeStep = reader.ReadInt32();
        float simTime = reader.ReadSingle();
        
        var field = new float[w, h, d];
        var buffer = new byte[field.Length * sizeof(float)];
        reader.Read(buffer, 0, buffer.Length);
        Buffer.BlockCopy(buffer, 0, field, 0, buffer.Length);
        
        return new WaveFieldSnapshot
        {
            TimeStep = timeStep,
            SimulationTime = simTime,
            VelocityField = field
        };
    }
}

/// <summary>
///     Manages a collection of simulation results to calibrate material properties.
/// </summary>
internal class CalibrationManager
{
    private readonly List<CalibrationPoint> _calibrationPoints = new();

    /// <summary>
    ///     Adds a new calibration data point from a completed simulation.
    /// </summary>
    public void AddCalibrationPoint(string materialName, byte materialID, float density,
        float confiningPressure, float youngsModulus, float poissonRatio,
        double vp, double vs, double vpVsRatio)
    {
        _calibrationPoints.Add(new CalibrationPoint
        {
            MaterialName = materialName,
            MaterialID = materialID,
            Density = density,
            ConfiningPressureMPa = confiningPressure,
            YoungsModulusMPa = youngsModulus,
            PoissonRatio = poissonRatio,
            MeasuredVp = vp,
            MeasuredVs = vs,
            MeasuredVpVsRatio = vpVsRatio,
            Timestamp = DateTime.Now
        });

        Logger.Log($"[CalibrationManager] Added calibration point for {materialName}");
    }

    /// <summary>
    ///     Checks if there is enough data to perform a calibration.
    /// </summary>
    public bool HasCalibration()
    {
        return _calibrationPoints.Count >= 2;
    }

    /// <summary>
    ///     Gets calibrated material parameters by interpolating from the nearest data points.
    /// </summary>
    public (float YoungsModulus, float PoissonRatio) GetCalibratedParameters(
        float density, float confiningPressure)
    {
        if (_calibrationPoints.Count < 2)
            return (30000.0f, 0.25f); // Default values

        // Find the two closest calibration points based on density and pressure
        var closestPoints = _calibrationPoints
            .OrderBy(p => Math.Abs(p.Density - density) + Math.Abs(p.ConfiningPressureMPa - confiningPressure))
            .Take(2)
            .ToList();

        if (closestPoints.Count == 0)
            return (30000.0f, 0.25f);

        // If only one point, return its values
        if (closestPoints.Count == 1)
            return (closestPoints[0].YoungsModulusMPa, closestPoints[0].PoissonRatio);

        var p1 = closestPoints[0];
        var p2 = closestPoints[1];

        // Perform linear interpolation
        var totalDist = Math.Abs(p1.Density - density) + Math.Abs(p2.Density - density);
        if (totalDist < 0.001f)
            return (p1.YoungsModulusMPa, p1.PoissonRatio);

        var weight1 = 1.0f - Math.Abs(p1.Density - density) / totalDist;
        var weight2 = 1.0f - weight1;

        var E = p1.YoungsModulusMPa * weight1 + p2.YoungsModulusMPa * weight2;
        var nu = p1.PoissonRatio * weight1 + p2.PoissonRatio * weight2;

        return (E, nu);
    }

    /// <summary>
    ///     Generates a summary string of the current calibration data.
    /// </summary>
    public string GetCalibrationSummary()
    {
        if (_calibrationPoints.Count == 0)
            return "No calibration points available.";

        var sb = new StringBuilder();
        sb.AppendLine($"Calibration Points: {_calibrationPoints.Count}");

        foreach (var point in _calibrationPoints.Take(3))
            sb.AppendLine($"  • {point.MaterialName}: Vp/Vs={point.MeasuredVpVsRatio:F3}, ρ={point.Density:F1} g/cm³");

        if (_calibrationPoints.Count > 3)
            sb.AppendLine($"  ... and {_calibrationPoints.Count - 3} more");

        return sb.ToString();
    }

    /// <summary>
    ///     Internal data structure for a single calibration point.
    /// </summary>
    private class CalibrationPoint
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
        public DateTime Timestamp { get; set; }
    }
}

#endregion