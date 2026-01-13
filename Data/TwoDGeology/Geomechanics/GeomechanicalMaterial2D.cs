// GeoscientistToolkit/Data/TwoDGeology/Geomechanics/GeomechanicalMaterial2D.cs

using System;
using System.Collections.Generic;
using System.Numerics;
using GeoscientistToolkit.Data.Materials;

namespace GeoscientistToolkit.Data.TwoDGeology.Geomechanics;

/// <summary>
/// Failure criterion types for 2D geomechanical analysis
/// </summary>
public enum FailureCriterion2D
{
    LinearMohrCoulomb,      // τ = c + σn·tan(φ)
    CurvedMohrCoulomb,      // τ = A(σn + T)^B (power law)
    HoekBrown,              // σ1 = σ3 + σci(mb·σ3/σci + s)^a
    DruckerPrager,          // Smooth cone approximation
    Griffith,               // Parabolic tensile
    ModifiedCamClay,        // Critical state soil mechanics
    CapModel,               // Compaction cap for soils
    JointedRock             // Ubiquitous joint model
}

/// <summary>
/// Plasticity flow rule types
/// </summary>
public enum FlowRule
{
    Associated,             // Dilation = friction angle
    NonAssociated,          // Dilation < friction angle
    CustomDilation          // User-defined dilation
}

/// <summary>
/// Hardening/softening law types
/// </summary>
public enum HardeningSofteningLaw
{
    None,
    LinearHardening,
    LinearSoftening,
    ExponentialSoftening,
    PiecewiseLinear,
    CohesionSoftening,      // c decreases with plastic strain
    FrictionSoftening       // φ decreases with plastic strain
}

/// <summary>
/// Comprehensive material model for 2D geomechanical simulations.
/// Implements curved Mohr-Coulomb and multiple failure criteria
/// similar to FLAC2D and OPTUM G2.
/// </summary>
public class GeomechanicalMaterial2D
{
    #region Identification

    public int Id { get; set; }
    public string Name { get; set; }
    public Vector4 Color { get; set; } = new(0.5f, 0.5f, 0.5f, 1.0f);

    #endregion

    #region Elastic Properties

    /// <summary>Young's modulus (Pa)</summary>
    public double YoungModulus { get; set; } = 50e9;

    /// <summary>Poisson's ratio</summary>
    public double PoissonRatio { get; set; } = 0.25;

    /// <summary>Shear modulus G = E/(2(1+ν)) (Pa)</summary>
    public double ShearModulus => YoungModulus / (2.0 * (1.0 + PoissonRatio));

    /// <summary>Bulk modulus K = E/(3(1-2ν)) (Pa)</summary>
    public double BulkModulus => YoungModulus / (3.0 * (1.0 - 2.0 * PoissonRatio));

    /// <summary>Lamé's first parameter λ</summary>
    public double LameFirst => YoungModulus * PoissonRatio / ((1.0 + PoissonRatio) * (1.0 - 2.0 * PoissonRatio));

    /// <summary>Lamé's second parameter μ = G</summary>
    public double LameSecond => ShearModulus;

    /// <summary>Plane strain bulk modulus K' = K + G/3</summary>
    public double PlaneStrainBulkModulus => BulkModulus + ShearModulus / 3.0;

    #endregion

    #region Density and Weight

    /// <summary>
    /// Global gravitational acceleration constant (m/s²). Default is 9.81 for Earth.
    /// Can be changed for simulations on other celestial bodies (e.g., 1.62 for Moon, 3.72 for Mars).
    /// </summary>
    public static double GravityConstant { get; set; } = 9.81;

    /// <summary>Mass density (kg/m³)</summary>
    public double Density { get; set; } = 2700.0;

    /// <summary>Unit weight γ = ρg (N/m³)</summary>
    public double UnitWeight => Density * GravityConstant;

    /// <summary>Saturated density (kg/m³)</summary>
    public double SaturatedDensity { get; set; } = 2800.0;

    /// <summary>Porosity (0-1)</summary>
    public double Porosity { get; set; } = 0.1;

    #endregion

    #region Strength Parameters - Linear Mohr-Coulomb

    /// <summary>Cohesion c (Pa)</summary>
    public double Cohesion { get; set; } = 10e6;

    /// <summary>Peak friction angle φ (degrees)</summary>
    public double FrictionAngle { get; set; } = 35.0;

    /// <summary>Friction angle in radians</summary>
    public double FrictionAngleRad => FrictionAngle * Math.PI / 180.0;

    /// <summary>Dilation angle ψ (degrees)</summary>
    public double DilationAngle { get; set; } = 5.0;

    /// <summary>Dilation angle in radians</summary>
    public double DilationAngleRad => DilationAngle * Math.PI / 180.0;

    /// <summary>Tensile strength T (Pa)</summary>
    public double TensileStrength { get; set; } = 5e6;

    /// <summary>Uniaxial compressive strength UCS (Pa)</summary>
    public double UCS => 2.0 * Cohesion * Math.Cos(FrictionAngleRad) / (1.0 - Math.Sin(FrictionAngleRad));

