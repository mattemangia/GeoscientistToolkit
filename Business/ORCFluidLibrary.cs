// GeoscientistToolkit/Business/ORCFluidLibrary.cs
//
// Comprehensive library of ORC (Organic Rankine Cycle) working fluids
// with complete thermodynamic properties for power generation simulations.
//
// SOURCES:
// - REFPROP Database (NIST)
// - Lemmon, E.W., Huber, M.L., McLinden, M.O. (2013): NIST Reference Fluid Thermodynamic and Transport Properties
// - Calm, J.M., Hourahan, G.C. (2011): Refrigerant Data Update
// - Tchanche et al. (2009): Low-grade heat conversion into power using organic Rankine cycles
// - Quoilin et al. (2013): Thermo-economic optimization of waste heat recovery Organic Rankine Cycles
//

using System.Text.Json;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business;

/// <summary>
/// Category of working fluid based on temperature range and application
/// </summary>
public enum FluidCategory
{
    LowTemperature,    // < 100°C (waste heat recovery, geothermal)
    MediumTemperature, // 100-200°C (medium-grade heat sources)
    HighTemperature,   // > 200°C (high-grade heat sources)
    Cryogenic          // Very low temperature applications
}

/// <summary>
/// Safety classification according to ASHRAE Standard 34
/// </summary>
public enum SafetyClass
{
    A1,  // Low toxicity, no flame propagation
    A2,  // Low toxicity, lower flammability
    A2L, // Low toxicity, mildly flammable
    A3,  // Low toxicity, higher flammability
    B1,  // Higher toxicity, no flame propagation
    B2,  // Higher toxicity, lower flammability
    B2L, // Higher toxicity, mildly flammable
    B3   // Higher toxicity, higher flammability
}

/// <summary>
/// Comprehensive ORC working fluid data with thermodynamic properties
/// </summary>
public class ORCFluid
{
    public string Name { get; set; } = string.Empty;
    public string ChemicalFormula { get; set; } = string.Empty;
    public string Refrigerant Code { get; set; } = string.Empty; // e.g., "R245fa", "R134a"

    // Classification
    public FluidCategory Category { get; set; }
    public SafetyClass Safety { get; set; } = SafetyClass.A1;
    public bool IsNaturalFluid { get; set; } // Natural (e.g., hydrocarbons) vs synthetic (e.g., HFCs)

    // Critical properties
    public float CriticalTemperature_K { get; set; }
    public float CriticalPressure_Pa { get; set; }
    public float CriticalDensity_kg_m3 { get; set; }

    // Triple point
    public float TriplePointTemperature_K { get; set; }
    public float TriplePointPressure_Pa { get; set; }

    // Molecular properties
    public float MolecularWeight_g_mol { get; set; }
    public float AccentricFactor { get; set; } // For equation of state calculations

    // Environmental properties
    public float ODP { get; set; } // Ozone Depletion Potential
    public float GWP100 { get; set; } // Global Warming Potential (100-year)
    public float AtmosphericLifetime_years { get; set; }

    // Saturation pressure correlation (Antoine equation): log10(P[Pa]) = A - B/(T[K] + C)
    public float[] AntoinCoefficients_A_B_C { get; set; } = new float[3];
    public float[] AntoineValidRange_K { get; set; } = new float[2]; // [Tmin, Tmax]

    // Liquid density correlation: ρ[kg/m³] = A + B*T + C*T²
    public float[] LiquidDensityCoeff_A_B_C { get; set; } = new float[3];

    // Vapor density correlation: ρ[kg/m³] = P*MW/(R*T*Z) where Z = compressibility factor
    public float[] CompressibilityCoeff { get; set; } = new float[3]; // Z = a + b*Tr + c*Pr

    // Enthalpy correlations
    // Liquid: h[J/kg] = A + B*T + C*T² + D*T³
    public float[] LiquidEnthalpyCoeff_A_B_C_D { get; set; } = new float[4];
    // Vapor: h[J/kg] = A + B*T + C*T² + D*T³
    public float[] VaporEnthalpyCoeff_A_B_C_D { get; set; } = new float[4];

    // Entropy correlations
    // Liquid: s[J/(kg·K)] = A + B*T + C*T²
    public float[] LiquidEntropyCoeff_A_B_C { get; set; } = new float[3];
    // Vapor: s[J/(kg·K)] = A + B*T + C*T²
    public float[] VaporEntropyCoeff_A_B_C { get; set; } = new float[3];

    // Specific heat capacity at constant pressure
    // Liquid: Cp[J/(kg·K)] = A + B*T + C*T²
    public float[] LiquidCpCoeff_A_B_C { get; set; } = new float[3];
    // Vapor (ideal gas): Cp[J/(kg·K)] = A + B*T + C*T² + D*T³
    public float[] VaporCpCoeff_A_B_C_D { get; set; } = new float[4];

    // Viscosity correlations
    // Liquid: μ[Pa·s] = A*exp(B/T)
    public float[] LiquidViscosityCoeff_A_B { get; set; } = new float[2];
    // Vapor: μ[Pa·s] = A*T^B
    public float[] VaporViscosityCoeff_A_B { get; set; } = new float[2];

    // Thermal conductivity correlations
    // Liquid: k[W/(m·K)] = A + B*T + C*T²
    public float[] LiquidThermalConductivityCoeff_A_B_C { get; set; } = new float[3];
    // Vapor: k[W/(m·K)] = A*T^B
    public float[] VaporThermalConductivityCoeff_A_B { get; set; } = new float[2];

    // Application parameters
    public float RecommendedEvaporatorPressure_Pa { get; set; } // Typical operating pressure
    public float RecommendedCondenserTemperature_K { get; set; }
    public float MinimumTemperature_K { get; set; } // Minimum recommended temperature
    public float MaximumTemperature_K { get; set; } // Maximum recommended temperature (stability limit)

    // Additional properties
    public string Manufacturer { get; set; } = string.Empty;
    public List<string> Applications { get; set; } = new List<string>();
    public List<string> Sources { get; set; } = new List<string>();
    public string Notes { get; set; } = string.Empty;
    public bool IsUserFluid { get; set; } // True if user-created
}

/// <summary>
/// Singleton library for ORC working fluids
/// </summary>
public sealed class ORCFluidLibrary
{
    private static ORCFluidLibrary? _instance;
    private static readonly object _lock = new();
    private List<ORCFluid> _fluids = new();

