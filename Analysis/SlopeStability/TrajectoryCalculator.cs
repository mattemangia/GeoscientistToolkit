using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// Calculates and tracks trajectories of selected blocks during simulation.
    /// This is essential for hazard analysis, runout distance prediction, and impact zone mapping.
    /// Similar to trajectory tracking in RocFall and other rockfall analysis software.
    /// </summary>
    public class TrajectoryCalculator
    {
        private Dictionary<int, BlockTrajectory> _trajectories = new Dictionary<int, BlockTrajectory>();
        private HashSet<int> _trackedBlockIds = new HashSet<int>();
        private float _recordingInterval = 0.01f;  // Record every 10 ms
        private float _lastRecordTime = 0.0f;

        /// <summary>
        /// Sets which blocks should have their trajectories tracked.
        /// </summary>
        public void SetTrackedBlocks(List<int> blockIds)
        {
            _trackedBlockIds = new HashSet<int>(blockIds);

            // Initialize trajectories for new blocks
            foreach (var blockId in blockIds)
            {
                if (!_trajectories.ContainsKey(blockId))
                {
                    _trajectories[blockId] = new BlockTrajectory
                    {
                        BlockId = blockId,
                        Waypoints = new List<TrajectoryWaypoint>()
                    };
                }
            }
        }

        /// <summary>
        /// Sets the time interval for recording waypoints (in seconds).
        /// Smaller intervals give more detailed trajectories but use more memory.
        /// </summary>
        public void SetRecordingInterval(float intervalSeconds)
        {
            _recordingInterval = Math.Max(0.001f, intervalSeconds);
        }

        /// <summary>
        /// Records the current state of tracked blocks.
        /// Call this from the simulation loop at each timestep.
        /// </summary>
        public void RecordTimestep(float currentTime, List<Block> blocks)
        {
            // Only record at specified intervals to reduce memory usage
            if (currentTime - _lastRecordTime < _recordingInterval)
                return;

            _lastRecordTime = currentTime;

            foreach (var block in blocks)
            {
                if (_trackedBlockIds.Contains(block.Id))
                {
                    if (!_trajectories.ContainsKey(block.Id))
                    {
                        _trajectories[block.Id] = new BlockTrajectory
                        {
                            BlockId = block.Id,
                            Waypoints = new List<TrajectoryWaypoint>()
                        };
                    }

                    var waypoint = new TrajectoryWaypoint
                    {
                        Time = currentTime,
                        Position = block.Position,
                        Velocity = block.Velocity,
                        Acceleration = block.Acceleration,
                        AngularVelocity = block.AngularVelocity,
                        Displacement = block.TotalDisplacement
                    };

                    _trajectories[block.Id].Waypoints.Add(waypoint);
                }
            }
        }

        /// <summary>
        /// Calculates statistics for all tracked trajectories.
        /// </summary>
        public Dictionary<int, TrajectoryStatistics> CalculateStatistics()
        {
            var stats = new Dictionary<int, TrajectoryStatistics>();

            foreach (var kvp in _trajectories)
            {
                stats[kvp.Key] = CalculateTrajectoryStatistics(kvp.Value);
            }

            return stats;
        }

        /// <summary>
        /// Calculates statistics for a single trajectory.
        /// </summary>
        private TrajectoryStatistics CalculateTrajectoryStatistics(BlockTrajectory trajectory)
        {
            if (trajectory.Waypoints.Count == 0)
                return new TrajectoryStatistics { BlockId = trajectory.BlockId };

            var stats = new TrajectoryStatistics
            {
                BlockId = trajectory.BlockId,
                TotalWaypoints = trajectory.Waypoints.Count,
                TotalDuration = trajectory.Waypoints.Last().Time - trajectory.Waypoints.First().Time
            };

            // Calculate runout distance (horizontal distance traveled)
            Vector3 startPos = trajectory.Waypoints.First().Position;
            Vector3 endPos = trajectory.Waypoints.Last().Position;

            stats.TotalDistance3D = (endPos - startPos).Length();
            stats.HorizontalRunout = MathF.Sqrt(
                (endPos.X - startPos.X) * (endPos.X - startPos.X) +
                (endPos.Y - startPos.Y) * (endPos.Y - startPos.Y));
            stats.VerticalDrop = startPos.Z - endPos.Z;

            // Find maximum values
            stats.MaxVelocity = trajectory.Waypoints.Max(w => w.Velocity.Length());
            stats.MaxAcceleration = trajectory.Waypoints.Max(w => w.Acceleration.Length());
            stats.MaxAngularVelocity = trajectory.Waypoints.Max(w => w.AngularVelocity.Length());

            // Find time of maximum velocity
            var maxVelWaypoint = trajectory.Waypoints.OrderByDescending(w => w.Velocity.Length()).First();
            stats.TimeOfMaxVelocity = maxVelWaypoint.Time;
            stats.PositionAtMaxVelocity = maxVelWaypoint.Position;

            // Calculate average velocity
            float totalVelocity = 0.0f;
            foreach (var waypoint in trajectory.Waypoints)
            {
                totalVelocity += waypoint.Velocity.Length();
            }
            stats.AverageVelocity = totalVelocity / trajectory.Waypoints.Count;

            // Calculate path length (actual distance traveled along path)
            float pathLength = 0.0f;
            for (int i = 1; i < trajectory.Waypoints.Count; i++)
            {
                pathLength += (trajectory.Waypoints[i].Position - trajectory.Waypoints[i - 1].Position).Length();
            }
            stats.PathLength = pathLength;

            // Calculate kinetic energy at impact (final waypoint)
            // Assuming impact is the last waypoint
            var finalWaypoint = trajectory.Waypoints.Last();
            float finalSpeed = finalWaypoint.Velocity.Length();
            // Energy = 0.5 * m * v² (would need mass from Block)
            stats.FinalVelocity = finalSpeed;
            stats.FinalPosition = finalWaypoint.Position;

            // Calculate H/L ratio (vertical drop / horizontal runout)
            // This is important for rockfall hazard assessment
            if (stats.HorizontalRunout > 0.001f)
            {
                stats.HeightToLengthRatio = stats.VerticalDrop / stats.HorizontalRunout;
                stats.ApparentFrictionAngle = MathF.Atan(stats.HeightToLengthRatio) * 180.0f / MathF.PI;
            }

            return stats;
        }

        /// <summary>
        /// Gets the trajectory for a specific block.
        /// </summary>
        public BlockTrajectory GetTrajectory(int blockId)
        {
            return _trajectories.ContainsKey(blockId) ? _trajectories[blockId] : null;
        }

        /// <summary>
        /// Gets all tracked trajectories.
        /// </summary>
        public Dictionary<int, BlockTrajectory> GetAllTrajectories()
        {
            return _trajectories;
        }

        /// <summary>
        /// Exports trajectory data to CSV file.
        /// </summary>
        public void ExportToCSV(string filePath, int? specificBlockId = null)
        {
            var sb = new StringBuilder();

            // Header
            sb.AppendLine("BlockID,Time,PosX,PosY,PosZ,VelX,VelY,VelZ,VelMag,AccX,AccY,AccZ,AccMag,DispX,DispY,DispZ,DispMag");

            var trajectoriesToExport = specificBlockId.HasValue
                ? _trajectories.Where(kvp => kvp.Key == specificBlockId.Value)
                : _trajectories;

            foreach (var kvp in trajectoriesToExport)
            {
                int blockId = kvp.Key;
                var trajectory = kvp.Value;

                foreach (var waypoint in trajectory.Waypoints)
                {
                    sb.AppendLine($"{blockId}," +
                        $"{waypoint.Time:F4}," +
                        $"{waypoint.Position.X:F4},{waypoint.Position.Y:F4},{waypoint.Position.Z:F4}," +
                        $"{waypoint.Velocity.X:F4},{waypoint.Velocity.Y:F4},{waypoint.Velocity.Z:F4},{waypoint.Velocity.Length():F4}," +
                        $"{waypoint.Acceleration.X:F4},{waypoint.Acceleration.Y:F4},{waypoint.Acceleration.Z:F4},{waypoint.Acceleration.Length():F4}," +
                        $"{waypoint.Displacement.X:F4},{waypoint.Displacement.Y:F4},{waypoint.Displacement.Z:F4},{waypoint.Displacement.Length():F4}");
                }
            }

            File.WriteAllText(filePath, sb.ToString());
            Console.WriteLine($"Exported trajectory data to {filePath}");
        }

        /// <summary>
        /// Exports trajectory statistics to CSV file.
        /// </summary>
        public void ExportStatisticsToCSV(string filePath)
        {
            var stats = CalculateStatistics();
            var sb = new StringBuilder();

            // Header
            sb.AppendLine("BlockID,TotalDuration,TotalDistance3D,PathLength,HorizontalRunout,VerticalDrop," +
                         "MaxVelocity,AverageVelocity,FinalVelocity,MaxAcceleration,MaxAngularVelocity," +
                         "TimeOfMaxVelocity,HeightToLengthRatio,ApparentFrictionAngle,TotalWaypoints");

            foreach (var stat in stats.Values)
            {
                sb.AppendLine($"{stat.BlockId}," +
                    $"{stat.TotalDuration:F4}," +
                    $"{stat.TotalDistance3D:F4}," +
                    $"{stat.PathLength:F4}," +
                    $"{stat.HorizontalRunout:F4}," +
                    $"{stat.VerticalDrop:F4}," +
                    $"{stat.MaxVelocity:F4}," +
                    $"{stat.AverageVelocity:F4}," +
                    $"{stat.FinalVelocity:F4}," +
                    $"{stat.MaxAcceleration:F4}," +
                    $"{stat.MaxAngularVelocity:F4}," +
                    $"{stat.TimeOfMaxVelocity:F4}," +
                    $"{stat.HeightToLengthRatio:F4}," +
                    $"{stat.ApparentFrictionAngle:F4}," +
                    $"{stat.TotalWaypoints}");
            }

            File.WriteAllText(filePath, sb.ToString());
            Console.WriteLine($"Exported trajectory statistics to {filePath}");
        }

        /// <summary>
        /// Clears all recorded trajectories.
        /// </summary>
        public void Clear()
        {
            _trajectories.Clear();
            _lastRecordTime = 0.0f;
        }

        /// <summary>
        /// Clears trajectory for a specific block.
        /// </summary>
        public void ClearTrajectory(int blockId)
        {
            if (_trajectories.ContainsKey(blockId))
            {
                _trajectories.Remove(blockId);
            }
        }
    }

    /// <summary>
    /// Represents a complete trajectory for a block.
    /// </summary>
    public class BlockTrajectory
    {
        public int BlockId { get; set; }
        public List<TrajectoryWaypoint> Waypoints { get; set; } = new List<TrajectoryWaypoint>();
    }

    /// <summary>
    /// Represents a single point along a trajectory.
    /// </summary>
    public class TrajectoryWaypoint
    {
        public float Time { get; set; }                 // seconds
        public Vector3 Position { get; set; }           // m
        public Vector3 Velocity { get; set; }           // m/s
        public Vector3 Acceleration { get; set; }       // m/s²
        public Vector3 AngularVelocity { get; set; }    // rad/s
        public Vector3 Displacement { get; set; }       // m (total displacement from start)
    }

    /// <summary>
    /// Statistical summary of a trajectory.
    /// </summary>
    public class TrajectoryStatistics
    {
        public int BlockId { get; set; }
        public int TotalWaypoints { get; set; }
        public float TotalDuration { get; set; }        // seconds

        // Distance measures
        public float TotalDistance3D { get; set; }      // m (straight-line distance)
        public float PathLength { get; set; }           // m (actual distance along path)
        public float HorizontalRunout { get; set; }     // m (horizontal distance traveled)
        public float VerticalDrop { get; set; }         // m (elevation change)

        // Velocity measures
        public float MaxVelocity { get; set; }          // m/s
        public float AverageVelocity { get; set; }      // m/s
        public float FinalVelocity { get; set; }        // m/s (velocity at impact/rest)

        // Acceleration measures
        public float MaxAcceleration { get; set; }      // m/s²
        public float MaxAngularVelocity { get; set; }   // rad/s

        // Key points
        public float TimeOfMaxVelocity { get; set; }    // seconds
        public Vector3 PositionAtMaxVelocity { get; set; }
        public Vector3 FinalPosition { get; set; }

        // Rockfall-specific measures
        public float HeightToLengthRatio { get; set; }  // H/L ratio (for hazard assessment)
        public float ApparentFrictionAngle { get; set; } // degrees (atan(H/L))

        public string GetSummary()
        {
            return $"Block {BlockId} Trajectory Summary:\n" +
                   $"  Duration: {TotalDuration:F2} s ({TotalWaypoints} waypoints)\n" +
                   $"  Runout: {HorizontalRunout:F2} m horizontal, {VerticalDrop:F2} m vertical drop\n" +
                   $"  Path length: {PathLength:F2} m\n" +
                   $"  Max velocity: {MaxVelocity:F2} m/s at t={TimeOfMaxVelocity:F2} s\n" +
                   $"  Final velocity: {FinalVelocity:F2} m/s\n" +
                   $"  H/L ratio: {HeightToLengthRatio:F3} (apparent friction angle: {ApparentFrictionAngle:F1}°)\n";
        }
    }
}
