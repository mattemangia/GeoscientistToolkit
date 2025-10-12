// GeoscientistToolkit/Business/Thermodynamics/ActivityCoefficientCalculator.cs
//
// Activity coefficient calculation using Debye-Hückel, extended Debye-Hückel,
// Davies, and Pitzer equations for various ionic strength ranges.
//
// SOURCES:
// - Debye, P. & Hückel, E., 1923. The theory of electrolytes I. Lowering of freezing point 
//   and related phenomena. Physikalische Zeitschrift, 24, 185-206.
// - Davies, C.W., 1962. Ion Association. Butterworths, London.
// - Pitzer, K.S., 1973. Thermodynamics of electrolytes. I. Theoretical basis and general equations. 
//   Journal of Physical Chemistry, 77(2), 268-277.
// - Pitzer, K.S., 1991. Activity Coefficients in Electrolyte Solutions, 2nd ed. CRC Press.
// - Truesdell, A.H. & Jones, B.F., 1974. WATEQ, a computer program for calculating chemical 
//   equilibria of natural waters. Journal of Research USGS, 2(2), 233-248.
// - Helgeson, H.C., Kirkham, D.H. & Flowers, G.C., 1981. Theoretical prediction of the 
//   thermodynamic behavior of aqueous electrolytes at high pressures and temperatures. 
//   American Journal of Science, 281, 1249-1516.
//

using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business.Thermodynamics;

public class ActivityCoefficientCalculator
{
    // Physical constants (CODATA 2018 values)
    private const double AVOGADRO = 6.02214076e23; // mol⁻¹
    private const double BOLTZMANN = 1.380649e-23; // J·K⁻¹
    private const double ELEMENTARY_CHARGE = 1.602176634e-19; // C
    private const double PERMITTIVITY_VACUUM = 8.8541878128e-12; // F·m⁻¹

    private readonly CompoundLibrary _compoundLibrary;
    private readonly Dictionary<string, PitzerParameters> _pitzerParams;

    public ActivityCoefficientCalculator()
    {
        _compoundLibrary = CompoundLibrary.Instance;
        _pitzerParams = LoadPitzerParameters();
    }

    /// <summary>
    ///     Calculate activity coefficients for all species in the state.
    ///     Automatically selects appropriate model based on ionic strength.
    /// </summary>
    public void CalculateActivityCoefficients(ThermodynamicState state)
    {
        var I = state.IonicStrength_molkg;

        // Select model based on ionic strength ranges
        // Source: Nordstrom & Munoz, 1994. Geochemical Thermodynamics, Table 9.2
        if (I < 0.1)
        {
            // Use extended Debye-Hückel or Davies equation
            CalculateDebyeHuckelCoefficients(state);
        }
        else if (I < 6.0)
        {
            // Use Pitzer equations for higher ionic strength
            CalculatePitzerCoefficients(state);
        }
        else
        {
            Logger.LogWarning($"[ActivityCoefficient] Ionic strength {I:F2} mol/kg exceeds validated range");
            CalculatePitzerCoefficients(state); // Use Pitzer with caution
        }
    }

    /// <summary>
    ///     Calculate single ion activity coefficient.
    /// </summary>
    public double CalculateSingleIonActivityCoefficient(string species, ThermodynamicState state)
    {
        var compound = _compoundLibrary.Find(species);
        if (compound == null || compound.Phase != CompoundPhase.Aqueous)
            return 1.0; // Unity for non-aqueous or neutral species

        if (compound.IonicCharge == null || compound.IonicCharge == 0)
            return CalculateNeutralSpeciesActivity(state); // Neutral aqueous species

        var I = state.IonicStrength_molkg;

        if (I < 0.1)
            return CalculateExtendedDebyeHuckel(compound.IonicCharge.Value, I, state.Temperature_K);
        return CalculatePitzerSingleIon(species, state);
    }

