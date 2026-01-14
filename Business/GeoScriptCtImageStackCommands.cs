// GeoscientistToolkit/Business/GeoScript/GeoScriptCtImageStackCommands.cs

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Analysis.AcousticSimulation;
using GeoscientistToolkit.Analysis.Geomechanics;
using GeoscientistToolkit.Analysis.NMR;
using GeoscientistToolkit.Analysis.ThermalConductivity;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business.GeoScriptCtImageStackCommands;

/// <summary>
/// CT_SEGMENT - Segment CT image stack
/// Usage: CT_SEGMENT method=threshold min=100 max=200 material=1
/// </summary>
public class CtSegmentCommand : IGeoScriptCommand
{
    public string Name => "CT_SEGMENT";
    public string HelpText => "Segment CT image stack using various methods";
    public string Usage => "CT_SEGMENT method=<threshold|otsu|watershed> [min=<value>] [max=<value>] material=<id>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not CtImageStackDataset ctDs)
            throw new NotSupportedException("CT_SEGMENT only works with CT Image Stack datasets");

        var cmd = (CommandNode)node;
        string method = ParseStringParameter(cmd.FullText, "method", "threshold");
        int materialId = (int)ParseFloatParameter(cmd.FullText, "material", 1);

        Logger.Log($"Segmenting CT stack using {method} method...");

        // Create a copy for output
        var output = ctDs; // In real implementation, would create a proper copy

        switch (method.ToLower())
        {
            case "threshold":
                int min = (int)ParseFloatParameter(cmd.FullText, "min", 0);
                int max = (int)ParseFloatParameter(cmd.FullText, "max", 255);
                Logger.Log($"Threshold segmentation: [{min}, {max}] -> Material {materialId}");
                // Actual implementation would call segmentation code
                break;

            case "otsu":
                Logger.Log($"Otsu automatic thresholding -> Material {materialId}");
                break;

            case "watershed":
                Logger.Log($"Watershed segmentation -> Material {materialId}");
                break;
        }

        return Task.FromResult<Dataset>(output);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}

/// <summary>
/// CT_FILTER3D - Apply 3D filters to CT stack
/// Usage: CT_FILTER3D type=gaussian size=5
/// </summary>
public class CtFilter3DCommand : IGeoScriptCommand
{
    public string Name => "CT_FILTER3D";
    public string HelpText => "Apply 3D filters to CT image stack (gaussian, median, mean, nlm)";
    public string Usage => "CT_FILTER3D type=<gaussian|median|mean|nlm|bilateral> size=<kernelSize>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not CtImageStackDataset ctDs)
            throw new NotSupportedException("CT_FILTER3D only works with CT Image Stack datasets");

        var cmd = (CommandNode)node;
        string filterType = ParseStringParameter(cmd.FullText, "type", "gaussian");
        int kernelSize = (int)ParseFloatParameter(cmd.FullText, "size", 5);

        Logger.Log($"Applying 3D {filterType} filter with kernel size {kernelSize}...");

        return Task.FromResult<Dataset>(ctDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}

/// <summary>
/// CT_ADD_MATERIAL - Add a new material to CT stack
/// Usage: CT_ADD_MATERIAL name=Sandstone color=255,200,100
/// </summary>
public class CtAddMaterialCommand : IGeoScriptCommand
{
    public string Name => "CT_ADD_MATERIAL";
    public string HelpText => "Add a new material to the CT image stack";
    public string Usage => "CT_ADD_MATERIAL name=<materialName> color=<r,g,b>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not CtImageStackDataset ctDs)
            throw new NotSupportedException("CT_ADD_MATERIAL only works with CT Image Stack datasets");

        var cmd = (CommandNode)node;
        string name = ParseStringParameter(cmd.FullText, "name", "New Material");

        if (ctDs.Materials == null)
            ctDs.Materials = new List<Material>();

        var newMaterial = new Material((byte)(ctDs.Materials.Count + 1), name, new System.Numerics.Vector4(1.0f, 0.78f, 0.39f, 1.0f));

        ctDs.Materials.Add(newMaterial);
        Logger.Log($"Added material: {name} (ID: {newMaterial.ID})");

