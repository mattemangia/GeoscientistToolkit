using GeoscientistToolkit.Data;
using System.Collections.Generic;

namespace GeoscientistToolkit.Scripting.GeoScript.Operations
{
    /// <summary>
    /// Interface for all GeoScript operations
    /// </summary>
    public interface IOperation
    {
        /// <summary>
        /// Name of the operation (e.g., "BRIGHTNESS_CONTRAST", "FILTER")
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Description of the operation
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Parameter names and descriptions
        /// </summary>
        Dictionary<string, string> Parameters { get; }

        /// <summary>
        /// Execute the operation on a dataset
        /// </summary>
        /// <param name="inputDataset">Input dataset</param>
        /// <param name="parameters">Operation parameters</param>
        /// <returns>Output dataset</returns>
        Dataset Execute(Dataset inputDataset, List<object> parameters);

        /// <summary>
        /// Check if this operation can be applied to the given dataset type
        /// </summary>
        bool CanApplyTo(DatasetType type);
    }
}
