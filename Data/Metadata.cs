// GeoscientistToolkit/Data/Metadata.cs
using System;
using System.Collections.Generic;
using System.Numerics;

namespace GeoscientistToolkit.Data
{
    /// <summary>
    /// Metadata for individual datasets
    /// </summary>
    public class DatasetMetadata
    {
        public string SampleName { get; set; } = "";
        public string LocationName { get; set; } = "";
        public double? Latitude { get; set; } // WGS 84 decimal degrees
        public double? Longitude { get; set; } // WGS 84 decimal degrees
        public double? Depth { get; set; } // Depth in meters
        public Vector3? Size { get; set; } // Physical size (x, y, z)
        public string SizeUnit { get; set; } = "mm";
        public string Notes { get; set; } = "";
        public DateTime? CollectionDate { get; set; }
        public string Collector { get; set; } = "";
        public Dictionary<string, string> CustomFields { get; set; } = new Dictionary<string, string>();

        public DatasetMetadata Clone()
        {
            return new DatasetMetadata
            {
                SampleName = SampleName,
                LocationName = LocationName,
                Latitude = Latitude,
                Longitude = Longitude,
                Depth = Depth,
                Size = Size,
                SizeUnit = SizeUnit,
                Notes = Notes,
                CollectionDate = CollectionDate,
                Collector = Collector,
                CustomFields = new Dictionary<string, string>(CustomFields)
            };
        }
    }

    /// <summary>
    /// Project-level metadata
    /// </summary>
    public class ProjectMetadata
    {
        public string Organisation { get; set; } = "";
        public string Department { get; set; } = "";
        public int? Year { get; set; }
        public string Expedition { get; set; } = "";
        public string Author { get; set; } = "";
        public string ProjectDescription { get; set; } = "";
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string FundingSource { get; set; } = "";
        public string License { get; set; } = "";
        public Dictionary<string, string> CustomFields { get; set; } = new Dictionary<string, string>();

        public ProjectMetadata Clone()
        {
            return new ProjectMetadata
            {
                Organisation = Organisation,
                Department = Department,
                Year = Year,
                Expedition = Expedition,
                Author = Author,
                ProjectDescription = ProjectDescription,
                StartDate = StartDate,
                EndDate = EndDate,
                FundingSource = FundingSource,
                License = License,
                CustomFields = new Dictionary<string, string>(CustomFields)
            };
        }
    }
}