    #endregion

    #region Curved Mohr-Coulomb Parameters

    /// <summary>
    /// Use curved (power-law) Mohr-Coulomb envelope instead of linear.
    /// τ = A(σn + T)^B where T is tensile strength
    /// </summary>
    public bool UseCurvedMohrCoulomb { get; set; }

    /// <summary>
    /// Curved MC coefficient A - controls envelope magnitude.
    /// For consistency with linear MC at high stress: A ≈ tan(φ)
    /// </summary>
    public double CurvedMC_A { get; set; } = 0.7;

    /// <summary>
    /// Curved MC exponent B - controls curvature (0.5-1.0 typical).
    /// B=1 gives linear, B=0.5 gives parabolic (Griffith-like)
    /// </summary>
    public double CurvedMC_B { get; set; } = 0.7;

    /// <summary>
    /// Optional curvature transition stress σt - above this stress,
    /// envelope transitions to linear with friction angle φ
    /// </summary>
    public double CurvedMC_TransitionStress { get; set; } = -1; // -1 = no transition

    /// <summary>
    /// Compute curved Mohr-Coulomb shear strength at given normal stress.
    /// Uses Barton-Bandis style envelope for joint shear strength.
    /// </summary>
    public double GetCurvedMCShearStrength(double sigmaN)
    {
        if (!UseCurvedMohrCoulomb)
        {
            // Linear Mohr-Coulomb
            return Cohesion + sigmaN * Math.Tan(FrictionAngleRad);
        }

        // Curved envelope: τ = A(σn + T)^B
        double effectiveStress = sigmaN + TensileStrength;
        if (effectiveStress <= 0) return 0;

        double tau = CurvedMC_A * Math.Pow(effectiveStress, CurvedMC_B);

        // Optional transition to linear at high stress
        if (CurvedMC_TransitionStress > 0 && sigmaN > CurvedMC_TransitionStress)
        {
            double tauTransition = CurvedMC_A * Math.Pow(CurvedMC_TransitionStress + TensileStrength, CurvedMC_B);
            double linearSlope = Math.Tan(FrictionAngleRad);
            tau = tauTransition + (sigmaN - CurvedMC_TransitionStress) * linearSlope;
        }

        return tau;
    }

    /// <summary>
    /// Compute instantaneous friction angle at given normal stress (curved envelope).
    /// φ_instant = arctan(dτ/dσn) = arctan(A·B·(σn + T)^(B-1))
    /// </summary>
    public double GetInstantaneousFrictionAngle(double sigmaN)
    {
        if (!UseCurvedMohrCoulomb)
        {
            return FrictionAngle;
        }

        double effectiveStress = sigmaN + TensileStrength;
        if (effectiveStress <= 0) return 0;

        double slope = CurvedMC_A * CurvedMC_B * Math.Pow(effectiveStress, CurvedMC_B - 1);
        return Math.Atan(slope) * 180.0 / Math.PI;
    }

    #endregion

    #region Hoek-Brown Parameters

    /// <summary>Intact rock constant mi</summary>
    public double HB_mi { get; set; } = 10.0;

    /// <summary>Rock mass constant mb (reduced from mi)</summary>
    public double HB_mb { get; set; } = 1.5;

    /// <summary>Rock mass constant s</summary>
    public double HB_s { get; set; } = 0.004;

    /// <summary>Rock mass constant a</summary>
    public double HB_a { get; set; } = 0.5;

    /// <summary>Geological Strength Index (0-100)</summary>
    public double GSI { get; set; } = 50.0;

    /// <summary>Disturbance factor D (0-1)</summary>
    public double DisturbanceFactor { get; set; } = 0.0;

    /// <summary>
    /// Calculate Hoek-Brown parameters from GSI and mi
    /// </summary>
    public void CalculateHoekBrownFromGSI()
    {
        double D = DisturbanceFactor;
        HB_mb = HB_mi * Math.Exp((GSI - 100) / (28 - 14 * D));
        HB_s = Math.Exp((GSI - 100) / (9 - 3 * D));
        HB_a = 0.5 + (Math.Exp(-GSI / 15.0) - Math.Exp(-20.0 / 3.0)) / 6.0;
    }

    /// <summary>
    /// Evaluate Hoek-Brown criterion σ1 = σ3 + σci(mb·σ3/σci + s)^a
    /// Returns predicted σ1 at failure for given σ3
    /// </summary>
    public double GetHoekBrownSigma1(double sigma3)
    {
        double normalizedSigma3 = HB_mb * sigma3 / UCS + HB_s;
        if (normalizedSigma3 < 0) normalizedSigma3 = 0;
        return sigma3 + UCS * Math.Pow(normalizedSigma3, HB_a);
    }

    #endregion

    #region Residual Strength (Post-Peak Softening)

    /// <summary>Residual cohesion after failure (Pa)</summary>
    public double ResidualCohesion { get; set; } = 0.0;

    /// <summary>Residual friction angle after failure (degrees)</summary>
    public double ResidualFrictionAngle { get; set; } = 25.0;

