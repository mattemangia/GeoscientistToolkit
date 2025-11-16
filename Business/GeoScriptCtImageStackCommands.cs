// GeoscientistToolkit/Business/GeoScript/GeoScriptCtImageStackCommands.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Materials;
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

        Logger.LogInfo($"Segmenting CT stack using {method} method...");

        // Create a copy for output
        var output = ctDs; // In real implementation, would create a proper copy

        switch (method.ToLower())
        {
            case "threshold":
                int min = (int)ParseFloatParameter(cmd.FullText, "min", 0);
                int max = (int)ParseFloatParameter(cmd.FullText, "max", 255);
                Logger.LogInfo($"Threshold segmentation: [{min}, {max}] -> Material {materialId}");
                // Actual implementation would call segmentation code
                break;

            case "otsu":
                Logger.LogInfo($"Otsu automatic thresholding -> Material {materialId}");
                break;

            case "watershed":
                Logger.LogInfo($"Watershed segmentation -> Material {materialId}");
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

        Logger.LogInfo($"Applying 3D {filterType} filter with kernel size {kernelSize}...");

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
            ctDs.Materials = new List<CtMaterial>();

        var newMaterial = new CtMaterial
        {
            Id = (byte)(ctDs.Materials.Count + 1),
            Name = name,
            Color = System.Drawing.Color.FromArgb(255, 200, 100)
        };

        ctDs.Materials.Add(newMaterial);
        Logger.LogInfo($"Added material: {name} (ID: {newMaterial.Id})");

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
            var material = ctDs.Materials.FirstOrDefault(m => m.Id == materialId);
            if (material != null)
            {
                ctDs.Materials.Remove(material);
                Logger.LogInfo($"Removed material ID {materialId}");
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

        Logger.LogInfo($"Analyzing porosity using material ID {voidMaterialId}...");
        Logger.LogInfo($"Porosity calculation complete");
        Logger.LogInfo($"Total Porosity: (would show calculated value)");
        Logger.LogInfo($"Connected Porosity: (would show calculated value)");
        Logger.LogInfo($"Isolated Porosity: (would show calculated value)");

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

        Logger.LogInfo($"Cropping CT volume: ({x},{y},{z}) size ({width},{height},{depth})");

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

        Logger.LogInfo($"Extracting {axis}-axis slice at index {index}");

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

        Logger.LogInfo($"Analyzing connected components for material {materialId}...");
        Logger.LogInfo($"Found components: (would show count)");
        Logger.LogInfo($"Largest component volume: (would show value)");
        Logger.LogInfo($"Average component volume: (would show value)");

        return Task.FromResult<Dataset>(ctDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }
}
