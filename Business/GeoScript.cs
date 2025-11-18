// GeoscientistToolkit/Business/GeoScript/GeoScript.cs

using System.Data;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.Business.Thermodynamics;
using GeoscientistToolkit.Business.GeoScriptImageCommands;
using GeoscientistToolkit.Business.GeoScriptUtilityCommands;
using GeoscientistToolkit.Business.GeoScriptCtImageStackCommands;
using GeoscientistToolkit.Business.GeoScriptBoreholeCommands;
using GeoscientistToolkit.Business.GeoScriptGISExtendedCommands;
using GeoscientistToolkit.Business.GeoScriptPNMCommands;
using GeoscientistToolkit.Business.GeoScriptSeismicCommands;
using GeoscientistToolkit.Business.GeoScriptMiscDatasetCommands;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.Util;
using NCalc;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;

// Added for expression evaluation

namespace GeoscientistToolkit.Business.GeoScript;

/// <summary>
///     Main entry point for parsing and executing GeoScript.
/// </summary>
public class GeoScriptEngine
{
    private readonly GeoScriptParser _parser = new();

    public async Task<Dataset> ExecuteAsync(string script, Dataset inputDataset,
        Dictionary<string, Dataset> contextDatasets = null)
    {
        try
        {
            var commandNode = _parser.Parse(script);
            var context = new GeoScriptContext
            {
                InputDataset = inputDataset,
                AvailableDatasets = contextDatasets ?? new Dictionary<string, Dataset>()
            };
            return await ExecuteNodeAsync(commandNode, context);
        }
        catch (Exception ex)
        {
            Logger.LogError($"GeoScript execution failed: {ex.Message}");
            throw; // Re-throw to be caught by the UI
        }
    }

    private async Task<Dataset> ExecuteNodeAsync(AstNode node, GeoScriptContext context)
    {
        if (node is CommandNode cmd)
        {
            var command = CommandRegistry.GetCommand(cmd.CommandName);
            if (command == null)
                throw new NotSupportedException($"Command '{cmd.CommandName}' not recognized.");

            return await command.ExecuteAsync(context, cmd);
        }

        if (node is PipelineNode pipeline)
        {
            var leftResult = await ExecuteNodeAsync(pipeline.Left, context);
            // The output of the left side becomes the input for the right side
            var nextContext = new GeoScriptContext
            {
                InputDataset = leftResult,
                AvailableDatasets = context.AvailableDatasets
            };
            return await ExecuteNodeAsync(pipeline.Right, nextContext);
        }

        throw new InvalidOperationException("Invalid AST structure.");
    }
}

/// <summary>
///     A simple parser for the GeoScript language.
/// </summary>
public class GeoScriptParser
{
    public AstNode Parse(string script)
    {
        // Handle pipelines first, splitting by |>
        var pipelineParts = script.Split(new[] { "|>" }, StringSplitOptions.RemoveEmptyEntries);
        if (pipelineParts.Length > 1)
        {
            AstNode root = ParseCommand(pipelineParts[0]);
            for (var i = 1; i < pipelineParts.Length; i++)
                root = new PipelineNode
                {
                    Left = root,
                    Right = ParseCommand(pipelineParts[i])
                };
            return root;
        }

        return ParseCommand(script);
    }

    private CommandNode ParseCommand(string commandText)
    {
        commandText = commandText.Trim();
        var commandMatch = Regex.Match(commandText, @"^\w+");
        if (!commandMatch.Success)
            throw new ArgumentException("Invalid command syntax. Must start with a command word.");

        return new CommandNode
        {
            CommandName = commandMatch.Value,
            FullText = commandText
        };
    }
}

#region Language Core Classes

public class GeoScriptContext
{
    public Dataset InputDataset { get; set; }

    /// <summary>
    ///     Other datasets available in the current scope, for joins or cross-layer queries.
    ///     The key is the dataset name.
    /// </summary>
    public Dictionary<string, Dataset> AvailableDatasets { get; set; }
}

public interface IGeoScriptCommand
{
    string Name { get; }
    string HelpText { get; }
    string Usage { get; }
    Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node);
}

public static class CommandRegistry
{
    private static readonly Dictionary<string, IGeoScriptCommand> Commands;

    static CommandRegistry()
    {
        var commandList = new List<IGeoScriptCommand>
        {
            // Table Commands
            new SelectCommand(),
            new CalculateCommand(),
            new SortByCommand(),
            new GroupByCommand(),
            new RenameCommand(),
            new DropCommand(),
            new TakeCommand(),
            new UniqueCommand(),
            new JoinCommand(),

            // GIS Vector Commands
            new BufferCommand(),
            new DissolveCommand(),
            new ExplodeCommand(),
            new CleanCommand(),

            // GIS Raster Commands
            new ReclassifyCommand(),
            new SlopeCommand(),
            new AspectCommand(),
            new ContourCommand(),

            // Thermodynamics Commands
            new CreateDiagramCommand(),
            new EquilibrateCommand(),
            new SaturationCommand(),
            new SaturationIndexCommand(),
            new BalanceReactionCommand(),
            new EvaporateCommand(),
            new ReactCommand(),
            new SpeciateCommand(),
            new DiagnoseSpeciateCommand(),
            new DiagnosticThermodynamicCommand(),

            // Thermodynamics Extensions
            new CalculatePhasesCommand(),
            new CalculateCarbonateAlkalinityCommand(),

            // Petrology Commands (Igneous & Metamorphic)
            new FractionateMagmaCommand(),
            new LiquidusSolidusCommand(),
            new MetamorphicPTCommand(),

            // PhysicoChem Reactor Commands
            new CreateReactorCommand(),
            new AddDomainCommand(),
            new SetMineralsCommand(),
            new RunSimulationCommand(),

            // PNM Reactive Transport Commands
            new RunPNMReactiveTransportCommand(),
            new SetPNMSpeciesCommand(),
            new SetPNMMineralsCommand(),
            new ExportPNMResultsCommand(),

            // Image Processing Commands
            new BrightnessContrastCommand(),
            new FilterCommand(),
            new ThresholdCommand(),
            new BinarizeCommand(),
            new GrayscaleCommand(),
            new InvertCommand(),
            new NormalizeCommand(),

            // Utility Commands
            new ListOpsCommand(),
            new DispTypeCommand(),
            new UnloadCommand(),
            new InfoCommand(),

            // CT Image Stack Commands
            new CtSegmentCommand(),
            new CtFilter3DCommand(),
            new CtAddMaterialCommand(),
            new CtRemoveMaterialCommand(),
            new CtAnalyzePorosityCommand(),
            new CtCropCommand(),
            new CtExtractSliceCommand(),
            new CtLabelAnalysisCommand(),

            // Borehole Commands
            new BhAddLithologyCommand(),
            new BhRemoveLithologyCommand(),
            new BhAddLogCommand(),
            new BhCalculatePorosityCommand(),
            new BhCalculateSaturationCommand(),
            new BhDepthShiftCommand(),
            new BhCorrelationCommand(),

            // GIS Extended Commands
            new GisAddLayerCommand(),
            new GisRemoveLayerCommand(),
            new GisIntersectCommand(),
            new GisUnionCommand(),
            new GisClipCommand(),
            new GisCalculateAreaCommand(),
            new GisCalculateLengthCommand(),
            new GisReprojectCommand(),

            // PNM Commands
            new PnmFilterPoresCommand(),
            new PnmFilterThroatsCommand(),
            new PnmCalculatePermeabilityCommand(),
            new PnmDrainageSimulationCommand(),
            new PnmImbibitionSimulationCommand(),
            new PnmExtractLargestClusterCommand(),
            new PnmStatisticsCommand(),

            // Seismic Commands
            new SeisFilterCommand(),
            new SeisAGCCommand(),
            new SeisVelocityAnalysisCommand(),
            new SeisNMOCorrectionCommand(),
            new SeisStackCommand(),
            new SeisMigrationCommand(),
            new SeisPickHorizonCommand(),

            // AcousticVolume Commands
            new AcousticThresholdCommand(),
            new AcousticExtractTargetsCommand(),

            // Mesh3D Commands
            new MeshSmoothCommand(),
            new MeshDecimateCommand(),
            new MeshRepairCommand(),
            new MeshCalculateVolumeCommand(),

            // Video Commands
            new VideoExtractFrameCommand(),
            new VideoStabilizeCommand(),

            // Audio Commands
            new AudioTrimCommand(),
            new AudioNormalizeCommand(),

            // Text Commands
            new TextSearchCommand(),
            new TextReplaceCommand(),
            new TextStatisticsCommand()
        };
        Commands = commandList.ToDictionary(c => c.Name.ToUpper(), c => c);
    }

    public static IGeoScriptCommand GetCommand(string name)
    {
        Commands.TryGetValue(name.ToUpper(), out var command);
        return command;
    }

    public static IEnumerable<IGeoScriptCommand> GetAllCommands()
    {
        return Commands.Values;
    }
}

#endregion

#region AST Nodes

public abstract class AstNode
{
}

public class CommandNode : AstNode
{
    public string CommandName { get; set; }
    public string FullText { get; set; }
}

public class PipelineNode : AstNode
{
    public AstNode Left { get; set; }
    public AstNode Right { get; set; }
}

#endregion

#region --- Table Command Implementations ---

public class SelectCommand : IGeoScriptCommand
{
    public string Name => "SELECT";
    public string HelpText => "Filters rows or features based on an attribute or spatial condition.";
    public string Usage => "SELECT WHERE <attribute_condition> | SELECT <spatial_condition> @'OtherLayer'";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is TableDataset tableDs)
            return ExecuteTableSelect(tableDs, (CommandNode)node);

        if (context.InputDataset is GISDataset gisDs)
            return ExecuteGisSelect(gisDs, (CommandNode)node, context.AvailableDatasets);

        throw new NotSupportedException("SELECT is not supported for this dataset type.");
    }

    private Task<Dataset> ExecuteTableSelect(TableDataset tableDs, CommandNode cmd)
    {
        var whereMatch = Regex.Match(cmd.FullText, @"WHERE\s+(.*)", RegexOptions.IgnoreCase);
        if (!whereMatch.Success) throw new ArgumentException("Table SELECT requires a WHERE clause.");

        var filterExpression = Regex.Replace(whereMatch.Groups[1].Value.Trim(), @"'([^']+)'", "[$1]");
        var dataTable = tableDs.GetDataTable();
        var newTable = dataTable.Clone();
        var filteredRows = dataTable.Select(filterExpression);
        foreach (var row in filteredRows) newTable.ImportRow(row);

        return Task.FromResult<Dataset>(new TableDataset($"{tableDs.Name}_Selected", newTable));
    }

    private Task<Dataset> ExecuteGisSelect(GISDataset gisDs, CommandNode cmd,
        IReadOnlyDictionary<string, Dataset> availableDatasets)
    {
        var whereMatch = Regex.Match(cmd.FullText, @"WHERE\s+(.*)", RegexOptions.IgnoreCase);
        var spatialMatch = Regex.Match(cmd.FullText, @"(INTERSECTS|CONTAINS|WITHIN)\s+@'([^']+)'",
            RegexOptions.IgnoreCase);

        var newDataset = gisDs.CloneWithFeatures(new List<GISFeature>(), "_Selected");

        if (whereMatch.Success) // Attribute query
        {
            var expression = new Expression(whereMatch.Groups[1].Value.Trim());
            foreach (var feature in gisDs.Layers.SelectMany(l => l.Features))
            {
                foreach (var prop in feature.Properties) expression.Parameters[prop.Key] = prop.Value;
                if (expression.Evaluate() as bool? == true) newDataset.Layers.First().Features.Add(feature);
            }
        }
        else if (spatialMatch.Success) // Spatial query
        {
            var operation = spatialMatch.Groups[1].Value.ToUpper();
            var otherDatasetName = spatialMatch.Groups[2].Value;
            if (!availableDatasets.TryGetValue(otherDatasetName, out var otherDataset) ||
                otherDataset is not GISDataset otherGisDs)
                throw new ArgumentException(
                    $"Referenced dataset '@{otherDatasetName}' not found or is not a GIS dataset.");

            var allOtherGeoms = otherGisDs.Layers.SelectMany(l => l.Features).Select(f => gisDs.ConvertToNTSGeometry(f))
                .ToList();
            var combinedOtherGeom = CascadedPolygonUnion.Union(allOtherGeoms);

            foreach (var feature in gisDs.Layers.SelectMany(l => l.Features))
            {
                var ntsGeom = gisDs.ConvertToNTSGeometry(feature);
                var match = operation switch
                {
                    "INTERSECTS" => ntsGeom.Intersects(combinedOtherGeom),
                    "CONTAINS" => ntsGeom.Contains(combinedOtherGeom),
                    "WITHIN" => ntsGeom.Within(combinedOtherGeom),
                    _ => false
                };
                if (match) newDataset.Layers.First().Features.Add(feature);
            }
        }
        else
        {
            throw new ArgumentException("Invalid GIS SELECT syntax.");
        }

        newDataset.UpdateBounds();
        return Task.FromResult<Dataset>(newDataset);
    }
}

