// GeoscientistToolkit/Data/Loaders/Tough2Loader.cs
//
// TOUGH2 file importer for multiphysics subsurface flow and transport simulations
// TOUGH2 is a numerical simulation program for multi-dimensional fluid and heat flow in porous media

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GeoscientistToolkit.Data.PhysicoChem;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders;

public class Tough2Loader : IDataLoader
{
    public string FilePath { get; set; } = "";
    public string Name => "TOUGH2 Input File";
    public string Description => "Import TOUGH2 multiphysics subsurface flow simulation input file";
    public bool CanImport => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);
    public string ValidationMessage => CanImport ? null : "Please select a valid TOUGH2 input file.";

    public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
    {
        return await Task.Run(() =>
        {
            try
            {
                progressReporter?.Report((0.1f, "Reading TOUGH2 file..."));

                var fileContent = File.ReadAllText(FilePath);
                var parser = new Tough2Parser();
                var tough2Data = parser.Parse(fileContent);

                progressReporter?.Report((0.3f, "Parsing TOUGH2 blocks..."));

                // Create PhysicoChemDataset from TOUGH2 data
                var dataset = new PhysicoChemDataset(
                    Path.GetFileNameWithoutExtension(FilePath),
                    FilePath
                );

                dataset.Description = $"Imported from TOUGH2 file: {Path.GetFileName(FilePath)}";

                progressReporter?.Report((0.5f, "Converting mesh data..."));

                // Convert mesh (ELEME block) to domains
                ConvertMeshToDomains(tough2Data, dataset);

                progressReporter?.Report((0.6f, "Converting material properties..."));

                // Convert material properties (ROCKS block)
                ConvertRocksToMaterials(tough2Data, dataset);

                progressReporter?.Report((0.7f, "Converting initial conditions..."));

                // Convert initial conditions (INCON block)
                ConvertInitialConditions(tough2Data, dataset);

                progressReporter?.Report((0.8f, "Converting boundary conditions..."));

                // Convert sources/sinks (GENER block) to boundary conditions
                ConvertGenerToBoundaryConditions(tough2Data, dataset);

                progressReporter?.Report((0.9f, "Setting simulation parameters..."));

                // Convert simulation parameters (PARAM block)
                ConvertSimulationParameters(tough2Data, dataset);

                progressReporter?.Report((1.0f, "TOUGH2 dataset loaded successfully."));

                return dataset;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Tough2Loader] Failed to load TOUGH2 file: {ex.Message}");
                throw new Exception("Failed to load or parse the TOUGH2 file.", ex);
            }
        });
    }

    public void Reset()
    {
        FilePath = "";
    }

    private void ConvertMeshToDomains(Tough2Data tough2Data, PhysicoChemDataset dataset)
    {
        if (tough2Data.Elements.Count == 0)
            return;

        // Group elements by rock type to create domains
        var elementsByRock = tough2Data.Elements
            .GroupBy(e => e.RockType)
            .ToList();

        foreach (var group in elementsByRock)
        {
            var rockName = group.Key;
            var elements = group.ToList();

            // Calculate bounding box for this domain
            var minX = elements.Min(e => e.X);
            var maxX = elements.Max(e => e.X);
            var minY = elements.Min(e => e.Y);
            var maxY = elements.Max(e => e.Y);
            var minZ = elements.Min(e => e.Z);
            var maxZ = elements.Max(e => e.Z);

            var centerX = (minX + maxX) / 2.0;
            var centerY = (minY + maxY) / 2.0;
            var centerZ = (minZ + maxZ) / 2.0;

            var width = maxX - minX;
            var height = maxY - minY;
            var depth = maxZ - minZ;

            // Create domain with box geometry
            var domain = new ReactorDomain
            {
                Name = rockName,
                Geometry = new ReactorGeometry
                {
                    Type = GeometryType.Box,
                    Center = (centerX, centerY, centerZ),
                    Dimensions = (width, height, depth)
                },
                IsActive = true
            };

            dataset.AddDomain(domain);
        }
    }

    private void ConvertRocksToMaterials(Tough2Data tough2Data, PhysicoChemDataset dataset)
    {
        foreach (var domain in dataset.Domains)
        {
            // Find corresponding rock type
            var rock = tough2Data.Rocks.FirstOrDefault(r => r.Name == domain.Name);
            if (rock != null)
            {
                domain.Material = new MaterialProperties
                {
                    Porosity = rock.Porosity,
                    Permeability = rock.Permeability,
                    Density = rock.Density,
                    SpecificHeat = rock.SpecificHeat,
                    ThermalConductivity = rock.ThermalConductivity
                };
            }
        }
    }

    private void ConvertInitialConditions(Tough2Data tough2Data, PhysicoChemDataset dataset)
    {
        if (tough2Data.InitialConditions.Count == 0)
            return;

        // Use first initial condition as global default
        var firstIC = tough2Data.InitialConditions.FirstOrDefault();
        if (firstIC != null)
        {
            foreach (var domain in dataset.Domains)
            {
                if (domain.InitialConditions == null)
                {
                    domain.InitialConditions = new InitialConditions
                    {
                        Temperature = firstIC.Temperature,
                        Pressure = firstIC.Pressure,
                        LiquidSaturation = firstIC.LiquidSaturation
                    };
                }
            }
        }

        // Apply element-specific initial conditions
        foreach (var ic in tough2Data.InitialConditions)
        {
            // Find element and corresponding domain
            var element = tough2Data.Elements.FirstOrDefault(e => e.Name == ic.ElementName);
            if (element != null)
            {
                var domain = dataset.Domains.FirstOrDefault(d => d.Name == element.RockType);
                if (domain != null && domain.InitialConditions != null)
                {
                    domain.InitialConditions.Temperature = ic.Temperature;
                    domain.InitialConditions.Pressure = ic.Pressure;
                    domain.InitialConditions.LiquidSaturation = ic.LiquidSaturation;
                }
            }
        }
    }

    private void ConvertGenerToBoundaryConditions(Tough2Data tough2Data, PhysicoChemDataset dataset)
    {
        foreach (var gener in tough2Data.Generators)
        {
            // Find element location
            var element = tough2Data.Elements.FirstOrDefault(e => e.Name == gener.ElementName);
            if (element == null)
                continue;

            BoundaryType bcType;
            BoundaryVariable bcVariable;
            double value = 0;

            // Determine BC type based on generation type
            if (gener.Type.Contains("HEAT") || gener.Type.Contains("COM"))
            {
                bcType = BoundaryType.Inlet;
                bcVariable = BoundaryVariable.MassFlux;
                value = gener.GenerationRate;
            }
            else
            {
                bcType = BoundaryType.FixedValue;
                bcVariable = BoundaryVariable.Pressure;
                value = gener.SpecificEnthalpy; // Can represent pressure or other values
            }

            var bc = new BoundaryCondition
            {
                Name = gener.Name,
                Type = bcType,
                Location = BoundaryLocation.Custom,
                Variable = bcVariable,
                Value = value,
                FluxValue = gener.GenerationRate,
                CustomRegionCenter = (element.X, element.Y, element.Z),
                CustomRegionRadius = Math.Pow(element.Volume, 1.0 / 3.0), // Approximate radius from volume
                IsActive = true
            };

            dataset.BoundaryConditions.Add(bc);
        }
    }

    private void ConvertSimulationParameters(Tough2Data tough2Data, PhysicoChemDataset dataset)
    {
        var param = tough2Data.Parameters;
        if (param != null)
        {
            dataset.SimulationParams.TotalTime = param.MaxSimulationTime;
            dataset.SimulationParams.TimeStep = param.InitialTimeStep;
            dataset.SimulationParams.MaxIterations = param.MaxIterations;
            dataset.SimulationParams.ConvergenceTolerance = param.ConvergenceTolerance;
            dataset.SimulationParams.EnableHeatTransfer = true;
            dataset.SimulationParams.EnableFlow = true;
            dataset.SimulationParams.EnableReactiveTransport = param.NumComponents > 1;
        }
    }
}