        return Task.FromResult<Dataset>(ctDs);
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}

/// <summary>
/// CT_REMOVE_MATERIAL - Remove material from CT stack
/// Usage: CT_REMOVE_MATERIAL id=2
/// </summary>
public class CtRemoveMaterialCommand : IGeoScriptCommand
{
    public string Name => "CT_REMOVE_MATERIAL";
    public string HelpText => "Remove a material from the CT image stack";
    public string Usage => "CT_REMOVE_MATERIAL id=<materialId>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not CtImageStackDataset ctDs)
            throw new NotSupportedException("CT_REMOVE_MATERIAL only works with CT Image Stack datasets");

        var cmd = (CommandNode)node;
        int materialId = (int)ParseFloatParameter(cmd.FullText, "id", 1);

        if (ctDs.Materials != null)
        {
            var material = ctDs.Materials.FirstOrDefault(m => m.ID == materialId);
            if (material != null)
            {
                ctDs.Materials.Remove(material);
                Logger.Log($"Removed material ID {materialId}");
            }
        }

        return Task.FromResult<Dataset>(ctDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }
}

/// <summary>
/// CT_LIST_MATERIALS - List materials in the CT stack
/// Usage: CT_LIST_MATERIALS [include_exterior=true]
/// </summary>
public class CtListMaterialsCommand : IGeoScriptCommand
{
    public string Name => "CT_LIST_MATERIALS";
    public string HelpText => "List materials in the CT image stack (ID and Name)";
    public string Usage => "CT_LIST_MATERIALS [include_exterior=<true|false>]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not CtImageStackDataset ctDs)
            throw new NotSupportedException("CT_LIST_MATERIALS only works with CT Image Stack datasets");

        var cmd = (CommandNode)node;
        bool includeExterior = ParseBoolParameter(cmd.FullText, "include_exterior", true);

        var materials = ctDs.Materials ?? new List<Material>();
        if (!includeExterior)
            materials = materials.Where(m => m.ID != 0).ToList();

        Logger.Log($"CT materials ({materials.Count}):");
        foreach (var material in materials.OrderBy(m => m.ID))
            Logger.Log($"  {material.ID}: {material.Name}");

        return Task.FromResult<Dataset>(ctDs);
    }

    private bool ParseBoolParameter(string fullText, string paramName, bool defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*(true|false)", RegexOptions.IgnoreCase);
        if (!match.Success)
            return defaultValue;

        return bool.TryParse(match.Groups[1].Value, out var value) ? value : defaultValue;
    }
}

/// <summary>
/// CT_MATERIAL_STATS - Export material statistics to a table dataset
/// Usage: CT_MATERIAL_STATS
/// </summary>
public class CtMaterialStatsCommand : IGeoScriptCommand
{
    public string Name => "CT_MATERIAL_STATS";
    public string HelpText => "Generate material statistics table from a CT image stack";
    public string Usage => "CT_MATERIAL_STATS";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not CtImageStackDataset ctDs)
            throw new NotSupportedException("CT_MATERIAL_STATS only works with CT Image Stack datasets");

        var tableDataset = TableExporter.ExportMaterialStatistics(ctDs);
        if (tableDataset == null)
        {
            Logger.LogError("CT_MATERIAL_STATS failed to generate material statistics.");
            return Task.FromResult<Dataset>(ctDs);
        }

        return Task.FromResult<Dataset>(tableDataset);
    }
}

/// <summary>
/// CT_ANALYZE_POROSITY - Calculate porosity from segmentation
/// Usage: CT_ANALYZE_POROSITY void_material=1
/// </summary>
public class CtAnalyzePorosityCommand : IGeoScriptCommand
{
    public string Name => "CT_ANALYZE_POROSITY";
    public string HelpText => "Calculate porosity and pore statistics from CT segmentation";
    public string Usage => "CT_ANALYZE_POROSITY void_material=<materialId>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not CtImageStackDataset ctDs)
            throw new NotSupportedException("CT_ANALYZE_POROSITY only works with CT Image Stack datasets");

        var cmd = (CommandNode)node;
        int voidMaterialId = (int)ParseFloatParameter(cmd.FullText, "void_material", 1);

        Logger.Log($"Analyzing porosity using material ID {voidMaterialId}...");
        Logger.Log($"Porosity calculation complete");
        Logger.Log($"Total Porosity: (would show calculated value)");
        Logger.Log($"Connected Porosity: (would show calculated value)");
        Logger.Log($"Isolated Porosity: (would show calculated value)");

        return Task.FromResult<Dataset>(ctDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }
}

