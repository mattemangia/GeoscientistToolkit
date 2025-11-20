// GeoscientistToolkit/Data/PhysicoChem/PhysicoChemTools.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Analysis.PhysicoChem;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.Exporters;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Data.PhysicoChem;

/// <summary>
/// Tools panel for PhysicoChem datasets - domain creation, BC setup,
/// simulation controls, and results export
/// </summary>
public class PhysicoChemTools : IDatasetTools
{
    private readonly ImGuiExportFileDialog _exportDialog;
    private readonly ImGuiExportFileDialog _datasetExportDialog;
    private readonly ImGuiExportFileDialog _tough2ExportDialog;

    // Domain creation state
    private string _newDomainName = "Domain";
    private int _geometryTypeIndex = 0;
    private readonly string[] _geometryTypes = Enum.GetNames(typeof(GeometryType));
    private Vector3 _domainCenter = Vector3.Zero;
    private Vector3 _domainSize = Vector3.One;
    private float _domainRadius = 0.5f;
    private float _domainHeight = 1.0f;
    private float _domainInnerRadius = 0.0f;

    // Material properties
    private float _porosity = 0.3f;
    private float _permeability = 1e-12f;
    private float _thermalConductivity = 2.0f;
    private float _specificHeat = 1000.0f;
    private float _density = 2500.0f;

    // Initial conditions
    private float _initialTemp = 298.15f;
    private float _initialPressure = 101325.0f;

    // Boundary condition state
    private string _newBCName = "BC";
    private int _bcTypeIndex = 0;
    private readonly string[] _bcTypes = Enum.GetNames(typeof(BoundaryType));
    private int _bcLocationIndex = 0;
    private readonly string[] _bcLocations = Enum.GetNames(typeof(BoundaryLocation));
    private int _bcVariableIndex = 0;
    private readonly string[] _bcVariables = Enum.GetNames(typeof(BoundaryVariable));
    private float _bcValue = 0.0f;
    private float _bcFluxValue = 0.0f;

    // Force field state
    private string _newForceName = "Force";
    private int _forceTypeIndex = 0;
    private readonly string[] _forceTypes = Enum.GetNames(typeof(ForceType));
    private Vector3 _gravityVector = new Vector3(0, 0, -9.81f);
    private Vector3 _vortexCenter = Vector3.Zero;
    private float _vortexStrength = 1.0f;
    private float _vortexRadius = 1.0f;

    // Nucleation state
    private string _newNucleationName = "Nucleation";
    private Vector3 _nucleationPos = Vector3.Zero;
    private string _mineralType = "Calcite";
    private float _nucleationRate = 1e3f;

    // Simulation state
    private bool _isSimulating = false;
    private float _simulationProgress = 0.0f;
    private string _simulationStatus = "";

    // Selected items
    private int _selectedDomainIndex = -1;
    private int _selectedBCIndex = -1;
    private int _selectedForceIndex = -1;
    private int _selectedNucleationIndex = -1;

    // Material/compound selection
    private string _mineralSearchFilter = "";
    private List<string> _selectedMinerals = new();
    private Dictionary<string, float> _mineralFractions = new();

    // Cell splitting state
    private int _xDivisions = 10;
    private int _yDivisions = 10;
    private int _zDivisions = 10;

    // Cell selection state
    private string _selectedCellID = null;

    // Voronoi meshing state
    private int _selectedBoreholeIndex = 0;
    private int _voronoiLayers = 5;
    private float _voronoiRadius = 100.0f;
    private float _voronoiHeight = 10.0f;

    // Mesh import state
    private int _selectedMeshIndex = 0;
    private float _meshHeight = 10.0f;

