// GeoscientistToolkit/Data/PhysicoChem/PhysicoChemPropertiesRenderer.cs

using System;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

namespace GeoscientistToolkit.Data.PhysicoChem;

/// <summary>
/// Properties renderer for PhysicoChem datasets - displays dataset information,
/// domain statistics, simulation parameters, and results summary
/// </summary>
public class PhysicoChemPropertiesRenderer : IDatasetPropertiesRenderer
{
    public void Draw(Dataset dataset)
    {
        if (dataset is not PhysicoChemDataset pcDataset)
        {
            ImGui.TextDisabled("Invalid dataset type");
            return;
        }

        ImGui.Text("PhysicoChem Reactor Dataset");
        ImGui.Separator();

        // Basic info
        DrawBasicInfo(pcDataset);

        ImGui.Spacing();
        ImGui.Separator();

        // Domain statistics
        if (ImGui.CollapsingHeader("Domain Statistics", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawDomainStatistics(pcDataset);
        }

        ImGui.Spacing();

        // Boundary conditions summary
        if (ImGui.CollapsingHeader("Boundary Conditions"))
        {
            DrawBoundaryConditionsSummary(pcDataset);
        }

        ImGui.Spacing();

        // Force fields summary
        if (ImGui.CollapsingHeader("Force Fields"))
        {
            DrawForceFieldsSummary(pcDataset);
        }

        ImGui.Spacing();

        // Mesh info
        if (pcDataset.GeneratedMesh != null && ImGui.CollapsingHeader("Mesh Information"))
        {
            DrawMeshInfo(pcDataset);
        }

        ImGui.Spacing();

        // Simulation parameters
        if (ImGui.CollapsingHeader("Simulation Parameters"))
        {
            DrawSimulationParameters(pcDataset);
        }

        ImGui.Spacing();

        // Results summary
        if (pcDataset.ResultHistory != null && pcDataset.ResultHistory.Count > 0 &&
            ImGui.CollapsingHeader("Results Summary"))
        {
            DrawResultsSummary(pcDataset);
        }
    }

    private void DrawBasicInfo(PhysicoChemDataset dataset)
    {
        ImGui.Text($"Name: {dataset.Name}");
        if (!string.IsNullOrEmpty(dataset.Description))
        {
            ImGui.TextWrapped($"Description: {dataset.Description}");
        }

        ImGui.Text($"Type: {dataset.Type}");
        ImGui.Text($"Size: {FormatBytes(dataset.GetSizeInBytes())}");

        if (!string.IsNullOrEmpty(dataset.FilePath))
        {
            ImGui.Text($"Path: {dataset.FilePath}");
        }
    }

    private void DrawDomainStatistics(PhysicoChemDataset dataset)
    {
        ImGui.Text($"Total Domains: {dataset.Domains.Count}");

        if (dataset.Domains.Count == 0)
        {
            ImGui.TextDisabled("No domains defined");
            return;
        }

        ImGui.Spacing();

        // Count by geometry type
        var geometryGroups = dataset.Domains.GroupBy(d => d.Geometry?.Type ?? GeometryType.Box);
        foreach (var group in geometryGroups)
        {
            ImGui.BulletText($"{group.Key}: {group.Count()}");
        }

        ImGui.Spacing();

        // Material property ranges
        if (dataset.Domains.Any(d => d.Material != null))
        {
            ImGui.Text("Material Property Ranges:");

            var materials = dataset.Domains.Where(d => d.Material != null).Select(d => d.Material).ToList();

            if (materials.Count > 0)
            {
                var porosityRange = (materials.Min(m => m.Porosity), materials.Max(m => m.Porosity));
                var permRange = (materials.Min(m => m.Permeability), materials.Max(m => m.Permeability));
                var densityRange = (materials.Min(m => m.Density), materials.Max(m => m.Density));

                ImGui.Indent();
                ImGui.Text($"Porosity: {porosityRange.Item1:F3} - {porosityRange.Item2:F3}");
                ImGui.Text($"Permeability: {permRange.Item1.ToString("E2")} - {permRange.Item2.ToString("E2")} m²");
                ImGui.Text($"Density: {densityRange.Item1:F0} - {densityRange.Item2:F0} kg/m³");
                ImGui.Unindent();
            }
        }

        ImGui.Spacing();

        // Initial conditions ranges
        if (dataset.Domains.Any(d => d.InitialConditions != null))
        {
            ImGui.Text("Initial Conditions Ranges:");

            var ics = dataset.Domains.Where(d => d.InitialConditions != null)
                .Select(d => d.InitialConditions).ToList();

            if (ics.Count > 0)
            {
                var tempRange = (ics.Min(ic => ic.Temperature), ics.Max(ic => ic.Temperature));
                var pressRange = (ics.Min(ic => ic.Pressure), ics.Max(ic => ic.Pressure));

                ImGui.Indent();
                ImGui.Text($"Temperature: {tempRange.Item1:F2} - {tempRange.Item2:F2} K");
                ImGui.Text($"Pressure: {pressRange.Item1.ToString("E2")} - {pressRange.Item2.ToString("E2")} Pa");
                ImGui.Unindent();
            }
        }
    }

    private void DrawBoundaryConditionsSummary(PhysicoChemDataset dataset)
    {
        ImGui.Text($"Total BCs: {dataset.BoundaryConditions.Count}");

        if (dataset.BoundaryConditions.Count == 0)
        {
            ImGui.TextDisabled("No boundary conditions defined");
            return;
        }

        ImGui.Spacing();

        // Count by type
        var typeGroups = dataset.BoundaryConditions.GroupBy(bc => bc.Type);
        ImGui.Text("By Type:");
        ImGui.Indent();
        foreach (var group in typeGroups)
        {
            int active = group.Count(bc => bc.IsActive);
            ImGui.Text($"{group.Key}: {active}/{group.Count()} active");
        }
        ImGui.Unindent();

        ImGui.Spacing();

        // Count by variable
        var varGroups = dataset.BoundaryConditions.GroupBy(bc => bc.Variable);
        ImGui.Text("By Variable:");
        ImGui.Indent();
        foreach (var group in varGroups)
        {
            ImGui.Text($"{group.Key}: {group.Count()}");
        }
        ImGui.Unindent();
    }

    private void DrawForceFieldsSummary(PhysicoChemDataset dataset)
    {
        ImGui.Text($"Total Force Fields: {dataset.Forces.Count}");

        if (dataset.Forces.Count == 0)
        {
            ImGui.TextDisabled("No force fields defined");
            return;
        }

        ImGui.Spacing();

        // List by type
        var forceGroups = dataset.Forces.GroupBy(f => f.Type);
        foreach (var group in forceGroups)
        {
            int active = group.Count(f => f.IsActive);
            ImGui.BulletText($"{group.Key}: {active}/{group.Count()} active");
        }

        ImGui.Spacing();

        // Nucleation sites
        if (dataset.NucleationSites.Count > 0)
        {
            int activeNuc = dataset.NucleationSites.Count(n => n.IsActive);
            ImGui.Text($"Nucleation Sites: {activeNuc}/{dataset.NucleationSites.Count} active");

            if (dataset.NucleationSites.Any())
            {
                var minerals = dataset.NucleationSites.Select(n => n.MineralType).Distinct();
                ImGui.Indent();
                ImGui.Text("Minerals:");
                foreach (var mineral in minerals)
                {
                    int count = dataset.NucleationSites.Count(n => n.MineralType == mineral);
                    ImGui.BulletText($"{mineral}: {count}");
                }
                ImGui.Unindent();
            }
        }
    }

    private void DrawMeshInfo(PhysicoChemDataset dataset)
    {
        var mesh = dataset.GeneratedMesh;
        var gridSize = mesh.GridSize;

        ImGui.Text($"Grid Size: {gridSize.X} × {gridSize.Y} × {gridSize.Z}");
        ImGui.Text($"Total Cells: {gridSize.X * gridSize.Y * gridSize.Z:N0}");

        var spacing = mesh.Spacing;
        ImGui.Text($"Cell Spacing: {spacing.X:F4} × {spacing.Y:F4} × {spacing.Z:F4} m");

        var domainSize = mesh.GetDomainSize();
        var origin = mesh.Origin;
        ImGui.Text($"Bounds:");
        ImGui.Indent();
        ImGui.Text($"X: [{origin.X:F3}, {(origin.X + domainSize.X):F3}]");
        ImGui.Text($"Y: [{origin.Y:F3}, {(origin.Y + domainSize.Y):F3}]");
        ImGui.Text($"Z: [{origin.Z:F3}, {(origin.Z + domainSize.Z):F3}]");
        ImGui.Unindent();

        // Memory estimate
        long totalCells = gridSize.X * gridSize.Y * gridSize.Z;
        long memoryPerCell = 10 * sizeof(float); // Approximate fields per cell
        long estimatedMemory = totalCells * memoryPerCell;
        ImGui.Text($"Est. Memory: {FormatBytes(estimatedMemory)}");
    }

    private void DrawSimulationParameters(PhysicoChemDataset dataset)
    {
        var simParams = dataset.SimulationParams;

        ImGui.Text($"Total Time: {simParams.TotalTime:F2} s");
        ImGui.Text($"Time Step: {simParams.TimeStep:F4} s");
        ImGui.Text($"Output Interval: {simParams.OutputInterval:F2} s");

        int expectedSteps = (int)(simParams.TotalTime / simParams.TimeStep);
        int expectedOutputs = (int)(simParams.TotalTime / simParams.OutputInterval);
        ImGui.Text($"Expected Steps: {expectedSteps:N0}");
        ImGui.Text($"Expected Outputs: {expectedOutputs:N0}");

        ImGui.Spacing();
        ImGui.Text("Enabled Physics:");
        ImGui.Indent();

        if (simParams.EnableReactiveTransport)
            ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), "✓ Reactive Transport");
        else
            ImGui.TextDisabled("✗ Reactive Transport");