public class CalculateCommand : IGeoScriptCommand
{
    public string Name => "CALCULATE";
    public string HelpText => "Creates a new column/attribute from a calculation.";
    public string Usage => "CALCULATE 'NewField' = <expression>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is TableDataset tableDs)
            return ExecuteTableCalculate(tableDs, (CommandNode)node);

        if (context.InputDataset is GISDataset gisDs)
            return ExecuteVectorCalculate(gisDs, (CommandNode)node);

        throw new NotSupportedException("CALCULATE is not supported for this dataset type.");
    }

    private Task<Dataset> ExecuteTableCalculate(TableDataset tableDs, CommandNode cmd)
    {
        var calcMatch = Regex.Match(cmd.FullText, @"CALCULATE\s+'([^']+)'\s*=\s*(.*)", RegexOptions.IgnoreCase);
        if (!calcMatch.Success) throw new ArgumentException("Invalid table CALCULATE syntax.");

        var newColumnName = calcMatch.Groups[1].Value;
        var expressionStr = calcMatch.Groups[2].Value.Trim();

        // Use DataTable's built-in expression engine
        var expression = Regex.Replace(expressionStr, @"'([^']+)'", "[$1]");
        var newTable = tableDs.GetDataTable().Copy();
        newTable.Columns.Add(newColumnName, typeof(double), expression);

        return Task.FromResult<Dataset>(new TableDataset($"{tableDs.Name}_Calculated", newTable));
    }

    private Task<Dataset> ExecuteVectorCalculate(GISDataset gisDs, CommandNode cmd)
    {
        var calcMatch = Regex.Match(cmd.FullText, @"CALCULATE\s+'([^']+)'\s*=\s*(.*)", RegexOptions.IgnoreCase);
        if (!calcMatch.Success) throw new ArgumentException("Invalid vector CALCULATE syntax.");

        var newAttributeName = calcMatch.Groups[1].Value;
        var expressionStr = calcMatch.Groups[2].Value.Trim();

        var newDataset = gisDs.CloneWithFeatures(new List<GISFeature>(), "_Calculated");
        var newLayer = newDataset.Layers.First();

        foreach (var feature in gisDs.Layers.SelectMany(l => l.Features))
        {
            var newFeature = feature.Clone();
            var expression = new Expression(expressionStr, EvaluateOptions.IgnoreCase);

            // Populate parameters from existing attributes
            foreach (var prop in newFeature.Properties)
            {
                // Ensure parameter names are valid for NCalc
                var paramName = Regex.Replace(prop.Key, @"[^A-Za-z0-9_]", "");
                expression.Parameters[paramName] = prop.Value;
            }

            // Add special geometry parameters
            var ntsGeom = gisDs.ConvertToNTSGeometry(newFeature);
            if (ntsGeom != null)
            {
                expression.Parameters["AREA"] = ntsGeom.Area;
                expression.Parameters["LENGTH"] = ntsGeom.Length;
                expression.Parameters["X"] = ntsGeom.Centroid.X;
                expression.Parameters["Y"] = ntsGeom.Centroid.Y;
            }

            try
            {
                var result = expression.Evaluate();
                newFeature.Properties[newAttributeName] = result;
            }
            catch (Exception ex)
            {
                // Could fail if attributes are missing, add as null
                newFeature.Properties[newAttributeName] = null;
                Logger.LogWarning($"Could not calculate '{expressionStr}' for one feature: {ex.Message}");
            }

            newLayer.Features.Add(newFeature);
        }

        newDataset.UpdateBounds();
        return Task.FromResult<Dataset>(newDataset);
    }
}

public class SortByCommand : IGeoScriptCommand
{
    public string Name => "SORTBY";
    public string HelpText => "Sorts a table dataset by a specific column.";
    public string Usage => "SORTBY 'ColumnName' <ASC|DESC>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not TableDataset tableDs)
            throw new NotSupportedException("SORTBY only works on Table Datasets.");

        var cmd = (CommandNode)node;
        var sortMatch = Regex.Match(cmd.FullText, @"SORTBY\s+'([^']+)'(?:\s+(ASC|DESC))?", RegexOptions.IgnoreCase);
        if (!sortMatch.Success) throw new ArgumentException("Invalid SORTBY syntax.");

        var columnName = sortMatch.Groups[1].Value;
        var direction = sortMatch.Groups[2].Value.ToUpper() == "DESC" ? "DESC" : "ASC";
        var newTable = tableDs.GetDataTable().Clone();
        var sortedRows = tableDs.GetDataTable().Select("", $"[{columnName}] {direction}");
        foreach (var row in sortedRows) newTable.ImportRow(row);

        return Task.FromResult<Dataset>(new TableDataset($"{tableDs.Name}_Sorted", newTable));
    }
}

public class GroupByCommand : IGeoScriptCommand
{
    public string Name => "GROUPBY";
    public string HelpText => "Groups rows and calculates aggregate values (COUNT, SUM, AVG, MIN, MAX).";
    public string Usage => "GROUPBY 'GroupCol' AGGREGATE <FUNC('ValCol') AS 'Alias', ...>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not TableDataset tableDs)
            throw new NotSupportedException("GROUPBY only works on Table Datasets.");

        var cmd = (CommandNode)node;
        var groupByMatch =
            Regex.Match(cmd.FullText, @"GROUPBY\s+'([^']+)'\s+AGGREGATE\s+(.*)", RegexOptions.IgnoreCase);
        if (!groupByMatch.Success) throw new ArgumentException("Invalid GROUPBY syntax.");

        var groupCol = groupByMatch.Groups[1].Value;
        var aggText = groupByMatch.Groups[2].Value;

        var sourceTable = tableDs.GetDataTable();
        if (!sourceTable.Columns.Contains(groupCol))
            throw new ArgumentException($"Grouping column '{groupCol}' not found.");

        // Parse aggregation clauses
        var aggMatches = Regex.Matches(aggText, @"(COUNT|SUM|AVG|MIN|MAX)\s*\(\s*'([^']+)'\s*\)\s+AS\s+'([^']+)'",
            RegexOptions.IgnoreCase);
        if (aggMatches.Count == 0) throw new ArgumentException("No valid aggregate functions found.");

        // Create the result table structure
        var resultTable = new DataTable();
        resultTable.Columns.Add(groupCol, sourceTable.Columns[groupCol].DataType);
        foreach (Match match in
                 aggMatches)
            resultTable.Columns.Add(match.Groups[3].Value, typeof(double)); // Aggregates are typically numeric

        // Perform the grouping and aggregation
        var groups = sourceTable.AsEnumerable().GroupBy(row => row[groupCol]);

        foreach (var group in groups)
        {
            var newRow = resultTable.NewRow();
            newRow[groupCol] = group.Key;

            foreach (Match match in aggMatches)
            {
                var func = match.Groups[1].Value.ToUpper();
                var valCol = match.Groups[2].Value;
                var alias = match.Groups[3].Value;

                var values = group.Select(row => Convert.ToDouble(row[valCol])).ToList();

                newRow[alias] = func switch
                {
                    "COUNT" => values.Count,
                    "SUM" => values.Sum(),
                    "AVG" => values.Average(),
                    "MIN" => values.Min(),
                    "MAX" => values.Max(),
                    _ => 0
                };
            }

            resultTable.Rows.Add(newRow);
        }

        return Task.FromResult<Dataset>(new TableDataset($"{tableDs.Name}_Grouped", resultTable));
    }
}

public class RenameCommand : IGeoScriptCommand
{
    public string Name => "RENAME";
    public string HelpText => "Renames a column in a table dataset.";
    public string Usage => "RENAME 'OldName' TO 'NewName'";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not TableDataset tableDs)
            throw new NotSupportedException("RENAME only works on Table Datasets.");

        var cmd = (CommandNode)node;
        var renameMatch = Regex.Match(cmd.FullText, @"RENAME\s+'([^']+)'\s+TO\s+'([^']+)'", RegexOptions.IgnoreCase);
        if (!renameMatch.Success) throw new ArgumentException("Invalid RENAME syntax.");

        var oldName = renameMatch.Groups[1].Value;
        var newName = renameMatch.Groups[2].Value;
        var newTable = tableDs.GetDataTable().Copy();
        if (newTable.Columns.Contains(oldName))
            newTable.Columns[oldName].ColumnName = newName;
        else
            throw new ArgumentException($"Column '{oldName}' not found.");

        return Task.FromResult<Dataset>(new TableDataset($"{tableDs.Name}_Renamed", newTable));
    }
}

public class DropCommand : IGeoScriptCommand
{
    public string Name => "DROP";
    public string HelpText => "Removes one or more columns from a table dataset.";
    public string Usage => "DROP 'Column1', 'Column2', ...";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not TableDataset tableDs)
            throw new NotSupportedException("DROP only works on Table Datasets.");

        var cmd = (CommandNode)node;
        var columnsToDrop = Regex.Matches(cmd.FullText, @"'([^']+)'")
            .Select(m => m.Groups[1].Value)
            .ToList();

        var newTable = tableDs.GetDataTable().Copy();
        foreach (var colName in columnsToDrop)
            if (newTable.Columns.Contains(colName))
                newTable.Columns.Remove(colName);
            else
                Logger.LogWarning($"Column '{colName}' not found to drop.");

        return Task.FromResult<Dataset>(new TableDataset($"{tableDs.Name}_Reduced", newTable));
    }
}

public class TakeCommand : IGeoScriptCommand
{
    public string Name => "TAKE";
    public string HelpText => "Selects the top N rows from a table.";
    public string Usage => "TAKE <count>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not TableDataset tableDs)
            throw new NotSupportedException("TAKE only works on Table Datasets.");

        var cmd = (CommandNode)node;
        var takeMatch = Regex.Match(cmd.FullText, @"TAKE\s+(\d+)", RegexOptions.IgnoreCase);
        if (!takeMatch.Success) throw new ArgumentException("Invalid TAKE syntax.");

        var count = int.Parse(takeMatch.Groups[1].Value);
        var dataTable = tableDs.GetDataTable();
        var newTable = dataTable.Clone();
        for (var i = 0; i < Math.Min(count, dataTable.Rows.Count); i++) newTable.ImportRow(dataTable.Rows[i]);

        return Task.FromResult<Dataset>(new TableDataset($"{tableDs.Name}_Top{count}", newTable));
    }
}

public class UniqueCommand : IGeoScriptCommand
{
    public string Name => "UNIQUE";
    public string HelpText => "Creates a new table with unique values from a specified column.";
    public string Usage => "UNIQUE 'ColumnName'";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not TableDataset tableDs)
            throw new NotSupportedException("UNIQUE only works on Table Datasets.");

        var cmd = (CommandNode)node;
        var uniqueMatch = Regex.Match(cmd.FullText, @"UNIQUE\s+'([^']+)'", RegexOptions.IgnoreCase);
        if (!uniqueMatch.Success) throw new ArgumentException("Invalid UNIQUE syntax.");

        var colName = uniqueMatch.Groups[1].Value;
        var dataTable = tableDs.GetDataTable();
        if (!dataTable.Columns.Contains(colName)) throw new ArgumentException($"Column '{colName}' not found.");

        var uniqueView = new DataView(dataTable).ToTable(true, colName);

        return Task.FromResult<Dataset>(new TableDataset($"Unique_{colName}", uniqueView));
    }
}

public class JoinCommand : IGeoScriptCommand
{
    public string Name => "JOIN";
    public string HelpText => "Merges attributes from another dataset based on a key.";
    public string Usage => "JOIN @'OtherDataset' ON 'LeftKey' = 'RightKey'";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        var cmd = (CommandNode)node;
        var joinMatch = Regex.Match(cmd.FullText, @"JOIN\s+@'([^']+)'\s+ON\s+'([^']+)'\s*=\s*'([^']+)'",
            RegexOptions.IgnoreCase);
        if (!joinMatch.Success) throw new ArgumentException("Invalid JOIN syntax.");

        var otherDsName = joinMatch.Groups[1].Value;
        var leftKey = joinMatch.Groups[2].Value;
        var rightKey = joinMatch.Groups[3].Value;

        if (!context.AvailableDatasets.TryGetValue(otherDsName, out var otherDs))
            throw new ArgumentException($"Join dataset '@{otherDsName}' not found in project.");

        if (context.InputDataset is not GISDataset leftGis || otherDs is not TableDataset rightTable)
            throw new NotSupportedException("JOIN currently only supports joining a Table to a GIS Layer.");

        var newDataset = leftGis.CloneWithFeatures(new List<GISFeature>(), "_Joined");
        var newLayer = newDataset.Layers.First();

        var rightLookup = rightTable.GetDataTable().AsEnumerable()
            .ToLookup(row => row[rightKey]?.ToString());