    /// <summary>Plastic strain at which residual strength is reached</summary>
    public double PlasticStrainAtResidual { get; set; } = 0.01;

    /// <summary>
    /// Get effective cohesion considering strain softening
    /// </summary>
    public double GetEffectiveCohesion(double plasticStrain)
    {
        if (plasticStrain <= 0) return Cohesion;
        if (plasticStrain >= PlasticStrainAtResidual) return ResidualCohesion;

        double ratio = plasticStrain / PlasticStrainAtResidual;
        return Cohesion * (1 - ratio) + ResidualCohesion * ratio;
    }

    /// <summary>
    /// Get effective friction angle considering strain softening
    /// </summary>
    public double GetEffectiveFrictionAngle(double plasticStrain)
    {
        if (plasticStrain <= 0) return FrictionAngle;
        if (plasticStrain >= PlasticStrainAtResidual) return ResidualFrictionAngle;

        double ratio = plasticStrain / PlasticStrainAtResidual;
        return FrictionAngle * (1 - ratio) + ResidualFrictionAngle * ratio;
    }

    #endregion

    #region Plasticity Settings

    public FailureCriterion2D FailureCriterion { get; set; } = FailureCriterion2D.LinearMohrCoulomb;
    public FlowRule FlowRule { get; set; } = FlowRule.NonAssociated;
    public HardeningSofteningLaw HardeningLaw { get; set; } = HardeningSofteningLaw.None;

    /// <summary>Hardening modulus H (Pa) - slope of stress-plastic strain curve</summary>
    public double HardeningModulus { get; set; } = 0.0;

    /// <summary>Enable strain softening after peak strength</summary>
    public bool EnableSoftening { get; set; }

    #endregion

    #region Damage Model

    /// <summary>Enable damage evolution</summary>
    public bool EnableDamage { get; set; }

    /// <summary>Damage variable (0 = intact, 1 = fully damaged)</summary>
    public double DamageVariable { get; set; }

    /// <summary>Strain threshold for damage initiation</summary>
    public double DamageThreshold { get; set; } = 0.001;

    /// <summary>Critical strain for complete damage</summary>
    public double DamageCriticalStrain { get; set; } = 0.01;

    /// <summary>
    /// Get degraded Young's modulus due to damage: E_eff = E(1-D)
    /// </summary>
    public double GetDamagedYoungModulus()
    {
        return YoungModulus * (1 - DamageVariable);
    }

    #endregion

    #region Joint/Discontinuity Properties

    /// <summary>Enable ubiquitous joint behavior</summary>
    public bool HasUbiquitousJoints { get; set; }

    /// <summary>Joint dip angle (degrees from horizontal)</summary>
    public double JointDipAngle { get; set; } = 45.0;

    /// <summary>Joint cohesion (Pa)</summary>
    public double JointCohesion { get; set; } = 0.0;

    /// <summary>Joint friction angle (degrees)</summary>
    public double JointFrictionAngle { get; set; } = 20.0;

    /// <summary>Joint tensile strength (Pa)</summary>
    public double JointTensileStrength { get; set; } = 0.0;

    /// <summary>Joint normal stiffness (Pa/m)</summary>
    public double JointNormalStiffness { get; set; } = 1e10;

    /// <summary>Joint shear stiffness (Pa/m)</summary>
    public double JointShearStiffness { get; set; } = 1e9;

    /// <summary>Joint dilation angle (degrees)</summary>
    public double JointDilationAngle { get; set; } = 0.0;

    #endregion

    #region Pore Pressure Effects

    /// <summary>Enable effective stress calculation with pore pressure</summary>
    public bool UsePorePressure { get; set; }

    /// <summary>Biot coefficient α (0-1)</summary>
    public double BiotCoefficient { get; set; } = 0.8;

    /// <summary>
    /// Calculate effective stress: σ'_ij = σ_ij - α·p·δ_ij
    /// </summary>
    public double GetEffectiveStress(double totalStress, double porePressure)
    {
        return totalStress - BiotCoefficient * porePressure;
    }

    #endregion

    #region Permeability (for coupled analysis)

    /// <summary>Intrinsic permeability k (m²)</summary>
    public double Permeability { get; set; } = 1e-15;

    /// <summary>Hydraulic conductivity K (m/s) = k·ρ_w·g/μ_w (using configurable gravity)</summary>
    public double HydraulicConductivity => Permeability * WaterDensity * GravityConstant / WaterViscosity;

    /// <summary>Water density for hydraulic conductivity calculation (kg/m³). Default 1000.</summary>
    public static double WaterDensity { get; set; } = 1000.0;

    /// <summary>Water dynamic viscosity for hydraulic conductivity calculation (Pa·s). Default 0.001.</summary>
    public static double WaterViscosity { get; set; } = 0.001;

    /// <summary>Permeability can change with volumetric strain</summary>
    public bool StrainDependentPermeability { get; set; }

    /// <summary>Permeability multiplier per unit volumetric strain</summary>
    public double PermeabilityStrainSensitivity { get; set; } = 10.0;

    #endregion

    #region Thermal Properties (for coupled thermo-mechanical)

