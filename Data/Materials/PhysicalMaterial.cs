// GeoscientistToolkit/Data/Materials/PhysicalMaterial.cs
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GeoscientistToolkit.Data.Materials
{
    public enum PhaseType { Solid, Liquid, Gas }

    public sealed class PhysicalMaterial
    {
        public string Name { get; set; } = "Unnamed";
        public PhaseType Phase { get; set; } = PhaseType.Solid;

        // --- Core requested properties (SI units) ---
        public double? Viscosity_Pa_s { get; set; }                 // dynamic viscosity (Pa·s); fluids only
        public double? MohsHardness { get; set; }                   // hardness (Mohs) where meaningful
        public double? Density_kg_m3 { get; set; }                  // typical density
        public double? ThermalConductivity_W_mK { get; set; }       // W/m·K
        public double? PoissonRatio { get; set; }                   // ν (dimensionless)
        public double? FrictionAngle_deg { get; set; }              // ° (a.k.a. internal friction / fracture angle)
        public double? YoungModulus_GPa { get; set; }               // GPa
        public double? BreakingPressure_MPa { get; set; }           // MPa (compressive / collapse pressure as applicable)

        // Wettability & porosity
        public double? TypicalWettability_contactAngle_deg { get; set; } // contact angle (water on solid), if known
        public double? TypicalPorosity_fraction { get; set; }            // 0..1 (fraction)

        // Acoustics
        public double? Vs_m_s { get; set; }                        // shear velocity
        public double? Vp_m_s { get; set; }                        // P-wave velocity
        public double? VpVsRatio { get; set; }                     // optional convenience

        // Extra user-defined parameters (you can add any numeric fields)
        public Dictionary<string, double> Extra { get; set; } = new();

        // Optional textual notes & per-material sources
        public string Notes { get; set; } = "";
        public List<string> Sources { get; set; } = new();

        [JsonIgnore] public bool IsUserMaterial { get; set; } = true; // not serialized; internal UI hint
    }
}