/// <summary>
/// TOUGH2 file parser
/// </summary>
public class Tough2Parser
{
    public Tough2Data Parse(string fileContent)
    {
        var data = new Tough2Data();
        var lines = fileContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        string currentBlock = "";
        var blockLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Skip comments
            if (trimmedLine.StartsWith("!") || trimmedLine.StartsWith("#"))
                continue;

            // Detect block headers (usually 5 characters at start of line)
            if (IsBlockHeader(trimmedLine))
            {
                // Process previous block
                if (!string.IsNullOrEmpty(currentBlock))
                {
                    ParseBlock(currentBlock, blockLines, data);
                }

                currentBlock = trimmedLine.Substring(0, Math.Min(5, trimmedLine.Length)).Trim();
                blockLines.Clear();
            }
            else
            {
                blockLines.Add(line);
            }
        }

        // Process final block
        if (!string.IsNullOrEmpty(currentBlock))
        {
            ParseBlock(currentBlock, blockLines, data);
        }

        return data;
    }

    private bool IsBlockHeader(string line)
    {
        if (line.Length < 5)
            return false;

        var potentialHeader = line.Substring(0, 5).ToUpperInvariant();
        var validHeaders = new[] { "ROCKS", "PARAM", "ELEME", "CONNE", "INCON", "GENER", "TITLE", "MESHM", "MULTI", "START", "ENDCY", "FOFT", "COFT", "GOFT", "TIMES", "MOMOP", "DIFFU", "SELEC", "ROFT", "LINEQ", "OUTPU" };

        return validHeaders.Contains(potentialHeader);
    }

    private void ParseBlock(string blockName, List<string> blockLines, Tough2Data data)
    {
        switch (blockName.ToUpperInvariant())
        {
            case "ROCKS":
                ParseRocksBlock(blockLines, data);
                break;
            case "PARAM":
                ParseParamBlock(blockLines, data);
                break;
            case "ELEME":
                ParseElemeBlock(blockLines, data);
                break;
            case "CONNE":
                ParseConneBlock(blockLines, data);
                break;
            case "INCON":
                ParseInconBlock(blockLines, data);
                break;
            case "GENER":
                ParseGenerBlock(blockLines, data);
                break;
            case "FOFT":
                ParseFoftBlock(blockLines, data);
                break;
            case "COFT":
                ParseCoftBlock(blockLines, data);
                break;
            case "GOFT":
                ParseGoftBlock(blockLines, data);
                break;
            case "TIMES":
                ParseTimesBlock(blockLines, data);
                break;
            case "MOMOP":
                ParseMomopBlock(blockLines, data);
                break;
            case "DIFFU":
                ParseDiffuBlock(blockLines, data);
                break;
            case "SELEC":
                ParseSelecBlock(blockLines, data);
                break;
            case "MESHM":
                ParseMeshmBlock(blockLines, data);
                break;
            case "TITLE":
                data.Title = string.Join(" ", blockLines);
                break;
        }
    }

    private void ParseRocksBlock(List<string> lines, Tough2Data data)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var rock = new RockType
                {
                    Name = ReadFixedString(line, 0, 5).Trim(),
                    NAD = ReadInt(line, 5, 5),
                    Density = ReadDouble(line, 10, 10),
                    Porosity = ReadDouble(line, 20, 10),
                    Permeability = ReadDouble(line, 30, 10),
                    SpecificHeat = ReadDouble(line, 40, 10),
                    ThermalConductivity = ReadDouble(line, 50, 10)
                };

                data.Rocks.Add(rock);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[Tough2Parser] Failed to parse ROCKS line: {line} - {ex.Message}");
            }
        }
    }

    private void ParseParamBlock(List<string> lines, Tough2Data data)
    {
        if (lines.Count == 0)
            return;

        try
        {
            var param = new SimulationParam();

            // TOUGH2 PARAM block typically has multiple lines
            // Line 1: NOITE, KDATA, MCYC, MSEC, MCYPR
            if (lines.Count > 0)
            {
                var line1 = lines[0];
                param.MaxIterations = ReadInt(line1, 0, 5, 100);
                param.PrintInterval = ReadInt(line1, 10, 5, 1);
                param.MaxTimeSteps = ReadInt(line1, 15, 5, 1000);
            }

            // Line 2: TEXP, BE, TSTART, TIMAX, DELTEN, DELTMX, ELST, GF
            if (lines.Count > 1)
            {
                var line2 = lines[1];
                param.InitialTimeStep = ReadDouble(line2, 30, 10, 1.0);
                param.MaxSimulationTime = ReadDouble(line2, 20, 10, 3600.0);
                param.MaxTimeStepSize = ReadDouble(line2, 40, 10, 100.0);
            }

            // Line 3: RE1, RE2, U, W
            if (lines.Count > 2)
            {
                var line3 = lines[2];
                param.ConvergenceTolerance = ReadDouble(line3, 0, 10, 1e-6);
            }

            // Line 4: Number of components
            if (lines.Count > 3)
            {
                var line4 = lines[3];
                param.NumComponents = ReadInt(line4, 0, 5, 1);
            }

            data.Parameters = param;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[Tough2Parser] Failed to parse PARAM block: {ex.Message}");
        }
    }

    private void ParseElemeBlock(List<string> lines, Tough2Data data)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var element = new Element
                {
                    Name = ReadFixedString(line, 0, 5).Trim(),
                    NSEQ = ReadInt(line, 5, 5),
                    NADD = ReadInt(line, 10, 5),
                    MA1 = ReadFixedString(line, 15, 3),
                    MA2 = ReadFixedString(line, 18, 2),
                    RockType = ReadFixedString(line, 15, 5).Trim(),
                    Volume = ReadDouble(line, 20, 10),
                    X = ReadDouble(line, 30, 10),
                    Y = ReadDouble(line, 40, 10),
                    Z = ReadDouble(line, 50, 10)
                };

                data.Elements.Add(element);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[Tough2Parser] Failed to parse ELEME line: {line} - {ex.Message}");
            }
        }
    }

    private void ParseConneBlock(List<string> lines, Tough2Data data)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var connection = new Connection
                {
                    Element1 = ReadFixedString(line, 0, 5).Trim(),
                    Element2 = ReadFixedString(line, 5, 5).Trim(),
                    NSEQ = ReadInt(line, 10, 5),
                    NAD1 = ReadInt(line, 15, 5),
                    NAD2 = ReadInt(line, 20, 5),
                    InterfaceArea = ReadDouble(line, 25, 10),
                    Distance1 = ReadDouble(line, 35, 10),
                    Distance2 = ReadDouble(line, 45, 10)
                };

                data.Connections.Add(connection);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[Tough2Parser] Failed to parse CONNE line: {line} - {ex.Message}");
            }
        }
    }

    private void ParseInconBlock(List<string> lines, Tough2Data data)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var incon = new InitialCondition
                {
                    ElementName = ReadFixedString(line, 0, 5).Trim(),
                    Porosity = ReadDouble(line, 10, 10, 0.3),
                    Pressure = ReadDouble(line, 20, 10, 101325.0),
                    Temperature = ReadDouble(line, 30, 10, 298.15),
                    LiquidSaturation = ReadDouble(line, 40, 10, 1.0)
                };

                data.InitialConditions.Add(incon);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[Tough2Parser] Failed to parse INCON line: {line} - {ex.Message}");
            }
        }
    }

    private void ParseGenerBlock(List<string> lines, Tough2Data data)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var gener = new Generator
                {
                    ElementName = ReadFixedString(line, 0, 5).Trim(),
                    Name = ReadFixedString(line, 5, 5).Trim(),
                    Type = ReadFixedString(line, 10, 5).Trim(),
                    LTAB = ReadInt(line, 15, 5),
                    GenerationRate = ReadDouble(line, 20, 10),
                    SpecificEnthalpy = ReadDouble(line, 30, 10)
                };

                data.Generators.Add(gener);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[Tough2Parser] Failed to parse GENER line: {line} - {ex.Message}");
            }
        }
    }

    private void ParseFoftBlock(List<string> lines, Tough2Data data)
    {
        // FOFT specifies elements for which time series output is desired
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Element names are typically 5 characters
            var elementName = ReadFixedString(line, 0, 5).Trim();
            if (!string.IsNullOrEmpty(elementName))
            {
                data.ElementOutputs.Add(elementName);
            }
        }
    }

    private void ParseCoftBlock(List<string> lines, Tough2Data data)
    {
        // COFT specifies connections for which time series output is desired
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Connection names are element1 + element2
            var elem1 = ReadFixedString(line, 0, 5).Trim();
            var elem2 = ReadFixedString(line, 5, 5).Trim();

            if (!string.IsNullOrEmpty(elem1) && !string.IsNullOrEmpty(elem2))
            {
                data.ConnectionOutputs.Add($"{elem1}-{elem2}");
            }
        }
    }

    private void ParseGoftBlock(List<string> lines, Tough2Data data)
    {
        // GOFT specifies generators for which time series output is desired
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var generatorName = ReadFixedString(line, 0, 10).Trim();
            if (!string.IsNullOrEmpty(generatorName))
            {
                data.GeneratorOutputs.Add(generatorName);
            }
        }
    }

    private void ParseTimesBlock(List<string> lines, Tough2Data data)
    {
        // TIMES specifies times at which output is desired
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                // Parse multiple times per line (typically 8 values per line, 10 chars each)
                for (int i = 0; i < line.Length; i += 10)
                {
                    if (i + 10 > line.Length && i >= line.Length)
                        break;

                    var timeValue = ReadDouble(line, i, Math.Min(10, line.Length - i));
                    if (timeValue > 0)
                    {
                        data.OutputTimes.Add(timeValue);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[Tough2Parser] Failed to parse TIMES line: {line} - {ex.Message}");
            }
        }
    }

    private void ParseMomopBlock(List<string> lines, Tough2Data data)
    {
        // MOMOP - More Output Options
        if (lines.Count == 0)
            return;

        try
        {
            var momop = new MomopOptions();

            // Line format varies, but typically contains integer flags
            if (lines.Count > 0)
            {
                var line = lines[0];
                momop.MOP = new List<int>();

                // Parse up to 40 integers (5 chars each)
                for (int i = 0; i < Math.Min(40, line.Length / 5); i++)
                {
                    var val = ReadInt(line, i * 5, 5);
                    momop.MOP.Add(val);
                }
            }

            data.MoreOptions = momop;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[Tough2Parser] Failed to parse MOMOP block: {ex.Message}");
        }
    }

    private void ParseDiffuBlock(List<string> lines, Tough2Data data)
    {
        // DIFFU - Diffusion coefficients
        if (lines.Count == 0)
            return;

        try
        {
            var diffu = new DiffusionData();

            if (lines.Count > 0)
            {
                var line = lines[0];
                diffu.DiffusionCoefficients = new List<double>();

                // Parse diffusion coefficients (typically 10 chars each)
                for (int i = 0; i < line.Length; i += 10)
                {
                    if (i + 10 > line.Length)
                        break;

                    var coeff = ReadDouble(line, i, 10);
                    diffu.DiffusionCoefficients.Add(coeff);
                }
            }

            data.Diffusion = diffu;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[Tough2Parser] Failed to parse DIFFU block: {ex.Message}");
        }
    }

    private void ParseSelecBlock(List<string> lines, Tough2Data data)
    {
        // SELEC - Selection of various options (equation of state, etc.)
        if (lines.Count == 0)
            return;

        try
        {
            var selec = new SelectionData();
            selec.Options = new Dictionary<string, string>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Parse key-value pairs or numbered options
                // Format varies by EOS module
                var trimmed = line.Trim();
                selec.RawOptions.Add(trimmed);

                // Try to parse as integer selection
                if (int.TryParse(trimmed, out int selection))
                {
                    selec.IntegerSelections.Add(selection);
                }
            }

            data.Selections = selec;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[Tough2Parser] Failed to parse SELEC block: {ex.Message}");
        }
    }

    private void ParseMeshmBlock(List<string> lines, Tough2Data data)
    {
        // MESHM - MESHMaker for automatic mesh generation
        if (lines.Count == 0)
            return;

        try
        {
            var meshm = new MeshMakerData();
            meshm.MeshType = "CUSTOM";
            meshm.RawData = new List<string>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                meshm.RawData.Add(line);

                // Check for mesh type keywords
                var upperLine = line.ToUpperInvariant();
                if (upperLine.Contains("RZ2D"))
                    meshm.MeshType = "RZ2D";
                else if (upperLine.Contains("XYZ"))
                    meshm.MeshType = "XYZ";
                else if (upperLine.Contains("MINC"))
                    meshm.MeshType = "MINC";
            }

            data.MeshMaker = meshm;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[Tough2Parser] Failed to parse MESHM block: {ex.Message}");
        }
    }

    // Helper methods for parsing fixed-width fields
    private string ReadFixedString(string line, int start, int length)
    {
        if (start >= line.Length)
            return "";

        int actualLength = Math.Min(length, line.Length - start);
        return line.Substring(start, actualLength);
    }

    private int ReadInt(string line, int start, int length, int defaultValue = 0)
    {
        try
        {
            var str = ReadFixedString(line, start, length).Trim();
            if (string.IsNullOrEmpty(str))
                return defaultValue;
            return int.Parse(str);
        }
        catch
        {
            return defaultValue;
        }
    }

    private double ReadDouble(string line, int start, int length, double defaultValue = 0.0)
    {
        try
        {
            var str = ReadFixedString(line, start, length).Trim();
            if (string.IsNullOrEmpty(str))
                return defaultValue;

            // TOUGH2 sometimes uses 'D' for exponents instead of 'E'
            str = str.Replace('D', 'E').Replace('d', 'e');

            return double.Parse(str, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        catch
        {
            return defaultValue;
        }
    }
}

/// <summary>
/// Data structure for TOUGH2 file contents
/// </summary>
public class Tough2Data
{
    public string Title { get; set; } = "";
    public List<RockType> Rocks { get; set; } = new();
    public SimulationParam Parameters { get; set; }
    public List<Element> Elements { get; set; } = new();
    public List<Connection> Connections { get; set; } = new();
    public List<InitialCondition> InitialConditions { get; set; } = new();
    public List<Generator> Generators { get; set; } = new();
    public List<string> ElementOutputs { get; set; } = new(); // FOFT
    public List<string> ConnectionOutputs { get; set; } = new(); // COFT
    public List<string> GeneratorOutputs { get; set; } = new(); // GOFT
    public List<double> OutputTimes { get; set; } = new(); // TIMES
    public MomopOptions MoreOptions { get; set; } // MOMOP
    public DiffusionData Diffusion { get; set; } // DIFFU
    public SelectionData Selections { get; set; } // SELEC
    public MeshMakerData MeshMaker { get; set; } // MESHM
}

public class RockType
{
    public string Name { get; set; }
    public int NAD { get; set; }
    public double Density { get; set; }
    public double Porosity { get; set; }
    public double Permeability { get; set; }
    public double SpecificHeat { get; set; }
    public double ThermalConductivity { get; set; }
}

public class SimulationParam
{
    public int MaxIterations { get; set; } = 100;
    public int PrintInterval { get; set; } = 1;
    public int MaxTimeSteps { get; set; } = 1000;
    public double InitialTimeStep { get; set; } = 1.0;
    public double MaxSimulationTime { get; set; } = 3600.0;
    public double MaxTimeStepSize { get; set; } = 100.0;
    public double ConvergenceTolerance { get; set; } = 1e-6;
    public int NumComponents { get; set; } = 1;
}

public class Element
{
    public string Name { get; set; }
    public int NSEQ { get; set; }
    public int NADD { get; set; }
    public string MA1 { get; set; }
    public string MA2 { get; set; }
    public string RockType { get; set; }
    public double Volume { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
}

public class Connection
{
    public string Element1 { get; set; }
    public string Element2 { get; set; }
    public int NSEQ { get; set; }
    public int NAD1 { get; set; }
    public int NAD2 { get; set; }
    public double InterfaceArea { get; set; }
    public double Distance1 { get; set; }
    public double Distance2 { get; set; }
}

public class InitialCondition
{
    public string ElementName { get; set; }
    public double Porosity { get; set; }
    public double Pressure { get; set; }
    public double Temperature { get; set; }
    public double LiquidSaturation { get; set; }
}

public class Generator
{
    public string ElementName { get; set; }
    public string Name { get; set; }
    public string Type { get; set; }
    public int LTAB { get; set; }
    public double GenerationRate { get; set; }
    public double SpecificEnthalpy { get; set; }
}

public class MomopOptions
{
    public List<int> MOP { get; set; } = new();
}

public class DiffusionData
{
    public List<double> DiffusionCoefficients { get; set; } = new();
}

public class SelectionData
{
    public Dictionary<string, string> Options { get; set; } = new();
    public List<string> RawOptions { get; set; } = new();
    public List<int> IntegerSelections { get; set; } = new();
}

public class MeshMakerData
{
    public string MeshType { get; set; } = "CUSTOM";
    public List<string> RawData { get; set; } = new();
}
