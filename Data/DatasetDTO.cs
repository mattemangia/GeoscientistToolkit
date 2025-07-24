// GeoscientistToolkit/Data/DatasetDTO.cs
using System.Collections.Generic;
using System.Numerics; // Added for Vector4

namespace GeoscientistToolkit.Data
{
    // Base DTO
    public class DatasetDTO
    {
        public string TypeName { get; set; }
        public string Name { get; set; }
        public string FilePath { get; set; }
    }

    // --- NEW --- DTO for Material
    public class MaterialDTO
    {
        public byte ID { get; set; }
        public string Name { get; set; }
        public Vector4 Color { get; set; }
        public byte MinValue { get; set; }
        public byte MaxValue { get; set; }
        public bool IsExterior { get; set; }
        public double Density { get; set; }
        public bool IsVisible { get; set; }
    }

    // DTO for ImageDataset
    public class ImageDatasetDTO : DatasetDTO
    {
        public float PixelSize { get; set; }
        public string Unit { get; set; }
    }

    // --- MODIFIED --- DTO for CtImageStackDataset
    public class CtImageStackDatasetDTO : DatasetDTO
    {
        public float PixelSize { get; set; }
        public float SliceThickness { get; set; }
        public string Unit { get; set; }
        public int BinningSize { get; set; }

        // List of materials associated with this dataset
        public List<MaterialDTO> Materials { get; set; } = new List<MaterialDTO>();
    }

    // DTO for DatasetGroup
    public class DatasetGroupDTO : DatasetDTO
    {
        public List<DatasetDTO> Datasets { get; set; } = new List<DatasetDTO>();
    }

    // Main project file structure
    public class ProjectFileDTO
    {
        public string ProjectName { get; set; }
        public List<DatasetDTO> Datasets { get; set; } = new List<DatasetDTO>();
    }
    public class StreamingCtVolumeDatasetDTO : DatasetDTO
    {
        /// <summary>
        /// The file path to the corresponding CtImageStackDataset's folder, used to link the editable partner.
        /// </summary>
        public string PartnerFilePath { get; set; }
    }
}