        foreach (var feature in leftGis.Layers.SelectMany(l => l.Features))
        {
            var newFeature = feature.Clone();
            if (newFeature.Properties.TryGetValue(leftKey, out var key) && key != null)
            {
                var match = rightLookup[key.ToString()].FirstOrDefault();
                if (match != null)
                    foreach (DataColumn col in rightTable.GetDataTable().Columns)
                        if (col.ColumnName != rightKey) // Avoid duplicating key
                            newFeature.Properties[col.ColumnName] = match[col];
            }

            newLayer.Features.Add(newFeature);
        }

        newDataset.UpdateBounds();
        return Task.FromResult<Dataset>(newDataset);
    }
}

#endregion

#region --- GIS Vector Command Implementations ---

public class BufferCommand : IGeoScriptCommand
{
    public string Name => "BUFFER";
    public string HelpText => "Creates a buffer zone around vector features.";
    public string Usage => "BUFFER <distance>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not GISDataset gisDs)
            throw new NotSupportedException("BUFFER only works on GIS Datasets.");

        var cmd = (CommandNode)node;
        var bufferMatch = Regex.Match(cmd.FullText, @"BUFFER\s+([\d\.]+)", RegexOptions.IgnoreCase);
        if (!bufferMatch.Success) throw new ArgumentException("Invalid BUFFER syntax. Usage: BUFFER <distance>");

        var distance = double.Parse(bufferMatch.Groups[1].Value);

        var newDataset = gisDs.CloneWithFeatures(new List<GISFeature>(), "_Buffered");
        var newLayer = newDataset.Layers.First();

        foreach (var feature in gisDs.Layers.SelectMany(l => l.Features))
        {
            var ntsGeom = gisDs.ConvertToNTSGeometry(feature);
            if (ntsGeom == null) continue;

            var bufferedGeom = GISOperationsImpl.BufferGeometry(ntsGeom, distance);
            var newFeature = GISDataset.ConvertNTSGeometry(bufferedGeom, feature.Properties);
            if (newFeature != null)
                newLayer.Features.Add(newFeature);
        }

        newDataset.UpdateBounds();
        return Task.FromResult<Dataset>(newDataset);
    }
}

public class DissolveCommand : IGeoScriptCommand
{
    public string Name => "DISSOLVE";
    public string HelpText => "Merges adjacent features based on a common attribute value.";
    public string Usage => "DISSOLVE 'FieldName'";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not GISDataset gisDs)
            throw new NotSupportedException("DISSOLVE only works on GIS Datasets.");

        var cmd = (CommandNode)node;
        var dissolveMatch = Regex.Match(cmd.FullText, @"DISSOLVE\s+'([^']+)'", RegexOptions.IgnoreCase);
        if (!dissolveMatch.Success) throw new ArgumentException("Invalid DISSOLVE syntax.");

        var fieldName = dissolveMatch.Groups[1].Value;
        var newDataset = gisDs.CloneWithFeatures(new List<GISFeature>(), "_Dissolved");

        var groups = gisDs.Layers.SelectMany(l => l.Features)
            .Where(f => f.Properties.ContainsKey(fieldName))
            .GroupBy(f => f.Properties[fieldName]);

        foreach (var group in groups)
        {
            var geometries = group.Select(f => gisDs.ConvertToNTSGeometry(f)).ToList();
            var unionGeom = CascadedPolygonUnion.Union(geometries);

            // Keep attributes from the first feature in the group
            var newFeature = GISDataset.ConvertNTSGeometry(unionGeom, group.First().Properties);
            if (newFeature != null)
                newDataset.Layers.First().Features.Add(newFeature);
        }

        newDataset.UpdateBounds();
        return Task.FromResult<Dataset>(newDataset);
    }
}

public class ExplodeCommand : IGeoScriptCommand
{
    public string Name => "EXPLODE";
    public string HelpText => "Converts multi-part features into single-part features.";
    public string Usage => "EXPLODE";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not GISDataset gisDs)
            throw new NotSupportedException("EXPLODE only works on GIS Datasets.");

        var newDataset = gisDs.CloneWithFeatures(new List<GISFeature>(), "_Exploded");
        var newLayer = newDataset.Layers.First();

        foreach (var feature in gisDs.Layers.SelectMany(l => l.Features))
        {
            var ntsGeom = gisDs.ConvertToNTSGeometry(feature);
            if (ntsGeom is GeometryCollection collection)
                for (var i = 0; i < collection.NumGeometries; i++)
                {
                    var singleGeom = collection.GetGeometryN(i);
                    var newFeature = GISDataset.ConvertNTSGeometry(singleGeom, feature.Properties);
                    if (newFeature != null) newLayer.Features.Add(newFeature);
                }
            else
                newLayer.Features.Add(feature.Clone()); // It's already single-part
        }

        newDataset.UpdateBounds();
        return Task.FromResult<Dataset>(newDataset);
    }
}

public class CleanCommand : IGeoScriptCommand
{
    public string Name => "CLEAN";
    public string HelpText => "Attempts to fix invalid geometries (e.g., self-intersections).";
    public string Usage => "CLEAN";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not GISDataset gisDs)
            throw new NotSupportedException("CLEAN only works on GIS Datasets.");

        var newDataset = gisDs.CloneWithFeatures(new List<GISFeature>(), "_Cleaned");
        var newLayer = newDataset.Layers.First();

        foreach (var feature in gisDs.Layers.SelectMany(l => l.Features))
        {
            var ntsGeom = gisDs.ConvertToNTSGeometry(feature);
            if (ntsGeom != null && !ntsGeom.IsValid)
            {
                var cleanedGeom = GISOperationsImpl.BufferGeometry(ntsGeom, 0); // Buffer by 0 trick
                var newFeature = GISDataset.ConvertNTSGeometry(cleanedGeom, feature.Properties);
                if (newFeature != null) newLayer.Features.Add(newFeature);
            }
            else
            {
                newLayer.Features.Add(feature.Clone()); // Geometry is already valid
            }
        }

        newDataset.UpdateBounds();
        return Task.FromResult<Dataset>(newDataset);
    }
}

#endregion

#region --- GIS Raster Command Implementations ---

public class ReclassifyCommand : IGeoScriptCommand
{
    public string Name => "RECLASSIFY";
    public string HelpText => "Reclassifies raster values into new categories.";
    public string Usage => "RECLASSIFY INTO 'NewLayer' RANGES(min-max: new_val, ...)";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not GISDataset gisDs)
            throw new NotSupportedException("RECLASSIFY only works on GIS Datasets.");
        var sourceLayer = gisDs.Layers.FirstOrDefault(l => l.Type == LayerType.Raster) as GISRasterLayer;
        if (sourceLayer == null) throw new NotSupportedException("Input dataset has no raster layer to reclassify.");

        var cmd = (CommandNode)node;
        var match = Regex.Match(cmd.FullText, @"INTO\s+'([^']+)'\s+RANGES\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
        if (!match.Success) throw new ArgumentException("Invalid RECLASSIFY syntax.");

        var newLayerName = match.Groups[1].Value;
        var rangesStr = match.Groups[2].Value;

        var ranges = new List<Tuple<float, float, float>>();
        var rangeMatches = Regex.Matches(rangesStr, @"([\d\.-]+)\s*-\s*([\d\.-]+)\s*:\s*([\d\.-]+)");
        foreach (Match rangeMatch in rangeMatches)
            ranges.Add(new Tuple<float, float, float>(
                float.Parse(rangeMatch.Groups[1].Value),
                float.Parse(rangeMatch.Groups[2].Value),
                float.Parse(rangeMatch.Groups[3].Value)
            ));

        var sourceData = sourceLayer.GetPixelData();
        var width = sourceLayer.Width;
        var height = sourceLayer.Height;
        var newData = new float[width, height];

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var val = sourceData[x, y];
            var newVal = float.NaN; // Default for no match
            foreach (var (min, max, assignedVal) in ranges)
                if (val >= min && val <= max)
                {
                    newVal = assignedVal;
                    break;
                }

            newData[x, y] = newVal;
        }

        var newGisDs = new GISDataset($"{gisDs.Name}_Reclassified", "");
        var newRasterLayer = new GISRasterLayer(newData, sourceLayer.Bounds) { Name = newLayerName };
        newGisDs.Layers.Add(newRasterLayer);
        newGisDs.UpdateBounds();

        return Task.FromResult<Dataset>(newGisDs);
    }
}

public class SlopeCommand : IGeoScriptCommand
{
    public string Name => "SLOPE";
    public string HelpText => "Calculates slope from a Digital Elevation Model (DEM).";
    public string Usage => "SLOPE AS 'NewLayerName'";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not GISDataset gisDs)
            throw new NotSupportedException("SLOPE only works on GIS Datasets.");
        var demLayer = gisDs.Layers.FirstOrDefault(l => l.Type == LayerType.Raster) as GISRasterLayer;
        if (demLayer == null)
            throw new NotSupportedException("Input dataset has no raster DEM layer for slope calculation.");

        var cmd = (CommandNode)node;
        var match = Regex.Match(cmd.FullText, @"AS\s+'([^']+)'", RegexOptions.IgnoreCase);
        if (!match.Success) throw new ArgumentException("Invalid SLOPE syntax.");

        var newLayerName = match.Groups[1].Value;

        var demData = demLayer.GetPixelData();
        var width = demLayer.Width;
        var height = demLayer.Height;
        var slopeData = new float[width, height];

        // Simple 3x3 Sobel operator for slope calculation
        for (var y = 1; y < height - 1; y++)
        for (var x = 1; x < width - 1; x++)
        {
            var p = demData; // shorthand
            var dx = p[x + 1, y - 1] + 2 * p[x + 1, y] + p[x + 1, y + 1] -
                     (p[x - 1, y - 1] + 2 * p[x - 1, y] + p[x - 1, y + 1]);
            var dy = p[x - 1, y + 1] + 2 * p[x, y + 1] + p[x + 1, y + 1] -
                     (p[x - 1, y - 1] + 2 * p[x, y - 1] + p[x + 1, y - 1]);

            var riseRun = Math.Sqrt(dx * dx + dy * dy);
            slopeData[x, y] = (float)(Math.Atan(riseRun) * (180 / Math.PI)); // Slope in degrees
        }

        var newGisDs = new GISDataset($"{gisDs.Name}_Slope", "");
        var newRasterLayer = new GISRasterLayer(slopeData, demLayer.Bounds) { Name = newLayerName };
        newGisDs.Layers.Add(newRasterLayer);
        newGisDs.UpdateBounds();

        return Task.FromResult<Dataset>(newGisDs);
    }
}

public class AspectCommand : IGeoScriptCommand
{
    public string Name => "ASPECT";
    public string HelpText => "Calculates an aspect raster (slope direction) from a DEM.";
    public string Usage => "ASPECT AS 'NewLayerName'";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        Logger.LogWarning("ASPECT command is not yet implemented. Requires complex raster algorithms.");
        return Task.FromResult(context.InputDataset);
    }
}

public class ContourCommand : IGeoScriptCommand
{
    public string Name => "CONTOUR";
    public string HelpText => "Generates vector contour lines from a DEM raster.";
    public string Usage => "CONTOUR INTERVAL <value> AS 'NewLayerName'";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        Logger.LogWarning(
            "CONTOUR command is not yet implemented. Requires a raster-to-vector marching squares algorithm.");
        return Task.FromResult(context.InputDataset);
    }
}

#endregion

#region --- Thermodynamics Command Implementations ---

/// <summary>
///     Base class for thermodynamics commands to share helper methods.
/// </summary>
public abstract class ThermoCommandBase
{
    /// <summary>
    ///     Converts a concentration value to mol/L based on units found in the column header.
    /// </summary>
    /// <param name="columnName">The column header, e.g., "Ca (mg/L)"</param>
    /// <param name="value">The numeric value from the cell.</param>
    /// <param name="compoundLib">The compound library for molar mass lookups.</param>
    /// <returns>The concentration in mol/L.</returns>
    protected double ConvertToMoles(string columnName, double value, CompoundLibrary compoundLib)
    {
        // Regex to find units in parentheses, brackets, or after an underscore.
        // Examples: "Ca (mg/L)", "Na [ppm]", "Cl_mg_L"
        var unitMatch = Regex.Match(columnName, @"[\(\[_](?<unit>.+)[\)\]_]?");
        var speciesName = columnName.Split(' ', '(', '[', '_')[0].Trim();

        if (!unitMatch.Success)
            // No units specified, assume mol/L as the base unit
            return value;

        var unit = unitMatch.Groups["unit"].Value.Trim().ToLower();
        var compound = compoundLib.Find(speciesName);

        if (compound == null)
        {
            // If we can't find the compound, we can't get a molar mass for conversion
            Logger.LogWarning(
                $"Could not find compound '{speciesName}' for unit conversion. Assuming a molar mass of 1 g/mol.");
            return value; // Cannot convert without molar mass
        }

        var molarMass_g_mol = compound.MolecularWeight_g_mol ?? 1.0;
        if (molarMass_g_mol == 0) molarMass_g_mol = 1.0;

        switch (unit)
        {
            // Mass-based units
            case "mg/l":
            case "ppm": // ppm is roughly mg/L for dilute aqueous solutions
                // (value mg/L) * (1 g / 1000 mg) / (molarMass g/mol) = mol/L
                return value / 1000.0 / molarMass_g_mol;
            case "ug/l":
            case "ppb": // ppb is roughly ug/L
                // (value ug/L) * (1 g / 1,000,000 ug) / (molarMass g/mol) = mol/L
                return value / 1_000_000.0 / molarMass_g_mol;
            case "g/l":
                return value / molarMass_g_mol;

            // Molar-based units
            case "mol/l":
            case "m": // common abbreviation for molarity
                return value;
            case "mmol/l":
                return value / 1000.0;
            case "umol/l":
                return value / 1_000_000.0;
            case "nmol/l":
                return value / 1_000_000_000.0;

            default:
                Logger.LogWarning(
                    $"Unrecognized unit '{unit}' in column '{columnName}'. Value will be treated as mol/L.");
                return value;
        }
    }

