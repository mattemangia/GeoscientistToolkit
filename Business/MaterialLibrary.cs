// GeoscientistToolkit/Business/MaterialLibrary.cs
//
// Singleton service that stores materials, handles I/O, and seeds realistic values.
//
// SOURCES (general; see per-material comments in SeedDefaults()):
// - Engineering Toolbox (common material properties: ρ, k, E, ν, η, c_s)
// - CRC Handbook / NIST data for fluids & gases (η, k, ρ, speed of sound)
// - Rock physics & geotechnical handbooks for typical rock E, ν, φ, Vp/Vs, friction angles
//   (e.g., Schön 2015 "Physical Properties of Rocks"; Carmichael 1982; Jaeger et al. "Rock Mechanics")
// - Online databases and publications for specific material properties.
// - Typical values consolidated to SI; ranges exist—these are representative midpoints.
//   Always validate for your specimen, T, P, saturation, fabric, anisotropy.
//

using System.Text.Json;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business;

public sealed class MaterialLibrary
{
    private static readonly Lazy<MaterialLibrary> _lazy = new(() => new MaterialLibrary());
    private readonly List<PhysicalMaterial> _materials = new();

    private MaterialLibrary()
    {
        // Seed with defaults so the software "can pick stuff if needed".
        SeedDefaults();
    }

    public static MaterialLibrary Instance => _lazy.Value;

    public IReadOnlyList<PhysicalMaterial> Materials => _materials;

    // Default path in project folder
    public string LibraryFilePath { get; private set; } = "Materials.library.json";

    public void SetLibraryFilePath(string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            LibraryFilePath = path;
    }

    public void Clear()
    {
        _materials.Clear();
    }

