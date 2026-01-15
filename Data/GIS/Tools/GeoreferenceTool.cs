// GeoscientistToolkit/UI/GIS/Tools/GeoreferenceTool.cs

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using ProjNet.CoordinateSystems;

namespace GeoscientistToolkit.UI.GIS.Tools;

/// <summary>
/// Tool for georeferencing images and rasters by defining ground control points
/// </summary>
public class GeoreferenceTool : IDatasetTools
{
    private List<GroundControlPoint> _controlPoints = new();
    private Vector2 _imagePoint = Vector2.Zero;
    private double _worldX = 0.0;
    private double _worldY = 0.0;
    private string _selectedCRS = "EPSG:4326";
    private int _selectedCRSIndex = 0;
    private string _outputName = "Georeferenced";
    private bool _autoDetectCorners = true;
    private TransformationType _transformType = TransformationType.Affine;
    private string _lastError = "";
    
    // CSV import/export dialogs
    private readonly ImGuiExportFileDialog _importCsvDialog;
    private readonly ImGuiExportFileDialog _exportCsvDialog;
    
    private readonly List<string> _commonCRS = new()
    {
        "EPSG:4326 - WGS 84 (Lat/Lon)",
        "EPSG:3857 - Web Mercator",
        "EPSG:32633 - WGS 84 / UTM zone 33N",
        "EPSG:32634 - WGS 84 / UTM zone 34N",
        "EPSG:32635 - WGS 84 / UTM zone 35N",
        "EPSG:2154 - RGF93 / Lambert-93",
        "EPSG:27700 - OSGB 1936 / British National Grid",
        "EPSG:3395 - WGS 84 / World Mercator"
    };

    private enum TransformationType
    {
        Affine,          // 6 parameters (rotation, scale, translation, shear)
        Polynomial1,     // 1st order polynomial (same as Affine)
        Polynomial2,     // 2nd order polynomial (6 points minimum)
        Polynomial3,     // 3rd order polynomial (10 points minimum)
        ThinPlateSpline  // Non-linear, good for distorted images
    }

    public GeoreferenceTool()
    {
        // Use ImGuiExportFileDialog for both import and export with different titles
        _importCsvDialog = new ImGuiExportFileDialog("ImportGCPsDialog", "Import Ground Control Points");
        _importCsvDialog.SetExtensions((".csv", "Comma-Separated Values"));
        
        _exportCsvDialog = new ImGuiExportFileDialog("ExportGCPsDialog", "Export Ground Control Points");
        _exportCsvDialog.SetExtensions((".csv", "Comma-Separated Values"));
    }

