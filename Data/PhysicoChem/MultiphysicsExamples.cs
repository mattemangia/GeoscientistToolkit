// GeoscientistToolkit/Data/PhysicoChem/MultiphysicsExamples.cs
//
// Example simulation setups demonstrating multiphysics capabilities
// Including waves, evaporation, wind, currents, and heat sources

using System;
using System.Collections.Generic;

namespace GeoscientistToolkit.Data.PhysicoChem;

/// <summary>
/// Pre-configured multiphysics simulation examples
/// </summary>
public static class MultiphysicsExamples
{
    /// <summary>
    /// Create a wave tank simulation with incoming waves
    /// </summary>
    public static PhysicoChemDataset CreateWaveTank(double width, double height, double depth)
    {
        var dataset = new PhysicoChemDataset("Wave Tank", "Simulation of wave propagation in a tank");

        // Create water domain
        var waterDomain = new ReactorDomain
        {
            Name = "Water",
            Geometry = new ReactorGeometry
            {
                Type = GeometryType.Box,
                Center = (width / 2, depth / 2, height / 4),
                Dimensions = (width, depth, height / 2)
            },
            Material = new MaterialProperties
            {
                Density = 1000.0,      // kg/m³
                Porosity = 1.0,         // Pure water
                Permeability = 1e-10,
                ThermalConductivity = 0.6,
                SpecificHeat = 4186.0
            },
            InitialConditions = new InitialConditions
            {
                Temperature = 298.15,
                Pressure = 101325.0,
                LiquidSaturation = 1.0,
                FluidType = "Water"
            }
        };

        dataset.AddDomain(waterDomain);

        // Add progressive wave force
        var waveForce = new ForceField
        {
            Name = "Progressive Wave",
            Type = ForceType.Wave,
            IsActive = true,
            Wave = new WaveProperties
            {
                Type = WaveType.Progressive,
                Amplitude = 0.1,        // 10 cm waves
                Wavelength = 2.0,       // 2 m wavelength
                Period = 2.0,           // 2 second period
                WaterLevel = height / 2,
                WaterComposition = new Dictionary<string, double>
                {
                    { "NaCl", 0.035 }   // Seawater salinity
                },
                WaterTemperature = 298.15,
                WaterSalinity = 35.0
            }
        };

        dataset.Forces.Add(waveForce);

        // Add gravity
        dataset.Forces.Add(new ForceField
        {
            Name = "Gravity",
            Type = ForceType.Gravity,
            GravityVector = (0, 0, -9.81)
        });

        // Set simulation parameters
        dataset.SimulationParams.TotalTime = 60.0;
        dataset.SimulationParams.TimeStep = 0.01;
        dataset.SimulationParams.EnableFlow = true;
        dataset.SimulationParams.EnableHeatTransfer = false;

        return dataset;
    }

