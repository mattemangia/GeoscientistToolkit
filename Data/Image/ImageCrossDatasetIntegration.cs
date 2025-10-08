// GeoscientistToolkit/Data/Image/ImageCrossDatasetIntegration.cs (Complete Implementation)

using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Image;

/// <summary>
///     Provides integration between tagged images and other dataset types
/// </summary>
public static class ImageCrossDatasetIntegration
{
    // Common EPSG codes and their WKT representations
    private static readonly Dictionary<string, string> CommonProjections = new()
    {
        ["EPSG:4326"] =
            @"GEOGCS[""WGS 84"",DATUM[""WGS_1984"",SPHEROID[""WGS 84"",6378137,298.257223563,AUTHORITY[""EPSG"",""7030""]],AUTHORITY[""EPSG"",""6326""]],PRIMEM[""Greenwich"",0,AUTHORITY[""EPSG"",""8901""]],UNIT[""degree"",0.0174532925199433,AUTHORITY[""EPSG"",""9122""]],AUTHORITY[""EPSG"",""4326""]]",
        ["EPSG:3857"] =
            @"PROJCS[""WGS 84 / Pseudo-Mercator"",GEOGCS[""WGS 84"",DATUM[""WGS_1984"",SPHEROID[""WGS 84"",6378137,298.257223563,AUTHORITY[""EPSG"",""7030""]],AUTHORITY[""EPSG"",""6326""]],PRIMEM[""Greenwich"",0,AUTHORITY[""EPSG"",""8901""]],UNIT[""degree"",0.0174532925199433,AUTHORITY[""EPSG"",""9122""]],AUTHORITY[""EPSG"",""4326""]],PROJECTION[""Mercator_1SP""],PARAMETER[""central_meridian"",0],PARAMETER[""scale_factor"",1],PARAMETER[""false_easting"",0],PARAMETER[""false_northing"",0],UNIT[""metre"",1,AUTHORITY[""EPSG"",""9001""]],AXIS[""X"",EAST],AXIS[""Y"",NORTH],EXTENSION[""PROJ4"",""+proj=merc +a=6378137 +b=6378137 +lat_ts=0.0 +lon_0=0.0 +x_0=0.0 +y_0=0 +k=1.0 +units=m +nadgrids=@null +wktext +no_defs""],AUTHORITY[""EPSG"",""3857""]]",
        ["EPSG:32633"] =
            @"PROJCS[""WGS 84 / UTM zone 33N"",GEOGCS[""WGS 84"",DATUM[""WGS_1984"",SPHEROID[""WGS 84"",6378137,298.257223563,AUTHORITY[""EPSG"",""7030""]],AUTHORITY[""EPSG"",""6326""]],PRIMEM[""Greenwich"",0,AUTHORITY[""EPSG"",""8901""]],UNIT[""degree"",0.0174532925199433,AUTHORITY[""EPSG"",""9122""]],AUTHORITY[""EPSG"",""4326""]],PROJECTION[""Transverse_Mercator""],PARAMETER[""latitude_of_origin"",0],PARAMETER[""central_meridian"",15],PARAMETER[""scale_factor"",0.9996],PARAMETER[""false_easting"",500000],PARAMETER[""false_northing"",0],UNIT[""metre"",1,AUTHORITY[""EPSG"",""9001""]],AXIS[""Easting"",EAST],AXIS[""Northing"",NORTH],AUTHORITY[""EPSG"",""32633""]]"
    };

    /// <summary>
    ///     Link an SEM image to a specific location in a CT volume
    /// </summary>
    public static void LinkSEMtoCTSlice(ImageDataset semImage, CtImageStackDataset ctDataset, int sliceIndex,
        Vector2 position)
    {
        if (!semImage.HasTag(ImageTag.SEM))
        {
            Logger.LogWarning("Image is not tagged as SEM");
            return;
        }

        // Store the link in metadata
        semImage.ImageMetadata["LinkedCTDataset"] = ctDataset.Name;
        semImage.ImageMetadata["LinkedCTSlice"] = sliceIndex.ToString();
        semImage.ImageMetadata["LinkedCTPosition"] = $"{position.X},{position.Y}";

        // Store reverse link in CT dataset
        ctDataset.Metadata[$"LinkedSEM_{sliceIndex}_{position.X}_{position.Y}"] = semImage.Name;

        Logger.Log(
            $"Linked SEM image '{semImage.Name}' to CT slice {sliceIndex} at position ({position.X}, {position.Y})");
    }

