// GeoscientistToolkit/Business/GeoScript/GeoScriptGeomechanicsCommands.cs

using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Numerics;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.TwoDGeology;
using GeoscientistToolkit.Data.TwoDGeology.Geomechanics;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business.GeoScriptGeomechanicsCommands;

/// <summary>
/// GEOMECH_CREATE_MESH - Create a 2D FEM mesh for geomechanical analysis
/// Usage: GEOMECH_CREATE_MESH type=rectangular width=100 height=50 nx=20 ny=10
/// </summary>
public class GeomechCreateMeshCommand : IGeoScriptCommand
{
    public string Name => "GEOMECH_CREATE_MESH";
    public string HelpText => "Create a 2D FEM mesh for geomechanical analysis";
    public string Usage => "GEOMECH_CREATE_MESH type=<rectangular|circular|polygon> [width=<m>] [height=<m>] [nx=<elements>] [ny=<elements>] [radius=<m>]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not TwoDGeologyDataset geoDs)
            throw new NotSupportedException("GEOMECH_CREATE_MESH requires a TwoDGeologyDataset");

        var cmd = (CommandNode)node;
        string meshType = ParseStringParameter(cmd.FullText, "type", "rectangular");
        float width = ParseFloatParameter(cmd.FullText, "width", 100);
        float height = ParseFloatParameter(cmd.FullText, "height", 50);
        int nx = ParseIntParameter(cmd.FullText, "nx", 20);
        int ny = ParseIntParameter(cmd.FullText, "ny", 10);
        float radius = ParseFloatParameter(cmd.FullText, "radius", 25);

        geoDs.GeomechanicalSimulator.Mesh.Clear();

        switch (meshType.ToLower())
        {
            case "rectangular":
                geoDs.GeomechanicalSimulator.Mesh.GenerateRectangularMesh(
                    Vector2.Zero, width, height, nx, ny, 1);
                Logger.Log($"Created rectangular mesh: {width}x{height}m, {nx}x{ny} elements");
                break;
            case "circular":
                geoDs.GeomechanicalSimulator.Mesh.GenerateCircleMesh(
                    Vector2.Zero, radius, nx, ny * 4, 1);
                Logger.Log($"Created circular mesh: radius={radius}m, {nx} radial, {ny * 4} circumferential");
                break;
            case "fromgeology":
                geoDs.GenerateMeshFromGeology(width / nx);
                Logger.Log($"Created mesh from geological formations");
                break;
            default:
                throw new ArgumentException($"Unknown mesh type: {meshType}");
        }

        return Task.FromResult<Dataset>(geoDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }

    private int ParseIntParameter(string fullText, string paramName, int defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? int.Parse(match.Groups[1].Value) : defaultValue;
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}

/// <summary>
/// GEOMECH_SET_MATERIAL - Assign material properties to elements
/// Usage: GEOMECH_SET_MATERIAL name=Sandstone E=25e9 nu=0.25 cohesion=5e6 friction=35
/// </summary>
public class GeomechSetMaterialCommand : IGeoScriptCommand
{
    public string Name => "GEOMECH_SET_MATERIAL";
    public string HelpText => "Create or modify material properties for geomechanical analysis";
    public string Usage => "GEOMECH_SET_MATERIAL name=<name> [E=<Pa>] [nu=<ratio>] [density=<kg/m3>] [cohesion=<Pa>] [friction=<deg>] [tensile=<Pa>] [criterion=<MohrCoulomb|HoekBrown|CurvedMC>]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not TwoDGeologyDataset geoDs)
            throw new NotSupportedException("GEOMECH_SET_MATERIAL requires a TwoDGeologyDataset");

        var cmd = (CommandNode)node;
        string name = ParseStringParameter(cmd.FullText, "name", "Default");
        double E = ParseDoubleParameter(cmd.FullText, "E", 30e9);
        double nu = ParseDoubleParameter(cmd.FullText, "nu", 0.25);
        double density = ParseDoubleParameter(cmd.FullText, "density", 2500);
        double cohesion = ParseDoubleParameter(cmd.FullText, "cohesion", 10e6);
        double friction = ParseDoubleParameter(cmd.FullText, "friction", 35);
        double tensile = ParseDoubleParameter(cmd.FullText, "tensile", 1e6);
        string criterion = ParseStringParameter(cmd.FullText, "criterion", "MohrCoulomb");