    /// <summary>
    /// Create a reactor with water inlet, outlet, and evaporation
    /// </summary>
    public static PhysicoChemDataset CreateEvaporatingReactor(double radius, double height)
    {
        var dataset = new PhysicoChemDataset("Evaporating Reactor",
            "Reactor with water inlet, outlet, and surface evaporation");

        // Create cylindrical reactor
        var reactor = new ReactorDomain
        {
            Name = "Reactor",
            Geometry = new ReactorGeometry
            {
                Type = GeometryType.Cylinder,
                Center = (0, 0, height / 2),
                Radius = radius,
                Height = height
            },
            Material = new MaterialProperties
            {
                Density = 1000.0,
                Porosity = 1.0,
                Permeability = 1e-10,
                ThermalConductivity = 0.6,
                SpecificHeat = 4186.0
            },
            InitialConditions = new InitialConditions
            {
                Temperature = 298.15,
                Pressure = 101325.0,
                LiquidSaturation = 0.8,  // Initially 80% filled
                Concentrations = new Dictionary<string, double>
                {
                    { "Salt", 0.01 }     // 1% salt solution
                }
            }
        };

        dataset.AddDomain(reactor);

        // Water inlet at bottom
        var inlet = new BoundaryCondition
        {
            Name = "Water Inlet",
            Type = BoundaryType.Inlet,
            Location = BoundaryLocation.ZMin,
            Variable = BoundaryVariable.MassFlux,
            FluxValue = 0.01,            // kg/s
            IsCompositional = true,
            InletComposition = new Dictionary<string, double>
            {
                { "Salt", 0.005 }        // Incoming water has 0.5% salt
            },
            InletTemperature = 293.15,   // 20°C
            InletFlowRate = 0.01,
            IsActive = true
        };

        dataset.BoundaryConditions.Add(inlet);

        // Outlet at top
        var outlet = new BoundaryCondition
        {
            Name = "Overflow Outlet",
            Type = BoundaryType.Outlet,
            Location = BoundaryLocation.ZMax,
            Variable = BoundaryVariable.Pressure,
            Value = 101325.0,
            IsActive = true
        };

        dataset.BoundaryConditions.Add(outlet);

        // Evaporation at surface
        var evapForce = new ForceField
        {
            Name = "Surface Evaporation",
            Type = ForceType.Custom,  // Would use dedicated evaporation in solver
            IsActive = true,
            Evaporation = new EvaporationProperties
            {
                IsActive = true,
                SurfaceLevel = height * 0.8,
                Model = EvaporationModel.Penman,
                AirTemperature = 298.15,
                RelativeHumidity = 0.5,
                WindSpeed = 2.0,
                SolarRadiation = 500.0,
                AccountForSalinity = true
            }
        };

        dataset.Forces.Add(evapForce);

        // Heat source (solar radiation)
        var heatSource = new ForceField
        {
            Name = "Solar Heating",
            Type = ForceType.Custom,
            IsActive = true,
            HeatSource = new HeatSourceProperties
            {
                Type = HeatSourceType.Solar,
                SolarIntensity = 800.0,  // W/m²
                SolarAngle = 30.0,       // 30° from vertical
                DiurnalCycle = true
            }
        };

        dataset.Forces.Add(heatSource);

        dataset.SimulationParams.TotalTime = 3600.0;  // 1 hour
        dataset.SimulationParams.TimeStep = 1.0;
        dataset.SimulationParams.EnableReactiveTransport = true;
        dataset.SimulationParams.EnableHeatTransfer = true;
        dataset.SimulationParams.EnableFlow = true;

        return dataset;
    }

    /// <summary>
    /// Create underwater reactor with currents and thermal stratification
    /// </summary>
    public static PhysicoChemDataset CreateUnderwaterReactor(double width, double height, double depth)
    {
        var dataset = new PhysicoChemDataset("Underwater Reactor",
            "Subsurface reactor with ocean currents and thermal gradients");

        // Create reactor volume
        var reactor = new ReactorDomain
        {
            Name = "Underwater Volume",
            Geometry = new ReactorGeometry
            {
                Type = GeometryType.Box,
                Center = (width / 2, depth / 2, -height / 2),  // Below sea level
                Dimensions = (width, depth, height)
            },
            Material = new MaterialProperties
            {
                Density = 1025.0,       // Seawater density
                Porosity = 1.0,
                Permeability = 1e-10,
                ThermalConductivity = 0.6,
                SpecificHeat = 3985.0   // Seawater specific heat
            },
            InitialConditions = new InitialConditions
            {
                Temperature = 283.15,   // 10°C
                Pressure = 101325.0 + 1025 * 9.81 * height,  // Hydrostatic pressure
                LiquidSaturation = 1.0,
                Concentrations = new Dictionary<string, double>
                {
                    { "NaCl", 0.035 },  // Seawater salinity
                    { "O2", 8.0e-6 }    // Dissolved oxygen
                }
            }
        };

        dataset.AddDomain(reactor);

        // Underwater current
        var current = new ForceField
        {
            Name = "Ocean Current",
            Type = ForceType.UnderwaterCurrent,
            IsActive = true,
            Current = new CurrentProperties
            {
                Type = CurrentType.DepthVarying,
                VelocityX = 0.5,        // 0.5 m/s current
                VelocityY = 0.0,
                VelocityZ = 0.0,
                SurfaceLevel = 0.0,
                CharacteristicDepth = 50.0,
                CurrentComposition = new Dictionary<string, double>
                {
                    { "NaCl", 0.035 },
                    { "O2", 8.0e-6 },
                    { "Nutrients", 1.0e-6 }
                },
                CurrentTemperature = 283.15,
                CurrentSalinity = 35.0
            }
        };

        dataset.Forces.Add(current);

        // Geothermal heat source from below
        var geothermalHeat = new ForceField
        {
            Name = "Geothermal Heat",
            Type = ForceType.Custom,
            IsActive = true,
            HeatSource = new HeatSourceProperties
            {
                Type = HeatSourceType.Geothermal,
                PowerDensity = 0.1,     // W/m² (typical geothermal flux)
                Temperature = 373.15    // 100°C at depth
            }
        };

        dataset.Forces.Add(geothermalHeat);

        // Buoyancy
        dataset.Forces.Add(new ForceField
        {
            Name = "Buoyancy",
            Type = ForceType.Buoyancy
        });

        dataset.SimulationParams.TotalTime = 86400.0;  // 24 hours
        dataset.SimulationParams.TimeStep = 10.0;
        dataset.SimulationParams.EnableReactiveTransport = true;
        dataset.SimulationParams.EnableHeatTransfer = true;
        dataset.SimulationParams.EnableFlow = true;

        return dataset;
    }

