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
            WriteFoftBlock();
            WriteCoftBlock();
            WriteGoftBlock();
            WriteTimesBlock();
            WriteDiffuBlock();
            WriteSelecBlock();
            WriteMomopBlock();
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

        foreach (var mat in _dataset.Materials)
        {
            var rockName = TruncateString(mat.MaterialID, 5);

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

        GenerateMeshElements();

        _output.AppendLine();
    }

    private void GenerateMeshElements()
    {
        foreach (var cell in _dataset.Mesh.Cells.Values)
        {
            var rockType = TruncateString(cell.MaterialID, 5);
            var center = cell.Center;
            var volume = cell.Volume;

            _output.AppendLine(
                $"{TruncateString(cell.ID, 5),-5}" +
                $"{0,5}" +  // NSEQ
                $"{0,5}" +  // NADD
                $"{rockType,-5}" +
                $"{FormatDouble(volume, 10)}" +
                $"{FormatDouble(0.0, 10)}" +  // Heat exchange area (optional)
                $"{FormatDouble(center.X, 10)}" +
                $"{FormatDouble(center.Y, 10)}" +
                $"{FormatDouble(center.Z, 10)}"
            );
        }
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

        foreach (var cell in _dataset.Mesh.Cells.Values)
        {
            if (cell.InitialConditions == null)
                continue;

            var ic = cell.InitialConditions;
            var material = _dataset.Materials.FirstOrDefault(m => m.MaterialID == cell.MaterialID);
            var porosity = material?.Porosity ?? 0.3;

            // INCON format: ELEM, POROSITY, conditions
            _output.AppendLine($"{TruncateString(cell.ID, 5),-5}");

            // Second line: porosity and initial conditions
            // For TOUGH2: we need to specify primary variables
            // For 2-component (water+heat): P, T, Sg (or Sl)
            _output.AppendLine(
                $"{FormatDouble(porosity, 15)}" +
                $"{FormatDouble(ic.Pressure, 15)}" +
                $"{FormatDouble(ic.Temperature, 15)}" +
                $"{FormatDouble(1.0 - ic.LiquidSaturation, 15)}"  // Gas saturation
            );
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

    private void WriteFoftBlock()
    {
        // FOFT - Element output times
        // Export first 5 cells as elements to monitor
        if (_dataset.Mesh.Cells.Count == 0)
            return;

        _output.AppendLine("FOFT");
        _output.AppendLine("----1----*----2----*----3----*----4----*----5----*----6----*----7----*----8");

        foreach (var cell in _dataset.Mesh.Cells.Values.Take(5))
        {
            _output.AppendLine(TruncateString(cell.ID, 5).PadRight(5));
        }

        _output.AppendLine();
    }

    private void WriteCoftBlock()
    {
        // COFT - Connection output times
        // Typically used for monitoring flow between elements
        // We'll skip this for simplified export
        _output.AppendLine();
    }

    private void WriteGoftBlock()
    {
        // GOFT - Generator output times
        // Export generators for time series monitoring
        if (_dataset.BoundaryConditions.Count == 0)
            return;

        _output.AppendLine("GOFT");
        _output.AppendLine("----1----*----2----*----3----*----4----*----5----*----6----*----7----*----8");

        int count = 0;
        foreach (var bc in _dataset.BoundaryConditions.Take(10))
        {
            if (!bc.IsActive)
                continue;

            var elementName = $"E{count:D4}";
            var generName = TruncateString(bc.Name, 5);
            _output.AppendLine($"{elementName}{generName}");
            count++;
        }

        _output.AppendLine();
    }

    private void WriteTimesBlock()
    {
        // TIMES - Specific times for output
        // Generate output times based on simulation parameters
        var param = _dataset.SimulationParams;
        if (param.OutputInterval <= 0)
            return;

        _output.AppendLine("TIMES");
        _output.AppendLine("----1----*----2----*----3----*----4----*----5----*----6----*----7----*----8");

        var times = new List<double>();
        double currentTime = param.OutputInterval;
        while (currentTime <= param.TotalTime && times.Count < 100)
        {
            times.Add(currentTime);
            currentTime += param.OutputInterval;
        }

        // Write 8 values per line
        for (int i = 0; i < times.Count; i++)
        {
            _output.Append(FormatDouble(times[i], 10));

            if ((i + 1) % 8 == 0 || i == times.Count - 1)
            {
                _output.AppendLine();
            }
        }

        _output.AppendLine();
    }

    private void WriteDiffuBlock()
    {
        // DIFFU - Diffusion coefficients
        // Write default diffusion coefficients for components
        _output.AppendLine("DIFFU");
        _output.AppendLine("----1----*----2----*----3----*----4----*----5----*----6----*----7----*----8");

        // Default diffusion coefficients (e.g., for water and heat)
        _output.Append(FormatDouble(2.13e-5, 10)); // Water diffusivity in air (m²/s)
        _output.Append(FormatDouble(1.5e-7, 10));  // Thermal diffusivity
        _output.AppendLine();

        _output.AppendLine();
    }

    private void WriteSelecBlock()
    {
        // SELEC - Selection of various options
        // Used for equation of state and other module selections
        // We'll write basic EOS selection
        _output.AppendLine("SELEC");
        _output.AppendLine("----1----*----2----*----3----*----4----*----5----*----6----*----7----*----8");

        // Default to EOS1 (water, single-phase)
        _output.AppendLine("1");

        _output.AppendLine();
    }

    private void WriteMomopBlock()
    {
        // MOMOP - More output options
        // Control additional output parameters
        _output.AppendLine("MOMOP");
        _output.AppendLine("----1----*----2----*----3----*----4----*----5----*----6----*----7----*----8");

        // Write default MOP array (40 integers, typically 0 or 1)
        // MOP(1) through MOP(40) control various output and computational options
        for (int i = 0; i < 40; i++)
        {
            int value = 0;

            // Set some common options
            if (i == 0) value = 1;  // MOP(1) - write results to SAVE file
            if (i == 1) value = 1;  // MOP(2) - formatted output

            _output.Append($"{value,5}");

            if ((i + 1) % 8 == 0)
            {
                _output.AppendLine();
            }
        }

        _output.AppendLine();
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
