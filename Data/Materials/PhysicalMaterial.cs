// GeoscientistToolkit/Data/Materials/PhysicalMaterial.cs

using System.Text.Json.Serialization;

namespace GeoscientistToolkit.Data.Materials;

public enum PhaseType
{
    Solid,
    Liquid,
    Gas
}

public sealed class PhysicalMaterial
{
    public string Name { get; set; } = "Unnamed";
    public PhaseType Phase { get; set; } = PhaseType.Solid;

    // --- Core requested properties (SI units) ---
    public double? Viscosity_Pa_s { get; set; } // dynamic viscosity (Pa·s); fluids only
    public double? MohsHardness { get; set; } // hardness (Mohs) where meaningful
    public double? Density_kg_m3 { get; set; } // typical density

    // --- Mechanical Properties for Deformation & Triaxial Sims ---
    public double? YoungModulus_GPa { get; set; } // E (GPa)
    public double? PoissonRatio { get; set; } // ν (dimensionless)
    public double? FrictionAngle_deg { get; set; } // ° (a.k.a. internal friction / fracture angle)
    public double? CompressiveStrength_MPa { get; set; } // Uniaxial Compressive Strength (UCS)
    public double? TensileStrength_MPa { get; set; } // Direct or Brazilian tensile strength
    public double? YieldStrength_MPa { get; set; } // Primarily for ductile materials like metals

    // --- Thermal Properties for Heat Exchange Sims ---
    public double? ThermalConductivity_W_mK { get; set; } // W/m·K
    public double? SpecificHeatCapacity_J_kgK { get; set; } // J/kg·K
    public double? ThermalDiffusivity_m2_s { get; set; } // m²/s

    // Wettability & porosity
    public double? TypicalWettability_contactAngle_deg { get; set; } // contact angle (water on solid), if known
    public double? TypicalPorosity_fraction { get; set; } // 0..1 (fraction)

    // --- Acoustic Properties ---
    public double? Vs_m_s { get; set; } // shear velocity
    public double? Vp_m_s { get; set; } // P-wave velocity
    public double? VpVsRatio { get; set; } // optional convenience
    public double? AcousticImpedance_MRayl { get; set; } // Z (MRayl = 10^6 kg/m²s)

    // --- Electrical & Magnetic Properties ---
    public double? ElectricalResistivity_Ohm_m { get; set; } // Ω·m
    public double? MagneticSusceptibility_SI { get; set; } // Dimensionless (SI)

    // Extra user-defined parameters (you can add any numeric fields)
    public Dictionary<string, double> Extra { get; set; } = new();

    // Optional textual notes & per-material sources
    public string Notes { get; set; } = "";
    public List<string> Sources { get; set; } = new();

    [JsonIgnore] public bool IsUserMaterial { get; set; } = true; // not serialized; internal UI hint
}