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
        public DatasetMetadataDTO Metadata { get; set; } = new DatasetMetadataDTO();
        
    }
    public class ProjectMetadataDTO
    {
        public string Organisation { get; set; }
        public string Department { get; set; }
        public int? Year { get; set; }
        public string Expedition { get; set; }
        public string Author { get; set; }
        public string ProjectDescription { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string FundingSource { get; set; }
        public string License { get; set; }
        public Dictionary<string, string> CustomFields { get; set; } = new Dictionary<string, string>();
    }
    public class DatasetMetadataDTO
    {
        public string SampleName { get; set; }
        public string LocationName { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public double? Depth { get; set; }
        public float? SizeX { get; set; }
        public float? SizeY { get; set; }
        public float? SizeZ { get; set; }
        public string SizeUnit { get; set; }
        public DateTime? CollectionDate { get; set; }
        public string Collector { get; set; }
        public string Notes { get; set; }
        public Dictionary<string, string> CustomFields { get; set; } = new Dictionary<string, string>();
    }
    public class TableDatasetDTO : DatasetDTO
    {
        public string SourceFormat { get; set; }
        public string Delimiter { get; set; }
        public bool HasHeaders { get; set; }
        public string Encoding { get; set; }
        public int RowCount { get; set; }
        public int ColumnCount { get; set; }
        public List<string> ColumnNames { get; set; }
        public List<string> ColumnTypes { get; set; }
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
        
        public string PhysicalMaterialName { get; set; }
    }

    // DTO for ImageDataset
    public class ImageDatasetDTO:DatasetDTO
    {
        public string TypeName { get; set; }
        public string Name { get; set; }
        public string FilePath { get; set; }
        public float PixelSize { get; set; }
        public string Unit { get; set; }
        public long Tags { get; set; } 
        public Dictionary<string, string> ImageMetadata { get; set; }
        public string SegmentationPath { get; set; }
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
    public class AcousticVolumeDatasetDTO : DatasetDTO
    {
        public double PWaveVelocity { get; set; }
        public double SWaveVelocity { get; set; }
        public double VpVsRatio { get; set; }
        public int TimeSteps { get; set; }
        public double ComputationTimeSeconds { get; set; }
        public float YoungsModulusMPa { get; set; }
        public float PoissonRatio { get; set; }
        public float ConfiningPressureMPa { get; set; }
        public float SourceFrequencyKHz { get; set; }
        public float SourceEnergyJ { get; set; }
        public string SourceDatasetPath { get; set; }
        public string SourceMaterialName { get; set; }
        public bool HasTimeSeries { get; set; }
        public int TimeSeriesCount { get; set; }
    }
    // Main project file structure
    public class ProjectFileDTO
    {
        public string ProjectName { get; set; }
        public List<DatasetDTO> Datasets { get; set; } = new List<DatasetDTO>();
        public ProjectMetadataDTO ProjectMetadata { get; set; } = new ProjectMetadataDTO();
    }
    public class StreamingCtVolumeDatasetDTO : DatasetDTO
    {
        /// <summary>
        /// The file path to the corresponding CtImageStackDataset's folder, used to link the editable partner.
        /// </summary>
        public string PartnerFilePath { get; set; }
    }

    // --- NEW DTOs for PNM ---
    public class PoreDTO
    {
        public int ID { get; set; }
        public Vector3 Position { get; set; }
        public float Area { get; set; }
        public float VolumeVoxels { get; set; }
        public float VolumePhysical { get; set; }
        public int Connections { get; set; }
        public float Radius { get; set; }
    }

    public class ThroatDTO
    {
        public int ID { get; set; }
        public int Pore1ID { get; set; }
        public int Pore2ID { get; set; }
        public float Radius { get; set; }
    }

    public class PNMDatasetDTO : DatasetDTO
    {
        public float VoxelSize { get; set; }
        public float Tortuosity { get; set; }
        public float DarcyPermeability { get; set; }
        public float NavierStokesPermeability { get; set; }
        public float LatticeBoltzmannPermeability { get; set; }
        public List<PoreDTO> Pores { get; set; } = new List<PoreDTO>();
        public List<ThroatDTO> Throats { get; set; } = new List<ThroatDTO>();
    }
}