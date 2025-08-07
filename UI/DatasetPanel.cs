// GeoscientistToolkit/UI/DatasetPanel.cs (Updated with grouping and multi-close support)
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using ImGuiNET;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using GeoscientistToolkit.Data.Mesh3D;

namespace GeoscientistToolkit.UI
{
    public class DatasetPanel : BasePanel
    {
        private string _searchFilter = "";
        private Action<Dataset> _onDatasetSelected;
        private Action _onImportClicked;
        
        // Multi-selection state
        private HashSet<Dataset> _selectedDatasets = new HashSet<Dataset>();
        private Dataset _lastSelectedDataset = null;
        private List<Dataset> _orderedDatasets = new List<Dataset>(); // To maintain order for shift-selection
        
        public DatasetPanel() : base("Datasets", new Vector2(250, 400))
        {
        }
        
        public void Submit(ref bool pOpen, Action<Dataset> onDatasetSelected, Action onImportClicked)
        {
            _onDatasetSelected = onDatasetSelected;
            _onImportClicked = onImportClicked;
            base.Submit(ref pOpen);
        }
        
        protected override void DrawContent()
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##Search", "Search datasets...", ref _searchFilter, 256);
            ImGui.Separator();

            var datasets = ProjectManager.Instance.LoadedDatasets;
            
            // Update ordered datasets list for shift-selection
            _orderedDatasets = datasets
                .Where(d => string.IsNullOrEmpty(_searchFilter) || 
                           d.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d.Type)
                .ThenBy(d => d.Name)
                .ToList();
            
            if (datasets.Count == 0)
            {
                // --- Show empty state UI ---
                var windowWidth = ImGui.GetWindowSize().X;
                var textWidth = ImGui.CalcTextSize("No datasets loaded").X;
                ImGui.SetCursorPosX((windowWidth - textWidth) * 0.5f);
                ImGui.TextDisabled("No datasets loaded");
                
                ImGui.Spacing();
                
                var buttonText = "Import Data";
                var buttonWidth = ImGui.CalcTextSize(buttonText).X + ImGui.GetStyle().FramePadding.X * 2;
                ImGui.SetCursorPosX((windowWidth - buttonWidth) * 0.5f);
                
                if (ImGui.Button(buttonText))
                {
                    _onImportClicked?.Invoke();
                }
            }
            else
            {
                // Check for clicks outside any dataset to clear selection
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && ImGui.IsWindowHovered() && 
                    !ImGui.IsAnyItemHovered())
                {
                    _selectedDatasets.Clear();
                }
                
                // --- Group datasets by type ---
                var datasetsByType = _orderedDatasets
                    .GroupBy(d => d.Type)
                    .OrderBy(g => g.Key.ToString());

