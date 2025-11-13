// GeoscientistToolkit/Data/Loaders/LASLoader.cs

using System.Globalization;
using System.Numerics;
using System.Text;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders;

/// <summary>
/// Loader for LAS (Log ASCII Standard) format files - industry standard for well log data
/// Supports LAS 1.2, 2.0, and 3.0 formats
/// </summary>
public class LASLoader : IDataLoader
{
    private string _filePath;
    private LASFile _parsedData;
    
    public string Name => "LAS Log Loader";
    public string Description => "Import well log data from LAS (Log ASCII Standard) format files";
    
    public bool CanImport => !string.IsNullOrEmpty(_filePath) && 
                             File.Exists(_filePath) && 
                             _parsedData != null && 
                             _parsedData.IsValid;
    
    public string ValidationMessage => _parsedData?.ValidationMessage ?? "No file selected";
    
    public string FilePath
    {
        get => _filePath;
        set
        {
            _filePath = value;
            if (!string.IsNullOrEmpty(_filePath) && File.Exists(_filePath))
            {
                try
                {
                    _parsedData = ParseLASFile(_filePath);
                }
                catch (Exception ex)
                {
                    _parsedData = new LASFile { IsValid = false, ValidationMessage = $"Error parsing file: {ex.Message}" };
                }
            }
            else
            {
                _parsedData = null;
            }
        }
    }
    
    public LASFile ParsedData => _parsedData;
    
    public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
    {
        if (!CanImport)
            throw new InvalidOperationException("Cannot import: " + ValidationMessage);
        
        return await Task.Run(() =>
        {
            try
            {
                progressReporter?.Report((0.1f, "Reading LAS file..."));
                
                // Re-parse to ensure we have fresh data
                var lasData = ParseLASFile(_filePath);
                
                progressReporter?.Report((0.3f, "Creating borehole dataset..."));
                
                // Create borehole dataset - no file path since it's memory-only from LAS import
                var wellName = lasData.WellInfo.GetValueOrDefault("WELL", Path.GetFileNameWithoutExtension(_filePath));
                var dataset = new BoreholeDataset(wellName, "");
                
                // Store original LAS source in metadata for reference
                dataset.DatasetMetadata.CustomFields["ImportedFromLAS"] = _filePath;
                
                // Set well information
                progressReporter?.Report((0.4f, "Setting well information..."));
                SetWellInformation(dataset, lasData);
                
                // Clear default parameter tracks - we'll add only LAS curves
                progressReporter?.Report((0.5f, "Clearing default tracks..."));
                dataset.ParameterTracks.Clear();
                dataset.LithologyUnits.Clear();
                dataset.Fractures.Clear();
                
                // Add parameter tracks
                progressReporter?.Report((0.6f, "Adding parameter tracks..."));
                AddParameterTracks(dataset, lasData);
                
                // Populate data points
                progressReporter?.Report((0.75f, "Loading log data..."));
                PopulateLogData(dataset, lasData);
                
                // Estimate lithology if possible
                progressReporter?.Report((0.9f, "Processing lithology..."));
                EstimateLithology(dataset, lasData);
                
                progressReporter?.Report((1.0f, "Import complete!"));
                
                Logger.Log($"Successfully loaded LAS file: {wellName} with {lasData.Curves.Count} curves and {lasData.DataRows.Count} data points");
                
                return dataset;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to load LAS file: {ex.Message}");
                throw;
            }
        });
    }
    
    public void Reset()
    {
        _filePath = null;
        _parsedData = null;
    }
    
