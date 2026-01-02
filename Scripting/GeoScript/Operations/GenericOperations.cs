using GeoscientistToolkit.Data;
using GeoscientistToolkit.Util;
using System;
using System.Collections.Generic;

namespace GeoscientistToolkit.Scripting.GeoScript.Operations
{
    /// <summary>
    /// Copy a dataset
    /// </summary>
    public class CopyOperation : IOperation
    {
        public string Name => "COPY";
        public string Description => "Create a copy of the dataset";
        public Dictionary<string, string> Parameters => new()
        {
            { "name", "Name for the copied dataset" }
        };

        public Dataset Execute(Dataset inputDataset, List<object> parameters)
        {
            if (inputDataset == null)
                throw new ArgumentNullException(nameof(inputDataset));

            var clone = inputDataset.Clone();
            var newName = parameters.Count > 0 && parameters[0] is string name && !string.IsNullOrWhiteSpace(name)
                ? name
                : $"{inputDataset.Name}_copy";

            clone.Name = newName;
            Logger.Log($"Copied dataset '{inputDataset.Name}' to '{newName}'");
            return clone;
        }

        public bool CanApplyTo(DatasetType type) => true; // Works on all types
    }

    /// <summary>
    /// Rename a dataset
    /// </summary>
    public class RenameOperation : IOperation
    {
        public string Name => "RENAME";
        public string Description => "Rename the dataset";
        public Dictionary<string, string> Parameters => new()
        {
            { "name", "New name for the dataset" }
        };

        public Dataset Execute(Dataset inputDataset, List<object> parameters)
        {
            if (parameters.Count > 0 && parameters[0] is string newName)
            {
                inputDataset.Name = newName;
                Logger.Log($"Renamed dataset to: {newName}");
            }
            else
            {
                Logger.Log($"RENAME operation requires a name parameter");
            }
            return inputDataset;
        }

        public bool CanApplyTo(DatasetType type) => true; // Works on all types
    }
}
