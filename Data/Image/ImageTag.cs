// GeoscientistToolkit/Data/Image/ImageTag.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace GeoscientistToolkit.Data.Image
{
    /// <summary>
    /// Defines the type/category of an image for specialized processing
    /// </summary>
    [Flags]
    public enum ImageTag
    {
        None = 0,
        
        // Microscopy types
        SEM = 1 << 0,           // Scanning Electron Microscope
        TEM = 1 << 1,           // Transmission Electron Microscope
        OpticalMicroscopy = 1 << 2,
        Fluorescence = 1 << 3,
        Confocal = 1 << 4,
        
        // Medical/Scientific imaging
        CTSlice = 1 << 5,       // CT scan slice
        MRI = 1 << 6,           // MRI scan
        XRay = 1 << 7,
        
        // Remote sensing
        Drone = 1 << 8,         // Drone/UAV imagery
        Satellite = 1 << 9,
        Aerial = 1 << 10,
        
        // Geographic
        Map = 1 << 11,
        PanoramaTile = 1 << 12,
        Orthophoto = 1 << 13,
        
        // Geological
        ThinSection = 1 << 14,  // Petrographic thin section
        CorePhoto = 1 << 15,    // Drill core photography
        OutcropPhoto = 1 << 16,
        
        // Analysis types
        Spectrogram = 1 << 17,
        Diffraction = 1 << 18,
        
        // Generic
        Photo = 1 << 19,
        Diagram = 1 << 20,
        Chart = 1 << 21,
        
        // Properties (can be combined with types)
        Calibrated = 1 << 22,   // Has known scale
        Georeferenced = 1 << 23,
        TimeSeries = 1 << 24,
        Multispectral = 1 << 25
    }

    public static class ImageTagExtensions
    {
        private static readonly Dictionary<ImageTag, string> _displayNames = new Dictionary<ImageTag, string>
        {
            { ImageTag.SEM, "SEM (Scanning Electron Microscope)" },
            { ImageTag.TEM, "TEM (Transmission Electron Microscope)" },
            { ImageTag.OpticalMicroscopy, "Optical Microscopy" },
            { ImageTag.Fluorescence, "Fluorescence Microscopy" },
            { ImageTag.Confocal, "Confocal Microscopy" },
            { ImageTag.CTSlice, "CT Slice" },
            { ImageTag.MRI, "MRI Scan" },
            { ImageTag.XRay, "X-Ray" },
            { ImageTag.Drone, "Drone/UAV Image" },
            { ImageTag.Satellite, "Satellite Image" },
            { ImageTag.Aerial, "Aerial Photo" },
            { ImageTag.Map, "Map" },
            { ImageTag.PanoramaTile, "Panorama Tile" },
            { ImageTag.Orthophoto, "Orthophoto" },
            { ImageTag.ThinSection, "Thin Section" },
            { ImageTag.CorePhoto, "Core Photography" },
            { ImageTag.OutcropPhoto, "Outcrop Photo" },
            { ImageTag.Spectrogram, "Spectrogram" },
            { ImageTag.Diffraction, "Diffraction Pattern" },
            { ImageTag.Photo, "Photograph" },
            { ImageTag.Diagram, "Diagram" },
            { ImageTag.Chart, "Chart" },
            { ImageTag.Calibrated, "Calibrated" },
            { ImageTag.Georeferenced, "Georeferenced" },
            { ImageTag.TimeSeries, "Time Series" },
            { ImageTag.Multispectral, "Multispectral" }
        };

        private static readonly Dictionary<ImageTag, string[]> _availableTools = new Dictionary<ImageTag, string[]>
        {
            { ImageTag.SEM, new[] { "Measurement", "Particle Analysis", "EDX Analysis", "Grain Size", "Porosity" } },
            { ImageTag.TEM, new[] { "Measurement", "Crystal Analysis", "FFT", "SAED Pattern" } },
            { ImageTag.OpticalMicroscopy, new[] { "Measurement", "Point Counting", "Grain Size", "Color Analysis" } },
            { ImageTag.ThinSection, new[] { "Point Counting", "Mineral ID", "Porosity", "Grain Contacts" } },
            { ImageTag.Drone, new[] { "Orthorectification", "3D Reconstruction", "NDVI", "Classification" } },
            { ImageTag.Map, new[] { "Georeferencing", "Digitization", "Projection", "Scale Bar" } },
            { ImageTag.CTSlice, new[] { "Density Analysis", "3D Reconstruction", "Segmentation" } }
        };

        public static string GetDisplayName(this ImageTag tag)
        {
            return _displayNames.TryGetValue(tag, out var name) ? name : tag.ToString();
        }

        public static string[] GetAvailableTools(this ImageTag tag)
        {
            var tools = new HashSet<string> { "Basic Adjustments", "Export" }; // Always available
            
            foreach (var flag in GetFlags(tag))
            {
                if (_availableTools.TryGetValue(flag, out var specificTools))
                {
                    foreach (var tool in specificTools)
                        tools.Add(tool);
                }
            }
            
            return tools.ToArray();
        }

        public static IEnumerable<ImageTag> GetFlags(this ImageTag tags)
        {
            foreach (ImageTag value in Enum.GetValues(typeof(ImageTag)))
            {
                if (value != ImageTag.None && tags.HasFlag(value))
                    yield return value;
            }
        }

        public static bool RequiresCalibration(this ImageTag tag)
        {
            return tag.HasFlag(ImageTag.SEM) || 
                   tag.HasFlag(ImageTag.TEM) || 
                   tag.HasFlag(ImageTag.OpticalMicroscopy) ||
                   tag.HasFlag(ImageTag.ThinSection);
        }
    }
}