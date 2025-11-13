// GeoscientistToolkit/Analysis/Geothermal/SubsurfaceGeothermalTools.cs

using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.Geothermal;

/// <summary>
/// Helper class for creating subsurface geothermal models from multiple boreholes
/// </summary>
public static class SubsurfaceGeothermalTools
{
    /// <summary>
    /// Creates debug test boreholes for deep geothermal reservoirs
    /// </summary>
    public static List<BoreholeDataset> CreateDebugDeepGeothermalBoreholes()
    {
        Logger.Log("Creating debug deep geothermal boreholes...");

        var boreholes = new List<BoreholeDataset>();

        // --- FIXED: Changed 'const' to 'double' for runtime calculation ---
        // Define a base geographic location for the test field (e.g., near Munich)
        double baseLat = 48.1;
        double baseLon = 11.5;
        // Approximate conversion from meters to degrees at this latitude
        double metersToLatDeg = 1.0 / 111111.0;
        double metersToLonDeg = 1.0 / (111111.0 * Math.Cos(baseLat * Math.PI / 180.0));
        // --------------------------------------------------------------------

        // Borehole 1: North position, higher elevation
        var bh1 = CreateDeepGeothermalBorehole(
            "GTW-01",
            new Vector2(1000, 1500),
            420.0f, // elevation in meters
            3500.0f,  // depth in meters
            baseLat + 1500 * metersToLatDeg,
            baseLon + 1000 * metersToLonDeg
        );
        boreholes.Add(bh1);

        // Borehole 2: Central position, medium elevation
        var bh2 = CreateDeepGeothermalBorehole(
            "GTW-02",
            new Vector2(1100, 1400),
            410.0f,
            3600.0f,
            baseLat + 1400 * metersToLatDeg,
            baseLon + 1100 * metersToLonDeg
        );
        boreholes.Add(bh2);

        // Borehole 3: South position, lower elevation
        var bh3 = CreateDeepGeothermalBorehole(
            "GTW-03",
            new Vector2(1050, 1300),
            400.0f,
            3550.0f,
            baseLat + 1300 * metersToLatDeg,
            baseLon + 1050 * metersToLonDeg
        );
        boreholes.Add(bh3);

        // Borehole 4: East position
        var bh4 = CreateDeepGeothermalBorehole(
            "GTW-04",
            new Vector2(1200, 1400),
            415.0f,
            3580.0f,
            baseLat + 1400 * metersToLatDeg,
            baseLon + 1200 * metersToLonDeg
        );
        boreholes.Add(bh4);

        Logger.Log($"Created {boreholes.Count} debug geothermal boreholes");
        return boreholes;
    }