    /// <summary>
    /// Create wind-driven evaporation pond
    /// </summary>
    public static PhysicoChemDataset CreateEvaporationPond(double length, double width)
    {
        var dataset = new PhysicoChemDataset("Evaporation Pond",
            "Solar evaporation pond with wind effects");

        double waterDepth = 0.5;  // 50 cm water depth

        var pond = new ReactorDomain
        {
            Name = "Brine Pool",
            Geometry = new ReactorGeometry
            {
                Type = GeometryType.Box,
                Center = (length / 2, width / 2, waterDepth / 2),
                Dimensions = (length, width, waterDepth)
            },
            Material = new MaterialProperties
            {
                Density = 1200.0,       // Dense brine
                Porosity = 1.0,
                ThermalConductivity = 0.6,
                SpecificHeat = 3500.0
            },
            InitialConditions = new InitialConditions
            {
                Temperature = 308.15,   // 35°C
                Pressure = 101325.0,
                LiquidSaturation = 1.0,
                Concentrations = new Dictionary<string, double>
                {
                    { "NaCl", 0.25 },   // 25% brine
                    { "MgCl2", 0.05 },
                    { "CaCl2", 0.02 }
                }
            }
        };

        dataset.AddDomain(pond);

        // Wind across the pond
        var wind = new ForceField
        {
            Name = "Prevailing Wind",
            Type = ForceType.Wind,
            IsActive = true,
            Wind = new WindProperties
            {
                Speed = 5.0,            // 5 m/s
                Direction = 90.0,       // From East
                SurfaceLevel = waterDepth,
                EnableGusts = true,
                GustIntensity = 0.4,
                GustPeriod = 30.0,
                RelativeHumidity = 0.3, // Arid climate
                AirTemperature = 303.15 // 30°C
            }
        };

        dataset.Forces.Add(wind);

        // Intense evaporation
        var evaporation = new ForceField
        {
            Name = "Evaporation",
            Type = ForceType.Custom,
            IsActive = true,
            Evaporation = new EvaporationProperties
            {
                IsActive = true,
                SurfaceLevel = waterDepth,
                Model = EvaporationModel.Penman,
                AirTemperature = 303.15,
                RelativeHumidity = 0.3,
                WindSpeed = 5.0,
                SolarRadiation = 1000.0,  // Strong sunlight
                AccountForSalinity = true
            }
        };

        dataset.Forces.Add(evaporation);

        // Solar heating
        var solar = new ForceField
        {
            Name = "Solar Radiation",
            Type = ForceType.Custom,
            IsActive = true,
            HeatSource = new HeatSourceProperties
            {
                Type = HeatSourceType.Solar,
                SolarIntensity = 1000.0,
                SolarAngle = 20.0,
                DiurnalCycle = true
            }
        };

        dataset.Forces.Add(solar);

        dataset.SimulationParams.TotalTime = 86400.0 * 7;  // 1 week
        dataset.SimulationParams.TimeStep = 60.0;
        dataset.SimulationParams.EnableReactiveTransport = true;
        dataset.SimulationParams.EnableHeatTransfer = true;
        dataset.SimulationParams.EnableFlow = true;
        dataset.SimulationParams.EnableNucleation = true;  // For salt crystallization

        return dataset;
    }