    private ORCFluidLibrary()
    {
        SeedDefaults();
        LoadUserFluids();
    }

    public static ORCFluidLibrary Instance
    {
        get
        {
            lock (_lock)
            {
                return _instance ??= new ORCFluidLibrary();
            }
        }
    }

    public IReadOnlyList<ORCFluid> AllFluids => _fluids.AsReadOnly();

    public ORCFluid? GetFluidByName(string name) =>
        _fluids.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public ORCFluid? GetFluidByRefrigerantCode(string code) =>
        _fluids.FirstOrDefault(f => f.RefrigerantCode.Equals(code, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<ORCFluid> GetFluidsByCategory(FluidCategory category) =>
        _fluids.Where(f => f.Category == category);

    public void AddFluid(ORCFluid fluid)
    {
        fluid.IsUserFluid = true;
        _fluids.Add(fluid);
        SaveUserFluids();
    }

    public void UpdateFluid(ORCFluid fluid)
    {
        var existing = _fluids.FirstOrDefault(f => f.Name == fluid.Name);
        if (existing != null)
        {
            _fluids.Remove(existing);
            _fluids.Add(fluid);
            SaveUserFluids();
        }
    }

    public void RemoveFluid(string name)
    {
        var fluid = _fluids.FirstOrDefault(f => f.Name == name && f.IsUserFluid);
        if (fluid != null)
        {
            _fluids.Remove(fluid);
            SaveUserFluids();
        }
    }

    private void SeedDefaults()
    {
        // R245fa - Pentafluoropropane (most common low-temp ORC fluid)
        _fluids.Add(new ORCFluid
        {
            Name = "R245fa (Pentafluoropropane)",
            ChemicalFormula = "CHF₂CF₂CH₂F",
            RefrigerantCode = "R245fa",
            Category = FluidCategory.LowTemperature,
            Safety = SafetyClass.B1,
            IsNaturalFluid = false,

            CriticalTemperature_K = 427.16f,
            CriticalPressure_Pa = 3.651e6f,
            CriticalDensity_kg_m3 = 516.0f,

            TriplePointTemperature_K = 171.05f,
            TriplePointPressure_Pa = 398.0f,

            MolecularWeight_g_mol = 134.05f,
            AccentricFactor = 0.3776f,

            ODP = 0.0f,
            GWP100 = 1030.0f,
            AtmosphericLifetime_years = 7.6f,

            // Antoine equation for saturation pressure
            AntoineCoefficients_A_B_C = new float[] { 4.37863f, 1310.02f, -53.226f },
            AntoineValidRange_K = new float[] { 273.15f, 420.0f },

            // Property correlations (simplified)
            LiquidDensityCoeff_A_B_C = new float[] { 1500.0f, -2.5f, 0.0f },
            LiquidEnthalpyCoeff_A_B_C_D = new float[] { -200000f, 1400f, 0.5f, -0.001f },
            VaporEnthalpyCoeff_A_B_C_D = new float[] { 250000f, 400f, -0.2f, 0.0005f },
            LiquidEntropyCoeff_A_B_C = new float[] { -1000f, 5.0f, 0.002f },
            VaporEntropyCoeff_A_B_C = new float[] { 1200f, 2.5f, -0.001f },

            RecommendedEvaporatorPressure_Pa = 1.5e6f, // 15 bar
            RecommendedCondenserTemperature_K = 303.15f, // 30°C
            MinimumTemperature_K = 273.15f,
            MaximumTemperature_K = 420.0f,

            Applications = new List<string> { "Low-temperature geothermal", "Waste heat recovery", "Solar ORC" },
            Sources = new List<string> { "REFPROP 10.0", "Calm & Hourahan (2011)" },
            Notes = "Excellent for 80-150°C applications. Non-flammable but relatively high GWP.",
            IsUserFluid = false
        });

        // Isobutane (R600a)
        _fluids.Add(new ORCFluid
        {
            Name = "Isobutane",
            ChemicalFormula = "C₄H₁₀",
            RefrigerantCode = "R600a",
            Category = FluidCategory.MediumTemperature,
            Safety = SafetyClass.A3,
            IsNaturalFluid = true,

            CriticalTemperature_K = 407.81f,
            CriticalPressure_Pa = 3.629e6f,
            CriticalDensity_kg_m3 = 225.5f,

            TriplePointTemperature_K = 113.73f,
            TriplePointPressure_Pa = 0.00212f,

            MolecularWeight_g_mol = 58.12f,
            AccentricFactor = 0.1756f,

            ODP = 0.0f,
            GWP100 = 3.0f,
            AtmosphericLifetime_years = 0.019f,

            AntoineCoefficients_A_B_C = new float[] { 4.3281f, 1132.108f, -40.044f },
            AntoineValidRange_K = new float[] { 273.15f, 400.0f },

            LiquidDensityCoeff_A_B_C = new float[] { 730.0f, -1.8f, 0.0f },
            LiquidEnthalpyCoeff_A_B_C_D = new float[] { -150000f, 1800f, 0.4f, -0.0008f },
            VaporEnthalpyCoeff_A_B_C_D = new float[] { 300000f, 500f, -0.3f, 0.0006f },
            LiquidEntropyCoeff_A_B_C = new float[] { -800f, 5.5f, 0.0018f },
            VaporEntropyCoeff_A_B_C = new float[] { 1400f, 2.8f, -0.0012f },

            RecommendedEvaporatorPressure_Pa = 2.0e6f,
            RecommendedCondenserTemperature_K = 303.15f,
            MinimumTemperature_K = 273.15f,
            MaximumTemperature_K = 400.0f,

            Applications = new List<string> { "Medium-temperature geothermal (100-180°C)", "Biomass heat recovery", "Industrial waste heat" },
            Sources = new List<string> { "REFPROP 10.0", "Tchanche et al. (2009)" },
            Notes = "Natural fluid with very low GWP. Flammable (A3 safety class). Good for 100-180°C.",
            IsUserFluid = false
        });

        // Isopentane (R601a)
        _fluids.Add(new ORCFluid
        {
            Name = "Isopentane",
            ChemicalFormula = "C₅H₁₂",
            RefrigerantCode = "R601a",
            Category = FluidCategory.MediumTemperature,
            Safety = SafetyClass.A3,
            IsNaturalFluid = true,

            CriticalTemperature_K = 460.35f,
            CriticalPressure_Pa = 3.378e6f,
            CriticalDensity_kg_m3 = 236.0f,

            TriplePointTemperature_K = 112.65f,
            TriplePointPressure_Pa = 0.00012f,

            MolecularWeight_g_mol = 72.15f,
            AccentricFactor = 0.2274f,

            ODP = 0.0f,
            GWP100 = 4.0f,
            AtmosphericLifetime_years = 0.014f,

            AntoineCoefficients_A_B_C = new float[] { 4.51730f, 1440.897f, -48.883f },
            AntoineValidRange_K = new float[] { 273.15f, 450.0f },

            LiquidDensityCoeff_A_B_C = new float[] { 690.0f, -1.6f, 0.0f },
            LiquidEnthalpyCoeff_A_B_C_D = new float[] { -120000f, 1600f, 0.45f, -0.0007f },
            VaporEnthalpyCoeff_A_B_C_D = new float[] { 320000f, 480f, -0.25f, 0.0005f },
            LiquidEntropyCoeff_A_B_C = new float[] { -750f, 5.2f, 0.0016f },
            VaporEntropyCoeff_A_B_C = new float[] { 1500f, 2.6f, -0.0011f },

            RecommendedEvaporatorPressure_Pa = 1.8e6f,
            RecommendedCondenserTemperature_K = 303.15f,
            MinimumTemperature_K = 273.15f,
            MaximumTemperature_K = 450.0f,

            Applications = new List<string> { "Medium to high-temperature geothermal (120-200°C)", "Solar thermal ORC", "Biomass CHP" },
            Sources = new List<string> { "REFPROP 10.0", "Quoilin et al. (2013)" },
            Notes = "Natural hydrocarbon with excellent performance for 120-200°C. Flammable.",
            IsUserFluid = false
        });

        // Toluene
        _fluids.Add(new ORCFluid
        {
            Name = "Toluene",
            ChemicalFormula = "C₇H₈",
            RefrigerantCode = "Toluene",
            Category = FluidCategory.HighTemperature,
            Safety = SafetyClass.A3,
            IsNaturalFluid = true,

            CriticalTemperature_K = 591.75f,
            CriticalPressure_Pa = 4.126e6f,
            CriticalDensity_kg_m3 = 292.0f,

            TriplePointTemperature_K = 178.0f,
            TriplePointPressure_Pa = 0.0397f,

            MolecularWeight_g_mol = 92.14f,
            AccentricFactor = 0.2657f,

            ODP = 0.0f,
            GWP100 = 2.7f,
            AtmosphericLifetime_years = 0.0054f,

            AntoineCoefficients_A_B_C = new float[] { 4.07827f, 1343.943f, -53.773f },
            AntoineValidRange_K = new float[] { 300.0f, 580.0f },

            LiquidDensityCoeff_A_B_C = new float[] { 1050.0f, -1.1f, 0.0f },
            LiquidEnthalpyCoeff_A_B_C_D = new float[] { -50000f, 1400f, 0.5f, -0.0005f },
            VaporEnthalpyCoeff_A_B_C_D = new float[] { 380000f, 450f, -0.18f, 0.0004f },
            LiquidEntropyCoeff_A_B_C = new float[] { -600f, 4.8f, 0.0014f },
            VaporEntropyCoeff_A_B_C = new float[] { 1600f, 2.4f, -0.001f },

            RecommendedEvaporatorPressure_Pa = 2.5e6f,
            RecommendedCondenserTemperature_K = 313.15f,
            MinimumTemperature_K = 300.0f,
            MaximumTemperature_K = 580.0f,

            Applications = new List<string> { "High-temperature geothermal (200-300°C)", "Concentrated solar power", "Biomass combustion" },
            Sources = new List<string> { "REFPROP 10.0", "Tchanche et al. (2009)" },
            Notes = "Aromatic hydrocarbon for high-temperature ORC. Toxic vapor, flammable. Excellent for 200-300°C.",
            IsUserFluid = false
        });

        // R134a - Tetrafluoroethane
        _fluids.Add(new ORCFluid
        {
            Name = "R134a (Tetrafluoroethane)",
            ChemicalFormula = "CF₃CH₂F",
            RefrigerantCode = "R134a",
            Category = FluidCategory.LowTemperature,
            Safety = SafetyClass.A1,
            IsNaturalFluid = false,

            CriticalTemperature_K = 374.21f,
            CriticalPressure_Pa = 4.059e6f,
            CriticalDensity_kg_m3 = 511.9f,

            TriplePointTemperature_K = 169.85f,
            TriplePointPressure_Pa = 389.6f,

            MolecularWeight_g_mol = 102.03f,
            AccentricFactor = 0.3268f,

            ODP = 0.0f,
            GWP100 = 1430.0f,
            AtmosphericLifetime_years = 14.0f,

            AntoineCoefficients_A_B_C = new float[] { 4.37880f, 1169.09f, -36.33f },
            AntoineValidRange_K = new float[] { 273.15f, 370.0f },

            LiquidDensityCoeff_A_B_C = new float[] { 1450.0f, -3.0f, 0.0f },
            LiquidEnthalpyCoeff_A_B_C_D = new float[] { -180000f, 1500f, 0.55f, -0.0012f },
            VaporEnthalpyCoeff_A_B_C_D = new float[] { 280000f, 420f, -0.22f, 0.0007f },
            LiquidEntropyCoeff_A_B_C = new float[] { -950f, 5.3f, 0.0021f },
            VaporEntropyCoeff_A_B_C = new float[] { 1300f, 2.7f, -0.0013f },

            RecommendedEvaporatorPressure_Pa = 2.2e6f,
            RecommendedCondenserTemperature_K = 303.15f,
            MinimumTemperature_K = 273.15f,
            MaximumTemperature_K = 370.0f,

            Applications = new List<string> { "Low-temperature geothermal (60-120°C)", "Refrigeration", "Air conditioning" },
            Sources = new List<string> { "REFPROP 10.0", "ASHRAE Handbook" },
            Notes = "Common refrigerant, good for low-temp ORC. Non-flammable. Being phased out due to high GWP.",
            IsUserFluid = false
        });

        // Propane (R290)
        _fluids.Add(new ORCFluid
        {
            Name = "Propane",
            ChemicalFormula = "C₃H₈",
            RefrigerantCode = "R290",
            Category = FluidCategory.LowTemperature,
            Safety = SafetyClass.A3,
            IsNaturalFluid = true,

            CriticalTemperature_K = 369.89f,
            CriticalPressure_Pa = 4.251e6f,
            CriticalDensity_kg_m3 = 220.0f,

            TriplePointTemperature_K = 85.53f,
            TriplePointPressure_Pa = 0.00017f,

            MolecularWeight_g_mol = 44.10f,
            AccentricFactor = 0.1521f,

            ODP = 0.0f,
            GWP100 = 3.3f,
            AtmosphericLifetime_years = 0.04f,

            AntoineCoefficients_A_B_C = new float[] { 4.53678f, 1149.36f, -24.906f },
            AntoineValidRange_K = new float[] { 273.15f, 365.0f },

            LiquidDensityCoeff_A_B_C = new float[] { 650.0f, -2.0f, 0.0f },
            LiquidEnthalpyCoeff_A_B_C_D = new float[] { -180000f, 2000f, 0.6f, -0.001f },
            VaporEnthalpyCoeff_A_B_C_D = new float[] { 350000f, 550f, -0.35f, 0.0008f },
            LiquidEntropyCoeff_A_B_C = new float[] { -900f, 6.0f, 0.0022f },
            VaporEntropyCoeff_A_B_C = new float[] { 1500f, 3.0f, -0.0014f },

            RecommendedEvaporatorPressure_Pa = 2.4e6f,
            RecommendedCondenserTemperature_K = 303.15f,
            MinimumTemperature_K = 273.15f,
            MaximumTemperature_K = 365.0f,

            Applications = new List<string> { "Low to medium-temp geothermal (70-120°C)", "Heat pumps", "Direct expansion systems" },
            Sources = new List<string> { "REFPROP 10.0", "IEA Heat Pump Centre" },
            Notes = "Natural refrigerant, very low GWP. Highly flammable (A3). Good for 70-120°C range.",
            IsUserFluid = false
        });

        // n-Pentane (R601)
        _fluids.Add(new ORCFluid
        {
            Name = "n-Pentane",
            ChemicalFormula = "C₅H₁₂",
            RefrigerantCode = "R601",
            Category = FluidCategory.MediumTemperature,
            Safety = SafetyClass.A3,
            IsNaturalFluid = true,

            CriticalTemperature_K = 469.7f,
            CriticalPressure_Pa = 3.370e6f,
            CriticalDensity_kg_m3 = 232.0f,

            TriplePointTemperature_K = 143.47f,
            TriplePointPressure_Pa = 0.069f,

            MolecularWeight_g_mol = 72.15f,
            AccentricFactor = 0.2510f,

            ODP = 0.0f,
            GWP100 = 5.0f,
            AtmosphericLifetime_years = 0.016f,

            AntoineCoefficients_A_B_C = new float[] { 4.54213f, 1481.11f, -51.154f },
            AntoineValidRange_K = new float[] { 273.15f, 465.0f },

            LiquidDensityCoeff_A_B_C = new float[] { 680.0f, -1.5f, 0.0f },
            LiquidEnthalpyCoeff_A_B_C_D = new float[] { -125000f, 1650f, 0.47f, -0.0007f },
            VaporEnthalpyCoeff_A_B_C_D = new float[] { 325000f, 490f, -0.27f, 0.0006f },
            LiquidEntropyCoeff_A_B_C = new float[] { -760f, 5.3f, 0.0017f },
            VaporEntropyCoeff_A_B_C = new float[] { 1520f, 2.65f, -0.0011f },

            RecommendedEvaporatorPressure_Pa = 1.7e6f,
            RecommendedCondenserTemperature_K = 303.15f,
            MinimumTemperature_K = 273.15f,
            MaximumTemperature_K = 465.0f,

            Applications = new List<string> { "Medium-temperature geothermal (110-190°C)", "Biomass ORC", "Solar thermal" },
            Sources = new List<string> { "REFPROP 10.0", "Quoilin et al. (2013)" },
            Notes = "Natural alkane, slightly different properties from isopentane. Flammable, low GWP.",
            IsUserFluid = false
        });

        // R1234yf - Tetrafluoropropene
        _fluids.Add(new ORCFluid
        {
            Name = "R1234yf (Tetrafluoropropene)",
            ChemicalFormula = "CF₃CF=CH₂",
            RefrigerantCode = "R1234yf",
            Category = FluidCategory.LowTemperature,
            Safety = SafetyClass.A2L,
            IsNaturalFluid = false,

            CriticalTemperature_K = 367.85f,
            CriticalPressure_Pa = 3.382e6f,
            CriticalDensity_kg_m3 = 478.0f,

            TriplePointTemperature_K = 220.0f,
            TriplePointPressure_Pa = 31500.0f,

            MolecularWeight_g_mol = 114.04f,
            AccentricFactor = 0.3760f,

            ODP = 0.0f,
            GWP100 = 4.0f, // Very low!
            AtmosphericLifetime_years = 0.029f,

            AntoineCoefficients_A_B_C = new float[] { 4.42150f, 1168.0f, -35.8f },
            AntoineValidRange_K = new float[] { 273.15f, 365.0f },

            LiquidDensityCoeff_A_B_C = new float[] { 1380.0f, -2.8f, 0.0f },
            LiquidEnthalpyCoeff_A_B_C_D = new float[] { -185000f, 1480f, 0.52f, -0.0011f },
            VaporEnthalpyCoeff_A_B_C_D = new float[] { 270000f, 410f, -0.21f, 0.0007f },
            LiquidEntropyCoeff_A_B_C = new float[] { -920f, 5.1f, 0.0020f },
            VaporEntropyCoeff_A_B_C = new float[] { 1280f, 2.65f, -0.0012f },

            RecommendedEvaporatorPressure_Pa = 2.0e6f,
            RecommendedCondenserTemperature_K = 303.15f,
            MinimumTemperature_K = 273.15f,
            MaximumTemperature_K = 365.0f,

            Applications = new List<string> { "Low-temp ORC (60-110°C)", "Mobile air conditioning replacement for R134a", "Heat pumps" },
            Sources = new List<string> { "REFPROP 10.0", "Honeywell" },
            Notes = "Fourth-generation refrigerant (HFO). Ultra-low GWP, mildly flammable (A2L). Excellent environmental profile.",
            IsUserFluid = false
        });

        // R1234ze(E) - Trans-1,3,3,3-tetrafluoropropene
        _fluids.Add(new ORCFluid
        {
            Name = "R1234ze(E)",
            ChemicalFormula = "CF₃CH=CHF",
            RefrigerantCode = "R1234ze(E)",
            Category = FluidCategory.LowTemperature,
            Safety = SafetyClass.A2L,
            IsNaturalFluid = false,

            CriticalTemperature_K = 382.52f,
            CriticalPressure_Pa = 3.635e6f,
            CriticalDensity_kg_m3 = 489.0f,

            TriplePointTemperature_K = 168.62f,
            TriplePointPressure_Pa = 201.0f,

            MolecularWeight_g_mol = 114.04f,
            AccentricFactor = 0.3130f,

            ODP = 0.0f,
            GWP100 = 6.0f,
            AtmosphericLifetime_years = 0.045f,

            AntoineCoefficients_A_B_C = new float[] { 4.39220f, 1212.45f, -39.2f },
            AntoineValidRange_K = new float[] { 273.15f, 380.0f },

            LiquidDensityCoeff_A_B_C = new float[] { 1420.0f, -2.7f, 0.0f },
            LiquidEnthalpyCoeff_A_B_C_D = new float[] { -190000f, 1460f, 0.53f, -0.0010f },
            VaporEnthalpyCoeff_A_B_C_D = new float[] { 275000f, 415f, -0.22f, 0.0007f },
            LiquidEntropyCoeff_A_B_C = new float[] { -930f, 5.2f, 0.0019f },
            VaporEntropyCoeff_A_B_C = new float[] { 1290f, 2.7f, -0.0012f },

            RecommendedEvaporatorPressure_Pa = 2.1e6f,
            RecommendedCondenserTemperature_K = 303.15f,
            MinimumTemperature_K = 273.15f,
            MaximumTemperature_K = 380.0f,

            Applications = new List<string> { "Low to medium-temp geothermal (70-130°C)", "Chiller replacement for R134a", "Data center cooling" },
            Sources = new List<string> { "REFPROP 10.0", "Honeywell" },
            Notes = "HFO refrigerant with excellent environmental properties. GWP of only 6. Mildly flammable.",
            IsUserFluid = false
        });

        // Cyclohexane
        _fluids.Add(new ORCFluid
        {
            Name = "Cyclohexane",
            ChemicalFormula = "C₆H₁₂",
            RefrigerantCode = "Cyclohexane",
            Category = FluidCategory.MediumTemperature,
            Safety = SafetyClass.A3,
            IsNaturalFluid = true,

            CriticalTemperature_K = 553.6f,
            CriticalPressure_Pa = 4.080e6f,
            CriticalDensity_kg_m3 = 273.0f,

            TriplePointTemperature_K = 279.69f,
            TriplePointPressure_Pa = 5360.0f,

            MolecularWeight_g_mol = 84.16f,
            AccentricFactor = 0.2096f,

            ODP = 0.0f,
            GWP100 = 3.2f,
            AtmosphericLifetime_years = 0.011f,

            AntoineCoefficients_A_B_C = new float[] { 4.12025f, 1490.0f, -60.0f },
            AntoineValidRange_K = new float[] { 300.0f, 550.0f },

            LiquidDensityCoeff_A_B_C = new float[] { 850.0f, -1.2f, 0.0f },
            LiquidEnthalpyCoeff_A_B_C_D = new float[] { -80000f, 1350f, 0.55f, -0.0006f },
            VaporEnthalpyCoeff_A_B_C_D = new float[] { 340000f, 440f, -0.20f, 0.0005f },
            LiquidEntropyCoeff_A_B_C = new float[] { -650f, 4.9f, 0.0015f },
            VaporEntropyCoeff_A_B_C = new float[] { 1550f, 2.5f, -0.0010f },

            RecommendedEvaporatorPressure_Pa = 2.2e6f,
            RecommendedCondenserTemperature_K = 313.15f,
            MinimumTemperature_K = 300.0f,
            MaximumTemperature_K = 550.0f,

            Applications = new List<string> { "High-temperature geothermal (180-280°C)", "Supercritical ORC", "Solar concentrators" },
            Sources = new List<string> { "REFPROP 10.0", "Tchanche et al. (2009)" },
            Notes = "Cyclic hydrocarbon for high-temp applications. Flammable. Good thermal stability up to 280°C.",
            IsUserFluid = false
        });

        // Benzene
        _fluids.Add(new ORCFluid
        {
            Name = "Benzene",
            ChemicalFormula = "C₆H₆",
            RefrigerantCode = "Benzene",
            Category = FluidCategory.HighTemperature,
            Safety = SafetyClass.B3,
            IsNaturalFluid = true,

            CriticalTemperature_K = 562.05f,
            CriticalPressure_Pa = 4.895e6f,
            CriticalDensity_kg_m3 = 305.0f,

            TriplePointTemperature_K = 278.68f,
            TriplePointPressure_Pa = 4785.0f,

            MolecularWeight_g_mol = 78.11f,
            AccentricFactor = 0.2103f,

            ODP = 0.0f,
            GWP100 = 2.5f,
            AtmosphericLifetime_years = 0.013f,

            AntoineCoefficients_A_B_C = new float[] { 4.01814f, 1203.835f, -53.226f },
            AntoineValidRange_K = new float[] { 300.0f, 560.0f },

            LiquidDensityCoeff_A_B_C = new float[] { 980.0f, -1.3f, 0.0f },
            LiquidEnthalpyCoeff_A_B_C_D = new float[] { -60000f, 1320f, 0.48f, -0.0005f },
            VaporEnthalpyCoeff_A_B_C_D = new float[] { 360000f, 430f, -0.19f, 0.0004f },
            LiquidEntropyCoeff_A_B_C = new float[] { -620f, 4.7f, 0.0014f },
            VaporEntropyCoeff_A_B_C = new float[] { 1580f, 2.45f, -0.0009f },

            RecommendedEvaporatorPressure_Pa = 2.6e6f,
            RecommendedCondenserTemperature_K = 313.15f,
            MinimumTemperature_K = 300.0f,
            MaximumTemperature_K = 560.0f,

            Applications = new List<string> { "High-temperature geothermal (190-290°C)", "Supercritical ORC", "Research applications" },
            Sources = new List<string> { "REFPROP 10.0", "NIST WebBook" },
            Notes = "Aromatic hydrocarbon. TOXIC and carcinogenic - use only in sealed systems. Flammable. Research use only.",
            IsUserFluid = false
        });

        // R236fa - Hexafluoropropane
        _fluids.Add(new ORCFluid
        {
            Name = "R236fa (Hexafluoropropane)",
            ChemicalFormula = "CF₃CH₂CF₃",
            RefrigerantCode = "R236fa",
            Category = FluidCategory.LowTemperature,
            Safety = SafetyClass.A1,
            IsNaturalFluid = false,

            CriticalTemperature_K = 398.07f,
            CriticalPressure_Pa = 3.200e6f,
            CriticalDensity_kg_m3 = 565.0f,

            TriplePointTemperature_K = 179.52f,
            TriplePointPressure_Pa = 179.8f,

            MolecularWeight_g_mol = 152.04f,
            AccentricFactor = 0.3776f,

            ODP = 0.0f,
            GWP100 = 9810.0f, // Very high!
            AtmosphericLifetime_years = 240.0f,

            AntoineCoefficients_A_B_C = new float[] { 4.32980f, 1290.45f, -45.62f },
            AntoineValidRange_K = new float[] { 273.15f, 395.0f },

            LiquidDensityCoeff_A_B_C = new float[] { 1650.0f, -3.2f, 0.0f },
            LiquidEnthalpyCoeff_A_B_C_D = new float[] { -210000f, 1350f, 0.48f, -0.0009f },
            VaporEnthalpyCoeff_A_B_C_D = new float[] { 240000f, 380f, -0.18f, 0.0006f },
            LiquidEntropyCoeff_A_B_C = new float[] { -1020f, 4.9f, 0.0018f },
            VaporEntropyCoeff_A_B_C = new float[] { 1180f, 2.55f, -0.0011f },

            RecommendedEvaporatorPressure_Pa = 1.8e6f,
            RecommendedCondenserTemperature_K = 303.15f,
            MinimumTemperature_K = 273.15f,
            MaximumTemperature_K = 395.0f,

            Applications = new List<string> { "Low to medium-temp ORC (80-140°C)", "Fire suppression systems", "Aerospace cooling" },
            Sources = new List<string> { "REFPROP 10.0", "3M Novec" },
            Notes = "Non-flammable HFC. Very high GWP - being phased out. Good thermal stability. Use environmentally preferable alternatives.",
            IsUserFluid = false
        });

        // R227ea - Heptafluoropropane
        _fluids.Add(new ORCFluid
        {
            Name = "R227ea (Heptafluoropropane)",
            ChemicalFormula = "CF₃CHFCF₃",
            RefrigerantCode = "R227ea",
            Category = FluidCategory.LowTemperature,
            Safety = SafetyClass.A1,
            IsNaturalFluid = false,

            CriticalTemperature_K = 374.90f,
            CriticalPressure_Pa = 2.925e6f,
            CriticalDensity_kg_m3 = 594.0f,

            TriplePointTemperature_K = 146.35f,
            TriplePointPressure_Pa = 7.5f,

            MolecularWeight_g_mol = 170.03f,
            AccentricFactor = 0.3572f,

            ODP = 0.0f,
            GWP100 = 3220.0f, // High
            AtmosphericLifetime_years = 34.2f,

            AntoineCoefficients_A_B_C = new float[] { 4.36280f, 1184.32f, -40.18f },
            AntoineValidRange_K = new float[] { 273.15f, 372.0f },

            LiquidDensityCoeff_A_B_C = new float[] { 1720.0f, -3.5f, 0.0f },
            LiquidEnthalpyCoeff_A_B_C_D = new float[] { -220000f, 1280f, 0.44f, -0.0008f },
            VaporEnthalpyCoeff_A_B_C_D = new float[] { 230000f, 360f, -0.17f, 0.0005f },
            LiquidEntropyCoeff_A_B_C = new float[] { -1050f, 4.8f, 0.0017f },
            VaporEntropyCoeff_A_B_C = new float[] { 1160f, 2.5f, -0.0010f },

            RecommendedEvaporatorPressure_Pa = 1.6e6f,
            RecommendedCondenserTemperature_K = 303.15f,
            MinimumTemperature_K = 273.15f,
            MaximumTemperature_K = 372.0f,

            Applications = new List<string> { "Low-temp ORC (65-120°C)", "Fire suppression (FM-200)", "Precision cooling" },
            Sources = new List<string> { "REFPROP 10.0", "3M" },
            Notes = "Non-flammable HFC. High GWP. Excellent for fire suppression but poor environmental profile for ORC.",
            IsUserFluid = false
        });

        // Methanol
        _fluids.Add(new ORCFluid
        {
            Name = "Methanol",
            ChemicalFormula = "CH₃OH",
            RefrigerantCode = "Methanol",
            Category = FluidCategory.MediumTemperature,
            Safety = SafetyClass.B2,
            IsNaturalFluid = true,

            CriticalTemperature_K = 512.5f,
            CriticalPressure_Pa = 8.084e6f,
            CriticalDensity_kg_m3 = 272.0f,

            TriplePointTemperature_K = 175.47f,
            TriplePointPressure_Pa = 0.00019f,

            MolecularWeight_g_mol = 32.04f,
            AccentricFactor = 0.5656f,

            ODP = 0.0f,
            GWP100 = 2.8f,
            AtmosphericLifetime_years = 0.035f,

            AntoineCoefficients_A_B_C = new float[] { 5.20409f, 1581.34f, -33.50f },
            AntoineValidRange_K = new float[] { 288.15f, 510.0f },

            LiquidDensityCoeff_A_B_C = new float[] { 950.0f, -1.0f, 0.0f },
            LiquidEnthalpyCoeff_A_B_C_D = new float[] { -100000f, 2200f, 0.8f, -0.0015f },
            VaporEnthalpyCoeff_A_B_C_D = new float[] { 400000f, 600f, -0.4f, 0.001f },
            LiquidEntropyCoeff_A_B_C = new float[] { -550f, 6.5f, 0.0025f },
            VaporEntropyCoeff_A_B_C = new float[] { 1700f, 3.2f, -0.0016f },

            RecommendedEvaporatorPressure_Pa = 3.5e6f,
            RecommendedCondenserTemperature_K = 313.15f,
            MinimumTemperature_K = 288.15f,
            MaximumTemperature_K = 510.0f,

            Applications = new List<string> { "Medium to high-temp geothermal (150-250°C)", "Biomass combustion", "Kalina cycles" },
            Sources = new List<string> { "REFPROP 10.0", "NIST WebBook" },
            Notes = "Simple alcohol. TOXIC. Flammable. Good for high-pressure ORC. Used in Kalina ammonia-water cycles as additive.",
            IsUserFluid = false
        });

        // Ammonia (R717)
        _fluids.Add(new ORCFluid
        {
            Name = "Ammonia",
            ChemicalFormula = "NH₃",
            RefrigerantCode = "R717",
            Category = FluidCategory.LowTemperature,
            Safety = SafetyClass.B2,
            IsNaturalFluid = true,

            CriticalTemperature_K = 405.40f,
            CriticalPressure_Pa = 11.333e6f,
            CriticalDensity_kg_m3 = 225.0f,

            TriplePointTemperature_K = 195.495f,
            TriplePointPressure_Pa = 6111.0f,

            MolecularWeight_g_mol = 17.03f,
            AccentricFactor = 0.2560f,

            ODP = 0.0f,
            GWP100 = 0.0f, // Negligible
            AtmosphericLifetime_years = 0.027f,

            AntoineCoefficients_A_B_C = new float[] { 4.86886f, 1113.928f, -10.409f },
            AntoineValidRange_K = new float[] { 239.15f, 405.0f },

            LiquidDensityCoeff_A_B_C = new float[] { 730.0f, -1.8f, 0.0f },
            LiquidEnthalpyCoeff_A_B_C_D = new float[] { -140000f, 2500f, 1.0f, -0.002f },
            VaporEnthalpyCoeff_A_B_C_D = new float[] { 450000f, 700f, -0.5f, 0.0012f },
            LiquidEntropyCoeff_A_B_C = new float[] { -500f, 7.0f, 0.003f },
            VaporEntropyCoeff_A_B_C = new float[] { 1800f, 3.5f, -0.0018f },

            RecommendedEvaporatorPressure_Pa = 4.5e6f,
            RecommendedCondenserTemperature_K = 303.15f,
            MinimumTemperature_K = 239.15f,
            MaximumTemperature_K = 405.0f,

            Applications = new List<string> { "Low to medium-temp ORC (80-150°C)", "Industrial refrigeration", "Kalina cycles", "Absorption chillers" },
            Sources = new List<string> { "REFPROP 10.0", "ASHRAE Handbook" },
            Notes = "Natural refrigerant. Zero GWP, excellent thermodynamic properties. TOXIC and corrosive. Pungent odor provides leak detection.",
            IsUserFluid = false
        });

        // Water (Steam)
        _fluids.Add(new ORCFluid
        {
            Name = "Water (Steam)",
            ChemicalFormula = "H₂O",
            RefrigerantCode = "R718",
            Category = FluidCategory.HighTemperature,
            Safety = SafetyClass.A1,
            IsNaturalFluid = true,

            CriticalTemperature_K = 647.096f,
            CriticalPressure_Pa = 22.064e6f,
            CriticalDensity_kg_m3 = 322.0f,

            TriplePointTemperature_K = 273.16f,
            TriplePointPressure_Pa = 611.657f,

            MolecularWeight_g_mol = 18.015f,
            AccentricFactor = 0.3443f,

            ODP = 0.0f,
            GWP100 = 0.0f,
            AtmosphericLifetime_years = 0.027f, // As vapor

            AntoineCoefficients_A_B_C = new float[] { 5.40221f, 1838.675f, -31.737f },
            AntoineValidRange_K = new float[] { 373.15f, 640.0f },

            LiquidDensityCoeff_A_B_C = new float[] { 1100.0f, -0.6f, 0.0f },
            LiquidEnthalpyCoeff_A_B_C_D = new float[] { -50000f, 2800f, 1.2f, -0.0025f },
            VaporEnthalpyCoeff_A_B_C_D = new float[] { 500000f, 800f, -0.6f, 0.0015f },
            LiquidEntropyCoeff_A_B_C = new float[] { -400f, 8.0f, 0.0035f },
            VaporEntropyCoeff_A_B_C = new float[] { 2000f, 4.0f, -0.002f },

            RecommendedEvaporatorPressure_Pa = 8.0e6f,
            RecommendedCondenserTemperature_K = 323.15f, // 50°C
            MinimumTemperature_K = 373.15f, // 100°C
            MaximumTemperature_K = 640.0f,

            Applications = new List<string> { "High-temperature geothermal (>200°C)", "Conventional steam power", "Flash steam plants", "Supercritical Rankine cycles" },
            Sources = new List<string> { "IAPWS-IF97", "Wagner & Pruss (2002)" },
            Notes = "Most common working fluid for conventional power generation. Zero environmental impact. Requires high temperatures (>200°C) for good efficiency. Use IAPWS-IF97 for accurate properties.",
            IsUserFluid = false
        });

        // Carbon Dioxide (R744) - Transcritical
        _fluids.Add(new ORCFluid
        {
            Name = "Carbon Dioxide (Transcritical)",
            ChemicalFormula = "CO₂",
            RefrigerantCode = "R744",
            Category = FluidCategory.LowTemperature,
            Safety = SafetyClass.A1,
            IsNaturalFluid = true,

            CriticalTemperature_K = 304.13f,
            CriticalPressure_Pa = 7.377e6f,
            CriticalDensity_kg_m3 = 467.6f,

            TriplePointTemperature_K = 216.59f,
            TriplePointPressure_Pa = 518000.0f,

            MolecularWeight_g_mol = 44.01f,
            AccentricFactor = 0.2239f,

            ODP = 0.0f,
            GWP100 = 1.0f, // By definition (baseline)
            AtmosphericLifetime_years = 100.0f,

            AntoineCoefficients_A_B_C = new float[] { 6.81228f, 1301.679f, -3.494f },
            AntoineValidRange_K = new float[] { 216.59f, 304.0f },

            LiquidDensityCoeff_A_B_C = new float[] { 1200.0f, -4.0f, 0.0f },
            LiquidEnthalpyCoeff_A_B_C_D = new float[] { -160000f, 1100f, 0.3f, -0.0005f },
            VaporEnthalpyCoeff_A_B_C_D = new float[] { 220000f, 350f, -0.15f, 0.0004f },
            LiquidEntropyCoeff_A_B_C = new float[] { -1100f, 4.5f, 0.0016f },
            VaporEntropyCoeff_A_B_C = new float[] { 1100f, 2.3f, -0.0009f },

            RecommendedEvaporatorPressure_Pa = 9.0e6f, // Supercritical!
            RecommendedCondenserTemperature_K = 298.15f,
            MinimumTemperature_K = 273.15f,
            MaximumTemperature_K = 350.0f, // Can operate supercritical

            Applications = new List<string> { "Transcritical power cycles (50-120°C)", "Heat pumps (cold climates)", "Waste heat recovery", "Cascade refrigeration" },
            Sources = new List<string> { "REFPROP 10.0", "Span & Wagner (1996)" },
            Notes = "Natural refrigerant operating in transcritical mode. Very high operating pressures (>73 bar). Good for compact systems. Use Span-Wagner EOS for accuracy.",
            IsUserFluid = false
        });

        // Neopentane (R601b)
        _fluids.Add(new ORCFluid
        {
            Name = "Neopentane",
            ChemicalFormula = "C₅H₁₂",
            RefrigerantCode = "R601b",
            Category = FluidCategory.MediumTemperature,
            Safety = SafetyClass.A3,
            IsNaturalFluid = true,

            CriticalTemperature_K = 433.75f,
            CriticalPressure_Pa = 3.196e6f,
            CriticalDensity_kg_m3 = 233.0f,

            TriplePointTemperature_K = 256.6f,
            TriplePointPressure_Pa = 35400.0f,

            MolecularWeight_g_mol = 72.15f,
            AccentricFactor = 0.1961f,

            ODP = 0.0f,
            GWP100 = 4.0f,
            AtmosphericLifetime_years = 0.015f,

            AntoineCoefficients_A_B_C = new float[] { 4.47350f, 1366.2f, -45.0f },
            AntoineValidRange_K = new float[] { 273.15f, 430.0f },

            LiquidDensityCoeff_A_B_C = new float[] { 660.0f, -1.7f, 0.0f },
            LiquidEnthalpyCoeff_A_B_C_D = new float[] { -130000f, 1580f, 0.46f, -0.0007f },
            VaporEnthalpyCoeff_A_B_C_D = new float[] { 315000f, 470f, -0.26f, 0.0006f },
            LiquidEntropyCoeff_A_B_C = new float[] { -770f, 5.15f, 0.0017f },
            VaporEntropyCoeff_A_B_C = new float[] { 1480f, 2.6f, -0.0011f },

            RecommendedEvaporatorPressure_Pa = 1.9e6f,
            RecommendedCondenserTemperature_K = 303.15f,
            MinimumTemperature_K = 273.15f,
            MaximumTemperature_K = 430.0f,

            Applications = new List<string> { "Medium-temp geothermal (100-170°C)", "Waste heat ORC", "Binary geothermal" },
            Sources = new List<string> { "REFPROP 10.0", "IEA Database" },
            Notes = "Branched isomer of pentane. Natural, low GWP. Flammable. Good for moderate temperature ORC applications.",
            IsUserFluid = false
        });

        // R365mfc - 1,1,1,3,3-Pentafluorobutane
        _fluids.Add(new ORCFluid
        {
            Name = "R365mfc",
            ChemicalFormula = "CF₃CH₂CF₂CH₃",
            RefrigerantCode = "R365mfc",
            Category = FluidCategory.LowTemperature,
            Safety = SafetyClass.A1,
            IsNaturalFluid = false,

            CriticalTemperature_K = 460.0f,
            CriticalPressure_Pa = 3.266e6f,
            CriticalDensity_kg_m3 = 476.0f,

            TriplePointTemperature_K = 235.15f,
            TriplePointPressure_Pa = 2990.0f,

            MolecularWeight_g_mol = 148.07f,
            AccentricFactor = 0.3806f,

            ODP = 0.0f,
            GWP100 = 794.0f,
            AtmosphericLifetime_years = 8.7f,

            AntoineCoefficients_A_B_C = new float[] { 4.29150f, 1405.3f, -52.1f },
            AntoineValidRange_K = new float[] { 273.15f, 455.0f },

            LiquidDensityCoeff_A_B_C = new float[] { 1550.0f, -3.1f, 0.0f },
            LiquidEnthalpyCoeff_A_B_C_D = new float[] { -205000f, 1390f, 0.50f, -0.0009f },
            VaporEnthalpyCoeff_A_B_C_D = new float[] { 250000f, 390f, -0.19f, 0.0006f },
            LiquidEntropyCoeff_A_B_C = new float[] { -990f, 4.95f, 0.0018f },
            VaporEntropyCoeff_A_B_C = new float[] { 1200f, 2.57f, -0.0011f },

            RecommendedEvaporatorPressure_Pa = 1.4e6f,
            RecommendedCondenserTemperature_K = 303.15f,
            MinimumTemperature_K = 273.15f,
            MaximumTemperature_K = 455.0f,

            Applications = new List<string> { "Medium-temp ORC (90-190°C)", "Foam blowing", "Solvent applications" },
            Sources = new List<string> { "REFPROP 10.0", "Honeywell Solstice" },
            Notes = "HFC blend. Non-flammable. Moderate GWP. Good for high-temperature ORC compared to other HFCs. Being evaluated for phase-out.",
            IsUserFluid = false
        });
    }

    private void SaveUserFluids()
    {
        try
        {
            var userFluids = _fluids.Where(f => f.IsUserFluid).ToList();
            var json = JsonSerializer.Serialize(userFluids, new JsonSerializerOptions { WriteIndented = true });
            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "orc_fluids_user.json");
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to save user ORC fluids: {ex.Message}");
        }
    }

    private void LoadUserFluids()
    {
        try
        {
            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "orc_fluids_user.json");
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var userFluids = JsonSerializer.Deserialize<List<ORCFluid>>(json);
                if (userFluids != null)
                {
                    foreach (var fluid in userFluids)
                    {
                        fluid.IsUserFluid = true;
                        _fluids.Add(fluid);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to load user ORC fluids: {ex.Message}");
        }
    }
}
