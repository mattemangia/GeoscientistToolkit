// NodeEndpoint/TableDatasetDTO.cs
// Minimal DTO types needed for TableDataset.cs (headless server)

namespace GeoscientistToolkit.Data;

// Base DTO
public class DatasetDTO
{
    public string TypeName { get; set; }
    public string Name { get; set; }
    public string FilePath { get; set; }
    public DatasetMetadataDTO Metadata { get; set; } = new();
}

public class DatasetMetadataDTO
{
    public string SampleName { get; set; }
    public string LocationName { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public float? CoordinatesX { get; set; }
    public float? CoordinatesY { get; set; }
    public double? Depth { get; set; }
    public float? SizeX { get; set; }
    public float? SizeY { get; set; }
    public float? SizeZ { get; set; }
    public string SizeUnit { get; set; }
    public DateTime? CollectionDate { get; set; }
    public string Collector { get; set; }
    public string Notes { get; set; }
    public Dictionary<string, string> CustomFields { get; set; } = new();
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