    public PhysicalMaterial? Find(string name)
    {
        return _materials.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public void AddOrUpdate(PhysicalMaterial mat)
    {
        if (mat == null || string.IsNullOrWhiteSpace(mat.Name)) return;
        var existing = Find(mat.Name);
        if (existing == null)
        {
            _materials.Add(mat);
        }
        else
        {
            // Update existing in-place
            var idx = _materials.IndexOf(existing);
            _materials[idx] = mat;
        }
    }

    public bool Remove(string name)
    {
        var m = Find(name);
        if (m == null) return false;
        _materials.Remove(m);
        return true;
    }

    public bool Load(string? path = null)
    {
        try
        {
            var p = path ?? LibraryFilePath;
            if (!File.Exists(p))
            {
                Logger.LogWarning($"[MaterialLibrary] File not found: {p}");
                return false;
            }

            var json = File.ReadAllText(p);
            var loaded = JsonSerializer.Deserialize<List<PhysicalMaterial>>(json, JsonOptions());
            if (loaded == null) return false;

            _materials.Clear();
            _materials.AddRange(loaded);

            // Mark loaded ones as "user" (so editor shows e.g. delete enabled)
            foreach (var m in _materials) m.IsUserMaterial = true;

            Logger.Log($"[MaterialLibrary] Loaded {loaded.Count} materials from {p}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"[MaterialLibrary] Load error: {ex.Message}");
            return false;
        }
    }

    public bool Save(string? path = null)
    {
        try
        {
            var p = path ?? LibraryFilePath;
            var json = JsonSerializer.Serialize(_materials, JsonOptions());
            File.WriteAllText(p, json);
            Logger.Log($"[MaterialLibrary] Saved {Materials.Count} materials to {p}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"[MaterialLibrary] Save error: {ex.Message}");
            return false;
        }
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
    }

    // ------------------------------------------------------------
    // Seed defaults: realistic, SI-converted, representative values
    // ------------------------------------------------------------
    private void SeedDefaults()
    {
        _materials.Clear();

        // --- WATER ------------------------------------------------
        _materials.Add(new PhysicalMaterial
        {
            Name = "Water (20°C)",
            Phase = PhaseType.Liquid,
            Viscosity_Pa_s = 1.0e-3, // Pa·s @ ~20°C
            Density_kg_m3 = 998, // kg/m3 @ ~20°C
            ThermalConductivity_W_mK = 0.6, // W/mK @ ~25°C
            SpecificHeatCapacity_J_kgK = 4182,
            PoissonRatio = 0.5, // incompressible fluid limit (for reference)
            TypicalWettability_contactAngle_deg = 0, // water on clean hydrophilic solids
            Vp_m_s = 1480, // speed of sound @20°C
            AcousticImpedance_MRayl = 1.48,
            ElectricalResistivity_Ohm_m = 1.8e5, // for pure water
            Notes = "Liquid water at ambient conditions.",
            Sources = new List<string>
            {
                "CRC/NIST water properties; common engineering tables (η≈1e-3 Pa·s at 20°C, ρ≈998 kg/m³, k≈0.6 W/mK, c≈1480 m/s)."
            },
            IsUserMaterial = false
        });

        // --- GASES -----------------------------------------------
        _materials.Add(new PhysicalMaterial
        {
            Name = "Nitrogen (N2, 20°C, 1 atm)",
            Phase = PhaseType.Gas,
            Density_kg_m3 = 1.2,
            Viscosity_Pa_s = 1.8e-5,
            ThermalConductivity_W_mK = 0.026,
            SpecificHeatCapacity_J_kgK = 1040,
            Vp_m_s = 350, Vs_m_s = null, VpVsRatio = null,
            Notes = "Dry nitrogen at ambient conditions.",
            Sources = new List<string> { "NIST/Engineering Toolbox typical gas properties at ~20°C." },
            IsUserMaterial = false
        });
        _materials.Add(new PhysicalMaterial
        {
            Name = "Oxygen (O2, 20°C, 1 atm)",
            Phase = PhaseType.Gas,
            Density_kg_m3 = 1.33,
            Viscosity_Pa_s = 2.0e-5,
            ThermalConductivity_W_mK = 0.026,
            SpecificHeatCapacity_J_kgK = 919,
            Vp_m_s = 330,
            Notes = "Dry oxygen at ambient conditions.",
            Sources = new List<string> { "NIST/Engineering Toolbox typical gas properties at ~20°C." },
            IsUserMaterial = false
        });
        _materials.Add(new PhysicalMaterial
        {
            Name = "Carbon Dioxide (CO2, 20°C, 1 atm)",
            Phase = PhaseType.Gas,
            Density_kg_m3 = 1.8,
            Viscosity_Pa_s = 1.5e-5,
            ThermalConductivity_W_mK = 0.017,
            SpecificHeatCapacity_J_kgK = 844,
            Vp_m_s = 270,
            Notes = "Dry CO₂ at ambient conditions.",
            Sources = new List<string> { "NIST data for CO₂; Engineering Toolbox." },
            IsUserMaterial = false
        });

        // --- SEDIMENTARY ROCKS -----------------------------------
        _materials.Add(new PhysicalMaterial
        {
            Name = "Sandstone (quartz-rich, dense)",
            Phase = PhaseType.Solid,
            MohsHardness = 7,
            Density_kg_m3 = 2400,
            YoungModulus_GPa = 38,
            PoissonRatio = 0.25,
            FrictionAngle_deg = 32,
            CompressiveStrength_MPa = 120,
            TensileStrength_MPa = 4.0,
            ThermalConductivity_W_mK = 5.0,
            SpecificHeatCapacity_J_kgK = 710,
            TypicalPorosity_fraction = 0.10,
            Vp_m_s = 4000, Vs_m_s = 2200, VpVsRatio = 1.82,
            AcousticImpedance_MRayl = 9.6,
            ElectricalResistivity_Ohm_m = 1e3, // Highly variable
            Notes = "Dry, compact sandstone; properties vary strongly with φ, cement, saturation.",
            Sources = new List<string>
            {
                "Rock property compilations: Schön, Jaeger, Carmichael; Eng. tables.",
                "MakeItFrom.com for tensile strength [5]"
            },
            IsUserMaterial = false
        });
        _materials.Add(new PhysicalMaterial
        {
            Name = "Limestone (calcite, dense)",
            Phase = PhaseType.Solid,
            MohsHardness = 3,
            Density_kg_m3 = 2600,
            YoungModulus_GPa = 77,
            PoissonRatio = 0.25,
            FrictionAngle_deg = 36,
            CompressiveStrength_MPa = 150,
            TensileStrength_MPa = 5,
            ThermalConductivity_W_mK = 2.1,
            SpecificHeatCapacity_J_kgK = 810,
            TypicalPorosity_fraction = 0.03,
            Vp_m_s = 5800, Vs_m_s = 3200, VpVsRatio = 1.81,
            AcousticImpedance_MRayl = 15.08,
            Notes = "Dense limestone; micrite/porosity lowers E & Vp.",
            Sources = new List<string>
                { "Rock property compilations; Eng. tables.", "Johnson and Degraff, 1988 for tensile strength [47]" },
            IsUserMaterial = false
        });
        _materials.Add(new PhysicalMaterial
        {
            Name = "Shale (clay-rich)",
            Phase = PhaseType.Solid,
            MohsHardness = 2.5,
            Density_kg_m3 = 2500,
            YoungModulus_GPa = 11,
            PoissonRatio = 0.29,
            FrictionAngle_deg = 24,
            CompressiveStrength_MPa = 100,
            TensileStrength_MPa = 2,
            ThermalConductivity_W_mK = 1.5,
            SpecificHeatCapacity_J_kgK = 710,
            TypicalPorosity_fraction = 0.20,
            Vp_m_s = 3000, Vs_m_s = 1500, VpVsRatio = 2.0,
            AcousticImpedance_MRayl = 7.5,
            ElectricalResistivity_Ohm_m = 1e2, // Lower due to clay/water
            Notes = "Strong anisotropy; properties depend on clay, water content.",
            Sources = new List<string> { "Rock property compilations; Eng. tables." },
            IsUserMaterial = false
        });
        _materials.Add(new PhysicalMaterial
        {
            Name = "Coal (Bituminous)",
            Phase = PhaseType.Solid,
            MohsHardness = 1.5,
            Density_kg_m3 = 1350,
            YoungModulus_GPa = 4,
            PoissonRatio = 0.35,
            CompressiveStrength_MPa = 25,
            ThermalConductivity_W_mK = 0.25,
            SpecificHeatCapacity_J_kgK = 1320,
            Vp_m_s = 2300,
            ElectricalResistivity_Ohm_m = 1e4,
            Notes = "Properties are highly variable depending on rank and composition.",
            Sources = new List<string> { "Geology Science [2]", "Compare Rocks [19]", "Britannica [36]" },
            IsUserMaterial = false
        });

        // --- IGNEOUS ROCKS ---------------------------------------
        _materials.Add(new PhysicalMaterial
        {
            Name = "Granite (fresh, dense)",
            Phase = PhaseType.Solid,
            MohsHardness = 6.5,
            Density_kg_m3 = 2650,
            YoungModulus_GPa = 70,
            PoissonRatio = 0.22,
            FrictionAngle_deg = 32,
            CompressiveStrength_MPa = 200,
            TensileStrength_MPa = 10,
            ThermalConductivity_W_mK = 2.7,
            SpecificHeatCapacity_J_kgK = 790,
            TypicalPorosity_fraction = 0.01,
            Vp_m_s = 6000, Vs_m_s = 3300, VpVsRatio = 1.82,
            AcousticImpedance_MRayl = 15.9,
            ElectricalResistivity_Ohm_m = 1e6, // Very high for dry granite
            MagneticSusceptibility_SI = 3e-3, // Can vary significantly
            Notes = "Dry, intact granite.",
            Sources = new List<string>
                { "Rock property compilations; Eng. tables.", "Geological Society of London for Mag. Sus." },
            IsUserMaterial = false
        });
        _materials.Add(new PhysicalMaterial
        {
            Name = "Basalt (dense)",
            Phase = PhaseType.Solid,
            MohsHardness = 6,
            Density_kg_m3 = 2950,
            YoungModulus_GPa = 80,
            PoissonRatio = 0.25,
            FrictionAngle_deg = 33,
            CompressiveStrength_MPa = 200,
            TensileStrength_MPa = 15,
            ThermalConductivity_W_mK = 2.0,
            SpecificHeatCapacity_J_kgK = 840,
            TypicalPorosity_fraction = 0.01,
            Vp_m_s = 5600, Vs_m_s = 3200, VpVsRatio = 1.75,
            AcousticImpedance_MRayl = 16.52,
            MagneticSusceptibility_SI = 3e-2,
            Notes = "Dense, low-porosity basalt.",
            Sources = new List<string> { "Rock property compilations; Eng. tables." },
            IsUserMaterial = false
        });

        // --- METAMORPHIC ROCKS -----------------------------------
        _materials.Add(new PhysicalMaterial
        {
            Name = "Marble (calcite)",
            Phase = PhaseType.Solid,
            MohsHardness = 3.5,
            Density_kg_m3 = 2700,
            YoungModulus_GPa = 50,
            PoissonRatio = 0.25,
            CompressiveStrength_MPa = 115,
            TensileStrength_MPa = 4,
            ThermalConductivity_W_mK = 2.8,
            SpecificHeatCapacity_J_kgK = 880,
            Vp_m_s = 5500, Vs_m_s = 3100,
            AcousticImpedance_MRayl = 14.85,
            Notes = "Properties vary based on purity and texture.",
            Sources = new List<string> { "Geology.com [10]", "Compare Rocks [17]", "Geology Science [3]" },
            IsUserMaterial = false
        });
        _materials.Add(new PhysicalMaterial
        {
            Name = "Quartzite",
            Phase = PhaseType.Solid,
            MohsHardness = 7,
            Density_kg_m3 = 2650,
            YoungModulus_GPa = 60,
            PoissonRatio = 0.15,
            CompressiveStrength_MPa = 115,
            TensileStrength_MPa = 10,
            ThermalConductivity_W_mK = 7.5,
            SpecificHeatCapacity_J_kgK = 750,
            Vp_m_s = 5800, Vs_m_s = 3900,
            Notes = "Very hard, low porosity metamorphic rock.",
            Sources = new List<string> { "Geology.com [20]", "Compare Rocks [13]", "Dedalo Stone [12]" },
            IsUserMaterial = false
        });

        // --- ENGINEERING MATERIALS -----------------------------------
        _materials.Add(new PhysicalMaterial
        {
            Name = "Steel (Carbon)",
            Phase = PhaseType.Solid,
            Density_kg_m3 = 7850,
            YoungModulus_GPa = 200,
            PoissonRatio = 0.30,
            YieldStrength_MPa = 250,
            TensileStrength_MPa = 400,
            ThermalConductivity_W_mK = 45,
            SpecificHeatCapacity_J_kgK = 490,
            Vp_m_s = 5960, Vs_m_s = 3240,
            ElectricalResistivity_Ohm_m = 1.7e-7,
            Notes = "Properties for a typical low-carbon steel.",
            Sources = new List<string> { "Metal Zenith [8]", "MatWeb", "ULMA Forged Solutions [32]" },
            IsUserMaterial = false
        });
        _materials.Add(new PhysicalMaterial
        {
            Name = "Aluminum (Alloy)",
            Phase = PhaseType.Solid,
            Density_kg_m3 = 2700,
            YoungModulus_GPa = 70,
            PoissonRatio = 0.33,
            YieldStrength_MPa = 200,
            TensileStrength_MPa = 290,
            ThermalConductivity_W_mK = 167,
            SpecificHeatCapacity_J_kgK = 900,
            Vp_m_s = 6320, Vs_m_s = 3130,
            ElectricalResistivity_Ohm_m = 2.82e-8,
            Notes = "Properties can vary significantly between alloys.",
            Sources = new List<string> { "Kloeckner Metals [7]", "GSA [21]" },
            IsUserMaterial = false
        });
        _materials.Add(new PhysicalMaterial
        {
            Name = "Copper",
            Phase = PhaseType.Solid,
            Density_kg_m3 = 8960,
            YoungModulus_GPa = 117,
            PoissonRatio = 0.34,
            YieldStrength_MPa = 70,
            TensileStrength_MPa = 220,
            ThermalConductivity_W_mK = 401,
            SpecificHeatCapacity_J_kgK = 385,
            Vp_m_s = 4660, Vs_m_s = 2260,
            ElectricalResistivity_Ohm_m = 1.68e-8,
            Notes = "Pure, annealed copper.",
            Sources = new List<string> { "C-FLO COPPER [11]", "ThoughtCo [34]" },
            IsUserMaterial = false
        });
        _materials.Add(new PhysicalMaterial
        {
            Name = "Glass (Soda-Lime)",
            Phase = PhaseType.Solid,
            MohsHardness = 6,
            Density_kg_m3 = 2500,
            YoungModulus_GPa = 70,
            PoissonRatio = 0.23,
            CompressiveStrength_MPa = 1000,
            TensileStrength_MPa = 33,
            ThermalConductivity_W_mK = 1,
            SpecificHeatCapacity_J_kgK = 840,
            Vp_m_s = 5840, Vs_m_s = 3430,
            ElectricalResistivity_Ohm_m = 1e12,
            Notes = "General purpose window and container glass.",
            Sources = new List<string> { "Abrisa Technologies [1]", "Study.com [14]" },
            IsUserMaterial = false
        });
    }
}