    /// <summary>
    /// Creates a single deep geothermal borehole with realistic stratigraphy
    /// </summary>
    private static BoreholeDataset CreateDeepGeothermalBorehole(
        string name,
        Vector2 coordinates,
        float elevation,
        float totalDepth,
        double latitude,
        double longitude)
    {
        var borehole = new BoreholeDataset(name, "")
        {
            WellName = name,
            Field = "Deep Geothermal Test Field",
            SurfaceCoordinates = coordinates,
            Elevation = elevation,
            TotalDepth = totalDepth,
            WellDiameter = 0.311f, // ~12" diameter
            WaterTableDepth = 15.0f
        };
        
        // --- FIXED: Assign Latitude and Longitude to the dataset's metadata ---
        borehole.DatasetMetadata.Latitude = latitude;
        borehole.DatasetMetadata.Longitude = longitude;
        // --------------------------------------------------------------------

        // Add some randomization to make boreholes slightly different
        var random = new Random(name.GetHashCode());
        float variationFactor = 0.8f + (float)random.NextDouble() * 0.4f; // Increased range: 0.8 to 1.2

        // Deep geothermal stratigraphy
        // Layer 1: Quaternary deposits (0-50m)
        var lithologyType1 = "Sandstone";
        var porosity1 = 0.25f + ((float)random.NextDouble() - 0.5f) * 0.1f; // +/- 5%
        var permeability1 = 1e-12f * (0.5f + (float)random.NextDouble()); // Wider range
        var thermalConductivity1 = 2.0f + ((float)random.NextDouble() - 0.5f) * 1.0f; // Wider range
        var density1 = GetDensityForLithology(lithologyType1);
        var specificHeat1 = GetSpecificHeatForLithology(lithologyType1);
        var thermalDiffusivity1 = thermalConductivity1 / (density1 * specificHeat1);
        borehole.LithologyUnits.Add(new LithologyUnit
        {
            Name = "Quaternary Alluvium",
            LithologyType = lithologyType1,
            DepthFrom = 0,
            DepthTo = 50 * variationFactor,
            Color = new Vector4(0.9f, 0.85f, 0.7f, 1.0f),
            Description = "Loose sediments",
            GrainSize = GetGrainSizeForLithology(lithologyType1),
            Parameters = new Dictionary<string, float>
            {
                ["Porosity"] = porosity1,
                ["Permeability"] = permeability1,
                ["Thermal Conductivity"] = thermalConductivity1,
                ["Density"] = density1,
                ["Specific Heat"] = specificHeat1,
                ["Thermal Diffusivity"] = thermalDiffusivity1
            }
        });

        // Layer 2: Tertiary sediments (50-400m)
        var lithologyType2 = "Shale";
        var porosity2 = 0.15f + ((float)random.NextDouble() - 0.5f) * 0.08f;
        var permeability2 = 1e-15f * (0.2f + (float)random.NextDouble() * 1.8f);
        var thermalConductivity2 = 1.8f + ((float)random.NextDouble() - 0.5f) * 0.8f;
        var density2 = GetDensityForLithology(lithologyType2);
        var specificHeat2 = GetSpecificHeatForLithology(lithologyType2);
        var thermalDiffusivity2 = thermalConductivity2 / (density2 * specificHeat2);
        borehole.LithologyUnits.Add(new LithologyUnit
        {
            Name = "Molasse Basin Fill",
            LithologyType = lithologyType2,
            DepthFrom = 50 * variationFactor,
            DepthTo = 400 * variationFactor,
            Color = new Vector4(0.6f, 0.5f, 0.4f, 1.0f),
            Description = "Tertiary sedimentary sequence",
            GrainSize = GetGrainSizeForLithology(lithologyType2),
            Parameters = new Dictionary<string, float>
            {
                ["Porosity"] = porosity2,
                ["Permeability"] = permeability2,
                ["Thermal Conductivity"] = thermalConductivity2,
                ["Density"] = density2,
                ["Specific Heat"] = specificHeat2,
                ["Thermal Diffusivity"] = thermalDiffusivity2
            }
        });

        // Layer 3: Upper Jurassic Limestone (400-1200m)
        var lithologyType3 = "Limestone";
        var porosity3 = 0.08f + ((float)random.NextDouble() - 0.5f) * 0.06f;
        var permeability3 = 1e-14f * (0.5f + (float)random.NextDouble());
        var thermalConductivity3 = 2.5f + ((float)random.NextDouble() - 0.5f) * 1.0f;
        var density3 = GetDensityForLithology(lithologyType3);
        var specificHeat3 = GetSpecificHeatForLithology(lithologyType3);
        var thermalDiffusivity3 = thermalConductivity3 / (density3 * specificHeat3);
        borehole.LithologyUnits.Add(new LithologyUnit
        {
            Name = "Malm Carbonate Platform",
            LithologyType = lithologyType3,
            DepthFrom = 400 * variationFactor,
            DepthTo = 1200 * variationFactor,
            Color = new Vector4(0.85f, 0.85f, 0.75f, 1.0f),
            Description = "Karstified limestone, cap rock",
            GrainSize = GetGrainSizeForLithology(lithologyType3),
            Parameters = new Dictionary<string, float>
            {
                ["Porosity"] = porosity3,
                ["Permeability"] = permeability3,
                ["Thermal Conductivity"] = thermalConductivity3,
                ["Density"] = density3,
                ["Specific Heat"] = specificHeat3,
                ["Thermal Diffusivity"] = thermalDiffusivity3
            }
        });

        // Layer 4: Middle Jurassic Sandstone (1200-2000m)
        var lithologyType4 = "Sandstone";
        var porosity4 = 0.18f + ((float)random.NextDouble() - 0.5f) * 0.1f;
        var permeability4 = 1e-13f * (0.6f + (float)random.NextDouble() * 0.8f);
        var thermalConductivity4 = 3.0f + ((float)random.NextDouble() - 0.5f) * 1.2f;
        var density4 = GetDensityForLithology(lithologyType4);
        var specificHeat4 = GetSpecificHeatForLithology(lithologyType4);
        var thermalDiffusivity4 = thermalConductivity4 / (density4 * specificHeat4);
        borehole.LithologyUnits.Add(new LithologyUnit
        {
            Name = "Dogger Sandstone",
            LithologyType = lithologyType4,
            DepthFrom = 1200 * variationFactor,
            DepthTo = 2000 * variationFactor,
            Color = new Vector4(0.8f, 0.7f, 0.5f, 1.0f),
            Description = "Porous sandstone",
            GrainSize = GetGrainSizeForLithology(lithologyType4),
            Parameters = new Dictionary<string, float>
            {
                ["Porosity"] = porosity4,
                ["Permeability"] = permeability4,
                ["Thermal Conductivity"] = thermalConductivity4,
                ["Density"] = density4,
                ["Specific Heat"] = specificHeat4,
                ["Thermal Diffusivity"] = thermalDiffusivity4
            }
        });

        // Layer 5: GEOTHERMAL RESERVOIR - Permo-Carboniferous fractured granite (2000-2800m)
        var lithologyType5 = "Basement";
        var porosity5 = 0.02f + ((float)random.NextDouble() - 0.5f) * 0.015f;
        var permeability5 = 1e-12f * (0.5f + (float)random.NextDouble() * 2.5f);
        var thermalConductivity5 = 3.5f + ((float)random.NextDouble() - 0.5f) * 1.5f;
        var density5 = GetDensityForLithology(lithologyType5);
        var specificHeat5 = GetSpecificHeatForLithology(lithologyType5);
        var thermalDiffusivity5 = thermalConductivity5 / (density5 * specificHeat5);
        borehole.LithologyUnits.Add(new LithologyUnit
        {
            Name = "Geothermal Reservoir - Fractured Crystalline Basement",
            LithologyType = lithologyType5,
            DepthFrom = 2000 * variationFactor,
            DepthTo = 2800 * variationFactor,
            Color = new Vector4(0.7f, 0.3f, 0.3f, 1.0f),
            Description = "Highly fractured granite with geothermal fluids - PRIMARY RESERVOIR",
            GrainSize = GetGrainSizeForLithology(lithologyType5),
            Parameters = new Dictionary<string, float>
            {
                ["Porosity"] = porosity5, // Low matrix porosity
                ["Permeability"] = permeability5, // High due to fractures
                ["Thermal Conductivity"] = thermalConductivity5,
                ["Density"] = density5,
                ["Specific Heat"] = specificHeat5,
                ["Thermal Diffusivity"] = thermalDiffusivity5
            }
        });

        // Layer 6: Deep crystalline basement (2800m - bottom)
        var lithologyType6 = "Basement";
        var porosity6 = 0.01f + ((float)random.NextDouble() - 0.5f) * 0.005f;
        var permeability6 = 1e-16f * (0.1f + (float)random.NextDouble());
        var thermalConductivity6 = 3.2f + ((float)random.NextDouble() - 0.5f) * 1.0f;
        var density6 = GetDensityForLithology(lithologyType6);
        var specificHeat6 = GetSpecificHeatForLithology(lithologyType6);
        var thermalDiffusivity6 = thermalConductivity6 / (density6 * specificHeat6);
        borehole.LithologyUnits.Add(new LithologyUnit
        {
            Name = "Lower Crystalline Basement",
            LithologyType = lithologyType6,
            DepthFrom = 2800 * variationFactor,
            DepthTo = totalDepth,
            Color = new Vector4(0.4f, 0.2f, 0.2f, 1.0f),
            Description = "Intact crystalline basement",
            GrainSize = GetGrainSizeForLithology(lithologyType6),
            Parameters = new Dictionary<string, float>
            {
                ["Porosity"] = porosity6,
                ["Permeability"] = permeability6,
                ["Thermal Conductivity"] = thermalConductivity6,
                ["Density"] = density6,
                ["Specific Heat"] = specificHeat6,
                ["Thermal Diffusivity"] = thermalDiffusivity6
            }
        });

        // Populate the parameter tracks from the lithology unit parameters
        GenerateParameterTracks(borehole);

        // Sync metadata to ensure all generic dataset properties are populated
        borehole.SyncMetadata();

        Logger.Log($"Created borehole {name} at ({coordinates.X}, {coordinates.Y}), elevation {elevation}m, depth {totalDepth}m");

        return borehole;
    }

