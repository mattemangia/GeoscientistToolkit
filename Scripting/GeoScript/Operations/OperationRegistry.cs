using GeoscientistToolkit.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GeoscientistToolkit.Scripting.GeoScript.Operations
{
    /// <summary>
    /// Registry of all available operations for each dataset type
    /// </summary>
    public static class OperationRegistry
    {
        private static readonly Dictionary<DatasetType, List<string>> _operationsByType = new();
        private static readonly Dictionary<string, IOperation> _operations = new(StringComparer.OrdinalIgnoreCase);

        static OperationRegistry()
        {
            RegisterOperations();
        }

        /// <summary>
        /// Register all available operations
        /// </summary>
        private static void RegisterOperations()
        {
            // Image operations
            RegisterImageOperations();

            // CT Image Stack operations
            RegisterCtImageStackOperations();

            // Table operations
            RegisterTableOperations();

            // GIS operations
            RegisterGISOperations();

            // Generic operations (work on all types)
            RegisterGenericOperations();
        }

        private static void RegisterImageOperations()
        {
            var imageOps = new List<IOperation>
            {
                new BrightnessContrastOperation(),
                new FilterOperation(),
                new ThresholdOperation(),
                new BinarizeOperation(),
                new ResizeOperation(),
                new CropOperation(),
                new RotateOperation(),
                new FlipOperation(),
                new NormalizeOperation(),
                new InvertOperation(),
                new GrayscaleOperation(),
                new HistogramEqualizeOperation()
            };

            foreach (var op in imageOps)
            {
                _operations[op.Name] = op;
                AddOperationToType(DatasetType.SingleImage, op.Name);
            }
        }

        private static void RegisterCtImageStackOperations()
        {
            var ctOps = new List<IOperation>
            {
                new BrightnessContrastOperation(),
                new FilterOperation(),
                new ThresholdOperation(),
                new BinarizeOperation(),
                new NormalizeOperation()
            };

            foreach (var op in ctOps)
            {
                if (!_operations.ContainsKey(op.Name))
                    _operations[op.Name] = op;
                AddOperationToType(DatasetType.CtImageStack, op.Name);
            }
        }

        private static void RegisterTableOperations()
        {
            var tableOps = new List<IOperation>
            {
                new FilterRowsOperation(),
                new SortOperation(),
                new SelectColumnsOperation(),
                new AggregateOperation()
            };

            foreach (var op in tableOps)
            {
                if (!_operations.ContainsKey(op.Name))
                    _operations[op.Name] = op;
                AddOperationToType(DatasetType.Table, op.Name);
            }
        }

        private static void RegisterGISOperations()
        {
            var gisOps = new List<IOperation>
            {
                new BufferOperation(),
                new ClipOperation(),
                new UnionOperation(),
                new IntersectOperation()
            };

            foreach (var op in gisOps)
            {
                if (!_operations.ContainsKey(op.Name))
                    _operations[op.Name] = op;
                AddOperationToType(DatasetType.GIS, op.Name);
            }
        }

        private static void RegisterGenericOperations()
        {
            // These operations work on all dataset types
            var genericOps = new List<IOperation>
            {
                new CopyOperation(),
                new RenameOperation()
            };

            foreach (var op in genericOps)
            {
                _operations[op.Name] = op;
            }
        }

        private static void AddOperationToType(DatasetType type, string operationName)
        {
            if (!_operationsByType.ContainsKey(type))
                _operationsByType[type] = new List<string>();

            if (!_operationsByType[type].Contains(operationName, StringComparer.OrdinalIgnoreCase))
                _operationsByType[type].Add(operationName);
        }

        /// <summary>
        /// Get all operations available for a dataset type
        /// </summary>
        public static List<string> GetOperationsForType(DatasetType type)
        {
            if (_operationsByType.TryGetValue(type, out var ops))
                return new List<string>(ops);
            return new List<string>();
        }

        /// <summary>
        /// Get operation by name
        /// </summary>
        public static IOperation GetOperation(string name)
        {
            _operations.TryGetValue(name, out var operation);
            return operation;
        }

        /// <summary>
        /// Check if operation exists
        /// </summary>
        public static bool HasOperation(string name)
        {
            return _operations.ContainsKey(name);
        }

        /// <summary>
        /// Get all operation names
        /// </summary>
        public static List<string> GetAllOperationNames()
        {
            return _operations.Keys.ToList();
        }
    }
}