    /// <summary>
    ///     Creates a ThermodynamicState from a row in a DataTable, now with unit conversion.
    /// </summary>
    protected ThermodynamicState CreateStateFromDataRow(DataRow row, double temperatureK = 298.15,
        double pressureBar = 1.0)
    {
        var state = new ThermodynamicState
        {
            Temperature_K = temperatureK,
            Pressure_bar = pressureBar,
            Volume_L = 1.0 // Assume 1 kg of H2O solvent
        };

        var compoundLib = CompoundLibrary.Instance;
        var reactionGenerator = new ReactionGenerator(compoundLib);

        foreach (DataColumn col in row.Table.Columns)
            // Try to parse the cell value to a double
            if (double.TryParse(row[col].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var rawValue))
            {
                // *** NEW: Convert value from specified units to mol/L ***
                var moles = ConvertToMoles(col.ColumnName, rawValue, compoundLib);
                var speciesName = col.ColumnName.Split(' ', '(', '[', '_')[0].Trim();
                var compound = compoundLib.Find(speciesName);

                if (compound != null)
                {
                    var composition = reactionGenerator.ParseChemicalFormula(compound.ChemicalFormula);
                    foreach (var (element, stoichiometry) in composition)
                    {
                        var molesOfElement = moles * stoichiometry;
                        state.ElementalComposition[element] =
                            state.ElementalComposition.GetValueOrDefault(element, 0) + molesOfElement;
                    }

                    state.SpeciesMoles[compound.Name] = moles;
                }
            }

        // (Charge balance logic would go here as implemented previously)

        return state;
    }
}

internal static class GeoScriptThermoHelper
{
    public static (string Name, double Moles) ParseComponentToken(string rawToken, double defaultMoles = 0.001)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return (string.Empty, defaultMoles);

        var trimmed = rawToken.Trim();
        var amount = defaultMoles;
        var name = trimmed;

        var leadingMatch = Regex.Match(trimmed, @"^(?<amt>[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)\s*(?<name>.+)$");
        if (leadingMatch.Success)
        {
            amount = double.Parse(leadingMatch.Groups["amt"].Value, CultureInfo.InvariantCulture);
            name = leadingMatch.Groups["name"].Value.Trim();
        }
        else
        {
            var trailingMatch = Regex.Match(trimmed, @"(?<name>.+?)\s+(?<amt>[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)$");
            if (trailingMatch.Success)
            {
                amount = double.Parse(trailingMatch.Groups["amt"].Value, CultureInfo.InvariantCulture);
                name = trailingMatch.Groups["name"].Value.Trim();
            }
        }

        return (name, amount);
    }

    public static ChemicalCompound FindCompoundFlexible(CompoundLibrary library, string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var trimmed = input.Trim();
        var compound = library.FindFlexible(trimmed);
        if (compound != null) return compound;

        var normalized = CompoundLibrary.NormalizeFormulaInput(trimmed);
        compound = library.FindFlexible(normalized);
        return compound ?? library.Find(trimmed);
    }

    public static void AddCompoundToState(ThermodynamicState state, ReactionGenerator reactionGenerator,
        ChemicalCompound compound, double moles)
    {
        if (compound == null || moles <= 0) return;

        state.SpeciesMoles[compound.Name] = state.SpeciesMoles.GetValueOrDefault(compound.Name, 0) + moles;

        var composition = reactionGenerator.ParseChemicalFormula(compound.ChemicalFormula);
        foreach (var (element, stoichiometry) in composition)
        {
            var addition = moles * stoichiometry;
            state.ElementalComposition[element] =
                state.ElementalComposition.GetValueOrDefault(element, 0) + addition;
        }
    }
}

public class CreateDiagramCommand : IGeoScriptCommand
{
    public string Name => "CREATE_DIAGRAM";
    public string HelpText => "Generates thermodynamic phase diagrams from components or sample data.";

    public string Usage =>
        "CREATE_DIAGRAM <type> [options]\n" +
        "  BINARY FROM '<c1>' AND '<c2>' TEMP <val> K PRES <val> BAR\n" +
        "  TERNARY FROM '<c1>', '<c2>', '<c3>' TEMP <val> K PRES <val> BAR\n" +
        "  PT FOR COMP('<c1>'=<m1>,...) T_RANGE(<min>-<max>) K P_RANGE(<min>-<max>) BAR\n" +
        "  ENERGY FROM '<c1>' AND '<c2>' TEMP <val> K PRES <val> BAR\n" +
        "  COMPOSITION FROM '<col1>', '<col2>', '<col3>'";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        var cmd = (CommandNode)node;
        var generator = new PhaseDiagramGenerator();

        // Match for different diagram types
        var binaryMatch = Regex.Match(cmd.FullText,
            @"BINARY FROM '([^']+)' AND '([^']+)' TEMP ([\d\.]+) K PRES ([\d\.]+) BAR", RegexOptions.IgnoreCase);
        var ternaryMatch = Regex.Match(cmd.FullText,
            @"TERNARY FROM '([^']+)',\s*'([^']+)',\s*'([^']+)' TEMP ([\d\.]+) K PRES ([\d\.]+) BAR",
            RegexOptions.IgnoreCase);
        var ptMatch = Regex.Match(cmd.FullText,
            @"PT FOR COMP\(([^)]+)\) T_RANGE\(([\d\.]+)-([\d\.]+)\) K P_RANGE\(([\d\.]+)-([\d\.]+)\) BAR",
            RegexOptions.IgnoreCase);
        var energyMatch = Regex.Match(cmd.FullText,
            @"ENERGY FROM '([^']+)' AND '([^']+)' TEMP ([\d\.]+) K PRES ([\d\.]+) BAR", RegexOptions.IgnoreCase);
        var compositionMatch = Regex.Match(cmd.FullText, @"COMPOSITION FROM '([^']+)', '([^']+)', '([^']+)'",
            RegexOptions.IgnoreCase);

        if (binaryMatch.Success)
        {
            var comp1 = binaryMatch.Groups[1].Value;
            var comp2 = binaryMatch.Groups[2].Value;
            var temp = double.Parse(binaryMatch.Groups[3].Value, CultureInfo.InvariantCulture);
            var pres = double.Parse(binaryMatch.Groups[4].Value, CultureInfo.InvariantCulture);

            var diagramData = generator.GenerateBinaryDiagram(comp1, comp2, temp, pres);
            var resultTable = ExportBinaryToTable(diagramData);
            return Task.FromResult<Dataset>(new TableDataset("BinaryDiagram", resultTable));
        }

        if (ternaryMatch.Success)
        {
            var comp1 = ternaryMatch.Groups[1].Value;
            var comp2 = ternaryMatch.Groups[2].Value;
            var comp3 = ternaryMatch.Groups[3].Value;
            var temp = double.Parse(ternaryMatch.Groups[4].Value, CultureInfo.InvariantCulture);
            var pres = double.Parse(ternaryMatch.Groups[5].Value, CultureInfo.InvariantCulture);

            var diagramData = generator.GenerateTernaryDiagram(comp1, comp2, comp3, temp, pres);
            var resultTable = ExportTernaryToTable(diagramData);
            return Task.FromResult<Dataset>(new TableDataset("TernaryDiagram", resultTable));
        }

        if (ptMatch.Success)
        {
            var compStr = ptMatch.Groups[1].Value;
            var minT = double.Parse(ptMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            var maxT = double.Parse(ptMatch.Groups[3].Value, CultureInfo.InvariantCulture);
            var minP = double.Parse(ptMatch.Groups[4].Value, CultureInfo.InvariantCulture);
            var maxP = double.Parse(ptMatch.Groups[5].Value, CultureInfo.InvariantCulture);

            var composition = ParseComposition(compStr);
            var diagramData = generator.GeneratePTDiagram(composition, minT, maxT, minP, maxP);
            var resultTable = ExportPTToTable(diagramData);
            return Task.FromResult<Dataset>(new TableDataset("PT_Diagram", resultTable));
        }

        if (energyMatch.Success)
        {
            var comp1 = energyMatch.Groups[1].Value;
            var comp2 = energyMatch.Groups[2].Value;
            var temp = double.Parse(energyMatch.Groups[3].Value, CultureInfo.InvariantCulture);
            var pres = double.Parse(energyMatch.Groups[4].Value, CultureInfo.InvariantCulture);

            var diagramData = generator.GenerateEnergyDiagram(comp1, comp2, temp, pres);
            var resultTable = ExportEnergyToTable(diagramData);
            return Task.FromResult<Dataset>(new TableDataset("EnergyDiagram", resultTable));
        }

        if (compositionMatch.Success)
        {
            if (context.InputDataset is not TableDataset tableDs)
                throw new NotSupportedException("COMPOSITION diagram requires a Table Dataset as input.");

            var comp1Col = compositionMatch.Groups[1].Value;
            var comp2Col = compositionMatch.Groups[2].Value;
            var comp3Col = compositionMatch.Groups[3].Value;
            var resultTable = CreateCompositionPlotData(tableDs, comp1Col, comp2Col, comp3Col);
            return Task.FromResult<Dataset>(new TableDataset("SampleComposition", resultTable));
        }

        throw new ArgumentException($"Invalid CREATE_DIAGRAM syntax. Usage:\n{Usage}");
    }

    private Dictionary<string, double> ParseComposition(string compStr)
    {
        var composition = new Dictionary<string, double>();
        var parts = compStr.Split(',');
        foreach (var part in parts)
        {
            var pair = part.Split('=');
            if (pair.Length == 2)
            {
                var name = pair[0].Trim().Trim('\'');
                var moles = double.Parse(pair[1].Trim(), CultureInfo.InvariantCulture);
                composition[name] = moles;
            }
        }

        return composition;
    }

    private DataTable CreateCompositionPlotData(TableDataset tableDs, string comp1Col, string comp2Col,
        string comp3Col)
    {
        var sourceTable = tableDs.GetDataTable();
        if (!sourceTable.Columns.Contains(comp1Col) || !sourceTable.Columns.Contains(comp2Col) ||
            !sourceTable.Columns.Contains(comp3Col))
            throw new ArgumentException("Input table is missing one or more specified component columns.");

        var resultTable = new DataTable("CompositionPlotData");
        resultTable.Columns.Add("PlotX", typeof(double));
        resultTable.Columns.Add("PlotY", typeof(double));
        foreach (DataColumn col in sourceTable.Columns)
            if (!resultTable.Columns.Contains(col.ColumnName))
                resultTable.Columns.Add(col.ColumnName, col.DataType);

        foreach (DataRow row in sourceTable.Rows)
        {
            var val1 = Convert.ToDouble(row[comp1Col]);
            var val2 = Convert.ToDouble(row[comp2Col]);
            var val3 = Convert.ToDouble(row[comp3Col]);
            var total = val1 + val2 + val3;
            if (total == 0) continue;

            var x2 = val2 / total;
            var x3 = val3 / total;
            var plotX = x2 + 0.5 * x3;
            var plotY = Math.Sqrt(3) / 2.0 * x3;
            var newRow = resultTable.NewRow();
            newRow["PlotX"] = plotX;
            newRow["PlotY"] = plotY;
            foreach (DataColumn col in sourceTable.Columns) newRow[col.ColumnName] = row[col];
            resultTable.Rows.Add(newRow);
        }

        return resultTable;
    }

    #region Private Table Exporters

    private DataTable ExportBinaryToTable(BinaryPhaseDiagramData data)
    {
        var table = new DataTable("BinaryPhaseDiagram");
        table.Columns.Add("X_" + data.Component1, typeof(double));
        table.Columns.Add("X_" + data.Component2, typeof(double));
        table.Columns.Add("Phases", typeof(string));
        table.Columns.Add("pH", typeof(double));
        table.Columns.Add("IonicStrength", typeof(double));
        table.Columns.Add("Precipitates", typeof(string));

        foreach (var point in data.Points)
            table.Rows.Add(
                point.X_Component1,
                point.X_Component2,
                string.Join(", ", point.PhasesPresent),
                point.pH,
                point.IonicStrength,
                string.Join(", ", point.PrecipitatingMinerals)
            );
        return table;
    }

