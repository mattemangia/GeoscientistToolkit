using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// Calculates factor of safety using strength reduction method.
    /// This is a critical component for professional slope stability analysis.
    /// Based on 3DEC and RocFall methodologies.
    /// </summary>
    public class SafetyFactorCalculator
    {
        private readonly SlopeStabilityDataset _dataset;
        private readonly SlopeStabilityParameters _originalParameters;

        public SafetyFactorCalculator(SlopeStabilityDataset dataset)
        {
            _dataset = dataset;
            _originalParameters = dataset.Parameters;
        }

        /// <summary>
        /// Calculates factor of safety using strength reduction method (SRM).
        /// Progressively reduces strength parameters until failure occurs.
        /// FOS = 1.0 means marginally stable, FOS < 1.0 means failure.
        /// </summary>
        public SafetyFactorResult CalculateFactorOfSafety(
            float tolerance = 0.01f,
            float minFOS = 0.5f,
            float maxFOS = 5.0f,
            int maxIterations = 20,
            Action<float, string> progressCallback = null)
        {
            progressCallback?.Invoke(0.0f, "Starting strength reduction method...");

            float fosLower = minFOS;
            float fosUpper = maxFOS;
            float fosCurrent = 1.0f;

            bool lowerIsStable = false;
            bool upperIsStable = true;

            var result = new SafetyFactorResult
            {
                Method = "Strength Reduction Method",
                StartTime = DateTime.Now
            };

            // Binary search for critical FOS
            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                fosCurrent = (fosLower + fosUpper) / 2.0f;

                progressCallback?.Invoke(
                    iteration / (float)maxIterations,
                    $"Testing FOS = {fosCurrent:F3} (iteration {iteration + 1}/{maxIterations})");

                // Run simulation with reduced strength
                bool isStable = TestStabilityWithReducedStrength(fosCurrent, out var testResults);

                result.TestedFactors.Add(fosCurrent);
                result.StabilityResults.Add(isStable);

                if (isStable)
                {
                    // System is still stable, can reduce strength further
                    fosLower = fosCurrent;
                    lowerIsStable = true;
                }
                else
                {
                    // System failed, need higher strength
                    fosUpper = fosCurrent;
                    upperIsStable = false;

                    // Store failure information
                    result.FailureDisplacement = testResults.MaxDisplacement;
                    result.FailedBlockCount = testResults.NumFailedBlocks;
                }

                // Check convergence
                if (Math.Abs(fosUpper - fosLower) < tolerance)
                {
                    result.Converged = true;
                    break;
                }

                result.Iterations = iteration + 1;
            }

            // Final FOS is the upper bound (conservative)
            result.FactorOfSafety = fosUpper;
            result.ConvergedFOS = fosCurrent;
            result.IsStable = upperIsStable || (fosUpper > 1.0f);
            result.EndTime = DateTime.Now;

            progressCallback?.Invoke(1.0f, $"Completed. FOS = {result.FactorOfSafety:F3}");

            return result;
        }

        /// <summary>
        /// Tests stability with reduced strength parameters.
        /// </summary>
        private bool TestStabilityWithReducedStrength(float reductionFactor, out SlopeStabilityResults results)
        {
            // Create a copy of parameters with reduced strength
            var testParams = CloneParameters(_originalParameters);
            testParams.TotalTime = 5.0f;  // Shorter simulation for testing
            testParams.Mode = SimulationMode.QuasiStatic;  // Use quasi-static for SRM
            testParams.LocalDamping = 0.8f;  // High damping for stability

            // Reduce strength of all materials
            var originalMaterials = new List<SlopeStabilityMaterial>();
            foreach (var material in _dataset.Materials)
            {
                // Store original
                originalMaterials.Add(CloneMaterial(material));

                // Reduce strength parameters
                material.Cohesion /= reductionFactor;
                material.FrictionAngle = MathF.Atan(MathF.Tan(material.FrictionAngle * MathF.PI / 180.0f) / reductionFactor) * 180.0f / MathF.PI;
                material.TensileStrength /= reductionFactor;

                // Update constitutive model
                material.ConstitutiveModel.Cohesion = material.Cohesion;
                material.ConstitutiveModel.FrictionAngle = material.FrictionAngle;
                material.ConstitutiveModel.TensileStrength = material.TensileStrength;
            }

            // Reduce joint strength
            var originalJoints = new List<JointSet>();
            foreach (var joint in _dataset.JointSets)
            {
                originalJoints.Add(CloneJointSet(joint));

                joint.Cohesion /= reductionFactor;
                joint.FrictionAngle = MathF.Atan(MathF.Tan(joint.FrictionAngle * MathF.PI / 180.0f) / reductionFactor) * 180.0f / MathF.PI;
                joint.TensileStrength /= reductionFactor;
            }

            // Run simulation
            var simulator = new SlopeStabilitySimulator(_dataset, testParams);
            results = simulator.RunSimulation();

            // Restore original strength parameters
            for (int i = 0; i < _dataset.Materials.Count; i++)
            {
                _dataset.Materials[i] = originalMaterials[i];
            }

            for (int i = 0; i < _dataset.JointSets.Count; i++)
            {
                _dataset.JointSets[i] = originalJoints[i];
            }

            // Check if system is stable
            // Criteria: max displacement < threshold and no excessive velocity
            float displacementThreshold = 0.1f;  // 10 cm
            float velocityThreshold = 0.01f;     // 1 cm/s

            bool isStable = results.MaxDisplacement < displacementThreshold &&
                           results.BlockResults.All(b => b.Velocity.Length() < velocityThreshold);

            return isStable;
        }

        /// <summary>
        /// Calculates local factor of safety for each block based on stress state.
        /// </summary>
        public Dictionary<int, float> CalculateLocalFactorOfSafety(SlopeStabilityResults results)
        {
            var localFOS = new Dictionary<int, float>();

            foreach (var blockResult in results.BlockResults)
            {
                var block = _dataset.GetBlock(blockResult.BlockId);
                if (block == null) continue;

                var material = _dataset.GetMaterial(block.MaterialId);
                if (material == null) continue;

                // Calculate local FOS based on force equilibrium
                // FOS = Resisting forces / Driving forces

                float drivingForce = block.Mass * _dataset.Parameters.Gravity.Length();
                float resistingForce = 0.0f;

                // Sum resisting forces from contacts
                foreach (var contactId in block.ContactingBlockIds)
                {
                    var contact = results.ContactResults.FirstOrDefault(c =>
                        (c.BlockAId == block.Id || c.BlockBId == block.Id));

                    if (contact != null)
                    {
                        resistingForce += contact.MaxShearForce;
                    }
                }

                float fos = resistingForce > 0 ? resistingForce / drivingForce : 0.0f;
                localFOS[block.Id] = Math.Min(fos, 10.0f);  // Cap at 10 for display
            }

            return localFOS;
        }

        private SlopeStabilityParameters CloneParameters(SlopeStabilityParameters original)
        {
            return new SlopeStabilityParameters
            {
                TimeStep = original.TimeStep,
                TotalTime = original.TotalTime,
                Gravity = original.Gravity,
                SlopeAngle = original.SlopeAngle,
                UseCustomGravityDirection = original.UseCustomGravityDirection,
                LocalDamping = original.LocalDamping,
                Mode = original.Mode,
                UseMultithreading = original.UseMultithreading,
                UseSIMD = original.UseSIMD
            };
        }

        private SlopeStabilityMaterial CloneMaterial(SlopeStabilityMaterial original)
        {
            var clone = new SlopeStabilityMaterial
            {
                Id = original.Id,
                Name = original.Name,
                Density = original.Density,
                YoungModulus = original.YoungModulus,
                PoissonRatio = original.PoissonRatio,
                Cohesion = original.Cohesion,
                FrictionAngle = original.FrictionAngle,
                TensileStrength = original.TensileStrength,
                DilationAngle = original.DilationAngle
            };

            clone.ConstitutiveModel = new ConstitutiveModel
            {
                YoungModulus = original.ConstitutiveModel.YoungModulus,
                PoissonRatio = original.ConstitutiveModel.PoissonRatio,
                Cohesion = original.ConstitutiveModel.Cohesion,
                FrictionAngle = original.ConstitutiveModel.FrictionAngle,
                TensileStrength = original.ConstitutiveModel.TensileStrength
            };

            return clone;
        }

        private JointSet CloneJointSet(JointSet original)
        {
            return new JointSet
            {
                Id = original.Id,
                Name = original.Name,
                Dip = original.Dip,
                DipDirection = original.DipDirection,
                Spacing = original.Spacing,
                Cohesion = original.Cohesion,
                FrictionAngle = original.FrictionAngle,
                TensileStrength = original.TensileStrength,
                NormalStiffness = original.NormalStiffness,
                ShearStiffness = original.ShearStiffness
            };
        }
    }

    /// <summary>
    /// Result of factor of safety calculation.
    /// </summary>
    public class SafetyFactorResult
    {
        public string Method { get; set; }
        public float FactorOfSafety { get; set; }
        public float ConvergedFOS { get; set; }
        public bool IsStable { get; set; }
        public bool Converged { get; set; }
        public int Iterations { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }

        public List<float> TestedFactors { get; set; } = new List<float>();
        public List<bool> StabilityResults { get; set; } = new List<bool>();

        public float FailureDisplacement { get; set; }
        public int FailedBlockCount { get; set; }

        public TimeSpan ComputationTime => EndTime - StartTime;

        public string GetInterpretation()
        {
            if (FactorOfSafety < 1.0f)
                return "UNSTABLE - Failure imminent";
            else if (FactorOfSafety < 1.2f)
                return "MARGINALLY STABLE - Monitor closely";
            else if (FactorOfSafety < 1.5f)
                return "STABLE - Acceptable for temporary slopes";
            else if (FactorOfSafety < 2.0f)
                return "STABLE - Acceptable for permanent slopes";
            else
                return "VERY STABLE - High safety margin";
        }
    }
}
