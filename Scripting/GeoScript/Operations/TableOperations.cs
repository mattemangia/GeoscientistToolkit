using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.Util;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;

namespace GeoscientistToolkit.Scripting.GeoScript.Operations
{
    /// <summary>
    /// Filter rows in a table based on conditions
    /// </summary>
    public class FilterRowsOperation : IOperation
    {
        public string Name => "FILTER_ROWS";
        public string Description => "Filter rows based on conditions";
        public Dictionary<string, string> Parameters => new()
        {
            { "column", "Column name to filter on" },
            { "operator", "Comparison operator (>, <, =, !=, etc.)" },
            { "value", "Value to compare against" }
        };

        public Dataset Execute(Dataset inputDataset, List<object> parameters)
        {
            if (inputDataset is not TableDataset tableDataset)
                throw new InvalidOperationException("FILTER_ROWS can only be applied to table datasets");

            if (parameters.Count < 3)
                throw new ArgumentException("FILTER_ROWS requires column, operator, and value parameters");

            var columnName = parameters[0]?.ToString();
            var op = parameters[1]?.ToString();
            var value = parameters[2]?.ToString();

            if (string.IsNullOrWhiteSpace(columnName) || string.IsNullOrWhiteSpace(op))
                throw new ArgumentException("FILTER_ROWS requires a column name and operator");

            var data = TableOperationHelpers.EnsureDataTable(tableDataset);
            if (!data.Columns.Contains(columnName))
                throw new ArgumentException($"Column '{columnName}' not found");

            var filteredTable = data.Clone();
            foreach (DataRow row in data.Rows)
            {
                var cellValue = row[columnName];
                if (TableOperationHelpers.MatchesCondition(cellValue, op, value))
                    filteredTable.ImportRow(row);
            }

            return new TableDataset($"{inputDataset.Name}_filtered", filteredTable);
        }

        public bool CanApplyTo(DatasetType type) => type == DatasetType.Table;
    }

    /// <summary>
    /// Sort table rows by column
    /// </summary>
    public class SortOperation : IOperation
    {
        public string Name => "SORT";
        public string Description => "Sort rows by column value";
        public Dictionary<string, string> Parameters => new()
        {
            { "column", "Column name to sort by" },
            { "order", "Sort order: asc or desc" }
        };

        public Dataset Execute(Dataset inputDataset, List<object> parameters)
        {
            if (inputDataset is not TableDataset tableDataset)
                throw new InvalidOperationException("SORT can only be applied to table datasets");

            if (parameters.Count < 1)
                throw new ArgumentException("SORT requires a column parameter");

            var columnName = parameters[0]?.ToString();
            var order = parameters.Count > 1 ? parameters[1]?.ToString() : "asc";

            if (string.IsNullOrWhiteSpace(columnName))
                throw new ArgumentException("SORT requires a column name");

            var data = TableOperationHelpers.EnsureDataTable(tableDataset);
            if (!data.Columns.Contains(columnName))
                throw new ArgumentException($"Column '{columnName}' not found");

            var normalizedOrder = string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
            var view = new DataView(data) { Sort = $"{columnName} {normalizedOrder}" };
            var sortedTable = view.ToTable();

            return new TableDataset($"{inputDataset.Name}_sorted", sortedTable);
        }

        public bool CanApplyTo(DatasetType type) => type == DatasetType.Table;
    }

    /// <summary>
    /// Select specific columns from table
    /// </summary>
    public class SelectColumnsOperation : IOperation
    {
        public string Name => "SELECT_COLUMNS";
        public string Description => "Select specific columns from table";
        public Dictionary<string, string> Parameters => new()
        {
            { "columns", "Comma-separated list of column names" }
        };

        public Dataset Execute(Dataset inputDataset, List<object> parameters)
        {
            if (inputDataset is not TableDataset tableDataset)
                throw new InvalidOperationException("SELECT_COLUMNS can only be applied to table datasets");

            if (parameters.Count < 1)
                throw new ArgumentException("SELECT_COLUMNS requires a columns parameter");

            var columnsParam = parameters[0]?.ToString();
            if (string.IsNullOrWhiteSpace(columnsParam))
                throw new ArgumentException("SELECT_COLUMNS requires column names");

            var requestedColumns = columnsParam
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToList();

            var data = TableOperationHelpers.EnsureDataTable(tableDataset);
            foreach (var column in requestedColumns)
                if (!data.Columns.Contains(column))
                    throw new ArgumentException($"Column '{column}' not found");

            var selected = new DataTable($"{data.TableName}_selected");
            foreach (var column in requestedColumns)
                selected.Columns.Add(column, data.Columns[column].DataType);

            foreach (DataRow row in data.Rows)
            {
                var newRow = selected.NewRow();
                foreach (var column in requestedColumns)
                    newRow[column] = row[column];
                selected.Rows.Add(newRow);
            }

            return new TableDataset($"{inputDataset.Name}_selected", selected);
        }

        public bool CanApplyTo(DatasetType type) => type == DatasetType.Table;
    }

    /// <summary>
    /// Aggregate table data (sum, avg, count, etc.)
    /// </summary>
    public class AggregateOperation : IOperation
    {
        public string Name => "AGGREGATE";
        public string Description => "Aggregate data using functions like SUM, AVG, COUNT";
        public Dictionary<string, string> Parameters => new()
        {
            { "column", "Column to aggregate" },
            { "function", "Aggregation function: sum, avg, count, min, max" },
            { "groupby", "Optional column to group by" }
        };

