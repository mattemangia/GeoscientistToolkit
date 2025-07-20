// GeoscientistToolkit/Data/ISerializableDataset.cs
namespace GeoscientistToolkit.Data
{
    /// <summary>
    /// Defines a contract for datasets that can be serialized.
    /// </summary>
    public interface ISerializableDataset
    {
        /// <summary>
        /// Creates a data transfer object (DTO) from the dataset instance.
        /// </summary>
        /// <returns>A serializable object representing the dataset's state.</returns>
        object ToSerializableObject();
    }
}