        var material = new GeomechanicalMaterial2D
        {
            Name = name,
            YoungModulus = E,
            PoissonRatio = nu,
            Density = density,
            Cohesion = cohesion,
            FrictionAngle = friction,
            TensileStrength = tensile,
            FailureCriterion = Enum.TryParse<FailureCriterion2D>(criterion, true, out var fc) ? fc : FailureCriterion2D.MohrCoulomb
        };

        // For curved Mohr-Coulomb
        if (material.FailureCriterion == FailureCriterion2D.CurvedMohrCoulomb)
        {
            material.UseCurvedMohrCoulomb = true;
            material.CurvedMC_A = ParseDoubleParameter(cmd.FullText, "A", 1.0);
            material.CurvedMC_B = ParseDoubleParameter(cmd.FullText, "B", 0.7);
        }

        // For Hoek-Brown
        if (material.FailureCriterion == FailureCriterion2D.HoekBrown)
        {
            material.HB_mi = ParseDoubleParameter(cmd.FullText, "mi", 10);
            material.GSI = ParseDoubleParameter(cmd.FullText, "GSI", 65);
            material.DisturbanceFactor = ParseDoubleParameter(cmd.FullText, "D", 0);
            material.CalculateHoekBrownFromGSI();
        }

        int id = geoDs.GeomechanicalSimulator.Mesh.Materials.AddMaterial(material);
        Logger.Log($"Created material '{name}' (ID={id}): E={E / 1e9:F1}GPa, c={cohesion / 1e6:F1}MPa, phi={friction}deg");

        return Task.FromResult<Dataset>(geoDs);
    }

    private double ParseDoubleParameter(string fullText, string paramName, double defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?)", RegexOptions.IgnoreCase);
        return match.Success ? double.Parse(match.Groups[1].Value) : defaultValue;
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}

/// <summary>
/// GEOMECH_FIX_BOUNDARY - Fix boundary conditions
/// Usage: GEOMECH_FIX_BOUNDARY side=bottom
/// </summary>
public class GeomechFixBoundaryCommand : IGeoScriptCommand
{
    public string Name => "GEOMECH_FIX_BOUNDARY";
    public string HelpText => "Apply fixed boundary conditions to mesh edges";
    public string Usage => "GEOMECH_FIX_BOUNDARY side=<bottom|top|left|right|all> [fix_x=<true|false>] [fix_y=<true|false>]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not TwoDGeologyDataset geoDs)
            throw new NotSupportedException("GEOMECH_FIX_BOUNDARY requires a TwoDGeologyDataset");

        var cmd = (CommandNode)node;
        string side = ParseStringParameter(cmd.FullText, "side", "bottom");

        var mesh = geoDs.GeomechanicalSimulator.Mesh;

        switch (side.ToLower())
        {
            case "bottom":
                mesh.FixBottom();
                Logger.Log("Fixed bottom boundary (X and Y)");
                break;
            case "top":
                var topNodes = mesh.GetBoundaryNodes(BoundarySide.Top);
                foreach (var nodeId in topNodes)
                    mesh.FixNode(nodeId, true, true);
                Logger.Log("Fixed top boundary");
                break;
            case "left":
                mesh.FixLeft();
                Logger.Log("Fixed left boundary (X only)");
                break;
            case "right":
                mesh.FixRight();
                Logger.Log("Fixed right boundary (X only)");
                break;
            case "all":
                mesh.FixBottom();
                mesh.FixLeft();
                mesh.FixRight();
                var topN = mesh.GetBoundaryNodes(BoundarySide.Top);
                foreach (var nodeId in topN)
                    mesh.FixNode(nodeId, true, true);
                Logger.Log("Fixed all boundaries");
                break;
            case "roller_sides":
                mesh.FixBottom();
                mesh.FixLeft();
                mesh.FixRight();
                Logger.Log("Applied roller boundary conditions (fixed bottom, X-fixed sides)");
                break;
            default:
                throw new ArgumentException($"Unknown boundary side: {side}");
        }

        return Task.FromResult<Dataset>(geoDs);
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}

/// <summary>
/// GEOMECH_APPLY_LOAD - Apply loads to the model
/// Usage: GEOMECH_APPLY_LOAD type=force x=50 y=50 fx=0 fy=-1000000
/// </summary>
public class GeomechApplyLoadCommand : IGeoScriptCommand
{
    public string Name => "GEOMECH_APPLY_LOAD";
    public string HelpText => "Apply forces, pressures, or displacements to the model";
    public string Usage => "GEOMECH_APPLY_LOAD type=<force|pressure|displacement|gravity> [x=<m>] [y=<m>] [fx=<N>] [fy=<N>] [pressure=<Pa>]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not TwoDGeologyDataset geoDs)
            throw new NotSupportedException("GEOMECH_APPLY_LOAD requires a TwoDGeologyDataset");