    private DataTable ExportTernaryToTable(TernaryPhaseDiagramData data)
    {
        var table = new DataTable("TernaryPhaseDiagram");
        table.Columns.Add("X_" + data.Component1, typeof(double));
        table.Columns.Add("X_" + data.Component2, typeof(double));
        table.Columns.Add("X_" + data.Component3, typeof(double));
        table.Columns.Add("Phases", typeof(string));
        table.Columns.Add("Precipitates", typeof(string));
        table.Columns.Add("pH", typeof(double));
        table.Columns.Add("IonicStrength", typeof(double));

        foreach (var point in data.Points)
            table.Rows.Add(
                point.X_Component1,
                point.X_Component2,
                point.X_Component3,
                string.Join(", ", point.PhasesPresent),
                string.Join(", ", point.PrecipitatingMinerals),
                point.pH,
                point.IonicStrength
            );
        return table;
    }

    private DataTable ExportPTToTable(PTPhaseDiagramData data)
    {
        var table = new DataTable("PTPhaseDiagram");
        table.Columns.Add("Temperature_K", typeof(double));
        table.Columns.Add("Pressure_bar", typeof(double));
        table.Columns.Add("Phases", typeof(string));
        table.Columns.Add("DominantPhase", typeof(string));
        table.Columns.Add("MolarVolume", typeof(double));

        foreach (var point in data.Points)
            table.Rows.Add(
                point.Temperature_K,
                point.Pressure_bar,
                string.Join(", ", point.PhasesPresent),
                point.DominantPhase,
                point.MolarVolume
            );
        return table;
    }

    private DataTable ExportEnergyToTable(EnergyDiagramData data)
    {
        var table = new DataTable("EnergyDiagram");
        table.Columns.Add("X_" + data.Component1, typeof(double));
        table.Columns.Add("GibbsEnergy", typeof(double));
        table.Columns.Add("Enthalpy", typeof(double));
        table.Columns.Add("Entropy", typeof(double));
        table.Columns.Add("ChemicalPotential_" + data.Component1, typeof(double));
        table.Columns.Add("ChemicalPotential_" + data.Component2, typeof(double));

        foreach (var point in data.Points)
            table.Rows.Add(
                point.X_Component1,
                point.GibbsEnergy,
                point.Enthalpy,
                point.Entropy,
                point.ChemicalPotential1,
                point.ChemicalPotential2
            );
        return table;
    }

    #endregion
}

public class EquilibrateCommand : IGeoScriptCommand
{
    public string Name => "EQUILIBRATE";
    public string HelpText => "Calculate chemical equilibrium for a system";
    public string Usage => "EQUILIBRATE <Compounds> [TEMP <val> C|K] [PRES <val> BAR|ATM] [pH <val>]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        var cmd = (CommandNode)node;
        var compoundLib = CompoundLibrary.Instance;
        var reactionGenerator = new ReactionGenerator(compoundLib);
        var solver = new ThermodynamicSolver();

        var pattern =
            @"EQUILIBRATE\s+(?<compounds>.+?)(?:\s+TEMP\s+(?<tempval>[\d\.]+)\s*(?<tempunit>C|K))?(?:\s+PRES\s+(?<presval>[\d\.]+)\s*(?<presunit>BAR|ATM))?(?:\s+pH\s+(?<ph>[\d\.]+))?$";
        var match = Regex.Match(cmd.FullText, pattern, RegexOptions.IgnoreCase);

        if (!match.Success)
            throw new ArgumentException($"Invalid syntax. Usage: {Usage}");

        var compoundsStr = match.Groups["compounds"].Value.Trim();
        var compoundInputs = compoundsStr.Split('+', StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Trim())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToList();

        if (compoundInputs.Count == 0)
            throw new ArgumentException("You must provide at least one compound to equilibrate.");

        var temperatureK = 298.15;
        if (match.Groups["tempval"].Success)
        {
            var tempVal = double.Parse(match.Groups["tempval"].Value, CultureInfo.InvariantCulture);
            temperatureK = match.Groups["tempunit"].Value.Equals("K", StringComparison.OrdinalIgnoreCase)
                ? tempVal
                : tempVal + 273.15;
        }

        var pressureBar = 1.0;
        if (match.Groups["presval"].Success)
        {
            var presVal = double.Parse(match.Groups["presval"].Value, CultureInfo.InvariantCulture);
            pressureBar = match.Groups["presunit"].Value.Equals("ATM", StringComparison.OrdinalIgnoreCase)
                ? presVal * 1.01325
                : presVal;
        }

        double? targetPh = null;
        if (match.Groups["ph"].Success)
            targetPh = double.Parse(match.Groups["ph"].Value, CultureInfo.InvariantCulture);

        var initialState = new ThermodynamicState
        {
            Temperature_K = temperatureK,
            Pressure_bar = pressureBar,
            Volume_L = 1.0
        };

        if (targetPh.HasValue)
        {
            initialState.pH = targetPh.Value;
            var hActivity = Math.Pow(10, -targetPh.Value);
            var ohActivity = Math.Max(1e-30, 1e-14 / hActivity);

            initialState.Activities["H⁺"] = hActivity;
            initialState.Activities["H+"] = hActivity;
            initialState.Activities["OH⁻"] = ohActivity;
            initialState.Activities["OH-"] = ohActivity;
        }

        // Always include liquid water as solvent
        var water = GeoScriptThermoHelper.FindCompoundFlexible(compoundLib, "H2O") ??
                    GeoScriptThermoHelper.FindCompoundFlexible(compoundLib, "Water");
        if (water != null)
        {
            var waterMoles = 55.508; // ~1 kg of water at 25 °C
            GeoScriptThermoHelper.AddCompoundToState(initialState, reactionGenerator, water, waterMoles);
        }
        else
        {
            Logger.LogWarning("[EQUILIBRATE] Unable to locate water in the compound library.");
        }

        var parsedCompounds = new List<(ChemicalCompound compound, double moles, string token)>();
        foreach (var input in compoundInputs)
        {
            var (cleanName, moles) = GeoScriptThermoHelper.ParseComponentToken(input);
            var compound = GeoScriptThermoHelper.FindCompoundFlexible(compoundLib, cleanName);
            if (compound == null)
            {
                Logger.LogWarning($"[EQUILIBRATE] Compound '{cleanName}' not found in library");
                continue;
            }

            parsedCompounds.Add((compound, moles, input));
            GeoScriptThermoHelper.AddCompoundToState(initialState, reactionGenerator, compound, moles);
        }

        if (parsedCompounds.Count == 0)
            throw new ArgumentException("No valid compounds were provided for EQUILIBRATE.");

        Logger.Log($"[EQUILIBRATE] Building system at {temperatureK:F2} K and {pressureBar:F2} bar");
        foreach (var (compound, moles, token) in parsedCompounds)
            Logger.Log($"  - {token} → {compound.Name} ({compound.ChemicalFormula}) : {moles:E3} mol");

        var finalState = solver.SolveEquilibrium(initialState);

        Logger.Log("\n=== EQUILIBRATION RESULTS ===");
        Logger.Log($"Temperature: {finalState.Temperature_K:F2} K");
        Logger.Log($"Pressure: {finalState.Pressure_bar:F2} bar");
        Logger.Log($"pH: {finalState.pH:F2}");
        Logger.Log($"pe: {finalState.pe:F2}");
        Logger.Log($"Ionic Strength: {finalState.IonicStrength_molkg:F4} mol/kg\n");

        var resultTable = new DataTable("Equilibrium_State");
        resultTable.Columns.Add("Species", typeof(string));
        resultTable.Columns.Add("Formula", typeof(string));
        resultTable.Columns.Add("Phase", typeof(string));
        resultTable.Columns.Add("Moles", typeof(double));
        resultTable.Columns.Add("Mass_g", typeof(double));
        resultTable.Columns.Add("Activity", typeof(double));
        resultTable.Columns.Add("LogActivity", typeof(double));

        var sortedSpecies = finalState.SpeciesMoles
            .Where(kvp => kvp.Value > 1e-15)
            .OrderBy(kvp => GetPhaseOrder(GeoScriptThermoHelper.FindCompoundFlexible(compoundLib, kvp.Key)?.Phase))
            .ThenByDescending(kvp => kvp.Value)
            .ToList();

        foreach (var (speciesName, moles) in sortedSpecies)
        {
            var compound = GeoScriptThermoHelper.FindCompoundFlexible(compoundLib, speciesName);
            var lookupName = compound?.Name ?? speciesName;
            var phase = compound?.Phase ?? CompoundPhase.Aqueous;
            var formula = compound?.ChemicalFormula ?? speciesName;
            var molarMass = compound?.MolecularWeight_g_mol;
            var mass = molarMass.HasValue ? moles * molarMass.Value : double.NaN;

            double defaultActivity;
            if (phase == CompoundPhase.Aqueous)
                defaultActivity = moles / Math.Max(finalState.Volume_L, 1e-12);
            else
                defaultActivity = 1.0;

            var activity = finalState.Activities.GetValueOrDefault(lookupName, defaultActivity);
            var logActivity = activity > 0 ? Math.Log10(activity) : double.NegativeInfinity;

            resultTable.Rows.Add(
                lookupName,
                formula,
                phase.ToString(),
                moles,
                mass,
                activity,
                logActivity
            );

            Logger.Log(
                $"{phase,-10} {lookupName,-20} {formula,-15} {moles,10:E3} mol  a = {activity,10:E3} (log10 a = {logActivity,6:F2})");
        }

        return Task.FromResult<Dataset>(new TableDataset(resultTable.TableName, resultTable));
    }

    private static int GetPhaseOrder(CompoundPhase? phase)
    {
        return phase switch
        {
            CompoundPhase.Solid => 0,
            CompoundPhase.Aqueous => 1,
            CompoundPhase.Gas => 2,
            CompoundPhase.Liquid => 3,
            _ => 4
        };
    }
}

public class SaturationCommand : ThermoCommandBase, IGeoScriptCommand
{
    public string Name => "SATURATION";
    public string HelpText => "Calculates mineral saturation indices (Log Q/K).";
    public string Usage => "SATURATION MINERALS 'Mineral1', 'Mineral2', ...";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not TableDataset tableDs)
            throw new NotSupportedException("SATURATION only works on Table Datasets.");

        var cmd = (CommandNode)node;
        var mineralMatches = Regex.Matches(cmd.FullText, @"'([^']+)'");
        var minerals = mineralMatches.Select(m => m.Groups[1].Value).ToList();
        if (minerals.Count == 0) throw new ArgumentException("You must specify at least one mineral.");

        var solver = new ThermodynamicSolver();
        var sourceTable = tableDs.GetDataTable();
        var resultTable = sourceTable.Copy();

        // Add columns for the requested mineral SIs
        foreach (var mineral in minerals) resultTable.Columns.Add($"SI_{mineral}", typeof(double));

        foreach (DataRow row in resultTable.Rows)
        {
            var initialState = CreateStateFromDataRow(row);
            var equilibratedState = solver.SolveSpeciation(initialState);
            var saturationIndices = solver.CalculateSaturationIndices(equilibratedState);

            foreach (var (reactionName, si) in saturationIndices)
            {
                // The reaction name is usually "MineralName dissolution"
                var mineralName = reactionName.Replace(" dissolution", "");
                if (resultTable.Columns.Contains($"SI_{mineralName}")) row[$"SI_{mineralName}"] = si;
            }
        }

        return Task.FromResult<Dataset>(new TableDataset($"{tableDs.Name}_Saturation", resultTable));
    }
}

public class SaturationIndexCommand : IGeoScriptCommand
{
    public string Name => "SATURATION_INDEX";
    public string HelpText => "Calculate saturation indices for minerals";
    public string Usage => "SATURATION_INDEX <Solution> WITH <Minerals>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        var cmd = (CommandNode)node;
        var compoundLib = CompoundLibrary.Instance;
        var reactionGenerator = new ReactionGenerator(compoundLib);
        var solver = new ThermodynamicSolver();

        var pattern = @"SATURATION_INDEX\s+(?<solution>.+?)\s+WITH\s+(?<minerals>.+)$";
        var match = Regex.Match(cmd.FullText, pattern, RegexOptions.IgnoreCase);

        if (!match.Success) throw new ArgumentException($"Invalid syntax. Usage: {Usage}");