    /// <summary>Thermal conductivity (W/m·K)</summary>
    public double ThermalConductivity { get; set; } = 2.5;

    /// <summary>Specific heat capacity (J/kg·K)</summary>
    public double SpecificHeat { get; set; } = 800;

    /// <summary>Thermal expansion coefficient (1/K)</summary>
    public double ThermalExpansion { get; set; } = 1e-5;

    #endregion

    #region Failure Evaluation

    /// <summary>
    /// Evaluate failure criterion for given stress state.
    /// Returns yield function value f: f < 0 elastic, f = 0 yield, f > 0 violation
    /// </summary>
    public double EvaluateYieldFunction(double sigmaX, double sigmaY, double tauXY, double plasticStrain = 0)
    {
        // Calculate principal stresses
        double p = (sigmaX + sigmaY) / 2;
        double R = Math.Sqrt(Math.Pow((sigmaX - sigmaY) / 2, 2) + tauXY * tauXY);
        double sigma1 = p + R; // Maximum (most tensile/least compressive)
        double sigma3 = p - R; // Minimum (most compressive)

        return EvaluateYieldFunction(sigma1, sigma3, plasticStrain);
    }

    /// <summary>
    /// Evaluate failure criterion for principal stresses.
    /// Returns yield function value f: f < 0 elastic, f = 0 yield, f > 0 violation
    /// </summary>
    public double EvaluateYieldFunction(double sigma1, double sigma3, double plasticStrain = 0)
    {
        switch (FailureCriterion)
        {
            case FailureCriterion2D.LinearMohrCoulomb:
                return EvaluateMohrCoulomb(sigma1, sigma3, plasticStrain);

            case FailureCriterion2D.CurvedMohrCoulomb:
                return EvaluateCurvedMohrCoulomb(sigma1, sigma3);

            case FailureCriterion2D.HoekBrown:
                return EvaluateHoekBrown(sigma1, sigma3);

            case FailureCriterion2D.DruckerPrager:
                return EvaluateDruckerPrager(sigma1, sigma3, plasticStrain);

            case FailureCriterion2D.Griffith:
                return EvaluateGriffith(sigma1, sigma3);

            case FailureCriterion2D.JointedRock:
                return EvaluateJointedRock(sigma1, sigma3, plasticStrain);

            default:
                return EvaluateMohrCoulomb(sigma1, sigma3, plasticStrain);
        }
    }

    private double EvaluateMohrCoulomb(double sigma1, double sigma3, double plasticStrain)
    {
        double c = GetEffectiveCohesion(plasticStrain);
        double phi = GetEffectiveFrictionAngle(plasticStrain) * Math.PI / 180.0;
        double T = TensileStrength;

        // Tension cutoff
        if (sigma1 > T)
        {
            return sigma1 - T;
        }

        // Shear failure: f = σ1 - σ3·Nφ - 2c√Nφ where Nφ = (1+sin(φ))/(1-sin(φ))
        double sinPhi = Math.Sin(phi);
        double Nphi = (1 + sinPhi) / (1 - sinPhi);
        return sigma1 - sigma3 * Nphi - 2 * c * Math.Sqrt(Nphi);
    }

    private double EvaluateCurvedMohrCoulomb(double sigma1, double sigma3)
    {
        // Tension cutoff
        if (sigma1 > TensileStrength)
        {
            return sigma1 - TensileStrength;
        }

        // Normal stress on potential failure plane
        double sigmaN = (sigma1 + sigma3) / 2;
        double tau = (sigma1 - sigma3) / 2;

        // Shear strength from curved envelope
        double tauStrength = GetCurvedMCShearStrength(sigmaN);

        return tau - tauStrength;
    }

    private double EvaluateHoekBrown(double sigma1, double sigma3)
    {
        // Tension cutoff: σ_t = -s·σci/mb for Hoek-Brown
        double tensionLimit = -HB_s * UCS / HB_mb;
        if (sigma3 < tensionLimit)
        {
            return sigma3 - tensionLimit;
        }

        // σ1 = σ3 + σci(mb·σ3/σci + s)^a
        double sigma1Predicted = GetHoekBrownSigma1(sigma3);
        return sigma1 - sigma1Predicted;
    }

    private double EvaluateDruckerPrager(double sigma1, double sigma3, double plasticStrain)
    {
        double c = GetEffectiveCohesion(plasticStrain);
        double phi = GetEffectiveFrictionAngle(plasticStrain) * Math.PI / 180.0;

        // Drucker-Prager parameters (inscribed to Mohr-Coulomb)
        double alpha = 2 * Math.Sin(phi) / (Math.Sqrt(3) * (3 - Math.Sin(phi)));
        double k = 6 * c * Math.Cos(phi) / (Math.Sqrt(3) * (3 - Math.Sin(phi)));

        // For plane strain: I1 = σ1 + σ3 (σ2 = ν(σ1+σ3) for plane strain)
        double I1 = sigma1 + sigma3;
        double J2 = Math.Pow(sigma1 - sigma3, 2) / 4;

        return Math.Sqrt(J2) + alpha * I1 / 3 - k;
    }

