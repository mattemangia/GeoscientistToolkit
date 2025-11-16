// GeoscientistToolkit/Data/Exporters/Tough2Exporter.cs
//
// TOUGH2 file exporter for multiphysics subsurface flow and transport simulations
// Exports PhysicoChemDataset to TOUGH2 input file format

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GeoscientistToolkit.Data.PhysicoChem;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Exporters;

public class Tough2Exporter
{
    private PhysicoChemDataset _dataset;
    private StringBuilder _output;

    public void Export(PhysicoChemDataset dataset, string outputPath)
    {
        try
        {
            Logger.Log($"Exporting PhysicoChemDataset to TOUGH2 format: {outputPath}");

            _dataset = dataset;
            _output = new StringBuilder();

            // Generate TOUGH2 file
            WriteTitleBlock();
            WriteRocksBlock();
            WriteMultiBlock();
            WriteParamBlock();
            WriteElemeBlock();
            WriteConneBlock();
            WriteInconBlock();
            WriteGenerBlock();
            WriteEndcy();

            // Write to file
            File.WriteAllText(outputPath, _output.ToString());

            Logger.Log($"Successfully exported TOUGH2 file to: {outputPath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export TOUGH2 file: {ex.Message}");
            throw;
        }
    }

    private void WriteTitleBlock()
    {
        _output.AppendLine("TITLE");
        _output.AppendLine($"**** {_dataset.Name} ****");
        _output.AppendLine($"Exported from GeoscientistToolkit PhysicoChemDataset");
        _output.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        if (!string.IsNullOrEmpty(_dataset.Description))
        {
            _output.AppendLine(_dataset.Description);
        }
        _output.AppendLine();
    }

    private void WriteRocksBlock()
    {
        _output.AppendLine("ROCKS");
        _output.AppendLine("----1----*----2----*----3----*----4----*----5----*----6----*----7----*----8");

        foreach (var domain in _dataset.Domains)
        {
            if (domain.Material == null)
                continue;

            var mat = domain.Material;
            var rockName = TruncateString(domain.Name, 5);

            // ROCKS block format:
            // MAT   NAD   DENSITY  POROSITY  K(1)     K(2)     K(3)     CWET    SPHT
            _output.AppendLine(
                $"{rockName,-5}" +
                $"{0,5}" +  // NAD
                $"{FormatDouble(mat.Density, 10)}" +
                $"{FormatDouble(mat.Porosity, 10)}" +
                $"{FormatDouble(mat.Permeability, 10)}" +  // K(1)
                $"{FormatDouble(mat.Permeability, 10)}" +  // K(2) - assume isotropic
                $"{FormatDouble(mat.Permeability, 10)}" +  // K(3) - assume isotropic
                $"{FormatDouble(mat.ThermalConductivity, 10)}" +
                $"{FormatDouble(mat.SpecificHeat, 10)}"
            );

            // Second line for additional rock properties (optional)
            _output.AppendLine($"{FormatDouble(0.0, 10)}{FormatDouble(0.0, 10)}{FormatDouble(0.0, 10)}");
        }

        _output.AppendLine();
    }

    private void WriteMultiBlock()
    {
        // MULTI block specifies the number of mass components and phases
        _output.AppendLine("MULTI");
        _output.AppendLine("----1----*----2----*----3----*----4----*----5----*----6----*----7----*----8");

        // For PhysicoChem, we'll default to water with heat (2 components)
        int numComponents = 2; // Water + Heat
        int numPhases = 2;     // Liquid + Gas

        _output.AppendLine($"{numComponents,5}{numPhases,5}");
        _output.AppendLine();
    }

    private void WriteParamBlock()
    {
        _output.AppendLine("PARAM");
        _output.AppendLine("----1----*----2----*----3----*----4----*----5----*----6----*----7----*----8");

        var param = _dataset.SimulationParams;

        // Line 1: NOITE, KDATA, MCYC, MSEC, MCYPR
        var maxIter = param.MaxIterations;
        var maxCycles = (int)(param.TotalTime / param.TimeStep);

        _output.AppendLine(
            $"{maxIter,5}" +
            $"{1,5}" +      // KDATA - output frequency
            $"{maxCycles,5}" +
            $"{0,5}" +      // MSEC - seconds for printout
            $"{100,5}"      // MCYPR - printout frequency
        );

        // Line 2: TEXP, BE, TSTART, TIMAX, DELTEN, DELTMX, ELST
        _output.AppendLine(
            $"{FormatDouble(0.0, 10)}" +           // TEXP - time step amplification
            $"{FormatDouble(1.0, 10)}" +           // BE - upstream weighting
            $"{FormatDouble(0.0, 10)}" +           // TSTART - starting time
            $"{FormatDouble(param.TotalTime, 10)}" +
            $"{FormatDouble(param.TimeStep, 10)}" +
            $"{FormatDouble(param.TimeStep * 10, 10)}" +  // DELTMX - max time step
            $"{FormatDouble(0.0, 10)}"             // ELST
        );

        // Line 3: RE1, RE2, U, W (convergence criteria)
        _output.AppendLine(
            $"{FormatDouble(param.ConvergenceTolerance, 10)}" +
            $"{FormatDouble(param.ConvergenceTolerance * 10, 10)}" +
            $"{FormatDouble(1.0, 10)}" +
            $"{FormatDouble(1.0, 10)}"
        );

        // Line 4: SCALE (for mesh, Jacobian)
        _output.AppendLine($"{FormatDouble(1.0, 10)}{FormatDouble(1.0, 10)}{FormatDouble(1.0, 10)}");

        _output.AppendLine();
    }

