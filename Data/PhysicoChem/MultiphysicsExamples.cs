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

    /// <summary>
    /// Create underwater sonar simulation with acoustic propagation
    /// </summary>
    public static PhysicoChemDataset CreateUnderwaterSonar(double width, double height, double depth)
    {
        var dataset = new PhysicoChemDataset("Underwater Sonar",
            "Sonar acoustic propagation and target detection in seawater");

        double waterDepth = 100.0;  // 100 m depth

        // Ocean water volume
        var ocean = new ReactorDomain
        {
            Name = "Ocean Water",
            Geometry = new ReactorGeometry
            {
                Type = GeometryType.Box,
                Center = (width / 2, depth / 2, -waterDepth / 2),
                Dimensions = (width, depth, waterDepth)
            },
            Material = new MaterialProperties
            {
                Density = 1025.0,       // Seawater
                Porosity = 1.0,
                ThermalConductivity = 0.6,
                SpecificHeat = 3985.0
            },
            InitialConditions = new InitialConditions
            {
                Temperature = 283.15,   // 10°C
                Pressure = 101325.0 + 1025 * 9.81 * waterDepth / 2,
                LiquidSaturation = 1.0,
                Concentrations = new Dictionary<string, double>
                {
                    { "NaCl", 0.035 },  // Seawater salinity
                    { "O2", 8.0e-6 }
                }
            }
        };

        dataset.AddDomain(ocean);

        // Active sonar - pulsed plane wave
        var sonarPing = new ForceField
        {
            Name = "Active Sonar Ping",
            Type = ForceType.Acoustic,
            IsActive = true,
            Acoustic = new AcousticProperties
            {
                WaveType = AcousticWaveType.PlaneWave,
                Frequency = 50000.0,    // 50 kHz (typical mid-frequency sonar)
                Amplitude = 1e6,        // 1 MPa pressure amplitude
                SpeedOfSound = 1500.0,  // In seawater at 10°C
                Direction = (1, 0, 0),  // Propagate in +X direction
                ParticleRadius = 0.0,   // Not relevant for sonar
                FluidCompressibility = 4.5e-10,
                AttenuationCoefficient = 0.01, // dB/m attenuation in seawater
                EnableAcousticStreaming = false
            }
        };

        dataset.Forces.Add(sonarPing);

        // Low frequency sonar for long-range detection
        var lowFreqSonar = new ForceField
        {
            Name = "Low Frequency Sonar",
            Type = ForceType.Acoustic,
            IsActive = true,
            Acoustic = new AcousticProperties
            {
                WaveType = AcousticWaveType.Spherical,
                Frequency = 1000.0,     // 1 kHz (long range)
                Amplitude = 5e5,        // 500 kPa
                SpeedOfSound = 1500.0,
                Direction = (0, 0, -1), // Downward
                AttenuationCoefficient = 0.001, // Lower attenuation at low frequency
                EnableAcousticStreaming = false
            }
        };

        dataset.Forces.Add(lowFreqSonar);

        // Underwater current affecting sound propagation
        var current = new ForceField
        {
            Name = "Ocean Current",
            Type = ForceType.UnderwaterCurrent,
            IsActive = true,
            Current = new CurrentProperties
            {
                Type = CurrentType.DepthVarying,
                VelocityX = 0.3,
                VelocityY = 0.0,
                VelocityZ = 0.0,
                SurfaceLevel = 0.0,
                CharacteristicDepth = 50.0,
                CurrentComposition = new Dictionary<string, double> { { "NaCl", 0.035 } },
                CurrentTemperature = 283.15,
                CurrentSalinity = 35.0
            }
        };

        dataset.Forces.Add(current);

        // Temperature gradient affecting sound speed
        dataset.SimulationParams.TotalTime = 10.0;  // 10 seconds
        dataset.SimulationParams.TimeStep = 0.001;  // 1 ms for acoustic resolution
        dataset.SimulationParams.EnableHeatTransfer = true;
        dataset.SimulationParams.EnableFlow = true;

        return dataset;
    }

    /// <summary>
    /// Create acoustic particle manipulation device (acoustophoresis)
    /// </summary>
    public static PhysicoChemDataset CreateAcousticTweezers(double channelLength, double channelWidth, double channelHeight)
    {
        var dataset = new PhysicoChemDataset("Acoustic Tweezers",
            "Ultrasonic standing wave for particle manipulation and separation");

        // Microfluidic channel
        var channel = new ReactorDomain
        {
            Name = "Microfluidic Channel",
            Geometry = new ReactorGeometry
            {
                Type = GeometryType.Box,
                Center = (channelLength / 2, channelWidth / 2, channelHeight / 2),
                Dimensions = (channelLength, channelWidth, channelHeight)
            },
            Material = new MaterialProperties
            {
                Density = 1000.0,
                Porosity = 1.0,
                ThermalConductivity = 0.6,
                SpecificHeat = 4186.0
            },
            InitialConditions = new InitialConditions
            {
                Temperature = 298.15,
                Pressure = 101325.0,
                LiquidSaturation = 1.0,
                Concentrations = new Dictionary<string, double>
                {
                    { "Cells", 1e6 },   // 1 million cells/m³
                    { "Beads", 1e9 }    // Polystyrene beads
                }
            }
        };

        dataset.AddDomain(channel);

        // Standing wave acoustic field
        var standingWave = new ForceField
        {
            Name = "Ultrasonic Standing Wave",
            Type = ForceType.Acoustic,
            IsActive = true,
            Acoustic = new AcousticProperties
            {
                WaveType = AcousticWaveType.StandingWave,
                Frequency = 2e6,        // 2 MHz - typical for particle manipulation
                Amplitude = 2e5,        // 200 kPa - moderate pressure amplitude
                SpeedOfSound = 1500.0,  // Water
                Direction = (0, 0, 1),  // Vertical standing wave
                ParticleRadius = 5e-6,  // 5 micron particles
                FluidCompressibility = 4.5e-10,
                ParticleCompressibility = 2.5e-11,  // Polystyrene
                EnableAcousticStreaming = true,
                AttenuationCoefficient = 0.002,
                NodeSpacing = 0.375e-3  // λ/2 = 750 μm / 2
            }
        };

        dataset.Forces.Add(standingWave);

        // Particle sedimentation
        var sedimentation = new ForceField
        {
            Name = "Particle Settling",
            Type = ForceType.Sedimentation,
            IsActive = true,
            Sedimentation = new SedimentationProperties
            {
                ParticleDensity = 1050.0,   // Polystyrene beads
                ParticleRadius = 5e-6,      // 5 microns
                FluidViscosity = 1e-3,
                VolumetricConcentration = 0.001,
                EnableTurbulentDispersion = false
            }
        };

        dataset.Forces.Add(sedimentation);

        // Inlet flow
        var inlet = new BoundaryCondition
        {
            Name = "Sample Inlet",
            Type = BoundaryType.Inlet,
            Location = BoundaryLocation.XMin,
            Variable = BoundaryVariable.VelocityX,
            Value = 0.001,  // 1 mm/s flow
            IsActive = true
        };

        dataset.BoundaryConditions.Add(inlet);

        dataset.SimulationParams.TotalTime = 60.0;  // 1 minute
        dataset.SimulationParams.TimeStep = 0.01;
        dataset.SimulationParams.EnableFlow = true;

        return dataset;
    }

    /// <summary>
    /// Create microfluidic device with electrokinetic flow control
    /// </summary>
    public static PhysicoChemDataset CreateElectrokineticMicrofluidics(double length, double width, double height)
    {
        var dataset = new PhysicoChemDataset("Electrokinetic Microfluidics",
            "Lab-on-chip device with electroosmotic and electrophoretic separation");

        var microchannel = new ReactorDomain
        {
            Name = "Separation Channel",
            Geometry = new ReactorGeometry
            {
                Type = GeometryType.Box,
                Center = (length / 2, width / 2, height / 2),
                Dimensions = (length, width, height)
            },
            Material = new MaterialProperties
            {
                Density = 1000.0,
                Porosity = 1.0,
                ThermalConductivity = 0.6,
                SpecificHeat = 4186.0
            },
            InitialConditions = new InitialConditions
            {
                Temperature = 298.15,
                Pressure = 101325.0,
                LiquidSaturation = 1.0,
                Concentrations = new Dictionary<string, double>
                {
                    { "DNA", 1e-6 },        // DNA fragments
                    { "Protein", 1e-5 },    // Protein molecules
                    { "Buffer", 0.01 }      // Electrolyte buffer
                }
            }
        };

        dataset.AddDomain(microchannel);

        // Electroosmotic flow
        var electroosmosis = new ForceField
        {
            Name = "Electroosmotic Pumping",
            Type = ForceType.Electrokinetic,
            IsActive = true,
            Electrokinetic = new ElectrokineticProperties
            {
                ElectricField = (1000.0, 0, 0),  // 1 kV/m in X direction
                FieldGradient = (0, 0, 0),
                Permittivity = 7.08e-10,         // Water
                ZetaPotential = -0.05,           // -50 mV (glass surface)
                FluidViscosity = 1e-3,
                EnableElectroosmosis = true,
                EnableElectrophoresis = true,
                EnableDielectrophoresis = false,
                ParticleCharge = -1.6e-19,       // Negatively charged biomolecules
                ParticleRadius = 1e-8,           // 10 nm
                ElectroosmoticMobility = 3e-8,   // Typical for glass/water
                IsACField = false
            }
        };

        dataset.Forces.Add(electroosmosis);

        dataset.SimulationParams.TotalTime = 300.0;  // 5 minutes
        dataset.SimulationParams.TimeStep = 0.1;
        dataset.SimulationParams.EnableReactiveTransport = true;
        dataset.SimulationParams.EnableFlow = true;

        return dataset;
    }

    /// <summary>
    /// Create bioreactor with bacterial growth and biofilm formation
    /// </summary>
    public static PhysicoChemDataset CreateBioreactor(double radius, double height)
    {
        var dataset = new PhysicoChemDataset("Bioreactor",
            "Bacterial fermentation with biofilm formation and substrate consumption");

        var reactor = new ReactorDomain
        {
            Name = "Fermentation Vessel",
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
                ThermalConductivity = 0.6,
                SpecificHeat = 4186.0
            },
            InitialConditions = new InitialConditions
            {
                Temperature = 310.15,   // 37°C
                Pressure = 101325.0,
                LiquidSaturation = 1.0,
                Concentrations = new Dictionary<string, double>
                {
                    { "Glucose", 10.0 },        // 10 kg/m³ initial substrate
                    { "Biomass", 0.1 },         // Initial inoculum
                    { "Oxygen", 8.0e-3 },       // Dissolved oxygen
                    { "Ethanol", 0.0 }          // Product
                }
            }
        };

        dataset.AddDomain(reactor);

        // Bacterial growth
        var bioprocess = new ForceField
        {
            Name = "Bacterial Fermentation",
            Type = ForceType.Biological,
            IsActive = true,
            Biological = new BiologicalProperties
            {
                Type = BiologicalProcessType.Fermentation,
                MaxGrowthRate = 5e-5,           // 1/s (doubling time ~4 hours)
                HalfSaturationConstant = 0.1,   // kg/m³
                YieldCoefficient = 0.5,          // 50% conversion
                DecayRate = 1e-6,
                EnableBiofilm = true,
                BiofilmThickness = 2e-4,        // 200 microns
                BiofilmDensity = 80.0,
                DetachmentRate = 5e-8,
                SubstrateName = "Glucose",
                ProductNames = new List<string> { "Ethanol", "CO2" },
                OptimalTemperature = 310.15,
                OptimalPH = 7.0,
                TemperatureCoefficient = 1.07
            }
        };

        dataset.Forces.Add(bioprocess);

        // Mixing/agitation (vortex)
        var agitation = new ForceField
        {
            Name = "Mechanical Agitation",
            Type = ForceType.Vortex,
            IsActive = true,
            VortexCenter = (0, 0, height / 2),
            VortexAxis = (0, 0, 1),
            VortexStrength = 2.0,   // 2 rad/s stirring
            VortexRadius = radius * 0.8
        };

        dataset.Forces.Add(agitation);

        // Air sparging for oxygen
        var airInlet = new BoundaryCondition
        {
            Name = "Air Sparger",
            Type = BoundaryType.Inlet,
            Location = BoundaryLocation.ZMin,
            Variable = BoundaryVariable.MassFlux,
            FluxValue = 0.001,  // kg/s air
            IsCompositional = true,
            InletComposition = new Dictionary<string, double>
            {
                { "O2", 0.21 },
                { "N2", 0.79 }
            },
            InletPhase = PhaseType.Gas,
            IsActive = true
        };

        dataset.BoundaryConditions.Add(airInlet);

        // Temperature control
        var heating = new ForceField
        {
            Name = "Temperature Control",
            Type = ForceType.Custom,
            IsActive = true,
            HeatSource = new HeatSourceProperties
            {
                Type = HeatSourceType.FixedTemperature,
                Temperature = 310.15,
                PowerDensity = 100.0
            }
        };

        dataset.Forces.Add(heating);

        dataset.SimulationParams.TotalTime = 86400.0;  // 24 hours
        dataset.SimulationParams.TimeStep = 60.0;      // 1 minute
        dataset.SimulationParams.EnableReactiveTransport = true;
        dataset.SimulationParams.EnableHeatTransfer = true;
        dataset.SimulationParams.EnableFlow = true;

        return dataset;
    }

    /// <summary>
    /// Create river sediment transport simulation
    /// </summary>
    public static PhysicoChemDataset CreateRiverSedimentTransport(double length, double width, double depth)
    {
        var dataset = new PhysicoChemDataset("River Sediment Transport",
            "Sediment erosion, transport, and deposition in river flow");

        var river = new ReactorDomain
        {
            Name = "River Channel",
            Geometry = new ReactorGeometry
            {
                Type = GeometryType.Box,
                Center = (length / 2, width / 2, depth / 2),
                Dimensions = (length, width, depth)
            },
            Material = new MaterialProperties
            {
                Density = 1000.0,
                Porosity = 1.0,
                ThermalConductivity = 0.6,
                SpecificHeat = 4186.0
            },
            InitialConditions = new InitialConditions
            {
                Temperature = 288.15,
                Pressure = 101325.0,
                LiquidSaturation = 1.0,
                Concentrations = new Dictionary<string, double>
                {
                    { "Sediment", 0.5 },    // Suspended sediment load
                    { "Clay", 0.1 },
                    { "Sand", 0.3 },
                    { "Silt", 0.1 }
                }
            }
        };

        dataset.AddDomain(river);

        // River flow
        var flow = new BoundaryCondition
        {
            Name = "Upstream Flow",
            Type = BoundaryType.Inlet,
            Location = BoundaryLocation.XMin,
            Variable = BoundaryVariable.VelocityX,
            Value = 1.5,  // 1.5 m/s river velocity
            IsCompositional = true,
            InletComposition = new Dictionary<string, double>
            {
                { "Sediment", 0.8 }  // High sediment load at inlet
            },
            IsActive = true
        };

        dataset.BoundaryConditions.Add(flow);

        // Sediment settling (multiple size classes)
        var sedimentSettling = new ForceField
        {
            Name = "Sediment Settling",
            Type = ForceType.Sedimentation,
            IsActive = true,
            Sedimentation = new SedimentationProperties
            {
                ParticleDensity = 2650.0,       // Quartz sand
                ParticleRadius = 1e-4,          // 100 microns (fine sand)
                FluidViscosity = 1.5e-3,        // Cold water
                UseDistribution = true,
                MeanRadius = 1e-4,
                StandardDeviation = 5e-5,
                VolumetricConcentration = 0.01,
                HinderedSettlingExponent = 4.65,
                EnableTurbulentDispersion = true,
                TurbulentDispersivity = 0.2,
                EnableFlocculation = true,
                FlocculationRate = 1e-6
            }
        };

        dataset.Forces.Add(sedimentSettling);

        // Turbulent flow
        var turbulence = new ForceField
        {
            Name = "River Turbulence",
            Type = ForceType.Turbulence,
            IsActive = true,
            Turbulence = new TurbulenceProperties
            {
                Model = TurbulenceModel.KEpsilon,
                TurbulentKineticEnergy = 0.1,       // High TKE in river
                TurbulentDissipationRate = 0.01,
                EddyViscosity = 0.01,
                UseWallFunctions = true,
                WallYPlus = 30.0
            }
        };

        dataset.Forces.Add(turbulence);

        // Gravity
        dataset.Forces.Add(new ForceField
        {
            Name = "Gravity",
            Type = ForceType.Gravity,
            GravityVector = (0, 0, -9.81)
        });

        dataset.SimulationParams.TotalTime = 3600.0;  // 1 hour
        dataset.SimulationParams.TimeStep = 1.0;
        dataset.SimulationParams.EnableReactiveTransport = true;
        dataset.SimulationParams.EnableFlow = true;

        return dataset;
    }

    /// <summary>
    /// Create ice formation and melting simulation
    /// </summary>
    public static PhysicoChemDataset CreateIceFormation(double width, double height, double depth)
    {
        var dataset = new PhysicoChemDataset("Ice Formation",
            "Phase change simulation with freezing and melting (Stefan problem)");

        var waterBody = new ReactorDomain
        {
            Name = "Water Volume",
            Geometry = new ReactorGeometry
            {
                Type = GeometryType.Box,
                Center = (width / 2, depth / 2, height / 2),
                Dimensions = (width, depth, height)
            },
            Material = new MaterialProperties
            {
                Density = 1000.0,
                Porosity = 1.0,
                ThermalConductivity = 0.6,
                SpecificHeat = 4186.0
            },
            InitialConditions = new InitialConditions
            {
                Temperature = 273.15,   // 0°C - at freezing point
                Pressure = 101325.0,
                LiquidSaturation = 1.0
            }
        };

        dataset.AddDomain(waterBody);

        // Freezing from top
        var freezing = new ForceField
        {
            Name = "Freezing Process",
            Type = ForceType.PhaseChange,
            IsActive = true,
            PhaseChange = new PhaseChangeProperties
            {
                Type = PhaseChangeType.Freezing,
                TransitionTemperature = 273.15,
                LatentHeat = 3.34e5,    // Latent heat of fusion for water
                TransitionPressure = 101325.0,
                EnableNucleation = true,
                NucleationSiteDensity = 1e5,
                ContactAngle = 45.0,
                IsMovingBoundary = true,
                InterfaceVelocity = 0.0,  // Calculated dynamically
                Supercooling = 2.0,       // 2 K supercooling
                MassTransferCoefficient = 1e-4
            }
        };

        dataset.Forces.Add(freezing);

        // Cold air above
        var coldBoundary = new BoundaryCondition
        {
            Name = "Cold Air",
            Type = BoundaryType.FixedValue,
            Location = BoundaryLocation.ZMax,
            Variable = BoundaryVariable.Temperature,
            Value = 253.15,  // -20°C
            IsActive = true
        };

        dataset.BoundaryConditions.Add(coldBoundary);

        // Convection in liquid water (before freezing)
        var buoyancy = new ForceField
        {
            Name = "Natural Convection",
            Type = ForceType.Buoyancy,
            IsActive = true
        };

        dataset.Forces.Add(buoyancy);

        dataset.SimulationParams.TotalTime = 7200.0;  // 2 hours
        dataset.SimulationParams.TimeStep = 1.0;
        dataset.SimulationParams.EnableHeatTransfer = true;
        dataset.SimulationParams.EnableFlow = true;

        return dataset;
    }

    /// <summary>
    /// Create chemical reactor with exothermic reaction
    /// </summary>
    public static PhysicoChemDataset CreateExothermicReactor(double radius, double height)
    {
        var dataset = new PhysicoChemDataset("Exothermic Chemical Reactor",
            "Chemical reaction with heat generation and temperature control");

        var reactor = new ReactorDomain
        {
            Name = "Batch Reactor",
            Geometry = new ReactorGeometry
            {
                Type = GeometryType.Cylinder,
                Center = (0, 0, height / 2),
                Radius = radius,
                Height = height
            },
            Material = new MaterialProperties
            {
                Density = 1200.0,
                Porosity = 1.0,
                ThermalConductivity = 0.5,
                SpecificHeat = 3500.0
            },
            InitialConditions = new InitialConditions
            {
                Temperature = 298.15,
                Pressure = 101325.0,
                LiquidSaturation = 1.0,
                Concentrations = new Dictionary<string, double>
                {
                    { "ReactantA", 5.0 },
                    { "ReactantB", 3.0 },
                    { "Product", 0.0 },
                    { "Catalyst", 0.01 }
                }
            }
        };

        dataset.AddDomain(reactor);

        // Exothermic reaction: A + B → Product (with catalyst)
        var reaction = new ForceField
        {
            Name = "Exothermic Synthesis",
            Type = ForceType.ChemicalReaction,
            IsActive = true,
            ChemicalReaction = new ChemicalReactionProperties
            {
                ReactionName = "A + B -> Product",
                Reactants = new List<ReactionSpecies>
                {
                    new ReactionSpecies { Name = "ReactantA", StoichiometricCoefficient = 1.0 },
                    new ReactionSpecies { Name = "ReactantB", StoichiometricCoefficient = 1.0 }
                },
                Products = new List<ReactionSpecies>
                {
                    new ReactionSpecies { Name = "Product", StoichiometricCoefficient = 1.0 }
                },
                Type = ReactionType.Arrhenius,
                PreExponentialFactor = 1e12,    // Large pre-exponential factor
                ActivationEnergy = 80000.0,     // 80 kJ/mol
                ReactionOrder = 2.0,             // Second order
                IsReversible = false,
                IsCatalyzed = true,
                CatalystName = "Catalyst",
                CatalyticEfficiency = 10.0,     // 10x rate enhancement
                IsSurfaceReaction = false
            }
        };

        dataset.Forces.Add(reaction);

        // Cooling jacket
        var cooling = new BoundaryCondition
        {
            Name = "Cooling Jacket",
            Type = BoundaryType.Convective,
            Location = BoundaryLocation.Custom,
            Variable = BoundaryVariable.Temperature,
            Value = 293.15,  // 20°C coolant
            IsActive = true
        };

        dataset.BoundaryConditions.Add(cooling);

        // Stirring
        var mixing = new ForceField
        {
            Name = "Impeller Mixing",
            Type = ForceType.Vortex,
            IsActive = true,
            VortexCenter = (0, 0, height / 2),
            VortexAxis = (0, 0, 1),
            VortexStrength = 5.0,
            VortexRadius = radius * 0.7
        };

        dataset.Forces.Add(mixing);

        dataset.SimulationParams.TotalTime = 3600.0;  // 1 hour
        dataset.SimulationParams.TimeStep = 1.0;
        dataset.SimulationParams.EnableReactiveTransport = true;
        dataset.SimulationParams.EnableHeatTransfer = true;
        dataset.SimulationParams.EnableFlow = true;

        return dataset;
    }
}
