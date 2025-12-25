using System;
using System.Numerics;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// Constitutive model type for block material behavior.
    /// </summary>
    public enum ConstitutiveModelType
    {
        Elastic,        // Linear elastic
        Plastic,        // Elasto-plastic with hardening/softening
        Brittle,        // Brittle failure with sudden strength loss
        ElastoPlastic,  // Combined elastic-plastic
        ViscoElastic    // Time-dependent behavior
    }

    /// <summary>
    /// Failure criterion for stress state evaluation.
    /// </summary>
    public enum FailureCriterionType
    {
        MohrCoulomb,    // Classical Mohr-Coulomb (c + σn*tan(φ))
        HoekBrown,      // Hoek-Brown for rock masses
        DruckerPrager,  // Smooth yield surface
        Griffith,       // Tensile failure criterion
        VonMises,       // Ductile materials
        Tresca          // Maximum shear stress
    }

    /// <summary>
    /// Damage evolution model for brittle behavior.
    /// </summary>
    public enum DamageEvolutionModel
    {
        None,           // No damage
        Linear,         // Linear degradation
        Exponential,    // Exponential decay
        Mazars,         // Mazars damage model
        Lemaitre        // Lemaitre damage model
    }

    /// <summary>
    /// Constitutive model parameters and behavior.
    /// </summary>
    public class ConstitutiveModel
    {
        // Model type
        public ConstitutiveModelType ModelType { get; set; }
        public FailureCriterionType FailureCriterion { get; set; }

        // Elastic properties
        public float YoungModulus { get; set; }     // Pa
        public float PoissonRatio { get; set; }
        public float ShearModulus { get; set; }     // Pa (G = E / (2(1+ν)))
        public float BulkModulus { get; set; }      // Pa (K = E / (3(1-2ν)))

        // Strength parameters (Mohr-Coulomb)
        public float Cohesion { get; set; }         // Pa
        public float FrictionAngle { get; set; }    // degrees
        public float DilationAngle { get; set; }    // degrees
        public float TensileStrength { get; set; }  // Pa

        // Hoek-Brown parameters
        public float HB_mi { get; set; }            // Intact rock parameter
        public float HB_mb { get; set; }            // Reduced rock mass parameter
        public float HB_s { get; set; }             // Rock mass parameter
        public float HB_a { get; set; }             // Rock mass parameter
        public float HB_GSI { get; set; }           // Geological Strength Index (0-100)

        // Plastic behavior
        public bool EnablePlasticity { get; set; }
        public float HardeningModulus { get; set; } // Pa (for strain hardening)
        public float SofteningModulus { get; set; } // Pa (for strain softening)
        public float PlasticStrainAtPeak { get; set; }
        public float ResidualCohesion { get; set; } // Pa (post-peak)
        public float ResidualFriction { get; set; } // degrees (post-peak)

        // Brittle behavior
        public bool EnableBrittleFailure { get; set; }
        public DamageEvolutionModel DamageModel { get; set; }
        public float DamageThreshold { get; set; }          // Strain/stress threshold
        public float DamageCriticalValue { get; set; }      // Complete failure
        public float DamageEvolutionRate { get; set; }
        public bool ApplyDamageToStiffness { get; set; }
        public bool ApplyDamageToStrength { get; set; }

        // Visco-elastic (time-dependent)
        public bool EnableViscoelasticity { get; set; }
        public float RelaxationTime { get; set; }   // seconds
        public float CreepCoefficient { get; set; }

        // State variables (updated during simulation)
        public float CurrentDamage { get; set; }            // 0-1
        public float PlasticStrain { get; set; }
        public bool HasFailed { get; set; }

        public ConstitutiveModel()
        {
            ModelType = ConstitutiveModelType.Elastic;
            FailureCriterion = FailureCriterionType.MohrCoulomb;

            // Default elastic properties (granite-like)
            YoungModulus = 50e9f;
            PoissonRatio = 0.25f;
            UpdateDerivedProperties();

            // Default strength (Mohr-Coulomb)
            Cohesion = 10e6f;
            FrictionAngle = 35.0f;
            DilationAngle = 5.0f;
            TensileStrength = 5e6f;

            // Hoek-Brown defaults
            HB_mi = 10.0f;
            HB_mb = 1.5f;
            HB_s = 0.004f;
            HB_a = 0.5f;
            HB_GSI = 50.0f;

            // Plasticity
            EnablePlasticity = false;
            HardeningModulus = 0.0f;
            SofteningModulus = -1e9f;
            PlasticStrainAtPeak = 0.001f;
            ResidualCohesion = 0.0f;
            ResidualFriction = 25.0f;

            // Brittle
            EnableBrittleFailure = false;
            DamageModel = DamageEvolutionModel.Exponential;
            DamageThreshold = 0.001f;
            DamageCriticalValue = 0.01f;
            DamageEvolutionRate = 100.0f;
            ApplyDamageToStiffness = true;
            ApplyDamageToStrength = true;

            // Visco
            EnableViscoelasticity = false;
            RelaxationTime = 3600.0f;
            CreepCoefficient = 1e-6f;

            // State
            CurrentDamage = 0.0f;
            PlasticStrain = 0.0f;
            HasFailed = false;
        }

        /// <summary>
        /// Updates derived elastic properties (G, K) from E and ν.
        /// </summary>
        public void UpdateDerivedProperties()
        {
            ShearModulus = YoungModulus / (2.0f * (1.0f + PoissonRatio));
            BulkModulus = YoungModulus / (3.0f * (1.0f - 2.0f * PoissonRatio));
        }

        /// <summary>
        /// Evaluates failure criterion given stress state.
        /// Returns failure index (0 = safe, 1 = at yield, >1 = failure).
        /// </summary>
        public float EvaluateFailure(float sigma1, float sigma2, float sigma3)
        {
            switch (FailureCriterion)
            {
                case FailureCriterionType.MohrCoulomb:
                    return EvaluateMohrCoulomb(sigma1, sigma2, sigma3);

                case FailureCriterionType.HoekBrown:
                    return EvaluateHoekBrown(sigma1, sigma3);

                case FailureCriterionType.DruckerPrager:
                    return EvaluateDruckerPrager(sigma1, sigma2, sigma3);

                case FailureCriterionType.VonMises:
                    return EvaluateVonMises(sigma1, sigma2, sigma3);

                default:
                    return 0.0f;
            }
        }

        /// <summary>
        /// Mohr-Coulomb criterion: τ = c + σn*tan(φ)
        /// </summary>
        private float EvaluateMohrCoulomb(float s1, float s2, float s3)
        {
            float c = Cohesion * (1.0f - CurrentDamage * (ApplyDamageToStrength ? 1.0f : 0.0f));
            float phi = FrictionAngle * MathF.PI / 180.0f;
            float st = TensileStrength * (1.0f - CurrentDamage * (ApplyDamageToStrength ? 1.0f : 0.0f));

            // Maximum and minimum principal stresses
            float sigmaMax = Math.Max(s1, Math.Max(s2, s3));
            float sigmaMin = Math.Min(s1, Math.Min(s2, s3));

            // Check tensile failure
            if (sigmaMax > st)
                return sigmaMax / st;

            // Mohr-Coulomb shear failure
            float Nphi = (1.0f + MathF.Sin(phi)) / (1.0f - MathF.Sin(phi));
            float shearStrength = 2.0f * c * MathF.Sqrt(Nphi);

            float tau = (sigmaMax - sigmaMin) / 2.0f;
            float sigmaN = (sigmaMax + sigmaMin) / 2.0f;

            float f = tau - (c + sigmaN * MathF.Tan(phi));
            return f / shearStrength;
        }

        /// <summary>
        /// Hoek-Brown criterion: σ1 = σ3 + σci*(mb*σ3/σci + s)^a
        /// </summary>
        private float EvaluateHoekBrown(float s1, float s3)
        {
            float sci = TensileStrength * 10.0f;  // Approximate UCS from tensile
            float mb = HB_mb;
            float s = HB_s;
            float a = HB_a;

            // Apply damage
            if (ApplyDamageToStrength && CurrentDamage > 0.0f)
            {
                mb *= (1.0f - CurrentDamage);
                s *= (1.0f - CurrentDamage);
            }

            float ratio = mb * s3 / sci + s;
            if (ratio < 0) ratio = 0;

            float s1_predicted = s3 + sci * MathF.Pow(ratio, a);
            return s1 / s1_predicted;
        }

        /// <summary>
        /// Drucker-Prager criterion (smooth approximation of Mohr-Coulomb).
        /// </summary>
        private float EvaluateDruckerPrager(float s1, float s2, float s3)
        {
            float phi = FrictionAngle * MathF.PI / 180.0f;
            float c = Cohesion * (1.0f - CurrentDamage * (ApplyDamageToStrength ? 1.0f : 0.0f));

            // Drucker-Prager parameters
            float alpha = 2.0f * MathF.Sin(phi) / (MathF.Sqrt(3.0f) * (3.0f - MathF.Sin(phi)));
            float k = 6.0f * c * MathF.Cos(phi) / (MathF.Sqrt(3.0f) * (3.0f - MathF.Sin(phi)));

            // Invariants
            float I1 = s1 + s2 + s3;
            float J2 = ((s1 - s2) * (s1 - s2) + (s2 - s3) * (s2 - s3) + (s3 - s1) * (s3 - s1)) / 6.0f;

            float f = MathF.Sqrt(J2) + alpha * I1 / 3.0f - k;
            return f / k;
        }

        /// <summary>
        /// Von Mises criterion (for ductile materials).
        /// </summary>
        private float EvaluateVonMises(float s1, float s2, float s3)
        {
            float yieldStress = Cohesion * 2.0f;  // Approximate
            float vonMises = MathF.Sqrt(0.5f * ((s1 - s2) * (s1 - s2) +
                                                 (s2 - s3) * (s2 - s3) +
                                                 (s3 - s1) * (s3 - s1)));
            return vonMises / yieldStress;
        }

        /// <summary>
        /// Updates damage based on strain/stress state.
        /// </summary>
        public void UpdateDamage(float equivalentStrain, float deltaTime)
        {
            if (!EnableBrittleFailure || HasFailed)
                return;

            if (equivalentStrain < DamageThreshold)
                return;

            float damageIncrement = 0.0f;

            switch (DamageModel)
            {
                case DamageEvolutionModel.Linear:
                    damageIncrement = (equivalentStrain - DamageThreshold) /
                                     (DamageCriticalValue - DamageThreshold);
                    break;

                case DamageEvolutionModel.Exponential:
                    float ratio = (equivalentStrain - DamageThreshold) / DamageThreshold;
                    damageIncrement = 1.0f - MathF.Exp(-DamageEvolutionRate * ratio);
                    break;

                case DamageEvolutionModel.Mazars:
                    // Simplified Mazars model
                    float beta = 1.0f;
                    damageIncrement = 1.0f - DamageThreshold / equivalentStrain *
                                             MathF.Exp(-beta * (equivalentStrain - DamageThreshold));
                    break;
            }

            CurrentDamage = Math.Clamp(damageIncrement, 0.0f, 1.0f);

            if (CurrentDamage >= 0.99f)
                HasFailed = true;
        }

        /// <summary>
        /// Gets effective Young's modulus accounting for damage.
        /// </summary>
        public float GetEffectiveYoungModulus()
        {
            if (ApplyDamageToStiffness && CurrentDamage > 0.0f)
                return YoungModulus * (1.0f - CurrentDamage);
            return YoungModulus;
        }

        /// <summary>
        /// Gets effective cohesion accounting for damage and plasticity.
        /// </summary>
        public float GetEffectiveCohesion()
        {
            float c = Cohesion;

            // Apply damage
            if (ApplyDamageToStrength && CurrentDamage > 0.0f)
                c *= (1.0f - CurrentDamage);

            // Apply softening
            if (EnablePlasticity && PlasticStrain > PlasticStrainAtPeak)
            {
                float softeningRatio = Math.Min(PlasticStrain / (PlasticStrainAtPeak * 5.0f), 1.0f);
                c = c * (1.0f - softeningRatio) + ResidualCohesion * softeningRatio;
            }

            return c;
        }
    }
}
