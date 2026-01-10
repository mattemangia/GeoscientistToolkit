// GeoscientistToolkit/Business/GeoScript/GeoScriptSimulationHelpers.cs

using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using GeoscientistToolkit.Analysis.AcousticSimulation;
using GeoscientistToolkit.Analysis.Geomechanics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business.GeoScriptCtImageStackCommands;

public static class GeoScriptSimulationHelpers
{
    public static void ApplyArguments(object target, Dictionary<string, string> args, GeoScriptContext context,
        HashSet<string> reservedKeys)
    {
        var properties = target.GetType()
            .GetProperties()
            .Where(p => p.CanWrite)
            .ToDictionary(p => GeoScriptArgumentParser.NormalizeKey(p.Name), p => p,
                StringComparer.OrdinalIgnoreCase);

        foreach (var (rawKey, rawValue) in args)
        {
            var key = GeoScriptArgumentParser.NormalizeKey(rawKey);
            if (reservedKeys.Contains(key))
                continue;

            if (!properties.TryGetValue(key, out var property))
                continue;

            var value = ConvertValue(property.PropertyType, rawValue, context);
            if (value != null)
                property.SetValue(target, value);
        }
    }

    public static GeoscientistToolkit.Analysis.Geomechanics.BoundingBox BuildGeomechanicsExtent(
        CtImageStackDataset dataset,
        Dictionary<string, string> args,
        GeoScriptContext context)
    {
        var minX = GetExtentValue(args, "extent_min_x", "min_x", 0, context);
        var minY = GetExtentValue(args, "extent_min_y", "min_y", 0, context);
        var minZ = GetExtentValue(args, "extent_min_z", "min_z", 0, context);

        var width = GetExtentValue(args, "extent_width", "width", dataset.Width, context);
        var height = GetExtentValue(args, "extent_height", "height", dataset.Height, context);
        var depth = GetExtentValue(args, "extent_depth", "depth", dataset.Depth, context);

        return new GeoscientistToolkit.Analysis.Geomechanics.BoundingBox(minX, minY, minZ, width, height, depth);
    }

    public static Analysis.AcousticSimulation.BoundingBox BuildAcousticExtent(
        CtImageStackDataset dataset, Dictionary<string, string> args, GeoScriptContext context)
    {
        var minX = GetExtentValue(args, "extent_min_x", "min_x", 0, context);
        var minY = GetExtentValue(args, "extent_min_y", "min_y", 0, context);
        var minZ = GetExtentValue(args, "extent_min_z", "min_z", 0, context);

        var width = GetExtentValue(args, "extent_width", "width", dataset.Width, context);
        var height = GetExtentValue(args, "extent_height", "height", dataset.Height, context);
        var depth = GetExtentValue(args, "extent_depth", "depth", dataset.Depth, context);

        return new Analysis.AcousticSimulation.BoundingBox(minX, minY, minZ, width, height, depth);
    }

    public static async Task<byte[,,]> ExtractLabelsAsync(CtImageStackDataset dataset,
        GeoscientistToolkit.Analysis.Geomechanics.BoundingBox extent)
    {
        return await Task.Run(() =>
        {
            var labels = new byte[extent.Width, extent.Height, extent.Depth];
            Parallel.For(0, extent.Depth, z =>
            {
                for (var y = 0; y < extent.Height; y++)
                for (var x = 0; x < extent.Width; x++)
                    labels[x, y, z] = dataset.LabelData[extent.MinX + x, extent.MinY + y, extent.MinZ + z];
            });

            return labels;
        });
    }

    public static async Task<float[,,]> ExtractGeomechanicsDensityAsync(CtImageStackDataset dataset,
        GeoscientistToolkit.Analysis.Geomechanics.BoundingBox extent)
    {
        return await Task.Run(() =>
        {
            var density = new float[extent.Width, extent.Height, extent.Depth];
            var materialDensity = dataset.Materials.ToDictionary(m => m.ID, m => (float)m.Density * 1000f);

            Parallel.For(0, extent.Depth, z =>
            {
                for (var y = 0; y < extent.Height; y++)
                for (var x = 0; x < extent.Width; x++)
                {
                    var label = dataset.LabelData[extent.MinX + x, extent.MinY + y, extent.MinZ + z];
                    density[x, y, z] = materialDensity.GetValueOrDefault(label, 2700f);
                }
            });

            return density;
        });
    }