    /// <summary>
    ///     Georeference a drone/satellite image using GIS data
    /// </summary>
    public static void GeoreferenceImage(ImageDataset image, GISDataset gisDataset, List<GroundControlPoint> gcps)
    {
        if (!image.HasTag(ImageTag.Drone) && !image.HasTag(ImageTag.Satellite) && !image.HasTag(ImageTag.Aerial))
        {
            Logger.LogWarning("Image is not tagged as aerial/drone/satellite");
            return;
        }

        // Calculate transformation matrix from GCPs
        var transform = CalculateGeotransform(gcps);

        // Store georeferencing data
        image.ImageMetadata["GeotransformMatrix"] = SerializeMatrix(transform);

        // Store projection information from GIS dataset
        if (gisDataset.Projection != null)
        {
            image.ImageMetadata["ProjectionEPSG"] = gisDataset.Projection.EPSG;
            image.ImageMetadata["ProjectionName"] = gisDataset.Projection.Name;
            image.ImageMetadata["ProjectionType"] = gisDataset.Projection.Type.ToString();

            // Store WKT if we need to generate it
            var wkt = GenerateProjectionWKT(gisDataset.Projection);
            if (!string.IsNullOrEmpty(wkt)) image.ImageMetadata["ProjectionWKT"] = wkt;
        }

        image.ImageMetadata["GCPCount"] = gcps.Count.ToString();

        // Store individual GCPs for reference
        for (var i = 0; i < gcps.Count; i++)
        {
            var gcp = gcps[i];
            image.ImageMetadata[$"GCP_{i}_Image"] = $"{gcp.ImageCoordinates.X},{gcp.ImageCoordinates.Y}";
            image.ImageMetadata[$"GCP_{i}_World"] =
                $"{gcp.WorldCoordinates.X},{gcp.WorldCoordinates.Y},{gcp.WorldCoordinates.Z}";
            image.ImageMetadata[$"GCP_{i}_Error"] = gcp.ResidualError.ToString("F3");
        }

        // Add georeferenced tag
        image.AddTag(ImageTag.Georeferenced);

        Logger.Log($"Georeferenced image '{image.Name}' using {gcps.Count} ground control points");
    }

    /// <summary>
    ///     Generate WKT string from GIS projection
    /// </summary>
    private static string GenerateProjectionWKT(GISProjection projection)
    {
        // Check if we have a predefined WKT for this EPSG code
        if (CommonProjections.ContainsKey(projection.EPSG)) return CommonProjections[projection.EPSG];

        // Generate WKT based on projection type
        switch (projection.Type)
        {
            case ProjectionType.Geographic:
                return GenerateGeographicWKT(projection);

            /*case ProjectionType.UTM:
                return GenerateUTMWKT(projection);

            case ProjectionType.Mercator:
                return GenerateMercatorWKT(projection);

            case ProjectionType.LambertConformalConic:
                return GenerateLambertWKT(projection);

            case ProjectionType.AlbersEqualArea:
                return GenerateAlbersWKT(projection);*/

            default:
                // Generic fallback
                return GenerateGenericWKT(projection);
        }
    }

    private static string GenerateGeographicWKT(GISProjection projection)
    {
        var wkt = new StringBuilder();
        wkt.Append($"GEOGCS[\"{projection.Name}\",");
        wkt.Append($"DATUM[\"{projection.Name}_Datum\",");
        wkt.Append($"SPHEROID[\"{projection.Name}_Spheroid\",6378137,298.257223563]],");
        wkt.Append("PRIMEM[\"Greenwich\",0],");
        wkt.Append("UNIT[\"degree\",0.0174532925199433]");

        if (!string.IsNullOrEmpty(projection.EPSG))
        {
            var parts = projection.EPSG.Split(':');
            if (parts.Length == 2) wkt.Append($",AUTHORITY[\"{parts[0]}\",\"{parts[1]}\"]");
        }

        wkt.Append("]");

        return wkt.ToString();
    }

    private static string GenerateUTMWKT(GISProjection projection)
    {
        // Extract UTM zone from projection name or EPSG code
        var zone = ExtractUTMZone(projection);
        var isNorth = projection.Name.Contains("N") || !projection.Name.Contains("S");

        var wkt = new StringBuilder();
        wkt.Append($"PROJCS[\"{projection.Name}\",");
        wkt.Append("GEOGCS[\"WGS 84\",");
        wkt.Append("DATUM[\"WGS_1984\",");
        wkt.Append("SPHEROID[\"WGS 84\",6378137,298.257223563]],");
        wkt.Append("PRIMEM[\"Greenwich\",0],");
        wkt.Append("UNIT[\"degree\",0.0174532925199433]],");
        wkt.Append("PROJECTION[\"Transverse_Mercator\"],");
        wkt.Append("PARAMETER[\"latitude_of_origin\",0],");
        wkt.Append($"PARAMETER[\"central_meridian\",{(zone - 1) * 6 - 180 + 3}],");
        wkt.Append("PARAMETER[\"scale_factor\",0.9996],");
        wkt.Append("PARAMETER[\"false_easting\",500000],");
        wkt.Append($"PARAMETER[\"false_northing\",{(isNorth ? 0 : 10000000)}],");
        wkt.Append("UNIT[\"metre\",1]");

        if (!string.IsNullOrEmpty(projection.EPSG))
        {
            var parts = projection.EPSG.Split(':');
            if (parts.Length == 2) wkt.Append($",AUTHORITY[\"{parts[0]}\",\"{parts[1]}\"]");
        }

        wkt.Append("]");

        return wkt.ToString();
    }