    private double EvaluateGriffith(double sigma1, double sigma3)
    {
        double T = TensileStrength;

        // Pure tension
        if (sigma1 > T)
        {
            return sigma1 - T;
        }

        // Griffith parabolic: (σ1-σ3)² = 8T(σ1+σ3+2T) for σ1+3σ3 < 0
        if (sigma1 + 3 * sigma3 < 0)
        {
            double left = Math.Pow(sigma1 - sigma3, 2);
            double right = 8 * T * (sigma1 + sigma3 + 2 * T);
            return left - right;
        }

        // Tension region
        return sigma1 - T;
    }

    private double EvaluateJointedRock(double sigma1, double sigma3, double plasticStrain)
    {
        // First check matrix failure
        double matrixYield = EvaluateMohrCoulomb(sigma1, sigma3, plasticStrain);

        if (!HasUbiquitousJoints) return matrixYield;

        // Check joint failure
        double beta = JointDipAngle * Math.PI / 180.0;
        double cj = JointCohesion;
        double phij = JointFrictionAngle * Math.PI / 180.0;

        // Transform stresses to joint plane
        double cos2b = Math.Cos(2 * beta);
        double sin2b = Math.Sin(2 * beta);
        double p = (sigma1 + sigma3) / 2;
        double q = (sigma1 - sigma3) / 2;

        double sigmaJ = p - q * cos2b;  // Normal stress on joint
        double tauJ = q * sin2b;        // Shear stress on joint

        // Joint shear strength
        double tauJStrength = cj + sigmaJ * Math.Tan(phij);

        double jointYield = Math.Abs(tauJ) - tauJStrength;

        // Return most critical
        return Math.Max(matrixYield, jointYield);
    }

    #endregion

    #region Stress Return (Plasticity)

    /// <summary>
    /// Perform plastic stress return for Mohr-Coulomb criterion.
    /// Returns corrected stress state that satisfies yield condition.
    /// </summary>
    public (double sigmaX, double sigmaY, double tauXY, double dPlasticStrain)
        PlasticReturn(double sigmaX, double sigmaY, double tauXY, double currentPlasticStrain)
    {
        // Calculate trial principal stresses
        double p = (sigmaX + sigmaY) / 2;
        double R = Math.Sqrt(Math.Pow((sigmaX - sigmaY) / 2, 2) + tauXY * tauXY);
        double sigma1 = p + R;
        double sigma3 = p - R;

        double f = EvaluateYieldFunction(sigma1, sigma3, currentPlasticStrain);

        if (f <= 0)
        {
            // Elastic - no correction needed
            return (sigmaX, sigmaY, tauXY, 0);
        }

        // Plastic return algorithm
        double c = GetEffectiveCohesion(currentPlasticStrain);
        double phi = GetEffectiveFrictionAngle(currentPlasticStrain) * Math.PI / 180.0;
        double psi = DilationAngleRad;

        double sinPhi = Math.Sin(phi);
        double cosPhi = Math.Cos(phi);
        double sinPsi = Math.Sin(psi);

        // Return to yield surface
        double Nphi = (1 + sinPhi) / (1 - sinPhi);
        double Npsi = (1 + sinPsi) / (1 - sinPsi);

        double sigma1_yield, sigma3_yield;

        if (sigma1 > TensileStrength)
        {
            // Tension cutoff return
            sigma1_yield = TensileStrength;
            sigma3_yield = sigma3;
        }
        else
        {
            // Shear return
            // Using non-associated flow rule
            double dLambda = f / (2 * ShearModulus * (1 + Nphi * Npsi));

            sigma1_yield = sigma1 - 2 * ShearModulus * dLambda * Npsi;
            sigma3_yield = sigma3 + 2 * ShearModulus * dLambda;

            // Ensure we're on yield surface
            double fCheck = sigma1_yield - sigma3_yield * Nphi - 2 * c * Math.Sqrt(Nphi);
            if (fCheck > 1e-6)
            {
                // Apex return (corner region)
                double apex_p = c / Math.Tan(phi);
                sigma1_yield = apex_p;
                sigma3_yield = apex_p;
            }
        }

        // Transform back to Cartesian stresses
        double p_new = (sigma1_yield + sigma3_yield) / 2;
        double R_new = (sigma1_yield - sigma3_yield) / 2;

        // Principal direction angle
        double theta = 0.5 * Math.Atan2(2 * tauXY, sigmaX - sigmaY);

        double sigmaX_new = p_new + R_new * Math.Cos(2 * theta);
        double sigmaY_new = p_new - R_new * Math.Cos(2 * theta);
        double tauXY_new = R_new * Math.Sin(2 * theta);

        // Calculate plastic strain increment
        double dPlasticStrain = Math.Sqrt(
            Math.Pow(sigma1 - sigma1_yield, 2) +
            Math.Pow(sigma3 - sigma3_yield, 2)) / (2 * ShearModulus);

        return (sigmaX_new, sigmaY_new, tauXY_new, dPlasticStrain);
    }

    #endregion

    #region Elasticity Matrix

