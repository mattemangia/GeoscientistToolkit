// GeoscientistToolkit/UI/Interfaces/IDatasetPropertiesRenderer.cs
using GeoscientistToolkit.Data;

namespace GeoscientistToolkit.UI.Interfaces
{
    /// <summary>
    /// Interface for rendering dataset-specific properties in the Properties panel.
    /// </summary>
    public interface IDatasetPropertiesRenderer
    {
        void Draw(Dataset dataset);
    }
}