        var solutionInputs = match.Groups["solution"].Value
            .Split('+', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
        if (solutionInputs.Count == 0)
            throw new ArgumentException("You must provide at least one solute in the solution portion.");

        var mineralInputs = match.Groups["minerals"].Value
            .Split('+', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
        if (mineralInputs.Count == 0)
            throw new ArgumentException("You must provide at least one mineral after WITH.");

        var initialState = new ThermodynamicState
        {
            Temperature_K = 298.15,
            Pressure_bar = 1.0,
            Volume_L = 1.0
        };

        var water = GeoScriptThermoHelper.FindCompoundFlexible(compoundLib, "H2O") ??
                    GeoScriptThermoHelper.FindCompoundFlexible(compoundLib, "Water");
        if (water != null)
            GeoScriptThermoHelper.AddCompoundToState(initialState, reactionGenerator, water, 55.508);

        foreach (var input in solutionInputs)
        {
            var (cleanName, moles) = GeoScriptThermoHelper.ParseComponentToken(input);
            var compound = GeoScriptThermoHelper.FindCompoundFlexible(compoundLib, cleanName);
            if (compound == null)
            {
                Logger.LogWarning($"[SATURATION_INDEX] Solution species '{cleanName}' not found");
                continue;
            }

            GeoScriptThermoHelper.AddCompoundToState(initialState, reactionGenerator, compound, moles);
        }

        var equilibratedState = solver.SolveEquilibrium(initialState);
        var saturationIndices = solver.CalculateSaturationIndices(equilibratedState);

        Logger.Log("\n=== SATURATION INDEX RESULTS ===");
        Logger.Log($"Temperature: {equilibratedState.Temperature_K:F2} K");
        Logger.Log($"Pressure: {equilibratedState.Pressure_bar:F2} bar");
        Logger.Log($"pH: {equilibratedState.pH:F2}\n");

        var resultTable = new DataTable("SaturationIndices");
        resultTable.Columns.Add("Mineral", typeof(string));
        resultTable.Columns.Add("Formula", typeof(string));
        resultTable.Columns.Add("SaturationIndex", typeof(double));
        resultTable.Columns.Add("Status", typeof(string));

        foreach (var input in mineralInputs)
        {
            var mineral = GeoScriptThermoHelper.FindCompoundFlexible(compoundLib, input);
            if (mineral == null)
            {
                Logger.LogWarning($"[SATURATION_INDEX] Mineral '{input}' not found");
                continue;
            }

            if (mineral.Phase != CompoundPhase.Solid)
            {
                Logger.LogWarning($"[SATURATION_INDEX] {mineral.Name} is not a solid mineral. Skipping.");
                continue;
            }

            var hasValue = saturationIndices.TryGetValue(mineral.Name, out var siValue);
            var status = DescribeSaturationStatus(hasValue ? siValue : double.NaN);
            resultTable.Rows.Add(mineral.Name, mineral.ChemicalFormula, hasValue ? siValue : double.NaN, status);

            Logger.Log($"{mineral.Name,-20} SI = {(hasValue ? siValue : double.NaN):F3}  → {status}");
        }

        return Task.FromResult<Dataset>(new TableDataset(resultTable.TableName, resultTable));
    }

    private static string DescribeSaturationStatus(double value)
    {
        if (double.IsNaN(value)) return "Not available";
        if (value > 0.1) return "Supersaturated";
        if (value < -0.1) return "Undersaturated";
        return "At equilibrium";
    }
}

public class BalanceReactionCommand : IGeoScriptCommand
{
    public string Name => "BALANCE_REACTION";
    public string HelpText => "Generates a balanced dissolution reaction for a mineral.";
    public string Usage => "BALANCE_REACTION 'MineralName'";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        var cmd = (CommandNode)node;
        var match = Regex.Match(cmd.FullText, @"'([^']+)'", RegexOptions.IgnoreCase);
        if (!match.Success) throw new ArgumentException("Invalid syntax. Specify a mineral name in quotes.");

        var mineralName = match.Groups[1].Value;
        var compoundLib = CompoundLibrary.Instance;
        var mineral = compoundLib.Find(mineralName);
        if (mineral == null) throw new ArgumentException($"Mineral '{mineralName}' not found in the database.");

        var reactionGenerator = new ReactionGenerator(compoundLib);
        var reaction =
            reactionGenerator.GetType()
                .GetMethod("GenerateSingleDissolutionReaction", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(reactionGenerator, new object[] { mineral }) as ChemicalReaction;

        if (reaction == null)
            throw new InvalidOperationException($"Could not generate a reaction for '{mineralName}'.");

        var reactants = reaction.Stoichiometry.Where(kvp => kvp.Value < 0)
            .Select(kvp => $"{Math.Abs(kvp.Value)} {kvp.Key}");
        var products = reaction.Stoichiometry.Where(kvp => kvp.Value > 0).Select(kvp => $"{kvp.Value} {kvp.Key}");
        var reactionString = $"{string.Join(" + ", reactants)} <=> {string.Join(" + ", products)}";

        var resultTable = new DataTable("BalancedReaction");
        resultTable.Columns.Add("Mineral", typeof(string));
        resultTable.Columns.Add("Reaction", typeof(string));
        resultTable.Columns.Add("LogK_25C", typeof(double));
        resultTable.Rows.Add(mineralName, reactionString, reaction.LogK_25C);

        return Task.FromResult<Dataset>(new TableDataset("BalancedReaction", resultTable));
    }
}

public class EvaporateCommand : ThermoCommandBase, IGeoScriptCommand
{
    public string Name => "EVAPORATE";
    public string HelpText => "Simulates evaporation and mineral precipitation sequence.";
    public string Usage => "EVAPORATE UPTO <factor>x STEPS <count> MINERALS 'Mineral1', 'Mineral2', ...";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not TableDataset tableDs)
            throw new NotSupportedException("EVAPORATE only works on Table Datasets.");

        var cmd = (CommandNode)node;
        var factorMatch = Regex.Match(cmd.FullText, @"UPTO\s+([\d\.]+)", RegexOptions.IgnoreCase);
        var stepsMatch = Regex.Match(cmd.FullText, @"STEPS\s+(\d+)", RegexOptions.IgnoreCase);
        var mineralMatches = Regex.Matches(cmd.FullText, @"'([^']+)'");

        if (!factorMatch.Success || !stepsMatch.Success || mineralMatches.Count == 0)
            throw new ArgumentException("Invalid EVAPORATE syntax.");

        var maxFactor = double.Parse(factorMatch.Groups[1].Value);
        var steps = int.Parse(stepsMatch.Groups[1].Value);
        var minerals = mineralMatches.Select(m => m.Groups[1].Value).ToList();

        var solver = new ThermodynamicSolver();
        var sourceTable = tableDs.GetDataTable();
        if (sourceTable.Rows.Count == 0) throw new DataException("Input table for EVAPORATE cannot be empty.");

        // Use the first row as the starting water composition
        var initialWaterRow = sourceTable.Rows[0];
        var state = CreateStateFromDataRow(initialWaterRow);
        var initialVolume = state.Volume_L;

        var resultTable = new DataTable($"{tableDs.Name}_Evaporation_Sequence");
        resultTable.Columns.Add("EvaporationFactor", typeof(double));
        resultTable.Columns.Add("RemainingVolume_L", typeof(double));
        resultTable.Columns.Add("pH", typeof(double));
        resultTable.Columns.Add("IonicStrength_molkg", typeof(double));
        foreach (var mineral in minerals) resultTable.Columns.Add($"Moles_{mineral}", typeof(double));

        var totalPrecipitated = minerals.ToDictionary(m => m, m => 0.0);

        for (var i = 0; i <= steps; i++)
        {
            var currentFactor = 1.0 + (maxFactor - 1.0) * i / steps;
            state.Volume_L = initialVolume / currentFactor;

            // Re-speciate with the new concentrated solution
            var equilibratedState = solver.SolveSpeciation(state);

            // Precipitation check (simplified approach)
            // A full reaction path model would remove elements from solution.
            // Here, we just check for supersaturation.
            var SIs = solver.CalculateSaturationIndices(equilibratedState);
            foreach (var (reactionName, si) in SIs)
            {
                var mineralName = reactionName.Replace(" dissolution", "");
                if (totalPrecipitated.ContainsKey(mineralName) && si > 0.01) // 0.01 threshold for precipitation
                    // This is a simplification. A real model would calculate moles to precipitate
                    // to bring SI back to 0. We'll just mark it as precipitating.
                    // For a simple output, we can use the SI value as a proxy for amount.
                    totalPrecipitated[mineralName] += si;
            }

            var newRow = resultTable.NewRow();
            newRow["EvaporationFactor"] = currentFactor;
            newRow["RemainingVolume_L"] = equilibratedState.Volume_L;
            newRow["pH"] = equilibratedState.pH;
            newRow["IonicStrength_molkg"] = equilibratedState.IonicStrength_molkg;
            foreach (var mineral in minerals) newRow[$"Moles_{mineral}"] = totalPrecipitated[mineral];
            resultTable.Rows.Add(newRow);
        }

        return Task.FromResult<Dataset>(new TableDataset(resultTable.TableName, resultTable));
    }
}

public class ReactCommand : IGeoScriptCommand
{
    public string Name => "REACT";
    public string HelpText => "Calculates the equilibrium products of a set of reactants at given T and P.";
    public string Usage => "REACT <Reactants> [TEMP <val> C|K] [PRES <val> BAR|ATM]";

    /// <summary>
    ///     Normalizes a chemical formula by converting plain numbers to subscripts.
    ///     E.g., "H2O" -> "H₂O", "CaCO3" -> "CaCO₃", "H2S" -> "H₂S"
    /// </summary>
    private string NormalizeChemicalFormula(string formula)
    {
        // Convert regular digits to subscript Unicode characters
        var normalized = formula
            .Replace('0', '₀')
            .Replace('1', '₁')
            .Replace('2', '₂')
            .Replace('3', '₃')
            .Replace('4', '₄')
            .Replace('5', '₅')
            .Replace('6', '₆')
            .Replace('7', '₇')
            .Replace('8', '₈')
            .Replace('9', '₉');

        return normalized;
    }

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        var cmd = (CommandNode)node;

        // --- 1. Parse the command string ---
        var pattern =
            @"REACT\s+(?<reaction>.+?)(?:\s+TEMP\s+(?<tempval>[\d\.]+)\s*(?<tempunit>C|K))?(?:\s+PRES\s+(?<presval>[\d\.]+)\s*(?<presunit>BAR|ATM))?$";
        var match = Regex.Match(cmd.FullText, pattern, RegexOptions.IgnoreCase);

        if (!match.Success) throw new ArgumentException($"Invalid REACT syntax. Usage: {Usage}");

        var reactionStr = match.Groups["reaction"].Value.Trim();

        // --- 2. Set default T and P, then override with user values ---
        var temperatureK = 20.0 + 273.15; // Default 20 C
        var pressureBar = 1.0; // Default 1 atm ~ 1 bar

        if (match.Groups["tempval"].Success)
        {
            var tempVal = double.Parse(match.Groups["tempval"].Value, CultureInfo.InvariantCulture);
            temperatureK = match.Groups["tempunit"].Value.ToUpper() == "K" ? tempVal : tempVal + 273.15;
        }

        if (match.Groups["presval"].Success)
        {
            var presVal = double.Parse(match.Groups["presval"].Value, CultureInfo.InvariantCulture);
            pressureBar = match.Groups["presunit"].Value.ToUpper() == "ATM" ? presVal * 1.01325 : presVal;
        }

        // --- 3. Build the initial thermodynamic state from reactants ---
        var initialState = new ThermodynamicState
        {
            Temperature_K = temperatureK,
            Pressure_bar = pressureBar,
            Volume_L = 1.0 // Start with 1L volume
        };

        var compoundLib = CompoundLibrary.Instance;
        var reactionGenerator = new ReactionGenerator(compoundLib);

        var reactants = reactionStr.Split('+').Select(r => r.Trim()).ToList();

        foreach (var reactantName in reactants)
        {
            // Handle custom hydration character '!' -> '·'
            var cleanedName = reactantName.Replace('!', '·');

            // Normalize chemical formula: convert plain numbers to subscripts
            // This allows users to type "H2O" and it will match "H₂O"
            var normalizedName = NormalizeChemicalFormula(cleanedName);

            // Try to find the compound, first with normalized name, then with original
            var compound = compoundLib.Find(normalizedName) ?? compoundLib.Find(cleanedName);

            if (compound == null)
            {
                Logger.LogError($"Reactant '{reactantName}' (normalized: '{normalizedName}') not found in the chemical database.");
                throw new ArgumentException($"Unknown reactant: {reactantName}");
            }

            // Assume 1 mole of each reactant, except for water which is the solvent.
            // For water, calculate moles in 1 L: 1000 g / molar_mass
            double moles;
            var isWater = compound.Name.ToUpper() == "WATER" || compound.ChemicalFormula == "H₂O";
            if (isWater)
            {
                var waterMolarMass = compound.MolecularWeight_g_mol ?? 18.015; // g/mol
                moles = 1000.0 / waterMolarMass; // moles in 1 L of water
            }
            else
            {
                moles = 1.0; // 1 mole for non-water reactants
            }

            Logger.Log($"[REACT] Adding reactant: {compound.Name} (formula: {compound.ChemicalFormula}) - {moles:F3} moles");

            initialState.SpeciesMoles[compound.Name] =
                initialState.SpeciesMoles.GetValueOrDefault(compound.Name, 0) + moles;

            // Add to total elemental composition
            var composition = reactionGenerator.ParseChemicalFormula(compound.ChemicalFormula);
            foreach (var (element, stoichiometry) in composition)
                initialState.ElementalComposition[element] =
                    initialState.ElementalComposition.GetValueOrDefault(element, 0) + moles * stoichiometry;
        }