    public PhysicoChemTools()
    {
        _exportDialog = new ImGuiExportFileDialog("ExportPhysicoChemDialog", "Export Results");
        _exportDialog.SetExtensions(
            (".csv", "CSV File"),
            (".vtk", "VTK File"),
            (".json", "JSON Results")
        );

        _datasetExportDialog = new ImGuiExportFileDialog("ExportPhysicoChemDatasetDialog", "Export Dataset");
        _datasetExportDialog.SetExtensions(
            (".physicochem", "PhysicoChem Dataset")
        );

        _tough2ExportDialog = new ImGuiExportFileDialog("ExportTough2Dialog", "Export to TOUGH2");
        _tough2ExportDialog.SetExtensions(
            (".dat", "TOUGH2 Input File"),
            (".inp", "TOUGH2 Input File"),
            (".tough2", "TOUGH2 Input File")
        );
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not PhysicoChemDataset pcDataset)
        {
            ImGui.TextDisabled("This panel only works with PhysicoChem datasets.");
            return;
        }

        ImGui.Text("PhysicoChem Reactor Simulation Tools");
        ImGui.Separator();

        // Domain Management
        if (ImGui.CollapsingHeader("Domains", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawDomainManagement(pcDataset);
        }

        ImGui.Spacing();

        // Boundary Conditions
        if (ImGui.CollapsingHeader("Boundary Conditions"))
        {
            DrawBoundaryConditions(pcDataset);
        }

        ImGui.Spacing();

        // Force Fields
        if (ImGui.CollapsingHeader("Force Fields"))
        {
            DrawForceFields(pcDataset);
        }

        ImGui.Spacing();

        // Nucleation Sites
        if (ImGui.CollapsingHeader("Nucleation Sites"))
        {
            DrawNucleationSites(pcDataset);
        }

        ImGui.Spacing();

        // Simulation Parameters
        if (ImGui.CollapsingHeader("Simulation Parameters", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawSimulationParameters(pcDataset);
        }

        ImGui.Spacing();

        // Simulation Controls
        if (ImGui.CollapsingHeader("Simulation Controls", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawSimulationControls(pcDataset);
        }

        ImGui.Spacing();

        // Export
        if (ImGui.CollapsingHeader("Export"))
        {
            DrawExportOptions(pcDataset);
        }

        // Handle export dialogs
        if (_exportDialog.IsOpen)
        {
            if (_exportDialog.Submit())
            {
                var selectedPath = _exportDialog.SelectedPath;
                ExportResults(pcDataset, selectedPath);
            }
        }

        if (_datasetExportDialog.IsOpen)
        {
            if (_datasetExportDialog.Submit())
            {
                var selectedPath = _datasetExportDialog.SelectedPath;
                ExportDatasetToBinary(pcDataset, selectedPath);
            }
        }

        if (_tough2ExportDialog.IsOpen)
        {
            if (_tough2ExportDialog.Submit())
            {
                var selectedPath = _tough2ExportDialog.SelectedPath;
                ExportToTough2(pcDataset, selectedPath);
            }
        }
    }

    private void DrawDomainManagement(PhysicoChemDataset dataset)
    {
        ImGui.Text($"Total Cells: {dataset.Mesh.Cells.Count}");
        ImGui.Text($"Total Materials: {dataset.Materials.Count}");
        ImGui.Separator();

        ImGui.Text("Cell Management");
        ImGui.Separator();

        // Cell list
        ImGui.BeginChild("cell_list", new Vector2(200, 200), ImGuiChildFlags.Border);
        foreach (var cell in dataset.Mesh.Cells.Values)
        {
            if (ImGui.Selectable(cell.ID, cell.ID == _selectedCellID))
            {
                _selectedCellID = cell.ID;
            }
        }
        ImGui.EndChild();

        ImGui.SameLine();

        // Cell properties
        ImGui.BeginChild("cell_properties", new Vector2(0, 200), ImGuiChildFlags.Border);
        if (_selectedCellID != null && dataset.Mesh.Cells.TryGetValue(_selectedCellID, out var selectedCell))
        {
            ImGui.Text($"Properties for Cell: {selectedCell.ID}");
            ImGui.Separator();

            bool isActive = selectedCell.IsActive;
            if (ImGui.Checkbox("Is Active", ref isActive))
            {
                selectedCell.IsActive = isActive;
            }

            var materialIDs = dataset.Materials.Select(m => m.MaterialID).ToArray();
            var currentMaterialIndex = Array.IndexOf(materialIDs, selectedCell.MaterialID);
            if (ImGui.Combo("Material", ref currentMaterialIndex, materialIDs, materialIDs.Length))
            {
                selectedCell.MaterialID = materialIDs[currentMaterialIndex];
            }
        }
        else
        {
            ImGui.Text("Select a cell to view its properties.");
        }
        ImGui.EndChild();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Split Mesh into Grid:");

        ImGui.InputInt("X Divisions", ref _xDivisions);
        ImGui.InputInt("Y Divisions", ref _yDivisions);
        ImGui.InputInt("Z Divisions", ref _zDivisions);

        if (ImGui.Button("Split"))
        {
            try
            {
                dataset.Mesh.SplitIntoGrid(_xDivisions, _yDivisions, _zDivisions);
                Logger.Log($"Split mesh into a {_xDivisions}x{_yDivisions}x{_zDivisions} grid.");
                ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to split mesh: {ex.Message}");
            }
        }


        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Generate Voronoi Mesh around Well:");

        var boreholes = ProjectManager.Instance.LoadedDatasets.OfType<BoreholeDataset>().ToList();
        if (boreholes.Any())
        {
            var boreholeNames = boreholes.Select(b => b.WellName).ToArray();
            ImGui.Combo("Borehole", ref _selectedBoreholeIndex, boreholeNames, boreholeNames.Length);
            ImGui.InputInt("Layers", ref _voronoiLayers);
            ImGui.InputFloat("Radius", ref _voronoiRadius);
            ImGui.InputFloat("Height", ref _voronoiHeight);

            if (ImGui.Button("Generate Voronoi Mesh"))
            {
                try
                {
                    var selectedBorehole = boreholes[_selectedBoreholeIndex];
                    dataset.Mesh.GenerateVoronoiMesh(selectedBorehole, _voronoiLayers, _voronoiRadius, _voronoiHeight);
                    Logger.Log($"Generated Voronoi mesh with {_voronoiLayers} layers and a radius of {_voronoiRadius} around {selectedBorehole.WellName}.");
                    ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to generate Voronoi mesh: {ex.Message}");
                }
            }
        }
        else
        {
            ImGui.TextDisabled("No boreholes found in the project. Add a borehole to generate a Voronoi mesh.");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Import Mesh from Mesh3DDataset:");

        var meshes = ProjectManager.Instance.LoadedDatasets.OfType<Mesh3DDataset>().ToList();
        if (meshes.Any())
        {
            var meshNames = meshes.Select(m => m.Name).ToArray();
            ImGui.Combo("Mesh", ref _selectedMeshIndex, meshNames, meshNames.Length);
            ImGui.InputFloat("Height##mesh", ref _meshHeight);

            if (ImGui.Button("Import Mesh"))
            {
                try
                {
                    var selectedMesh = meshes[_selectedMeshIndex];
                    dataset.Mesh.FromMesh3DDataset(selectedMesh, _meshHeight);
                    Logger.Log($"Imported mesh from {selectedMesh.Name}.");
                    ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to import mesh: {ex.Message}");
                }
            }
        }
        else
        {
            ImGui.TextDisabled("No 3D meshes found in the project. Add a 3D mesh to import it.");
        }
    }


    private void DrawBoundaryConditions(PhysicoChemDataset dataset)
    {
        // List existing BCs
        ImGui.Text($"Existing Boundary Conditions: {dataset.BoundaryConditions.Count}");
        ImGui.Separator();

        for (int i = 0; i < dataset.BoundaryConditions.Count; i++)
        {
            var bc = dataset.BoundaryConditions[i];
            bool isSelected = i == _selectedBCIndex;
            bool isActive = bc.IsActive;

            if (ImGui.Checkbox($"##bc_active{i}", ref isActive))
            {
                bc.IsActive = isActive;
                ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
            }

            ImGui.SameLine();

            if (ImGui.Selectable($"{bc.Name} ({bc.Type}, {bc.Variable})##bc{i}", isSelected))
            {
                _selectedBCIndex = i;
            }

            if (ImGui.BeginPopupContextItem($"bc_ctx_{i}"))
            {
                if (ImGui.MenuItem("Delete"))
                {
                    dataset.BoundaryConditions.RemoveAt(i);
                    _selectedBCIndex = -1;
                    ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
                }
                ImGui.EndPopup();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Add New Boundary Condition:");

        // BC configuration
        ImGui.InputText("Name##bc", ref _newBCName, 64);
        ImGui.Combo("Type##bc", ref _bcTypeIndex, _bcTypes, _bcTypes.Length);
        ImGui.Combo("Location##bc", ref _bcLocationIndex, _bcLocations, _bcLocations.Length);
        ImGui.Combo("Variable##bc", ref _bcVariableIndex, _bcVariables, _bcVariables.Length);

        var bcType = (BoundaryType)_bcTypeIndex;

        if (bcType == BoundaryType.FixedValue || bcType == BoundaryType.Convective)
        {
            ImGui.InputFloat("Value##bc", ref _bcValue, 0, 0, "%.2e");
        }

        if (bcType == BoundaryType.FixedFlux)
        {
            ImGui.InputFloat("Flux Value##bc", ref _bcFluxValue, 0, 0, "%.2e");
        }

        if (ImGui.Button("Add Boundary Condition"))
        {
            var bc = new BoundaryCondition
            {
                Name = _newBCName,
                Type = bcType,
                Location = (BoundaryLocation)_bcLocationIndex,
                Variable = (BoundaryVariable)_bcVariableIndex,
                Value = _bcValue,
                FluxValue = _bcFluxValue
            };

            dataset.BoundaryConditions.Add(bc);
            ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
            Logger.Log($"Added boundary condition: {_newBCName}");

            _newBCName = "BC" + (dataset.BoundaryConditions.Count + 1);
        }
    }

    private void DrawForceFields(PhysicoChemDataset dataset)
    {
        // List existing forces
        ImGui.Text($"Existing Force Fields: {dataset.Forces.Count}");
        ImGui.Separator();

        for (int i = 0; i < dataset.Forces.Count; i++)
        {
            var force = dataset.Forces[i];
            bool isSelected = i == _selectedForceIndex;
            bool isActive = force.IsActive;

            if (ImGui.Checkbox($"##force_active{i}", ref isActive))
            {
                force.IsActive = isActive;
                ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
            }

            ImGui.SameLine();

            if (ImGui.Selectable($"{force.Name} ({force.Type})##force{i}", isSelected))
            {
                _selectedForceIndex = i;
            }

            if (ImGui.BeginPopupContextItem($"force_ctx_{i}"))
            {
                if (ImGui.MenuItem("Delete"))
                {
                    dataset.Forces.RemoveAt(i);
                    _selectedForceIndex = -1;
                    ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
                }
                ImGui.EndPopup();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Add New Force Field:");

        // Force configuration
        ImGui.InputText("Name##force", ref _newForceName, 64);
        ImGui.Combo("Type##force", ref _forceTypeIndex, _forceTypes, _forceTypes.Length);

        var forceType = (ForceType)_forceTypeIndex;

        switch (forceType)
        {
            case ForceType.Gravity:
                ImGui.DragFloat3("Gravity Vector (m/s²)", ref _gravityVector, 0.1f);
                break;

            case ForceType.Vortex:
            case ForceType.Centrifugal:
                ImGui.DragFloat3("Center", ref _vortexCenter, 0.1f);
                ImGui.DragFloat("Strength (rad/s)", ref _vortexStrength, 0.1f, 0.0f, 100.0f);
                ImGui.DragFloat("Radius (m)", ref _vortexRadius, 0.1f, 0.1f, 50.0f);
                break;
        }

        if (ImGui.Button("Add Force Field"))
        {
            var force = new ForceField(_newForceName, forceType);

            if (forceType == ForceType.Gravity)
            {
                force.GravityVector = (_gravityVector.X, _gravityVector.Y, _gravityVector.Z);
            }
            else if (forceType == ForceType.Vortex || forceType == ForceType.Centrifugal)
            {
                force.VortexCenter = (_vortexCenter.X, _vortexCenter.Y, _vortexCenter.Z);
                force.VortexStrength = _vortexStrength;
                force.VortexRadius = _vortexRadius;
            }

            dataset.Forces.Add(force);
            ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
            Logger.Log($"Added force field: {_newForceName}");

            _newForceName = "Force" + (dataset.Forces.Count + 1);
        }
    }

    private void DrawNucleationSites(PhysicoChemDataset dataset)
    {
        // List existing nucleation sites
        ImGui.Text($"Existing Nucleation Sites: {dataset.NucleationSites.Count}");
        ImGui.Separator();

        for (int i = 0; i < dataset.NucleationSites.Count; i++)
        {
            var site = dataset.NucleationSites[i];
            bool isSelected = i == _selectedNucleationIndex;
            bool isActive = site.IsActive;

            if (ImGui.Checkbox($"##nuc_active{i}", ref isActive))
            {
                site.IsActive = isActive;
                ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
            }

            ImGui.SameLine();

            if (ImGui.Selectable($"{site.Name} ({site.MineralType})##nuc{i}", isSelected))
            {
                _selectedNucleationIndex = i;
            }

            if (ImGui.BeginPopupContextItem($"nuc_ctx_{i}"))
            {
                if (ImGui.MenuItem("Delete"))
                {
                    dataset.NucleationSites.RemoveAt(i);
                    _selectedNucleationIndex = -1;
                    ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
                }
                ImGui.EndPopup();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Add New Nucleation Site:");

        // Nucleation configuration
        ImGui.InputText("Name##nuc", ref _newNucleationName, 64);
        ImGui.DragFloat3("Position", ref _nucleationPos, 0.1f);
        ImGui.InputText("Mineral Type##nuc", ref _mineralType, 64);
        ImGui.InputFloat("Nucleation Rate (nuclei/s)", ref _nucleationRate, 0, 0, "%.2e");

        if (ImGui.Button("Add Nucleation Site"))
        {
            var site = new NucleationSite(_newNucleationName,
                (_nucleationPos.X, _nucleationPos.Y, _nucleationPos.Z),
                _mineralType)
            {
                NucleationRate = _nucleationRate
            };

            dataset.NucleationSites.Add(site);
            ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
            Logger.Log($"Added nucleation site: {_newNucleationName}");

            _newNucleationName = "Nucleation" + (dataset.NucleationSites.Count + 1);
        }
    }

    private void DrawSimulationParameters(PhysicoChemDataset dataset)
    {
        var simParams = dataset.SimulationParams;

        // Use temporary float variables for editing
        float totalTime = (float)simParams.TotalTime;
        float timeStep = (float)simParams.TimeStep;
        float outputInterval = (float)simParams.OutputInterval;
        float convergenceTolerance = (float)simParams.ConvergenceTolerance;

        if (ImGui.DragFloat("Total Time (s)", ref totalTime, 10.0f, 1.0f, 1e6f))
            simParams.TotalTime = totalTime;

        if (ImGui.DragFloat("Time Step (s)", ref timeStep, 0.1f, 0.001f, 100.0f))
            simParams.TimeStep = timeStep;

        if (ImGui.DragFloat("Output Interval (s)", ref outputInterval, 1.0f, 0.1f, 1000.0f))
            simParams.OutputInterval = outputInterval;

        ImGui.Separator();

        bool enableReactiveTransport = simParams.EnableReactiveTransport;
        bool enableHeatTransfer = simParams.EnableHeatTransfer;
        bool enableFlow = simParams.EnableFlow;
        bool enableForces = simParams.EnableForces;
        bool enableNucleation = simParams.EnableNucleation;
        bool useGPU = simParams.UseGPU;
        int maxIterations = simParams.MaxIterations;

        if (ImGui.Checkbox("Enable Reactive Transport", ref enableReactiveTransport))
            simParams.EnableReactiveTransport = enableReactiveTransport;
        if (ImGui.Checkbox("Enable Heat Transfer", ref enableHeatTransfer))
            simParams.EnableHeatTransfer = enableHeatTransfer;
        if (ImGui.Checkbox("Enable Flow", ref enableFlow))
            simParams.EnableFlow = enableFlow;
        if (ImGui.Checkbox("Enable Forces", ref enableForces))
            simParams.EnableForces = enableForces;
        if (ImGui.Checkbox("Enable Nucleation", ref enableNucleation))
            simParams.EnableNucleation = enableNucleation;

        ImGui.Separator();

        if (ImGui.Checkbox("Use GPU", ref useGPU))
            simParams.UseGPU = useGPU;

        if (ImGui.InputFloat("Convergence Tolerance", ref convergenceTolerance, 0, 0, "%.2e"))
            simParams.ConvergenceTolerance = convergenceTolerance;

        if (ImGui.InputInt("Max Iterations", ref maxIterations))
            simParams.MaxIterations = maxIterations;
    }

    private void DrawSimulationControls(PhysicoChemDataset dataset)
    {
        // Validation
        var errors = dataset.Validate();
        if (errors.Count > 0)
        {
            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), "Validation Errors:");
            foreach (var error in errors)
            {
                ImGui.BulletText(error);
            }
            ImGui.Separator();
        }

        // Run simulation button
        bool canRun = !_isSimulating && errors.Count == 0 && dataset.GeneratedMesh != null;

        if (!canRun) ImGui.BeginDisabled();

        if (ImGui.Button("Run Simulation", new Vector2(150, 30)))
        {
            RunSimulation(dataset);
        }

        if (!canRun) ImGui.EndDisabled();

        ImGui.SameLine();

        // Stop button
        if (_isSimulating)
        {
            if (ImGui.Button("Stop", new Vector2(80, 30)))
            {
                _isSimulating = false;
                _simulationStatus = "Stopped by user";
            }
        }

        // Progress bar
        if (_isSimulating)
        {
            ImGui.ProgressBar(_simulationProgress, new Vector2(-1, 0), $"{_simulationProgress * 100:F1}%");
            ImGui.Text(_simulationStatus);
        }

        // Results info
        if (dataset.ResultHistory != null && dataset.ResultHistory.Count > 0)
        {
            ImGui.Separator();
            ImGui.Text($"Results: {dataset.ResultHistory.Count} timesteps");
            ImGui.Text($"Current time: {dataset.CurrentState?.CurrentTime:F2}s");
        }
    }

    private void RunSimulation(PhysicoChemDataset dataset)
    {
        _isSimulating = true;
        _simulationProgress = 0.0f;
        _simulationStatus = "Initializing...";

        try
        {
            // Initialize state
            dataset.InitializeState();
            _simulationStatus = "Running simulation...";

            // Create progress reporter
            var progress = new Progress<(float, string)>(report =>
            {
                _simulationProgress = report.Item1;
                _simulationStatus = report.Item2;
            });

            // Create solver
            var solver = new PhysicoChemSolver(dataset, progress);

            // Run simulation
            solver.RunSimulation();

            _isSimulating = false;
            _simulationProgress = 1.0f;
            _simulationStatus = "Simulation completed";

            ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
            Logger.Log($"Simulation completed: {dataset.ResultHistory.Count} timesteps");
        }
        catch (Exception ex)
        {
            _isSimulating = false;
            _simulationStatus = $"Error: {ex.Message}";
            Logger.LogError($"Simulation failed: {ex.Message}");
        }
    }

    private void DrawExportOptions(PhysicoChemDataset dataset)
    {
        ImGui.Text("Export options:");

        // Export full dataset (always available)
        if (ImGui.Button("Export Dataset...", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
        {
            _datasetExportDialog.Open();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("(Full dataset with all configuration)");

        ImGui.Spacing();

        // Export to TOUGH2 format
        if (ImGui.Button("Export to TOUGH2...", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
        {
            _tough2ExportDialog.Open();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("(Multiphysics subsurface flow simulator)");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Export simulation results:");

        bool hasResults = dataset.ResultHistory != null && dataset.ResultHistory.Count > 0;

        if (!hasResults) ImGui.BeginDisabled();

        if (ImGui.Button("Export Results...", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
        {
            _exportDialog.Open();
        }

        if (!hasResults)
        {
            ImGui.EndDisabled();
            ImGui.TextDisabled("Run simulation first to generate results");
        }
    }

    private void ExportResults(PhysicoChemDataset dataset, string path)
    {
        try
        {
            string ext = System.IO.Path.GetExtension(path).ToLower();

            switch (ext)
            {
                case ".csv":
                    ExportToCSV(dataset, path);
                    break;
                case ".vtk":
                    ExportToVTK(dataset, path);
                    break;
                case ".json":
                    ExportToJSON(dataset, path);
                    break;
            }

            Logger.Log($"Exported results to: {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Export failed: {ex.Message}");
        }
    }

    private void ExportToCSV(PhysicoChemDataset dataset, string path)
    {
        // Simplified CSV export
        using var writer = new System.IO.StreamWriter(path);
        writer.WriteLine("Time,Temperature_Avg,Pressure_Avg,Velocity_Avg");

        foreach (var state in dataset.ResultHistory)
        {
            float tempAvg = CalculateAverage(state.Temperature);
            float pressAvg = CalculateAverage(state.Pressure);
            float velAvg = CalculateVelocityMagnitudeAverage(state);

            writer.WriteLine($"{state.CurrentTime},{tempAvg},{pressAvg},{velAvg}");
        }
    }

    private void ExportToVTK(PhysicoChemDataset dataset, string path)
    {
        // VTK export would be more complex - placeholder
        Logger.Log("VTK export not yet implemented");
    }

    private void ExportToJSON(PhysicoChemDataset dataset, string path)
    {
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(dataset.ResultHistory,
            Newtonsoft.Json.Formatting.Indented);
        System.IO.File.WriteAllText(path, json);
    }

    private void ExportDatasetToBinary(PhysicoChemDataset dataset, string path)
    {
        try
        {
            // Use the ISerializableDataset interface to get the DTO
            var dto = dataset.ToSerializableObject() as PhysicoChemDatasetDTO;

            if (dto == null)
                throw new InvalidOperationException("Failed to serialize dataset to DTO");

            // Serialize to JSON
            var options = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            };

            var json = System.Text.Json.JsonSerializer.Serialize(dto, options);
            System.IO.File.WriteAllText(path, json);

            Logger.Log($"Exported dataset to: {path}");
            ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export dataset: {ex.Message}");
        }
    }

    private void ExportToTough2(PhysicoChemDataset dataset, string path)
    {
        try
        {
            Logger.Log($"Exporting PhysicoChemDataset to TOUGH2 format: {path}");

            var exporter = new Tough2Exporter();
            exporter.Export(dataset, path);

            Logger.Log($"Successfully exported to TOUGH2 format: {path}");
            ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export to TOUGH2: {ex.Message}");
        }
    }

    private float CalculateAverage(float[,,] field)
    {
        int nx = field.GetLength(0);
        int ny = field.GetLength(1);
        int nz = field.GetLength(2);

        float sum = 0;
        int count = 0;

        for (int i = 0; i < nx; i++)
        for (int j = 0; j < ny; j++)
        for (int k = 0; k < nz; k++)
        {
            sum += field[i, j, k];
            count++;
        }

        return count > 0 ? sum / count : 0;
    }

    private float CalculateVelocityMagnitudeAverage(PhysicoChemState state)
    {
        int nx = state.VelocityX.GetLength(0);
        int ny = state.VelocityX.GetLength(1);
        int nz = state.VelocityX.GetLength(2);

        float sum = 0;
        int count = 0;

        for (int i = 0; i < nx; i++)
        for (int j = 0; j < ny; j++)
        for (int k = 0; k < nz; k++)
        {
            float vx = state.VelocityX[i, j, k];
            float vy = state.VelocityY[i, j, k];
            float vz = state.VelocityZ[i, j, k];
            sum += MathF.Sqrt(vx * vx + vy * vy + vz * vz);
            count++;
        }

        return count > 0 ? sum / count : 0;
    }

    private void DrawMineralSelector()
    {
        var compoundLib = CompoundLibrary.Instance;
        var minerals = compoundLib.Compounds
            .Where(c => c.Phase == CompoundPhase.Solid && !string.IsNullOrEmpty(c.Name))
            .OrderBy(c => c.Name)
            .ToList();

        ImGui.Indent();

        // Search filter
        ImGui.InputText("Search##minerals", ref _mineralSearchFilter, 128);

        // Filtered mineral list
        var filteredMinerals = string.IsNullOrWhiteSpace(_mineralSearchFilter)
            ? minerals
            : minerals.Where(m => m.Name.Contains(_mineralSearchFilter, StringComparison.OrdinalIgnoreCase) ||
                                  (m.ChemicalFormula?.Contains(_mineralSearchFilter, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();

        ImGui.BeginChild("mineral_list", new Vector2(0, 150), ImGuiChildFlags.Border);

        foreach (var mineral in filteredMinerals.Take(20)) // Limit to 20 for performance
        {
            bool isSelected = _selectedMinerals.Contains(mineral.Name);

            if (ImGui.Checkbox($"##{mineral.Name}_check", ref isSelected))
            {
                if (isSelected)
                {
                    if (!_selectedMinerals.Contains(mineral.Name))
                    {
                        _selectedMinerals.Add(mineral.Name);
                        _mineralFractions[mineral.Name] = 1.0f / (_selectedMinerals.Count); // Equal distribution

                        // Renormalize fractions
                        RenormalizeMineralFractions();
                    }
                }
                else
                {
                    _selectedMinerals.Remove(mineral.Name);
                    _mineralFractions.Remove(mineral.Name);
                    RenormalizeMineralFractions();
                }
            }

            ImGui.SameLine();
            ImGui.Text($"{mineral.Name}");
            if (!string.IsNullOrEmpty(mineral.ChemicalFormula))
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"({mineral.ChemicalFormula})");
            }
        }

        ImGui.EndChild();

        // Show selected minerals with fractions
        if (_selectedMinerals.Count > 0)
        {
            ImGui.Spacing();
            ImGui.Text("Selected Minerals:");
            ImGui.Indent();

            var mineralsCopy = _selectedMinerals.ToList(); // Avoid modification during iteration
            foreach (var mineralName in mineralsCopy)
            {
                if (!_mineralFractions.ContainsKey(mineralName))
                    _mineralFractions[mineralName] = 0.1f;

                float fraction = _mineralFractions[mineralName];
                if (ImGui.SliderFloat($"{mineralName}##fraction", ref fraction, 0.0f, 1.0f, "%.3f"))
                {
                    _mineralFractions[mineralName] = fraction;
                    RenormalizeMineralFractions();
                }
            }

            ImGui.Unindent();

            // Show total
            float total = _mineralFractions.Values.Sum();
            if (Math.Abs(total - 1.0f) > 0.01f)
            {
                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), $"Warning: Total = {total:F3} (should be 1.0)");
            }
            else
            {
                ImGui.TextColored(new Vector4(0, 1, 0, 1), $"Total: {total:F3}");
            }

            if (ImGui.Button("Normalize Fractions"))
            {
                RenormalizeMineralFractions();
            }
        }
        else
        {
            ImGui.TextDisabled("No minerals selected");
        }

        ImGui.Unindent();
    }

    private void RenormalizeMineralFractions()
    {
        float total = _mineralFractions.Values.Sum();
        if (total > 0)
        {
            var keys = _mineralFractions.Keys.ToList();
            foreach (var key in keys)
            {
                _mineralFractions[key] /= total;
            }
        }
    }
}