                if (!datasetsByType.Any())
                {
                    ImGui.TextDisabled("No datasets match the search");
                }
                else
                {
                    foreach (var group in datasetsByType)
                    {
                        string icon = GetIconForDatasetType(group.Key);
                        string headerText = $"{icon} {group.Key}";
                        
                        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.26f, 0.59f, 0.98f, 0.31f));
                        if (ImGui.TreeNodeEx(headerText, ImGuiTreeNodeFlags.DefaultOpen))
                        {
                            ImGui.PopStyleColor();
                            
                            foreach (var dataset in group.OrderBy(d => d.Name))
                            {
                                DrawDatasetItem(dataset);
                            }
                            ImGui.TreePop();
                        }
                        else
                        {
                            ImGui.PopStyleColor();
                        }
                    }
                }
            }
            
            ImGui.Separator();
            ImGui.TextDisabled($"{datasets.Count} dataset(s) loaded");
            if (_selectedDatasets.Count > 1)
            {
                ImGui.TextDisabled($"{_selectedDatasets.Count} selected");
            }
        }
        
        private void DrawDatasetItem(Dataset dataset, int indentLevel = 0)
        {
            ImGui.PushID(dataset.GetHashCode());
            
            // Apply indentation for grouped items
            if (indentLevel > 0)
            {
                ImGui.Indent(20f * indentLevel);
            }
            
            bool isSelected = _selectedDatasets.Contains(dataset);
            
            // Change color if dataset is missing
            if (dataset.IsMissing)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.4f, 0.4f, 1.0f));
            }
            
            // Handle selection
            if (ImGui.Selectable(dataset.Name, isSelected))
            {
                HandleDatasetSelection(dataset);
            }
            
            if (dataset.IsMissing)
            {
                ImGui.PopStyleColor();
            }
            
            // Show tooltip
            if (ImGui.IsItemHovered())
            {
                ShowDatasetTooltip(dataset);
            }
            
            // Context menu
            if (ImGui.BeginPopupContextItem())
            {
                // If right-clicking on an item that is not selected, clear 
                // the current selection and select only this one.
                if (!isSelected)
                {
                    _selectedDatasets.Clear();
                    _selectedDatasets.Add(dataset);
                    _lastSelectedDataset = dataset;
                }
                DrawContextMenu(dataset);
                ImGui.EndPopup();
            }
            
            // If this is a group, draw its children
            if (dataset is DatasetGroup group)
            {
                foreach (var child in group.Datasets)
                {
                    DrawDatasetItem(child, indentLevel + 1);
                }
            }
            
            if (indentLevel > 0)
            {
                ImGui.Unindent(20f * indentLevel);
            }
            
            ImGui.PopID();
        }
        
        private void HandleDatasetSelection(Dataset dataset)
        {
            if (dataset.IsMissing)
            {
                // Optionally prevent interaction with missing datasets
                return;
            }

            bool ctrlHeld = ImGui.GetIO().KeyCtrl;
            bool shiftHeld = ImGui.GetIO().KeyShift;
            
            if (ctrlHeld)
            {
                // Toggle selection
                if (_selectedDatasets.Contains(dataset))
                    _selectedDatasets.Remove(dataset);
                else
                    _selectedDatasets.Add(dataset);
            }
            else if (shiftHeld && _lastSelectedDataset != null)
            {
                // Range selection
                int startIdx = _orderedDatasets.IndexOf(_lastSelectedDataset);
                int endIdx = _orderedDatasets.IndexOf(dataset);
                
                if (startIdx != -1 && endIdx != -1)
                {
                    int minIdx = Math.Min(startIdx, endIdx);
                    int maxIdx = Math.Max(startIdx, endIdx);
                    
                    for (int i = minIdx; i <= maxIdx; i++)
                    {
                        if (!_orderedDatasets[i].IsMissing) // Don't select missing items in range
                            _selectedDatasets.Add(_orderedDatasets[i]);
                    }
                }
            }
            else
            {
                // Single selection
                _selectedDatasets.Clear();
                _selectedDatasets.Add(dataset);
                _onDatasetSelected?.Invoke(dataset);
            }
            
            _lastSelectedDataset = dataset;
        }
        
        private void ShowDatasetTooltip(Dataset dataset)
        {
            ImGui.BeginTooltip();
            if (dataset.IsMissing)
            {
                ImGui.TextColored(new Vector4(1.0f, 0.4f, 0.4f, 1.0f), "Source file or directory not found!");
            }
            ImGui.TextUnformatted($"Name: {dataset.Name}");
            ImGui.TextUnformatted($"Type: {dataset.Type}");
            ImGui.TextUnformatted($"Path: {dataset.FilePath}");
    
            if (dataset is CtImageStackDataset ctDataset)
            {
                ImGui.Separator();
                ImGui.TextUnformatted($"Binning: {ctDataset.BinningSize}");
                ImGui.TextUnformatted($"Pixel Size: {ctDataset.PixelSize} {ctDataset.Unit}");
            }
            else if (dataset is Mesh3DDataset mesh3D)
            {
                ImGui.Separator();
                ImGui.TextUnformatted($"Format: {mesh3D.FileFormat}");
                ImGui.TextUnformatted($"Vertices: {mesh3D.VertexCount:N0}");
                ImGui.TextUnformatted($"Faces: {mesh3D.FaceCount:N0}");
                if (mesh3D.Scale != 1.0f)
                {
                    ImGui.TextUnformatted($"Scale: {mesh3D.Scale:F2}x");
                }
            }
            else if (dataset is DatasetGroup group)
            {
                ImGui.Separator();
                ImGui.TextUnformatted($"Contains {group.Datasets.Count} datasets:");
                foreach (var child in group.Datasets)
                {
                    ImGui.TextUnformatted($"  • {child.Name}");
                }
            }
    
            ImGui.EndTooltip();
        }
        
        private void DrawContextMenu(Dataset dataset)
        {
            // View option
            if (ImGui.MenuItem("View", null, false, !(dataset is DatasetGroup) && !dataset.IsMissing))
            {
                _onDatasetSelected?.Invoke(dataset);
            }
            
            // Group-specific options
            if (dataset is DatasetGroup group)
            {
                if (ImGui.MenuItem("View Thumbnails"))
                {
                    // Signal to open thumbnail viewer
                    OnOpenThumbnailViewer?.Invoke(group);
                }
                
                if (ImGui.MenuItem("Ungroup"))
                {
                    UngroupDataset(group);
                }
            }
            
            // Multi-selection grouping
            if (_selectedDatasets.Count > 1 && _selectedDatasets.Contains(dataset))
            {
                if (ImGui.MenuItem("Group Selected"))
                {
                    CreateGroup();
                }
            }
            
            ImGui.Separator();
            
            // Close/Remove option - now acts on all selected items
            if (ImGui.MenuItem("Close"))
            {
                var itemsToClose = _selectedDatasets.ToList();
                
                foreach (var item in itemsToClose)
                {
                    if (item is DatasetGroup grp)
                    {
                        // Also remove all datasets within the group
                        foreach (var child in grp.Datasets.ToList())
                        {
                            ProjectManager.Instance.RemoveDataset(child);
                        }
                    }
                    ProjectManager.Instance.RemoveDataset(item);
                }
                
                _selectedDatasets.Clear();
            }
        }
        
        // Add this event at the top of the class with other fields
        public event Action<DatasetGroup> OnOpenThumbnailViewer;
        
        private void CreateGroup()
        {
            if (_selectedDatasets.Count < 2) return;
            
            // Create group name
            string groupName = $"Group {ProjectManager.Instance.LoadedDatasets.Count(d => d is DatasetGroup) + 1}";
            
            // Create the group
            var group = new DatasetGroup(groupName, _selectedDatasets.ToList());
            
            // Remove individual datasets from the project
            foreach (var dataset in _selectedDatasets)
            {
                ProjectManager.Instance.RemoveDataset(dataset);
            }
            
            // Add the group
            ProjectManager.Instance.AddDataset(group);
            
            // Clear selection
            _selectedDatasets.Clear();
        }
        
        private void UngroupDataset(DatasetGroup group)
        {
            // Add all datasets back to the project
            foreach (var dataset in group.Datasets)
            {
                ProjectManager.Instance.AddDataset(dataset);
            }
            
            // Remove the group
            ProjectManager.Instance.RemoveDataset(group);
            _selectedDatasets.Remove(group);
        }
        
        private string GetIconForDatasetType(DatasetType type)
        {
            return type switch
            {
                DatasetType.CtImageStack => "[STACK]",
                DatasetType.CtBinaryFile => "[BIN]",
                DatasetType.MicroXrf => "[XRF]",
                DatasetType.PointCloud => "[PCD]",
                DatasetType.Mesh => "[MESH]",
                DatasetType.SingleImage => "[IMG]",
                DatasetType.Group => "[GROUP]",
                DatasetType.Mesh3D => "[3D]",  
                _ => "[DATA]"
            };
        }
    }
}