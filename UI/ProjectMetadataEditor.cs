// GeoscientistToolkit/UI/ProjectMetadataEditor.cs
using System;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Util;
using ImGuiNET;
using System.Numerics;

namespace GeoscientistToolkit.UI
{
    public class ProjectMetadataEditor
    {
        private bool _isOpen = false;
        private ProjectMetadata _tempMetadata;
        
        // Form fields
        private string _organisation = "";
        private string _department = "";
        private string _yearStr = "";
        private string _expedition = "";
        private string _author = "";
        private string _projectDescription = "";
        private string _startDateStr = "";
        private string _endDateStr = "";
        private string _fundingSource = "";
        private string _license = "";
        
        // Custom fields
        private string _newFieldKey = "";
        private string _newFieldValue = "";

        public void Open()
        {
            _tempMetadata = ProjectManager.Instance.ProjectMetadata.Clone();
            LoadFromMetadata();
            _isOpen = true;
        }

        private void LoadFromMetadata()
        {
            _organisation = _tempMetadata.Organisation ?? "";
            _department = _tempMetadata.Department ?? "";
            _yearStr = _tempMetadata.Year?.ToString() ?? "";
            _expedition = _tempMetadata.Expedition ?? "";
            _author = _tempMetadata.Author ?? "";
            _projectDescription = _tempMetadata.ProjectDescription ?? "";
            _startDateStr = _tempMetadata.StartDate?.ToString("yyyy-MM-dd") ?? "";
            _endDateStr = _tempMetadata.EndDate?.ToString("yyyy-MM-dd") ?? "";
            _fundingSource = _tempMetadata.FundingSource ?? "";
            _license = _tempMetadata.License ?? "";
        }

        public void Submit()
        {
            if (!_isOpen) return;

            ImGui.SetNextWindowSize(new Vector2(600, 650), ImGuiCond.FirstUseEver);
            var center = ImGui.GetMainViewport().GetCenter();
            ImGui.SetNextWindowPos(center, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));

            if (ImGui.Begin("Project Metadata###ProjectMetadataEditor", ref _isOpen))
            {
                // Project Information
                if (ImGui.CollapsingHeader("Project Information", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.Indent();
                    
                    ImGui.Text("Organisation:");
                    ImGui.SetNextItemWidth(-1);
                    ImGui.InputText("##Organisation", ref _organisation, 256);
                    
                    ImGui.Text("Department:");
                    ImGui.SetNextItemWidth(-1);
                    ImGui.InputText("##Department", ref _department, 256);
                    
                    ImGui.Text("Year:");
                    ImGui.SetNextItemWidth(100);
                    ImGui.InputText("##Year", ref _yearStr, 4);
                    
                    ImGui.Text("Expedition:");
                    ImGui.SetNextItemWidth(-1);
                    ImGui.InputText("##Expedition", ref _expedition, 256);
                    
                    ImGui.Text("Author:");
                    ImGui.SetNextItemWidth(-1);
                    ImGui.InputText("##Author", ref _author, 256);
                    
                    ImGui.Unindent();
                }

                // Project Details
                if (ImGui.CollapsingHeader("Project Details", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.Indent();
                    
                    ImGui.Text("Project Description:");
                    ImGui.InputTextMultiline("##ProjectDescription", ref _projectDescription, 1024, new Vector2(-1, 100));
                    
                    ImGui.Text("Start Date (YYYY-MM-DD):");
                    ImGui.SetNextItemWidth(150);
                    ImGui.InputText("##StartDate", ref _startDateStr, 32);
                    
                    ImGui.Text("End Date (YYYY-MM-DD):");
                    ImGui.SetNextItemWidth(150);
                    ImGui.InputText("##EndDate", ref _endDateStr, 32);
                    
                    ImGui.Text("Funding Source:");
                    ImGui.SetNextItemWidth(-1);
                    ImGui.InputText("##FundingSource", ref _fundingSource, 256);
                    
                    ImGui.Text("License:");
                    ImGui.SetNextItemWidth(-1);
                    ImGui.InputText("##License", ref _license, 256);
                    
                    ImGui.Unindent();
                }

                // Custom Fields
                if (ImGui.CollapsingHeader("Custom Fields"))
                {
                    ImGui.Indent();
                    
                    // Display existing custom fields
                    foreach (var kvp in _tempMetadata.CustomFields)
                    {
                        ImGui.Text($"{kvp.Key}: {kvp.Value}");
                        ImGui.SameLine();
                        if (ImGui.SmallButton($"Remove##{kvp.Key}"))
                        {
                            _tempMetadata.CustomFields.Remove(kvp.Key);
                            break;
                        }
                    }
                    
                    ImGui.Separator();
                    
                    // Add new custom field
                    ImGui.Text("Add Custom Field:");
                    ImGui.SetNextItemWidth(150);
                    ImGui.InputText("##NewFieldKey", ref _newFieldKey, 64);
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(200);
                    ImGui.InputText("##NewFieldValue", ref _newFieldValue, 256);
                    ImGui.SameLine();
                    if (ImGui.Button("Add") && !string.IsNullOrWhiteSpace(_newFieldKey))
                    {
                        _tempMetadata.CustomFields[_newFieldKey] = _newFieldValue;
                        _newFieldKey = "";
                        _newFieldValue = "";
                    }
                    
                    ImGui.Unindent();
                }

                ImGui.Separator();

                // Buttons
                if (ImGui.Button("Save", new Vector2(100, 0)))
                {
                    SaveMetadata();
                    _isOpen = false;
                }
                
                ImGui.SameLine();
                
                if (ImGui.Button("Cancel", new Vector2(100, 0)))
                {
                    _isOpen = false;
                }

                ImGui.End();
            }
        }

        private void SaveMetadata()
        {
            _tempMetadata.Organisation = _organisation;
            _tempMetadata.Department = _department;
            
            if (int.TryParse(_yearStr, out int year))
                _tempMetadata.Year = year;
            else
                _tempMetadata.Year = null;
            
            _tempMetadata.Expedition = _expedition;
            _tempMetadata.Author = _author;
            _tempMetadata.ProjectDescription = _projectDescription;
            
            if (DateTime.TryParse(_startDateStr, out DateTime startDate))
                _tempMetadata.StartDate = startDate;
            else
                _tempMetadata.StartDate = null;
            
            if (DateTime.TryParse(_endDateStr, out DateTime endDate))
                _tempMetadata.EndDate = endDate;
            else
                _tempMetadata.EndDate = null;
            
            _tempMetadata.FundingSource = _fundingSource;
            _tempMetadata.License = _license;
            
            // Apply to project
            ProjectManager.Instance.ProjectMetadata = _tempMetadata;
            ProjectManager.Instance.HasUnsavedChanges = true;
            
            Logger.Log("Updated project metadata");
        }
    }
}