    private static string GenerateMercatorWKT(GISProjection projection)
    {
        var wkt = new StringBuilder();
        wkt.Append($"PROJCS[\"{projection.Name}\",");
        wkt.Append("GEOGCS[\"WGS 84\",");
        wkt.Append("DATUM[\"WGS_1984\",");
        wkt.Append("SPHEROID[\"WGS 84\",6378137,298.257223563]],");
        wkt.Append("PRIMEM[\"Greenwich\",0],");
        wkt.Append("UNIT[\"degree\",0.0174532925199433]],");
        wkt.Append("PROJECTION[\"Mercator_1SP\"],");
        wkt.Append("PARAMETER[\"central_meridian\",0],");
        wkt.Append("PARAMETER[\"scale_factor\",1],");
        wkt.Append("PARAMETER[\"false_easting\",0],");
        wkt.Append("PARAMETER[\"false_northing\",0],");
        wkt.Append("UNIT[\"metre\",1]");

        if (!string.IsNullOrEmpty(projection.EPSG))
        {
            var parts = projection.EPSG.Split(':');
            if (parts.Length == 2) wkt.Append($",AUTHORITY[\"{parts[0]}\",\"{parts[1]}\"]");
        }

        wkt.Append("]");

        return wkt.ToString();
    }

    private static string GenerateLambertWKT(GISProjection projection)
    {
        var wkt = new StringBuilder();
        wkt.Append($"PROJCS[\"{projection.Name}\",");
        wkt.Append("GEOGCS[\"WGS 84\",");
        wkt.Append("DATUM[\"WGS_1984\",");
        wkt.Append("SPHEROID[\"WGS 84\",6378137,298.257223563]],");
        wkt.Append("PRIMEM[\"Greenwich\",0],");
        wkt.Append("UNIT[\"degree\",0.0174532925199433]],");
        wkt.Append("PROJECTION[\"Lambert_Conformal_Conic_2SP\"],");
        wkt.Append("PARAMETER[\"standard_parallel_1\",33],");
        wkt.Append("PARAMETER[\"standard_parallel_2\",45],");
        wkt.Append("PARAMETER[\"latitude_of_origin\",39],");
        wkt.Append("PARAMETER[\"central_meridian\",-96],");
        wkt.Append("PARAMETER[\"false_easting\",0],");
        wkt.Append("PARAMETER[\"false_northing\",0],");
        wkt.Append("UNIT[\"metre\",1]");

        if (!string.IsNullOrEmpty(projection.EPSG))
        {
            var parts = projection.EPSG.Split(':');
            if (parts.Length == 2) wkt.Append($",AUTHORITY[\"{parts[0]}\",\"{parts[1]}\"]");
        }

        wkt.Append("]");

        return wkt.ToString();
    }

    private static string GenerateAlbersWKT(GISProjection projection)
    {
        var wkt = new StringBuilder();
        wkt.Append($"PROJCS[\"{projection.Name}\",");
        wkt.Append("GEOGCS[\"WGS 84\",");
        wkt.Append("DATUM[\"WGS_1984\",");
        wkt.Append("SPHEROID[\"WGS 84\",6378137,298.257223563]],");
        wkt.Append("PRIMEM[\"Greenwich\",0],");
        wkt.Append("UNIT[\"degree\",0.0174532925199433]],");
        wkt.Append("PROJECTION[\"Albers_Conic_Equal_Area\"],");
        wkt.Append("PARAMETER[\"standard_parallel_1\",29.5],");
        wkt.Append("PARAMETER[\"standard_parallel_2\",45.5],");
        wkt.Append("PARAMETER[\"latitude_of_center\",37.5],");
        wkt.Append("PARAMETER[\"longitude_of_center\",-96],");
        wkt.Append("PARAMETER[\"false_easting\",0],");
        wkt.Append("PARAMETER[\"false_northing\",0],");
        wkt.Append("UNIT[\"metre\",1]");

        if (!string.IsNullOrEmpty(projection.EPSG))
        {
            var parts = projection.EPSG.Split(':');
            if (parts.Length == 2) wkt.Append($",AUTHORITY[\"{parts[0]}\",\"{parts[1]}\"]");
        }

        wkt.Append("]");

        return wkt.ToString();
    }

    private static string GenerateGenericWKT(GISProjection projection)
    {
        var wkt = new StringBuilder();
        wkt.Append($"PROJCS[\"{projection.Name}\"");

        if (!string.IsNullOrEmpty(projection.EPSG))
        {
            var parts = projection.EPSG.Split(':');
            if (parts.Length == 2) wkt.Append($",AUTHORITY[\"{parts[0]}\",\"{parts[1]}\"]");
        }

        wkt.Append("]");

        return wkt.ToString();
    }