    /// <summary>
    /// Get plane strain elasticity matrix [D] (3x3 for σx, σy, τxy)
    /// </summary>
    public double[,] GetPlaneStrainElasticityMatrix()
    {
        double E = EnableDamage ? GetDamagedYoungModulus() : YoungModulus;
        double nu = PoissonRatio;

        double factor = E / ((1 + nu) * (1 - 2 * nu));

        return new double[,]
        {
            { factor * (1 - nu), factor * nu, 0 },
            { factor * nu, factor * (1 - nu), 0 },
            { 0, 0, factor * (1 - 2 * nu) / 2 }
        };
    }

    /// <summary>
    /// Get plane stress elasticity matrix [D] (3x3 for σx, σy, τxy)
    /// </summary>
    public double[,] GetPlaneStressElasticityMatrix()
    {
        double E = EnableDamage ? GetDamagedYoungModulus() : YoungModulus;
        double nu = PoissonRatio;

        double factor = E / (1 - nu * nu);

        return new double[,]
        {
            { factor, factor * nu, 0 },
            { factor * nu, factor, 0 },
            { 0, 0, factor * (1 - nu) / 2 }
        };
    }

    #endregion

    #region Presets

    public static GeomechanicalMaterial2D CreateFromPreset(string presetName)
    {
        return presetName.ToLower() switch
        {
            "granite" => CreateGranite(),
            "sandstone" => CreateSandstone(),
            "shale" => CreateShale(),
            "limestone" => CreateLimestone(),
            "clay" => CreateClay(),
            "sand" => CreateSand(),
            "concrete" => CreateConcrete(),
            "steel" => CreateSteel(),
            _ => new GeomechanicalMaterial2D { Name = presetName }
        };
    }

    public static GeomechanicalMaterial2D CreateGranite()
    {
        return new GeomechanicalMaterial2D
        {
            Name = "Granite",
            Color = new Vector4(0.7f, 0.7f, 0.7f, 1),
            YoungModulus = 50e9,
            PoissonRatio = 0.25,
            Density = 2700,
            Cohesion = 30e6,
            FrictionAngle = 45,
            TensileStrength = 15e6,
            DilationAngle = 15,
            HB_mi = 32,
            GSI = 75
        };
    }

    public static GeomechanicalMaterial2D CreateSandstone()
    {
        return new GeomechanicalMaterial2D
        {
            Name = "Sandstone",
            Color = new Vector4(0.9f, 0.8f, 0.5f, 1),
            YoungModulus = 20e9,
            PoissonRatio = 0.2,
            Density = 2400,
            Cohesion = 10e6,
            FrictionAngle = 35,
            TensileStrength = 5e6,
            DilationAngle = 10,
            HB_mi = 17,
            GSI = 60
        };
    }

    public static GeomechanicalMaterial2D CreateShale()
    {
        return new GeomechanicalMaterial2D
        {
            Name = "Shale",
            Color = new Vector4(0.4f, 0.4f, 0.5f, 1),
            YoungModulus = 10e9,
            PoissonRatio = 0.3,
            Density = 2500,
            Cohesion = 5e6,
            FrictionAngle = 25,
            TensileStrength = 2e6,
            DilationAngle = 5,
            HB_mi = 6,
            GSI = 40,
            HasUbiquitousJoints = true,
            JointDipAngle = 30,
            JointFrictionAngle = 15
        };
    }

    public static GeomechanicalMaterial2D CreateLimestone()
    {
        return new GeomechanicalMaterial2D
        {
            Name = "Limestone",
            Color = new Vector4(0.85f, 0.85f, 0.75f, 1),
            YoungModulus = 30e9,
            PoissonRatio = 0.25,
            Density = 2600,
            Cohesion = 15e6,
            FrictionAngle = 38,
            TensileStrength = 8e6,
            DilationAngle = 10,
            HB_mi = 12,
            GSI = 65
        };
    }

    public static GeomechanicalMaterial2D CreateClay()
    {
        return new GeomechanicalMaterial2D
        {
            Name = "Clay",
            Color = new Vector4(0.6f, 0.5f, 0.3f, 1),
            YoungModulus = 50e6,
            PoissonRatio = 0.4,
            Density = 1800,
            Cohesion = 20e3,
            FrictionAngle = 20,
            TensileStrength = 5e3,
            DilationAngle = 0,
            FailureCriterion = FailureCriterion2D.DruckerPrager
        };
    }

    public static GeomechanicalMaterial2D CreateSand()
    {
        return new GeomechanicalMaterial2D
        {
            Name = "Sand",
            Color = new Vector4(0.95f, 0.9f, 0.6f, 1),
            YoungModulus = 100e6,
            PoissonRatio = 0.3,
            Density = 1600,
            Cohesion = 0,
            FrictionAngle = 33,
            TensileStrength = 0,
            DilationAngle = 5,
            FailureCriterion = FailureCriterion2D.LinearMohrCoulomb
        };
    }

