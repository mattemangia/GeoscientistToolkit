// GeoscientistToolkit/Business/GeoScript/GeoScript.cs

using System.Data;
using System.Text.RegularExpressions;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.GIS;
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
            new ContourCommand()
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