    /// <summary>
    ///     Extended Debye-Hückel equation with ion size parameter.
    ///     Source: Truesdell & Jones, 1974. WATEQ model, Eq. 20-21
    ///     Valid for I < 0.1 mol/ kg
    /// </summary>
    private double CalculateExtendedDebyeHuckel(int charge, double I, double T_K)
    {
        // Calculate A and B parameters (temperature dependent)
        var (A, B) = CalculateDebyeHuckelParameters(T_K);

        // Ion size parameter (Å) - typical values from literature
        // Source: Kielland, J., 1937. Individual activity coefficients of ions in aqueous solutions. 
        //         Journal of the American Chemical Society, 59(9), 1675-1678.
        var a = GetIonSizeParameter(Math.Abs(charge));

        // Extended Debye-Hückel: log₁₀(γ) = -A·z²·√I / (1 + B·a·√I)
        var z2 = charge * charge;
        var sqrtI = Math.Sqrt(I);

        var logGamma = -A * z2 * sqrtI / (1.0 + B * a * sqrtI);

        return Math.Pow(10, logGamma);
    }

    /// <summary>
    ///     Calculate Debye-Hückel A and B parameters as function of temperature.
    ///     Source: Helgeson et al., 1981. American Journal of Science, 281, Eq. 141-142
    /// </summary>
    private (double A, double B) CalculateDebyeHuckelParameters(double T_K)
    {
        // Get water properties at temperature
        var (epsilon, rho) = GetWaterProperties(T_K);

        // A = (e²/(4πε₀εₖT))^(3/2) · √(2πNₐρ/1000) / (2.303)
        // Units: (mol/kg)^(-1/2)
        var e = ELEMENTARY_CHARGE;
        var factor1 = Math.Pow(e * e / (4.0 * Math.PI * PERMITTIVITY_VACUUM * epsilon * BOLTZMANN * T_K), 1.5);
        var factor2 = Math.Sqrt(2.0 * Math.PI * AVOGADRO * rho / 1000.0);
        var A = factor1 * factor2 / 2.303;

        // B = √(2e²Nₐρ/(1000ε₀εₖT)) · 10⁸
        // Units: (mol/kg)^(-1/2) · Å⁻¹
        var B = Math.Sqrt(2.0 * e * e * AVOGADRO * rho / (1000.0 * PERMITTIVITY_VACUUM * epsilon * BOLTZMANN * T_K)) *
            1e8 / 2.303;

        return (A, B);
    }

    /// <summary>
    ///     Get water dielectric constant and density as function of temperature.
    ///     Source: Bradley, D.J. & Pitzer, K.S., 1979. Thermodynamics of electrolytes. 12.
    ///     Dielectric properties of water and Debye-Hückel parameters to 350°C and 1 kbar.
    ///     Journal of Physical Chemistry, 83(12), 1599-1603.
    /// </summary>
    private (double epsilon, double rho_kg_m3) GetWaterProperties(double T_K)
    {
        var T_C = T_K - 273.15;

        // Dielectric constant (dimensionless)
        // Valid 0-350°C at 1 bar
        var epsilon = 87.74 - 0.40008 * T_C + 9.398e-4 * T_C * T_C - 1.410e-6 * T_C * T_C * T_C;

        // Density (kg/m³)
        // IAPWS-IF97 formulation simplified for 1 bar
        var rho = 1000.0 * (1.0 - Math.Pow((T_C + 288.9414) / 508929.2, 1.68) * Math.Pow(T_C - 3.9863, 2) / 1000.0);

        return (epsilon, rho);
    }

    /// <summary>
    ///     Ion size parameters (Å) from Kielland compilation.
    ///     Source: Kielland, J., 1937. JACS, 59(9), 1675-1678.
    /// </summary>
    private double GetIonSizeParameter(int absCharge)
    {
        return absCharge switch
        {
            1 => 4.5, // Monovalent: K⁺, Cl⁻, etc.
            2 => 5.0, // Divalent: Ca²⁺, SO₄²⁻, etc.
            3 => 9.0, // Trivalent: Al³⁺, Fe³⁺, etc.
            _ => 6.0 // Default
        };
    }

    /// <summary>
    ///     Davies equation - simplified extended Debye-Hückel.
    ///     Source: Davies, C.W., 1962. Ion Association. Butterworths.
    ///     Valid for I < 0.5 mol/ kg
    /// </summary>
    private double CalculateDaviesEquation(int charge, double I, double T_K)
    {
        // Calculate temperature-dependent A parameter
        var (A, _) = CalculateDebyeHuckelParameters(T_K);
        var z2 = charge * charge;
        var sqrtI = Math.Sqrt(I);

        // log₁₀(γ) = -A·z²·(√I/(1+√I) - 0.3·I)
        var logGamma = -A * z2 * (sqrtI / (1.0 + sqrtI) - 0.3 * I);

        return Math.Pow(10, logGamma);
    }

