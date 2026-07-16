// GAIA/UI/Interfaces/IDatasetPropertiesRenderer.cs

using GAIA.Data;

namespace GAIA.UI.Interfaces;

/// <summary>
///     Interface for rendering dataset-specific properties in the Properties panel.
/// </summary>
public interface IDatasetPropertiesRenderer
{
    void Draw(Dataset dataset);
}