    private static int ExtractUTMZone(GISProjection projection)
    {
        // Try to extract zone from EPSG code (e.g., EPSG:32633 -> zone 33)
        if (!string.IsNullOrEmpty(projection.EPSG))
            if (projection.EPSG.StartsWith("EPSG:326") || projection.EPSG.StartsWith("EPSG:327"))
                if (int.TryParse(projection.EPSG.Substring(projection.EPSG.Length - 2), out var zone))
                    return zone;

        // Try to extract from name (e.g., "UTM Zone 33N")
        var match = Regex.Match(projection.Name, @"zone\s*(\d+)",
            RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var zoneFromName)) return zoneFromName;

        // Default to zone 33 if unable to determine
        return 33;
    }

    /// <summary>
    ///     Extract scale from map images for GIS integration
    /// </summary>
    public static float ExtractMapScale(ImageDataset mapImage)
    {
        if (!mapImage.HasTag(ImageTag.Map))
        {
            Logger.LogWarning("Image is not tagged as a map");
            return 0;
        }

        if (mapImage.PixelSize <= 0)
        {
            Logger.LogWarning("Map image has no calibrated scale");
            return 0;
        }

        // Convert pixel size to map scale (assuming meters)
        var metersPerPixel = mapImage.Unit switch
        {
            "m" => mapImage.PixelSize,
            "km" => mapImage.PixelSize * 1000,
            "cm" => mapImage.PixelSize / 100,
            "mm" => mapImage.PixelSize / 1000,
            _ => mapImage.PixelSize / 1000000 // Assume micrometers
        };

        // Calculate representative fraction (1:X scale)
        var mapScale = 1.0f / metersPerPixel;

        mapImage.ImageMetadata["MapScale"] = $"1:{mapScale:F0}";
        mapImage.ImageMetadata["MetersPerPixel"] = metersPerPixel.ToString();

        return mapScale;
    }

    /// <summary>
    ///     Create a multi-scale analysis linking microscopy images at different magnifications
    /// </summary>
    public static MultiScaleAnalysis CreateMultiScaleAnalysis(params ImageDataset[] images)
    {
        var analysis = new MultiScaleAnalysis();

        foreach (var image in images)
        {
            if (image.PixelSize <= 0)
            {
                Logger.LogWarning($"Image '{image.Name}' has no calibrated scale, skipping");
                continue;
            }

            // Convert all scales to micrometers for comparison
            var scaleInMicrometers = ConvertToMicrometers(image.PixelSize, image.Unit);

            analysis.AddImage(image, scaleInMicrometers);
        }

        analysis.OrganizeByScale();
        return analysis;
    }

    /// <summary>
    ///     Correlate thin section images with XRD/XRF data
    /// </summary>
    public static void CorrelateThinSectionWithSpectroscopy(
        ImageDataset thinSection,
        Dataset spectroscopyData,
        List<Vector2> analysisPoints)
    {
        if (!thinSection.HasTag(ImageTag.ThinSection))
        {
            Logger.LogWarning("Image is not tagged as thin section");
            return;
        }

        // Store correlation data
        thinSection.ImageMetadata["SpectroscopyDataset"] = spectroscopyData.Name;
        thinSection.ImageMetadata["AnalysisPointCount"] = analysisPoints.Count.ToString();

        for (var i = 0; i < analysisPoints.Count; i++)
            thinSection.ImageMetadata[$"AnalysisPoint_{i}"] = $"{analysisPoints[i].X},{analysisPoints[i].Y}";

        Logger.Log($"Correlated thin section '{thinSection.Name}' with {analysisPoints.Count} spectroscopy points");
    }

    /// <summary>
    ///     Create time series from tagged images
    /// </summary>
    public static TimeSeriesImageCollection CreateTimeSeries(List<(ImageDataset image, DateTime timestamp)> images)
    {
        var collection = new TimeSeriesImageCollection();

        foreach (var (image, timestamp) in images.OrderBy(i => i.timestamp))
        {
            image.AddTag(ImageTag.TimeSeries);
            image.ImageMetadata["TimeSeriesTimestamp"] = timestamp.ToString("o");
            collection.AddFrame(image, timestamp);
        }

        // Calculate temporal statistics
        collection.CalculateTemporalChanges();

        return collection;
    }

    private static float ConvertToMicrometers(float value, string unit)
    {
        return unit?.ToLower() switch
        {
            "nm" => value / 1000f,
            "µm" or "um" => value,
            "mm" => value * 1000f,
            "cm" => value * 10000f,
            "m" => value * 1000000f,
            _ => value
        };
    }

    private static Matrix4x4 CalculateGeotransform(List<GroundControlPoint> gcps)
    {
        if (gcps.Count < 3) throw new ArgumentException("Need at least 3 ground control points");

        // For affine transformation, we need to solve for 6 parameters:
        // x' = a*x + b*y + c
        // y' = d*x + e*y + f

        // Build the least squares system: A * params = B
        var n = gcps.Count;
        var A = new double[n * 2, 6];
        var B = new double[n * 2];

        for (var i = 0; i < n; i++)
        {
            var imgX = gcps[i].ImageCoordinates.X;
            var imgY = gcps[i].ImageCoordinates.Y;
            var worldX = gcps[i].WorldCoordinates.X;
            var worldY = gcps[i].WorldCoordinates.Y;

            // For X equation
            A[i * 2, 0] = imgX;
            A[i * 2, 1] = imgY;
            A[i * 2, 2] = 1;
            A[i * 2, 3] = 0;
            A[i * 2, 4] = 0;
            A[i * 2, 5] = 0;
            B[i * 2] = worldX;

            // For Y equation
            A[i * 2 + 1, 0] = 0;
            A[i * 2 + 1, 1] = 0;
            A[i * 2 + 1, 2] = 0;
            A[i * 2 + 1, 3] = imgX;
            A[i * 2 + 1, 4] = imgY;
            A[i * 2 + 1, 5] = 1;
            B[i * 2 + 1] = worldY;
        }

        // Solve using least squares (A^T * A * params = A^T * B)
        var parameters = SolveLeastSquares(A, B);

        // Calculate residual errors for each GCP
        for (var i = 0; i < n; i++)
        {
            var imgX = gcps[i].ImageCoordinates.X;
            var imgY = gcps[i].ImageCoordinates.Y;

            var predictedX = (float)(parameters[0] * imgX + parameters[1] * imgY + parameters[2]);
            var predictedY = (float)(parameters[3] * imgX + parameters[4] * imgY + parameters[5]);

            var errorX = predictedX - gcps[i].WorldCoordinates.X;
            var errorY = predictedY - gcps[i].WorldCoordinates.Y;

            gcps[i].ResidualError = (float)Math.Sqrt(errorX * errorX + errorY * errorY);
        }

        // Build transformation matrix
        var transform = new Matrix4x4(
            (float)parameters[0], (float)parameters[1], 0, (float)parameters[2],
            (float)parameters[3], (float)parameters[4], 0, (float)parameters[5],
            0, 0, 1, 0,
            0, 0, 0, 1
        );

        return transform;
    }

    private static double[] SolveLeastSquares(double[,] A, double[] B)
    {
        var rows = A.GetLength(0);
        var cols = A.GetLength(1);

        // Compute A^T * A
        var ATA = new double[cols, cols];
        for (var i = 0; i < cols; i++)
        for (var j = 0; j < cols; j++)
        {
            double sum = 0;
            for (var k = 0; k < rows; k++) sum += A[k, i] * A[k, j];
            ATA[i, j] = sum;
        }

        // Compute A^T * B
        var ATB = new double[cols];
        for (var i = 0; i < cols; i++)
        {
            double sum = 0;
            for (var k = 0; k < rows; k++) sum += A[k, i] * B[k];
            ATB[i] = sum;
        }

        // Solve ATA * x = ATB using Gaussian elimination
        return GaussianElimination(ATA, ATB);
    }

    private static double[] GaussianElimination(double[,] A, double[] B)
    {
        var n = B.Length;
        var augmented = new double[n, n + 1];

        // Create augmented matrix
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++) augmented[i, j] = A[i, j];
            augmented[i, n] = B[i];
        }

        // Forward elimination
        for (var i = 0; i < n; i++)
        {
            // Find pivot
            var maxRow = i;
            for (var k = i + 1; k < n; k++)
                if (Math.Abs(augmented[k, i]) > Math.Abs(augmented[maxRow, i]))
                    maxRow = k;

            // Swap rows
            for (var k = i; k <= n; k++)
            {
                var temp = augmented[maxRow, k];
                augmented[maxRow, k] = augmented[i, k];
                augmented[i, k] = temp;
            }

            // Make all rows below this one 0 in current column
            for (var k = i + 1; k < n; k++)
            {
                var factor = augmented[k, i] / augmented[i, i];
                for (var j = i; j <= n; j++) augmented[k, j] -= factor * augmented[i, j];
            }
        }

        // Back substitution
        var solution = new double[n];
        for (var i = n - 1; i >= 0; i--)
        {
            solution[i] = augmented[i, n];
            for (var j = i + 1; j < n; j++) solution[i] -= augmented[i, j] * solution[j];
            solution[i] /= augmented[i, i];
        }

        return solution;
    }

    private static string SerializeMatrix(Matrix4x4 matrix)
    {
        return $"{matrix.M11},{matrix.M12},{matrix.M13},{matrix.M14};" +
               $"{matrix.M21},{matrix.M22},{matrix.M23},{matrix.M24};" +
               $"{matrix.M31},{matrix.M32},{matrix.M33},{matrix.M34};" +
               $"{matrix.M41},{matrix.M42},{matrix.M43},{matrix.M44}";
    }

    public static Matrix4x4 DeserializeMatrix(string serialized)
    {
        var rows = serialized.Split(';');
        if (rows.Length != 4) throw new ArgumentException("Invalid matrix format");

        var values = new float[4, 4];
        for (var i = 0; i < 4; i++)
        {
            var cols = rows[i].Split(',');
            if (cols.Length != 4) throw new ArgumentException("Invalid matrix format");

            for (var j = 0; j < 4; j++) values[i, j] = float.Parse(cols[j]);
        }

        return new Matrix4x4(
            values[0, 0], values[0, 1], values[0, 2], values[0, 3],
            values[1, 0], values[1, 1], values[1, 2], values[1, 3],
            values[2, 0], values[2, 1], values[2, 2], values[2, 3],
            values[3, 0], values[3, 1], values[3, 2], values[3, 3]
        );
    }

    /// <summary>
    ///     Apply georeferencing transformation to convert image coordinates to world coordinates
    /// </summary>
    public static Vector3 TransformImageToWorld(Vector2 imageCoord, Matrix4x4 transform)
    {
        var homogeneous = new Vector4(imageCoord.X, imageCoord.Y, 0, 1);
        var transformed = Vector4.Transform(homogeneous, transform);
        return new Vector3(transformed.X, transformed.Y, transformed.Z);
    }

    /// <summary>
    ///     Apply inverse transformation to convert world coordinates to image coordinates
    /// </summary>
    public static Vector2 TransformWorldToImage(Vector3 worldCoord, Matrix4x4 transform)
    {
        Matrix4x4 inverse;
        if (!Matrix4x4.Invert(transform, out inverse))
            throw new InvalidOperationException("Transform matrix is not invertible");

        var homogeneous = new Vector4(worldCoord.X, worldCoord.Y, worldCoord.Z, 1);
        var transformed = Vector4.Transform(homogeneous, inverse);
        return new Vector2(transformed.X, transformed.Y);
    }
}

