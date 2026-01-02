using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GeoscientistToolkit.Data;

namespace GeoscientistToolkit.Data.Loaders
{
    public static class DataLoaderFactory
    {
        private static readonly Dictionary<string, Type> _loadersByExtension;
        private static readonly Dictionary<DatasetType, List<Type>> _loadersByDatasetType;

        static DataLoaderFactory()
        {
            _loadersByExtension = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            _loadersByDatasetType = new Dictionary<DatasetType, List<Type>>();

            // Register standard loaders
            RegisterLoader<SingleImageLoader>(DatasetType.SingleImage, ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff");
            RegisterLoader<CtStackFileLoader>(DatasetType.CtImageStack, ".ctstack", ".tif", ".tiff"); // Note .tif ambiguity
            RegisterLoader<TableLoader>(DatasetType.Table, ".csv", ".tsv", ".txt", ".xlsx", ".xls");
            RegisterLoader<GISLoader>(DatasetType.GIS, ".shp", ".geojson", ".kml", ".gpkg");
            RegisterLoader<LASLoader>(DatasetType.Borehole, ".las");
            RegisterLoader<BoreholeBinaryLoader>(DatasetType.Borehole, ".bhb");
            RegisterLoader<SeismicLoader>(DatasetType.Seismic, ".sgy", ".segy");
            RegisterLoader<Mesh3DLoader>(DatasetType.Mesh3D, ".obj", ".stl", ".ply");
            RegisterLoader<PNMLoader>(DatasetType.PNM, ".pnm");
            RegisterLoader<VideoLoader>(DatasetType.Video, ".mp4", ".avi", ".mov");
            RegisterLoader<AudioLoader>(DatasetType.Audio, ".wav", ".mp3", ".ogg");
            RegisterLoader<TextLoader>(DatasetType.Text, ".txt", ".md", ".json", ".xml", ".log");
            RegisterLoader<AcousticVolumeLoader>(DatasetType.AcousticVolume, ".wav", ".raw"); // AudioLoader also handles wav, ambiguity
            RegisterLoader<PhysicoChemLoader>(DatasetType.MicroXrf, ".physicochem"); // Assume specific type
            RegisterLoader<SubsurfaceGISLoader>(DatasetType.SubsurfaceGIS, ".subsurface");
            RegisterLoader<Tough2Loader>(DatasetType.Mesh3D, ".dat");
            RegisterLoader<TwoDGeologyLoader>(DatasetType.TwoDGeology, ".2dgeo");
            RegisterLoader<DualPNMLoader>(DatasetType.DualPNM, ".dualpnm");
            RegisterLoader<DicomLoader>(DatasetType.CtImageStack, ".dcm");

            // Note: Ambiguities like .tif or .wav are handled by GetLoaderForFile which can take a preferred type
        }

        private static void RegisterLoader<T>(DatasetType type, params string[] extensions) where T : IDataLoader
        {
            var loaderType = typeof(T);

            if (!_loadersByDatasetType.ContainsKey(type))
            {
                _loadersByDatasetType[type] = new List<Type>();
            }
            _loadersByDatasetType[type].Add(loaderType);

            foreach (var ext in extensions)
            {
                // Last registered loader for extension wins as default
                _loadersByExtension[ext] = loaderType;
            }
        }

        public static IDataLoader GetLoaderForFile(string filePath, DatasetType? preferredType = null)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            // If preferred type is specified, try to find a loader for that type
            if (preferredType.HasValue)
            {
                return GetLoaderForType(preferredType.Value, filePath);
            }

            // Default logic based on extension
            if (_loadersByExtension.TryGetValue(ext, out var type))
            {
                // Special handling for ambiguity
                if (ext.Equals(".tif", StringComparison.OrdinalIgnoreCase) || ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase))
                {
                    // Default to SingleImage for TIF if no type specified, as it's safer/more common for single files
                    // But user can override with preferredType=CtImageStack
                    return new SingleImageLoader(filePath);
                }

                return (IDataLoader)Activator.CreateInstance(type, filePath);
            }

            return null;
        }

        public static IDataLoader GetLoaderForType(DatasetType type, string filePath)
        {
            if (_loadersByDatasetType.TryGetValue(type, out var loaderTypes))
            {
                // Iterate through available loaders for this type and pick the best one
                // based on extension support or fallback to the first one.
                var ext = Path.GetExtension(filePath).ToLowerInvariant();

                // 1. Try to find a loader that explicitly claimed this extension
                if (_loadersByExtension.TryGetValue(ext, out var bestMatch) && loaderTypes.Contains(bestMatch))
                {
                    return (IDataLoader)Activator.CreateInstance(bestMatch, filePath);
                }

                // 2. Fallback: For CtImageStack, favor CtStackFileLoader for TIFs if available
                if (type == DatasetType.CtImageStack && (ext == ".tif" || ext == ".tiff"))
                {
                    var ctLoader = loaderTypes.FirstOrDefault(t => t == typeof(CtStackFileLoader));
                    if (ctLoader != null) return (IDataLoader)Activator.CreateInstance(ctLoader, filePath);
                }

                // 3. Fallback: Return the first registered loader for this type
                if (loaderTypes.Count > 0)
                {
                    return (IDataLoader)Activator.CreateInstance(loaderTypes[0], filePath);
                }
            }
            return null;
        }
    }
}
