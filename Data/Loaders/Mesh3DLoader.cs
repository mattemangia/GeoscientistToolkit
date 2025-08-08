// GeoscientistToolkit/Data/Loaders/Mesh3DLoader.cs
using System;
using System.IO;
using System.Threading.Tasks;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders
{
    public class Mesh3DLoader : IDataLoader
    {
        public string Name => "3D Object (OBJ/STL)";
        public string Description => "Import 3D models in OBJ or STL format";
        
        public string ModelPath { get; set; } = "";
        public float Scale { get; set; } = 1.0f;
        
        public bool CanImport => !string.IsNullOrEmpty(ModelPath) && File.Exists(ModelPath) && IsSupported();
        
        public string ValidationMessage
        {
            get
            {
                if (string.IsNullOrEmpty(ModelPath))
                    return "Please select a 3D model file";
                if (!File.Exists(ModelPath))
                    return "Selected file does not exist";
                if (!IsSupported())
                    return "Unsupported file format. Please select an OBJ or STL file.";
                return null;
            }
        }
        
        private bool IsSupported()
        {
            if (string.IsNullOrEmpty(ModelPath))
                return false;
                
            string extension = Path.GetExtension(ModelPath).ToLower();
            return extension == ".obj" || extension == ".stl";
        }
        
        public ModelInfo GetModelInfo()
        {
            if (!File.Exists(ModelPath))
                return null;
            
            try
            {
                var fileInfo = new FileInfo(ModelPath);
                string extension = fileInfo.Extension.ToLower();
                
                return new ModelInfo
                {
                    FileName = Path.GetFileName(ModelPath),
                    Format = extension.ToUpper().TrimStart('.'),
                    FileSize = fileInfo.Length,
                    IsSupported = IsSupported()
                };
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Mesh3DLoader] Error reading model info: {ex.Message}");
                return null;
            }
        }
        
        public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
        {
            return await Task.Run(() =>
            {
                try
                {
                    progressReporter?.Report((0.1f, "Loading 3D model..."));
                    
                    var fileName = Path.GetFileName(ModelPath);
                    var dataset = new Mesh3DDataset(Path.GetFileNameWithoutExtension(fileName), ModelPath)
                    {
                        Scale = Scale
                    };
                    
                    progressReporter?.Report((0.3f, "Reading model geometry..."));
                    
                    dataset.Load();
                    
                    progressReporter?.Report((1.0f, $"3D model imported successfully! ({dataset.VertexCount} vertices, {dataset.FaceCount} faces)"));
                    
                    return dataset;
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[Mesh3DLoader] Error importing model: {ex}");
                    throw new Exception($"Failed to import 3D model: {ex.Message}", ex);
                }
            });
        }
        
        public void Reset()
        {
            ModelPath = "";
            Scale = 1.0f;
        }
        
        public class ModelInfo
        {
            public string FileName { get; set; }
            public string Format { get; set; }
            public long FileSize { get; set; }
            public bool IsSupported { get; set; }
        }
    }
}