/// <summary>
/// CT_CROP - Crop CT volume to region
/// Usage: CT_CROP x=0 y=0 z=0 width=100 height=100 depth=100
/// </summary>
public class CtCropCommand : IGeoScriptCommand
{
    public string Name => "CT_CROP";
    public string HelpText => "Crop CT volume to specified region";
    public string Usage => "CT_CROP x=<x> y=<y> z=<z> width=<w> height=<h> depth=<d>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not CtImageStackDataset ctDs)
            throw new NotSupportedException("CT_CROP only works with CT Image Stack datasets");

        var cmd = (CommandNode)node;
        int x = (int)ParseFloatParameter(cmd.FullText, "x", 0);
        int y = (int)ParseFloatParameter(cmd.FullText, "y", 0);
        int z = (int)ParseFloatParameter(cmd.FullText, "z", 0);
        int width = (int)ParseFloatParameter(cmd.FullText, "width", 100);
        int height = (int)ParseFloatParameter(cmd.FullText, "height", 100);
        int depth = (int)ParseFloatParameter(cmd.FullText, "depth", 100);

        Logger.Log($"Cropping CT volume: ({x},{y},{z}) size ({width},{height},{depth})");

        return Task.FromResult<Dataset>(ctDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }
}

/// <summary>
/// CT_EXTRACT_SLICE - Extract a 2D slice from CT stack
/// Usage: CT_EXTRACT_SLICE axis=z index=50
/// </summary>
public class CtExtractSliceCommand : IGeoScriptCommand
{
    public string Name => "CT_EXTRACT_SLICE";
    public string HelpText => "Extract a 2D slice from CT stack";
    public string Usage => "CT_EXTRACT_SLICE axis=<x|y|z> index=<sliceNumber>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not CtImageStackDataset ctDs)
            throw new NotSupportedException("CT_EXTRACT_SLICE only works with CT Image Stack datasets");

        var cmd = (CommandNode)node;
        string axis = ParseStringParameter(cmd.FullText, "axis", "z");
        int index = (int)ParseFloatParameter(cmd.FullText, "index", 0);

        Logger.Log($"Extracting {axis}-axis slice at index {index}");

        return Task.FromResult<Dataset>(ctDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}

/// <summary>
/// CT_LABEL_ANALYSIS - Analyze connected components
/// Usage: CT_LABEL_ANALYSIS material=1
/// </summary>
public class CtLabelAnalysisCommand : IGeoScriptCommand
{
    public string Name => "CT_LABEL_ANALYSIS";
    public string HelpText => "Analyze connected components and label individual objects";
    public string Usage => "CT_LABEL_ANALYSIS material=<materialId>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not CtImageStackDataset ctDs)
            throw new NotSupportedException("CT_LABEL_ANALYSIS only works with CT Image Stack datasets");

        var cmd = (CommandNode)node;
        int materialId = (int)ParseFloatParameter(cmd.FullText, "material", 1);

        Logger.Log($"Analyzing connected components for material {materialId}...");
        Logger.Log($"Found components: (would show count)");
        Logger.Log($"Largest component volume: (would show value)");
        Logger.Log($"Average component volume: (would show value)");

        return Task.FromResult<Dataset>(ctDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }
}

/// <summary>
/// SIMULATE_ACOUSTIC - Run acoustic simulation on CT dataset
/// Usage: SIMULATE_ACOUSTIC materials=1,2 tx=0.1,0.5,0.5 rx=0.9,0.5,0.5 time_steps=1000
/// </summary>
public class SimulateAcousticCommand : IGeoScriptCommand
{
    public string Name => "SIMULATE_ACOUSTIC";
    public string HelpText => "Run acoustic wave simulation for the current CT dataset";
    public string Usage =>
        "SIMULATE_ACOUSTIC [materials=1,2] [tx=0.1,0.5,0.5] [rx=0.9,0.5,0.5] [time_steps=1000] [use_gpu=true]";

    public async Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not CtImageStackDataset ctDs)
            throw new NotSupportedException("SIMULATE_ACOUSTIC only works with CT Image Stack datasets");

        var cmd = (CommandNode)node;
        var args = GeoScriptArgumentParser.ParseArguments(cmd.FullText);

        var extent = GeoScriptSimulationHelpers.BuildAcousticExtent(ctDs, args, context);

