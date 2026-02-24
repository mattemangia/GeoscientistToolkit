using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Util;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace GeoscientistToolkit.Data.Loaders;

public class StratiFixDatasetFolderLoader : IDataLoader
{
    private const string Epsg25833Wkt =
        "PROJCS[\"ETRS89 / UTM zone 33N\",GEOGCS[\"ETRS89\",DATUM[\"European_Terrestrial_Reference_System_1989\",SPHEROID[\"GRS 1980\",6378137,298.257222101,AUTHORITY[\"EPSG\",\"7019\"]],TOWGS84[0,0,0,0,0,0,0],AUTHORITY[\"EPSG\",\"6258\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4258\"]],UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",15],PARAMETER[\"scale_factor\",0.9996],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",0],AXIS[\"Easting\",EAST],AXIS[\"Northing\",NORTH],AUTHORITY[\"EPSG\",\"25833\"]]";

    private readonly CoordinateTransformationFactory _transformFactory = new();
    private readonly ICoordinateTransformation _wgs84ToEtrsUtm33;
    private readonly ICoordinateTransformation _etrsUtm33ToWgs84;

    private readonly List<Dataset> _generatedDatasets = new();

    public StratiFixDatasetFolderLoader()
    {
        var csFactory = new CoordinateSystemFactory();
        var wgs84 = GeographicCoordinateSystem.WGS84;
        var etrsUtm33 = csFactory.CreateFromWkt(Epsg25833Wkt);
        _wgs84ToEtrsUtm33 = _transformFactory.CreateFromCoordinateSystems(wgs84, etrsUtm33);
        _etrsUtm33ToWgs84 = _transformFactory.CreateFromCoordinateSystems(etrsUtm33, wgs84);
    }

    public string DatasetFolderPath { get; set; } = string.Empty;
    public bool ImportShapefiles { get; set; } = true;
    public bool ImportDemCache { get; set; } = true;

    public IReadOnlyList<Dataset> GeneratedDatasets => _generatedDatasets;

    public string Name => "StratiFix Dataset Folder";

    public string Description =>
        "Import a StratiFix dataset folder (Processed_Excel, structural JSON, shapefiles, DEM cache)";

    public bool CanImport => ResolveDatasetRoot(DatasetFolderPath, out _, out _);

    public string ValidationMessage => CanImport
        ? null
        : "Please select a valid StratiFix dataset folder (root or Processed_Excel).";

    public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
    {
        return await Task.Run(() => LoadInternal(progressReporter));
    }

    public void Reset()
    {
        DatasetFolderPath = string.Empty;
        ImportShapefiles = true;
        ImportDemCache = true;
        _generatedDatasets.Clear();
    }

    public StratiFixFolderInfo GetFolderInfo()
    {
        var info = new StratiFixFolderInfo
        {
            SelectedPath = DatasetFolderPath
        };

        if (!ResolveDatasetRoot(DatasetFolderPath, out var datasetRoot, out var processedDir))
            return info;

        info.IsValid = true;
        info.DatasetRoot = datasetRoot;
        info.ProcessedExcelPath = processedDir;

        try
        {
            var excelDirs = new List<string>();
            if (!string.IsNullOrEmpty(processedDir) && Directory.Exists(processedDir))
                excelDirs.Add(processedDir);
            if (Directory.Exists(datasetRoot))
                excelDirs.Add(datasetRoot);

            info.ExcelFiles = excelDirs
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .SelectMany(d => Directory.EnumerateFiles(d, "*.xlsx", SearchOption.TopDirectoryOnly))
                .Where(f => !Path.GetFileName(f).StartsWith("~$", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            foreach (var shpDir in GetShapefileDirectories(datasetRoot, processedDir))
            {
                info.Shapefiles += Directory.EnumerateFiles(shpDir, "*.shp", SearchOption.TopDirectoryOnly).Count();
            }

            info.HasStructuralData = FindFirstExistingFile(datasetRoot, processedDir, "structural_data.json", "faults.json") != null;
            info.HasDemCache = FindFirstExistingFile(datasetRoot, processedDir, "dem_cache.json") != null;
        }
        catch
        {
            info.IsValid = false;
        }

        return info;
    }

    private Dataset LoadInternal(IProgress<(float progress, string message)> progressReporter)
    {
        _generatedDatasets.Clear();

        progressReporter?.Report((0.02f, "Validating StratiFix dataset folder..."));

        if (!ResolveDatasetRoot(DatasetFolderPath, out var datasetRoot, out var processedDir))
            throw new DirectoryNotFoundException(
                "Invalid StratiFix dataset folder. Select the dataset root or its Processed_Excel subfolder.");

        var datasetName = new DirectoryInfo(datasetRoot).Name;
        var excelFiles = CollectExcelFiles(datasetRoot, processedDir);

        var records = new List<InvestigationRecord>();
        var boreholes = new List<BoreholeDataset>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        progressReporter?.Report((0.08f, $"Scanning Excel files ({excelFiles.Count})..."));

        for (var i = 0; i < excelFiles.Count; i++)
        {
            var excel = excelFiles[i];
            var progress = 0.08f + (0.46f * ((i + 1f) / Math.Max(1, excelFiles.Count)));

            progressReporter?.Report((progress, $"Parsing {Path.GetFileName(excel)} ({i + 1}/{excelFiles.Count})..."));

            if (!TryParseInvestigationFromExcel(excel, out var record))
                continue;

            records.Add(record);

            if (record.Stratigraphy.Count == 0)
                continue;

            var preferredName = !string.IsNullOrWhiteSpace(record.HoleId)
                ? record.HoleId
                : Path.GetFileNameWithoutExtension(excel);
            var boreholeName = MakeUniqueName(preferredName, usedNames);

            var borehole = CreateBoreholeDataset(boreholeName, excel, record);
            if (borehole != null)
                boreholes.Add(borehole);
        }

        progressReporter?.Report((0.60f, "Building StratiFix GIS dataset..."));

        var gisDataset = CreateMainGisDataset(datasetName, datasetRoot, processedDir, records, boreholes);

        progressReporter?.Report((0.75f, "Loading DEM cache (if available)..."));

        var demDataset = ImportDemCache
            ? TryCreateDemDataset(datasetName, datasetRoot, processedDir)
            : null;

        var children = new List<Dataset>();
        children.AddRange(boreholes);
        if (gisDataset != null)
            children.Add(gisDataset);
        if (demDataset != null)
            children.Add(demDataset);

        if (children.Count == 0)
            throw new InvalidDataException("No importable StratiFix datasets found in the selected folder.");

        var groupName = MakeUniqueName($"{datasetName}_StratiFix", usedNames);
        var group = new DatasetGroup(groupName, children);

        _generatedDatasets.AddRange(children);
        _generatedDatasets.Add(group);

        progressReporter?.Report((1.0f,
            $"StratiFix import complete: {boreholes.Count} boreholes, {(gisDataset != null ? 1 : 0)} GIS, {(demDataset != null ? 1 : 0)} DEM"));

        return group;
    }

    private static bool ResolveDatasetRoot(string selectedPath, out string datasetRoot, out string processedDir)
    {
        datasetRoot = string.Empty;
        processedDir = string.Empty;

        if (string.IsNullOrWhiteSpace(selectedPath))
            return false;

        var fullPath = Path.GetFullPath(selectedPath);
        if (!Directory.Exists(fullPath))
            return false;

        var dirName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (dirName.Equals("Processed_Excel", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Directory.GetParent(fullPath);
            if (parent != null && Directory.Exists(parent.FullName))
            {
                datasetRoot = parent.FullName;
                processedDir = fullPath;
                return true;
            }

            datasetRoot = fullPath;
            processedDir = fullPath;
            return true;
        }

        datasetRoot = fullPath;

        var directProcessed = Path.Combine(datasetRoot, "Processed_Excel");
        if (Directory.Exists(directProcessed))
            processedDir = directProcessed;

        var hasRootSignals =
            Directory.EnumerateFiles(datasetRoot, "*.xlsx", SearchOption.TopDirectoryOnly).Any() ||
            File.Exists(Path.Combine(datasetRoot, "structural_data.json")) ||
            File.Exists(Path.Combine(datasetRoot, "dem_cache.json"));

        if (!string.IsNullOrEmpty(processedDir) || hasRootSignals)
            return true;

        return false;
    }

    private static List<string> CollectExcelFiles(string datasetRoot, string processedDir)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(processedDir) && Directory.Exists(processedDir))
        {
            foreach (var file in Directory.EnumerateFiles(processedDir, "*.xlsx", SearchOption.TopDirectoryOnly))
            {
                if (!Path.GetFileName(file).StartsWith("~$", StringComparison.OrdinalIgnoreCase))
                    files.Add(file);
            }
        }

        if (Directory.Exists(datasetRoot))
        {
            foreach (var file in Directory.EnumerateFiles(datasetRoot, "*.xlsx", SearchOption.TopDirectoryOnly))
            {
                if (!Path.GetFileName(file).StartsWith("~$", StringComparison.OrdinalIgnoreCase))
                    files.Add(file);
            }
        }

        return files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private bool TryParseInvestigationFromExcel(string excelPath, out InvestigationRecord record)
    {
        record = new InvestigationRecord
        {
            SourceFile = excelPath,
            HoleId = Path.GetFileNameWithoutExtension(excelPath)
        };

        try
        {
            using var workbook = new XLWorkbook(excelPath);

            var collarSheet = FindWorksheet(workbook, "Collar", "Strater_Collar", "GeneralData");
            if (collarSheet != null)
                ParseCollarSheet(collarSheet, record);

            if (record.HoleId == Path.GetFileNameWithoutExtension(excelPath))
            {
                var fileHoleId = ExtractHoleIdFromFileName(excelPath);
                if (!string.IsNullOrWhiteSpace(fileHoleId))
                    record.HoleId = fileHoleId;
            }

            record.HasCptData = workbook.Worksheets.Any(ws =>
                ws.Name.Equals("CPT_Data", StringComparison.OrdinalIgnoreCase));

            ParseStratigraphy(workbook, record);

            return record.HasCoordinates || record.Stratigraphy.Count > 0;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[StratiFixDatasetFolderLoader] Failed to parse '{Path.GetFileName(excelPath)}': {ex.Message}");
            return false;
        }
    }

    private static string ExtractHoleIdFromFileName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);

        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        if (name.StartsWith("Borehole_", StringComparison.OrdinalIgnoreCase))
        {
            var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
                return parts[2];
        }

        if (name.StartsWith("WebMS_", StringComparison.OrdinalIgnoreCase))
        {
            var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return parts[1];
        }

        return name;
    }

    private static void ParseCollarSheet(IXLWorksheet sheet, InvestigationRecord record)
    {
        var usedRange = sheet.RangeUsed();
        if (usedRange == null)
            return;

        var headerMap = BuildHeaderMap(sheet, 1);
        if (headerMap.Count == 0)
            return;

        var dataRow = FindFirstDataRow(sheet, 2);
        if (dataRow == null)
            return;

        var idCol = FindColumn(headerMap, "holeid", "nome", "id", "siteid");
        var typeCol = FindColumn(headerMap, "type", "investigationtype", "investigationclass", "classe");

        if (idCol > 0)
        {
            var holeId = dataRow.Cell(idCol).GetString().Trim();
            if (!string.IsNullOrWhiteSpace(holeId))
                record.HoleId = holeId;
        }

        if (typeCol > 0)
        {
            var investigationType = dataRow.Cell(typeCol).GetString().Trim();
            if (!string.IsNullOrWhiteSpace(investigationType))
                record.InvestigationType = investigationType;
        }

        var lonCol = FindColumn(headerMap, "longitude", "lon", "lng");
        var latCol = FindColumn(headerMap, "latitude", "lat");
        var eastCol = FindColumn(headerMap, "easting", "x", "coordx");
        var northCol = FindColumn(headerMap, "northing", "y", "coordy");

        if (lonCol > 0 && latCol > 0 &&
            TryReadDouble(dataRow.Cell(lonCol), out var lon) &&
            TryReadDouble(dataRow.Cell(latCol), out var lat))
        {
            record.Longitude = lon;
            record.Latitude = lat;
        }

        if (eastCol > 0 && northCol > 0 &&
            TryReadDouble(dataRow.Cell(eastCol), out var east) &&
            TryReadDouble(dataRow.Cell(northCol), out var north))
        {
            record.Easting = east;
            record.Northing = north;
        }

        var elevationCol = FindColumn(headerMap, "elevation", "quota", "altitude", "z", "ground");
        if (elevationCol > 0 && TryReadDouble(dataRow.Cell(elevationCol), out var elevation))
            record.Elevation = elevation;

        var totalDepthCol = FindColumn(headerMap, "totaldepth", "endingdepth", "depth", "profondita");
        if (totalDepthCol > 0 && TryReadDouble(dataRow.Cell(totalDepthCol), out var totalDepth))
            record.TotalDepth = totalDepth;
    }

    private static void ParseStratigraphy(XLWorkbook workbook, InvestigationRecord record)
    {
        var prioritizedSheets = new[] { "Stratigraphy", "Lithology", "Strater_Lithology", "Strat_Interpretata", "L" };

        foreach (var sheetName in prioritizedSheets)
        {
            var sheet = workbook.Worksheets.FirstOrDefault(ws =>
                ws.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase));
            if (sheet == null)
                continue;

            var parsed = ParseStratigraphySheet(sheet, requireLithologySignals: false);
            if (parsed.Count > 0)
            {
                record.Stratigraphy.AddRange(parsed);
                break;
            }
        }

        if (record.Stratigraphy.Count > 0)
            return;

        List<StratiFixLayerRecord>? bestCandidate = null;
        foreach (var worksheet in workbook.Worksheets)
        {
            if (worksheet.Name.Equals("Collar", StringComparison.OrdinalIgnoreCase) ||
                worksheet.Name.Equals("Strater_Collar", StringComparison.OrdinalIgnoreCase) ||
                worksheet.Name.Equals("GeneralData", StringComparison.OrdinalIgnoreCase) ||
                worksheet.Name.Equals("ProprietaMisurate", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parsed = ParseStratigraphySheet(worksheet, requireLithologySignals: true);
            if (parsed.Count == 0)
                continue;

            if (bestCandidate == null || parsed.Count > bestCandidate.Count)
                bestCandidate = parsed;
        }

        if (bestCandidate != null && bestCandidate.Count > 0)
            record.Stratigraphy.AddRange(bestCandidate);
    }

    private static List<StratiFixLayerRecord> ParseStratigraphySheet(IXLWorksheet sheet, bool requireLithologySignals)
    {
        var layers = new List<StratiFixLayerRecord>();

        var usedRange = sheet.RangeUsed();
        if (usedRange == null)
            return layers;

        var headerRow = FindStratigraphyHeaderRow(sheet);
        if (headerRow <= 0)
            return layers;

        var headerMap = BuildHeaderMap(sheet, headerRow);
        if (headerMap.Count == 0)
            return layers;

        if (!TryFindDepthColumns(headerMap, out var depthFromCol, out var depthToCol))
            return layers;

        var lithologyCol = FindColumn(headerMap,
            "chiavelitologia", "lithologykeyword", "lithologycode", "lithology", "sbtclass",
            "litologia", "codicelitologico", "classelitologica", "valore");
        var descriptionCol = FindColumn(headerMap,
            "descrizione", "description", "lithologyname", "sbtdescription", "litologia", "note");
        var parameterTypeCol = FindColumn(headerMap,
            "tipologiadelparametro", "tipoparametro", "parametertype", "parameterclass");

        if (requireLithologySignals)
        {
            var sheetLooksLithology = SheetLooksLithology(sheet.Name);
            var hasLithologyParameterType = parameterTypeCol > 0 &&
                                            SheetContainsLithologyType(sheet, parameterTypeCol, headerRow + 1);

            if (!sheetLooksLithology && !hasLithologyParameterType)
                return layers;
        }

        var parameterColumns = new Dictionary<string, int>
        {
            ["S-Wave Velocity"] = FindColumn(headerMap, "vs", "vsms", "velocitaondesms", "velocitaondes"),
            ["P-Wave Velocity"] = FindColumn(headerMap, "vp", "vpms", "velocitaondepms", "velocitaondep"),
            ["Density"] = FindColumn(headerMap, "densita", "density", "densitakgm3"),
            ["Porosity"] = FindColumn(headerMap, "porosita", "porosity", "porositafraz"),
            ["Permeability"] = FindColumn(headerMap, "permeabilita", "permeability", "permeabilitams"),
            ["Thermal Conductivity"] = FindColumn(headerMap, "lambda", "lambdawmk", "conducibilita", "conducibilitatermica"),
            ["Friction Angle"] = FindColumn(headerMap, "frictionangle", "phi"),
            ["Cohesion"] = FindColumn(headerMap, "cohesion", "cu", "c"),
            ["NSPT"] = FindColumn(headerMap, "nspt", "nss", "nsm"),
            ["Shear Modulus"] = FindColumn(headerMap, "shearmodulus", "g"),
            ["Young's Modulus"] = FindColumn(headerMap, "youngmodulus", "e"),
            ["Poisson's Ratio"] = FindColumn(headerMap, "poisson", "nu"),
            ["Resonance Frequency"] = FindColumn(headerMap, "resonancefrequency", "fo")
        };

        var lastRow = usedRange.LastRow().RowNumber();
        for (var rowIndex = headerRow + 1; rowIndex <= lastRow; rowIndex++)
        {
            var row = sheet.Row(rowIndex);

            if (parameterTypeCol > 0)
            {
                var parameterType = NormalizeHeader(row.Cell(parameterTypeCol).GetString());
                if (!string.IsNullOrWhiteSpace(parameterType) && !IsLithologyParameterType(parameterType))
                    continue;
            }

            if (!TryReadDouble(row.Cell(depthFromCol), out var depthFrom) ||
                !TryReadDouble(row.Cell(depthToCol), out var depthTo))
                continue;

            if (depthTo <= depthFrom)
                continue;

            var lithology = lithologyCol > 0
                ? row.Cell(lithologyCol).GetString().Trim()
                : string.Empty;

            var description = descriptionCol > 0
                ? row.Cell(descriptionCol).GetString().Trim()
                : string.Empty;

            if (string.IsNullOrWhiteSpace(lithology) && string.IsNullOrWhiteSpace(description))
                lithology = "Unknown";

            var layer = new StratiFixLayerRecord
            {
                DepthFrom = depthFrom,
                DepthTo = depthTo,
                Lithology = string.IsNullOrWhiteSpace(lithology) ? description : lithology,
                Description = description
            };

            foreach (var kvp in parameterColumns)
            {
                if (kvp.Value <= 0)
                    continue;

                if (TryReadDouble(row.Cell(kvp.Value), out var value))
                    layer.Parameters[kvp.Key] = (float)value;
            }

            layers.Add(layer);
        }

        return layers;
    }

    private static int FindStratigraphyHeaderRow(IXLWorksheet sheet)
    {
        var usedRange = sheet.RangeUsed();
        if (usedRange == null)
            return -1;

        var lastCandidateRow = Math.Min(8, usedRange.LastRow().RowNumber());
        for (var row = 1; row <= lastCandidateRow; row++)
        {
            var headerMap = BuildHeaderMap(sheet, row);
            if (headerMap.Count == 0)
                continue;

            if (TryFindDepthColumns(headerMap, out _, out _))
                return row;
        }

        return -1;
    }

    private static bool TryFindDepthColumns(Dictionary<string, int> headerMap, out int depthFromCol, out int depthToCol)
    {
        depthFromCol = FindColumn(headerMap,
            "da", "from", "depthfrom", "depthstart", "depthfromm", "depthfrommeter", "depthfrommtr",
            "profonditadeltop", "profonditadeltopm", "profonditadainizio", "profonditainiziale", "profonditadatop");
        depthToCol = FindColumn(headerMap,
            "a", "to", "depthto", "depthend", "depthtom", "depthtometer", "depthtomtr",
            "profonditadelbottom", "profonditadelbottomm", "profonditafinale", "profonditaabottom", "profonditaafine");

        if (depthFromCol > 0 && depthToCol > 0)
            return true;

        depthFromCol = FindColumn(headerMap, "depthfrom", "from", "profonditatop");
        depthToCol = FindColumn(headerMap, "depthto", "to", "profonditabottom");
        if (depthFromCol > 0 && depthToCol > 0)
            return true;

        depthFromCol = FindColumnContaining(headerMap,
            "depthfrom", "profonditadeltop", "profonditainizi", "profonditada");
        depthToCol = FindColumnContaining(headerMap,
            "depthto", "profonditadelbottom", "profonditafinal", "profonditaa");
        return depthFromCol > 0 && depthToCol > 0;
    }

    private static int FindColumnContaining(Dictionary<string, int> headerMap, params string[] fragments)
    {
        foreach (var fragment in fragments)
        {
            var normalized = NormalizeHeader(fragment);
            if (string.IsNullOrWhiteSpace(normalized))
                continue;

            foreach (var kvp in headerMap)
            {
                if (kvp.Key.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }
        }

        return -1;
    }

    private static bool SheetLooksLithology(string sheetName)
    {
        var normalized = NormalizeHeader(sheetName);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        return normalized == "l" ||
               normalized.Contains("lith") ||
               normalized.Contains("lito") ||
               normalized.Contains("strat");
    }

    private static bool SheetContainsLithologyType(IXLWorksheet sheet, int parameterTypeCol, int startRow)
    {
        var usedRange = sheet.RangeUsed();
        if (usedRange == null)
            return false;

        var lastRow = Math.Min(usedRange.LastRow().RowNumber(), startRow + 100);
        for (var row = startRow; row <= lastRow; row++)
        {
            var value = NormalizeHeader(sheet.Cell(row, parameterTypeCol).GetString());
            if (IsLithologyParameterType(value))
                return true;
        }

        return false;
    }

    private static bool IsLithologyParameterType(string normalizedValue)
    {
        if (string.IsNullOrWhiteSpace(normalizedValue))
            return false;

        return normalizedValue == "l" ||
               normalizedValue == "litologia" ||
               normalizedValue == "lithology" ||
               normalizedValue.StartsWith("litolog", StringComparison.OrdinalIgnoreCase) ||
               normalizedValue.StartsWith("litholog", StringComparison.OrdinalIgnoreCase);
    }

    private BoreholeDataset? CreateBoreholeDataset(string datasetName, string sourceExcel, InvestigationRecord record)
    {
        if (record.Stratigraphy.Count == 0)
            return null;

        if (!TryResolveProjectedCoordinates(record, out var x, out var y, out var lon, out var lat))
            return null;

        var dataset = BoreholeDataset.CreateEmpty(datasetName, string.Empty);
        dataset.WellName = datasetName;
        dataset.Field = "StratiFix";
        dataset.SurfaceCoordinates = new Vector2((float)x, (float)y);
        dataset.Elevation = (float)(record.Elevation ?? 0.0);

        foreach (var srcLayer in record.Stratigraphy.OrderBy(l => l.DepthFrom))
        {
            var unit = new LithologyUnit
            {
                Name = string.IsNullOrWhiteSpace(srcLayer.Lithology) ? "Unknown" : srcLayer.Lithology,
                LithologyType = string.IsNullOrWhiteSpace(srcLayer.Lithology) ? "Unknown" : srcLayer.Lithology,
                DepthFrom = (float)srcLayer.DepthFrom,
                DepthTo = (float)srcLayer.DepthTo,
                Color = ColorFromText(srcLayer.Lithology),
                Description = srcLayer.Description ?? string.Empty,
                Parameters = new Dictionary<string, float>(srcLayer.Parameters)
            };

            dataset.AddLithologyUnit(unit);
        }

        var maxDepth = record.TotalDepth ?? record.Stratigraphy.Max(s => s.DepthTo);
        dataset.TotalDepth = (float)Math.Max(0.0, maxDepth);

        dataset.DatasetMetadata.SampleName = datasetName;
        dataset.DatasetMetadata.LocationName = "StratiFix";
        dataset.DatasetMetadata.Coordinates = dataset.SurfaceCoordinates;
        dataset.DatasetMetadata.Latitude = lat;
        dataset.DatasetMetadata.Longitude = lon;
        dataset.DatasetMetadata.Depth = dataset.TotalDepth;
        dataset.DatasetMetadata.Elevation = dataset.Elevation;
        dataset.DatasetMetadata.CustomFields["ImportedFrom"] = "StratiFix";
        dataset.DatasetMetadata.CustomFields["SourceExcel"] = Path.GetFileName(sourceExcel);
        if (!string.IsNullOrWhiteSpace(record.InvestigationType))
            dataset.DatasetMetadata.CustomFields["InvestigationType"] = record.InvestigationType;

        EnsureParameterTracks(dataset);
        dataset.SyncMetadata();

        return dataset;
    }

    private static void EnsureParameterTracks(BoreholeDataset dataset)
    {
        foreach (var track in dataset.ParameterTracks.Values)
            track.Points.Clear();

        foreach (var unit in dataset.LithologyUnits)
        {
            foreach (var parameter in unit.Parameters)
            {
                if (!dataset.ParameterTracks.TryGetValue(parameter.Key, out var track))
                {
                    track = new ParameterTrack
                    {
                        Name = parameter.Key,
                        Unit = GuessParameterUnit(parameter.Key),
                        Color = ColorFromText(parameter.Key),
                        MinValue = parameter.Value,
                        MaxValue = parameter.Value,
                        IsLogarithmic = parameter.Key.Equals("Permeability", StringComparison.OrdinalIgnoreCase)
                    };
                    dataset.ParameterTracks[parameter.Key] = track;
                }

                track.Points.Add(new ParameterPoint
                {
                    Depth = unit.DepthFrom,
                    Value = parameter.Value,
                    SourceDataset = "StratiFix"
                });

                track.Points.Add(new ParameterPoint
                {
                    Depth = unit.DepthTo,
                    Value = parameter.Value,
                    SourceDataset = "StratiFix"
                });
            }
        }

        foreach (var track in dataset.ParameterTracks.Values)
        {
            if (track.Points.Count == 0)
                continue;

            track.Points.Sort((a, b) => a.Depth.CompareTo(b.Depth));
            track.MinValue = track.Points.Min(p => p.Value);
            track.MaxValue = track.Points.Max(p => p.Value);

            if (Math.Abs(track.MaxValue - track.MinValue) < 1e-6f)
                track.MaxValue = track.MinValue + 1f;
        }
    }

    private GISDataset CreateMainGisDataset(
        string datasetName,
        string datasetRoot,
        string processedDir,
        List<InvestigationRecord> records,
        List<BoreholeDataset> boreholes)
    {
        var gis = new GISDataset($"{datasetName}_GIS", string.Empty)
        {
            Projection = new GISProjection
            {
                EPSG = "EPSG:25833",
                Name = "ETRS89 / UTM zone 33N",
                Type = ProjectionType.Projected
            }
        };

        gis.Layers.Clear();
        gis.SetGeoreference("EPSG:25833", "ETRS89 / UTM zone 33N");
        gis.AddTag(GISTag.VectorData);

        AddInvestigationLayers(gis, records, boreholes);
        AddStructuralLayers(gis, datasetRoot, processedDir);
        AddSeismicLinesLayer(gis, datasetRoot, processedDir);

        if (ImportShapefiles)
            AddShapefileLayers(gis, datasetRoot, processedDir);

        if (gis.Layers.Count == 0)
        {
            gis.Layers.Add(new GISLayer
            {
                Name = "Empty",
                Type = LayerType.Vector,
                IsVisible = true,
                IsEditable = true,
                Color = new Vector4(0.25f, 0.5f, 0.9f, 1f)
            });
        }

        gis.UpdateBounds();

        return gis;
    }

    private void AddInvestigationLayers(GISDataset gis, List<InvestigationRecord> records, List<BoreholeDataset> boreholes)
    {
        var boreholeLayer = new GISLayer
        {
            Name = "Boreholes_CPT",
            Type = LayerType.Vector,
            IsVisible = true,
            IsEditable = false,
            Color = new Vector4(0.2f, 0.85f, 0.25f, 1f),
            PointSize = 6f
        };

        foreach (var borehole in boreholes)
        {
            var feature = new GISFeature
            {
                Type = FeatureType.Point,
                Coordinates = new List<Vector2> { borehole.SurfaceCoordinates },
                Properties = new Dictionary<string, object>
                {
                    ["id"] = borehole.WellName,
                    ["type"] = "Borehole/CPT",
                    ["has_stratigraphy"] = true,
                    ["depth_m"] = borehole.TotalDepth,
                    ["elevation_m"] = borehole.Elevation
                }
            };

            boreholeLayer.Features.Add(feature);
        }

        if (boreholeLayer.Features.Count > 0)
            gis.Layers.Add(boreholeLayer);

        var otherInvestigationsLayer = new GISLayer
        {
            Name = "Investigations",
            Type = LayerType.Vector,
            IsVisible = true,
            IsEditable = false,
            Color = new Vector4(0.95f, 0.72f, 0.2f, 1f),
            PointSize = 5f
        };

        foreach (var record in records.Where(r => r.Stratigraphy.Count == 0))
        {
            if (!TryResolveProjectedCoordinates(record, out var x, out var y, out _, out _))
                continue;

            var feature = new GISFeature
            {
                Type = FeatureType.Point,
                Coordinates = new List<Vector2> { new Vector2((float)x, (float)y) },
                Properties = new Dictionary<string, object>
                {
                    ["id"] = record.HoleId,
                    ["type"] = record.HasCptData ? "CPT" : "Investigation",
                    ["has_stratigraphy"] = false,
                    ["source_file"] = Path.GetFileName(record.SourceFile),
                    ["investigation_type"] = record.InvestigationType ?? string.Empty
                }
            };

            if (record.Elevation.HasValue)
                feature.Properties["elevation_m"] = record.Elevation.Value;

            if (record.TotalDepth.HasValue)
                feature.Properties["depth_m"] = record.TotalDepth.Value;

            otherInvestigationsLayer.Features.Add(feature);
        }

        if (otherInvestigationsLayer.Features.Count > 0)
            gis.Layers.Add(otherInvestigationsLayer);
    }

    private void AddShapefileLayers(GISDataset gis, string datasetRoot, string processedDir)
    {
        foreach (var shpDir in GetShapefileDirectories(datasetRoot, processedDir))
        {
            foreach (var shpPath in Directory.EnumerateFiles(shpDir, "*.shp", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var imported = new GISDataset(Path.GetFileNameWithoutExtension(shpPath), shpPath);
                    imported.Load();

                    foreach (var importedLayer in imported.Layers.Where(l => l.Type == LayerType.Vector))
                    {
                        var clonedLayer = CloneVectorLayer(importedLayer,
                            $"{Path.GetFileNameWithoutExtension(shpPath)}_{importedLayer.Name}");

                        var importedIsGeographic =
                            imported.Projection != null &&
                            imported.Projection.EPSG != null &&
                            imported.Projection.EPSG.Equals("EPSG:4326", StringComparison.OrdinalIgnoreCase);

                        if (importedIsGeographic || LayerLooksGeographic(clonedLayer))
                            ReprojectLayerToEtrs(clonedLayer);

                        if (clonedLayer.Features.Count > 0)
                            gis.Layers.Add(clonedLayer);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"[StratiFixDatasetFolderLoader] Failed shapefile import '{Path.GetFileName(shpPath)}': {ex.Message}");
                }
            }
        }
    }

    private static IEnumerable<string> GetShapefileDirectories(string datasetRoot, string processedDir)
    {
        var dirs = new List<string>();

        var processedShp = string.IsNullOrWhiteSpace(processedDir)
            ? string.Empty
            : Path.Combine(processedDir, "Shapefiles");

        if (!string.IsNullOrWhiteSpace(processedShp) && Directory.Exists(processedShp))
            dirs.Add(processedShp);

        var rootShp = Path.Combine(datasetRoot, "Shapefiles");
        if (Directory.Exists(rootShp))
            dirs.Add(rootShp);

        if (!string.IsNullOrWhiteSpace(processedDir) && Directory.Exists(processedDir))
            dirs.Add(processedDir);

        if (Directory.Exists(datasetRoot))
            dirs.Add(datasetRoot);

        return dirs.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private void AddStructuralLayers(GISDataset gis, string datasetRoot, string processedDir)
    {
        var structuralPath = FindFirstExistingFile(datasetRoot, processedDir, "structural_data.json", "faults.json");
        if (string.IsNullOrEmpty(structuralPath))
            return;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(structuralPath));
            var root = doc.RootElement;

            var faultLayer = new GISLayer
            {
                Name = "Faults",
                Type = LayerType.Vector,
                IsVisible = true,
                IsEditable = false,
                Color = new Vector4(0.95f, 0.25f, 0.22f, 1f),
                LineWidth = 2.2f
            };

            if (TryGetPropertyIgnoreCase(root, "Faults", out var faultsElement) &&
                faultsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var fault in faultsElement.EnumerateArray())
                {
                    if (!TryGetPropertyIgnoreCase(fault, "Points", out var pointsElement) ||
                        pointsElement.ValueKind != JsonValueKind.Array)
                        continue;

                    var coords = new List<Vector2>();
                    foreach (var point in pointsElement.EnumerateArray())
                    {
                        if (!TryReadGeoPointFromJson(point, out var x, out var y))
                            continue;

                        var projected = NormalizeToEtrs(x, y);
                        coords.Add(new Vector2((float)projected.X, (float)projected.Y));
                    }

                    if (coords.Count < 2)
                        continue;

                    var feature = new GISFeature
                    {
                        Type = FeatureType.Line,
                        Coordinates = coords,
                        Properties = new Dictionary<string, object>()
                    };

                    if (TryGetPropertyIgnoreCase(fault, "Source", out var source) && source.ValueKind == JsonValueKind.String)
                        feature.Properties["source"] = source.GetString() ?? string.Empty;

                    if (TryGetPropertyIgnoreCase(fault, "TypeCode", out var typeCode))
                        feature.Properties["type_code"] = typeCode.ToString();

                    if (TryGetPropertyIgnoreCase(fault, "DipAngle", out var dipAngle) && dipAngle.ValueKind == JsonValueKind.Number)
                        feature.Properties["dip_angle"] = dipAngle.GetDouble();

                    if (TryGetPropertyIgnoreCase(fault, "DipDirection", out var dipDirection) && dipDirection.ValueKind == JsonValueKind.Number)
                        feature.Properties["dip_direction"] = dipDirection.GetDouble();

                    faultLayer.Features.Add(feature);
                }
            }

            if (faultLayer.Features.Count > 0)
                gis.Layers.Add(faultLayer);

            var dipLayer = new GISLayer
            {
                Name = "Dips",
                Type = LayerType.Vector,
                IsVisible = true,
                IsEditable = false,
                Color = new Vector4(0.18f, 0.72f, 0.98f, 1f),
                PointSize = 5.5f
            };

            if (TryGetPropertyIgnoreCase(root, "Dips", out var dipsElement) &&
                dipsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var dip in dipsElement.EnumerateArray())
                {
                    if (!TryReadGeoPointFromJson(dip, out var x, out var y))
                        continue;

                    var projected = NormalizeToEtrs(x, y);

                    var feature = new GISFeature
                    {
                        Type = FeatureType.Point,
                        Coordinates = new List<Vector2> { new Vector2((float)projected.X, (float)projected.Y) },
                        Properties = new Dictionary<string, object>()
                    };

                    if (TryGetPropertyIgnoreCase(dip, "Source", out var source) && source.ValueKind == JsonValueKind.String)
                        feature.Properties["source"] = source.GetString() ?? string.Empty;

                    if (TryGetPropertyIgnoreCase(dip, "TypeCode", out var typeCode))
                        feature.Properties["type_code"] = typeCode.ToString();

                    if (TryGetPropertyIgnoreCase(dip, "DipAngle", out var dipAngle) && dipAngle.ValueKind == JsonValueKind.Number)
                        feature.Properties["dip_angle"] = dipAngle.GetDouble();

                    if (TryGetPropertyIgnoreCase(dip, "DipDirection", out var dipDirection) && dipDirection.ValueKind == JsonValueKind.Number)
                        feature.Properties["dip_direction"] = dipDirection.GetDouble();

                    dipLayer.Features.Add(feature);
                }
            }

            if (dipLayer.Features.Count > 0)
                gis.Layers.Add(dipLayer);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[StratiFixDatasetFolderLoader] Failed structural_data import: {ex.Message}");
        }
    }

    private void AddSeismicLinesLayer(GISDataset gis, string datasetRoot, string processedDir)
    {
        var seismicPath = FindFirstExistingFile(datasetRoot, processedDir, "seismic_lines.json");
        if (string.IsNullOrEmpty(seismicPath))
            return;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(seismicPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return;

            var layer = new GISLayer
            {
                Name = "Seismic_Lines",
                Type = LayerType.Vector,
                IsVisible = true,
                IsEditable = false,
                Color = new Vector4(0.64f, 0.42f, 0.96f, 1f),
                LineWidth = 1.8f
            };

            foreach (var line in doc.RootElement.EnumerateArray())
            {
                if (!TryGetPropertyIgnoreCase(line, "Points", out var pointsElement) ||
                    pointsElement.ValueKind != JsonValueKind.Array)
                    continue;

                var coords = new List<Vector2>();
                foreach (var point in pointsElement.EnumerateArray())
                {
                    if (!TryReadGeoPointFromJson(point, out var x, out var y))
                        continue;

                    var projected = NormalizeToEtrs(x, y);
                    coords.Add(new Vector2((float)projected.X, (float)projected.Y));
                }

                if (coords.Count < 2)
                    continue;

                var feature = new GISFeature
                {
                    Type = FeatureType.Line,
                    Coordinates = coords,
                    Properties = new Dictionary<string, object>()
                };

                if (TryGetPropertyIgnoreCase(line, "Name", out var nameElement) &&
                    nameElement.ValueKind == JsonValueKind.String)
                    feature.Properties["name"] = nameElement.GetString() ?? string.Empty;

                if (TryGetPropertyIgnoreCase(line, "Source", out var sourceElement) &&
                    sourceElement.ValueKind == JsonValueKind.String)
                    feature.Properties["source"] = sourceElement.GetString() ?? string.Empty;

                layer.Features.Add(feature);
            }

            if (layer.Features.Count > 0)
                gis.Layers.Add(layer);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[StratiFixDatasetFolderLoader] Failed seismic_lines import: {ex.Message}");
        }
    }

    private GISDataset? TryCreateDemDataset(string datasetName, string datasetRoot, string processedDir)
    {
        var demPath = FindFirstExistingFile(datasetRoot, processedDir, "dem_cache.json");
        if (string.IsNullOrEmpty(demPath))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(demPath));
            var root = doc.RootElement;

            if (!TryGetPropertyIgnoreCase(root, "Points", out var points) ||
                points.ValueKind != JsonValueKind.Array)
                return null;

            var demDataset = new GISDataset($"{datasetName}_DEM", demPath)
            {
                Projection = new GISProjection
                {
                    EPSG = "EPSG:25833",
                    Name = "ETRS89 / UTM zone 33N",
                    Type = ProjectionType.Projected
                }
            };

            demDataset.Layers.Clear();

            var layer = new GISLayer
            {
                Name = "DEM Points",
                Type = LayerType.Vector,
                IsVisible = true,
                IsEditable = false,
                Color = new Vector4(0.5f, 0.85f, 0.35f, 1f),
                PointSize = 4.0f
            };

            foreach (var point in points.EnumerateArray())
            {
                if (!TryGetPropertyIgnoreCase(point, "Latitude", out var latElement) ||
                    !TryGetPropertyIgnoreCase(point, "Longitude", out var lonElement) ||
                    !TryGetPropertyIgnoreCase(point, "Elevation", out var elevElement) ||
                    latElement.ValueKind != JsonValueKind.Number ||
                    lonElement.ValueKind != JsonValueKind.Number ||
                    elevElement.ValueKind != JsonValueKind.Number)
                    continue;

                var lat = latElement.GetDouble();
                var lon = lonElement.GetDouble();
                var elevation = elevElement.GetDouble();

                var projected = NormalizeToEtrs(lon, lat);

                var feature = new GISFeature
                {
                    Type = FeatureType.Point,
                    Coordinates = new List<Vector2> { new Vector2((float)projected.X, (float)projected.Y) },
                    Properties = new Dictionary<string, object>
                    {
                        ["elevation_m"] = elevation,
                        ["longitude"] = lon,
                        ["latitude"] = lat
                    }
                };

                layer.Features.Add(feature);
            }

            if (layer.Features.Count == 0)
                return null;

            demDataset.Layers.Add(layer);
            demDataset.SetGeoreference("EPSG:25833", "ETRS89 / UTM zone 33N");
            demDataset.AddTag(GISTag.VectorData);
            demDataset.AddTag(GISTag.DEM);
            demDataset.AddTag(GISTag.Topography);
            demDataset.UpdateBounds();

            return demDataset;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[StratiFixDatasetFolderLoader] Failed dem_cache import: {ex.Message}");
            return null;
        }
    }

    private static string? FindFirstExistingFile(string datasetRoot, string processedDir, params string[] names)
    {
        foreach (var name in names)
        {
            var rootPath = Path.Combine(datasetRoot, name);
            if (File.Exists(rootPath))
                return rootPath;

            if (!string.IsNullOrEmpty(processedDir))
            {
                var processedPath = Path.Combine(processedDir, name);
                if (File.Exists(processedPath))
                    return processedPath;
            }
        }

        return null;
    }

    private static IXLWorksheet? FindWorksheet(XLWorkbook workbook, params string[] names)
    {
        foreach (var name in names)
        {
            var sheet = workbook.Worksheets.FirstOrDefault(ws =>
                ws.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (sheet != null)
                return sheet;
        }

        return null;
    }

    private static IXLRow? FindFirstDataRow(IXLWorksheet sheet, int startRow)
    {
        var usedRange = sheet.RangeUsed();
        if (usedRange == null)
            return null;

        var lastRow = usedRange.LastRow().RowNumber();
        for (var row = startRow; row <= lastRow; row++)
        {
            var dataRow = sheet.Row(row);
            if (dataRow.CellsUsed().Any(cell => !string.IsNullOrWhiteSpace(cell.GetString())))
                return dataRow;
        }

        return null;
    }

    private static Dictionary<string, int> BuildHeaderMap(IXLWorksheet sheet, int headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var usedRange = sheet.RangeUsed();
        if (usedRange == null)
            return map;

        var lastCol = usedRange.LastColumn().ColumnNumber();
        for (var col = 1; col <= lastCol; col++)
        {
            var raw = sheet.Cell(headerRow, col).GetString().Trim();
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var normalized = NormalizeHeader(raw);
            if (!string.IsNullOrWhiteSpace(normalized) && !map.ContainsKey(normalized))
                map[normalized] = col;
        }

        return map;
    }

    private static int FindColumn(Dictionary<string, int> headerMap, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var normalized = NormalizeHeader(candidate);
            if (headerMap.TryGetValue(normalized, out var col))
                return col;
        }

        return -1;
    }

    private static string NormalizeHeader(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var normalizedInput = input.Normalize(NormalizationForm.FormD);
        Span<char> buffer = stackalloc char[normalizedInput.Length];
        var idx = 0;

        foreach (var ch in normalizedInput.ToLowerInvariant())
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(ch))
                buffer[idx++] = ch;
        }

        return new string(buffer.Slice(0, idx));
    }

    private static bool TryReadDouble(IXLCell cell, out double value)
    {
        value = 0;

        if (cell == null)
            return false;

        if (cell.DataType == XLDataType.Number)
        {
            value = cell.GetDouble();
            return true;
        }

        var text = cell.GetString();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
               double.TryParse(text, NumberStyles.Float, CultureInfo.GetCultureInfo("it-IT"), out value);
    }

    private bool TryResolveProjectedCoordinates(
        InvestigationRecord record,
        out double x,
        out double y,
        out double lon,
        out double lat)
    {
        x = 0;
        y = 0;
        lon = 0;
        lat = 0;

        if (record.Easting.HasValue && record.Northing.HasValue)
        {
            x = record.Easting.Value;
            y = record.Northing.Value;
            if (LooksLikeGeographic(x, y))
            {
                var projected = NormalizeToEtrs(x, y);
                x = projected.X;
                y = projected.Y;
                lon = projected.Lon;
                lat = projected.Lat;
                return true;
            }

            var geographic = InverseFromEtrs(x, y);
            lon = geographic.Lon;
            lat = geographic.Lat;
            return true;
        }

        if (record.Longitude.HasValue && record.Latitude.HasValue)
        {
            lon = record.Longitude.Value;
            lat = record.Latitude.Value;

            var projected = NormalizeToEtrs(lon, lat);
            x = projected.X;
            y = projected.Y;
            lon = projected.Lon;
            lat = projected.Lat;
            return true;
        }

        return false;
    }

    private (double X, double Y, double Lon, double Lat) NormalizeToEtrs(double xOrLon, double yOrLat)
    {
        if (LooksLikeGeographic(xOrLon, yOrLat))
        {
            var projected = _wgs84ToEtrsUtm33.MathTransform.Transform(new[] { xOrLon, yOrLat });
            return (projected[0], projected[1], xOrLon, yOrLat);
        }

        var geographic = _etrsUtm33ToWgs84.MathTransform.Transform(new[] { xOrLon, yOrLat });
        return (xOrLon, yOrLat, geographic[0], geographic[1]);
    }

    private (double Lon, double Lat) InverseFromEtrs(double x, double y)
    {
        if (LooksLikeGeographic(x, y))
            return (x, y);

        var geographic = _etrsUtm33ToWgs84.MathTransform.Transform(new[] { x, y });
        return (geographic[0], geographic[1]);
    }

    private static bool LooksLikeGeographic(double x, double y)
    {
        return Math.Abs(x) <= 180 && Math.Abs(y) <= 90;
    }

    private static bool TryReadGeoPointFromJson(JsonElement element, out double x, out double y)
    {
        x = 0;
        y = 0;

        if (TryGetPropertyIgnoreCase(element, "Longitude", out var lonElement) &&
            TryGetPropertyIgnoreCase(element, "Latitude", out var latElement) &&
            lonElement.ValueKind == JsonValueKind.Number &&
            latElement.ValueKind == JsonValueKind.Number)
        {
            x = lonElement.GetDouble();
            y = latElement.GetDouble();
            return true;
        }

        if (TryGetPropertyIgnoreCase(element, "X", out var xElement) &&
            TryGetPropertyIgnoreCase(element, "Y", out var yElement) &&
            xElement.ValueKind == JsonValueKind.Number &&
            yElement.ValueKind == JsonValueKind.Number)
        {
            x = xElement.GetDouble();
            y = yElement.GetDouble();
            return true;
        }

        return false;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        if (obj.TryGetProperty(name, out value))
            return true;

        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static GISLayer CloneVectorLayer(GISLayer source, string name)
    {
        var clone = new GISLayer
        {
            Name = name,
            Type = LayerType.Vector,
            IsVisible = source.IsVisible,
            IsEditable = false,
            Color = source.Color,
            LineWidth = source.LineWidth,
            PointSize = source.PointSize
        };

        foreach (var feature in source.Features)
            clone.Features.Add(feature.Clone());

        foreach (var kvp in source.Properties)
            clone.Properties[kvp.Key] = kvp.Value;

        return clone;
    }

    private static bool LayerLooksGeographic(GISLayer layer)
    {
        var sampleCoords = layer.Features
            .SelectMany(f => f.Coordinates)
            .Take(50)
            .ToList();

        if (sampleCoords.Count == 0)
            return false;

        var geographicCount = sampleCoords.Count(c => Math.Abs(c.X) <= 180 && Math.Abs(c.Y) <= 90);
        return geographicCount >= (int)(sampleCoords.Count * 0.8f);
    }

    private void ReprojectLayerToEtrs(GISLayer layer)
    {
        foreach (var feature in layer.Features)
        {
            for (var i = 0; i < feature.Coordinates.Count; i++)
            {
                var coord = feature.Coordinates[i];
                if (!LooksLikeGeographic(coord.X, coord.Y))
                    continue;

                var projected = _wgs84ToEtrsUtm33.MathTransform.Transform(new[] { (double)coord.X, (double)coord.Y });
                feature.Coordinates[i] = new Vector2((float)projected[0], (float)projected[1]);
            }
        }
    }

    private static Vector4 ColorFromText(string text)
    {
        var normalized = string.IsNullOrWhiteSpace(text) ? "unknown" : text.Trim().ToLowerInvariant();

        unchecked
        {
            uint hash = 2166136261;
            foreach (var ch in normalized)
            {
                hash ^= ch;
                hash *= 16777619;
            }

            var r = ((hash >> 16) & 0xFF) / 255f;
            var g = ((hash >> 8) & 0xFF) / 255f;
            var b = (hash & 0xFF) / 255f;

            return new Vector4(0.25f + (0.65f * r), 0.25f + (0.65f * g), 0.25f + (0.65f * b), 1f);
        }
    }

    private static string GuessParameterUnit(string parameterName)
    {
        return parameterName switch
        {
            "S-Wave Velocity" => "m/s",
            "P-Wave Velocity" => "m/s",
            "Density" => "kg/m3",
            "Porosity" => "%",
            "Permeability" => "m/s",
            "Thermal Conductivity" => "W/m·K",
            "Friction Angle" => "deg",
            "Cohesion" => "MPa",
            "NSPT" => "blows",
            "Shear Modulus" => "MPa",
            "Young's Modulus" => "MPa",
            "Poisson's Ratio" => "-",
            "Resonance Frequency" => "Hz",
            _ => string.Empty
        };
    }

    private static string MakeUniqueName(string preferredName, HashSet<string> usedNames)
    {
        var baseName = string.IsNullOrWhiteSpace(preferredName) ? "StratiFixDataset" : preferredName.Trim();

        if (usedNames.Add(baseName))
            return baseName;

        var suffix = 2;
        while (true)
        {
            var candidate = $"{baseName}_{suffix}";
            if (usedNames.Add(candidate))
                return candidate;

            suffix++;
        }
    }

    private sealed class InvestigationRecord
    {
        public string HoleId { get; set; } = string.Empty;
        public string SourceFile { get; set; } = string.Empty;
        public string? InvestigationType { get; set; }

        public double? Longitude { get; set; }
        public double? Latitude { get; set; }
        public double? Easting { get; set; }
        public double? Northing { get; set; }

        public double? Elevation { get; set; }
        public double? TotalDepth { get; set; }

        public bool HasCptData { get; set; }

        public bool HasCoordinates =>
            (Longitude.HasValue && Latitude.HasValue) ||
            (Easting.HasValue && Northing.HasValue);

        public List<StratiFixLayerRecord> Stratigraphy { get; } = new();
    }

    private sealed class StratiFixLayerRecord
    {
        public double DepthFrom { get; set; }
        public double DepthTo { get; set; }
        public string Lithology { get; set; } = "Unknown";
        public string? Description { get; set; }
        public Dictionary<string, float> Parameters { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

public class StratiFixFolderInfo
{
    public bool IsValid { get; set; }
    public string SelectedPath { get; set; } = string.Empty;
    public string DatasetRoot { get; set; } = string.Empty;
    public string ProcessedExcelPath { get; set; } = string.Empty;
    public int ExcelFiles { get; set; }
    public int Shapefiles { get; set; }
    public bool HasStructuralData { get; set; }
    public bool HasDemCache { get; set; }
}
