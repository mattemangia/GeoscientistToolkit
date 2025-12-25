using System;
using System.Collections.Generic;
using System.Numerics;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// Results from a slope stability simulation.
    /// </summary>
    public class SlopeStabilityResults
    {
        // Simulation metadata
        public DateTime SimulationDate { get; set; }
        public float TotalSimulationTime { get; set; }  // seconds
        public int TotalSteps { get; set; }
        public bool Converged { get; set; }
        public string StatusMessage { get; set; }

        // Block results (final state)
        public List<BlockResult> BlockResults { get; set; }

        // Time history (if saved)
        public List<TimeSnapshot> TimeHistory { get; set; }
        public bool HasTimeHistory { get; set; }

        // Contact results
        public List<ContactResult> ContactResults { get; set; }

        // Global statistics
        public float MaxDisplacement { get; set; }
        public float MeanDisplacement { get; set; }
        public int NumFailedBlocks { get; set; }
        public int NumSlidingContacts { get; set; }
        public int NumOpenedJoints { get; set; }

        // Safety factor (if computed)
        public float SafetyFactor { get; set; }
        public bool SafetyFactorComputed { get; set; }

        // Energy tracking
        public float KineticEnergy { get; set; }
        public float PotentialEnergy { get; set; }
        public float DissipatedEnergy { get; set; }

        // Performance metrics
        public float ComputationTimeSeconds { get; set; }
        public float AverageTimePerStep { get; set; }

        public SlopeStabilityResults()
        {
            SimulationDate = DateTime.Now;
            TotalSimulationTime = 0.0f;
            TotalSteps = 0;
            Converged = false;
            StatusMessage = "";
            BlockResults = new List<BlockResult>();
            TimeHistory = new List<TimeSnapshot>();
            HasTimeHistory = false;
            ContactResults = new List<ContactResult>();
            MaxDisplacement = 0.0f;
            MeanDisplacement = 0.0f;
            NumFailedBlocks = 0;
            NumSlidingContacts = 0;
            NumOpenedJoints = 0;
            SafetyFactor = 0.0f;
            SafetyFactorComputed = false;
            KineticEnergy = 0.0f;
            PotentialEnergy = 0.0f;
            DissipatedEnergy = 0.0f;
            ComputationTimeSeconds = 0.0f;
            AverageTimePerStep = 0.0f;
        }

        /// <summary>
        /// Calculates statistics from block results.
        /// </summary>
        public void ComputeStatistics()
        {
            if (BlockResults.Count == 0)
                return;

            MaxDisplacement = 0.0f;
            float totalDisplacement = 0.0f;
            NumFailedBlocks = 0;
            KineticEnergy = 0.0f;
            PotentialEnergy = 0.0f;

            foreach (var blockResult in BlockResults)
            {
                float displacement = blockResult.Displacement.Length();
                MaxDisplacement = Math.Max(MaxDisplacement, displacement);
                totalDisplacement += displacement;

                if (blockResult.HasFailed)
                    NumFailedBlocks++;

                // Calculate energies
                KineticEnergy += 0.5f * blockResult.Mass * blockResult.Velocity.LengthSquared();
                PotentialEnergy += blockResult.Mass * 9.81f * blockResult.FinalPosition.Z;
            }

            MeanDisplacement = totalDisplacement / BlockResults.Count;

            // Contact statistics
            NumSlidingContacts = 0;
            NumOpenedJoints = 0;

            foreach (var contact in ContactResults)
            {
                if (contact.HasSlipped)
                    NumSlidingContacts++;
                if (contact.HasOpened)
                    NumOpenedJoints++;
            }

            // Performance
            if (TotalSteps > 0)
                AverageTimePerStep = ComputationTimeSeconds / TotalSteps;
        }

        /// <summary>
        /// Gets displacement magnitude array for visualization.
        /// </summary>
        public float[] GetDisplacementMagnitudes()
        {
            float[] magnitudes = new float[BlockResults.Count];
            for (int i = 0; i < BlockResults.Count; i++)
            {
                magnitudes[i] = BlockResults[i].Displacement.Length();
            }
            return magnitudes;
        }

        /// <summary>
        /// Gets blocks that have moved beyond a threshold.
        /// </summary>
        public List<BlockResult> GetFailedBlocks(float displacementThreshold)
        {
            var failedBlocks = new List<BlockResult>();
            foreach (var block in BlockResults)
            {
                if (block.Displacement.Length() > displacementThreshold || block.HasFailed)
                {
                    failedBlocks.Add(block);
                }
            }
            return failedBlocks;
        }
    }

    /// <summary>
    /// Results for a single block.
    /// </summary>
    public class BlockResult
    {
        public int BlockId { get; set; }
        public Vector3 InitialPosition { get; set; }
        public Vector3 FinalPosition { get; set; }
        public Vector3 Displacement { get; set; }
        public Vector3 Velocity { get; set; }
        public Quaternion FinalOrientation { get; set; }
        public Vector3 AngularVelocity { get; set; }
        public float Mass { get; set; }
        public bool HasFailed { get; set; }
        public bool IsFixed { get; set; }
        public int NumContacts { get; set; }

        public BlockResult()
        {
            InitialPosition = Vector3.Zero;
            FinalPosition = Vector3.Zero;
            Displacement = Vector3.Zero;
            Velocity = Vector3.Zero;
            FinalOrientation = Quaternion.Identity;
            AngularVelocity = Vector3.Zero;
            Mass = 0.0f;
            HasFailed = false;
            IsFixed = false;
            NumContacts = 0;
        }
    }

    /// <summary>
    /// Snapshot of simulation state at a specific time.
    /// </summary>
    public class TimeSnapshot
    {
        public float Time { get; set; }
        public List<Vector3> BlockPositions { get; set; }
        public List<Quaternion> BlockOrientations { get; set; }
        public List<Vector3> BlockVelocities { get; set; }
        public float KineticEnergy { get; set; }
        public float PotentialEnergy { get; set; }

        public TimeSnapshot()
        {
            Time = 0.0f;
            BlockPositions = new List<Vector3>();
            BlockOrientations = new List<Quaternion>();
            BlockVelocities = new List<Vector3>();
            KineticEnergy = 0.0f;
            PotentialEnergy = 0.0f;
        }
    }

    /// <summary>
    /// Results for a contact interface.
    /// </summary>
    public class ContactResult
    {
        public int BlockAId { get; set; }
        public int BlockBId { get; set; }
        public Vector3 ContactPoint { get; set; }
        public Vector3 ContactNormal { get; set; }
        public float MaxNormalForce { get; set; }
        public float MaxShearForce { get; set; }
        public bool HasSlipped { get; set; }
        public bool HasOpened { get; set; }
        public float TotalSlipDisplacement { get; set; }
        public bool IsJointContact { get; set; }
        public int JointSetId { get; set; }

        public ContactResult()
        {
            BlockAId = -1;
            BlockBId = -1;
            ContactPoint = Vector3.Zero;
            ContactNormal = Vector3.UnitZ;
            MaxNormalForce = 0.0f;
            MaxShearForce = 0.0f;
            HasSlipped = false;
            HasOpened = false;
            TotalSlipDisplacement = 0.0f;
            IsJointContact = false;
            JointSetId = -1;
        }
    }
}