        // --- 4. Solve for equilibrium ---
        var solver = new ThermodynamicSolver();
        var finalState = solver.SolveEquilibrium(initialState);

        // --- 5. Format the output with improved phase separation ---
        var resultTable = new DataTable("ReactionProducts");
        resultTable.Columns.Add("Phase", typeof(string));
        resultTable.Columns.Add("Species", typeof(string));
        resultTable.Columns.Add("Formula", typeof(string));
        resultTable.Columns.Add("Moles", typeof(double));
        resultTable.Columns.Add("Mass_g", typeof(double));
        resultTable.Columns.Add("MoleFraction", typeof(double));

        // Calculate total moles for each phase
        var phaseGroups = finalState.SpeciesMoles
            .Where(kvp => kvp.Value > 1e-12) // Filter out negligible amounts
            .GroupBy(kvp => compoundLib.Find(kvp.Key)?.Phase ?? CompoundPhase.Aqueous)
            .ToDictionary(g => g.Key, g => g.Sum(kvp => kvp.Value));

        var sortedProducts = finalState.SpeciesMoles
            .Where(kvp => kvp.Value > 1e-12)
            .OrderBy(kvp =>
            {
                var phase = compoundLib.Find(kvp.Key)?.Phase;
                return phase switch
                {
                    CompoundPhase.Solid => 0,
                    CompoundPhase.Aqueous => 1,
                    CompoundPhase.Gas => 2,
                    CompoundPhase.Liquid => 3,
                    _ => 4
                };
            })
            .ThenByDescending(kvp => kvp.Value);

        // Build output string for terminal display using ASCII borders
        var terminalOutput = new System.Text.StringBuilder();
        terminalOutput.AppendLine("\n+----------------------------------------------------------------------------+");
        terminalOutput.AppendLine("|                           REACTION EQUILIBRIUM RESULTS                    |");
        terminalOutput.AppendLine("+----------------------------------------------------------------------------+");
        terminalOutput.AppendLine($"| Temperature: {temperatureK,6:F1} K          pH: {finalState.pH,5:F2}          pe: {finalState.pe,6:F2}        |");
        terminalOutput.AppendLine($"| Pressure:    {pressureBar,6:F2} bar        Ionic Strength: {finalState.IonicStrength_molkg,8:E2} mol/kg   |");
        terminalOutput.AppendLine("+----------------------------------------------------------------------------+");
        terminalOutput.AppendLine("| Phase      Species              Formula          Moles          Mass (g)   |");
        terminalOutput.AppendLine("+----------------------------------------------------------------------------+");

        foreach (var (speciesName, moles) in sortedProducts)
        {
            var compound = compoundLib.Find(speciesName);
            if (compound == null) continue;

            var phaseName = compound.Phase.ToString();
            if (compound.Phase == CompoundPhase.Solid)
                phaseName += " (mineral)";

            var mass = moles * (compound.MolecularWeight_g_mol ?? 0);
            var totalPhaseMoles = phaseGroups.GetValueOrDefault(compound.Phase, 1.0);
            var moleFraction = moles / totalPhaseMoles;

            resultTable.Rows.Add(
                phaseName,
                speciesName,
                compound.ChemicalFormula,
                moles,
                mass,
                moleFraction
            );

            // Add to terminal output
            var phaseLabel = compound.Phase == CompoundPhase.Solid ? "[SOLID]" :
                            compound.Phase == CompoundPhase.Aqueous ? "[AQUE] " :
                            compound.Phase == CompoundPhase.Gas ? "[GAS]  " :
                            compound.Phase == CompoundPhase.Liquid ? "[LIQU] " : "[    ] ";

            terminalOutput.AppendLine(
                $"| {phaseLabel} {speciesName,-18} {compound.ChemicalFormula,-14} {moles,12:E2}   {mass,12:E2}   |");
        }

        terminalOutput.AppendLine("+----------------------------------------------------------------------------+");
        terminalOutput.AppendLine("| PHASE TOTALS:                                                              |");
        foreach (var (phase, totalMoles) in phaseGroups.OrderBy(kvp => kvp.Key))
        {
            terminalOutput.AppendLine($"|   {phase,-20} {totalMoles,12:E2} moles                                  |");
        }
        terminalOutput.AppendLine("+----------------------------------------------------------------------------+");

        // Print to terminal
        Console.WriteLine(terminalOutput.ToString());

        return Task.FromResult<Dataset>(new TableDataset("Reaction_Products", resultTable));
    }
}
internal static class SpeciateCommandExecutor
{
    public static Task<Dataset> ExecuteAsync(GeoScriptContext context, CommandNode cmd, bool enableDiagnostics)
    {
        var label = enableDiagnostics ? "DIAGNOSE_SPECIATE" : "SPECIATE";
        void Trace(string message)
        {
            if (enableDiagnostics)
                Logger.Log($"[{label}] {message}");
        }

        void Log(string message) => Logger.Log($"[{label}] {message}");
        void Warn(string message) => Logger.LogWarning($"[{label}] {message}");

        Trace($"Command text: {cmd.FullText}");

        // Parse the command string
        var pattern = @"SPECIATE\s+(?<compounds>.+?)(?:\s+TEMP\s+(?<tempval>[\d\.]+)\s*(?<tempunit>C|K))?(?:\s+PRES\s+(?<presval>[\d\.]+)\s*(?<presunit>BAR|ATM))?$";
        var match = Regex.Match(cmd.FullText, pattern, RegexOptions.IgnoreCase);

        if (!match.Success) throw new ArgumentException("Invalid SPECIATE syntax. Usage: SPECIATE <Compounds> [TEMP <val> C|K] [PRES <val> BAR|ATM]");

        var compoundsStr = match.Groups["compounds"].Value.Trim();
        Trace($"Normalized compound string: '{compoundsStr}'");

        // Set temperature
        var temperatureK = 298.15; // Default 25 C
        if (match.Groups["tempval"].Success)
        {
            var tempVal = double.Parse(match.Groups["tempval"].Value, CultureInfo.InvariantCulture);
            temperatureK = match.Groups["tempunit"].Value.ToUpper() == "K" ? tempVal : tempVal + 273.15;
            Trace($"User-specified temperature detected -> {temperatureK:F2} K");
        }
        else
        {
            Trace("Using default temperature (298.15 K)");
        }

        // Set pressure
        var pressureBar = 1.0; // Default 1 bar
        if (match.Groups["presval"].Success)
        {
            var presVal = double.Parse(match.Groups["presval"].Value, CultureInfo.InvariantCulture);
            pressureBar = match.Groups["presunit"].Value.ToUpper() == "ATM" ? presVal * 1.01325 : presVal;
            Trace($"User-specified pressure detected -> {pressureBar:F2} bar");
        }
        else
        {
            Trace("Using default pressure (1.00 bar)");
        }

        var compoundLib = CompoundLibrary.Instance;
        var reactionGenerator = new ReactionGenerator(compoundLib);
        var solver = new ThermodynamicSolver();

        // Build initial state
        var initialState = new ThermodynamicState
        {
            Temperature_K = temperatureK,
            Pressure_bar = pressureBar,
            Volume_L = 1.0
        };
        Trace($"Initialized thermodynamic state: T={temperatureK:F2} K, P={pressureBar:F2} bar, V=1 L");

        // Parse and normalize compound inputs
        var compoundInputs = compoundsStr.Split('+').Select(c => c.Trim()).ToList();
        var compounds = new List<string>();

        foreach (var input in compoundInputs)
        {
            var normalized = CompoundLibrary.NormalizeFormulaInput(input);
            compounds.Add(normalized);
        }

        Log($"Processing compounds: {string.Join(", ", compounds)} at {temperatureK:F2} K, {pressureBar:F2} bar");

        // Always add water to the system for aqueous solutions - use flexible find
        var water = compoundLib.FindFlexible("H2O") ??
                    compoundLib.FindFlexible("Water") ??
                    compoundLib.FindFlexible("H₂O");

        if (water != null)
        {
            var waterMoles = 55.508; // 1L of water at 25°C
            initialState.SpeciesMoles[water.Name] = waterMoles;
            var waterComp = reactionGenerator.ParseChemicalFormula(water.ChemicalFormula);
            foreach (var (element, stoichiometry) in waterComp)
            {
                initialState.ElementalComposition[element] = waterMoles * stoichiometry;
            }

            Trace($"Added bulk water ({waterMoles} mol) as solvent");
        }
        else
        {
            Warn("Unable to resolve water reference. Speciation may be inaccurate.");
        }

        // Generate dissociation reactions for input compounds
        var dissociationReactions = reactionGenerator.GenerateSolubleCompoundDissociationReactions(
            compounds, temperatureK, pressureBar);
        Trace($"Generated {dissociationReactions.Count} dissociation reactions from library data");

        // Process each compound based on its dissociation behavior
        foreach (var compoundInput in compounds)
        {
            Trace($"Evaluating compound token '{compoundInput}'");

            // Use flexible find
            var compound = compoundLib.FindFlexible(compoundInput);
            if (compound == null)
            {
                Warn($"Compound '{compoundInput}' not found in library");
                continue;
            }
            
            // SURGICAL FIX: Skip water if it's in the compound list - we already added it as solvent
            if ((compound.Phase == CompoundPhase.Liquid || compound.Phase == CompoundPhase.Aqueous) && 
                (compound.Name == "Water" || compound.ChemicalFormula.Contains("H2O") || compound.ChemicalFormula.Contains("H₂O")))
            {
                Trace($"Skipping {compound.Name} - already added as solvent");
                continue;
            }

            double moles = 0.001; // Default 1 mmol

            // Check if user specified amount (e.g., "0.1 NaCl" or "NaCl 0.1")
            var amountMatch = Regex.Match(compoundInput, @"([\d\.]+)\s*(\w+)|(\w+)\s+([\d\.]+)");
            if (amountMatch.Success)
            {
                if (!string.IsNullOrEmpty(amountMatch.Groups[1].Value))
                {
                    moles = double.Parse(amountMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                }
                else if (!string.IsNullOrEmpty(amountMatch.Groups[4].Value))
                {
                    moles = double.Parse(amountMatch.Groups[4].Value, CultureInfo.InvariantCulture);
                }

                Trace($"User specified {moles:E3} mol for {compound.Name}");
            }
            else
            {
                Trace($"Using default quantity ({moles:E3} mol) for {compound.Name}");
            }

            // Check if there's a dissociation reaction for this compound
            var dissociationReaction = dissociationReactions.FirstOrDefault(r =>
                r.Stoichiometry.ContainsKey(compound.Name) && r.Stoichiometry[compound.Name] < 0);

            if (dissociationReaction != null)
            {
                // For compounds with dissociation reactions, add the products directly
                Log($"{compound.Name} will dissociate");

                foreach (var (product, stoich) in dissociationReaction.Stoichiometry)
                {
                    if (stoich > 0) // Products only
                    {
                        var productCompound = compoundLib.FindFlexible(product);
                        if (productCompound != null)
                        {
                            initialState.SpeciesMoles[productCompound.Name] =
                                initialState.SpeciesMoles.GetValueOrDefault(productCompound.Name, 0) + moles * stoich;

                            var composition = reactionGenerator.ParseChemicalFormula(productCompound.ChemicalFormula);
                            foreach (var (element, elemStoich) in composition)
                            {
                                initialState.ElementalComposition[element] =
                                    initialState.ElementalComposition.GetValueOrDefault(element, 0) +
                                    moles * stoich * elemStoich;
                            }

                            Log($"  → {productCompound.Name}: {moles * stoich:E3} moles");
                        }
                        else
                        {
                            Warn($"Unable to resolve dissociation product '{product}' for {compound.Name}");
                        }
                    }
                }
            }
            else
            {
                // For compounds without dissociation (or already aqueous), add directly
                initialState.SpeciesMoles[compound.Name] =
                    initialState.SpeciesMoles.GetValueOrDefault(compound.Name, 0) + moles;

                var composition = reactionGenerator.ParseChemicalFormula(compound.ChemicalFormula);
                foreach (var (element, stoichiometry) in composition)
                {
                    initialState.ElementalComposition[element] =
                        initialState.ElementalComposition.GetValueOrDefault(element, 0) + moles * stoichiometry;
                }

                Log($"  - {compound.Name}: {moles:E3} moles (no dissociation)");
            }
        }

        Trace($"Species count passed to solver: {initialState.SpeciesMoles.Count}");

        // Now solve for equilibrium with the properly dissociated species
        var finalState = solver.SolveEquilibrium(initialState);
        Trace($"Solver converged -> pH={finalState.pH:F2}, pe={finalState.pe:F2}, IonicStrength={finalState.IonicStrength_molkg:E2}");

        // Create output table with all necessary columns
        var resultTable = new DataTable("Speciation_Results");
        resultTable.Columns.Add("#", typeof(int));
        resultTable.Columns.Add("Species", typeof(string));
        resultTable.Columns.Add("Formula", typeof(string));
        resultTable.Columns.Add("Phase", typeof(string));
        resultTable.Columns.Add("Moles", typeof(double));
        resultTable.Columns.Add("Conc_M", typeof(double));
        resultTable.Columns.Add("Activity", typeof(double));
        resultTable.Columns.Add("Gamma", typeof(double)); // Activity coefficient

        // Create terminal output
        var terminalOutput = new StringBuilder();
        string FormatSpecLine(string content) => $"| {content.PadRight(70)} |";

        terminalOutput.AppendLine("+--------------------------------------------------------------------------+");
        terminalOutput.AppendLine("|                    AQUEOUS SPECIATION RESULTS                            |");
        terminalOutput.AppendLine("+--------------------------------------------------------------------------+");
        terminalOutput.AppendLine(FormatSpecLine($"Temperature: {finalState.Temperature_K:F2} K ({finalState.Temperature_K - 273.15:F2} °C)"));
        terminalOutput.AppendLine(FormatSpecLine($"Pressure:    {finalState.Pressure_bar:F2} bar"));
        terminalOutput.AppendLine(FormatSpecLine($"pH:          {finalState.pH:F2}"));
        terminalOutput.AppendLine(FormatSpecLine($"pe:          {finalState.pe:F2}"));
        terminalOutput.AppendLine(FormatSpecLine($"Ionic Str:   {finalState.IonicStrength_molkg:F4} mol/kg"));
        terminalOutput.AppendLine("+--------------------------------------------------------------------------+");
        terminalOutput.AppendLine("| Species        Formula    Conc(M)      Activity    Moles       Gamma    |");
        terminalOutput.AppendLine("+--------------------------------------------------------------------------+");

        // Sort species by concentration (descending)
        var sortedSpecies = finalState.SpeciesMoles
            .Where(kvp => kvp.Value > 1e-15) // Only significant species
            .OrderByDescending(kvp => kvp.Value)
            .ToList();

        int rowNum = 1;
        foreach (var (speciesName, moles) in sortedSpecies)
        {
            var species = compoundLib.FindFlexible(speciesName);
            if (species == null) continue;

            // Only show aqueous species in speciation results
            if (species.Phase != CompoundPhase.Aqueous) continue;

            var concentration = moles / finalState.Volume_L;
            var activity = finalState.Activities.GetValueOrDefault(speciesName, concentration);
            var activityCoeff = concentration > 0 ? activity / concentration : 1.0;

            // Skip very low concentrations unless it's a major component
            if (concentration < 1e-10 && !IsImportantSpecies(speciesName))
                continue;

            resultTable.Rows.Add(
                rowNum++,
                species.Name,
                species.ChemicalFormula,
                "Aqueous",
                moles,
                concentration,
                activity,
                activityCoeff
            );

            terminalOutput.AppendLine(
                $"| {species.Name,-14} {species.ChemicalFormula,-10} {concentration,10:E2} {activity,10:E2} {moles,10:E2} {activityCoeff,7:F3} |");
        }

        terminalOutput.AppendLine("+--------------------------------------------------------------------------+");

        // Show predominant species summary
        terminalOutput.AppendLine("=== PREDOMINANT SPECIES ===");

        // Group by element and show main species
        var elementSpecies = new Dictionary<string, List<(string species, double conc)>>();

        foreach (var (speciesName, moles) in sortedSpecies)
        {
            var species = compoundLib.FindFlexible(speciesName);
            if (species == null || species.Phase != CompoundPhase.Aqueous) continue;

            var elements = reactionGenerator.ParseChemicalFormula(species.ChemicalFormula);
            var concentration = moles / finalState.Volume_L;

            foreach (var element in elements.Keys)
            {
                if (element == "H" || element == "O") continue; // Skip H and O

                if (!elementSpecies.ContainsKey(element))
                    elementSpecies[element] = new List<(string, double)>();

                elementSpecies[element].Add((species.ChemicalFormula, concentration));
            }
        }

        foreach (var (element, speciesList) in elementSpecies)
        {
            var topSpecies = speciesList.OrderByDescending(s => s.conc).Take(3).ToList();
            terminalOutput.AppendLine($"{element}: {string.Join(", ", topSpecies.Select(s => $"{s.species} ({s.conc:E2} M)"))}");
        }

        // Print to console
        Console.WriteLine(terminalOutput.ToString());

        if (enableDiagnostics)
        {
            Trace($"Result table rows: {resultTable.Rows.Count}");
            Trace("Diagnostic speciation run complete");
        }

        return Task.FromResult<Dataset>(new TableDataset("Speciation_Results", resultTable));
    }

    /// <summary>
    /// Check if a species is important to always show.
    /// </summary>
    private static bool IsImportantSpecies(string speciesName)
    {
        var importantSpecies = new HashSet<string>
        {
            "H⁺", "Proton", "OH⁻", "Hydroxide",
            "H₂O", "Water",
            "e⁻", "Electron"
        };

        return importantSpecies.Contains(speciesName);
    }
}

public class SpeciateCommand : IGeoScriptCommand
{
    public string Name => "SPECIATE";
    public string HelpText => "Shows dissociation/speciation products when compounds dissolve in water.";
    public string Usage => "SPECIATE <Compounds> [TEMP <val> C|K] [PRES <val> BAR|ATM]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        return SpeciateCommandExecutor.ExecuteAsync(context, (CommandNode)node, enableDiagnostics: false);
    }
}

