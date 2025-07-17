// GeoscientistToolkit/UI/MainWindow.cs
// This is the definitive, corrected version with the proper ImGui call order.
// It ensures all dockable windows are submitted within the main DockSpace Begin/End block.

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using ImGuiNET;

namespace GeoscientistToolkit.UI
{
    public class MainWindow
    {
        private readonly DatasetPanel _datasetPanel;
        private readonly PropertiesPanel _propertiesPanel;
        private readonly ImportDataModal _importDataModal;
        private readonly List<DatasetViewPanel> _openDatasetPanels = new();

        private bool _showDatasetPanel = true;
        private bool _showPropertiesPanel = true;

        private Dataset _selectedDataset;

        public MainWindow()
        {
            _datasetPanel = new DatasetPanel();
            _propertiesPanel = new PropertiesPanel();
            _importDataModal = new ImportDataModal();

            // Add some dummy data for demonstration
            ProjectManager.Instance.AddDataset(new CtImageStackDataset("Sample CT Stack", "/path/to/sample1"));
            ProjectManager.Instance.AddDataset(new CtImageStackDataset("Another CT", "/path/to/sample2"));
        }

        public void SubmitUI()
        {
            // === THIS IS THE REWRITTEN AND CORRECTED UI SUBMISSION LOGIC ===

            // Set up the main viewport to be a dockable window
            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(viewport.WorkPos);
            ImGui.SetNextWindowSize(viewport.WorkSize);
            ImGui.SetNextWindowViewport(viewport.ID);

            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0.0f, 0.0f));
            
            ImGuiWindowFlags windowFlags = ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.MenuBar; // Use MenuBar flag
            windowFlags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;
            windowFlags |= ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus;

            // Begin the main window which will contain everything
            ImGui.Begin("GeoscientistToolkit DockSpace", windowFlags);
            ImGui.PopStyleVar(3);

            // Create the main menu bar *inside* the main window
            SubmitMainMenu();
            
            // Create the DockSpace that all other windows will dock into
            var dockspaceId = ImGui.GetID("MyDockSpace");
            ImGui.DockSpace(dockspaceId, new Vector2(0.0f, 0.0f), ImGuiDockNodeFlags.None);

            // Submit all our other UI panels. Because they are called after ImGui.DockSpace()
            // and within the main Begin/End block, they will be dockable.
            if (_showDatasetPanel)
            {
                _datasetPanel.Submit(ref _showDatasetPanel, OnDatasetSelected);
            }

            if (_showPropertiesPanel)
            {
                _propertiesPanel.Submit(ref _showPropertiesPanel, _selectedDataset);
            }

            // Submit and manage open dataset view panels
            for (int i = _openDatasetPanels.Count - 1; i >= 0; i--)
            {
                var panel = _openDatasetPanels[i];
                bool isOpen = true;
                panel.Submit(ref isOpen);
                if (!isOpen)
                {
                    _openDatasetPanels.RemoveAt(i);
                }
            }

            // The import modal is a popup, so its position in the code doesn't matter as much,
            // but it's good practice to keep it with the other UI submissions.
            if (_importDataModal.IsOpen)
            {
                _importDataModal.Submit();
            }

            // End the main window
            ImGui.End();
        }

        private void OnDatasetSelected(Dataset dataset)
        {
            _selectedDataset = dataset;
            if (_openDatasetPanels.All(p => p.Dataset != dataset))
            {
                _openDatasetPanels.Add(new DatasetViewPanel(dataset));
            }
        }

        private void SubmitMainMenu()
        {
            // This is now called within the main window's Begin/End block
            if (ImGui.BeginMenuBar())
            {
                if (ImGui.BeginMenu("File"))
                {
                    if (ImGui.MenuItem("New Project")) { ProjectManager.Instance.NewProject(); }
                    if (ImGui.MenuItem("Load Project")) { /* Logic to open file dialog */ }
                    if (ImGui.MenuItem("Save Project")) { /* Logic to open file dialog */ }
                    ImGui.Separator();
                    if (ImGui.MenuItem("Import Data")) { _importDataModal.Open(); }
                    if (ImGui.MenuItem("Export Data")) { /* To be implemented */ }
                    if (ImGui.MenuItem("Compress Data")) { /* To be implemented */ }
                    ImGui.Separator();
                    if (ImGui.MenuItem("Exit")) { Environment.Exit(0); }
                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("Edit"))
                {
                    // Add Edit menu items here
                    ImGui.EndMenu();
                }
                
                if (ImGui.BeginMenu("View"))
                {
                    ImGui.MenuItem("Datasets Panel", "", ref _showDatasetPanel);
                    ImGui.MenuItem("Properties Panel", "", ref _showPropertiesPanel);
                    ImGui.EndMenu();
                }
                
                if (ImGui.BeginMenu("Help"))
                {
                    // Add Help menu items here
                    ImGui.EndMenu();
                }
                
                ImGui.EndMenuBar();
            }
        }
    }
}