        if (simParams.EnableHeatTransfer)
            ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), "✓ Heat Transfer");
        else
            ImGui.TextDisabled("✗ Heat Transfer");

        if (simParams.EnableFlow)
            ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), "✓ Flow");
        else
            ImGui.TextDisabled("✗ Flow");

        if (simParams.EnableForces)
            ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), "✓ Forces");
        else
            ImGui.TextDisabled("✗ Forces");

        if (simParams.EnableNucleation)
            ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), "✓ Nucleation");
        else
            ImGui.TextDisabled("✗ Nucleation");

        ImGui.Unindent();

        ImGui.Spacing();
        ImGui.Text($"Solver: {simParams.SolverType}");
        ImGui.Text($"GPU Acceleration: {(simParams.UseGPU ? "Enabled" : "Disabled")}");
        ImGui.Text($"Convergence Tolerance: {simParams.ConvergenceTolerance:E2}");
        ImGui.Text($"Max Iterations: {simParams.MaxIterations}");
    }

    private void DrawResultsSummary(PhysicoChemDataset dataset)
    {
        ImGui.Text($"Timesteps Recorded: {dataset.ResultHistory.Count}");

        if (dataset.CurrentState != null)
        {
            ImGui.Text($"Current Time: {dataset.CurrentState.CurrentTime:F2} s");
        }

        if (dataset.ResultHistory.Count > 0)
        {
            var firstState = dataset.ResultHistory.First();
            var lastState = dataset.ResultHistory.Last();

            ImGui.Text($"Time Range: {firstState.CurrentTime:F2} - {lastState.CurrentTime:F2} s");

            ImGui.Spacing();
            ImGui.Text("Field Statistics (final state):");
            ImGui.Indent();

            // Temperature
            var (tempMin, tempMax, tempAvg) = GetFieldStats(lastState.Temperature);
            ImGui.Text($"Temperature: {tempMin:F2} - {tempMax:F2} K (avg: {tempAvg:F2} K)");

            // Pressure
            var (pressMin, pressMax, pressAvg) = GetFieldStats(lastState.Pressure);
            ImGui.Text($"Pressure: {pressMin.ToString("E2")} - {pressMax.ToString("E2")} Pa (avg: {pressAvg.ToString("E2")} Pa)");

            // Velocity magnitude
            var velStats = GetVelocityStats(lastState);
            ImGui.Text($"Velocity: {velStats.min.ToString("E2")} - {velStats.max.ToString("E2")} m/s (avg: {velStats.avg.ToString("E2")} m/s)");

            ImGui.Unindent();

            ImGui.Spacing();

            // Active nuclei
            if (lastState.ActiveNuclei.Count > 0)
            {
                ImGui.Text($"Active Nuclei: {lastState.ActiveNuclei.Count}");

                var mineralCounts = lastState.ActiveNuclei.GroupBy(n => n.MineralType);
                ImGui.Indent();
                foreach (var group in mineralCounts)
                {
                    ImGui.BulletText($"{group.Key}: {group.Count()}");
                }
                ImGui.Unindent();
            }
        }
    }

    private (float min, float max, float avg) GetFieldStats(float[,,] field)
    {
        int nx = field.GetLength(0);
        int ny = field.GetLength(1);
        int nz = field.GetLength(2);

        float min = float.MaxValue;
        float max = float.MinValue;
        float sum = 0;
        int count = 0;

        for (int i = 0; i < nx; i++)
        for (int j = 0; j < ny; j++)
        for (int k = 0; k < nz; k++)
        {
            float val = field[i, j, k];
            if (val < min) min = val;
            if (val > max) max = val;
            sum += val;
            count++;
        }

        float avg = count > 0 ? sum / count : 0;
        return (min, max, avg);
    }

    private (float min, float max, float avg) GetVelocityStats(PhysicoChemState state)
    {
        int nx = state.VelocityX.GetLength(0);
        int ny = state.VelocityX.GetLength(1);
        int nz = state.VelocityX.GetLength(2);

        float min = float.MaxValue;
        float max = float.MinValue;
        float sum = 0;
        int count = 0;

        for (int i = 0; i < nx; i++)
        for (int j = 0; j < ny; j++)
        for (int k = 0; k < nz; k++)
        {
            float vx = state.VelocityX[i, j, k];
            float vy = state.VelocityY[i, j, k];
            float vz = state.VelocityZ[i, j, k];
            float mag = MathF.Sqrt(vx * vx + vy * vy + vz * vz);

            if (mag < min) min = mag;
            if (mag > max) max = mag;
            sum += mag;
            count++;
        }

        float avg = count > 0 ? sum / count : 0;
        return (min, max, avg);
    }

    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }
}
