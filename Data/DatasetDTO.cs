// GeoscientistToolkit/Data/DatasetDTO.cs
using System.Collections.Generic;

namespace GeoscientistToolkit.Data
{
    // Base DTO
    public class DatasetDTO
    {
        public string TypeName { get; set; }
        public string Name { get; set; }
        public string FilePath { get; set; }
    }

    // DTO for ImageDataset
    public class ImageDatasetDTO : DatasetDTO
    {
        public float PixelSize { get; set; }
        public string Unit { get; set; }
    }

    // DTO for CtImageStackDataset
    public class CtImageStackDatasetDTO : DatasetDTO
    {
        public float PixelSize { get; set; }
        public float SliceThickness { get; set; }
        public string Unit { get; set; }
        public int BinningSize { get; set; }
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
}