// GeoscientistToolkit/UI/Borehole/BoreholeDebugTools.cs

using System.Numerics;
using GeoscientistToolkit.Data.Borehole;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Borehole;

/// <summary>
///     Debug tools for creating test boreholes with realistic parameters
/// </summary>
public static class BoreholeDebugTools
{
    private static bool _showDebugWindow;
    private static string _presetName = "Geothermal Test Site";
    private static int _selectedPreset;

    private static readonly string[] _presetNames = new[]
    {
        "Shallow Geothermal (Urban)",
        "Deep Geothermal (Sedimentary)",
        "EGS Test Site (Crystalline)",
        "Aquifer Thermal Storage",
        "Fractured Carbonate",
        "Multi-Aquifer System",
        "Volcanic Geothermal",
        "Stratificazione Umbro-Marchigiana",
        "Custom Configuration"
    };

    /// <summary>
    ///     Draw the debug tools UI
    /// </summary>
    public static void DrawDebugTools(BoreholeDataset borehole)
    {
        if (ImGui.Button("Generate Test Borehole")) _showDebugWindow = true;

        if (_showDebugWindow) ImGui.OpenPopup("Generate Test Borehole");

        if (ImGui.BeginPopupModal("Generate Test Borehole", ref _showDebugWindow, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Select a preset configuration:");
            ImGui.Combo("Preset", ref _selectedPreset, _presetNames, _presetNames.Length);

            ImGui.Separator();
            ImGui.TextWrapped(GetPresetDescription(_selectedPreset));
            ImGui.Separator();

            if (ImGui.Button("Generate", new Vector2(120, 0)))
            {
                GenerateTestBorehole(borehole, _selectedPreset);
                _showDebugWindow = false;
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0))) _showDebugWindow = false;

            ImGui.EndPopup();
        }
    }

    /// <summary>
    ///     Generate a test borehole from a preset
    /// </summary>
    public static void GenerateTestBorehole(BoreholeDataset borehole, int presetIndex)
    {
        switch (presetIndex)
        {
            case 0:
                GenerateShallowUrbanBorehole(borehole);
                break;
            case 1:
                GenerateDeepSedimentaryBorehole(borehole);
                break;
            case 2:
                GenerateEGSCrystallineBorehole(borehole);
                break;
            case 3:
                GenerateAquiferStorageBorehole(borehole);
                break;
            case 4:
                GenerateFracturedCarbonateBorehole(borehole);
                break;
            case 5:
                GenerateMultiAquiferBorehole(borehole);
                break;
            case 6:
                GenerateVolcanicBorehole(borehole);
                break;
            case 7:
                GenerateUmbroMarchigianaStratigraphy(borehole);
                break;
            case 8:
                GenerateCustomBorehole(borehole);
                break;
            default:
                GenerateShallowUrbanBorehole(borehole);
                break;
        }

        // Common post-processing
        borehole.ShowGrid = true;
        borehole.ShowLegend = false;
        borehole.TrackWidth = 150.0f;
        borehole.DepthScaleFactor = 2.0f;
    }

    /// <summary>
    ///     Fill an empty borehole with preset data when created from MainWindow
    /// </summary>
    public static void FillEmptyBorehole(BoreholeDataset borehole, string presetName = "default")
    {
        // Determine which preset to use based on the name
        var presetIndex = 0; // Default to shallow urban

        if (presetName.ToLower().Contains("deep"))
            presetIndex = 1;
        else if (presetName.ToLower().Contains("egs") || presetName.ToLower().Contains("crystalline"))
            presetIndex = 2;
        else if (presetName.ToLower().Contains("aquifer"))
            presetIndex = 3;
        else if (presetName.ToLower().Contains("test") || presetName.ToLower().Contains("debug"))
            presetIndex = 0; // Use shallow urban for testing

        GenerateTestBorehole(borehole, presetIndex);
    }

    private static string GetPresetDescription(int presetIndex)
    {
        return presetIndex switch
        {
            0 =>
                "100m urban borehole with soil, clay, sand, and bedrock layers. Suitable for ground source heat pump testing.",
            1 =>
                "2500m deep borehole through sedimentary basin with multiple aquifers. Good for deep geothermal exploration.",
            2 => "4500m Enhanced Geothermal System site with crystalline basement and fracture zones.",
            3 => "500m borehole optimized for aquifer thermal energy storage with confined aquifers.",
            4 => "1500m borehole through fractured carbonate formations with karst features.",
            5 => "800m borehole with multiple aquifer layers for complex groundwater flow modeling.",
            6 => "3000m volcanic geothermal site with high temperature gradients and altered zones.",
            7 =>
                "3200m complete Umbro-Marchigian stratigraphic sequence: includes Schlier, Marne a Fucoidi, Calcari Diasprigni, Corniola and all major formations from Marnoso-Arenacea to Burano Anhydrites.",
            8 => "Customizable configuration with user-defined layers and parameters.",
            _ => "Standard test configuration."
        };
    }

    private static void GenerateShallowUrbanBorehole(BoreholeDataset borehole)
    {
        borehole.WellName = "Urban-GSHP-01";
        borehole.Field = "City Center Test Site";
        borehole.TotalDepth = 100.0f;
        borehole.WellDiameter = 0.15f;
        borehole.Elevation = 250.0f;
        borehole.SurfaceCoordinates = new Vector2(500000, 4500000);
        borehole.WaterTableDepth = 5.0f;

        borehole.LithologyUnits.Clear();

        // Topsoil
        var topsoil = CreateLithologyUnit("Topsoil", "Soil", 0, 2,
            new Vector4(0.55f, 0.35f, 0.15f, 1.0f), "Fine", "Organic-rich topsoil");
        topsoil.Parameters["Porosity"] = 0.45f;
        topsoil.Parameters["Permeability"] = 1e-12f;
        topsoil.Parameters["ThermalConductivity"] = 1.2f;
        topsoil.Parameters["Density"] = 1800f;
        topsoil.Parameters["SpecificHeat"] = 1840f;
        borehole.LithologyUnits.Add(topsoil);

        // Clay layer
        var clay = CreateLithologyUnit("Clay Layer", "Clay", 2, 8,
            new Vector4(0.65f, 0.55f, 0.45f, 1.0f), "Clay", "Impermeable clay barrier");
        clay.Parameters["Porosity"] = 0.50f;
        clay.Parameters["Permeability"] = 1e-15f;
        clay.Parameters["ThermalConductivity"] = 1.5f;
        clay.Parameters["Density"] = 1900f;
        clay.Parameters["SpecificHeat"] = 1380f;
        clay.Parameters["Plasticity"] = 0.35f;
        borehole.LithologyUnits.Add(clay);

        // Sand layer (aquifer)
        var sand = CreateLithologyUnit("Sand Aquifer", "Sand", 8, 25,
            new Vector4(0.85f, 0.80f, 0.65f, 1.0f), "Medium", "Water-bearing sand layer");
        sand.Parameters["Porosity"] = 0.35f;
        sand.Parameters["Permeability"] = 1e-11f;
        sand.Parameters["ThermalConductivity"] = 2.5f;
        sand.Parameters["Density"] = 2650f;
        sand.Parameters["SpecificHeat"] = 830f;
        sand.Parameters["HydraulicConductivity"] = 1e-4f;
        sand.Parameters["WaterSaturation"] = 0.95f;
        borehole.LithologyUnits.Add(sand);

        // Sandy clay
        var sandyClay = CreateLithologyUnit("Sandy Clay", "Clay", 25, 35,
            new Vector4(0.70f, 0.60f, 0.50f, 1.0f), "Fine", "Mixed sand and clay");
        sandyClay.Parameters["Porosity"] = 0.40f;
        sandyClay.Parameters["Permeability"] = 1e-13f;
        sandyClay.Parameters["ThermalConductivity"] = 1.8f;
        sandyClay.Parameters["Density"] = 2100f;
        sandyClay.Parameters["SpecificHeat"] = 1100f;
        borehole.LithologyUnits.Add(sandyClay);

        // Gravel layer
        var gravel = CreateLithologyUnit("Gravel", "Conglomerate", 35, 42,
            new Vector4(0.65f, 0.65f, 0.60f, 1.0f), "Gravel", "Coarse gravel layer");
        gravel.Parameters["Porosity"] = 0.25f;
        gravel.Parameters["Permeability"] = 1e-9f;
        gravel.Parameters["ThermalConductivity"] = 2.8f;
        gravel.Parameters["Density"] = 2700f;
        gravel.Parameters["SpecificHeat"] = 840f;
        borehole.LithologyUnits.Add(gravel);

        // Weathered sandstone
        var weatheredSandstone = CreateLithologyUnit("Weathered Sandstone", "Sandstone", 42, 55,
            new Vector4(0.80f, 0.70f, 0.55f, 1.0f), "Fine", "Fractured and weathered");
        weatheredSandstone.Parameters["Porosity"] = 0.20f;
        weatheredSandstone.Parameters["Permeability"] = 1e-12f;
        weatheredSandstone.Parameters["ThermalConductivity"] = 2.3f;
        weatheredSandstone.Parameters["Density"] = 2400f;
        weatheredSandstone.Parameters["SpecificHeat"] = 920f;
        weatheredSandstone.Parameters["FractureFrequency"] = 0.5f;
        borehole.LithologyUnits.Add(weatheredSandstone);

        // Competent sandstone
        var sandstone = CreateLithologyUnit("Sandstone", "Sandstone", 55, 100,
            new Vector4(0.75f, 0.65f, 0.50f, 1.0f), "Medium", "Competent bedrock");
        sandstone.Parameters["Porosity"] = 0.15f;
        sandstone.Parameters["Permeability"] = 1e-14f;
        sandstone.Parameters["ThermalConductivity"] = 3.0f;
        sandstone.Parameters["Density"] = 2500f;
        sandstone.Parameters["SpecificHeat"] = 920f;
        sandstone.Parameters["CompressiveStrength"] = 85f;
        borehole.LithologyUnits.Add(sandstone);

        // Add parameter tracks with data
        GenerateParameterTracks(borehole);
    }

    private static void GenerateDeepSedimentaryBorehole(BoreholeDataset borehole)
    {
        borehole.WellName = "Deep-Geo-02";
        borehole.Field = "Sedimentary Basin Site";
        borehole.TotalDepth = 2500.0f;
        borehole.WellDiameter = 0.20f;
        borehole.Elevation = 150.0f;
        borehole.SurfaceCoordinates = new Vector2(600000, 4600000);
        borehole.WaterTableDepth = 15.0f;

        borehole.LithologyUnits.Clear();

        // Surface deposits
        AddUnit(borehole, "Quaternary Deposits", "Soil", 0, 20,
            new Vector4(0.60f, 0.50f, 0.40f, 1.0f), 0.40f, 1e-11f, 1.6f);

        // Upper shale
        AddUnit(borehole, "Upper Shale", "Shale", 20, 150,
            new Vector4(0.45f, 0.45f, 0.40f, 1.0f), 0.25f, 1e-16f, 2.1f);

        // First sandstone aquifer
        AddUnit(borehole, "Sandstone Aquifer 1", "Sandstone", 150, 350,
            new Vector4(0.80f, 0.75f, 0.60f, 1.0f), 0.22f, 1e-12f, 2.8f);

        // Middle shale seal
        AddUnit(borehole, "Middle Shale", "Shale", 350, 600,
            new Vector4(0.40f, 0.40f, 0.35f, 1.0f), 0.20f, 1e-17f, 2.2f);

        // Limestone layer
        AddUnit(borehole, "Limestone", "Limestone", 600, 900,
            new Vector4(0.85f, 0.85f, 0.80f, 1.0f), 0.10f, 1e-14f, 2.9f);

        // Second sandstone aquifer
        AddUnit(borehole, "Sandstone Aquifer 2", "Sandstone", 900, 1200,
            new Vector4(0.75f, 0.70f, 0.55f, 1.0f), 0.18f, 1e-11f, 3.0f);

        // Deep shale
        AddUnit(borehole, "Deep Shale", "Shale", 1200, 1500,
            new Vector4(0.35f, 0.35f, 0.30f, 1.0f), 0.15f, 1e-18f, 2.3f);

        // Dolomite
        AddUnit(borehole, "Dolomite", "Dolomite", 1500, 1800,
            new Vector4(0.80f, 0.75f, 0.70f, 1.0f), 0.08f, 1e-15f, 3.2f);

        // Deep sandstone reservoir
        AddUnit(borehole, "Deep Sandstone", "Sandstone", 1800, 2200,
            new Vector4(0.70f, 0.60f, 0.45f, 1.0f), 0.12f, 1e-13f, 3.3f);

        // Basement
        AddUnit(borehole, "Basement", "Granite", 2200, 2500,
            new Vector4(0.65f, 0.60f, 0.55f, 1.0f), 0.02f, 1e-18f, 3.5f);

        GenerateParameterTracks(borehole);
        AddFractures(borehole, new[] { 450f, 750f, 1100f, 1650f, 2100f });
    }

    private static void GenerateEGSCrystallineBorehole(BoreholeDataset borehole)
    {
        borehole.WellName = "EGS-Crystal-03";
        borehole.Field = "Enhanced Geothermal Site";
        borehole.TotalDepth = 4500.0f;
        borehole.WellDiameter = 0.25f;
        borehole.Elevation = 800.0f;
        borehole.SurfaceCoordinates = new Vector2(700000, 4700000);
        borehole.WaterTableDepth = 50.0f;

        borehole.LithologyUnits.Clear();

        // Sedimentary cover
        AddUnit(borehole, "Sedimentary Cover", "Sandstone", 0, 500,
            new Vector4(0.75f, 0.65f, 0.50f, 1.0f), 0.15f, 1e-13f, 2.5f);

        // Transition zone
        AddUnit(borehole, "Metamorphic Transition", "Mudstone", 500, 800,
            new Vector4(0.55f, 0.50f, 0.45f, 1.0f), 0.10f, 1e-15f, 2.8f);

        // Upper granite
        AddUnit(borehole, "Upper Granite", "Granite", 800, 1500,
            new Vector4(0.70f, 0.65f, 0.60f, 1.0f), 0.03f, 1e-16f, 3.0f);

        // Fracture zone 1
        AddUnit(borehole, "Fracture Zone 1", "Granite", 1500, 1700,
            new Vector4(0.60f, 0.55f, 0.50f, 1.0f), 0.08f, 1e-11f, 2.7f);

        // Middle granite
        AddUnit(borehole, "Middle Granite", "Granite", 1700, 2800,
            new Vector4(0.68f, 0.63f, 0.58f, 1.0f), 0.02f, 1e-17f, 3.2f);

        // Fracture zone 2
        AddUnit(borehole, "Fracture Zone 2", "Granite", 2800, 3000,
            new Vector4(0.58f, 0.53f, 0.48f, 1.0f), 0.10f, 1e-10f, 2.6f);

        // Deep granite
        AddUnit(borehole, "Deep Granite", "Granite", 3000, 4000,
            new Vector4(0.65f, 0.60f, 0.55f, 1.0f), 0.01f, 1e-18f, 3.5f);

        // Target zone (stimulated)
        AddUnit(borehole, "Target Zone", "Granite", 4000, 4500,
            new Vector4(0.55f, 0.50f, 0.45f, 1.0f), 0.05f, 1e-12f, 3.3f);

        GenerateParameterTracks(borehole);
        AddFractures(borehole, new[] { 1600f, 2900f, 4100f, 4200f, 4300f, 4400f });
    }

    private static void GenerateAquiferStorageBorehole(BoreholeDataset borehole)
    {
        borehole.WellName = "ATES-04";
        borehole.Field = "Aquifer Storage Site";
        borehole.TotalDepth = 500.0f;
        borehole.WellDiameter = 0.30f;
        borehole.Elevation = 25.0f;
        borehole.SurfaceCoordinates = new Vector2(400000, 4400000);
        borehole.WaterTableDepth = 3.0f;

        borehole.LithologyUnits.Clear();

        // Surface clay cap
        AddUnit(borehole, "Clay Cap", "Clay", 0, 50,
            new Vector4(0.60f, 0.50f, 0.40f, 1.0f), 0.45f, 1e-16f, 1.4f);

        // Upper aquifer
        AddUnit(borehole, "Upper Aquifer", "Sand", 50, 150,
            new Vector4(0.85f, 0.80f, 0.65f, 1.0f), 0.38f, 1e-10f, 2.8f);

        // Aquitard
        AddUnit(borehole, "Aquitard", "Clay", 150, 200,
            new Vector4(0.55f, 0.45f, 0.35f, 1.0f), 0.40f, 1e-17f, 1.6f);

        // Target storage aquifer
        AddUnit(borehole, "Storage Aquifer", "Sand", 200, 350,
            new Vector4(0.80f, 0.75f, 0.60f, 1.0f), 0.35f, 1e-9f, 3.0f);

        // Lower confining layer
        AddUnit(borehole, "Lower Aquitard", "Clay", 350, 400,
            new Vector4(0.50f, 0.40f, 0.30f, 1.0f), 0.38f, 1e-18f, 1.8f);

        // Bedrock
        AddUnit(borehole, "Bedrock", "Limestone", 400, 500,
            new Vector4(0.75f, 0.75f, 0.70f, 1.0f), 0.05f, 1e-15f, 3.1f);

        GenerateParameterTracks(borehole);
    }

    private static void GenerateFracturedCarbonateBorehole(BoreholeDataset borehole)
    {
        borehole.WellName = "Carbonate-05";
        borehole.Field = "Karst Geothermal Site";
        borehole.TotalDepth = 1500.0f;
        borehole.WellDiameter = 0.22f;
        borehole.Elevation = 450.0f;
        borehole.SurfaceCoordinates = new Vector2(550000, 4550000);
        borehole.WaterTableDepth = 25.0f;

        borehole.LithologyUnits.Clear();

        // Soil and weathered zone
        AddUnit(borehole, "Soil/Weathered", "Soil", 0, 30,
            new Vector4(0.55f, 0.45f, 0.35f, 1.0f), 0.35f, 1e-12f, 1.5f);

        // Upper limestone
        AddUnit(borehole, "Upper Limestone", "Limestone", 30, 300,
            new Vector4(0.85f, 0.85f, 0.80f, 1.0f), 0.15f, 1e-13f, 2.8f);

        // Karst zone 1
        AddUnit(borehole, "Karst Zone 1", "Limestone", 300, 400,
            new Vector4(0.75f, 0.75f, 0.70f, 1.0f), 0.25f, 1e-8f, 2.2f);

        // Dolomite layer
        AddUnit(borehole, "Dolomite", "Dolomite", 400, 700,
            new Vector4(0.80f, 0.75f, 0.70f, 1.0f), 0.10f, 1e-14f, 3.0f);

        // Karst zone 2
        AddUnit(borehole, "Karst Zone 2", "Limestone", 700, 800,
            new Vector4(0.70f, 0.70f, 0.65f, 1.0f), 0.30f, 1e-7f, 2.0f);

        // Lower limestone
        AddUnit(borehole, "Lower Limestone", "Limestone", 800, 1200,
            new Vector4(0.82f, 0.82f, 0.77f, 1.0f), 0.08f, 1e-15f, 3.1f);

        // Fractured dolomite
        AddUnit(borehole, "Fractured Dolomite", "Dolomite", 1200, 1350,
            new Vector4(0.75f, 0.70f, 0.65f, 1.0f), 0.12f, 1e-11f, 2.9f);

        // Basement
        AddUnit(borehole, "Basement", "Granite", 1350, 1500,
            new Vector4(0.65f, 0.60f, 0.55f, 1.0f), 0.03f, 1e-17f, 3.3f);

        GenerateParameterTracks(borehole);
        AddFractures(borehole, new[] { 350f, 450f, 750f, 950f, 1250f });
    }

    private static void GenerateMultiAquiferBorehole(BoreholeDataset borehole)
    {
        borehole.WellName = "Multi-Aquifer-06";
        borehole.Field = "Layered Aquifer System";
        borehole.TotalDepth = 800.0f;
        borehole.WellDiameter = 0.18f;
        borehole.Elevation = 120.0f;
        borehole.SurfaceCoordinates = new Vector2(480000, 4480000);
        borehole.WaterTableDepth = 8.0f;

        borehole.LithologyUnits.Clear();

        // Vadose zone
        AddUnit(borehole, "Vadose Zone", "Siltstone", 0, 8,
            new Vector4(0.70f, 0.65f, 0.55f, 1.0f), 0.30f, 1e-13f, 1.7f);

        // Perched aquifer
        AddUnit(borehole, "Perched Aquifer", "Sand", 8, 35,
            new Vector4(0.85f, 0.80f, 0.65f, 1.0f), 0.35f, 1e-11f, 2.4f);

        // Aquitard 1
        AddUnit(borehole, "Aquitard 1", "Clay", 35, 80,
            new Vector4(0.55f, 0.45f, 0.35f, 1.0f), 0.42f, 1e-16f, 1.5f);

        // Confined aquifer 1
        AddUnit(borehole, "Confined Aquifer 1", "Sand", 80, 180,
            new Vector4(0.80f, 0.75f, 0.60f, 1.0f), 0.32f, 1e-10f, 2.8f);

        // Aquitard 2
        AddUnit(borehole, "Aquitard 2", "Mudstone", 180, 250,
            new Vector4(0.50f, 0.45f, 0.40f, 1.0f), 0.35f, 1e-15f, 1.8f);

        // Confined aquifer 2
        AddUnit(borehole, "Confined Aquifer 2", "Gravel", 250, 380,
            new Vector4(0.75f, 0.70f, 0.65f, 1.0f), 0.28f, 1e-9f, 3.2f);

        // Aquitard 3
        AddUnit(borehole, "Aquitard 3", "Shale", 380, 450,
            new Vector4(0.45f, 0.40f, 0.35f, 1.0f), 0.25f, 1e-17f, 2.0f);

        // Confined aquifer 3
        AddUnit(borehole, "Confined Aquifer 3", "Sandstone", 450, 600,
            new Vector4(0.75f, 0.65f, 0.50f, 1.0f), 0.18f, 1e-12f, 3.0f);

        // Lower confining unit
        AddUnit(borehole, "Lower Confining", "Clay", 600, 680,
            new Vector4(0.48f, 0.38f, 0.28f, 1.0f), 0.40f, 1e-18f, 1.9f);

        // Deep aquifer
        AddUnit(borehole, "Deep Aquifer", "Limestone", 680, 800,
            new Vector4(0.82f, 0.82f, 0.77f, 1.0f), 0.12f, 1e-13f, 3.1f);

        GenerateParameterTracks(borehole);
    }

    private static void GenerateVolcanicBorehole(BoreholeDataset borehole)
    {
        borehole.WellName = "Volcanic-07";
        borehole.Field = "Volcanic Geothermal Field";
        borehole.TotalDepth = 3000.0f;
        borehole.WellDiameter = 0.28f;
        borehole.Elevation = 1200.0f;
        borehole.SurfaceCoordinates = new Vector2(650000, 4650000);
        borehole.WaterTableDepth = 100.0f;

        borehole.LithologyUnits.Clear();

        // Volcanic ash/tuff
        AddUnit(borehole, "Volcanic Ash", "Mudstone", 0, 200,
            new Vector4(0.75f, 0.70f, 0.65f, 1.0f), 0.40f, 1e-12f, 1.2f);

        // Andesite flow 1
        AddUnit(borehole, "Andesite Flow 1", "Basalt", 200, 500,
            new Vector4(0.45f, 0.40f, 0.35f, 1.0f), 0.10f, 1e-14f, 1.8f);

        // Altered zone 1
        AddUnit(borehole, "Altered Zone 1", "Clay", 500, 650,
            new Vector4(0.85f, 0.75f, 0.60f, 1.0f), 0.35f, 1e-13f, 1.5f);

        // Basalt flow
        AddUnit(borehole, "Basalt Flow", "Basalt", 650, 1000,
            new Vector4(0.35f, 0.30f, 0.25f, 1.0f), 0.08f, 1e-15f, 2.1f);

        // Fractured andesite
        AddUnit(borehole, "Fractured Andesite", "Basalt", 1000, 1300,
            new Vector4(0.50f, 0.45f, 0.40f, 1.0f), 0.15f, 1e-11f, 1.9f);

        // Altered zone 2
        AddUnit(borehole, "Altered Zone 2", "Clay", 1300, 1500,
            new Vector4(0.80f, 0.70f, 0.55f, 1.0f), 0.30f, 1e-12f, 1.6f);

        // Dacite intrusion
        AddUnit(borehole, "Dacite Intrusion", "Granite", 1500, 2000,
            new Vector4(0.70f, 0.65f, 0.60f, 1.0f), 0.05f, 1e-16f, 2.8f);

        // Deep altered zone
        AddUnit(borehole, "Deep Altered", "Clay", 2000, 2300,
            new Vector4(0.75f, 0.65f, 0.50f, 1.0f), 0.25f, 1e-11f, 1.7f);

        // Reservoir zone
        AddUnit(borehole, "Reservoir Zone", "Basalt", 2300, 2700,
            new Vector4(0.40f, 0.35f, 0.30f, 1.0f), 0.20f, 1e-10f, 2.2f);

        // Deep intrusive
        AddUnit(borehole, "Deep Intrusive", "Granite", 2700, 3000,
            new Vector4(0.65f, 0.60f, 0.55f, 1.0f), 0.03f, 1e-17f, 3.0f);

        GenerateParameterTracks(borehole);
        AddFractures(borehole, new[] { 1150f, 1450f, 2150f, 2400f, 2500f, 2600f });
    }

    /// <summary>
    ///     Generate a borehole with complete Umbro-Marchigian stratigraphic sequence
    ///     Reference: Centamore et al. (1971), Geological Map of Central Apennines; Selli (1954), Il Bacino del Metauro
    ///     Includes: Marnoso-Arenacea, Schlier, Bisciaro, Scaglia Cinerea, Scaglia Variegata, Scaglia Rossa,
    ///     Scaglia Bianca, Marne a Fucoidi, Maiolica, Calcari Diasprigni, Rosso Ammonitico, Corniola,
    ///     Calcare Massiccio, Anidriti di Burano
    /// </summary>
    private static void GenerateUmbroMarchigianaStratigraphy(BoreholeDataset borehole)
    {
        borehole.WellName = "Umbria-Marche-01";
        borehole.Field = "Appennino Umbro-Marchigiano";
        borehole.TotalDepth = 3200.0f;
        borehole.WellDiameter = 0.22f; // 8.5 inch
        borehole.Elevation = 450.0f; // Typical Apennine elevation
        borehole.SurfaceCoordinates = new Vector2(350000, 4800000); // UTM zone 33N (central Italy)
        borehole.WaterTableDepth = 15.0f;

        borehole.LithologyUnits.Clear();

        // Quaternary cover (0-50m)
        var quaternary = CreateLithologyUnit("Quaternario - Depositi continentali", "Soil", 0, 50,
            new Vector4(0.70f, 0.60f, 0.45f, 1.0f), "Mixed",
            "Quaternary continental deposits: colluvium, eluvium, and weathered bedrock");
        quaternary.Parameters["Porosity"] = 0.35f;
        quaternary.Parameters["Permeability"] = 1e-12f;
        quaternary.Parameters["ThermalConductivity"] = 1.8f;
        quaternary.Parameters["Density"] = 2000f;
        quaternary.Parameters["SpecificHeat"] = 1200f;
        borehole.LithologyUnits.Add(quaternary);

        // Marnoso-Arenacea Formation (Miocene) - Turbidites (50-500m)
        // Reference: Ricci Lucchi (1975), Depositional cycles in two turbidite formations
        var marnosoArenacea = CreateLithologyUnit("Formazione Marnoso-Arenacea", "Sandstone", 50, 500,
            new Vector4(0.72f, 0.68f, 0.55f, 1.0f), "Fine to Medium",
            "Miocene turbidite sequence: alternating sandstones and marls, Bouma sequences, syn-orogenic deposit");
        marnosoArenacea.Parameters["Porosity"] = 0.18f;
        marnosoArenacea.Parameters["Permeability"] = 5e-14f;
        marnosoArenacea.Parameters["ThermalConductivity"] = 2.3f;
        marnosoArenacea.Parameters["Density"] = 2450f;
        marnosoArenacea.Parameters["SpecificHeat"] = 920f;
        marnosoArenacea.Parameters["CompressiveStrength"] = 45f;
        borehole.LithologyUnits.Add(marnosoArenacea);

        // Schlier Formation (Middle-Upper Miocene) (500-800m)
        // Reference: Selli (1954), Il Bacino del Metauro
        var schlier = CreateLithologyUnit("Schlier", "Marl", 500, 800,
            new Vector4(0.70f, 0.73f, 0.78f, 1.0f), "Fine",
            "Middle-Upper Miocene hemipelagic marls: gray-bluish marly sequence, highly bioturbated");
        schlier.Parameters["Porosity"] = 0.15f;
        schlier.Parameters["Permeability"] = 1e-15f;
        schlier.Parameters["ThermalConductivity"] = 2.0f;
        schlier.Parameters["Density"] = 2480f;
        schlier.Parameters["SpecificHeat"] = 890f;
        schlier.Parameters["CompressiveStrength"] = 35f;
        borehole.LithologyUnits.Add(schlier);

        // Bisciaro Formation (Lower Miocene) (800-950m)
        // Reference: Monaco et al. (2005), Bisciaro stratigraphic marker with Livello Raffaello
        var bisciaro = CreateLithologyUnit("Formazione del Bisciaro", "Marl", 800, 950,
            new Vector4(0.75f, 0.72f, 0.65f, 1.0f), "Fine",
            "Lower Miocene marly limestones with chert, rich in diatoms and radiolarians, volcanic ash layers (Livello Raffaello, Livello Piero della Francesca)");
        bisciaro.Parameters["Porosity"] = 0.10f;
        bisciaro.Parameters["Permeability"] = 5e-16f;
        bisciaro.Parameters["ThermalConductivity"] = 2.0f;
        bisciaro.Parameters["Density"] = 2520f;
        bisciaro.Parameters["SpecificHeat"] = 870f;
        bisciaro.Parameters["CompressiveStrength"] = 55f;
        borehole.LithologyUnits.Add(bisciaro);

        // Scaglia Cinerea (Oligocene-Lower Miocene) (950-1100m)
        // Reference: Coccioni et al. (1988), The Scaglia Cinerea
        var scagliaCinerea = CreateLithologyUnit("Scaglia Cinerea", "Marl", 950, 1100,
            new Vector4(0.68f, 0.65f, 0.60f, 1.0f), "Fine",
            "Oligocene-Lower Miocene gray marly limestones, lower impermeability horizon");
        scagliaCinerea.Parameters["Porosity"] = 0.12f;
        scagliaCinerea.Parameters["Permeability"] = 1e-15f;
        scagliaCinerea.Parameters["ThermalConductivity"] = 2.1f;
        scagliaCinerea.Parameters["Density"] = 2500f;
        scagliaCinerea.Parameters["SpecificHeat"] = 880f;
        scagliaCinerea.Parameters["CompressiveStrength"] = 50f;
        borehole.LithologyUnits.Add(scagliaCinerea);

        // Scaglia Variegata (Middle-Upper Eocene) (1100-1150m)
        // Reference: Montanari et al. (1985), Massignano section as GSSP for Eocene-Oligocene boundary
        var scagliaVariegata = CreateLithologyUnit("Scaglia Variegata", "Marl", 1100, 1150,
            new Vector4(0.75f, 0.60f, 0.65f, 1.0f), "Fine",
            "Middle-Upper Eocene variegated marly limestones: alternating pink, red, gray-green colors, may contain Scaglia Rossa olistostromes");
        scagliaVariegata.Parameters["Porosity"] = 0.10f;
        scagliaVariegata.Parameters["Permeability"] = 8e-16f;
        scagliaVariegata.Parameters["ThermalConductivity"] = 2.2f;
        scagliaVariegata.Parameters["Density"] = 2530f;
        scagliaVariegata.Parameters["SpecificHeat"] = 870f;
        scagliaVariegata.Parameters["CompressiveStrength"] = 55f;
        borehole.LithologyUnits.Add(scagliaVariegata);

        // Scaglia Rossa (Upper Cretaceous-Paleogene) (1150-1550m)
        // Reference: Arthur & Fischer (1977), Upper Cretaceous-Paleocene magnetic stratigraphy
        var scagliaRossa = CreateLithologyUnit("Scaglia Rossa", "Limestone", 1150, 1550,
            new Vector4(0.85f, 0.65f, 0.60f, 1.0f), "Fine",
            "Upper Cretaceous-Paleogene pink to red pelagic limestones with marly interbeds, contains Livello Bonarelli (OAE 2, K-T boundary)");
        scagliaRossa.Parameters["Porosity"] = 0.08f;
        scagliaRossa.Parameters["Permeability"] = 1e-16f;
        scagliaRossa.Parameters["ThermalConductivity"] = 2.5f;
        scagliaRossa.Parameters["Density"] = 2650f;
        scagliaRossa.Parameters["SpecificHeat"] = 840f;
        scagliaRossa.Parameters["CompressiveStrength"] = 80f;
        scagliaRossa.Parameters["FractureFrequency"] = 0.5f;
        borehole.LithologyUnits.Add(scagliaRossa);

        // Scaglia Bianca (Lower-Upper Cretaceous) (1550-1700m)
        // Reference: Premoli Silva & Sliter (1999), Cretaceous paleoceanography
        var scagliaBianca = CreateLithologyUnit("Scaglia Bianca", "Limestone", 1550, 1700,
            new Vector4(0.92f, 0.90f, 0.88f, 1.0f), "Fine",
            "Lower to Upper Cretaceous white to light gray pelagic limestones with chert, contains Livello Bonarelli at top");
        scagliaBianca.Parameters["Porosity"] = 0.06f;
        scagliaBianca.Parameters["Permeability"] = 5e-17f;
        scagliaBianca.Parameters["ThermalConductivity"] = 2.7f;
        scagliaBianca.Parameters["Density"] = 2680f;
        scagliaBianca.Parameters["SpecificHeat"] = 830f;
        scagliaBianca.Parameters["CompressiveStrength"] = 90f;
        scagliaBianca.Parameters["FractureFrequency"] = 0.4f;
        borehole.LithologyUnits.Add(scagliaBianca);

        // Marne a Fucoidi (Lower Cretaceous) (1700-1800m)
        // Reference: Coccioni et al. (1987), Scisti a Fucoidi biostratigraphy
        var marneFucoidi = CreateLithologyUnit("Marne a Fucoidi", "Marl", 1700, 1800,
            new Vector4(0.60f, 0.65f, 0.55f, 1.0f), "Fine",
            "Lower Cretaceous marls and marly limestones with organic-rich black shales: Livello Selli, Livello 113, Livello Urbino (OAE 1a)");
        marneFucoidi.Parameters["Porosity"] = 0.15f;
        marneFucoidi.Parameters["Permeability"] = 1e-16f;
        marneFucoidi.Parameters["ThermalConductivity"] = 1.9f;
        marneFucoidi.Parameters["Density"] = 2550f;
        marneFucoidi.Parameters["SpecificHeat"] = 860f;
        marneFucoidi.Parameters["CompressiveStrength"] = 40f;
        borehole.LithologyUnits.Add(marneFucoidi);

        // Maiolica Formation (Upper Jurassic-Lower Cretaceous) (1800-2100m)
        // Reference: Santantonio (1993), Maiolica stratigraphy
        var maiolica = CreateLithologyUnit("Formazione della Maiolica", "Limestone", 1800, 2100,
            new Vector4(0.95f, 0.93f, 0.90f, 1.0f), "Very Fine",
            "Upper Jurassic-Lower Cretaceous white micritic limestones with black chert nodules and layers");
        maiolica.Parameters["Porosity"] = 0.04f;
        maiolica.Parameters["Permeability"] = 1e-17f;
        maiolica.Parameters["ThermalConductivity"] = 2.8f;
        maiolica.Parameters["Density"] = 2700f;
        maiolica.Parameters["SpecificHeat"] = 820f;
        maiolica.Parameters["CompressiveStrength"] = 100f;
        maiolica.Parameters["FractureFrequency"] = 0.3f;
        borehole.LithologyUnits.Add(maiolica);

        // Calcari Diasprigni - Calcari a Saccocoma ed Aptici (Middle-Upper Jurassic) (2100-2200m)
        // Reference: Cresta et al. (1989), Jurassic stratigraphy
        var calcariDiasprigni = CreateLithologyUnit("Calcari Diasprigni - Calcari a Saccocoma", "Limestone", 2100, 2200,
            new Vector4(0.75f, 0.78f, 0.70f, 1.0f), "Fine",
            "Middle-Upper Jurassic cherty limestones with Saccocoma and Aptychi, polychrome chert (green, red, white)");
        calcariDiasprigni.Parameters["Porosity"] = 0.05f;
        calcariDiasprigni.Parameters["Permeability"] = 5e-18f;
        calcariDiasprigni.Parameters["ThermalConductivity"] = 2.6f;
        calcariDiasprigni.Parameters["Density"] = 2710f;
        calcariDiasprigni.Parameters["SpecificHeat"] = 815f;
        calcariDiasprigni.Parameters["CompressiveStrength"] = 95f;
        borehole.LithologyUnits.Add(calcariDiasprigni);

        // Rosso Ammonitico (Middle-Upper Jurassic) (2200-2220m)
        // Reference: Jenkyns (1974), Origin of red nodular limestones
        var rossoAmmonitico = CreateLithologyUnit("Rosso Ammonitico", "Limestone", 2200, 2220,
            new Vector4(0.80f, 0.45f, 0.40f, 1.0f), "Fine",
            "Middle-Upper Jurassic red nodular pelagic limestones rich in ammonites, condensed sequence with hardgrounds");
        rossoAmmonitico.Parameters["Porosity"] = 0.05f;
        rossoAmmonitico.Parameters["Permeability"] = 5e-18f;
        rossoAmmonitico.Parameters["ThermalConductivity"] = 2.6f;
        rossoAmmonitico.Parameters["Density"] = 2720f;
        rossoAmmonitico.Parameters["SpecificHeat"] = 810f;
        rossoAmmonitico.Parameters["CompressiveStrength"] = 110f;
        rossoAmmonitico.Parameters["FractureFrequency"] = 0.6f;
        borehole.LithologyUnits.Add(rossoAmmonitico);

        // Corniola Formation (Lower Jurassic) (2220-2300m)
        // Reference: Centamore & Micarelli (1991), Corniola pelagic facies
        var corniola = CreateLithologyUnit("Corniola", "Limestone", 2220, 2300,
            new Vector4(0.80f, 0.75f, 0.65f, 1.0f), "Fine to Medium",
            "Lower Jurassic gray-beige pelagic limestones with white/gray chert, marks drowning of Calcare Massiccio platform");
        corniola.Parameters["Porosity"] = 0.06f;
        corniola.Parameters["Permeability"] = 8e-18f;
        corniola.Parameters["ThermalConductivity"] = 2.7f;
        corniola.Parameters["Density"] = 2690f;
        corniola.Parameters["SpecificHeat"] = 825f;
        corniola.Parameters["CompressiveStrength"] = 105f;
        corniola.Parameters["FractureFrequency"] = 0.4f;
        borehole.LithologyUnits.Add(corniola);

        // Calcare Massiccio Formation (Lower Jurassic - Lias) (2300-2900m)
        // Reference: Colacicchi et al. (1975), Calcare Massiccio facies
        var calcareMassiccio = CreateLithologyUnit("Calcare Massiccio", "Limestone", 2300, 2900,
            new Vector4(0.88f, 0.86f, 0.82f, 1.0f), "Medium to Coarse",
            "Lower Jurassic massive carbonate platform limestones: grain-supported, oolitic and bioclastic facies, occasionally dolomitized");
        calcareMassiccio.Parameters["Porosity"] = 0.08f;
        calcareMassiccio.Parameters["Permeability"] = 3e-15f; // Higher due to fractures and karst
        calcareMassiccio.Parameters["ThermalConductivity"] = 2.9f;
        calcareMassiccio.Parameters["Density"] = 2690f;
        calcareMassiccio.Parameters["SpecificHeat"] = 825f;
        calcareMassiccio.Parameters["CompressiveStrength"] = 120f;
        calcareMassiccio.Parameters["FractureFrequency"] = 1.2f; // Important aquifer
        calcareMassiccio.Parameters["HydraulicConductivity"] = 1e-5f;
        calcareMassiccio.Parameters["WaterSaturation"] = 0.85f;
        borehole.LithologyUnits.Add(calcareMassiccio);

        // Anidriti di Burano Formation (Upper Triassic) (2900-3200m)
        // Reference: Martinis & Pieri (1964), Triassic evaporites as basal décollement
        var aniditiBurano = CreateLithologyUnit("Anidriti di Burano", "Evaporite", 2900, 3200,
            new Vector4(0.92f, 0.88f, 0.85f, 1.0f), "Crystalline",
            "Upper Triassic evaporitic sequence: anhydrite with dolomite and halite interbeds, basal décollement of Apennine thrust belt");
        aniditiBurano.Parameters["Porosity"] = 0.02f;
        aniditiBurano.Parameters["Permeability"] = 1e-19f; // Essentially impermeable
        aniditiBurano.Parameters["ThermalConductivity"] = 5.5f; // High for anhydrite
        aniditiBurano.Parameters["Density"] = 2900f;
        aniditiBurano.Parameters["SpecificHeat"] = 700f;
        aniditiBurano.Parameters["CompressiveStrength"] = 60f; // Lower, plastic behavior
        borehole.LithologyUnits.Add(aniditiBurano);

        GenerateParameterTracks(borehole);

        // Add realistic fracture zones: concentrated in key horizons and formation boundaries
        AddFractures(borehole, new[]
        {
            1100f, // Scaglia Variegata/Cinerea boundary
            1150f, // Top Scaglia Rossa (boundary with Scaglia Variegata)
            1350f, // Within Scaglia Rossa
            1550f, // Scaglia Rossa/Bianca boundary
            1700f, // Scaglia Bianca/Marne a Fucoidi boundary (Livello Bonarelli)
            1800f, // Marne a Fucoidi/Maiolica boundary
            2100f, // Maiolica/Calcari Diasprigni boundary
            2200f, // Calcari Diasprigni/Rosso Ammonitico boundary
            2220f, // Rosso Ammonitico/Corniola boundary
            2300f, // Corniola/Calcare Massiccio boundary (platform drowning)
            2500f, // Within Calcare Massiccio (karst)
            2700f, // Within Calcare Massiccio (karst)
            2900f // Calcare Massiccio/Anidriti boundary (main thrust contact)
        });
    }

    private static void GenerateCustomBorehole(BoreholeDataset borehole)
    {
        // Start with a simple configuration that users can modify
        borehole.WellName = "Custom-Test-08";
        borehole.Field = "Custom Configuration";
        borehole.TotalDepth = 200.0f;
        borehole.WellDiameter = 0.16f;
        borehole.Elevation = 100.0f;
        borehole.SurfaceCoordinates = new Vector2(500000, 4500000);
        borehole.WaterTableDepth = 10.0f;

        borehole.LithologyUnits.Clear();

        // Add basic layers
        AddUnit(borehole, "Layer 1", "Soil", 0, 10,
            new Vector4(0.60f, 0.50f, 0.40f, 1.0f), 0.40f, 1e-12f, 1.5f);

        AddUnit(borehole, "Layer 2", "Clay", 10, 30,
            new Vector4(0.55f, 0.45f, 0.35f, 1.0f), 0.45f, 1e-15f, 1.8f);

        AddUnit(borehole, "Layer 3", "Sand", 30, 60,
            new Vector4(0.80f, 0.75f, 0.60f, 1.0f), 0.35f, 1e-11f, 2.5f);

        AddUnit(borehole, "Layer 4", "Sandstone", 60, 120,
            new Vector4(0.75f, 0.65f, 0.50f, 1.0f), 0.20f, 1e-13f, 2.8f);

        AddUnit(borehole, "Layer 5", "Limestone", 120, 200,
            new Vector4(0.85f, 0.85f, 0.80f, 1.0f), 0.10f, 1e-14f, 3.0f);

        GenerateParameterTracks(borehole);
    }

    private static LithologyUnit CreateLithologyUnit(string name, string lithologyType,
        float depthFrom, float depthTo, Vector4 color, string grainSize, string description)
    {
        return new LithologyUnit
        {
            ID = Guid.NewGuid().ToString(),
            Name = name,
            LithologyType = lithologyType,
            DepthFrom = depthFrom,
            DepthTo = depthTo,
            Color = color,
            GrainSize = grainSize,
            Description = description,
            Parameters = new Dictionary<string, float>(),
            ParameterSources = new Dictionary<string, ParameterSource>()
        };
    }

    private static void AddUnit(BoreholeDataset borehole, string name, string lithologyType,
        float depthFrom, float depthTo, Vector4 color, float porosity, float permeability, float thermalCond)
    {
        var unit = CreateLithologyUnit(name, lithologyType, depthFrom, depthTo, color,
            GetGrainSizeForLithology(lithologyType), $"{lithologyType} formation");

        unit.Parameters["Porosity"] = porosity;
        unit.Parameters["Permeability"] = permeability;
        unit.Parameters["ThermalConductivity"] = thermalCond;
        unit.Parameters["Density"] = GetDensityForLithology(lithologyType);
        unit.Parameters["SpecificHeat"] = GetSpecificHeatForLithology(lithologyType);

        // Add additional parameters based on lithology type
        if (lithologyType == "Sand" || lithologyType == "Gravel")
        {
            unit.Parameters["HydraulicConductivity"] = permeability * 1e7f;
            unit.Parameters["WaterSaturation"] = Math.Min(1.0f, porosity * 2.5f);
        }

        if (lithologyType == "Clay" || lithologyType == "Shale")
        {
            unit.Parameters["Plasticity"] = 0.3f + porosity * 0.5f;
            unit.Parameters["SwellingIndex"] = porosity * 0.8f;
        }

        if (lithologyType == "Granite" || lithologyType == "Basalt")
        {
            unit.Parameters["CompressiveStrength"] = 150f - porosity * 1000f;
            unit.Parameters["FractureFrequency"] = porosity * 10f;
        }

        borehole.LithologyUnits.Add(unit);
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
            "Basalt" => 840f,
            _ => 900f
        };
    }

    private static void GenerateParameterTracks(BoreholeDataset borehole)
    {
        // Clear existing tracks
        borehole.ParameterTracks.Clear();

        // Temperature track
        var tempTrack = new ParameterTrack
        {
            Name = "Temperature",
            Unit = "°C",
            MinValue = 10f,
            MaxValue = 150f,
            IsLogarithmic = false,
            Color = new Vector4(1.0f, 0.2f, 0.2f, 1.0f),
            IsVisible = true,
            Points = GenerateTemperatureProfile(borehole)
        };
        borehole.ParameterTracks["Temperature"] = tempTrack;

        // Porosity track
        var porosityTrack = new ParameterTrack
        {
            Name = "Porosity",
            Unit = "%",
            MinValue = 0f,
            MaxValue = 50f,
            IsLogarithmic = false,
            Color = new Vector4(0.2f, 0.6f, 1.0f, 1.0f),
            IsVisible = true,
            Points = GeneratePorosityProfile(borehole)
        };
        borehole.ParameterTracks["Porosity"] = porosityTrack;

        // Permeability track
        var permTrack = new ParameterTrack
        {
            Name = "Permeability",
            Unit = "mD",
            MinValue = 0.001f,
            MaxValue = 10000f,
            IsLogarithmic = true,
            Color = new Vector4(0.2f, 0.8f, 0.2f, 1.0f),
            IsVisible = true,
            Points = GeneratePermeabilityProfile(borehole)
        };
        borehole.ParameterTracks["Permeability"] = permTrack;

        // Thermal conductivity track
        var thermalTrack = new ParameterTrack
        {
            Name = "Thermal Conductivity",
            Unit = "W/m·K",
            MinValue = 0.5f,
            MaxValue = 4.0f,
            IsLogarithmic = false,
            Color = new Vector4(0.8f, 0.4f, 0.0f, 1.0f),
            IsVisible = true,
            Points = GenerateThermalProfile(borehole)
        };
        borehole.ParameterTracks["ThermalConductivity"] = thermalTrack;

        // Gamma ray track (synthetic)
        var gammaTrack = new ParameterTrack
        {
            Name = "Gamma Ray",
            Unit = "API",
            MinValue = 0f,
            MaxValue = 150f,
            IsLogarithmic = false,
            Color = new Vector4(0.6f, 0.3f, 0.9f, 1.0f),
            IsVisible = false,
            Points = GenerateGammaRayProfile(borehole)
        };
        borehole.ParameterTracks["GammaRay"] = gammaTrack;
    }

    private static List<ParameterPoint> GenerateTemperatureProfile(BoreholeDataset borehole)
    {
        var points = new List<ParameterPoint>();
        var gradient = 0.03f; // 30Â°C/km
        var surfaceTemp = 15.0f;

        for (float depth = 0; depth <= borehole.TotalDepth; depth += 5.0f)
        {
            var temp = surfaceTemp + gradient * depth;

            // Add some variation based on lithology
            var unit = borehole.LithologyUnits.FirstOrDefault(u => depth >= u.DepthFrom && depth <= u.DepthTo);
            if (unit != null)
                if (unit.Parameters.TryGetValue("ThermalConductivity", out var k))
                    temp += (k - 2.5f) * 2.0f; // Adjust based on conductivity

            points.Add(new ParameterPoint
            {
                Depth = depth,
                Value = temp,
                SourceDataset = "Synthetic"
            });
        }

        return points;
    }

    private static List<ParameterPoint> GeneratePorosityProfile(BoreholeDataset borehole)
    {
        var points = new List<ParameterPoint>();

        foreach (var unit in borehole.LithologyUnits)
            if (unit.Parameters.TryGetValue("Porosity", out var porosity))
                // Add points at unit boundaries with some variation
                for (var depth = unit.DepthFrom; depth <= unit.DepthTo; depth += 2.0f)
                {
                    var variation = (float)(Random.Shared.NextDouble() * 0.1 - 0.05); // Â±5%
                    var value = Math.Max(0, Math.Min(0.5f, porosity + variation)) * 100f; // Convert to percentage

                    points.Add(new ParameterPoint
                    {
                        Depth = depth,
                        Value = value,
                        SourceDataset = "Synthetic"
                    });
                }

        return points;
    }

    private static List<ParameterPoint> GeneratePermeabilityProfile(BoreholeDataset borehole)
    {
        var points = new List<ParameterPoint>();

        foreach (var unit in borehole.LithologyUnits)
            if (unit.Parameters.TryGetValue("Permeability", out var perm))
            {
                // Convert from mÂ² to millidarcies (1 mÂ² = 1.013e15 mD)
                var permMD = perm * 1.013e15f;

                for (var depth = unit.DepthFrom; depth <= unit.DepthTo; depth += 2.0f)
                {
                    // Add log-normal variation
                    var logVariation = (float)(Random.Shared.NextDouble() * 0.5 - 0.25);
                    var value = permMD * (float)Math.Exp(logVariation);

                    points.Add(new ParameterPoint
                    {
                        Depth = depth,
                        Value = value,
                        SourceDataset = "Synthetic"
                    });
                }
            }

        return points;
    }

    private static List<ParameterPoint> GenerateThermalProfile(BoreholeDataset borehole)
    {
        var points = new List<ParameterPoint>();

        foreach (var unit in borehole.LithologyUnits)
            if (unit.Parameters.TryGetValue("ThermalConductivity", out var thermal))
                for (var depth = unit.DepthFrom; depth <= unit.DepthTo; depth += 5.0f)
                {
                    var variation = (float)(Random.Shared.NextDouble() * 0.2 - 0.1);
                    var value = Math.Max(0.5f, Math.Min(4.0f, thermal + variation));

                    points.Add(new ParameterPoint
                    {
                        Depth = depth,
                        Value = value,
                        SourceDataset = "Synthetic"
                    });
                }

        return points;
    }

    private static List<ParameterPoint> GenerateGammaRayProfile(BoreholeDataset borehole)
    {
        var points = new List<ParameterPoint>();

        foreach (var unit in borehole.LithologyUnits)
        {
            // Estimate gamma ray based on lithology type
            var baseGR = unit.LithologyType switch
            {
                "Shale" => 100f,
                "Clay" => 90f,
                "Mudstone" => 85f,
                "Siltstone" => 70f,
                "Sandstone" => 40f,
                "Sand" => 35f,
                "Limestone" => 20f,
                "Dolomite" => 15f,
                "Granite" => 120f,
                "Basalt" => 60f,
                "Gravel" => 30f,
                _ => 50f
            };

            for (var depth = unit.DepthFrom; depth <= unit.DepthTo; depth += 1.0f)
            {
                var variation = (float)(Random.Shared.NextDouble() * 20 - 10);
                var value = Math.Max(0, Math.Min(150, baseGR + variation));

                points.Add(new ParameterPoint
                {
                    Depth = depth,
                    Value = value,
                    SourceDataset = "Synthetic"
                });
            }
        }

        return points;
    }

    private static void AddFractures(BoreholeDataset borehole, float[] depths)
    {
        borehole.Fractures.Clear();

        foreach (var depth in depths)
            borehole.Fractures.Add(new FractureData
            {
                Depth = depth,
                Strike = (float)(Random.Shared.NextDouble() * 360),
                Dip = (float)(Random.Shared.NextDouble() * 45 + 45), // 45-90 degrees
                Aperture = (float)(Random.Shared.NextDouble() * 5 + 0.5f), // 0.5-5.5 mm
                Description = $"Natural fracture at {depth:F1}m"
            });
    }
}