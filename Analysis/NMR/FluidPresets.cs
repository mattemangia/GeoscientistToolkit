// GeoscientistToolkit/Analysis/NMR/FluidPresets.cs

using GeoscientistToolkit.Data.CtImageStack;

namespace GeoscientistToolkit.Analysis.NMR;

/// <summary>
///     Predefined fluid configurations for NMR simulations.
/// </summary>
public static class FluidPresets
{
    public static readonly Dictionary<string, FluidProperties> Presets = new()
    {
        ["Water (25°C)"] = new FluidProperties
        {
            Name = "Water (25°C)",
            DiffusionCoefficient = 2.0e-9, // m²/s
            Temperature = 25.0,
            Viscosity = 0.89e-3, // Pa·s
            Description = "Pure water at room temperature",
            DefaultRelaxivities = new Dictionary<string, double>
            {
                ["Quartz"] = 5.0,
                ["Sandstone"] = 10.0,
                ["Clay/Shale"] = 50.0,
                ["Carbonate"] = 15.0,
                ["Feldspar"] = 8.0
            }
        },

        ["Water (60°C)"] = new FluidProperties
        {
            Name = "Water (60°C)",
            DiffusionCoefficient = 4.5e-9, // m²/s - higher temp = faster diffusion
            Temperature = 60.0,
            Viscosity = 0.47e-3, // Pa·s
            Description = "Water at reservoir temperature",
            DefaultRelaxivities = new Dictionary<string, double>
            {
                ["Quartz"] = 3.0,
                ["Sandstone"] = 7.0,
                ["Clay/Shale"] = 35.0,
                ["Carbonate"] = 10.0,
                ["Feldspar"] = 6.0
            }
        },

        ["Brine (NaCl 3%)"] = new FluidProperties
        {
            Name = "Brine (NaCl 3%)",
            DiffusionCoefficient = 1.6e-9, // m²/s - salt reduces diffusion
            Temperature = 25.0,
            Viscosity = 0.95e-3, // Pa·s
            Description = "Saltwater with 3% NaCl concentration",
            DefaultRelaxivities = new Dictionary<string, double>
            {
                ["Quartz"] = 8.0,
                ["Sandstone"] = 15.0,
                ["Clay/Shale"] = 80.0, // Clay interacts strongly with ions
                ["Carbonate"] = 20.0,
                ["Feldspar"] = 12.0
            }
        },

        ["Light Oil (API 35)"] = new FluidProperties
        {
            Name = "Light Oil (API 35)",
            DiffusionCoefficient = 3.0e-10, // m²/s - much slower than water
            Temperature = 25.0,
            Viscosity = 5.0e-3, // Pa·s
            Description = "Light crude oil, typical reservoir fluid",
            DefaultRelaxivities = new Dictionary<string, double>
            {
                ["Quartz"] = 0.5,
                ["Sandstone"] = 1.0,
                ["Clay/Shale"] = 5.0,
                ["Carbonate"] = 2.0,
                ["Feldspar"] = 1.0
            }
        },

        ["Heavy Oil (API 15)"] = new FluidProperties
        {
            Name = "Heavy Oil (API 15)",
            DiffusionCoefficient = 1.0e-11, // m²/s - very slow
            Temperature = 25.0,
            Viscosity = 100.0e-3, // Pa·s
            Description = "Heavy crude oil, high viscosity",
            DefaultRelaxivities = new Dictionary<string, double>
            {
                ["Quartz"] = 0.1,
                ["Sandstone"] = 0.2,
                ["Clay/Shale"] = 1.0,
                ["Carbonate"] = 0.5,
                ["Feldspar"] = 0.3
            }
        },

        ["Methane Gas"] = new FluidProperties
        {
            Name = "Methane Gas",
            DiffusionCoefficient = 2.0e-5, // m²/s - gas diffuses very fast
            Temperature = 25.0,
            Viscosity = 0.011e-3, // Pa·s
            Description = "Natural gas, primarily methane",
            DefaultRelaxivities = new Dictionary<string, double>
            {
                ["Quartz"] = 0.01,
                ["Sandstone"] = 0.02,
                ["Clay/Shale"] = 0.1,
                ["Carbonate"] = 0.05,
                ["Feldspar"] = 0.02
            }
        },

        ["CO₂ (Supercritical)"] = new FluidProperties
        {
            Name = "CO₂ (Supercritical)",
            DiffusionCoefficient = 1.0e-7, // m²/s - intermediate between liquid and gas
            Temperature = 40.0,
            Viscosity = 0.07e-3, // Pa·s
            Description = "Supercritical CO₂ for sequestration studies",
            DefaultRelaxivities = new Dictionary<string, double>
            {
                ["Quartz"] = 0.5,
                ["Sandstone"] = 1.0,
                ["Clay/Shale"] = 5.0,
                ["Carbonate"] = 10.0, // CO₂ interacts with carbonate
                ["Feldspar"] = 2.0
            }
        },

        ["Liquid Nitrogen"] = new FluidProperties
        {
            Name = "Liquid Nitrogen",
            DiffusionCoefficient = 1.5e-9, // m²/s
            Temperature = -196.0,
            Viscosity = 0.15e-3, // Pa·s
            Description = "Cryogenic fluid for porosity studies",
            DefaultRelaxivities = new Dictionary<string, double>
            {
                ["Quartz"] = 2.0,
                ["Sandstone"] = 4.0,
                ["Clay/Shale"] = 20.0,
                ["Carbonate"] = 8.0,
                ["Feldspar"] = 5.0
            }
        },

        ["Custom"] = new FluidProperties
        {
            Name = "Custom",
            DiffusionCoefficient = 2.0e-9,
            Temperature = 25.0,
            Viscosity = 1.0e-3,
            Description = "User-defined fluid properties",
            DefaultRelaxivities = new Dictionary<string, double>
            {
                ["Quartz"] = 5.0,
                ["Sandstone"] = 10.0,
                ["Clay/Shale"] = 50.0,
                ["Carbonate"] = 15.0,
                ["Feldspar"] = 8.0
            }
        }
    };

    /// <summary>
    ///     Applies a fluid preset to a simulation configuration.
    /// </summary>
    public static void ApplyPreset(NMRSimulationConfig config, string presetName, CtImageStackDataset dataset)
    {
        if (!Presets.TryGetValue(presetName, out var preset)) return;

        config.DiffusionCoefficient = preset.DiffusionCoefficient;

        // Apply relaxivities to matching materials
        foreach (var material in dataset.Materials)
        {
            if (material.ID == 0) continue; // Skip exterior

            if (!config.MaterialRelaxivities.ContainsKey(material.ID))
                config.MaterialRelaxivities[material.ID] = new MaterialRelaxivityConfig
                {
                    MaterialName = material.Name,
                    Color = material.Color,
                    SurfaceRelaxivity = 10.0
                };

            // Try to match material name to preset relaxivities
            var matConfig = config.MaterialRelaxivities[material.ID];
            foreach (var kvp in preset.DefaultRelaxivities)
                if (material.Name.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    matConfig.SurfaceRelaxivity = kvp.Value;
                    break;
                }
        }
    }
}

public class FluidProperties
{
    public string Name { get; set; }
    public double DiffusionCoefficient { get; set; } // m²/s
    public double Temperature { get; set; } // °C
    public double Viscosity { get; set; } // Pa·s
    public string Description { get; set; }
    public Dictionary<string, double> DefaultRelaxivities { get; set; }
}