/// <summary>
///     Ground Control Point for georeferencing
/// </summary>
public class GroundControlPoint
{
    public Vector2 ImageCoordinates { get; set; }
    public Vector3 WorldCoordinates { get; set; } // X, Y, Z in world coordinate system
    public string CoordinateSystem { get; set; } // e.g., "WGS84", "UTM"
    public float ResidualError { get; set; }
    public string PointName { get; set; }
    public string Description { get; set; }
    public DateTime CollectionDate { get; set; }
    public float Accuracy { get; set; } // Expected accuracy in meters
}

/// <summary>
///     Multi-scale analysis container
/// </summary>
public class MultiScaleAnalysis
{
    private readonly Dictionary<string, object> _analysisMetadata = new();
    private List<(ImageDataset image, float scale)> _images = new();

    public IReadOnlyList<(ImageDataset image, float scale)> Images => _images.AsReadOnly();
    public IReadOnlyDictionary<string, object> Metadata => _analysisMetadata;

    public void AddImage(ImageDataset image, float scaleInMicrometers)
    {
        _images.Add((image, scaleInMicrometers));
    }

    public void OrganizeByScale()
    {
        _images = _images.OrderBy(i => i.scale).ToList();

        // Calculate scale ratios and coverage
        var minScale = _images.First().scale;
        var maxScale = _images.Last().scale;
        var scaleRange = maxScale / minScale;

        _analysisMetadata["MinScale"] = minScale;
        _analysisMetadata["MaxScale"] = maxScale;
        _analysisMetadata["ScaleRange"] = scaleRange;
        _analysisMetadata["NumScales"] = _images.Count;

        // Calculate scale ratios between consecutive images
        for (var i = 1; i < _images.Count; i++)
        {
            var ratio = _images[i].scale / _images[i - 1].scale;
            _images[i].image.ImageMetadata["ScaleRatio"] = ratio.ToString("F2");
            _images[i].image.ImageMetadata["ScaleLevel"] = i.ToString();

            // Mark optimal scale transitions (ratio between 2 and 10)
            if (ratio >= 2 && ratio <= 10) _images[i].image.ImageMetadata["OptimalTransition"] = "true";
        }

        // Identify scale gaps
        List<(float start, float end)> gaps = new();
        for (var i = 1; i < _images.Count; i++)
        {
            var ratio = _images[i].scale / _images[i - 1].scale;
            if (ratio > 10) gaps.Add((_images[i - 1].scale, _images[i].scale));
        }

        _analysisMetadata["ScaleGaps"] = gaps;

        Logger.Log($"Organized {_images.Count} images in multi-scale hierarchy (range: {scaleRange:F1}x)");
    }