    /// <summary>
    ///     Pitzer equations for activity coefficients.
    ///     This is a more complete implementation for a single electrolyte.
    ///     A full multi-electrolyte system requires mixing terms (Theta and Psi parameters).
    ///     Source: Pitzer, K.S., 1991. Activity Coefficients in Electrolyte Solutions, 2nd ed. CRC Press. Chapter 3.
    /// </summary>
    private void CalculatePitzerCoefficients(ThermodynamicState state)
    {
        // This method should ideally solve for all ions simultaneously.
        // For demonstration, we calculate single-ion coefficients iteratively.
        foreach (var (species, moles) in state.SpeciesMoles)
        {
            var compound = _compoundLibrary.Find(species);
            if (compound?.Phase != CompoundPhase.Aqueous || compound.IonicCharge == null)
                continue;

            var gamma = CalculatePitzerSingleIon(species, state);
            var molality = moles / state.Volume_L; // Approximation
            state.Activities[species] = gamma * molality;
        }
    }

    private double CalculatePitzerSingleIon(string species, ThermodynamicState state)
    {
        var compound = _compoundLibrary.Find(species);
        if (compound?.IonicCharge == null || compound.IonicCharge == 0)
            return CalculateNeutralSpeciesActivity(state, compound);

        var I = state.IonicStrength_molkg;
        var (A_phi, _) = CalculateDebyeHuckelParameters(state.Temperature_K);
        var z = compound.IonicCharge.Value;

        // Pitzer equation for natural logarithm of single-ion activity coefficient (ln γ)
        // Note: Full model requires summation over all cations (c) and anions (a)
        var lnGamma = z * z / 2.0 * GetPitzerDebyeHuckelTerm(I, A_phi);

        // This simplified version only considers the primary ion-ion interactions
        // A full model would sum over all other ions in the solution
        if (_pitzerParams.TryGetValue(species, out var param))
        {
            var g = 2.0 * (1.0 - (1.0 + param.Alpha1 * Math.Sqrt(I)) * Math.Exp(-param.Alpha1 * Math.Sqrt(I))) /
                    (param.Alpha1 * param.Alpha1 * I);
            lnGamma += I * (2 * param.Beta0 + 2 * param.Beta1 * g);
        }

        return Math.Exp(lnGamma);
    }

    private double GetPitzerDebyeHuckelTerm(double I, double A_phi)
    {
        const double b = 1.2; // kg^1/2 mol^-1/2, a constant in the Pitzer model
        return -A_phi * (Math.Sqrt(I) / (1.0 + b * Math.Sqrt(I)) + 2.0 / b * Math.Log(1.0 + b * Math.Sqrt(I)));
    }

    /// <summary>
    ///     Activity coefficient for neutral aqueous species using Setchenow equation.
    ///     Source: Setchenow, M., 1889.
    /// </summary>
    private double CalculateNeutralSpeciesActivity(ThermodynamicState state, ChemicalCompound neutralCompound = null)
    {
        // log₁₀(γ) = k_s · I
        // k_s (Setchenow coefficient) is species-specific.
        var k_s = neutralCompound?.SetchenowCoefficient ?? 0.1; // Default if not in library
        var logGamma = k_s * state.IonicStrength_molkg;
        return Math.Pow(10, logGamma);
    }