    private void WriteElemeBlock()
    {
        _output.AppendLine("ELEME");
        _output.AppendLine("----1----*----2----*----3----*----4----*----5----*----6----*----7----*----8");

        if (_dataset.GeneratedMesh == null)
        {
            // Generate simple mesh from domains
            GenerateSimpleMeshElements();
        }
        else
        {
            // Use existing mesh
            GenerateMeshElements();
        }

        _output.AppendLine();
    }

    private void GenerateSimpleMeshElements()
    {
        int elementIndex = 0;

        foreach (var domain in _dataset.Domains)
        {
            if (domain.Geometry == null)
                continue;

            var rockType = TruncateString(domain.Name, 5);
            var center = domain.Geometry.Center;
            var dims = domain.Geometry.Dimensions;

            // Calculate approximate volume
            double volume = 0;
            switch (domain.Geometry.Type)
            {
                case GeometryType.Box:
                    volume = dims.Width * dims.Height * dims.Depth;
                    break;
                case GeometryType.Sphere:
                    volume = (4.0 / 3.0) * Math.PI * Math.Pow(domain.Geometry.Radius, 3);
                    break;
                case GeometryType.Cylinder:
                    volume = Math.PI * Math.Pow(domain.Geometry.Radius, 2) * domain.Geometry.Height;
                    break;
                default:
                    volume = dims.Width * dims.Height * dims.Depth;
                    break;
            }

            // Create single element for this domain
            var elementName = $"E{elementIndex:D4}";

            _output.AppendLine(
                $"{elementName,-5}" +
                $"{0,5}" +  // NSEQ
                $"{0,5}" +  // NADD
                $"{rockType,-5}" +
                $"{FormatDouble(volume, 10)}" +
                $"{FormatDouble(0.0, 10)}" +  // Heat exchange area (optional)
                $"{FormatDouble(center.X, 10)}" +
                $"{FormatDouble(center.Y, 10)}" +
                $"{FormatDouble(center.Z, 10)}"
            );

            elementIndex++;
        }
    }

    private void GenerateMeshElements()
    {
        var mesh = _dataset.GeneratedMesh;
        var gridSize = mesh.GridSize;
        var origin = mesh.Origin;
        var spacing = mesh.Spacing;

        int elementIndex = 0;

        // Generate elements for each grid cell
        for (int i = 0; i < gridSize.X; i++)
        {
            for (int j = 0; j < gridSize.Y; j++)
            {
                for (int k = 0; k < gridSize.Z; k++)
                {
                    var x = origin.X + i * spacing.X + spacing.X / 2.0;
                    var y = origin.Y + j * spacing.Y + spacing.Y / 2.0;
                    var z = origin.Z + k * spacing.Z + spacing.Z / 2.0;

                    var volume = spacing.X * spacing.Y * spacing.Z;

                    // Find which domain this cell belongs to
                    var domain = FindDomainAtPoint(x, y, z);
                    var rockType = domain != null ? TruncateString(domain.Name, 5) : "DEFLT";

                    var elementName = $"{i:D2}{j:D2}{k:D1}";

                    _output.AppendLine(
                        $"{elementName,-5}" +
                        $"{0,5}" +  // NSEQ
                        $"{0,5}" +  // NADD
                        $"{rockType,-5}" +
                        $"{FormatDouble(volume, 10)}" +
                        $"{FormatDouble(0.0, 10)}" +  // Heat exchange area
                        $"{FormatDouble(x, 10)}" +
                        $"{FormatDouble(y, 10)}" +
                        $"{FormatDouble(z, 10)}"
                    );

                    elementIndex++;

                    // Limit elements for performance
                    if (elementIndex > 10000)
                    {
                        Logger.LogWarning("TOUGH2 export limited to 10000 elements");
                        return;
                    }
                }
            }
        }
    }