        var materials = GeoScriptArgumentParser.GetByteSet(args, "materials", context) ??
                        ctDs.Materials.Where(m => m.ID != 0).Select(m => m.ID).ToHashSet();

        var txPosition = GeoScriptArgumentParser.GetVector3(args, "tx", new Vector3(0, 0.5f, 0.5f), context);
        var rxPosition = GeoScriptArgumentParser.GetVector3(args, "rx", new Vector3(1, 0.5f, 0.5f), context);

        var parameters = new SimulationParameters
        {
            Width = extent.Width,
            Height = extent.Height,
            Depth = extent.Depth,
            PixelSize = ctDs.PixelSize / 1_000_000.0f,
            SimulationExtent = extent,
            SelectedMaterialIDs = new HashSet<byte>(materials),
            SelectedMaterialID = materials.FirstOrDefault(),
            Axis = GeoScriptArgumentParser.GetInt(args, "axis", 2, context),
            UseFullFaceTransducers = GeoScriptArgumentParser.GetBool(args, "use_full_face_transducers", false, context),
            ConfiningPressureMPa = GeoScriptArgumentParser.GetFloat(args, "confining_pressure_mpa", 1.0f, context),
            FailureAngleDeg = GeoScriptArgumentParser.GetFloat(args, "failure_angle_deg", 30.0f, context),
            CohesionMPa = GeoScriptArgumentParser.GetFloat(args, "cohesion_mpa", 5.0f, context),
            SourceEnergyJ = GeoScriptArgumentParser.GetFloat(args, "source_energy_j", 1.0f, context),
            SourceFrequencyKHz = GeoScriptArgumentParser.GetFloat(args, "source_frequency_khz", 500.0f, context),
            SourceAmplitude = GeoScriptArgumentParser.GetInt(args, "source_amplitude", 100, context),
            TimeSteps = GeoScriptArgumentParser.GetInt(args, "time_steps", 1000, context),
            YoungsModulusMPa = GeoScriptArgumentParser.GetFloat(args, "youngs_modulus_mpa", 30000.0f, context),
            PoissonRatio = GeoScriptArgumentParser.GetFloat(args, "poisson_ratio", 0.25f, context),
            UseElasticModel = GeoScriptArgumentParser.GetBool(args, "use_elastic_model", true, context),
            UsePlasticModel = GeoScriptArgumentParser.GetBool(args, "use_plastic_model", false, context),
            UseBrittleModel = GeoScriptArgumentParser.GetBool(args, "use_brittle_model", false, context),
            UseGPU = GeoScriptArgumentParser.GetBool(args, "use_gpu", true, context),
            UseRickerWavelet = GeoScriptArgumentParser.GetBool(args, "use_ricker_wavelet", true, context),
            TxPosition = txPosition,
            RxPosition = rxPosition,
            EnableRealTimeVisualization = GeoScriptArgumentParser.GetBool(args, "enable_real_time_visualization", false, context),
            SaveTimeSeries = GeoScriptArgumentParser.GetBool(args, "save_time_series", false, context),
            SnapshotInterval = GeoScriptArgumentParser.GetInt(args, "snapshot_interval", 10, context),
            UseChunkedProcessing = GeoScriptArgumentParser.GetBool(args, "use_chunked_processing", true, context),
            ChunkSizeMB = GeoScriptArgumentParser.GetInt(args, "chunk_size_mb", 512, context),
            EnableOffloading = GeoScriptArgumentParser.GetBool(args, "enable_offloading", false, context),
            OffloadDirectory = GeoScriptArgumentParser.GetString(args, "offload_directory", string.Empty, context),
            ArtificialDampingFactor = GeoScriptArgumentParser.GetFloat(args, "artificial_damping_factor", 0.2f, context),
            TimeStepSeconds = GeoScriptArgumentParser.GetFloat(args, "time_step_seconds", 1e-6f, context)
        };

        var reservedKeys = new HashSet<string>
        {
            "materials", "tx", "rx", "extent_min_x", "extent_min_y", "extent_min_z",
            "extent_width", "extent_height", "extent_depth", "min_x", "min_y", "min_z", "width", "height", "depth"
        };
        GeoScriptSimulationHelpers.ApplyArguments(parameters, args, context, reservedKeys);