    public static GeomechanicalMaterial2D CreateConcrete()
    {
        return new GeomechanicalMaterial2D
        {
            Name = "Concrete",
            Color = new Vector4(0.6f, 0.6f, 0.6f, 1),
            YoungModulus = 30e9,
            PoissonRatio = 0.2,
            Density = 2400,
            Cohesion = 5e6,
            FrictionAngle = 45,
            TensileStrength = 3e6,
            DilationAngle = 10
        };
    }

    public static GeomechanicalMaterial2D CreateSteel()
    {
        return new GeomechanicalMaterial2D
        {
            Name = "Steel",
            Color = new Vector4(0.4f, 0.45f, 0.5f, 1),
            YoungModulus = 200e9,
            PoissonRatio = 0.3,
            Density = 7850,
            Cohesion = 250e6,  // Yield stress / sqrt(3)
            FrictionAngle = 0,
            TensileStrength = 400e6
        };
    }

    /// <summary>
    /// Create a GeomechanicalMaterial2D from a PhysicalMaterial in the material library.
    /// Converts units and estimates missing parameters based on typical rock/soil relationships.
    /// </summary>
    /// <param name="physMat">The source physical material</param>
    /// <returns>A new GeomechanicalMaterial2D with converted properties</returns>
    public static GeomechanicalMaterial2D CreateFromPhysicalMaterial(PhysicalMaterial physMat)
    {
        if (physMat == null)
            throw new ArgumentNullException(nameof(physMat));

        var geomMat = new GeomechanicalMaterial2D
        {
            Name = physMat.Name
        };

        // Density (kg/m³) - direct copy
        if (physMat.Density_kg_m3.HasValue)
            geomMat.Density = physMat.Density_kg_m3.Value;

        // Young's Modulus: Convert from GPa to Pa
        if (physMat.YoungModulus_GPa.HasValue)
            geomMat.YoungModulus = physMat.YoungModulus_GPa.Value * 1e9;

        // Poisson's Ratio - direct copy (dimensionless)
        if (physMat.PoissonRatio.HasValue)
            geomMat.PoissonRatio = physMat.PoissonRatio.Value;

        // Friction angle (degrees) - direct copy
        if (physMat.FrictionAngle_deg.HasValue)
            geomMat.FrictionAngle = physMat.FrictionAngle_deg.Value;

        // Cohesion: Convert from MPa to Pa
        if (physMat.Cohesion_MPa.HasValue)
        {
            geomMat.Cohesion = physMat.Cohesion_MPa.Value * 1e6;
        }
        else if (physMat.CompressiveStrength_MPa.HasValue && physMat.FrictionAngle_deg.HasValue)
        {
            // Estimate cohesion from UCS using Mohr-Coulomb relationship:
            // UCS = 2c * cos(φ) / (1 - sin(φ))
            // c = UCS * (1 - sin(φ)) / (2 * cos(φ))
            double phi = physMat.FrictionAngle_deg.Value * Math.PI / 180.0;
            double ucs_Pa = physMat.CompressiveStrength_MPa.Value * 1e6;
            geomMat.Cohesion = ucs_Pa * (1 - Math.Sin(phi)) / (2 * Math.Cos(phi));
        }
        else if (physMat.CompressiveStrength_MPa.HasValue)
        {
            // Rough estimate: c ≈ UCS / 4 for typical rocks
            geomMat.Cohesion = physMat.CompressiveStrength_MPa.Value * 1e6 / 4.0;
        }

        // Tensile strength: Convert from MPa to Pa
        if (physMat.TensileStrength_MPa.HasValue)
            geomMat.TensileStrength = physMat.TensileStrength_MPa.Value * 1e6;
        else if (physMat.CompressiveStrength_MPa.HasValue)
            // Typical rock: T ≈ UCS / 10 to UCS / 20
            geomMat.TensileStrength = physMat.CompressiveStrength_MPa.Value * 1e6 / 15.0;

        // Dilation angle (degrees) - direct copy or estimate
        if (physMat.DilationAngle_deg.HasValue)
            geomMat.DilationAngle = physMat.DilationAngle_deg.Value;
        else if (physMat.FrictionAngle_deg.HasValue)
            // Typical non-associated flow: ψ ≈ φ/3 to φ/2
            geomMat.DilationAngle = physMat.FrictionAngle_deg.Value / 3.0;

        // Hoek-Brown parameters
        if (physMat.HoekBrown_mi.HasValue)
            geomMat.HB_mi = physMat.HoekBrown_mi.Value;
        else
            // Estimate mi from material name (common defaults)
            geomMat.HB_mi = EstimateHoekBrownMi(physMat.Name);

        if (physMat.GSI.HasValue)
            geomMat.GSI = physMat.GSI.Value;
        else
            geomMat.GSI = 60; // Default for moderately jointed rock

        if (physMat.DisturbanceFactor_D.HasValue)
            geomMat.DisturbanceFactor = physMat.DisturbanceFactor_D.Value;

        // Calculate Hoek-Brown mb, s, a from mi, GSI, D
        geomMat.CalculateHoekBrownFromGSI();

        // Thermal properties
        if (physMat.ThermalConductivity_W_mK.HasValue)
            geomMat.ThermalConductivity = physMat.ThermalConductivity_W_mK.Value;

        if (physMat.SpecificHeatCapacity_J_kgK.HasValue)
            geomMat.SpecificHeat = physMat.SpecificHeatCapacity_J_kgK.Value;

        // Porosity
        if (physMat.TypicalPorosity_fraction.HasValue)
            geomMat.Porosity = physMat.TypicalPorosity_fraction.Value;

        // Set color based on material type
        geomMat.Color = GetDefaultColorForMaterial(physMat.Name);

        return geomMat;
    }

