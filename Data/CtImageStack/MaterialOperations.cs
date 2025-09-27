// GeoscientistToolkit/Data/CtImageStack/MaterialOperations.cs
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GeoscientistToolkit.Data.CtImageStack
{
    /// <summary>
    /// Provides high-performance, parallelized operations for material and voxel management in 3D volumes.
    /// </summary>
    public static class MaterialOperations
    {
        private static readonly int _optimalThreadCount = Math.Max(1, Environment.ProcessorCount - 1);

        /// <summary>
        /// Gets the next available material ID that is not currently in use.
        /// </summary>
        public static byte GetNextMaterialID(List<Material> materials)
        {
            if (materials == null) return 1;

            for (byte candidate = 1; candidate < byte.MaxValue; candidate++)
            {
                if (!materials.Any(m => m.ID == candidate))
                {
                    return candidate;
                }
            }
            throw new InvalidOperationException("No available material IDs remaining.");
        }

        /// <summary>
        /// Labels every voxel whose grayscale value is within the specified threshold with the given material ID.
        /// </summary>
        public static Task AddVoxelsByThresholdAsync(IGrayscaleVolumeData grayscaleVolume, ILabelVolumeData labelVolume, 
            byte materialID, byte minVal, byte maxVal, CtImageStackDataset dataset = null)
        {
            Logger.Log($"[MaterialOperations] Adding voxels to material {materialID} (Threshold: {minVal}-{maxVal})");
            return ProcessVolumeByThresholdAsync(grayscaleVolume, labelVolume, materialID, minVal, maxVal, true, dataset);
        }

        /// <summary>
        /// Clears voxels that belong to a specific material and are within a grayscale threshold.
        /// </summary>
        public static Task RemoveVoxelsByThresholdAsync(IGrayscaleVolumeData grayscaleVolume, ILabelVolumeData labelVolume, 
            byte materialID, byte minVal, byte maxVal, CtImageStackDataset dataset = null)
        {
            Logger.Log($"[MaterialOperations] Removing voxels from material {materialID} (Threshold: {minVal}-{maxVal})");
            return ProcessVolumeByThresholdAsync(grayscaleVolume, labelVolume, materialID, minVal, maxVal, false, dataset);
        }

        /// <summary>
        /// Core processing logic that operates on the volume slice by slice in parallel.
        /// </summary>
        private static Task ProcessVolumeByThresholdAsync(IGrayscaleVolumeData grayscaleVolume, ILabelVolumeData labelVolume, 
            byte materialID, byte minVal, byte maxVal, bool isAddOperation, CtImageStackDataset dataset)
        {
            if (grayscaleVolume == null || labelVolume == null)
            {
                Logger.LogWarning("[MaterialOperations] Grayscale or Label volume is null. Aborting operation.");
                return Task.CompletedTask;
            }

            int width = grayscaleVolume.Width;
            int height = grayscaleVolume.Height;
            int depth = grayscaleVolume.Depth;

            return Task.Run(() =>
            {
                bool anyModified = false;
                
                Parallel.For(0, depth, new ParallelOptions { MaxDegreeOfParallelism = _optimalThreadCount }, z =>
                {
                    var graySlice = new byte[width * height];
                    var labelSlice = new byte[width * height];
                    
                    grayscaleVolume.ReadSliceZ(z, graySlice);
                    labelVolume.ReadSliceZ(z, labelSlice);

                    bool modified = false;

                    if (isAddOperation)
                    {
                        for (int i = 0; i < graySlice.Length; i++)
                        {
                            byte gray = graySlice[i];
                            if (gray >= minVal && gray <= maxVal)
                            {
                                if (labelSlice[i] != materialID)
                                {
                                    labelSlice[i] = materialID;
                                    modified = true;
                                }
                            }
                        }
                    }
                    else // Remove operation
                    {
                        for (int i = 0; i < graySlice.Length; i++)
                        {
                            byte gray = graySlice[i];
                            if (labelSlice[i] == materialID && gray >= minVal && gray <= maxVal)
                            {
                                labelSlice[i] = 0; // Set to exterior
                                modified = true;
                            }
                        }
                    }

                    if (modified)
                    {
                        labelVolume.WriteSliceZ(z, labelSlice);
                        anyModified = true;
                    }
                });
                
                // FIXED: Auto-save label data after modification
                if (anyModified && dataset != null)
                {
                    Logger.Log($"[MaterialOperations] Saving label data for material {materialID}...");
                    dataset.SaveLabelData(); // Save the label volume
                    dataset.SaveMaterials(); // Save material definitions
                    
                    ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
                    ProjectManager.Instance.HasUnsavedChanges = true;
                }
                
                Logger.Log($"[MaterialOperations] Finished processing for material {materialID}.");
            });
        }

        /// <summary>
        /// Applies material assignment from interactive segmentation with auto-save
        /// </summary>
        public static async Task ApplySegmentationMaskAsync(ILabelVolumeData labelVolume, byte[] mask, 
            byte materialID, int sliceIndex, int viewType, int width, int height, int depth, 
            CtImageStackDataset dataset = null)
        {
            await Task.Run(() =>
            {
                byte[] currentSlice = new byte[width * height];
                
                switch (viewType)
                {
                    case 0: // XY view
                        labelVolume.ReadSliceZ(sliceIndex, currentSlice);
                        for (int i = 0; i < mask.Length && i < currentSlice.Length; i++)
                        {
                            if (mask[i] > 0) currentSlice[i] = materialID;
                        }
                        labelVolume.WriteSliceZ(sliceIndex, currentSlice);
                        break;
                        
                    case 1: // XZ view
                        for (int z = 0; z < depth; z++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                int maskIdx = z * width + x;
                                if (maskIdx < mask.Length && mask[maskIdx] > 0)
                                {
                                    labelVolume[x, sliceIndex, z] = materialID;
                                }
                            }
                        }
                        break;
                        
                    case 2: // YZ view
                        for (int z = 0; z < depth; z++)
                        {
                            for (int y = 0; y < height; y++)
                            {
                                int maskIdx = z * height + y;
                                if (maskIdx < mask.Length && mask[maskIdx] > 0)
                                {
                                    labelVolume[sliceIndex, y, z] = materialID;
                                }
                            }
                        }
                        break;
                }
                
                // FIXED: Auto-save after interactive segmentation
                if (dataset != null)
                {
                    Logger.Log($"[MaterialOperations] Auto-saving segmentation for material {materialID}...");
                    dataset.SaveLabelData();
                    dataset.SaveMaterials();
                    ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
                }
            });
        }
    }
}