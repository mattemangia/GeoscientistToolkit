// GeoscientistToolkit/Data/Loaders/GISLoader.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders
{
    public class GISLoader : IDataLoader
    {
        public string FilePath { get; set; }
        public GISFileType FileType { get; set; } = GISFileType.AutoDetect;
        public bool CreateEmpty { get; set; } = false;
        public string DatasetName { get; set; }
        
        public string Name => "GIS Map Loader";
        public string Description => "Load GIS data from shapefiles, GeoJSON, KML, or GeoTIFF files, or create an empty map";
        public bool CanImport => CreateEmpty || (!string.IsNullOrEmpty(FilePath) && File.Exists(FilePath));
        public string ValidationMessage { get; private set; }
        
        public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progress)
        {
            progress?.Report((0, "Starting GIS import..."));
            
            if (CreateEmpty)
            {
                progress?.Report((0.5f, "Creating empty GIS dataset..."));
                
                var dataset = new GISDataset(DatasetName ?? "New Map", "");
                
                progress?.Report((1.0f, "Empty GIS dataset created"));
                return dataset;
            }
            
            if (!File.Exists(FilePath))
            {
                throw new FileNotFoundException($"GIS file not found: {FilePath}");
            }
            
            string name = string.IsNullOrEmpty(DatasetName) 
                ? Path.GetFileNameWithoutExtension(FilePath) 
                : DatasetName;
            
            var gisDataset = new GISDataset(name, FilePath);
            
            progress?.Report((0.3f, "Loading GIS data..."));
            
            await Task.Run(() =>
            {
                try
                {
                    gisDataset.Load();
                    progress?.Report((0.9f, "Finalizing..."));
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to load GIS file: {ex.Message}");
                    throw;
                }
            });
            
            progress?.Report((1.0f, "GIS dataset loaded successfully"));
            return gisDataset;
        }
        
        public void Reset()
        {
            FilePath = null;
            FileType = GISFileType.AutoDetect;
            CreateEmpty = false;
            DatasetName = null;
        }
        
        public GISFileInfo GetFileInfo()
        {
            var info = new GISFileInfo();
            
            if (CreateEmpty)
            {
                info.IsValid = true;
                info.Type = "Empty";
                return info;
            }
            
            if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
                return info;
            
            var fileInfo = new FileInfo(FilePath);
            info.FileName = fileInfo.Name;
            info.FileSize = fileInfo.Length;
            
            string ext = fileInfo.Extension.ToLower();
            info.Type = ext switch
            {
                ".shp" => "Shapefile",
                ".geojson" or ".json" => "GeoJSON",
                ".kml" => "KML",
                ".kmz" => "KMZ (Compressed KML)",
                ".tif" or ".tiff" => "GeoTIFF",
                _ => "Unknown"
            };
            
            info.IsValid = info.Type != "Unknown";
            
            // Check for shapefile components
            if (ext == ".shp")
            {
                string basePath = Path.GetFileNameWithoutExtension(FilePath);
                string dir = Path.GetDirectoryName(FilePath);
                
                info.HasShx = File.Exists(Path.Combine(dir, basePath + ".shx"));
                info.HasDbf = File.Exists(Path.Combine(dir, basePath + ".dbf"));
                info.HasPrj = File.Exists(Path.Combine(dir, basePath + ".prj"));
                
                if (!info.HasShx || !info.HasDbf)
                {
                    info.IsValid = false;
                    ValidationMessage = "Missing required shapefile components (.shx, .dbf)";
                }
                else
                {
                    ValidationMessage = null;
                }
            }
            
            return info;
        }
        
        public enum GISFileType
        {
            AutoDetect,
            Shapefile,
            GeoJSON,
            KML,
            KMZ,
            GeoTIFF
        }
        
        public class GISFileInfo
        {
            public string FileName { get; set; }
            public long FileSize { get; set; }
            public string Type { get; set; }
            public bool IsValid { get; set; }
            
            // Shapefile specific
            public bool HasShx { get; set; }
            public bool HasDbf { get; set; }
            public bool HasPrj { get; set; }
        }
    }
}