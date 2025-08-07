// GeoscientistToolkit/Business/ProjectSerializer.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Util;

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
                ProjectName = project.ProjectName
            };

            foreach (var dataset in project.LoadedDatasets)
            {
                if (dataset is ISerializableDataset serializable)
                {
                    projectDto.Datasets.Add((DatasetDTO)serializable.ToSerializableObject());
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
                Logger.Log($"Project loaded successfully from {path}");
                return projectDto;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to load project: {ex.Message}");
                return null;
            }
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