    public static async Task<byte[,,]> ExtractAcousticLabelsAsync(CtImageStackDataset dataset,
        Analysis.AcousticSimulation.BoundingBox extent)
    {
        return await Task.Run(() =>
        {
            var labels = new byte[extent.Width, extent.Height, extent.Depth];
            Parallel.For(0, extent.Depth, z =>
            {
                for (var y = 0; y < extent.Height; y++)
                for (var x = 0; x < extent.Width; x++)
                    labels[x, y, z] = dataset.LabelData[extent.Min.X + x, extent.Min.Y + y, extent.Min.Z + z];
            });

            return labels;
        });
    }

    public static async Task<float[,,]> ExtractAcousticDensityAsync(CtImageStackDataset dataset,
        IGrayscaleVolumeData grayscaleVolume, Analysis.AcousticSimulation.BoundingBox extent)
    {
        if (grayscaleVolume == null)
        {
            Logger.LogWarning("[GeoScript Acoustic] Grayscale volume missing. Using material density only.");
            return await ExtractAcousticDensityFromMaterialsAsync(dataset, extent);
        }

        return await Task.Run(() =>
        {
            Logger.Log("[GeoScript Acoustic] Generating heterogeneous density volume from grayscale data...");

            var grayscaleStats = new ConcurrentDictionary<byte, (double sum, long count)>();

            Parallel.For(extent.Min.Z, extent.Max.Z + 1, z =>
            {
                var graySlice = new byte[dataset.Width * dataset.Height];
                var labelSlice = new byte[dataset.Width * dataset.Height];
                grayscaleVolume.ReadSliceZ(z, graySlice);
                dataset.LabelData.ReadSliceZ(z, labelSlice);

                for (var i = 0; i < labelSlice.Length; i++)
                {
                    var label = labelSlice[i];
                    if (label == 0) continue;

                    grayscaleStats.AddOrUpdate(label,
                        (graySlice[i], 1),
                        (_, existing) => (existing.sum + graySlice[i], existing.count + 1));
                }
            });

            var avgGrayscaleMap = grayscaleStats.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.count > 0 ? kvp.Value.sum / kvp.Value.count : 0.0
            );

            var density = new float[extent.Width, extent.Height, extent.Depth];
            var materialDensityMap = dataset.Materials
                .ToDictionary(m => m.ID, m => (m.Density > 0 ? (float)m.Density : 1.0f) * 1000.0f);

            const float backgroundDensity = 1.225f;

            Parallel.For(0, extent.Depth, z_local =>
            {
                var z_global = extent.Min.Z + z_local;
                var graySlice = new byte[dataset.Width * dataset.Height];
                var labelSlice = new byte[dataset.Width * dataset.Height];
                grayscaleVolume.ReadSliceZ(z_global, graySlice);
                dataset.LabelData.ReadSliceZ(z_global, labelSlice);

                for (var y_local = 0; y_local < extent.Height; y_local++)
                {
                    var y_global = extent.Min.Y + y_local;
                    for (var x_local = 0; x_local < extent.Width; x_local++)
                    {
                        var x_global = extent.Min.X + x_local;
                        var sliceIndex = y_global * dataset.Width + x_global;
                        var label = labelSlice[sliceIndex];

                        if (materialDensityMap.TryGetValue(label, out var meanMaterialDensity) &&
                            avgGrayscaleMap.TryGetValue(label, out var avgGrayscale) &&
                            avgGrayscale > 1e-6)
                        {
                            float grayscaleValue = graySlice[sliceIndex];
                            density[x_local, y_local, z_local] =
                                meanMaterialDensity * (float)(grayscaleValue / avgGrayscale);
                        }
                        else if (materialDensityMap.ContainsKey(label))
                        {
                            density[x_local, y_local, z_local] = materialDensityMap[label];
                        }
                        else
                        {
                            density[x_local, y_local, z_local] = backgroundDensity;
                        }

                        density[x_local, y_local, z_local] = Math.Max(1.0f, density[x_local, y_local, z_local]);
                    }
                }
            });

            Logger.Log("[GeoScript Acoustic] Generated heterogeneous density map.");
            return density;
        });
    }

    public static async Task<(float[,,] youngsModulus, float[,,] poissonRatio)> ExtractAcousticMaterialPropertiesAsync(
        CtImageStackDataset dataset, Analysis.AcousticSimulation.BoundingBox extent, float defaultYoungsModulus,
        float defaultPoissonRatio)
    {
        return await Task.Run(() =>
        {
            var youngsModulus = new float[extent.Width, extent.Height, extent.Depth];
            var poissonRatio = new float[extent.Width, extent.Height, extent.Depth];
            var materialProps = new Dictionary<byte, (float E, float Nu)>();

            foreach (var material in dataset.Materials)
            {
                var eValue = defaultYoungsModulus;
                var nuValue = defaultPoissonRatio;

                if (!string.IsNullOrEmpty(material.PhysicalMaterialName))
                {
                    var physMat = MaterialLibrary.Instance.Find(material.PhysicalMaterialName);
                    if (physMat != null)
                    {
                        eValue = (float)(physMat.YoungModulus_GPa ?? defaultYoungsModulus / 1000.0) * 1000f;
                        nuValue = (float)(physMat.PoissonRatio ?? defaultPoissonRatio);
                    }
                }

                materialProps[material.ID] = (eValue, nuValue);
            }

            const float backgroundE = 1.0f;
            const float backgroundNu = 0.3f;

            Parallel.For(0, extent.Depth, z_local =>
            {
                var z_global = extent.Min.Z + z_local;
                for (var y_local = 0; y_local < extent.Height; y_local++)
                {
                    var y_global = extent.Min.Y + y_local;
                    for (var x_local = 0; x_local < extent.Width; x_local++)
                    {
                        var x_global = extent.Min.X + x_local;
                        var label = dataset.LabelData[x_global, y_global, z_global];

                        if (materialProps.TryGetValue(label, out var props))
                        {
                            youngsModulus[x_local, y_local, z_local] = props.E;
                            poissonRatio[x_local, y_local, z_local] = props.Nu;
                        }
                        else
                        {
                            youngsModulus[x_local, y_local, z_local] = backgroundE;
                            poissonRatio[x_local, y_local, z_local] = backgroundNu;
                        }
                    }
                }
            });

            Logger.Log("[GeoScript Acoustic] Generated material property volumes.");
            return (youngsModulus, poissonRatio);
        });
    }

    private static async Task<float[,,]> ExtractAcousticDensityFromMaterialsAsync(CtImageStackDataset dataset,
        Analysis.AcousticSimulation.BoundingBox extent)
    {
        return await Task.Run(() =>
        {
            var density = new float[extent.Width, extent.Height, extent.Depth];
            var materialDensityMap = dataset.Materials
                .ToDictionary(m => m.ID, m => (m.Density > 0 ? (float)m.Density : 1.0f) * 1000.0f);

            const float backgroundDensity = 1.225f;

            Parallel.For(0, extent.Depth, z =>
            {
                for (var y = 0; y < extent.Height; y++)
                for (var x = 0; x < extent.Width; x++)
                {
                    var label = dataset.LabelData[extent.Min.X + x, extent.Min.Y + y, extent.Min.Z + z];
                    density[x, y, z] = materialDensityMap.GetValueOrDefault(label, backgroundDensity);
                }
            });

            return density;
        });
    }

    private static object ConvertValue(Type targetType, string token, GeoScriptContext context)
    {
        if (targetType == typeof(string))
            return GeoScriptArgumentParser.GetString(new Dictionary<string, string> { { "value", token } }, "value",
                token, context);

        if (targetType == typeof(bool))
            return GeoScriptArgumentParser.GetBool(new Dictionary<string, string> { { "value", token } }, "value",
                false, context);

        if (targetType == typeof(int))
            return GeoScriptArgumentParser.GetInt(new Dictionary<string, string> { { "value", token } }, "value", 0,
                context);

        if (targetType == typeof(float))
            return GeoScriptArgumentParser.GetFloat(new Dictionary<string, string> { { "value", token } }, "value", 0,
                context);

        if (targetType == typeof(double))
            return GeoScriptArgumentParser.GetDouble(new Dictionary<string, string> { { "value", token } }, "value", 0,
                context);

        if (targetType == typeof(Vector3))
            return GeoScriptArgumentParser.GetVector3(new Dictionary<string, string> { { "value", token } }, "value",
                Vector3.Zero, context);

        if (targetType == typeof(HashSet<byte>))
            return GeoScriptArgumentParser.GetByteSet(new Dictionary<string, string> { { "value", token } }, "value",
                context);

        if (targetType.IsEnum)
        {
            var value = GeoScriptArgumentParser.GetString(new Dictionary<string, string> { { "value", token } },
                "value", null, context);
            if (value == null)
                return null;
            return Enum.Parse(targetType, value, true);
        }

        return null;
    }

    private static int GetExtentValue(Dictionary<string, string> args, string primaryKey, string fallbackKey,
        int defaultValue, GeoScriptContext context)
    {
        if (GeoScriptArgumentParser.TryGetString(args, primaryKey, out var value) ||
            GeoScriptArgumentParser.TryGetString(args, fallbackKey, out value))
        {
            return GeoScriptArgumentParser.GetInt(new Dictionary<string, string> { { "value", value } }, "value",
                defaultValue, context);
        }

        return defaultValue;
    }
}