    private ReactorDomain FindDomainAtPoint(double x, double y, double z)
    {
        foreach (var domain in _dataset.Domains)
        {
            if (domain.Geometry != null && domain.Geometry.ContainsPoint(x, y, z))
            {
                return domain;
            }
        }
        return null;
    }

    private void WriteConneBlock()
    {
        _output.AppendLine("CONNE");
        _output.AppendLine("----1----*----2----*----3----*----4----*----5----*----6----*----7----*----8");

        // For simple export, we'll skip connections
        // In a full implementation, connections would be generated from mesh topology

        _output.AppendLine();
    }

    private void WriteInconBlock()
    {
        _output.AppendLine("INCON");
        _output.AppendLine("----1----*----2----*----3----*----4----*----5----*----6----*----7----*----8");

        int elementIndex = 0;

        foreach (var domain in _dataset.Domains)
        {
            if (domain.InitialConditions == null)
                continue;

            var ic = domain.InitialConditions;
            var elementName = $"E{elementIndex:D4}";

            // INCON format: ELEM, POROSITY, conditions
            _output.AppendLine($"{elementName,-5}");

            // Second line: porosity and initial conditions
            // For TOUGH2: we need to specify primary variables
            // For 2-component (water+heat): P, T, Sg (or Sl)
            _output.AppendLine(
                $"{FormatDouble(domain.Material?.Porosity ?? 0.3, 15)}" +
                $"{FormatDouble(ic.Pressure, 15)}" +
                $"{FormatDouble(ic.Temperature, 15)}" +
                $"{FormatDouble(1.0 - ic.LiquidSaturation, 15)}"  // Gas saturation
            );

            elementIndex++;
        }

        _output.AppendLine();
    }

    private void WriteGenerBlock()
    {
        _output.AppendLine("GENER");
        _output.AppendLine("----1----*----2----*----3----*----4----*----5----*----6----*----7----*----8");

        int generIndex = 0;

        foreach (var bc in _dataset.BoundaryConditions)
        {
            if (!bc.IsActive)
                continue;

            // Find nearest element
            var elementName = $"E{generIndex:D4}";  // Simplified
            var generName = TruncateString(bc.Name, 5);

            // Determine generator type based on boundary condition
            string generType = "COM1"; // Default: mass component 1

            if (bc.Variable == BoundaryVariable.HeatFlux || bc.Variable == BoundaryVariable.Temperature)
            {
                generType = "HEAT"; // Heat source
            }

            double generationRate = bc.FluxValue != 0 ? bc.FluxValue : bc.Value;
            double enthalpy = 0.0;

            if (bc.Variable == BoundaryVariable.Temperature)
            {
                // Approximate enthalpy from temperature (simplified)
                enthalpy = 4184.0 * (bc.Value - 273.15); // J/kg for water
            }

            _output.AppendLine(
                $"{elementName,-5}" +
                $"{generName,-5}" +
                $"{generType,-5}" +
                $"{0,5}" +  // LTAB
                $"{FormatDouble(generationRate, 10)}" +
                $"{FormatDouble(enthalpy, 10)}"
            );

            generIndex++;
        }

        _output.AppendLine();
    }

    private void WriteEndcy()
    {
        _output.AppendLine("ENDCY");
        _output.AppendLine();
    }

    // Utility methods
    private string TruncateString(string str, int maxLength)
    {
        if (string.IsNullOrEmpty(str))
            return new string(' ', maxLength);

        if (str.Length > maxLength)
            return str.Substring(0, maxLength);

        return str.PadRight(maxLength);
    }

    private string FormatDouble(double value, int width)
    {
        // TOUGH2 uses scientific notation with 'E'
        // Format: sign + mantissa + E + exponent
        string formatted;

        if (Math.Abs(value) < 1e-99 || Math.Abs(value) > 1e99)
        {
            formatted = value.ToString($"E{width - 7}", CultureInfo.InvariantCulture);
        }
        else if (Math.Abs(value) >= 0.001 && Math.Abs(value) < 1000000)
        {
            // Use fixed point for reasonable values
            int decimals = Math.Max(0, width - 6);
            formatted = value.ToString($"F{decimals}", CultureInfo.InvariantCulture);
        }
        else
        {
            formatted = value.ToString($"E{width - 7}", CultureInfo.InvariantCulture);
        }

        // Ensure width
        if (formatted.Length > width)
        {
            formatted = value.ToString("E2", CultureInfo.InvariantCulture);
        }

        return formatted.PadLeft(width);
    }
}
