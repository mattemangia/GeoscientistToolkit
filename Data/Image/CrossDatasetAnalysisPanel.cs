// GeoscientistToolkit/UI/CrossDatasetAnalysisPanel.cs (Fixed)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Data.CtImageStack;
using ImGuiNET;

namespace GeoscientistToolkit.UI
{
    /// <summary>
    /// Panel for cross-dataset analysis and integration
    /// </summary>
    public class CrossDatasetAnalysisPanel : BasePanel
    {
        private Dataset _selectedPrimaryDataset;
        private Dataset _selectedSecondaryDataset;
        private string _analysisType = "None";
        private List<Vector2> _linkPoints = new List<Vector2>();
        
        // SEM to CT correlation state
        private int _sliceIndex = 0;

        public CrossDatasetAnalysisPanel() : base("Cross-Dataset Analysis", new Vector2(600, 400))
        {
        }

        protected override void DrawContent()
        {
            ImGui.Text("Cross-Dataset Analysis and Integration");
            ImGui.Separator();

            // Dataset selection
            DrawDatasetSelection();
            
            ImGui.Separator();
            
            // Analysis type selection
            DrawAnalysisTypeSelection();
            
            ImGui.Separator();
            
            // Analysis-specific UI
            DrawAnalysisUI();
        }

        private void DrawDatasetSelection()
        {
            var datasets = ProjectManager.Instance.LoadedDatasets;
            
            if (ImGui.BeginCombo("Primary Dataset", _selectedPrimaryDataset?.Name ?? "Select..."))
            {
                foreach (var dataset in datasets)
                {
                    bool isSelected = dataset == _selectedPrimaryDataset;
                    if (ImGui.Selectable(dataset.Name, isSelected))
                    {
                        _selectedPrimaryDataset = dataset;
                        UpdateAvailableAnalyses();
                    }
                }
                ImGui.EndCombo();
            }
            
            if (ImGui.BeginCombo("Secondary Dataset", _selectedSecondaryDataset?.Name ?? "Select..."))
            {
                foreach (var dataset in datasets.Where(d => d != _selectedPrimaryDataset))
                {
                    bool isSelected = dataset == _selectedSecondaryDataset;
                    if (ImGui.Selectable(dataset.Name, isSelected))
                    {
                        _selectedSecondaryDataset = dataset;
                        UpdateAvailableAnalyses();
                    }
                }
                ImGui.EndCombo();
            }
        }

        private void DrawAnalysisTypeSelection()
        {
            ImGui.Text("Analysis Type:");
            
            var availableAnalyses = GetAvailableAnalyses();
            
            if (availableAnalyses.Count == 0)
            {
                ImGui.TextDisabled("Select compatible datasets to see available analyses");
                return;
            }
            
            if (ImGui.BeginCombo("##AnalysisType", _analysisType))
            {
                foreach (var analysis in availableAnalyses)
                {
                    if (ImGui.Selectable(analysis, analysis == _analysisType))
                    {
                        _analysisType = analysis;
                    }
                }
                ImGui.EndCombo();
            }
        }

        private void DrawAnalysisUI()
        {
            switch (_analysisType)
            {
                case "SEM to CT Correlation":
                    DrawSEMtoCTUI();
                    break;
                    
                case "Multi-Scale Analysis":
                    DrawMultiScaleUI();
                    break;
                    
                case "Time Series Analysis":
                    DrawTimeSeriesUI();
                    break;
                    
                case "Georeferencing":
                    DrawGeoreferencingUI();
                    break;
                    
                default:
                    ImGui.TextDisabled("Select an analysis type");
                    break;
            }
        }

        private void DrawSEMtoCTUI()
        {
            ImGui.Text("SEM to CT Slice Correlation");
            ImGui.Separator();
            
            if (_selectedPrimaryDataset is ImageDataset semImage && 
                _selectedSecondaryDataset is CtImageStackDataset ctDataset)
            {
                ImGui.SliderInt("CT Slice", ref _sliceIndex, 0, ctDataset.Depth - 1);
                
                ImGui.Text("Click to add correlation points:");
                
                // Display correlation points
                for (int i = 0; i < _linkPoints.Count; i++)
                {
                    ImGui.Text($"Point {i + 1}: ({_linkPoints[i].X:F1}, {_linkPoints[i].Y:F1})");
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"Remove##pt{i}"))
                    {
                        _linkPoints.RemoveAt(i);
                    }
                }
                