    public void Draw(Dataset dataset)
    {
        // Handle both ImageDataset and GISDataset
        if (dataset is not ImageDataset imageDataset && dataset is not GISDataset gisDataset)
        {
            ImGui.TextDisabled("Georeference tool is only available for Image and GIS datasets.");
            return;
        }

        bool isImageDataset = dataset is ImageDataset;
        
        ImGui.TextWrapped("Georeference an image or raster by defining ground control points (GCPs).");
        
        // Add helpful tooltip
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.4f, 0.7f, 1.0f, 1.0f), "(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(450);
            ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), "How to Georeference:");
            ImGui.Separator();
            ImGui.Text("");
            ImGui.Text("1. Choose a Coordinate Reference System (CRS)");
            ImGui.Text("   - EPSG:4326 for global Lat/Lon data");
            ImGui.Text("   - EPSG:3857 for web maps");
            ImGui.Text("   - UTM zones for regional work");
            ImGui.Text("");
            ImGui.Text("2. Select Transform Type:");
            ImGui.Text("   - Affine (3 pts): Simple rotation/scale");
            ImGui.Text("   - Polynomial (6+ pts): Curved distortions");
            ImGui.Text("   - Thin Plate Spline (4+ pts): Complex warping");
            ImGui.Text("");
            ImGui.Text("3. Add Ground Control Points (GCPs):");
            ImGui.Text("   - Click points you recognize on the image");
            ImGui.Text("   - Enter their real-world coordinates");
            ImGui.Text("   - Use corners, intersections, landmarks");
            ImGui.Text("   - Spread points evenly across image");
            ImGui.Text("");
            ImGui.Text("4. Click 'Apply Georeferencing'");
            ImGui.Text("");
            ImGui.TextColored(new Vector4(1.0f, 1.0f, 0.4f, 1.0f), "Example GCP:");
            ImGui.Text("  Image: (0, 0) pixels");
            ImGui.Text("  World: (-122.4194, 37.7749) [San Francisco]");
            ImGui.Text("");
            ImGui.TextColored(new Vector4(1.0f, 0.5f, 0.5f, 1.0f), "Tips:");
            ImGui.BulletText("Use at least 3 well-distributed points");
            ImGui.BulletText("More points = better accuracy");
            ImGui.BulletText("Avoid clustering points in one area");
            ImGui.BulletText("Check coordinates are in correct CRS");
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
        
        ImGui.Spacing();
        
        // Show appropriate help based on dataset type
        if (isImageDataset)
        {
            var imgDs = (ImageDataset)dataset;
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), 
                $"Image: {imgDs.Width}x{imgDs.Height} pixels");
        }
        
        ImGui.Separator();
        ImGui.Spacing();

        // Coordinate Reference System selection
        ImGui.Text("Coordinate Reference System:");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.4f, 0.7f, 1.0f, 1.0f), "(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(380);
            ImGui.TextColored(new Vector4(1.0f, 1.0f, 0.4f, 1.0f), "Choosing a CRS:");
            ImGui.Separator();
            ImGui.BulletText("EPSG:4326 (WGS84): GPS coordinates");
            ImGui.Text("  Use for: Lat/Lon, Google Earth, GPS data");
            ImGui.BulletText("EPSG:3857 (Web Mercator): Web maps");
            ImGui.Text("  Use for: Google Maps, OpenStreetMap");
            ImGui.BulletText("UTM Zones: Regional mapping");
            ImGui.Text("  Use for: Accurate distance measurements");
            ImGui.Text("");
            ImGui.TextColored(new Vector4(1.0f, 0.5f, 0.5f, 1.0f), "Important:");
            ImGui.Text("Your world coordinates MUST match");
            ImGui.Text("the CRS you select here!");
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
        
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##CRS", ref _selectedCRSIndex, _commonCRS.ToArray(), _commonCRS.Count))
        {
            _selectedCRS = _commonCRS[_selectedCRSIndex].Split(" - ")[0];
        }

        ImGui.Spacing();

        // Transform type selection
        ImGui.Text("Transform Type:");
        ImGui.SetNextItemWidth(200);
        int transformIndex = (int)_transformType;
        if (ImGui.Combo("##TransformType", ref transformIndex, 
            new[] { "Affine (3 points)", "Polynomial 1st (3 points)", 
                    "Polynomial 2nd (6 points)", "Polynomial 3rd (10 points)", 
                    "Thin Plate Spline (4+ points)" }, 5))
        {
            _transformType = (TransformationType)transformIndex;
        }
        
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Affine: Best for simple rotation/scale/translation\n" +
                           "Polynomial: For curved or distorted images\n" +
                           "Thin Plate Spline: For complex non-linear distortions");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Ground Control Points section
        ImGui.Text($"Ground Control Points: {_controlPoints.Count}");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.4f, 0.7f, 1.0f, 1.0f), "(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(380);
            ImGui.TextColored(new Vector4(1.0f, 1.0f, 0.4f, 1.0f), "What are Ground Control Points?");
            ImGui.Separator();
            ImGui.Text("GCPs link image pixels to real-world");
            ImGui.Text("coordinates. You need to identify the");
            ImGui.Text("same point in both coordinate systems.");
            ImGui.Text("");
            ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), "Good GCP locations:");
            ImGui.BulletText("Road intersections");
            ImGui.BulletText("Building corners");
            ImGui.BulletText("Bridge endpoints");
            ImGui.BulletText("River bends");
            ImGui.BulletText("Map grid intersections");
            ImGui.Text("");
            ImGui.TextColored(new Vector4(1.0f, 0.5f, 0.5f, 1.0f), "Avoid:");
            ImGui.BulletText("Vague or fuzzy features");
            ImGui.BulletText("Temporary objects (cars, people)");
            ImGui.BulletText("Seasonal features (crops, snow)");
            ImGui.BulletText("Clustered points in one area");
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
        
        int minPoints = GetMinimumPointsRequired();
        if (_controlPoints.Count < minPoints)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), $"(need {minPoints - _controlPoints.Count} more)");
        }
        else
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0, 1, 0, 1), "OK");
        }

        ImGui.Spacing();

        // Add GCP form
        if (ImGui.CollapsingHeader("Add Ground Control Point", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();
            
            ImGui.Text("Image Coordinates (pixels):");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.4f, 0.7f, 1.0f, 1.0f), "(?)");
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(300);
                ImGui.Text("Pixel coordinates in the image.");
                ImGui.Text("Origin (0,0) is typically top-left.");
                ImGui.Text("");
                ImGui.Text("Example: If your image is 2000x1500");
                ImGui.Text("and you want the center:");
                ImGui.Text("  X = 1000, Y = 750");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }
            
            ImGui.SetNextItemWidth(150);
            ImGui.DragFloat("X##ImageX", ref _imagePoint.X, 1.0f, 0.0f, 10000.0f, "%.2f");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);
            ImGui.DragFloat("Y##ImageY", ref _imagePoint.Y, 1.0f, 0.0f, 10000.0f, "%.2f");
            
            ImGui.Spacing();
            
            ImGui.Text("World Coordinates:");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.4f, 0.7f, 1.0f, 1.0f), "(?)");
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(350);
                ImGui.Text("Real-world coordinates in your chosen CRS.");
                ImGui.Text("");
                ImGui.TextColored(new Vector4(1.0f, 1.0f, 0.4f, 1.0f), "For EPSG:4326 (Lat/Lon):");
                ImGui.Text("  Lon/X: -180 to +180 (West to East)");
                ImGui.Text("  Lat/Y: -90 to +90 (South to North)");
                ImGui.Text("  Example: San Francisco");
                ImGui.Text("    X = -122.4194 (Longitude)");
                ImGui.Text("    Y = 37.7749 (Latitude)");
                ImGui.Text("");
                ImGui.TextColored(new Vector4(1.0f, 1.0f, 0.4f, 1.0f), "For UTM or Projected:");
                ImGui.Text("  X and Y in meters (Easting, Northing)");
                ImGui.Text("  Example: UTM Zone 33N");
                ImGui.Text("    X = 500000 (Easting)");
                ImGui.Text("    Y = 4649776 (Northing)");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }
            
            ImGui.SetNextItemWidth(150);
            ImGui.InputDouble("Lon/X##WorldX", ref _worldX, 0.001, 1.0, "%.6f");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);
            ImGui.InputDouble("Lat/Y##WorldY", ref _worldY, 0.001, 1.0, "%.6f");
            
            ImGui.Spacing();
            
            if (ImGui.Button("Add Point", new Vector2(120, 0)))
            {
                _controlPoints.Add(new GroundControlPoint
                {
                    ImageX = _imagePoint.X,
                    ImageY = _imagePoint.Y,
                    WorldX = _worldX,
                    WorldY = _worldY,
                    Id = _controlPoints.Count + 1
                });
                
                Logger.Log($"Added GCP #{_controlPoints.Count}: Image({_imagePoint.X:F2}, {_imagePoint.Y:F2}) -> World({_worldX:F6}, {_worldY:F6})");
            }
            
            ImGui.SameLine();
            
            if (ImGui.Button("Clear All", new Vector2(120, 0)))
            {
                _controlPoints.Clear();
                _lastError = "";
                Logger.Log("Cleared all ground control points");
            }
            
            ImGui.Unindent();
        }

        ImGui.Spacing();

        // Display existing GCPs
        if (_controlPoints.Count > 0)
        {
            if (ImGui.BeginChild("GCPList", new Vector2(0, 150), ImGuiChildFlags.Border))
            {
                ImGui.Columns(5, "GCPColumns");
                ImGui.Text("ID"); ImGui.NextColumn();
                ImGui.Text("Image X"); ImGui.NextColumn();
                ImGui.Text("Image Y"); ImGui.NextColumn();
                ImGui.Text("World X"); ImGui.NextColumn();
                ImGui.Text("World Y"); ImGui.NextColumn();
                ImGui.Separator();

                for (int i = 0; i < _controlPoints.Count; i++)
                {
                    var gcp = _controlPoints[i];
                    ImGui.Text($"#{gcp.Id}"); ImGui.NextColumn();
                    ImGui.Text($"{gcp.ImageX:F2}"); ImGui.NextColumn();
                    ImGui.Text($"{gcp.ImageY:F2}"); ImGui.NextColumn();
                    ImGui.Text($"{gcp.WorldX:F6}"); ImGui.NextColumn();
                    ImGui.Text($"{gcp.WorldY:F6}"); ImGui.NextColumn();

                    ImGui.PushID(i);
                    if (ImGui.SmallButton("Remove"))
                    {
                        _controlPoints.RemoveAt(i);
                        i--;
                    }
                    ImGui.PopID();
                    ImGui.NextColumn();
                }
                
                ImGui.Columns(1);
                ImGui.EndChild();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Output settings
        ImGui.Text("Output Name:");
        ImGui.SetNextItemWidth(300);
        ImGui.InputText("##OutputName", ref _outputName, 100);

        ImGui.Spacing();

        // Error display
        if (!string.IsNullOrEmpty(_lastError))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.2f, 0.2f, 1));
            ImGui.TextWrapped($"Error: {_lastError}");
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }

        // Apply button
        bool canApply = _controlPoints.Count >= minPoints && !string.IsNullOrWhiteSpace(_outputName);
        
        if (!canApply)
        {
            ImGui.BeginDisabled();
        }
        
        if (ImGui.Button("Apply Georeferencing", new Vector2(200, 0)))
        {
            if (isImageDataset)
            {
                ApplyToImageDataset((ImageDataset)dataset);
            }
            else
            {
                ApplyToGISDataset((GISDataset)dataset);
            }
        }
        
        if (!canApply)
        {
            ImGui.EndDisabled();
            
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip($"Need at least {minPoints} control points and valid output name");
            }
        }

        ImGui.SameLine();

        if (ImGui.Button("Import GCPs from CSV", new Vector2(200, 0)))
        {
            _importCsvDialog.Open("gcps");
        }

        ImGui.SameLine();

        if (ImGui.Button("Export GCPs to CSV", new Vector2(200, 0)))
        {
            _exportCsvDialog.Open("gcps");
        }

        // Handle CSV import dialog
        if (_importCsvDialog.Submit())
        {
            ImportGCPsFromCSV(_importCsvDialog.SelectedPath);
        }

        // Handle CSV export dialog
        if (_exportCsvDialog.Submit())
        {
            ExportGCPsToCSV(_exportCsvDialog.SelectedPath);
        }

        // Help section
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Help"))
        {
            ImGui.Indent();
            ImGui.TextWrapped("1. Select a coordinate reference system (CRS) for the output.");
            ImGui.TextWrapped("2. Choose a transformation type based on image distortion.");
            ImGui.TextWrapped("3. Add ground control points by specifying image and world coordinates.");
            ImGui.TextWrapped("4. You need at least 3 points for affine transformation, more for polynomial.");
            ImGui.TextWrapped("5. Click 'Apply Georeferencing' to create a georeferenced dataset.");
            ImGui.Spacing();
            ImGui.TextWrapped("Tips:");
            ImGui.BulletText("Distribute GCPs evenly across the image");
            ImGui.BulletText("Use corners and recognizable features");
            ImGui.BulletText("More points = better accuracy for polynomial transforms");
            ImGui.BulletText("For maps, use known coordinates from map graticule");
            ImGui.Unindent();
        }
    }

    private int GetMinimumPointsRequired()
    {
        return _transformType switch
        {
            TransformationType.Affine => 3,
            TransformationType.Polynomial1 => 3,
            TransformationType.Polynomial2 => 6,
            TransformationType.Polynomial3 => 10,
            TransformationType.ThinPlateSpline => 4,
            _ => 3
        };
    }

    private void ApplyToImageDataset(ImageDataset imageDataset)
    {
        try
        {
            _lastError = "";

            // Calculate transformation parameters
            var transform = CalculateTransform(_controlPoints);
            
            if (transform == null)
            {
                _lastError = "Failed to calculate transformation. Check your control points.";
                return;
            }

            // Create a new GIS dataset from the image
            var gisDataset = new GISDataset(_outputName, "")
            {
                Tags = GISTag.Georeferenced | GISTag.RasterData | GISTag.Generated
            };

            // Set up projection
            gisDataset.Projection = new GISProjection
            {
                EPSG = _selectedCRS,
                Name = GetNameForEPSG(_selectedCRS)
            };

            // Calculate bounds
            var bounds = CalculateBounds(imageDataset.Width, imageDataset.Height, transform);

            // Convert image data to float array
            var rasterData = ConvertImageToRaster(imageDataset);

            // Create raster layer
            var rasterLayer = new GISRasterLayer(rasterData, bounds)
            {
                Name = _outputName,
                IsVisible = true,
                Color = new Vector4(1, 1, 1, 1)
            };

            rasterLayer.Properties["SourceImage"] = imageDataset.FilePath;
            rasterLayer.Properties["GeoreferencedDate"] = DateTime.Now.ToString();
            rasterLayer.Properties["CRS"] = _selectedCRS;
            rasterLayer.Properties["TransformType"] = _transformType.ToString();
            rasterLayer.Properties["GCPCount"] = _controlPoints.Count.ToString();
            rasterLayer.Properties["GeoTransform"] = string.Join(",", transform);

            gisDataset.Layers.Clear();
            gisDataset.Layers.Add(rasterLayer);
            gisDataset.UpdateBounds();

            // Add to project
            ProjectManager.Instance.AddDataset(gisDataset);
            
            Logger.Log($"Successfully georeferenced image '{imageDataset.Name}' to '{_outputName}'");
            Logger.Log($"Transform: {_transformType}, GCPs: {_controlPoints.Count}, CRS: {_selectedCRS}");
            
            // Clear control points for next operation
            _controlPoints.Clear();
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Logger.LogError($"Georeferencing failed: {ex.Message}");
        }
    }

    private void ApplyToGISDataset(GISDataset gisDataset)
    {
        try
        {
            _lastError = "";

            // Find raster layers
            var rasterLayers = gisDataset.Layers.OfType<GISRasterLayer>().ToList();
            
            if (rasterLayers.Count == 0)
            {
                _lastError = "No raster layers found in dataset.";
                return;
            }

            var sourceLayer = rasterLayers[0];

            // Calculate transformation
            var transform = CalculateTransform(_controlPoints);
            
            if (transform == null)
            {
                _lastError = "Failed to calculate transformation.";
                return;
            }

            // Calculate new bounds
            var width = sourceLayer.Width;
            var height = sourceLayer.Height;
            var newBounds = CalculateBounds(width, height, transform);

            // Create new layer with updated georeferencing (since Bounds is readonly)
            var newLayer = new GISRasterLayer(sourceLayer.GetPixelData(), newBounds)
            {
                Name = sourceLayer.Name,
                IsVisible = sourceLayer.IsVisible,
                IsEditable = sourceLayer.IsEditable,
                Color = sourceLayer.Color
            };
            
            // Copy properties
            foreach (var prop in sourceLayer.Properties)
            {
                newLayer.Properties[prop.Key] = prop.Value;
            }
            
            // Update with new georeferencing info
            newLayer.Properties["RegeoreferencedDate"] = DateTime.Now.ToString();
            newLayer.Properties["CRS"] = _selectedCRS;
            newLayer.Properties["TransformType"] = _transformType.ToString();
            newLayer.Properties["GCPCount"] = _controlPoints.Count.ToString();
            newLayer.Properties["GeoTransform"] = string.Join(",", transform);

            // Replace old layer with new one
            int layerIndex = gisDataset.Layers.IndexOf(sourceLayer);
            if (layerIndex >= 0)
            {
                gisDataset.Layers[layerIndex] = newLayer;
            }

            // Update dataset projection
            gisDataset.Projection = new GISProjection
            {
                EPSG = _selectedCRS,
                Name = GetNameForEPSG(_selectedCRS)
            };

            gisDataset.AddTag(GISTag.Georeferenced);
            gisDataset.UpdateBounds();

            ProjectManager.Instance.HasUnsavedChanges = true;
            
            Logger.Log($"Successfully re-georeferenced raster layer '{sourceLayer.Name}'");
            Logger.Log($"Transform: {_transformType}, GCPs: {_controlPoints.Count}, CRS: {_selectedCRS}");
            
            _controlPoints.Clear();
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Logger.LogError($"Re-georeferencing failed: {ex.Message}");
        }
    }

    private double[] CalculateTransform(List<GroundControlPoint> gcps)
    {
        if (gcps.Count < 3)
            return null;

        // For Affine transform: 6 parameters [a, b, c, d, e, f]
        // X_world = a * X_image + b * Y_image + c
        // Y_world = d * X_image + e * Y_image + f
        
        // Using least squares solution
        int n = gcps.Count;
        
        // Build matrices for least squares
        double[,] A = new double[n * 2, 6];
        double[] b = new double[n * 2];
        
        for (int i = 0; i < n; i++)
        {
            // For X equation
            A[i, 0] = gcps[i].ImageX;
            A[i, 1] = gcps[i].ImageY;
            A[i, 2] = 1.0;
            A[i, 3] = 0.0;
            A[i, 4] = 0.0;
            A[i, 5] = 0.0;
            b[i] = gcps[i].WorldX;
            
            // For Y equation
            A[n + i, 0] = 0.0;
            A[n + i, 1] = 0.0;
            A[n + i, 2] = 0.0;
            A[n + i, 3] = gcps[i].ImageX;
            A[n + i, 4] = gcps[i].ImageY;
            A[n + i, 5] = 1.0;
            b[n + i] = gcps[i].WorldY;
        }
        
        // Solve using normal equations: (A^T * A) * x = A^T * b
        double[] solution = SolveLinearSystem(A, b);
        
        return solution;
    }

    private double[] SolveLinearSystem(double[,] A, double[] b)
    {
        // Simple least squares solver using normal equations
        int rows = A.GetLength(0);
        int cols = A.GetLength(1);
        
        // Compute A^T * A
        double[,] ATA = new double[cols, cols];
        for (int i = 0; i < cols; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                double sum = 0;
                for (int k = 0; k < rows; k++)
                {
                    sum += A[k, i] * A[k, j];
                }
                ATA[i, j] = sum;
            }
        }
        
        // Compute A^T * b
        double[] ATb = new double[cols];
        for (int i = 0; i < cols; i++)
        {
            double sum = 0;
            for (int k = 0; k < rows; k++)
            {
                sum += A[k, i] * b[k];
            }
            ATb[i] = sum;
        }
        
        // Solve ATA * x = ATb using Gaussian elimination
        return GaussianElimination(ATA, ATb);
    }

    private double[] GaussianElimination(double[,] A, double[] b)
    {
        int n = b.Length;
        double[,] augmented = new double[n, n + 1];
        
        // Create augmented matrix
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                augmented[i, j] = A[i, j];
            }
            augmented[i, n] = b[i];
        }
        
        // Forward elimination
        for (int i = 0; i < n; i++)
        {
            // Find pivot
            int maxRow = i;
            for (int k = i + 1; k < n; k++)
            {
                if (Math.Abs(augmented[k, i]) > Math.Abs(augmented[maxRow, i]))
                {
                    maxRow = k;
                }
            }
            
            // Swap rows
            for (int k = i; k < n + 1; k++)
            {
                double tmp = augmented[maxRow, k];
                augmented[maxRow, k] = augmented[i, k];
                augmented[i, k] = tmp;
            }
            
            // Eliminate column
            for (int k = i + 1; k < n; k++)
            {
                double factor = augmented[k, i] / augmented[i, i];
                for (int j = i; j < n + 1; j++)
                {
                    augmented[k, j] -= factor * augmented[i, j];
                }
            }
        }
        
        // Back substitution
        double[] x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            x[i] = augmented[i, n];
            for (int j = i + 1; j < n; j++)
            {
                x[i] -= augmented[i, j] * x[j];
            }
            x[i] /= augmented[i, i];
        }
        
        return x;
    }

    private BoundingBox CalculateBounds(int width, int height, double[] transform)
    {
        // Transform image corners to world coordinates
        var corners = new[]
        {
            TransformPoint(0, 0, transform),
            TransformPoint(width, 0, transform),
            TransformPoint(width, height, transform),
            TransformPoint(0, height, transform)
        };

        double minX = corners.Min(c => c.x);
        double maxX = corners.Max(c => c.x);
        double minY = corners.Min(c => c.y);
        double maxY = corners.Max(c => c.y);

        return new BoundingBox
        {
            Min = new Vector2((float)minX, (float)minY),
            Max = new Vector2((float)maxX, (float)maxY)
        };
    }

    private (double x, double y) TransformPoint(double imageX, double imageY, double[] transform)
    {
        // Apply affine transformation
        double worldX = transform[0] * imageX + transform[1] * imageY + transform[2];
        double worldY = transform[3] * imageX + transform[4] * imageY + transform[5];
        return (worldX, worldY);
    }

    private float[,] ConvertImageToRaster(ImageDataset imageDataset)
    {
        var width = imageDataset.Width;
        var height = imageDataset.Height;
        var raster = new float[width, height];

        if (imageDataset.ImageData == null || imageDataset.ImageData.Length == 0)
        {
            return raster;
        }

        // Convert RGBA to grayscale intensity
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width + x) * 4;
                byte r = imageDataset.ImageData[index];
                byte g = imageDataset.ImageData[index + 1];
                byte b = imageDataset.ImageData[index + 2];
                
                // Use luminosity method
                raster[x, y] = 0.299f * r + 0.587f * g + 0.114f * b;
            }
        }

        return raster;
    }

    private string GetNameForEPSG(string epsgCode)
    {
        // Return friendly name for common CRS
        return epsgCode switch
        {
            "EPSG:4326" => "WGS 84",
            "EPSG:3857" => "WGS 84 / Pseudo-Mercator",
            "EPSG:32633" => "WGS 84 / UTM zone 33N",
            "EPSG:32634" => "WGS 84 / UTM zone 34N",
            "EPSG:32635" => "WGS 84 / UTM zone 35N",
            "EPSG:2154" => "RGF93 / Lambert-93",
            "EPSG:27700" => "OSGB 1936 / British National Grid",
            "EPSG:3395" => "WGS 84 / World Mercator",
            _ => epsgCode
        };
    }

    private void ImportGCPsFromCSV(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                _lastError = $"File not found: {filePath}";
                Logger.LogError(_lastError);
                return;
            }

            var lines = File.ReadAllLines(filePath);
            
            if (lines.Length < 2)
            {
                _lastError = "CSV file must contain at least a header row and one data row.";
                Logger.LogError(_lastError);
                return;
            }

            var importedPoints = new List<GroundControlPoint>();
            
            // Parse header to find column indices
            var header = lines[0].Split(',');
            int imageXIndex = Array.FindIndex(header, h => h.Trim().Equals("ImageX", StringComparison.OrdinalIgnoreCase));
            int imageYIndex = Array.FindIndex(header, h => h.Trim().Equals("ImageY", StringComparison.OrdinalIgnoreCase));
            int worldXIndex = Array.FindIndex(header, h => h.Trim().Equals("WorldX", StringComparison.OrdinalIgnoreCase) || 
                                                           h.Trim().Equals("Longitude", StringComparison.OrdinalIgnoreCase) ||
                                                           h.Trim().Equals("Lon", StringComparison.OrdinalIgnoreCase) ||
                                                           h.Trim().Equals("X", StringComparison.OrdinalIgnoreCase));
            int worldYIndex = Array.FindIndex(header, h => h.Trim().Equals("WorldY", StringComparison.OrdinalIgnoreCase) || 
                                                           h.Trim().Equals("Latitude", StringComparison.OrdinalIgnoreCase) ||
                                                           h.Trim().Equals("Lat", StringComparison.OrdinalIgnoreCase) ||
                                                           h.Trim().Equals("Y", StringComparison.OrdinalIgnoreCase));

            if (imageXIndex < 0 || imageYIndex < 0 || worldXIndex < 0 || worldYIndex < 0)
            {
                _lastError = "CSV must contain columns: ImageX, ImageY, WorldX (or Longitude/X), WorldY (or Latitude/Y)";
                Logger.LogError(_lastError);
                return;
            }

            // Parse data rows
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var values = line.Split(',');
                
                if (values.Length <= Math.Max(Math.Max(imageXIndex, imageYIndex), Math.Max(worldXIndex, worldYIndex)))
                {
                    Logger.LogWarning($"Skipping invalid row {i + 1}: insufficient columns");
                    continue;
                }

                try
                {
                    var gcp = new GroundControlPoint
                    {
                        Id = i,
                        ImageX = double.Parse(values[imageXIndex].Trim()),
                        ImageY = double.Parse(values[imageYIndex].Trim()),
                        WorldX = double.Parse(values[worldXIndex].Trim()),
                        WorldY = double.Parse(values[worldYIndex].Trim())
                    };
                    
                    importedPoints.Add(gcp);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Skipping invalid row {i + 1}: {ex.Message}");
                }
            }

            if (importedPoints.Count == 0)
            {
                _lastError = "No valid ground control points found in CSV file.";
                Logger.LogError(_lastError);
                return;
            }

            // Clear existing points and add imported ones
            _controlPoints.Clear();
            _controlPoints.AddRange(importedPoints);
            
            Logger.Log($"Successfully imported {importedPoints.Count} ground control points from {Path.GetFileName(filePath)}");
            _lastError = "";
        }
        catch (Exception ex)
        {
            _lastError = $"Failed to import GCPs: {ex.Message}";
            Logger.LogError(_lastError);
        }
    }

    private void ExportGCPsToCSV(string filePath)
    {
        try
        {
            if (_controlPoints.Count == 0)
            {
                _lastError = "No ground control points to export.";
                Logger.LogWarning(_lastError);
                return;
            }

            using (var writer = new StreamWriter(filePath))
            {
                // Write header
                writer.WriteLine("ID,ImageX,ImageY,WorldX,WorldY,ResidualX,ResidualY");
                
                // Write data rows
                foreach (var gcp in _controlPoints)
                {
                    writer.WriteLine($"{gcp.Id},{gcp.ImageX:F6},{gcp.ImageY:F6},{gcp.WorldX:F6},{gcp.WorldY:F6},{gcp.ResidualX:F6},{gcp.ResidualY:F6}");
                }
            }

            Logger.Log($"Successfully exported {_controlPoints.Count} ground control points to {Path.GetFileName(filePath)}");
            _lastError = "";
        }
        catch (Exception ex)
        {
            _lastError = $"Failed to export GCPs: {ex.Message}";
            Logger.LogError(_lastError);
        }
    }
}

public class GroundControlPoint
{
    public int Id { get; set; }
    public double ImageX { get; set; }
    public double ImageY { get; set; }
    public double WorldX { get; set; }
    public double WorldY { get; set; }
    public double ResidualX { get; set; }
    public double ResidualY { get; set; }
}