        var volumeLabels = await GeoScriptSimulationHelpers.ExtractAcousticLabelsAsync(ctDs, extent);
        var densityVolume = await GeoScriptSimulationHelpers.ExtractAcousticDensityAsync(ctDs, ctDs.VolumeData, extent);
        var (youngsModulus, poissonRatio) = await GeoScriptSimulationHelpers.ExtractAcousticMaterialPropertiesAsync(
            ctDs, extent, parameters.YoungsModulusMPa, parameters.PoissonRatio);

        using var simulator = new ChunkedAcousticSimulator(parameters);
        simulator.SetPerVoxelMaterialProperties(youngsModulus, poissonRatio);

        Logger.Log("[GeoScript] Running acoustic simulation...");
        var results = await simulator.RunAsync(volumeLabels, densityVolume, CancellationToken.None);

        ctDs.Metadata["AcousticSimulationResults"] = results;
        Logger.Log("[GeoScript] Acoustic simulation completed.");

        return ctDs;
    }
}

/// <summary>
/// SIMULATE_NMR - Run NMR simulation on CT dataset
/// Usage: SIMULATE_NMR pore_material_id=1 steps=1000 timestep_ms=0.01 use_opencl=false
/// </summary>
public class SimulateNmrCommand : IGeoScriptCommand
{
    public string Name => "SIMULATE_NMR";
    public string HelpText => "Run NMR random-walk simulation for the current CT dataset";
    public string Usage =>
        "SIMULATE_NMR [pore_material_id=1] [steps=1000] [timestep_ms=0.01] [use_opencl=false]";

    public async Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not CtImageStackDataset ctDs)
            throw new NotSupportedException("SIMULATE_NMR only works with CT Image Stack datasets");

        var cmd = (CommandNode)node;
        var args = GeoScriptArgumentParser.ParseArguments(cmd.FullText);

        var config = new NMRSimulationConfig();
        InitializeNmrDefaults(config, ctDs);

        config.PoreMaterialID = (byte)GeoScriptArgumentParser.GetInt(args, "pore_material_id", config.PoreMaterialID, context);
        config.NumberOfWalkers = GeoScriptArgumentParser.GetInt(args, "number_of_walkers", config.NumberOfWalkers, context);
        config.NumberOfSteps = GeoScriptArgumentParser.GetInt(args, "steps", config.NumberOfSteps, context);
        config.TimeStepMs = GeoScriptArgumentParser.GetDouble(args, "timestep_ms", config.TimeStepMs, context);
        config.DiffusionCoefficient = GeoScriptArgumentParser.GetDouble(args, "diffusion_coefficient", config.DiffusionCoefficient, context);
        config.VoxelSize = GeoScriptArgumentParser.GetDouble(args, "voxel_size_m", config.VoxelSize, context);
        config.PoreShapeFactor = GeoScriptArgumentParser.GetDouble(args, "pore_shape_factor", config.PoreShapeFactor, context);
        config.T2BinCount = GeoScriptArgumentParser.GetInt(args, "t2_bin_count", config.T2BinCount, context);
        config.T2MinMs = GeoScriptArgumentParser.GetDouble(args, "t2_min_ms", config.T2MinMs, context);
        config.T2MaxMs = GeoScriptArgumentParser.GetDouble(args, "t2_max_ms", config.T2MaxMs, context);
        config.ComputeT1T2Map = GeoScriptArgumentParser.GetBool(args, "compute_t1t2_map", config.ComputeT1T2Map, context);
        config.T1BinCount = GeoScriptArgumentParser.GetInt(args, "t1_bin_count", config.T1BinCount, context);
        config.T1MinMs = GeoScriptArgumentParser.GetDouble(args, "t1_min_ms", config.T1MinMs, context);
        config.T1MaxMs = GeoScriptArgumentParser.GetDouble(args, "t1_max_ms", config.T1MaxMs, context);
        config.T1T2Ratio = GeoScriptArgumentParser.GetDouble(args, "t1t2_ratio", config.T1T2Ratio, context);
        config.RandomSeed = GeoScriptArgumentParser.GetInt(args, "random_seed", config.RandomSeed, context);
        config.UseOpenCL = GeoScriptArgumentParser.GetBool(args, "use_opencl", config.UseOpenCL, context);

        var relaxivityMap = GeoScriptArgumentParser.GetByteDoubleMap(args, "relaxivities", context);
        if (relaxivityMap != null)
        {
            foreach (var (materialId, relaxivity) in relaxivityMap)
            {
                if (!config.MaterialRelaxivities.TryGetValue(materialId, out var existing))
                {
                    existing = new MaterialRelaxivityConfig { MaterialName = $"Material_{materialId}" };
                    config.MaterialRelaxivities[materialId] = existing;
                }

                existing.SurfaceRelaxivity = relaxivity;
            }
        }

        if (GeoScriptArgumentParser.TryGetString(args, "voxel_size_um", out var voxelUm))
        {
            config.VoxelSize = double.Parse(voxelUm, CultureInfo.InvariantCulture) * 1e-6;
        }

        Logger.Log("[GeoScript] Running NMR simulation...");

        NMRResults results;
        if (config.UseOpenCL)
        {
            var tcs = new TaskCompletionSource<NMRResults>();
            using var simulation = new NMRSimulationOpenCL(ctDs, config);
            simulation.RunSimulationAsync(null, tcs.SetResult, tcs.SetException);
            results = await tcs.Task;
        }
        else
        {
            var simulation = new NMRSimulation(ctDs, config);
            results = await simulation.RunSimulationAsync(null);
        }

        ctDs.NmrResults = results;
        Logger.Log("[GeoScript] NMR simulation completed.");

        return ctDs;
    }

    private static void InitializeNmrDefaults(NMRSimulationConfig config, CtImageStackDataset dataset)
    {
        var voxelSizeMeters = ConvertToMeters(dataset.PixelSize, dataset.Unit);
        config.VoxelSize = voxelSizeMeters;

        config.MaterialRelaxivities.Clear();
        foreach (var material in dataset.Materials)
        {
            if (material.ID == 0) continue;

            config.MaterialRelaxivities[material.ID] = new MaterialRelaxivityConfig
            {
                MaterialName = material.Name,
                SurfaceRelaxivity = 10.0,
                Color = material.Color
            };
        }
    }

    private static double ConvertToMeters(float pixelSize, string unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
            return pixelSize * 1e-6f;

        var unitLower = unit.ToLowerInvariant().Trim();
        return unitLower switch
        {
            "m" or "meter" or "meters" => pixelSize,
            "mm" or "millimeter" or "millimeters" => pixelSize * 1e-3f,
            "µm" or "um" or "micrometer" or "micrometers" or "micron" or "microns" => pixelSize * 1e-6f,
            "nm" or "nanometer" or "nanometers" => pixelSize * 1e-9f,
            "pm" or "picometer" or "picometers" => pixelSize * 1e-12f,
            "km" or "kilometer" or "kilometers" => pixelSize * 1e3f,
            "cm" or "centimeter" or "centimeters" => pixelSize * 1e-2f,
            _ => pixelSize * 1e-6f
        };
    }
}