    public List<ImageDataset> GetImagesAtScale(float minScale, float maxScale)
    {
        return _images
            .Where(i => i.scale >= minScale && i.scale <= maxScale)
            .Select(i => i.image)
            .ToList();
    }

    public (ImageDataset image, float scale)? GetNearestScale(float targetScale)
    {
        if (_images.Count == 0) return null;

        return _images
            .OrderBy(i => Math.Abs(i.scale - targetScale))
            .FirstOrDefault();
    }

    public List<ImageDataset> GetOptimalPath(float startScale, float endScale)
    {
        var path = new List<ImageDataset>();

        var startImage = GetNearestScale(startScale);
        var endImage = GetNearestScale(endScale);

        if (!startImage.HasValue || !endImage.HasValue)
            return path;

        var startIdx = _images.FindIndex(i => i.image == startImage.Value.image);
        var endIdx = _images.FindIndex(i => i.image == endImage.Value.image);

        if (startIdx < 0 || endIdx < 0)
            return path;

        var step = startIdx < endIdx ? 1 : -1;
        for (var i = startIdx; i != endIdx + step; i += step) path.Add(_images[i].image);

        return path;
    }

    public void ExportScaleAnalysis(string outputPath)
    {
        using (var writer = new StreamWriter(outputPath))
        {
            writer.WriteLine("Image,Scale_um,Width,Height,ScaleRatio,OptimalTransition");

            for (var i = 0; i < _images.Count; i++)
            {
                var (image, scale) = _images[i];
                var ratio = i > 0 ? (scale / _images[i - 1].scale).ToString("F2") : "1.00";
                var optimal = image.ImageMetadata.ContainsKey("OptimalTransition");

                writer.WriteLine($"{image.Name},{scale:F3},{image.Width},{image.Height},{ratio},{optimal}");
            }
        }

        Logger.Log($"Exported scale analysis to {outputPath}");
    }
}