                if (ImGui.Button("Create Link", new Vector2(-1, 0)))
                {
                    if (_linkPoints.Count > 0)
                    {
                        ImageCrossDatasetIntegration.LinkSEMtoCTSlice(
                            semImage, ctDataset, _sliceIndex, _linkPoints[0]);
                        
                        ImGui.OpenPopup("LinkCreated");
                    }
                }
                
                if (ImGui.BeginPopup("LinkCreated"))
                {
                    ImGui.Text("Link created successfully!");
                    if (ImGui.Button("OK"))
                    {
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.EndPopup();
                }
            }
        }

        private void DrawMultiScaleUI()
        {
            ImGui.Text("Multi-Scale Image Analysis");
            ImGui.Separator();
            
            var imageDatasets = ProjectManager.Instance.LoadedDatasets
                .OfType<ImageDataset>()
                .Where(img => img.PixelSize > 0)
                .ToList();
            
            if (imageDatasets.Count < 2)
            {
                ImGui.TextWrapped("Need at least 2 calibrated images for multi-scale analysis");
                return;
            }
            
            ImGui.Text($"Found {imageDatasets.Count} calibrated images");
            
            // Display images by scale
            foreach (var img in imageDatasets.OrderBy(i => i.PixelSize))
            {
                ImGui.BulletText($"{img.Name}: {img.PixelSize:F2} {img.Unit}/pixel");
            }
            
            if (ImGui.Button("Create Multi-Scale Analysis", new Vector2(-1, 0)))
            {
                var analysis = ImageCrossDatasetIntegration.CreateMultiScaleAnalysis(
                    imageDatasets.ToArray());
                
                ImGui.OpenPopup("AnalysisComplete");
            }
            
            if (ImGui.BeginPopup("AnalysisComplete"))
            {
                ImGui.Text("Multi-scale analysis created!");
                if (ImGui.Button("OK"))
                {
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
        }

        private void DrawTimeSeriesUI()
        {
            ImGui.Text("Time Series Analysis");
            ImGui.Separator();
            
            ImGui.TextWrapped("Select images with timestamps to create time series analysis");
            
            // This would have UI for selecting images and assigning timestamps
            ImGui.TextDisabled("Time series UI implementation pending");
        }

        private void DrawGeoreferencingUI()
        {
            ImGui.Text("Image Georeferencing");
            ImGui.Separator();
            
            ImGui.TextWrapped("Add ground control points to georeference aerial/satellite imagery");
            
            // This would have UI for adding GCPs
            ImGui.TextDisabled("Georeferencing UI implementation pending");
        }

        private List<string> GetAvailableAnalyses()
        {
            var analyses = new List<string>();
            
            if (_selectedPrimaryDataset == null || _selectedSecondaryDataset == null)
                return analyses;
            
            // Check for SEM to CT correlation
            if (_selectedPrimaryDataset is ImageDataset img1 && img1.HasTag(ImageTag.SEM) &&
                _selectedSecondaryDataset is CtImageStackDataset)
            {
                analyses.Add("SEM to CT Correlation");
            }
            
            // Check for multi-scale analysis
            if (_selectedPrimaryDataset is ImageDataset img2 && img2.PixelSize > 0 &&
                _selectedSecondaryDataset is ImageDataset img3 && img3.PixelSize > 0)
            {
                analyses.Add("Multi-Scale Analysis");
            }
            
            // Add more analysis types as needed
            
            return analyses;
        }

        private void UpdateAvailableAnalyses()
        {
            var analyses = GetAvailableAnalyses();
            if (!analyses.Contains(_analysisType))
            {
                _analysisType = analyses.FirstOrDefault() ?? "None";
            }
        }
    }
}