        public Dataset Execute(Dataset inputDataset, List<object> parameters)
        {
            if (inputDataset is not TableDataset tableDataset)
                throw new InvalidOperationException("AGGREGATE can only be applied to table datasets");

            if (parameters.Count < 2)
                throw new ArgumentException("AGGREGATE requires column and function parameters");

            var columnName = parameters[0]?.ToString();
            var function = parameters[1]?.ToString();
            var groupByColumn = parameters.Count > 2 ? parameters[2]?.ToString() : null;

            if (string.IsNullOrWhiteSpace(function))
                throw new ArgumentException("AGGREGATE requires a function parameter");

            var data = TableOperationHelpers.EnsureDataTable(tableDataset);

            if (!string.IsNullOrWhiteSpace(columnName) &&
                columnName != "*" &&
                !data.Columns.Contains(columnName))
                throw new ArgumentException($"Column '{columnName}' not found");

            if (!string.IsNullOrWhiteSpace(groupByColumn) && !data.Columns.Contains(groupByColumn))
                throw new ArgumentException($"Group-by column '{groupByColumn}' not found");

            var resultTable = new DataTable($"{data.TableName}_aggregate");
            var resultColumnName = $"{function.ToUpperInvariant()}({columnName ?? "*"})";

            if (!string.IsNullOrWhiteSpace(groupByColumn))
                resultTable.Columns.Add(groupByColumn, data.Columns[groupByColumn].DataType);
            resultTable.Columns.Add(resultColumnName, typeof(double));

            var rows = data.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(groupByColumn))
            {
                var grouped = rows.GroupBy(r => r[groupByColumn]);
                foreach (var group in grouped)
                {
                    var aggregateValue = TableOperationHelpers.CalculateAggregate(
                        group,
                        columnName,
                        function);
                    var newRow = resultTable.NewRow();
                    newRow[groupByColumn] = group.Key;
                    newRow[resultColumnName] = aggregateValue;
                    resultTable.Rows.Add(newRow);
                }
            }
            else
            {
                var aggregateValue = TableOperationHelpers.CalculateAggregate(rows, columnName, function);
                var newRow = resultTable.NewRow();
                newRow[resultColumnName] = aggregateValue;
                resultTable.Rows.Add(newRow);
            }

            return new TableDataset($"{inputDataset.Name}_aggregate", resultTable);
        }

        public bool CanApplyTo(DatasetType type) => type == DatasetType.Table;

    }

    internal static class TableOperationHelpers
    {
        public static DataTable EnsureDataTable(TableDataset dataset)
        {
            var table = dataset.GetDataTable();
            if (table == null)
            {
                dataset.Load();
                table = dataset.GetDataTable();
            }
            return table ?? new DataTable();
        }

        public static bool MatchesCondition(object cellValue, string op, string targetValue)
        {
            if (cellValue == null || cellValue == DBNull.Value)
                return false;

            var opNormalized = op.Trim();
            var cellText = Convert.ToString(cellValue, CultureInfo.InvariantCulture) ?? string.Empty;

            var cellIsNumber = double.TryParse(cellText, NumberStyles.Any, CultureInfo.InvariantCulture, out var cellNum);
            var targetIsNumber = double.TryParse(targetValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var targetNum);

            if (cellIsNumber && targetIsNumber)
            {
                return opNormalized switch
                {
                    ">" => cellNum > targetNum,
                    "<" => cellNum < targetNum,
                    ">=" => cellNum >= targetNum,
                    "<=" => cellNum <= targetNum,
                    "=" or "==" => Math.Abs(cellNum - targetNum) < double.Epsilon,
                    "!=" => Math.Abs(cellNum - targetNum) >= double.Epsilon,
                    _ => false
                };
            }

            return opNormalized switch
            {
                "=" or "==" => string.Equals(cellText, targetValue, StringComparison.OrdinalIgnoreCase),
                "!=" => !string.Equals(cellText, targetValue, StringComparison.OrdinalIgnoreCase),
                "contains" => cellText.Contains(targetValue ?? string.Empty, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        public static double CalculateAggregate(IEnumerable<DataRow> rows, string columnName, string function)
        {
            var normalized = function.Trim().ToLowerInvariant();
            if ((string.IsNullOrWhiteSpace(columnName) || columnName == "*") && normalized != "count")
                throw new ArgumentException("Aggregate column must be specified for functions other than COUNT");

            if (normalized == "count" && (string.IsNullOrWhiteSpace(columnName) || columnName == "*"))
                return rows.Count();

            var numericValues = rows
                .Select(r => r[columnName])
                .Select(v => Convert.ToString(v, CultureInfo.InvariantCulture))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? (double?)d : null)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();

            return normalized switch
            {
                "count" => rows.Count(r => r[columnName] != null && r[columnName] != DBNull.Value),
                "sum" => numericValues.Sum(),
                "avg" or "mean" => numericValues.Count > 0 ? numericValues.Average() : 0,
                "min" => numericValues.Count > 0 ? numericValues.Min() : 0,
                "max" => numericValues.Count > 0 ? numericValues.Max() : 0,
                _ => throw new ArgumentException($"Unsupported aggregate function '{function}'")
            };
        }
    }
}