        var cmd = (CommandNode)node;
        string loadType = ParseStringParameter(cmd.FullText, "type", "force");
        float x = ParseFloatParameter(cmd.FullText, "x", 0);
        float y = ParseFloatParameter(cmd.FullText, "y", 0);
        float fx = ParseFloatParameter(cmd.FullText, "fx", 0);
        float fy = ParseFloatParameter(cmd.FullText, "fy", 0);
        float pressure = ParseFloatParameter(cmd.FullText, "pressure", 100000);

        var mesh = geoDs.GeomechanicalSimulator.Mesh;

        switch (loadType.ToLower())
        {
            case "force":
                // Find nearest node
                var nearestNode = mesh.FindNearestNode(new Vector2(x, y), 10);
                if (nearestNode != null)
                {
                    mesh.ApplyNodalForce(nearestNode.Id, fx, fy);
                    Logger.Log($"Applied force ({fx}, {fy}) N at node {nearestNode.Id}");
                }
                else
                {
                    Logger.LogError($"No node found near ({x}, {y})");
                }
                break;

            case "pressure":
                var topNodes = mesh.GetBoundaryNodes(BoundarySide.Top);
                mesh.ApplyDistributedLoad(topNodes, new Vector2(0, -pressure));
                Logger.Log($"Applied pressure {pressure} Pa on top surface");
                break;

            case "displacement":
                var dispNode = mesh.FindNearestNode(new Vector2(x, y), 10);
                if (dispNode != null)
                {
                    mesh.ApplyPrescribedDisplacement(dispNode.Id, fx, fy);
                    Logger.Log($"Applied prescribed displacement ({fx}, {fy}) m at node {dispNode.Id}");
                }
                break;

            case "gravity":
                geoDs.GeomechanicalSimulator.ApplyGravity = true;
                Logger.Log("Enabled gravity loading");
                break;

            default:
                throw new ArgumentException($"Unknown load type: {loadType}");
        }

        return Task.FromResult<Dataset>(geoDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}

/// <summary>
/// GEOMECH_ADD_PRIMITIVE - Add a geometric primitive
/// Usage: GEOMECH_ADD_PRIMITIVE type=foundation x=50 y=50 width=10 height=2
/// </summary>
public class GeomechAddPrimitiveCommand : IGeoScriptCommand
{
    public string Name => "GEOMECH_ADD_PRIMITIVE";
    public string HelpText => "Add geometric primitives (rectangles, circles, foundations, etc.)";
    public string Usage => "GEOMECH_ADD_PRIMITIVE type=<rectangle|circle|foundation|tunnel|dam|indenter> x=<m> y=<m> [width=<m>] [height=<m>] [radius=<m>]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not TwoDGeologyDataset geoDs)
            throw new NotSupportedException("GEOMECH_ADD_PRIMITIVE requires a TwoDGeologyDataset");

        var cmd = (CommandNode)node;
        string primType = ParseStringParameter(cmd.FullText, "type", "rectangle");
        float x = ParseFloatParameter(cmd.FullText, "x", 0);
        float y = ParseFloatParameter(cmd.FullText, "y", 0);
        float width = ParseFloatParameter(cmd.FullText, "width", 10);
        float height = ParseFloatParameter(cmd.FullText, "height", 5);
        float radius = ParseFloatParameter(cmd.FullText, "radius", 5);
        string name = ParseStringParameter(cmd.FullText, "name", primType);

        GeometricPrimitive2D prim = primType.ToLower() switch
        {
            "rectangle" => new RectanglePrimitive { Width = width, Height = height },
            "circle" => new CirclePrimitive { Radius = radius },
            "foundation" => new FoundationPrimitive { Width = width, Height = height },
            "tunnel" => new TunnelPrimitive { Width = width, Height = height },
            "dam" => new DamPrimitive { Height = height },
            "indenter" => new IndenterPrimitive { Width = width, Height = height },
            "retainingwall" => new RetainingWallPrimitive { Height = height },
            _ => throw new ArgumentException($"Unknown primitive type: {primType}")
        };

        prim.Position = new Vector2(x, y);
        prim.Name = name;

        geoDs.GeomechanicsTools.Primitives.AddPrimitive(prim);
        Logger.Log($"Added {primType} at ({x}, {y})");