    /// <summary>
    /// Populates the visible parameter tracks from the data stored in the lithology units.
    /// This is essential for the borehole viewer to display the log curves.
    /// </summary>
    private static void GenerateParameterTracks(BoreholeDataset borehole)
    {
        // Clear any existing points from the default tracks
        foreach (var track in borehole.ParameterTracks.Values)
        {
            track.Points.Clear();
        }

        // Add points for each lithology unit based on its parameters
        foreach (var unit in borehole.LithologyUnits)
        {
            foreach (var param in unit.Parameters)
            {
                if (borehole.ParameterTracks.TryGetValue(param.Key, out var track))
                {
                    float displayValue = param.Value;

                    // Apply necessary unit conversions for display
                    switch (param.Key)
                    {
                        case "Porosity":
                            displayValue *= 100f; // Convert fraction to percentage
                            break;
                        case "Permeability":
                            displayValue *= 1.013e15f; // Convert m² to millidarcies
                            break;
                    }

                    // Add points at the start and end of the unit to create a blocky log
                    track.Points.Add(new ParameterPoint
                    {
                        Depth = unit.DepthFrom,
                        Value = displayValue,
                        SourceDataset = "Generated"
                    });
                    track.Points.Add(new ParameterPoint
                    {
                        Depth = unit.DepthTo,
                        Value = displayValue,
                        SourceDataset = "Generated"
                    });
                }
            }
        }

        // Sort the points in each track by depth to ensure correct plotting
        foreach (var track in borehole.ParameterTracks.Values)
        {
            track.Points.Sort((a, b) => a.Depth.CompareTo(b.Depth));
        }
    }