    /// <summary>
    /// Estimate Hoek-Brown mi constant based on rock type name
    /// </summary>
    private static double EstimateHoekBrownMi(string materialName)
    {
        var lower = materialName.ToLower();

        // Values from Hoek (2007) "Practical Rock Engineering"
        if (lower.Contains("granite")) return 32;
        if (lower.Contains("basalt")) return 25;
        if (lower.Contains("diorite")) return 25;
        if (lower.Contains("gabbro")) return 27;
        if (lower.Contains("rhyolite")) return 25;
        if (lower.Contains("andesite")) return 25;
        if (lower.Contains("sandstone")) return 17;
        if (lower.Contains("limestone")) return 12;
        if (lower.Contains("dolomite")) return 9;
        if (lower.Contains("shale")) return 6;
        if (lower.Contains("mudstone")) return 4;
        if (lower.Contains("claystone")) return 4;
        if (lower.Contains("marble")) return 9;
        if (lower.Contains("quartzite")) return 20;
        if (lower.Contains("gneiss")) return 28;
        if (lower.Contains("schist")) return 12;
        if (lower.Contains("slate")) return 7;
        if (lower.Contains("coal")) return 6;
        if (lower.Contains("clay")) return 4;
        if (lower.Contains("sand")) return 17;

        return 10; // Default for unknown rock
    }

    /// <summary>
    /// Get a default display color based on material name
    /// </summary>
    private static Vector4 GetDefaultColorForMaterial(string materialName)
    {
        var lower = materialName.ToLower();

        if (lower.Contains("granite")) return new Vector4(0.7f, 0.7f, 0.7f, 1);
        if (lower.Contains("basalt")) return new Vector4(0.3f, 0.3f, 0.35f, 1);
        if (lower.Contains("sandstone")) return new Vector4(0.9f, 0.8f, 0.5f, 1);
        if (lower.Contains("limestone")) return new Vector4(0.85f, 0.85f, 0.75f, 1);
        if (lower.Contains("shale")) return new Vector4(0.4f, 0.4f, 0.5f, 1);
        if (lower.Contains("clay")) return new Vector4(0.6f, 0.5f, 0.3f, 1);
        if (lower.Contains("sand")) return new Vector4(0.95f, 0.9f, 0.6f, 1);
        if (lower.Contains("marble")) return new Vector4(0.95f, 0.95f, 0.95f, 1);
        if (lower.Contains("quartzite")) return new Vector4(0.9f, 0.9f, 0.85f, 1);
        if (lower.Contains("coal")) return new Vector4(0.2f, 0.2f, 0.2f, 1);
        if (lower.Contains("steel")) return new Vector4(0.4f, 0.45f, 0.5f, 1);
        if (lower.Contains("concrete")) return new Vector4(0.6f, 0.6f, 0.6f, 1);

        return new Vector4(0.5f, 0.5f, 0.5f, 1); // Default gray
    }

    #endregion

    public GeomechanicalMaterial2D Clone()
    {
        return (GeomechanicalMaterial2D)MemberwiseClone();
    }
}

/// <summary>
/// Material library for managing multiple materials in a simulation
/// </summary>
public class GeomechanicalMaterialLibrary2D
{
    private readonly Dictionary<int, GeomechanicalMaterial2D> _materials = new();
    private int _nextId = 1;

    public IReadOnlyDictionary<int, GeomechanicalMaterial2D> Materials => _materials;

    public int AddMaterial(GeomechanicalMaterial2D material)
    {
        material.Id = _nextId++;
        _materials[material.Id] = material;
        return material.Id;
    }

    public void RemoveMaterial(int id)
    {
        _materials.Remove(id);
    }

    public GeomechanicalMaterial2D GetMaterial(int id)
    {
        return _materials.TryGetValue(id, out var mat) ? mat : null;
    }

    public void Clear()
    {
        _materials.Clear();
        _nextId = 1;
    }

    public void LoadDefaults()
    {
        AddMaterial(GeomechanicalMaterial2D.CreateGranite());
        AddMaterial(GeomechanicalMaterial2D.CreateSandstone());
        AddMaterial(GeomechanicalMaterial2D.CreateShale());
        AddMaterial(GeomechanicalMaterial2D.CreateLimestone());
        AddMaterial(GeomechanicalMaterial2D.CreateClay());
        AddMaterial(GeomechanicalMaterial2D.CreateSand());
        AddMaterial(GeomechanicalMaterial2D.CreateConcrete());
        AddMaterial(GeomechanicalMaterial2D.CreateSteel());
    }
}
