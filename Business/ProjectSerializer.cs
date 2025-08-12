// GeoscientistToolkit/Business/ProjectSerializer.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Util;
using GeoscientistToolkit.Data.Table;

namespace GeoscientistToolkit.Business
{
    public static class ProjectSerializer
    {
        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new DatasetDTOConverter() }
        };

        public static void SaveProject(ProjectManager project, string path)
        {
            var projectDto = new ProjectFileDTO
            {
                ProjectName = project.ProjectName,
                ProjectMetadata = ConvertToProjectMetadataDTO(project.ProjectMetadata)
            };

            foreach (var dataset in project.LoadedDatasets)
            {
                if (dataset is ISerializableDataset serializable)
                {
                    var dto = (DatasetDTO)serializable.ToSerializableObject();
                    
                    // Add metadata to DTO
                    dto.Metadata = ConvertToDatasetMetadataDTO(dataset.DatasetMetadata);
                    
                    projectDto.Datasets.Add(dto);
                }
            }

            try
            {
                string jsonString = JsonSerializer.Serialize(projectDto, _options);
                File.WriteAllText(path, jsonString);
                Logger.Log($"Project saved successfully to {path}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to save project: {ex.Message}");
            }
        }

        public static ProjectFileDTO LoadProject(string path)
        {
            try
            {
                string jsonString = File.ReadAllText(path);
                var projectDto = JsonSerializer.Deserialize<ProjectFileDTO>(jsonString, _options);
                
                // Convert DTOs back to metadata objects
                if (projectDto != null)
                {
                    ProjectManager.Instance.ProjectMetadata = ConvertFromProjectMetadataDTO(projectDto.ProjectMetadata);
                }
                
                Logger.Log($"Project loaded successfully from {path}");
                return projectDto;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to load project: {ex.Message}");
                return null;
            }
        }
        
        private static DatasetMetadataDTO ConvertToDatasetMetadataDTO(DatasetMetadata meta)
        {
            if (meta == null) return new DatasetMetadataDTO();
            
            return new DatasetMetadataDTO
            {
                SampleName = meta.SampleName,
                LocationName = meta.LocationName,
                Latitude = meta.Latitude,
                Longitude = meta.Longitude,
                Depth = meta.Depth,
                SizeX = meta.Size?.X,
                SizeY = meta.Size?.Y,
                SizeZ = meta.Size?.Z,
                SizeUnit = meta.SizeUnit,
                CollectionDate = meta.CollectionDate,
                Collector = meta.Collector,
                Notes = meta.Notes,
                CustomFields = new Dictionary<string, string>(meta.CustomFields)
            };
        }
        
        private static DatasetMetadata ConvertFromDatasetMetadataDTO(DatasetMetadataDTO dto)
        {
            if (dto == null) return new DatasetMetadata();
            
            var meta = new DatasetMetadata
            {
                SampleName = dto.SampleName,
                LocationName = dto.LocationName,
                Latitude = dto.Latitude,
                Longitude = dto.Longitude,
                Depth = dto.Depth,
                SizeUnit = dto.SizeUnit,
                CollectionDate = dto.CollectionDate,
                Collector = dto.Collector,
                Notes = dto.Notes,
                CustomFields = new Dictionary<string, string>(dto.CustomFields ?? new Dictionary<string, string>())
            };
            
            if (dto.SizeX.HasValue && dto.SizeY.HasValue && dto.SizeZ.HasValue)
            {
                meta.Size = new Vector3(dto.SizeX.Value, dto.SizeY.Value, dto.SizeZ.Value);
            }
            
            return meta;
        }
        
        private static ProjectMetadataDTO ConvertToProjectMetadataDTO(ProjectMetadata meta)
        {
            if (meta == null) return new ProjectMetadataDTO();
            
            return new ProjectMetadataDTO
            {
                Organisation = meta.Organisation,
                Department = meta.Department,
                Year = meta.Year,
                Expedition = meta.Expedition,
                Author = meta.Author,
                ProjectDescription = meta.ProjectDescription,
                StartDate = meta.StartDate,
                EndDate = meta.EndDate,
                FundingSource = meta.FundingSource,
                License = meta.License,
                CustomFields = new Dictionary<string, string>(meta.CustomFields)
            };
        }
        
        private static ProjectMetadata ConvertFromProjectMetadataDTO(ProjectMetadataDTO dto)
        {
            if (dto == null) return new ProjectMetadata();
            
            return new ProjectMetadata
            {
                Organisation = dto.Organisation,
                Department = dto.Department,
                Year = dto.Year,
                Expedition = dto.Expedition,
                Author = dto.Author,
                ProjectDescription = dto.ProjectDescription,
                StartDate = dto.StartDate,
                EndDate = dto.EndDate,
                FundingSource = dto.FundingSource,
                License = dto.License,
                CustomFields = new Dictionary<string, string>(dto.CustomFields ?? new Dictionary<string, string>())
            };
        }
    }

    // Custom converter to handle polymorphism of DatasetDTO
    public class DatasetDTOConverter : JsonConverter<DatasetDTO>
    {
        public override DatasetDTO Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("TypeName", out var typeNameElem))
                {
                    string typeName = typeNameElem.GetString();
                    var rawText = root.GetRawText();

                    return typeName switch
                    {
                        nameof(ImageDataset) => JsonSerializer.Deserialize<ImageDatasetDTO>(rawText, options),
                        nameof(CtImageStackDataset) => JsonSerializer.Deserialize<CtImageStackDatasetDTO>(rawText, options),
                        nameof(DatasetGroup) => JsonSerializer.Deserialize<DatasetGroupDTO>(rawText, options),

                        // --- ADDED CASE FOR THE NEW DATASET TYPE ---
                        nameof(StreamingCtVolumeDataset) => JsonSerializer.Deserialize<StreamingCtVolumeDatasetDTO>(rawText, options),
                        nameof(Mesh3DDataset) => JsonSerializer.Deserialize<Mesh3DDatasetDTO>(rawText, options),
                        nameof(TableDataset) => JsonSerializer.Deserialize<TableDatasetDTO>(rawText, options),
                        

                        _ => throw new JsonException($"Unknown dataset type: {typeName}")
                    };
                }
                throw new JsonException("Missing 'TypeName' property in DatasetDTO.");
            }
        }

        public override void Write(Utf8JsonWriter writer, DatasetDTO value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }
}