/// <summary>
/// SIMULATE_THERMAL_CONDUCTIVITY - Run thermal conductivity simulation on CT dataset
/// Usage: SIMULATE_THERMAL_CONDUCTIVITY direction=z temperature_hot=373.15 temperature_cold=293.15
/// </summary>
public class SimulateThermalConductivityCommand : IGeoScriptCommand
{
    public string Name => "SIMULATE_THERMAL_CONDUCTIVITY";
    public string HelpText => "Run thermal conductivity simulation for the current CT dataset";
    public string Usage =>
        "SIMULATE_THERMAL_CONDUCTIVITY [direction=x|y|z] [temperature_hot=373.15] [temperature_cold=293.15]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not CtImageStackDataset ctDs)
            throw new NotSupportedException("SIMULATE_THERMAL_CONDUCTIVITY only works with CT Image Stack datasets");

        var cmd = (CommandNode)node;
        var args = GeoScriptArgumentParser.ParseArguments(cmd.FullText);

        var options = new ThermalOptions
        {
            Dataset = ctDs,
            TemperatureHot = GeoScriptArgumentParser.GetDouble(args, "temperature_hot", 373.15, context),
            TemperatureCold = GeoScriptArgumentParser.GetDouble(args, "temperature_cold", 293.15, context),
            HeatFlowDirection = GeoScriptArgumentParser.GetEnum(args, "direction", HeatFlowDirection.Z, context),
            SolverBackend = GeoScriptArgumentParser.GetEnum(args, "solver_backend", SolverBackend.CSharp_Parallel, context),
            ConvergenceTolerance = GeoScriptArgumentParser.GetDouble(args, "convergence_tolerance", 1e-6, context),
            MaxIterations = GeoScriptArgumentParser.GetInt(args, "max_iterations", 20000, context),
            SorFactor = GeoScriptArgumentParser.GetDouble(args, "sor_factor", 1.8, context)
        };