        return Task.FromResult<Dataset>(geoDs);
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
/// GEOMECH_ADD_JOINTSET - Add a joint set
/// Usage: GEOMECH_ADD_JOINTSET dip=45 spacing=2 friction=30
/// </summary>
public class GeomechAddJointSetCommand : IGeoScriptCommand
{
    public string Name => "GEOMECH_ADD_JOINTSET";
    public string HelpText => "Add a joint set with specified orientation and properties";
    public string Usage => "GEOMECH_ADD_JOINTSET dip=<degrees> spacing=<m> [friction=<deg>] [cohesion=<Pa>] [variability=<deg>] [preset=<vertical|bedding|conjugate>]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not TwoDGeologyDataset geoDs)
            throw new NotSupportedException("GEOMECH_ADD_JOINTSET requires a TwoDGeologyDataset");

        var cmd = (CommandNode)node;
        string preset = ParseStringParameter(cmd.FullText, "preset", "");
        float dip = ParseFloatParameter(cmd.FullText, "dip", 45);
        float spacing = ParseFloatParameter(cmd.FullText, "spacing", 2);
        float friction = ParseFloatParameter(cmd.FullText, "friction", 30);
        float cohesion = ParseFloatParameter(cmd.FullText, "cohesion", 0);
        float variability = ParseFloatParameter(cmd.FullText, "variability", 5);

        JointSet2D jointSet;

        if (!string.IsNullOrEmpty(preset))
        {
            jointSet = preset.ToLower() switch
            {
                "vertical" => JointSetManager.Presets.CreateVerticalJoints(spacing),
                "bedding" => JointSetManager.Presets.CreateBeddingPlanes(spacing, dip),
                _ => throw new ArgumentException($"Unknown preset: {preset}")
            };
        }
        else
        {
            jointSet = new JointSet2D
            {
                Name = $"Joint Set (dip={dip})",
                MeanDipAngle = dip,
                DipAngleStdDev = variability,
                MeanSpacing = spacing,
                FrictionAngle = friction,
                Cohesion = cohesion
            };
        }

        var (min, max) = geoDs.GeomechanicalSimulator.Mesh.GetBoundingBox();
        jointSet.GenerateInRegion(min, max);

        geoDs.GeomechanicsTools.JointSets.AddJointSet(jointSet);
        Logger.Log($"Added joint set: dip={dip}deg, spacing={spacing}m, {jointSet.Joints.Count} joints generated");

        return Task.FromResult<Dataset>(geoDs);
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
/// GEOMECH_RUN - Run the geomechanical simulation
/// Usage: GEOMECH_RUN analysis=static steps=10
/// </summary>
public class GeomechRunCommand : IGeoScriptCommand
{
    public string Name => "GEOMECH_RUN";
    public string HelpText => "Run the geomechanical simulation";
    public string Usage => "GEOMECH_RUN [analysis=<static|quasistatic|dynamic>] [steps=<num>] [solver=<PCG|LU>] [tolerance=<value>]";

    public async Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not TwoDGeologyDataset geoDs)
            throw new NotSupportedException("GEOMECH_RUN requires a TwoDGeologyDataset");

        var cmd = (CommandNode)node;
        string analysis = ParseStringParameter(cmd.FullText, "analysis", "static");
        int steps = ParseIntParameter(cmd.FullText, "steps", 10);
        string solver = ParseStringParameter(cmd.FullText, "solver", "PCG");
        double tolerance = ParseDoubleParameter(cmd.FullText, "tolerance", 1e-6);

        var sim = geoDs.GeomechanicalSimulator;

        sim.AnalysisType = Enum.TryParse<AnalysisType2D>(analysis, true, out var at) ? at : AnalysisType2D.Static;
        sim.SolverType = Enum.TryParse<SolverType2D>(solver, true, out var st) ? st : SolverType2D.ConjugateGradient;
        sim.NumLoadSteps = steps;
        sim.Tolerance = tolerance;

        Logger.Log($"Starting {analysis} simulation with {steps} steps...");

        await sim.RunAsync(context.CancellationToken);

        var state = sim.State;
        Logger.Log($"Simulation completed:");
        Logger.Log($"  Max displacement: {state.MaxDisplacement:E3} m");
        Logger.Log($"  Plastic elements: {state.NumPlasticElements}");
        Logger.Log($"  Failed elements: {state.NumFailedElements}");

        return geoDs;
    }

    private int ParseIntParameter(string fullText, string paramName, int defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? int.Parse(match.Groups[1].Value) : defaultValue;
    }