    private LASFile ParseLASFile(string filePath)
    {
        var lasFile = new LASFile();
        var lines = File.ReadAllLines(filePath, Encoding.UTF8);
        
        string currentSection = "";
        var dataLines = new List<string>();
        
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            
            // Skip empty lines and comments outside sections
            if (string.IsNullOrWhiteSpace(trimmedLine) || (trimmedLine.StartsWith("#") && currentSection == ""))
                continue;
            
            // Check for section markers
            if (trimmedLine.StartsWith("~"))
            {
                currentSection = trimmedLine.ToUpper();
                continue;
            }
            
            // Parse based on current section
            if (currentSection.StartsWith("~V"))
            {
                ParseVersionLine(trimmedLine, lasFile);
            }
            else if (currentSection.StartsWith("~W"))
            {
                ParseWellInfoLine(trimmedLine, lasFile);
            }
            else if (currentSection.StartsWith("~C"))
            {
                ParseCurveLine(trimmedLine, lasFile);
            }
            else if (currentSection.StartsWith("~P"))
            {
                ParseParameterLine(trimmedLine, lasFile);
            }
            else if (currentSection.StartsWith("~A"))
            {
                // Data section - collect all lines
                if (!trimmedLine.StartsWith("#") && !string.IsNullOrWhiteSpace(trimmedLine))
                    dataLines.Add(trimmedLine);
            }
        }
        
        // Parse data section
        if (dataLines.Count > 0)
            ParseDataSection(dataLines, lasFile);
        
        // Validate
        lasFile.IsValid = ValidateLASFile(lasFile, out var validationMsg);
        lasFile.ValidationMessage = validationMsg;
        
