// GeoscientistToolkit/UI/DatasetPanel.cs (Updated to inherit from BasePanel)
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using ImGuiNET;
using System.Numerics;

namespace GeoscientistToolkit.UI
{
    public class DatasetPanel : BasePanel
    {
        private string _searchFilter = "";
        private Action<Dataset> _onDatasetSelected;
        private Action _onImportClicked;
        
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
                // --- Group datasets by type ---
                var datasetsByType = datasets
                    .Where(d => string.IsNullOrEmpty(_searchFilter) || 
                               d.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
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
                                ImGui.PushID(dataset.GetHashCode());
                                
                                bool isSelected = false;
                                if (ImGui.Selectable(dataset.Name, isSelected))
                                {
                                    _onDatasetSelected?.Invoke(dataset);
                                }
                                
                                if (ImGui.IsItemHovered())
                                {
                                    ImGui.BeginTooltip();
                                    ImGui.TextUnformatted($"Name: {dataset.Name}");
                                    ImGui.TextUnformatted($"Type: {dataset.Type}");
                                    ImGui.TextUnformatted($"Path: {dataset.FilePath}");
                                    
                                    if (dataset is CtImageStackDataset ctDataset)
                                    {
                                        ImGui.Separator();
                                        ImGui.TextUnformatted($"Binning: {ctDataset.BinningSize}");
                                        ImGui.TextUnformatted($"Pixel Size: {ctDataset.PixelSize} {ctDataset.Unit}");
                                    }
                                    ImGui.EndTooltip();
                                }
                                
                                if (ImGui.BeginPopupContextItem())
                                {
                                    if (ImGui.MenuItem("View")) { _onDatasetSelected?.Invoke(dataset); }
                                    if (ImGui.MenuItem("Remove")) { ProjectManager.Instance.RemoveDataset(dataset); }
                                    ImGui.EndPopup();
                                }
                                
                                ImGui.PopID();
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
                _ => "[DATA]"
            };
        }
    }
}