    /// <summary>
    /// Run geothermal simulations on multiple boreholes
    /// </summary>
    public static Dictionary<string, GeothermalSimulationResults> RunSimulationsOnBoreholes(
        List<BoreholeDataset> boreholes,
        Action<string, float> progressCallback = null)
    {
        var results = new Dictionary<string, GeothermalSimulationResults>();

        int totalBoreholes = boreholes.Count;
        int processedBoreholes = 0;

        foreach (var borehole in boreholes)
        {
            Logger.Log($"Running simulation on borehole {borehole.WellName}...");

            try
            {
                // Create simulation options
                var options = new GeothermalSimulationOptions
                {
                    BoreholeDataset = borehole
                };
                options.SetDefaultValues();

                // Configure for deep geothermal
                options.HeatExchangerType = HeatExchangerType.UTube;
                options.SimulationTime = 30 * 365.25 * 24 * 3600; // 30 years
                options.FluidMassFlowRate = 15.0; // Corresponds to L/s for water
                options.SurfaceTemperature = 285.15; // 12°C
                options.AverageGeothermalGradient = 0.035f; // 35°C/km
                options.GroutThermalConductivity = 2.0f;

                // Create and run solver
                var mesh = GeothermalMeshGenerator.GenerateCylindricalMesh(borehole, options);
                var solver = new GeothermalSimulationSolver(options, mesh, null, CancellationToken.None);
                var result = solver.RunSimulationAsync().Result;

                results[borehole.WellName] = result;

                processedBoreholes++;
                float progress = (float)processedBoreholes / totalBoreholes;
                progressCallback?.Invoke($"Simulated {borehole.WellName}", progress);

                Logger.Log($"Simulation completed for {borehole.WellName}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to run simulation on {borehole.WellName}: {ex.Message}");
            }
        }

        return results;
    }