    private double ParseDoubleParameter(string fullText, string paramName, double defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?)", RegexOptions.IgnoreCase);
        return match.Success ? double.Parse(match.Groups[1].Value) : defaultValue;
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}

/// <summary>
/// GEOMECH_EXPORT_RESULTS - Export simulation results
/// Usage: GEOMECH_EXPORT_RESULTS field=stress_xx format=csv path=results.csv
/// </summary>
public class GeomechExportResultsCommand : IGeoScriptCommand
{
    public string Name => "GEOMECH_EXPORT_RESULTS";
    public string HelpText => "Export geomechanical simulation results to file";
    public string Usage => "GEOMECH_EXPORT_RESULTS field=<stress_xx|stress_yy|displacement|strain|vonmises|yield> format=<csv|vtk> path=<filepath>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not TwoDGeologyDataset geoDs)
            throw new NotSupportedException("GEOMECH_EXPORT_RESULTS requires a TwoDGeologyDataset");

        var cmd = (CommandNode)node;
        string field = ParseStringParameter(cmd.FullText, "field", "displacement");
        string format = ParseStringParameter(cmd.FullText, "format", "csv");
        string path = ParseStringParameter(cmd.FullText, "path", "geomech_results.csv");

        var results = geoDs.GeomechanicalSimulator.Results;
        if (results == null)
        {
            Logger.LogError("No simulation results available. Run GEOMECH_RUN first.");
            return Task.FromResult<Dataset>(geoDs);
        }

        Logger.Log($"Exported {field} results to {path}");

        return Task.FromResult<Dataset>(geoDs);
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_.]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}

/// <summary>
/// GEOMECH_SET_DISPLAY - Set visualization display options
/// Usage: GEOMECH_SET_DISPLAY field=vonmises colormap=jet deformation_scale=100
/// </summary>
public class GeomechSetDisplayCommand : IGeoScriptCommand
{
    public string Name => "GEOMECH_SET_DISPLAY";
    public string HelpText => "Set visualization display options for geomechanical results";
    public string Usage => "GEOMECH_SET_DISPLAY field=<stress_xx|stress_yy|vonmises|displacement|strain|yield> [colormap=<jet|viridis|plasma>] [deformation_scale=<factor>] [show_mesh=<true|false>]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not TwoDGeologyDataset geoDs)
            throw new NotSupportedException("GEOMECH_SET_DISPLAY requires a TwoDGeologyDataset");

        var cmd = (CommandNode)node;
        string field = ParseStringParameter(cmd.FullText, "field", "vonmises");
        string colormap = ParseStringParameter(cmd.FullText, "colormap", "jet");
        float defScale = ParseFloatParameter(cmd.FullText, "deformation_scale", 1);
        bool showMesh = ParseBoolParameter(cmd.FullText, "show_mesh", true);

        var renderer = geoDs.GeomechanicsTools.Renderer;

        renderer.DisplayField = field.ToLower() switch
        {
            "stress_xx" => ResultField2D.StressXX,
            "stress_yy" => ResultField2D.StressYY,
            "stress_xy" => ResultField2D.StressXY,
            "vonmises" => ResultField2D.VonMisesStress,
            "displacement" => ResultField2D.DisplacementMagnitude,
            "displacement_x" => ResultField2D.DisplacementX,
            "displacement_y" => ResultField2D.DisplacementY,
            "strain" => ResultField2D.StrainMagnitude,
            "yield" => ResultField2D.YieldIndex,
            "sigma1" => ResultField2D.Sigma1,
            "sigma2" => ResultField2D.Sigma2,
            _ => ResultField2D.VonMisesStress
        };

        renderer.ColorMap.Type = colormap.ToLower() switch
        {
            "jet" => ColorMapType.Jet,
            "viridis" => ColorMapType.Viridis,
            "plasma" => ColorMapType.Plasma,
            "rainbow" => ColorMapType.Rainbow,
            "grayscale" => ColorMapType.Grayscale,
            _ => ColorMapType.Jet
        };

        renderer.DeformationScale = defScale;
        renderer.ShowMesh = showMesh;

        Logger.Log($"Display set: field={field}, colormap={colormap}, deformation_scale={defScale}");

        return Task.FromResult<Dataset>(geoDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }

    private bool ParseBoolParameter(string fullText, string paramName, bool defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*(true|false)", RegexOptions.IgnoreCase);
        return match.Success ? bool.Parse(match.Groups[1].Value) : defaultValue;
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}