public class DiagnoseSpeciateCommand : IGeoScriptCommand
{
    public string Name => "DIAGNOSE_SPECIATE";
    public string HelpText => "Runs SPECIATE with verbose tracing information for debugging.";
    public string Usage => "DIAGNOSE_SPECIATE <Compounds> [TEMP <val> C|K] [PRES <val> BAR|ATM]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        return SpeciateCommandExecutor.ExecuteAsync(context, (CommandNode)node, enableDiagnostics: true);
    }
}

public class DiagnosticThermodynamicCommand : IGeoScriptCommand
{
    public string Name => "DIAGNOSTIC_THERMODYNAMIC";
    public string HelpText =>
        "Runs a NaCl dissolution benchmark and logs every step to verify the thermodynamic simulator.";

    public string Usage => "DIAGNOSTIC_THERMODYNAMIC";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        const string prefix = "[DIAGNOSTIC_THERMODYNAMIC]";
        Logger.Log($"{prefix} Step 1/4: Resolving reference compounds for NaCl dissolution benchmark.");

        var compoundLib = CompoundLibrary.Instance;
        var reactionGenerator = new ReactionGenerator(compoundLib);
        var solver = new ThermodynamicSolver();

        var water = compoundLib.FindFlexible("H2O") ??
                    compoundLib.FindFlexible("Water") ??
                    compoundLib.FindFlexible("H₂O");
        var halite = compoundLib.FindFlexible("NaCl") ??
                     compoundLib.FindFlexible("Halite");
        var sodiumIon = compoundLib.FindFlexible("Na+") ??
                        compoundLib.FindFlexible("Sodium Ion");
        var chlorideIon = compoundLib.FindFlexible("Cl-") ??
                          compoundLib.FindFlexible("Chloride Ion");

        var missing = new List<string>();
        if (water == null) missing.Add("H2O");
        if (halite == null) missing.Add("NaCl");
        if (sodiumIon == null) missing.Add("Na+");
        if (chlorideIon == null) missing.Add("Cl-");

        if (missing.Count > 0)
        {
            var message = $"{prefix} Missing required reference compounds: {string.Join(", ", missing)}";
            Logger.LogError(message);
            throw new InvalidOperationException(message);
        }

        Logger.Log(
            $"{prefix} Step 2/4: Constructing initial state (1 L water + 1 mol halite at 25°C, 1 bar).");

        var benchmarkState = new ThermodynamicState
        {
            Temperature_K = 298.15,
            Pressure_bar = 1.0,
            Volume_L = 1.0
        };

        void AddCompound(ChemicalCompound compound, double moles, string reason)
        {
            Logger.Log(
                $"{prefix} Adding {moles:F3} mol of {compound.Name} ({compound.ChemicalFormula}) [{reason}]");
            benchmarkState.SpeciesMoles[compound.Name] =
                benchmarkState.SpeciesMoles.GetValueOrDefault(compound.Name, 0) + moles;

            var composition = reactionGenerator.ParseChemicalFormula(compound.ChemicalFormula);
            foreach (var (element, stoichiometry) in composition)
            {
                var contribution = moles * stoichiometry;
                benchmarkState.ElementalComposition[element] =
                    benchmarkState.ElementalComposition.GetValueOrDefault(element, 0) + contribution;
                Logger.Log(
                    $"{prefix} -> {element}: +{contribution:F3} mol (total {benchmarkState.ElementalComposition[element]:F3} mol)");
            }
        }

        const double waterMoles = 55.508; // 1 L of water
        AddCompound(water, waterMoles, "bulk solvent");

        const double haliteMoles = 1.0; // Benchmark solute
        AddCompound(halite, haliteMoles, "solid benchmark solute");

        var elementSummary = string.Join(", ",
            benchmarkState.ElementalComposition.Select(kvp => $"{kvp.Key}={kvp.Value:F3} mol"));
        Logger.Log($"{prefix} Element totals before equilibration: {elementSummary}");

        Logger.Log($"{prefix} Step 3/4: Running thermodynamic solver...");
        var finalState = solver.SolveEquilibrium(benchmarkState);
        Logger.Log(
            $"{prefix} Solver finished. pH={finalState.pH:F2}, pe={finalState.pe:F2}, IonicStrength={finalState.IonicStrength_molkg:F4} mol/kg");

        Logger.Log($"{prefix} Step 4/4: Comparing results with theoretical NaCl dissolution.");

        double GetFinalMoles(ChemicalCompound compound)
        {
            return compound != null && finalState.SpeciesMoles.TryGetValue(compound.Name, out var moles)
                ? moles
                : 0.0;
        }

        var actualNa = GetFinalMoles(sodiumIon);
        var actualCl = GetFinalMoles(chlorideIon);
        var actualHalite = GetFinalMoles(halite);

        const double expectedNa = 1.0;
        const double expectedCl = 1.0;
        const double expectedHalite = 0.0;
        const double tolerance = 0.05;

        var resultTable = new DataTable("ThermodynamicDiagnostic");
        resultTable.Columns.Add("Species", typeof(string));
        resultTable.Columns.Add("ExpectedMoles", typeof(double));
        resultTable.Columns.Add("ActualMoles", typeof(double));
        resultTable.Columns.Add("AbsoluteDifference", typeof(double));
        resultTable.Columns.Add("Pass", typeof(bool));

        void AddResultRow(string label, double expected, double actual)
        {
            var difference = Math.Abs(actual - expected);
            var pass = difference <= tolerance;
            var row = resultTable.NewRow();
            row["Species"] = label;
            row["ExpectedMoles"] = expected;
            row["ActualMoles"] = actual;
            row["AbsoluteDifference"] = difference;
            row["Pass"] = pass;
            resultTable.Rows.Add(row);
            Logger.Log(
                $"{prefix} {label}: expected {expected:F3} mol, got {actual:F3} mol (Delta={difference:E2}) -> {(pass ? "PASS" : "FAIL")}");
        }

        AddResultRow($"{sodiumIon.Name} (aq)", expectedNa, actualNa);
        AddResultRow($"{chlorideIon.Name} (aq)", expectedCl, actualCl);
        AddResultRow($"{halite.Name} (s)", expectedHalite, actualHalite);

        var diagnosticPass = Math.Abs(actualNa - expectedNa) <= tolerance &&
                             Math.Abs(actualCl - expectedCl) <= tolerance &&
                             Math.Abs(actualHalite - expectedHalite) <= tolerance;
        Logger.Log(
            $"{prefix} Diagnostic verdict: {(diagnosticPass ? "PASS" : "FAIL")} within ±{tolerance:F3} mol tolerance.");

        Logger.Log(
            $"{prefix} expected results: Na+={expectedNa:F3} mol, Cl-={expectedCl:F3} mol, Halite(s)={expectedHalite:F3} mol");
        Logger.Log(
            $"{prefix} Got: Na+={actualNa:F3} mol, Cl-={actualCl:F3} mol, Halite(s)={actualHalite:F3} mol");

        Logger.Log($"{prefix} Diagnostic complete. Results table ready for inspection.");

        return Task.FromResult<Dataset>(new TableDataset("ThermodynamicDiagnostic", resultTable));
    }
}

#endregion