    /// <summary>
    /// Create a 3D subsurface GIS dataset from boreholes and simulation results
    /// </summary>
    public static SubsurfaceGISDataset CreateSubsurfaceModel(
        List<BoreholeDataset> boreholes,
        Dictionary<string, GeothermalSimulationResults> simulationResults = null,
        GISRasterLayer heightmap = null,
        int resolutionX = 30,
        int resolutionY = 30,
        int resolutionZ = 50)
    {
        Logger.Log("Creating 3D subsurface geothermal model...");

        var subsurfaceModel = new SubsurfaceGISDataset("Subsurface Geothermal Model", "")
        {
            GridResolutionX = resolutionX,
            GridResolutionY = resolutionY,
            GridResolutionZ = resolutionZ,
            InterpolationRadius = 500.0f,
            Method = InterpolationMethod.InverseDistanceWeighted,
            IDWPower = 2.0f
        };

        // Build the model from boreholes
        subsurfaceModel.BuildFromBoreholes(boreholes, heightmap);

        // Add simulation results if available
        if (simulationResults != null && simulationResults.Count > 0)
        {
            subsurfaceModel.AddSimulationResults(boreholes, simulationResults);
        }

        Logger.Log($"Subsurface model created with {subsurfaceModel.VoxelGrid.Count} voxels");

        return subsurfaceModel;
    }
    
    private static string GetGrainSizeForLithology(string lithologyType)
    {
        return lithologyType switch
        {
            "Clay" => "Clay",
            "Shale" => "Clay",
            "Mudstone" => "Clay",
            "Siltstone" => "Silt",
            "Sand" => "Medium",
            "Sandstone" => "Fine",
            "Gravel" => "Gravel",
            "Conglomerate" => "Gravel",
            "Limestone" => "Fine",
            "Dolomite" => "Fine",
            "Granite" => "Coarse",
            "Basement" => "Coarse",
            "Basalt" => "Very Fine",
            "Soil" => "Fine",
            _ => "Medium"
        };
    }

    private static float GetDensityForLithology(string lithologyType)
    {
        return lithologyType switch
        {
            "Soil" => 1800f,
            "Clay" => 1900f,
            "Shale" => 2400f,
            "Mudstone" => 2300f,
            "Siltstone" => 2350f,
            "Sand" => 2650f,
            "Sandstone" => 2500f,
            "Gravel" => 2700f,
            "Conglomerate" => 2600f,
            "Limestone" => 2700f,
            "Dolomite" => 2850f,
            "Granite" => 2750f,
            "Basement" => 2750f,
            "Basalt" => 2900f,
            _ => 2500f
        };
    }

    private static float GetSpecificHeatForLithology(string lithologyType)
    {
        return lithologyType switch
        {
            "Soil" => 1840f,
            "Clay" => 1380f,
            "Shale" => 900f,
            "Mudstone" => 950f,
            "Siltstone" => 920f,
            "Sand" => 830f,
            "Sandstone" => 920f,
            "Gravel" => 840f,
            "Conglomerate" => 880f,
            "Limestone" => 810f,
            "Dolomite" => 920f,
            "Granite" => 790f,
            "Basement" => 790f,
            "Basalt" => 840f,
            _ => 900f
        };
    }
}