    /// <summary>
    /// Create coastal simulation with waves, wind, and tidal currents
    /// </summary>
    public static PhysicoChemDataset CreateCoastalSimulation(double width, double depth)
    {
        var dataset = new PhysicoChemDataset("Coastal Zone",
            "Coastal simulation with waves, wind, tides, and evaporation");

        double waterHeight = 5.0;  // 5 m water depth

        var ocean = new ReactorDomain
        {
            Name = "Coastal Water",
            Geometry = new ReactorGeometry
            {
                Type = GeometryType.Box,
                Center = (width / 2, depth / 2, waterHeight / 2),
                Dimensions = (width, depth, waterHeight)
            },
            Material = new MaterialProperties
            {
                Density = 1025.0,
                Porosity = 1.0,
                ThermalConductivity = 0.6,
                SpecificHeat = 3985.0
            },
            InitialConditions = new InitialConditions
            {
                Temperature = 290.15,
                Pressure = 101325.0,
                LiquidSaturation = 1.0,
                Concentrations = new Dictionary<string, double>
                {
                    { "NaCl", 0.035 },
                    { "O2", 8.0e-6 }
                }
            }
        };

        dataset.AddDomain(ocean);

        // Ocean waves
        var waves = new ForceField
        {
            Name = "Ocean Waves",
            Type = ForceType.Wave,
            IsActive = true,
            Wave = new WaveProperties
            {
                Type = WaveType.Progressive,
                Amplitude = 0.5,
                Wavelength = 10.0,
                Period = 5.0,
                WaterLevel = waterHeight,
                WaterComposition = new Dictionary<string, double> { { "NaCl", 0.035 } },
                WaterTemperature = 290.15,
                WaterSalinity = 35.0
            }
        };

        dataset.Forces.Add(waves);

        // Tidal currents
        var tides = new ForceField
        {
            Name = "Tidal Current",
            Type = ForceType.UnderwaterCurrent,
            IsActive = true,
            Current = new CurrentProperties
            {
                Type = CurrentType.Tidal,
                VelocityX = 0.3,
                VelocityY = 0.0,
                TidalPeriod = 12.42 * 3600,  // Semi-diurnal tide
                SurfaceLevel = waterHeight,
                CurrentComposition = new Dictionary<string, double> { { "NaCl", 0.035 } },
                CurrentTemperature = 290.15,
                CurrentSalinity = 35.0
            }
        };

        dataset.Forces.Add(tides);

        // Coastal wind
        var wind = new ForceField
        {
            Name = "Sea Breeze",
            Type = ForceType.Wind,
            IsActive = true,
            Wind = new WindProperties
            {
                Speed = 8.0,
                Direction = 180.0,  // Onshore wind
                SurfaceLevel = waterHeight,
                EnableGusts = true,
                GustIntensity = 0.5,
                RelativeHumidity = 0.75,
                AirTemperature = 288.15
            }
        };

        dataset.Forces.Add(wind);

        dataset.SimulationParams.TotalTime = 86400.0;  // 24 hours
        dataset.SimulationParams.TimeStep = 1.0;
        dataset.SimulationParams.EnableReactiveTransport = true;
        dataset.SimulationParams.EnableHeatTransfer = true;
        dataset.SimulationParams.EnableFlow = true;

        return dataset;
    }
}