/// <summary>
///     Time series collection for temporal analysis
/// </summary>
public class TimeSeriesImageCollection
{
    private List<(ImageDataset image, DateTime timestamp)> _frames = new();
    public Dictionary<string, List<float>> TemporalMetrics { get; } = new();
    public Dictionary<string, object> AnalysisResults { get; } = new();

    public IReadOnlyList<(ImageDataset image, DateTime timestamp)> Frames => _frames.AsReadOnly();

    public void AddFrame(ImageDataset image, DateTime timestamp)
    {
        _frames.Add((image, timestamp));
    }

    public void CalculateTemporalChanges()
    {
        if (_frames.Count < 2) return;

        _frames = _frames.OrderBy(f => f.timestamp).ToList();

        var brightnessValues = new List<float>();
        var contrastValues = new List<float>();
        var entropyValues = new List<float>();
        var edgeDensityValues = new List<float>();

        foreach (var frame in _frames)
        {
            frame.image.Load();
            if (frame.image.ImageData != null)
            {
                var avgBrightness = CalculateAverageBrightness(frame.image.ImageData);
                var contrast = CalculateContrast(frame.image.ImageData);
                var entropy = CalculateEntropy(frame.image.ImageData, frame.image.Width, frame.image.Height);
                var edgeDensity = CalculateEdgeDensity(frame.image.ImageData, frame.image.Width, frame.image.Height);

                brightnessValues.Add(avgBrightness);
                contrastValues.Add(contrast);
                entropyValues.Add(entropy);
                edgeDensityValues.Add(edgeDensity);

                frame.image.Unload();
            }
        }

        TemporalMetrics["Brightness"] = brightnessValues;
        TemporalMetrics["Contrast"] = contrastValues;
        TemporalMetrics["Entropy"] = entropyValues;
        TemporalMetrics["EdgeDensity"] = edgeDensityValues;

        // Calculate change rates and trends
        CalculateChangeRates();
        DetectTrends();
        DetectAnomalies();
    }

    private void CalculateChangeRates()
    {
        for (var i = 1; i < _frames.Count; i++)
        {
            var timeDelta = (_frames[i].timestamp - _frames[i - 1].timestamp).TotalHours;

            foreach (var metric in TemporalMetrics.Keys.ToList())
                if (TemporalMetrics[metric].Count > i)
                {
                    var valueDelta = TemporalMetrics[metric][i] - TemporalMetrics[metric][i - 1];
                    var changeRate = valueDelta / timeDelta;

                    _frames[i].image.ImageMetadata[$"{metric}_ChangeRate"] = changeRate.ToString("F3");
                }
        }
    }

    private void DetectTrends()
    {
        foreach (var metric in TemporalMetrics)
        {
            if (metric.Value.Count < 3) continue;

            // Simple linear regression for trend detection
            float sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
            var n = metric.Value.Count;

            for (var i = 0; i < n; i++)
            {
                sumX += i;
                sumY += metric.Value[i];
                sumXY += i * metric.Value[i];
                sumX2 += i * i;
            }

            var slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
            var intercept = (sumY - slope * sumX) / n;

            // Calculate R-squared
            float totalSS = 0, residualSS = 0;
            var meanY = sumY / n;

            for (var i = 0; i < n; i++)
            {
                var predicted = slope * i + intercept;
                residualSS += (float)Math.Pow(metric.Value[i] - predicted, 2);
                totalSS += (float)Math.Pow(metric.Value[i] - meanY, 2);
            }

            var rSquared = 1 - residualSS / totalSS;

            AnalysisResults[$"{metric.Key}_Trend_Slope"] = slope;
            AnalysisResults[$"{metric.Key}_Trend_R2"] = rSquared;

            // Classify trend
            var trend = Math.Abs(slope) < 0.01 ? "Stable" :
                slope > 0 ? "Increasing" : "Decreasing";
            AnalysisResults[$"{metric.Key}_Trend"] = trend;
        }
    }

