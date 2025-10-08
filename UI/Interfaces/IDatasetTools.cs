// GeoscientistToolkit/UI/Interfaces/IDatasetTools.cs

using GeoscientistToolkit.Data;

namespace GeoscientistToolkit.UI.Interfaces;

/// <summary>
///     Interface for dataset-specific tools in the Tools panel.
/// </summary>
public interface IDatasetTools
{
    void Draw(Dataset dataset);
}