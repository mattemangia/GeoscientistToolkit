using System;
using System.Collections.Generic;
using System.Numerics;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// Monitoring point for tracking displacement, velocity, and acceleration at specific locations.
    /// Similar to instrumentation in real slope monitoring systems.
    /// </summary>
    public class MonitoringPoint
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public Vector3 Position { get; set; }
        public MonitoringPointType Type { get; set; }

        // Time history data
        public List<MonitoringDataPoint> TimeHistory { get; set; }

        // Alarm thresholds
        public float DisplacementAlarmThreshold { get; set; }  // meters
        public float VelocityAlarmThreshold { get; set; }      // m/s
        public bool IsAlarmTriggered { get; set; }
        public float TimeOfAlarm { get; set; }

        // Statistics
        public float MaxDisplacement { get; set; }
        public float MaxVelocity { get; set; }
        public float CumulativeDisplacement { get; set; }

        public MonitoringPoint()
        {
            TimeHistory = new List<MonitoringDataPoint>();
            DisplacementAlarmThreshold = 0.05f;  // 5 cm default
            VelocityAlarmThreshold = 0.001f;     // 1 mm/s default
            IsAlarmTriggered = false;
        }

        /// <summary>
        /// Records data at the current time step.
        /// </summary>
        public void RecordData(float time, Vector3 displacement, Vector3 velocity, Vector3 acceleration)
        {
            var dataPoint = new MonitoringDataPoint
            {
                Time = time,
                Displacement = displacement,
                Velocity = velocity,
                Acceleration = acceleration,
                DisplacementMagnitude = displacement.Length(),
                VelocityMagnitude = velocity.Length(),
                AccelerationMagnitude = acceleration.Length()
            };

            TimeHistory.Add(dataPoint);

            // Update statistics
            MaxDisplacement = Math.Max(MaxDisplacement, dataPoint.DisplacementMagnitude);
            MaxVelocity = Math.Max(MaxVelocity, dataPoint.VelocityMagnitude);

            if (TimeHistory.Count > 1)
            {
                var prev = TimeHistory[TimeHistory.Count - 2];
                CumulativeDisplacement += (dataPoint.Displacement - prev.Displacement).Length();
            }

            // Check alarms
            if (!IsAlarmTriggered)
            {
                if (dataPoint.DisplacementMagnitude > DisplacementAlarmThreshold ||
                    dataPoint.VelocityMagnitude > VelocityAlarmThreshold)
                {
                    IsAlarmTriggered = true;
                    TimeOfAlarm = time;
                }
            }
        }

        /// <summary>
        /// Gets displacement rate (velocity) between two time points.
        /// </summary>
        public float GetDisplacementRate(float timeWindow = 1.0f)
        {
            if (TimeHistory.Count < 2)
                return 0.0f;

            var recent = TimeHistory[TimeHistory.Count - 1];
            var past = TimeHistory.FindLast(d => recent.Time - d.Time >= timeWindow);

            if (past == null)
                past = TimeHistory[0];

            float deltaTime = recent.Time - past.Time;
            if (deltaTime < 1e-6f)
                return 0.0f;

            return (recent.Displacement - past.Displacement).Length() / deltaTime;
        }

        /// <summary>
        /// Exports time history to CSV format.
        /// </summary>
        public string ExportToCSV()
        {
            var csv = new System.Text.StringBuilder();
            csv.AppendLine($"# Monitoring Point: {Name}");
            csv.AppendLine($"# Position: ({Position.X}, {Position.Y}, {Position.Z})");
            csv.AppendLine($"# Type: {Type}");
            csv.AppendLine("Time,DisplacementX,DisplacementY,DisplacementZ,DisplacementMag," +
                          "VelocityX,VelocityY,VelocityZ,VelocityMag," +
                          "AccelerationX,AccelerationY,AccelerationZ,AccelerationMag");

            foreach (var point in TimeHistory)
            {
                csv.AppendLine($"{point.Time}," +
                    $"{point.Displacement.X},{point.Displacement.Y},{point.Displacement.Z},{point.DisplacementMagnitude}," +
                    $"{point.Velocity.X},{point.Velocity.Y},{point.Velocity.Z},{point.VelocityMagnitude}," +
                    $"{point.Acceleration.X},{point.Acceleration.Y},{point.Acceleration.Z},{point.AccelerationMagnitude}");
            }

            return csv.ToString();
        }
    }

    /// <summary>
    /// Single data point from monitoring.
    /// </summary>
    public class MonitoringDataPoint
    {
        public float Time { get; set; }
        public Vector3 Displacement { get; set; }
        public Vector3 Velocity { get; set; }
        public Vector3 Acceleration { get; set; }
        public float DisplacementMagnitude { get; set; }
        public float VelocityMagnitude { get; set; }
        public float AccelerationMagnitude { get; set; }
    }

    /// <summary>
    /// Type of monitoring point.
    /// </summary>
    public enum MonitoringPointType
    {
        Inclinometer,      // Measures lateral displacement
        Extensometer,      // Measures extension/compression
        Piezometer,        // Measures pore pressure
        GPS,               // Measures 3D displacement
        Accelerometer,     // Measures acceleration
        CrackMeter,        // Measures crack opening
        TotalStation       // Measures 3D position
    }

    /// <summary>
    /// Manager for multiple monitoring points.
    /// </summary>
    public class MonitoringSystem
    {
        public List<MonitoringPoint> MonitoringPoints { get; set; }
        private readonly SlopeStabilityDataset _dataset;

        public MonitoringSystem(SlopeStabilityDataset dataset)
        {
            _dataset = dataset;
            MonitoringPoints = new List<MonitoringPoint>();
        }

        /// <summary>
        /// Adds a monitoring point at a specific location.
        /// </summary>
        public MonitoringPoint AddMonitoringPoint(
            Vector3 position,
            string name,
            MonitoringPointType type = MonitoringPointType.GPS)
        {
            var point = new MonitoringPoint
            {
                Id = MonitoringPoints.Count,
                Name = name,
                Position = position,
                Type = type
            };

            MonitoringPoints.Add(point);
            return point;
        }

        /// <summary>
        /// Updates all monitoring points with current simulation state.
        /// </summary>
        public void UpdateMonitoringPoints(float currentTime, List<Block> blocks)
        {
            foreach (var point in MonitoringPoints)
            {
                // Find nearest block to monitoring point
                var nearestBlock = FindNearestBlock(point.Position, blocks);

                if (nearestBlock != null)
                {
                    point.RecordData(
                        currentTime,
                        nearestBlock.TotalDisplacement,
                        nearestBlock.Velocity,
                        nearestBlock.Acceleration);
                }
            }
        }

        /// <summary>
        /// Checks if any alarms have been triggered.
        /// </summary>
        public List<MonitoringPoint> GetTriggeredAlarms()
        {
            return MonitoringPoints.FindAll(p => p.IsAlarmTriggered);
        }

        /// <summary>
        /// Exports all monitoring data to CSV files.
        /// </summary>
        public void ExportAll(string outputDirectory)
        {
            if (!System.IO.Directory.Exists(outputDirectory))
                System.IO.Directory.CreateDirectory(outputDirectory);

            foreach (var point in MonitoringPoints)
            {
                string filename = System.IO.Path.Combine(
                    outputDirectory,
                    $"monitoring_{point.Name.Replace(" ", "_")}.csv");

                System.IO.File.WriteAllText(filename, point.ExportToCSV());
            }

            // Export summary
            var summary = new System.Text.StringBuilder();
            summary.AppendLine("Monitoring System Summary");
            summary.AppendLine($"Total Points: {MonitoringPoints.Count}");
            summary.AppendLine($"Alarms Triggered: {GetTriggeredAlarms().Count}");
            summary.AppendLine();
            summary.AppendLine("Point,Type,MaxDisplacement(m),MaxVelocity(m/s),AlarmStatus");

            foreach (var point in MonitoringPoints)
            {
                summary.AppendLine($"{point.Name},{point.Type}," +
                    $"{point.MaxDisplacement:F6},{point.MaxVelocity:F6}," +
                    $"{(point.IsAlarmTriggered ? $"ALARM@{point.TimeOfAlarm:F2}s" : "OK")}");
            }

            System.IO.File.WriteAllText(
                System.IO.Path.Combine(outputDirectory, "monitoring_summary.txt"),
                summary.ToString());
        }

        private Block FindNearestBlock(Vector3 position, List<Block> blocks)
        {
            Block nearest = null;
            float minDistance = float.MaxValue;

            foreach (var block in blocks)
            {
                float distance = (block.Position - position).Length();
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearest = block;
                }
            }

            return nearest;
        }
    }
}
