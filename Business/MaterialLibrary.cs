// GeoscientistToolkit/Business/MaterialLibrary.cs
//
// Singleton service that stores materials, handles I/O, and seeds realistic values.
//
// SOURCES (general; see per-material comments in SeedDefaults()):
// - Engineering Toolbox (common material properties: ρ, k, E, ν, η, c_s)
// - CRC Handbook / NIST data for fluids & gases (η, k, ρ, speed of sound)
// - Rock physics & geotechnical handbooks for typical rock E, ν, φ, Vp/Vs, friction angles
//   (e.g., Schön 2015 "Physical Properties of Rocks"; Carmichael 1982; Jaeger et al. "Rock Mechanics")
// - Typical values consolidated to SI; ranges exist—these are representative midpoints.
//   Always validate for your specimen, T, P, saturation, fabric, anisotropy.
//
// NOTE: BreakingPressure_MPa is interpreted as typical compressive strength or collapse pressure proxy
//       when meaningful; fluids/gases keep it null.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business
{
    public sealed class MaterialLibrary
    {
        private static readonly Lazy<MaterialLibrary> _lazy = new(() => new MaterialLibrary());
        public static MaterialLibrary Instance => _lazy.Value;

        public IReadOnlyList<PhysicalMaterial> Materials => _materials;
        private readonly List<PhysicalMaterial> _materials = new();

        // Default path in project folder
        public string LibraryFilePath { get; private set; } = "Materials.library.json";

        private MaterialLibrary()
        {
            // Seed with defaults so the software "can pick stuff if needed".
            SeedDefaults();
        }

        public void SetLibraryFilePath(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
                LibraryFilePath = path;
        }

        public void Clear() => _materials.Clear();

        public PhysicalMaterial? Find(string name) =>
            _materials.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

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
                int idx = _materials.IndexOf(existing);
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
                string p = path ?? LibraryFilePath;
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
                string p = path ?? LibraryFilePath;
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

        private static JsonSerializerOptions JsonOptions() => new()
        {
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

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
                Viscosity_Pa_s = 1.0e-3,                 // Pa·s @ ~20°C
                Density_kg_m3 = 998,                     // kg/m3 @ ~20°C
                ThermalConductivity_W_mK = 0.6,          // W/mK @ ~25°C
                PoissonRatio = 0.5,                      // incompressible fluid limit (for reference)
                TypicalWettability_contactAngle_deg = 0, // water on clean hydrophilic solids
                Vp_m_s = 1480,                           // speed of sound @20°C
                Vs_m_s = null,
                VpVsRatio = null,
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
                Density_kg_m3 = 1.2,                      // ~1.2–1.25
                Viscosity_Pa_s = 1.8e-5,                  // Pa·s
                ThermalConductivity_W_mK = 0.026,
                Vp_m_s = 350, Vs_m_s = null, VpVsRatio = null,
                Notes = "Dry nitrogen at ambient conditions.",
                Sources = new List<string>{ "NIST/Engineering Toolbox typical gas properties at ~20°C." },
                IsUserMaterial = false
            });
            _materials.Add(new PhysicalMaterial
            {
                Name = "Oxygen (O2, 20°C, 1 atm)",
                Phase = PhaseType.Gas,
                Density_kg_m3 = 1.33,                     // ~1.33–1.43 depending on T
                Viscosity_Pa_s = 2.0e-5,
                ThermalConductivity_W_mK = 0.026,
                Vp_m_s = 330,
                Notes = "Dry oxygen at ambient conditions.",
                Sources = new List<string>{ "NIST/Engineering Toolbox typical gas properties at ~20°C." },
                IsUserMaterial = false
            });
            _materials.Add(new PhysicalMaterial
            {
                Name = "Carbon Dioxide (CO2, 20°C, 1 atm)",
                Phase = PhaseType.Gas,
                Density_kg_m3 = 1.8,
                Viscosity_Pa_s = 1.5e-5,
                ThermalConductivity_W_mK = 0.017,
                Vp_m_s = 270,
                Notes = "Dry CO₂ at ambient conditions.",
                Sources = new List<string>{ "NIST data for CO₂; Engineering Toolbox." },
                IsUserMaterial = false
            });
            _materials.Add(new PhysicalMaterial
            {
                Name = "Helium (He, 20°C, 1 atm)",
                Phase = PhaseType.Gas,
                Density_kg_m3 = 0.166, // ~0.166 at 20°C
                Viscosity_Pa_s = 1.96e-5,
                ThermalConductivity_W_mK = 0.15, // high k
                Vp_m_s = 970, // ~973 m/s near 0°C; ~1000 m/s around 20°C
                Notes = "Helium has very high thermal conductivity.",
                Sources = new List<string>{ "NIST He properties; Engineering Toolbox." },
                IsUserMaterial = false
            });
            _materials.Add(new PhysicalMaterial
            {
                Name = "Argon (Ar, 20°C, 1 atm)",
                Phase = PhaseType.Gas,
                Density_kg_m3 = 1.62, // ~1.62–1.78 (T dependent)
                Viscosity_Pa_s = 2.2e-5,
                ThermalConductivity_W_mK = 0.0177,
                Vp_m_s = 323,
                Notes = "Noble gas at ambient.",
                Sources = new List<string>{ "NIST/Engineering Toolbox for Ar." },
                IsUserMaterial = false
            });
            _materials.Add(new PhysicalMaterial
            {
                Name = "Methane (CH4, 20°C, 1 atm)",
                Phase = PhaseType.Gas,
                Density_kg_m3 = 0.66,
                Viscosity_Pa_s = 1.1e-5,
                ThermalConductivity_W_mK = 0.034,
                Vp_m_s = 460, // ~450-470 m/s
                Notes = "Methane at ambient.",
                Sources = new List<string>{ "NIST methane properties; Engineering Toolbox." },
                IsUserMaterial = false
            });

            // --- PLASTICS --------------------------------------------
            _materials.Add(new PhysicalMaterial
            {
                Name = "Polyethylene (PE, HDPE)",
                Phase = PhaseType.Solid,
                MohsHardness = 2,
                Density_kg_m3 = 950,
                ThermalConductivity_W_mK = 0.5,     // HDPE high end
                PoissonRatio = 0.42,
                YoungModulus_GPa = 0.9,              // 0.7–1.0 typical
                TypicalWettability_contactAngle_deg = 90,
                Vp_m_s = 1900,
                Notes = "Typical HDPE properties at room temperature.",
                Sources = new List<string>{ "Polymer datasheets; Engineering Toolbox." },
                IsUserMaterial = false
            });
            _materials.Add(new PhysicalMaterial
            {
                Name = "Polyvinyl Chloride (PVC)",
                Phase = PhaseType.Solid,
                MohsHardness = 3,
                Density_kg_m3 = 1330,
                ThermalConductivity_W_mK = 0.19,
                PoissonRatio = 0.38,
                YoungModulus_GPa = 3.4,
                TypicalWettability_contactAngle_deg = 80,
                Vp_m_s = 2400,
                Notes = "Rigid PVC properties.",
                Sources = new List<string>{ "Polymer datasheets; Engineering Toolbox." },
                IsUserMaterial = false
            });
            _materials.Add(new PhysicalMaterial
            {
                Name = "Polystyrene (PS)",
                Phase = PhaseType.Solid,
                MohsHardness = 3,
                Density_kg_m3 = 1050,
                ThermalConductivity_W_mK = 0.093,
                PoissonRatio = 0.35,
                YoungModulus_GPa = 3.4,
                Vp_m_s = 2400,
                Notes = "General-purpose PS.",
                Sources = new List<string>{ "Polymer datasheets; Engineering Toolbox." },
                IsUserMaterial = false
            });
            _materials.Add(new PhysicalMaterial
            {
                Name = "Polyethylene Terephthalate (PET)",
                Phase = PhaseType.Solid,
                MohsHardness = 3,
                Density_kg_m3 = 1370,
                ThermalConductivity_W_mK = 0.24,
                PoissonRatio = 0.35,
                YoungModulus_GPa = 2.5,
                Vp_m_s = 2500,
                Notes = "Bottle-grade PET ballpark.",
                Sources = new List<string>{ "Polymer datasheets; Engineering Toolbox." },
                IsUserMaterial = false
            });

            // --- WOOD ------------------------------------------------
            _materials.Add(new PhysicalMaterial
            {
                Name = "Wood (Pine, along grain, ~12% MC)",
                Phase = PhaseType.Solid,
                MohsHardness = 2,
                Density_kg_m3 = 500,
                ThermalConductivity_W_mK = 0.12,
                PoissonRatio = 0.30,
                YoungModulus_GPa = 9,    // along grain
                BreakingPressure_MPa = 35, // compressive parallel to grain (order)
                TypicalPorosity_fraction = 0.5, // cellular structure (qualitative high)
                Vp_m_s = 3500, Vs_m_s = 1800, VpVsRatio = 1.9,
                Notes = "Orthotropic; strong anisotropy with grain.",
                Sources = new List<string>{ "Timber engineering data; Wood handbooks." },
                IsUserMaterial = false
            });
            _materials.Add(new PhysicalMaterial
            {
                Name = "Wood (Oak, along grain, ~12% MC)",
                Phase = PhaseType.Solid,
                MohsHardness = 3,
                Density_kg_m3 = 700,
                ThermalConductivity_W_mK = 0.17,
                PoissonRatio = 0.30,
                YoungModulus_GPa = 12,
                BreakingPressure_MPa = 50,
                Vp_m_s = 5000, Vs_m_s = 2500, VpVsRatio = 2.0,
                Notes = "Hardwood; anisotropy applies.",
                Sources = new List<string>{ "Timber engineering data; Wood handbooks." },
                IsUserMaterial = false
            });

            // --- SEDIMENTARY ROCKS -----------------------------------
            _materials.Add(new PhysicalMaterial
            {
                Name = "Sandstone (quartz-rich, dense)",
                Phase = PhaseType.Solid,
                MohsHardness = 7,
                Density_kg_m3 = 2400,
                ThermalConductivity_W_mK = 5.0,
                PoissonRatio = 0.25,
                FrictionAngle_deg = 32,
                YoungModulus_GPa = 38,
                BreakingPressure_MPa = 120,           // UCS order-of-magnitude
                TypicalPorosity_fraction = 0.10,
                Vp_m_s = 4000, Vs_m_s = 2200, VpVsRatio = 1.82,
                Notes = "Dry, compact sandstone; properties vary strongly with φ, cement, saturation.",
                Sources = new List<string>{ "Rock property compilations: Schön, Jaeger, Carmichael; Eng. tables." },
                IsUserMaterial = false
            });
            _materials.Add(new PhysicalMaterial
            {
                Name = "Limestone (calcite, dense)",
                Phase = PhaseType.Solid,
                MohsHardness = 3,
                Density_kg_m3 = 2600,
                ThermalConductivity_W_mK = 2.1,
                PoissonRatio = 0.25,
                FrictionAngle_deg = 36,
                YoungModulus_GPa = 77,
                BreakingPressure_MPa = 150, // wide range; sometimes reported much higher
                TypicalPorosity_fraction = 0.03,
                Vp_m_s = 5800, Vs_m_s = 3200, VpVsRatio = 1.81,
                Notes = "Dense limestone; micrite/porosity lowers E & Vp.",
                Sources = new List<string>{ "Rock property compilations; Eng. tables." },
                IsUserMaterial = false
            });
            _materials.Add(new PhysicalMaterial
            {
                Name = "Dolostone (dolomite, dense)",
                Phase = PhaseType.Solid,
                MohsHardness = 3.5,
                Density_kg_m3 = 2850,
                ThermalConductivity_W_mK = 3.0,
                PoissonRatio = 0.27,
                FrictionAngle_deg = 37,
                YoungModulus_GPa = 70,
                BreakingPressure_MPa = 180,
                TypicalPorosity_fraction = 0.05,
                Vp_m_s = 6200, Vs_m_s = 3400, VpVsRatio = 1.82,
                Notes = "Crystalline dolostone; variable porosity.",
                Sources = new List<string>{ "Rock property compilations; Eng. tables." },
                IsUserMaterial = false
            });
            _materials.Add(new PhysicalMaterial
            {
                Name = "Shale (clay-rich)",
                Phase = PhaseType.Solid,
                MohsHardness = 2.5,
                Density_kg_m3 = 2500,
                ThermalConductivity_W_mK = 1.5,
                PoissonRatio = 0.29,
                FrictionAngle_deg = 24,
                YoungModulus_GPa = 11,
                BreakingPressure_MPa = 100,
                TypicalPorosity_fraction = 0.20,
                Vp_m_s = 3000, Vs_m_s = 1500, VpVsRatio = 2.0,
                Notes = "Strong anisotropy; properties depend on clay, water content.",
                Sources = new List<string>{ "Rock property compilations; Eng. tables." },
                IsUserMaterial = false
            });
            _materials.Add(new PhysicalMaterial
            {
                Name = "Chalk",
                Phase = PhaseType.Solid,
                MohsHardness = 2.5,
                Density_kg_m3 = 2400,
                ThermalConductivity_W_mK = 1.3,
                PoissonRatio = 0.28,
                FrictionAngle_deg = 30,
                YoungModulus_GPa = 10,
                BreakingPressure_MPa = 30,
                TypicalPorosity_fraction = 0.35,
                Vp_m_s = 2500, Vs_m_s = 1200, VpVsRatio = 2.08,
                Notes = "High porosity carbonate mud.",
                Sources = new List<string>{ "Rock property compilations; Eng. tables." },
                IsUserMaterial = false
            });
            _materials.Add(new PhysicalMaterial
            {
                Name = "Conglomerate",
                Phase = PhaseType.Solid,
                MohsHardness = 7,
                Density_kg_m3 = 2400,
                ThermalConductivity_W_mK = 3.0,
                PoissonRatio = 0.25,
                FrictionAngle_deg = 35,
                YoungModulus_GPa = 35,
                BreakingPressure_MPa = 100,
                TypicalPorosity_fraction = 0.08,
                Vp_m_s = 3800, Vs_m_s = 2100, VpVsRatio = 1.81,
                Notes = "Matrix- and clast-dependent.",
                Sources = new List<string>{ "Rock property compilations; Eng. tables." },
                IsUserMaterial = false
            });

            // --- IGNEOUS ROCKS ---------------------------------------
            _materials.Add(new PhysicalMaterial
            {
                Name = "Granite (fresh, dense)",
                Phase = PhaseType.Solid,
                MohsHardness = 6.5,
                Density_kg_m3 = 2650,
                ThermalConductivity_W_mK = 2.7,
                PoissonRatio = 0.22,
                FrictionAngle_deg = 32,
                YoungModulus_GPa = 70,
                BreakingPressure_MPa = 200,
                TypicalPorosity_fraction = 0.01,
                Vp_m_s = 6000, Vs_m_s = 3300, VpVsRatio = 1.82,
                Notes = "Dry, intact granite.",
                Sources = new List<string>{ "Rock property compilations; Eng. tables." },
                IsUserMaterial = false
            });
            _materials.Add(new PhysicalMaterial
            {
                Name = "Basalt (dense)",
                Phase = PhaseType.Solid,
                MohsHardness = 6,
                Density_kg_m3 = 2950,
                ThermalConductivity_W_mK = 2.0,
                PoissonRatio = 0.25,
                FrictionAngle_deg = 33,
                YoungModulus_GPa = 80,
                BreakingPressure_MPa = 200,
                TypicalPorosity_fraction = 0.01,
                Vp_m_s = 5600, Vs_m_s = 3200, VpVsRatio = 1.75,
                Notes = "Dense, low-porosity basalt.",
                Sources = new List<string>{ "Rock property compilations; Eng. tables." },
                IsUserMaterial = false
            });
            _materials.Add(new PhysicalMaterial
            {
                Name = "Rhyolite",
                Phase = PhaseType.Solid,
                MohsHardness = 6,
                Density_kg_m3 = 2400,
                ThermalConductivity_W_mK = 1.8,
                PoissonRatio = 0.20,
                FrictionAngle_deg = 32,
                YoungModulus_GPa = 60,
                BreakingPressure_MPa = 150,
                TypicalPorosity_fraction = 0.05,
                Vp_m_s = 5200, Vs_m_s = 3000, VpVsRatio = 1.73,
                Notes = "Felsic volcanic; properties vary with vesicularity.",
                Sources = new List<string>{ "Rock property compilations; Eng. tables." },
                IsUserMaterial = false
            });
            _materials.Add(new PhysicalMaterial
            {
                Name = "Andesite",
                Phase = PhaseType.Solid,
                MohsHardness = 6,
                Density_kg_m3 = 2600,
                ThermalConductivity_W_mK = 2.0,
                PoissonRatio = 0.24,
                FrictionAngle_deg = 33,
                YoungModulus_GPa = 70,
                BreakingPressure_MPa = 180,
                TypicalPorosity_fraction = 0.03,
                Vp_m_s = 5600, Vs_m_s = 3200, VpVsRatio = 1.75,
                Notes = "Intermediate volcanic.",
                Sources = new List<string>{ "Rock property compilations; Eng. tables." },
                IsUserMaterial = false
            });
            _materials.Add(new PhysicalMaterial
            {
                Name = "Gabbro",
                Phase = PhaseType.Solid,
                MohsHardness = 6,
                Density_kg_m3 = 3000,
                ThermalConductivity_W_mK = 2.2,
                PoissonRatio = 0.26,
                FrictionAngle_deg = 34,
                YoungModulus_GPa = 90,
                BreakingPressure_MPa = 220,
                TypicalPorosity_fraction = 0.005,
                Vp_m_s = 6500, Vs_m_s = 3600, VpVsRatio = 1.81,
                Notes = "Mafic plutonic; very stiff.",
                Sources = new List<string>{ "Rock property compilations; Eng. tables." },
                IsUserMaterial = false
            });

            // You can keep adding more (Diorite, Peridotite, Obsidian, Pumice, Marl, Siltstone, Gypsum rock, Coal, etc.)
        }
    }
}