    private void DetectAnomalies()
    {
        foreach (var metric in TemporalMetrics)
        {
            if (metric.Value.Count < 5) continue;

            var mean = metric.Value.Average();
            var stdDev = (float)Math.Sqrt(metric.Value.Average(v => Math.Pow(v - mean, 2)));

            var anomalies = new List<int>();
            for (var i = 0; i < metric.Value.Count; i++)
            {
                var zScore = Math.Abs((metric.Value[i] - mean) / stdDev);
                if (zScore > 2.5) // Threshold for anomaly
                {
                    anomalies.Add(i);
                    _frames[i].image.ImageMetadata[$"{metric.Key}_Anomaly"] = "true";
                    _frames[i].image.ImageMetadata[$"{metric.Key}_ZScore"] = zScore.ToString("F2");
                }
            }

            if (anomalies.Count > 0) AnalysisResults[$"{metric.Key}_Anomalies"] = anomalies;
        }
    }

    private float CalculateAverageBrightness(byte[] imageData)
    {
        long sum = 0;
        var pixelCount = imageData.Length / 4;

        for (var i = 0; i < imageData.Length; i += 4)
        {
            // Calculate luminance
            var luminance = 0.299f * imageData[i] + 0.587f * imageData[i + 1] + 0.114f * imageData[i + 2];
            sum += (long)luminance;
        }

        return sum / (float)pixelCount;
    }

    private float CalculateContrast(byte[] imageData)
    {
        // Simplified contrast calculation using standard deviation
        var mean = CalculateAverageBrightness(imageData);
        float sumSquaredDiff = 0;
        var pixelCount = imageData.Length / 4;

        for (var i = 0; i < imageData.Length; i += 4)
        {
            var luminance = 0.299f * imageData[i] + 0.587f * imageData[i + 1] + 0.114f * imageData[i + 2];
            var diff = luminance - mean;
            sumSquaredDiff += diff * diff;
        }

        return (float)Math.Sqrt(sumSquaredDiff / pixelCount);
    }

    private float CalculateEntropy(byte[] imageData, int width, int height)
    {
        // Calculate histogram
        var histogram = new int[256];
        var pixelCount = width * height;

        for (var i = 0; i < imageData.Length; i += 4)
        {
            var gray = (int)(0.299f * imageData[i] + 0.587f * imageData[i + 1] + 0.114f * imageData[i + 2]);
            histogram[gray]++;
        }

        // Calculate entropy
        float entropy = 0;
        for (var i = 0; i < 256; i++)
            if (histogram[i] > 0)
            {
                var probability = (float)histogram[i] / pixelCount;
                entropy -= (float)(probability * Math.Log(probability, 2));
            }

        return entropy;
    }

    private float CalculateEdgeDensity(byte[] imageData, int width, int height)
    {
        // Simple edge detection using Sobel operator
        var edgePixels = 0;

        for (var y = 1; y < height - 1; y++)
        for (var x = 1; x < width - 1; x++)
        {
            var idx = (y * width + x) * 4;

            // Get surrounding pixels
            var center = 0.299f * imageData[idx] + 0.587f * imageData[idx + 1] + 0.114f * imageData[idx + 2];

            // Simplified edge detection
            var dx = Math.Abs(center -
                              (0.299f * imageData[idx + 4] + 0.587f * imageData[idx + 5] +
                               0.114f * imageData[idx + 6]));
            var dy = Math.Abs(center - (0.299f * imageData[idx + width * 4] + 0.587f * imageData[idx + width * 4 + 1] +
                                        0.114f * imageData[idx + width * 4 + 2]));

            var gradient = (float)Math.Sqrt(dx * dx + dy * dy);

            if (gradient > 30) // Threshold for edge
                edgePixels++;
        }

        return edgePixels / (float)(width * height);
    }

    public List<ImageDataset> GetFramesInTimeRange(DateTime start, DateTime end)
    {
        return _frames
            .Where(f => f.timestamp >= start && f.timestamp <= end)
            .Select(f => f.image)
            .ToList();
    }

    public void ExportTimeSeriesAnalysis(string outputPath)
    {
        using (var writer = new StreamWriter(outputPath))
        {
            // Write header
            writer.Write("Timestamp,Image");
            foreach (var metric in TemporalMetrics.Keys) writer.Write($",{metric}");
            writer.WriteLine();

            // Write data
            for (var i = 0; i < _frames.Count; i++)
            {
                writer.Write($"{_frames[i].timestamp:yyyy-MM-dd HH:mm:ss},{_frames[i].image.Name}");

                foreach (var metric in TemporalMetrics)
                    if (i < metric.Value.Count)
                        writer.Write($",{metric.Value[i]:F3}");
                    else
                        writer.Write(",");

                writer.WriteLine();
            }
        }

        Logger.Log($"Exported time series analysis to {outputPath}");
    }

    public (DateTime start, DateTime end, TimeSpan duration) GetTimeSpan()
    {
        if (_frames.Count == 0)
            return (DateTime.MinValue, DateTime.MinValue, TimeSpan.Zero);

        var sorted = _frames.OrderBy(f => f.timestamp).ToList();
        var start = sorted.First().timestamp;
        var end = sorted.Last().timestamp;
        var duration = end - start;

        return (start, end, duration);
    }
}