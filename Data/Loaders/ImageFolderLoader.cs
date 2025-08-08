// GeoscientistToolkit/Data/Loaders/ImageFolderLoader.cs
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders
{
    public class ImageFolderLoader : IDataLoader
    {
        public string Name => "Image Folder (Group)";
        public string Description => "Load multiple images from a folder and organize them into groups";
        
        public string FolderPath { get; set; } = "";
        public bool RequiresOrganizer { get; } = true;
        
        public bool CanImport => !string.IsNullOrEmpty(FolderPath) && Directory.Exists(FolderPath);
        
        public string ValidationMessage
        {
            get
            {
                if (string.IsNullOrEmpty(FolderPath))
                    return "Please select a folder containing images";
                if (!Directory.Exists(FolderPath))
                    return "Selected folder does not exist";
                    
                var info = GetFolderInfo();
                if (info.ImageCount == 0)
                    return "No supported image files found in this folder";
                    
                return null;
            }
        }
        
        public FolderInfo GetFolderInfo()
        {
            if (!Directory.Exists(FolderPath))
                return new FolderInfo { ImageCount = 0, TotalSize = 0 };
            
            try
            {
                var files = Directory.GetFiles(FolderPath)
                    .Where(ImageLoader.IsSupportedImageFile)
                    .ToArray();
                    
                return new FolderInfo
                {
                    ImageCount = files.Length,
                    TotalSize = files.Sum(f => new FileInfo(f).Length),
                    FolderName = Path.GetFileName(FolderPath)
                };
            }
            catch
            {
                return new FolderInfo { ImageCount = 0, TotalSize = 0 };
            }
        }
        
        public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
        {
            // This loader requires the ImageStackOrganizerDialog to be opened
            // The actual loading is handled by the organizer
            throw new NotImplementedException("ImageFolderLoader requires the ImageStackOrganizerDialog");
        }
        
        public void Reset()
        {
            FolderPath = "";
        }
        
        public class FolderInfo
        {
            public int ImageCount { get; set; }
            public long TotalSize { get; set; }
            public string FolderName { get; set; }
        }
    }
}