        return lasFile;
    }
    
    private void ParseVersionLine(string line, LASFile lasFile)
    {
        if (line.StartsWith("#")) return;
        
        var parts = line.Split(new[] { '.', ':' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            var key = parts[0].Trim();
            var value = parts[1].Split('#')[0].Trim(); // Remove inline comments
            
            if (key.Equals("VERS", StringComparison.OrdinalIgnoreCase))
                lasFile.Version = value;
            else if (key.Equals("WRAP", StringComparison.OrdinalIgnoreCase))
                lasFile.Wrap = value.Equals("YES", StringComparison.OrdinalIgnoreCase);
        }
    }
    
    private void ParseWellInfoLine(string line, LASFile lasFile)
    {
        if (line.StartsWith("#")) return;
        
        var parts = line.Split(new[] { '.' }, 2);
        if (parts.Length < 2) return;
        
        var mnemonic = parts[0].Trim();
        var rest = parts[1];
        
        // Split by first colon to separate unit/value from description
        var colonIndex = rest.IndexOf(':');
        if (colonIndex == -1) return;
        
        var unitValue = rest.Substring(0, colonIndex).Trim();
        var description = rest.Substring(colonIndex + 1).Trim();
        
        // Extract unit and value
        var spaceIndex = unitValue.IndexOf(' ');
        string unit = "";
        string value = unitValue;
        
        if (spaceIndex > 0)
        {
            unit = unitValue.Substring(0, spaceIndex).Trim();
            value = unitValue.Substring(spaceIndex + 1).Trim();
        }
        
        lasFile.WellInfo[mnemonic] = value;
        if (!string.IsNullOrEmpty(unit))
            lasFile.WellUnits[mnemonic] = unit;
        if (!string.IsNullOrEmpty(description))
            lasFile.WellDescriptions[mnemonic] = description;
    }
    
    private void ParseCurveLine(string line, LASFile lasFile)
    {
        if (line.StartsWith("#")) return;
        
        var parts = line.Split(new[] { '.' }, 2);
        if (parts.Length < 2) return;
        
        var mnemonic = parts[0].Trim();
        var rest = parts[1];
        
        var colonIndex = rest.IndexOf(':');
        if (colonIndex == -1) return;
        
        var unit = rest.Substring(0, colonIndex).Trim();
        var description = rest.Substring(colonIndex + 1).Trim();
        
        var curve = new LASCurve
        {
            Mnemonic = mnemonic,
            Unit = unit,
            Description = description
        };
        
        lasFile.Curves.Add(curve);
    }
    
    private void ParseParameterLine(string line, LASFile lasFile)
    {
        if (line.StartsWith("#")) return;
        
        var parts = line.Split(new[] { '.' }, 2);
        if (parts.Length < 2) return;
        
        var mnemonic = parts[0].Trim();
        var rest = parts[1];
        
        var colonIndex = rest.IndexOf(':');
        if (colonIndex == -1) return;
        
        var unitValue = rest.Substring(0, colonIndex).Trim();
        var description = rest.Substring(colonIndex + 1).Trim();
        
        var spaceIndex = unitValue.IndexOf(' ');
        string unit = "";
        string value = unitValue;
        
        if (spaceIndex > 0)
        {
            unit = unitValue.Substring(0, spaceIndex).Trim();
            value = unitValue.Substring(spaceIndex + 1).Trim();
        }
        
        lasFile.Parameters[mnemonic] = value;
        if (!string.IsNullOrEmpty(unit))
            lasFile.ParameterUnits[mnemonic] = unit;
        if (!string.IsNullOrEmpty(description))
            lasFile.ParameterDescriptions[mnemonic] = description;
    }
    
    private void ParseDataSection(List<string> dataLines, LASFile lasFile)
    {
        var nullValue = lasFile.WellInfo.GetValueOrDefault("NULL", "-999.25");
        
        foreach (var line in dataLines)
        {
            var values = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            
            if (values.Length == lasFile.Curves.Count)
            {
                var row = new List<float?>();
                
                foreach (var valueStr in values)
                {
                    if (float.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    {
                        // Check if this is a null value
                        if (Math.Abs(value - float.Parse(nullValue, CultureInfo.InvariantCulture)) < 0.01f)
                            row.Add(null);
                        else
                            row.Add(value);
                    }
                    else
                    {
                        row.Add(null);
                    }
                }
                
                lasFile.DataRows.Add(row);
            }
        }
    }
    
    private bool ValidateLASFile(LASFile lasFile, out string message)
    {
        if (lasFile.Curves.Count == 0)
        {
            message = "No curves defined in LAS file";
            return false;
        }
        
        if (lasFile.DataRows.Count == 0)
        {
            message = "No data found in LAS file";
            return false;
        }
        
        // Check for depth curve
        var depthCurve = lasFile.Curves.FirstOrDefault(c => 
            c.Mnemonic.Equals("DEPT", StringComparison.OrdinalIgnoreCase) ||
            c.Mnemonic.Equals("DEPTH", StringComparison.OrdinalIgnoreCase));
        
        if (depthCurve == null)
        {
            message = "No depth curve (DEPT/DEPTH) found in LAS file";
            return false;
        }
        
        message = $"Valid LAS file: {lasFile.Curves.Count} curves, {lasFile.DataRows.Count} data points";
        return true;
    }
    
    private void SetWellInformation(BoreholeDataset dataset, LASFile lasData)
    {
        // Well name
        dataset.WellName = lasData.WellInfo.GetValueOrDefault("WELL", dataset.Name);
        
        // Field
        dataset.Field = lasData.WellInfo.GetValueOrDefault("FLD", 
                        lasData.WellInfo.GetValueOrDefault("FIELD", ""));
        
        // Location
        if (lasData.WellInfo.TryGetValue("LOC", out var loc))
            dataset.DatasetMetadata.LocationName = loc;
        
        // Coordinates
        if (lasData.WellInfo.TryGetValue("X", out var xStr) && 
            float.TryParse(xStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            dataset.CoordinatesX = x;
        
        if (lasData.WellInfo.TryGetValue("Y", out var yStr) && 
            float.TryParse(yStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            dataset.CoordinatesY = y;
        
        // Elevation
        if (lasData.WellInfo.TryGetValue("ELEV", out var elevStr) && 
            float.TryParse(elevStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var elev))
            dataset.Elevation = elev;
        
        // Total depth - calculate from depth range (will be normalized to start at 0)
        var depthIndex = GetDepthCurveIndex(lasData);
        if (depthIndex >= 0)
        {
            var depths = lasData.DataRows
                .Where(row => row[depthIndex].HasValue)
                .Select(row => row[depthIndex].Value)
                .ToList();
            
            if (depths.Any())
            {
                var firstDepth = depths.First();
                var lastDepth = depths.Last();
                var minDepth = depths.Min();
                var maxDepth = depths.Max();
                
                // Total depth is always the absolute span
                dataset.TotalDepth = Math.Abs(maxDepth - minDepth);
                
                // Store original depth range in metadata
                dataset.DatasetMetadata.CustomFields["OriginalDepthStart"] = firstDepth.ToString(CultureInfo.InvariantCulture);
                dataset.DatasetMetadata.CustomFields["OriginalDepthEnd"] = lastDepth.ToString(CultureInfo.InvariantCulture);
            }
        }
        
        // Set additional metadata
        if (lasData.WellInfo.TryGetValue("UWI", out var uwi))
            dataset.DatasetMetadata.CustomFields["UWI"] = uwi;
        
        if (lasData.WellInfo.TryGetValue("COMP", out var company))
            dataset.DatasetMetadata.CustomFields["Company"] = company;
        
        if (lasData.WellInfo.TryGetValue("SRVC", out var service))
            dataset.DatasetMetadata.CustomFields["ServiceCompany"] = service;
        
        if (lasData.WellInfo.TryGetValue("DATE", out var date))
            dataset.DatasetMetadata.CustomFields["LogDate"] = date;
    }
    
    private void AddParameterTracks(BoreholeDataset dataset, LASFile lasData)
    {
        var depthIndex = GetDepthCurveIndex(lasData);
        
        for (int i = 0; i < lasData.Curves.Count; i++)
        {
            // Skip depth curve
            if (i == depthIndex) continue;
            
            var curve = lasData.Curves[i];
            
            // Determine if logarithmic based on common mnemonics
            var isLog = IsLogarithmicCurve(curve.Mnemonic);
            
            // Try to get min/max from data
            var values = lasData.DataRows
                .Where(row => row[i].HasValue)
                .Select(row => row[i].Value)
                .ToList();
            
            float minVal = 0;
            float maxVal = 100;
            
            if (values.Any())
            {
                minVal = values.Min();
                maxVal = values.Max();
                
                // Add some padding
                var range = maxVal - minVal;
                minVal -= range * 0.1f;
                maxVal += range * 0.1f;
            }
            
            // Assign color based on curve type
            var color = GetCurveColor(curve.Mnemonic);
            
            var track = new ParameterTrack
            {
                Name = string.IsNullOrEmpty(curve.Description) ? curve.Mnemonic : curve.Description,
                Unit = curve.Unit,
                MinValue = minVal,
                MaxValue = maxVal,
                IsLogarithmic = isLog,
                Color = color,
                IsVisible = true,
                Points = new List<ParameterPoint>()
            };
            
            dataset.ParameterTracks[curve.Mnemonic] = track;
        }
    }
    
    private void PopulateLogData(BoreholeDataset dataset, LASFile lasData)
    {
        var depthIndex = GetDepthCurveIndex(lasData);
        if (depthIndex < 0) return;
        
        // Get all depth values in original order
        var depthValues = lasData.DataRows
            .Where(row => row[depthIndex].HasValue)
            .Select(row => row[depthIndex].Value)
            .ToList();
        
        if (!depthValues.Any()) return;
        
        // Determine if depths are increasing or decreasing
        var firstDepth = depthValues.First();
        var lastDepth = depthValues.Last();
        var isDecreasing = firstDepth > lastDepth;
        
        // Reference depth is the first data point
        var referenceDepth = firstDepth;
        
        foreach (var row in lasData.DataRows)
        {
            // Get depth value
            if (!row[depthIndex].HasValue)
                continue;
            
            // Normalize depth so first data point is at 0
            var originalDepth = row[depthIndex].Value;
            float normalizedDepth;
            
            if (isDecreasing)
            {
                // Depths decrease: 1670 -> 1660, so 1670 becomes 0, 1660 becomes 10
                normalizedDepth = referenceDepth - originalDepth;
            }
            else
            {
                // Depths increase: 1660 -> 1670, so 1660 becomes 0, 1670 becomes 10
                normalizedDepth = originalDepth - referenceDepth;
            }
            
            // Add data point to each curve
            for (int i = 0; i < lasData.Curves.Count; i++)
            {
                if (i == depthIndex) continue; // Skip depth curve
                
                var curve = lasData.Curves[i];
                
                if (dataset.ParameterTracks.TryGetValue(curve.Mnemonic, out var track))
                {
                    if (row[i].HasValue)
                    {
                        track.Points.Add(new ParameterPoint
                        {
                            Depth = normalizedDepth,
                            Value = row[i].Value,
                            SourceDataset = dataset.Name
                        });
                    }
                }
            }
        }
    }
    
    private void EstimateLithology(BoreholeDataset dataset, LASFile lasData)
    {
        // Try to find gamma ray curve for lithology estimation
        var grCurve = lasData.Curves.FirstOrDefault(c => 
            c.Mnemonic.Equals("GR", StringComparison.OrdinalIgnoreCase) ||
            c.Mnemonic.Equals("GRD", StringComparison.OrdinalIgnoreCase) ||
            c.Mnemonic.Contains("GAMMA", StringComparison.OrdinalIgnoreCase));
        
        if (grCurve == null)
        {
            // No gamma ray - create a single unknown unit
            dataset.LithologyUnits.Add(new LithologyUnit
            {
                Name = "Unknown Formation",
                LithologyType = "Sandstone",
                DepthFrom = 0,
                DepthTo = dataset.TotalDepth,
                Color = new Vector4(0.8f, 0.8f, 0.7f, 1.0f),
                Description = "No lithology information available"
            });
            return;
        }
        
        // Get gamma ray track (already has normalized depths)
        if (!dataset.ParameterTracks.TryGetValue(grCurve.Mnemonic, out var grTrack))
            return;
        
        // Simple lithology estimation based on gamma ray values
        // Low GR (<60) = Sandstone, Medium GR (60-150) = Siltstone/Mixed, High GR (>150) = Shale
        var sortedPoints = grTrack.Points.OrderBy(p => p.Depth).ToList();
        
        if (sortedPoints.Count < 2) return;
        
        var lithUnits = new List<LithologyUnit>();
        string currentLitho = DetermineRockType(sortedPoints[0].Value);
        float startDepth = sortedPoints[0].Depth;
        
        for (int i = 1; i < sortedPoints.Count; i++)
        {
            var newLitho = DetermineRockType(sortedPoints[i].Value);
            
            if (newLitho != currentLitho)
            {
                // Create unit for previous lithology
                lithUnits.Add(new LithologyUnit
                {
                    Name = currentLitho,
                    LithologyType = currentLitho,
                    DepthFrom = startDepth,
                    DepthTo = sortedPoints[i].Depth,
                    Color = GetLithologyColor(currentLitho),
                    Description = $"Estimated from gamma ray log"
                });
                
                currentLitho = newLitho;
                startDepth = sortedPoints[i].Depth;
            }
        }
        
        // Add final unit
        lithUnits.Add(new LithologyUnit
        {
            Name = currentLitho,
            LithologyType = currentLitho,
            DepthFrom = startDepth,
            DepthTo = sortedPoints[^1].Depth,
            Color = GetLithologyColor(currentLitho),
            Description = $"Estimated from gamma ray log"
        });
        
        // Only use estimated lithology if we have reasonable units (not too many small units)
        if (lithUnits.Count < sortedPoints.Count / 10)
        {
            dataset.LithologyUnits.AddRange(lithUnits);
        }
        else
        {
            // Too many small units - just create one default unit
            dataset.LithologyUnits.Add(new LithologyUnit
            {
                Name = "Mixed Formation",
                LithologyType = "Sandstone",
                DepthFrom = 0,
                DepthTo = dataset.TotalDepth,
                Color = new Vector4(0.8f, 0.8f, 0.7f, 1.0f),
                Description = "Lithology varies - check gamma ray log"
            });
        }
    }
    
    private string DetermineRockType(float gammaRay)
    {
        if (gammaRay < 60)
            return "Sandstone";
        else if (gammaRay < 150)
            return "Siltstone";
        else
            return "Shale";
    }
    
    private Vector4 GetLithologyColor(string litho)
    {
        return litho switch
        {
            "Sandstone" => new Vector4(0.96f, 0.87f, 0.70f, 1.0f),  // Sandy yellow
            "Siltstone" => new Vector4(0.82f, 0.71f, 0.55f, 1.0f),  // Brown
            "Shale" => new Vector4(0.50f, 0.50f, 0.50f, 1.0f),      // Gray
            "Limestone" => new Vector4(0.68f, 0.85f, 0.90f, 1.0f),  // Light blue
            _ => new Vector4(0.8f, 0.8f, 0.8f, 1.0f)                // Default gray
        };
    }
    
    private int GetDepthCurveIndex(LASFile lasData)
    {
        for (int i = 0; i < lasData.Curves.Count; i++)
        {
            var mnemonic = lasData.Curves[i].Mnemonic;
            if (mnemonic.Equals("DEPT", StringComparison.OrdinalIgnoreCase) ||
                mnemonic.Equals("DEPTH", StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
    
    private bool IsLogarithmicCurve(string mnemonic)
    {
        var logCurves = new[] { "PERM", "RPERM", "ILD", "ILM", "MSFL", "LLD", "LLS", "RT", "RXO", "PERM" };
        return logCurves.Any(log => mnemonic.Contains(log, StringComparison.OrdinalIgnoreCase));
    }
    
    private Vector4 GetCurveColor(string mnemonic)
    {
        var upper = mnemonic.ToUpper();
        
        // Gamma ray - green
        if (upper.Contains("GR") || upper.Contains("GAMMA"))
            return new Vector4(0.2f, 0.8f, 0.2f, 1.0f);
        
        // Resistivity - red
        if (upper.Contains("RES") || upper.Contains("ILD") || upper.Contains("RT"))
            return new Vector4(0.9f, 0.2f, 0.2f, 1.0f);
        
        // Density - blue
        if (upper.Contains("RHOB") || upper.Contains("DEN"))
            return new Vector4(0.2f, 0.4f, 0.9f, 1.0f);
        
        // Neutron - cyan
        if (upper.Contains("NPHI") || upper.Contains("NEUT"))
            return new Vector4(0.2f, 0.8f, 0.9f, 1.0f);
        
        // Sonic - purple
        if (upper.Contains("DT") || upper.Contains("SONIC"))
            return new Vector4(0.7f, 0.2f, 0.8f, 1.0f);
        
        // Caliper - brown
        if (upper.Contains("CAL") || upper.Contains("CALI"))
            return new Vector4(0.6f, 0.4f, 0.2f, 1.0f);
        
        // Spontaneous potential - orange
        if (upper.Contains("SP"))
            return new Vector4(1.0f, 0.5f, 0.0f, 1.0f);
        
        // Porosity - light blue
        if (upper.Contains("PHIE") || upper.Contains("PORO"))
            return new Vector4(0.3f, 0.6f, 1.0f, 1.0f);
        
        // Default - gray
        return new Vector4(0.6f, 0.6f, 0.6f, 1.0f);
    }
}

/// <summary>
/// Represents a parsed LAS file structure
/// </summary>
public class LASFile
{
    public string Version { get; set; } = "2.0";
    public bool Wrap { get; set; } = false;
    
    public Dictionary<string, string> WellInfo { get; set; } = new();
    public Dictionary<string, string> WellUnits { get; set; } = new();
    public Dictionary<string, string> WellDescriptions { get; set; } = new();
    
    public List<LASCurve> Curves { get; set; } = new();
    
    public Dictionary<string, string> Parameters { get; set; } = new();
    public Dictionary<string, string> ParameterUnits { get; set; } = new();
    public Dictionary<string, string> ParameterDescriptions { get; set; } = new();
    
    public List<List<float?>> DataRows { get; set; } = new();
    
    public bool IsValid { get; set; }
    public string ValidationMessage { get; set; } = "";
}

/// <summary>
/// Represents a curve definition in a LAS file
/// </summary>
public class LASCurve
{
    public string Mnemonic { get; set; }
    public string Unit { get; set; }
    public string Description { get; set; }
}