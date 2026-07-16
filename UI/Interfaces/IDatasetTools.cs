// GAIA/UI/Interfaces/IDatasetTools.cs

using GAIA.Data;

namespace GAIA.UI.Interfaces;

/// <summary>
///     Interface for dataset-specific tools in the Tools panel.
/// </summary>
public interface IDatasetTools
{
    void Draw(Dataset dataset);
}