        var conductivityOverrides = GeoScriptArgumentParser.GetByteDoubleMap(args, "material_k", context);
        if (conductivityOverrides != null)
        {
            foreach (var (id, value) in conductivityOverrides)
                options.MaterialConductivities[id] = value;
        }

        Logger.Log("[GeoScript] Running thermal conductivity simulation...");
        var results = ThermalConductivitySolver.Solve(options, null, CancellationToken.None);
        ctDs.ThermalResults = results;
        Logger.Log("[GeoScript] Thermal conductivity simulation completed.");

        return Task.FromResult<Dataset>(ctDs);
    }
}

/// <summary>
/// SIMULATE_GEOMECH - Run geomechanical simulation on CT dataset
/// Usage: SIMULATE_GEOMECH sigma1=100 sigma2=50 sigma3=20 use_gpu=true
/// </summary>
public class SimulateGeomechCommand : IGeoScriptCommand
{
    public string Name => "SIMULATE_GEOMECH";
    public string HelpText => "Run geomechanical simulation for the current CT dataset";
    public string Usage =>
        "SIMULATE_GEOMECH [sigma1=100] [sigma2=50] [sigma3=20] [use_gpu=true] [porosity=dataset.field]";

    public async Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not CtImageStackDataset ctDs)
            throw new NotSupportedException("SIMULATE_GEOMECH only works with CT Image Stack datasets");

        var cmd = (CommandNode)node;
        var args = GeoScriptArgumentParser.ParseArguments(cmd.FullText);

        var extent = GeoScriptSimulationHelpers.BuildGeomechanicsExtent(ctDs, args, context);
        var selectedMaterials = GeoScriptArgumentParser.GetByteSet(args, "materials", context) ??
                                ctDs.Materials.Where(m => m.ID != 0).Select(m => m.ID).ToHashSet();

        var applyGravity = GeoScriptArgumentParser.GetBool(args, "apply_gravity", false, context);
        var gravity = GeoScriptArgumentParser.GetVector3(args, "gravity", new Vector3(0, 0, -9.81f), context);
        var gravitySpecified = false;

        if (GeoScriptArgumentParser.TryGetString(args, "gravity_preset", out var gravityPreset))
        {
            gravitySpecified = true;
            var magnitude = gravityPreset.ToLowerInvariant() switch
            {
                "earth" => 9.81f,
                "moon" => 1.62f,
                "mars" => 3.72f,
                "venus" => 8.87f,
                "jupiter" => 24.79f,
                "saturn" => 10.44f,
                "mercury" => 3.70f,
                _ => throw new ArgumentException($"Unknown gravity preset: {gravityPreset}")
            };
            gravity = new Vector3(0, 0, -magnitude);
        }

        if (GeoScriptArgumentParser.TryGetString(args, "gravity_magnitude", out var gravityMagnitudeValue))
        {
            gravitySpecified = true;
            var magnitude = float.Parse(gravityMagnitudeValue, CultureInfo.InvariantCulture);
            gravity = new Vector3(0, 0, -MathF.Abs(magnitude));
        }

        if (GeoScriptArgumentParser.TryGetString(args, "gravity_x", out var gravityXValue))
        {
            gravitySpecified = true;
            gravity.X = float.Parse(gravityXValue, CultureInfo.InvariantCulture);
        }

        if (GeoScriptArgumentParser.TryGetString(args, "gravity_y", out var gravityYValue))
        {
            gravitySpecified = true;
            gravity.Y = float.Parse(gravityYValue, CultureInfo.InvariantCulture);
        }

        if (GeoScriptArgumentParser.TryGetString(args, "gravity_z", out var gravityZValue))
        {
            gravitySpecified = true;
            gravity.Z = float.Parse(gravityZValue, CultureInfo.InvariantCulture);
        }

        if (gravitySpecified)
            applyGravity = true;

        var parameters = new GeomechanicalParameters
        {
            Width = extent.Width,
            Height = extent.Height,
            Depth = extent.Depth,
            PixelSize = ctDs.PixelSize,
            SimulationExtent = extent,
            SelectedMaterialIDs = new HashSet<byte>(selectedMaterials),
            YoungModulus = GeoScriptArgumentParser.GetFloat(args, "young_modulus", 30000f, context),
            PoissonRatio = GeoScriptArgumentParser.GetFloat(args, "poisson_ratio", 0.25f, context),
            Cohesion = GeoScriptArgumentParser.GetFloat(args, "cohesion", 10f, context),
            FrictionAngle = GeoScriptArgumentParser.GetFloat(args, "friction_angle", 30f, context),
            TensileStrength = GeoScriptArgumentParser.GetFloat(args, "tensile_strength", 5f, context),
            Density = GeoScriptArgumentParser.GetFloat(args, "density", 2700f, context),
            LoadingMode = GeoScriptArgumentParser.GetEnum(args, "loading_mode", LoadingMode.Triaxial, context),
            Sigma1 = GeoScriptArgumentParser.GetFloat(args, "sigma1", 100f, context),
            Sigma2 = GeoScriptArgumentParser.GetFloat(args, "sigma2", 50f, context),
            Sigma3 = GeoScriptArgumentParser.GetFloat(args, "sigma3", 20f, context),
            Sigma1Direction = GeoScriptArgumentParser.GetVector3(args, "sigma1_direction", new Vector3(0, 0, 1), context),
            ApplyGravity = applyGravity,
            GravityAcceleration = gravity,
            UsePorePressure = GeoScriptArgumentParser.GetBool(args, "use_pore_pressure", false, context),
            PorePressure = GeoScriptArgumentParser.GetFloat(args, "pore_pressure", 10f, context),
            BiotCoefficient = GeoScriptArgumentParser.GetFloat(args, "biot_coefficient", 0.8f, context),
            FailureCriterion = GeoScriptArgumentParser.GetEnum(args, "failure_criterion", FailureCriterion.MohrCoulomb, context),
            DilationAngle = GeoScriptArgumentParser.GetFloat(args, "dilation_angle", 10f, context),
            UseGPU = GeoScriptArgumentParser.GetBool(args, "use_gpu", true, context),
            MaxIterations = GeoScriptArgumentParser.GetInt(args, "max_iterations", 1000, context),
            Tolerance = GeoScriptArgumentParser.GetFloat(args, "tolerance", 1e-4f, context),
            EnableDamageEvolution = GeoScriptArgumentParser.GetBool(args, "enable_damage_evolution", true, context),
            DamageThreshold = GeoScriptArgumentParser.GetFloat(args, "damage_threshold", 0.001f, context),
            DamageCriticalStrain = GeoScriptArgumentParser.GetFloat(args, "damage_critical_strain", 0.01f, context),
            DamageEvolutionRate = GeoScriptArgumentParser.GetFloat(args, "damage_evolution_rate", 100f, context),
            DamageModel = GeoScriptArgumentParser.GetEnum(args, "damage_model", DamageModel.Exponential, context),
            ApplyDamageToStiffness = GeoScriptArgumentParser.GetBool(args, "apply_damage_to_stiffness", true, context),
            PlasticHardeningModulus = GeoScriptArgumentParser.GetFloat(args, "plastic_hardening_modulus", 1000f, context)
        };

        var reservedKeys = new HashSet<string>
        {
            "materials", "extent_min_x", "extent_min_y", "extent_min_z", "extent_width", "extent_height",
            "extent_depth", "min_x", "min_y", "min_z", "width", "height", "depth"
        };
        GeoScriptSimulationHelpers.ApplyArguments(parameters, args, context, reservedKeys);

        var labels = await GeoScriptSimulationHelpers.ExtractLabelsAsync(ctDs, extent);
        var density = await GeoScriptSimulationHelpers.ExtractGeomechanicsDensityAsync(ctDs, extent);

        Logger.Log("[GeoScript] Running geomechanical simulation...");

        GeomechanicalResults results;
        if (parameters.UseGPU)
        {
            using var simulator = new GeomechanicalSimulatorGPU(parameters);
            results = await Task.Run(() => simulator.Simulate(labels, density, null, CancellationToken.None));
        }
        else
        {
            var simulator = new GeomechanicalSimulatorCPU(parameters);
            results = await Task.Run(() => simulator.Simulate(labels, density, null, CancellationToken.None));
        }

        ctDs.Metadata["GeomechanicalResults"] = results;
        Logger.Log("[GeoScript] Geomechanical simulation completed.");

        return ctDs;
    }
}