    /// <summary>
    ///     Load Pitzer interaction parameters from database.
    ///     Source: Harvie, C.E., Møller, N. & Weare, J.H., 1984. The prediction of mineral solubilities
    ///     in natural waters: The Na-K-Mg-Ca-H-Cl-SO4-OH-HCO3-CO3-CO2-H2O system to high ionic strengths
    ///     at 25°C. Geochimica et Cosmochimica Acta, 48(4), 723-751.
    /// </summary>
    private Dictionary<string, PitzerParameters> LoadPitzerParameters()
    {
        var parameters = new Dictionary<string, PitzerParameters>();

        // Major seawater ions (from Harvie et al., 1984)
        parameters["Na⁺"] = new PitzerParameters { Beta0 = 0.0765, Beta1 = 0.2664, Cphi = 0.00127 };
        parameters["K⁺"] = new PitzerParameters { Beta0 = 0.0500, Beta1 = 0.2122, Cphi = -0.00084 };
        parameters["Mg²⁺"] = new PitzerParameters { Beta0 = 0.3514, Beta1 = 1.6815, Cphi = 0.00519 };
        parameters["Ca²⁺"] = new PitzerParameters { Beta0 = 0.3159, Beta1 = 1.6140, Cphi = -0.00034 };
        parameters["Cl⁻"] = new PitzerParameters { Beta0 = 0.0, Beta1 = 0.0, Cphi = 0.0 }; // Reference
        parameters["SO₄²⁻"] = new PitzerParameters { Beta0 = 0.0, Beta1 = 0.0, Cphi = 0.0 };
        parameters["HCO₃⁻"] = new PitzerParameters { Beta0 = 0.0277, Beta1 = 0.0411, Cphi = 0.0 };
        parameters["CO₃²⁻"] = new PitzerParameters { Beta0 = 0.0399, Beta1 = 1.389, Cphi = 0.0044 };

        return parameters;
    }

    /// <summary>
    ///     Calculate mean activity coefficient for electrolyte.
    ///     Source: Robinson, R.A. & Stokes, R.H., 1959. Electrolyte Solutions, 2nd ed. Butterworths.
    /// </summary>
    public double CalculateMeanActivityCoefficient(string cation, string anion, ThermodynamicState state)
    {
        var gammaCation = CalculateSingleIonActivityCoefficient(cation, state);
        var gammaAnion = CalculateSingleIonActivityCoefficient(anion, state);

        var cationCompound = _compoundLibrary.Find(cation);
        var anionCompound = _compoundLibrary.Find(anion);

        var nuPlus = Math.Abs(anionCompound?.IonicCharge ?? 1);
        var nuMinus = Math.Abs(cationCompound?.IonicCharge ?? 1);

        // γ± = (γ₊^ν₊ · γ₋^ν₋)^(1/(ν₊+ν₋))
        var gammaMean = Math.Pow(Math.Pow(gammaCation, nuPlus) * Math.Pow(gammaAnion, nuMinus),
            1.0 / (nuPlus + nuMinus));

        return gammaMean;
    }

    private void CalculateDebyeHuckelCoefficients(ThermodynamicState state)
    {
        var I = state.IonicStrength_molkg;
        var T = state.Temperature_K;

        foreach (var (species, moles) in state.SpeciesMoles)
        {
            var compound = _compoundLibrary.Find(species);
            if (compound?.Phase != CompoundPhase.Aqueous)
                continue;

            double gamma;
            if (compound.IonicCharge == null || compound.IonicCharge == 0)
            {
                gamma = CalculateNeutralSpeciesActivity(state);
            }
            else if (I < 0.01)
            {
                // Use simple Debye-Hückel for very dilute solutions
                var (A, _) = CalculateDebyeHuckelParameters(T);
                var z2 = compound.IonicCharge.Value * compound.IonicCharge.Value;
                var logGamma = -A * z2 * Math.Sqrt(I);
                gamma = Math.Pow(10, logGamma);
            }
            else
            {
                // Use extended Debye-Hückel
                gamma = CalculateExtendedDebyeHuckel(compound.IonicCharge.Value, I, T);
            }

            var molality = moles / state.Volume_L;
            state.Activities[species] = gamma * molality;
        }
    }
}

/// <summary>
///     Pitzer virial coefficients for ion-ion interactions.
/// </summary>
public class PitzerParameters
{
    public double Beta0 { get; set; } // Second virial coefficient
    public double Beta1 { get; set; } // Second virial coefficient (exponential term)
    public double Beta2 { get; set; } // For 2-2 electrolytes
    public double Cphi { get; set; } // Third virial coefficient
    public double Alpha1 { get; set; } = 2.0; // Default Pitzer α₁
    public double Alpha2 { get; set; } = 12.0; // Default Pitzer α₂ for 2-2 electrolytes
}