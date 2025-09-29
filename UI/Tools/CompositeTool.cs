// GeoscientistToolkit/UI/Tools/CompositeTool.cs
using System;
using System.Collections.Generic;
using GeoscientistToolkit.Analysis.Filtering;
using GeoscientistToolkit.Analysis.ImageAdjustment;
using GeoscientistToolkit.Analysis.MaterialManager;
using GeoscientistToolkit.Analysis.Pnm;
using GeoscientistToolkit.Analysis.RemoveSmallIslands;
using GeoscientistToolkit.Analysis.Transform;
using GeoscientistToolkit.Analysis.RockCoreExtractor;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;
using System.Numerics;
using GeoscientistToolkit.Analysis.MaterialStatistics;

namespace GeoscientistToolkit.UI.Tools
{
    /// <summary>
    /// Categorized tool panel for CT Image Stack datasets.
    /// Uses a compact dropdown + tabs navigation to maximize usable space.
    /// </summary>
    public class CtImageStackCompositeTool : IDatasetTools, IDisposable
    {
        // Tool categories
        private enum ToolCategory
        {
            Preprocessing,
            Segmentation,
            Analysis,
            Export
        }

        private class ToolEntry
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public IDatasetTools Tool { get; set; }
            public ToolCategory Category { get; set; }
        }

        // Registration tracking
        private readonly HashSet<(CtImageStackDataset ds, object toolKey)> _registered = new();
        
        private CtImageStackDataset _lastDataset;
        private ToolCategory _selectedCategory = ToolCategory.Segmentation; // Default to segmentation
        private int _selectedToolIndex = 0;
        private bool _disposed;

        // All tools organized by category
        private readonly Dictionary<ToolCategory, List<ToolEntry>> _toolsByCategory;
        private readonly Dictionary<ToolCategory, string> _categoryNames;
        private readonly Dictionary<ToolCategory, string> _categoryDescriptions;

        // Tool instances (stable references)
        private readonly TransformTool _transformTool;
        private readonly RockCoreExtractorTool _rockCoreTool;

        // --- Adapter for RockCoreExtractorTool ---
        private sealed class RockCoreAdapter : IDatasetTools
        {
            public RockCoreExtractorTool Tool { get; }
            public RockCoreAdapter(RockCoreExtractorTool tool) => Tool = tool ?? throw new ArgumentNullException(nameof(tool));
            public void Draw(Dataset dataset)
            {
                if (dataset is CtImageStackDataset ct)
                {
                    Tool.AttachDataset(ct);
                    Tool.DrawUI(ct);
                }
                else
                {
                    ImGui.TextDisabled("Rock Core tool requires a CT Image Stack dataset.");
                }
            }
        }

        public CtImageStackCompositeTool()
        {
            // Initialize stable tool instances
            _transformTool = new TransformTool();
            _rockCoreTool = new RockCoreExtractorTool();

            // Category metadata
            _categoryNames = new Dictionary<ToolCategory, string>
            {
                { ToolCategory.Preprocessing, "Preprocessing" },
                { ToolCategory.Segmentation, "Segmentation" },
                { ToolCategory.Analysis, "Analysis" },
                { ToolCategory.Export, "Export" }
            };

            _categoryDescriptions = new Dictionary<ToolCategory, string>
            {
                { ToolCategory.Preprocessing, "Data preparation and enhancement" },
                { ToolCategory.Segmentation, "Material identification and labeling" },
                { ToolCategory.Analysis, "Quantitative analysis and measurements" },
                { ToolCategory.Export, "3D model and simulation data generation" }
            };

            // Initialize tools by category
            _toolsByCategory = new Dictionary<ToolCategory, List<ToolEntry>>
            {
                {
                    ToolCategory.Preprocessing,
                    new List<ToolEntry>
                    {
                        new ToolEntry
                        {
                            Name = "Brightness/Contrast",
                            Description = "Adjust brightness and contrast with live preview",
                            Tool = new BrightnessContrastTool(),
                            Category = ToolCategory.Preprocessing
                        },
                        new ToolEntry
                        {
                            Name = "Filtering",
                            Description = "Advanced image filtering, noise reduction, and enhancement",
                            Tool = new FilterTool(),
                            Category = ToolCategory.Preprocessing
                        },
                        new ToolEntry
                        {
                            Name = "Transform",
                            Description = "Rotate, scale, crop, and resample datasets",
                            Tool = _transformTool,
                            Category = ToolCategory.Preprocessing
                        },
                        new ToolEntry
                        {
                            Name = "Rock Core",
                            Description = "Extract cylindrical core samples from datasets",
                            Tool = new RockCoreAdapter(_rockCoreTool),
                            Category = ToolCategory.Preprocessing
                        }
                    }
                },
                {
                    ToolCategory.Segmentation,
                    new List<ToolEntry>
                    {
                        new ToolEntry
                        {
                            Name = "Material Manager",
                            Description = "Create and manage materials for segmentation",
                            Tool = new MaterialManagerTool(),
                            Category = ToolCategory.Segmentation
                        },
                        new ToolEntry
                        {
                            Name = "Segmentation",
                            Description = "Material segmentation using thresholding and interactive tools",
                            Tool = new CtImageStackTools(),
                            Category = ToolCategory.Segmentation
                        },
                        new ToolEntry
                        {
                            Name = "Island Removal",
                            Description = "Remove small disconnected regions from segmented materials",
                            Tool = new RemoveSmallIslandsTool(),
                            Category = ToolCategory.Segmentation
                        },
                        new ToolEntry
                        {
                            Name = "Particle Separator",
                            Description = "Separate touching particles using watershed algorithms",
                            Tool = new ParticleSeparatorTool(),
                            Category = ToolCategory.Segmentation
                        }
                    }
                },
                {
                    ToolCategory.Analysis,
                    new List<ToolEntry>
                    {
                        new ToolEntry
                        {
                            Name = "Material Statistics",
                            Description = "Analyze material volumes, distributions, and export statistics",
                            Tool = new MaterialStatisticsTool(),
                            Category = ToolCategory.Analysis
                        },
                        new ToolEntry
                        {
                            Name = "Acoustic Simulation",
                            Description = "Compute acoustic properties and elastic wave velocities",
                            Tool = new AcousticSimulationTool(),
                            Category = ToolCategory.Analysis
                        }
                    }
                },
                {
                    ToolCategory.Export,
                    new List<ToolEntry>
                    {
                        new ToolEntry
                        {
                            Name = "Mesh Extraction",
                            Description = "Generate 3D surface meshes from segmented materials",
                            Tool = new MeshExtractionTool(),
                            Category = ToolCategory.Export
                        },
                        new ToolEntry
                        {
                            Name = "PNM Generation",
                            Description = "Create Pore Network Models for flow simulation",
                            Tool = new PNMGenerationTool(),
                            Category = ToolCategory.Export
                        }
                    }
                }
            };
        }

        public void Draw(Dataset dataset)
        {
            if (_disposed) return;

            if (dataset is not CtImageStackDataset ctDataset)
            {
                ImGui.TextDisabled("These tools are available for CT Image Stack datasets.");
                UnregisterAllForDataset(_lastDataset);
                _lastDataset = null;
                return;
            }

            // Dataset changed? Re-register everything for the new dataset.
            if (!ReferenceEquals(ctDataset, _lastDataset))
            {
                UnregisterAllForDataset(_lastDataset);
                RegisterAllForDataset(ctDataset);
                _lastDataset = ctDataset;
            }
            else
            {
                // Ensure idempotence
                RegisterAllForDataset(ctDataset);
            }

            // Draw the UI with better organization
            DrawCompactUI(ctDataset);
        }

        private void DrawCompactUI(CtImageStackDataset ctDataset)
        {
            // Compact category selector as dropdown
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8, 4));
            ImGui.Text("Category:");
            ImGui.SameLine();
            
            string currentCategoryName = _categoryNames[_selectedCategory];
            var categoryTools = _toolsByCategory[_selectedCategory];
            string preview = $"{currentCategoryName} ({categoryTools.Count})";
            
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.BeginCombo("##CategorySelector", preview))
            {
                foreach (var category in Enum.GetValues<ToolCategory>())
                {
                    var tools = _toolsByCategory[category];
                    bool isSelected = _selectedCategory == category;
                    string label = $"{_categoryNames[category]} ({tools.Count} tools)";
                    
                    if (ImGui.Selectable(label, isSelected))
                    {
                        _selectedCategory = category;
                        _selectedToolIndex = 0;
                    }
                    
                    // Tooltip with description
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(_categoryDescriptions[category]);
                    }
                }
                ImGui.EndCombo();
            }
            ImGui.PopStyleVar();
            
            // Category description
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), _categoryDescriptions[_selectedCategory]);
            ImGui.Separator();
            ImGui.Spacing();
            
            // Tools in selected category as tabs (if multiple) or direct render (if single)
            if (categoryTools.Count == 0)
            {
                ImGui.TextDisabled("No tools available in this category.");
            }
            else if (categoryTools.Count == 1)
            {
                // Single tool - no tabs needed
                var entry = categoryTools[0];
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1));
                ImGui.Text(entry.Name);
                ImGui.PopStyleColor();
                
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), entry.Description);
                ImGui.Separator();
                ImGui.Spacing();
                
                // Scrollable content area with horizontal scrolling
                ImGui.BeginChild($"ToolScrollArea_{entry.Name}", new Vector2(0, 0), ImGuiChildFlags.None, 
                    ImGuiWindowFlags.HorizontalScrollbar );
                {
                    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 3));
                    ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, 5));
                    
                    // Let content flow naturally - scrollbar appears only when needed
                    entry.Tool.Draw(ctDataset);
                    
                    ImGui.PopStyleVar(2);
                }
                ImGui.EndChild();
            }
            else
            {
                // Multiple tools - use compact tabs
                if (ImGui.BeginTabBar($"Tools_{_selectedCategory}", ImGuiTabBarFlags.None))
                {
                    for (int i = 0; i < categoryTools.Count; i++)
                    {
                        var entry = categoryTools[i];
                        
                        // Shorter tab labels to save space
                        if (ImGui.BeginTabItem(entry.Name))
                        {
                            _selectedToolIndex = i;
                            
                            // Tool description
                            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), entry.Description);
                            ImGui.Separator();
                            ImGui.Spacing();
                            
                            // Scrollable content area with horizontal scrolling
                            ImGui.BeginChild($"ToolContent_{entry.Name}", new Vector2(0, 0), ImGuiChildFlags.None,
                                ImGuiWindowFlags.HorizontalScrollbar );
                            {
                                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 3));
                                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, 5));
                                
                                // Let content flow naturally - scrollbar appears only when needed
                                entry.Tool.Draw(ctDataset);
                                
                                ImGui.PopStyleVar(2);
                            }
                            ImGui.EndChild();
                            
                            ImGui.EndTabItem();
                        }
                    }
                    ImGui.EndTabBar();
                }
            }
            
            // Status info moved to category dropdown tooltip to save space
        }

        private int GetTotalToolCount()
        {
            int count = 0;
            foreach (var tools in _toolsByCategory.Values)
            {
                count += tools.Count;
            }
            return count;
        }

        // ---- Registration wiring for interactive overlays ----

        private void RegisterAllForDataset(CtImageStackDataset ds)
        {
            if (ds == null) return;

            foreach (var category in _toolsByCategory.Values)
            {
                foreach (var entry in category)
                {
                    object keyTool = entry.Tool;
                    if (entry.Tool is RockCoreAdapter rca) keyTool = rca.Tool;

                    var key = (ds, keyTool);
                    if (_registered.Contains(key)) continue;

                    // Transform overlay integration
                    if (entry.Tool is TransformTool tTool)
                    {
                        TransformIntegration.RegisterTool(ds, tTool);
                        _registered.Add(key);
                    }
                    // Rock Core overlay integration
                    else if (entry.Tool is RockCoreAdapter rcAdapter)
                    {
                        RockCoreIntegration.RegisterTool(ds, rcAdapter.Tool);
                        _registered.Add(key);
                    }
                    else
                    {
                        _registered.Add(key);
                    }
                }
            }
        }

        private void UnregisterAllForDataset(CtImageStackDataset ds)
        {
            if (ds == null) return;

            TransformIntegration.UnregisterTool(ds);
            RockCoreIntegration.UnregisterTool(ds);
            _registered.RemoveWhere(tuple => ReferenceEquals(tuple.ds, ds));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            UnregisterAllForDataset(_lastDataset);

            foreach (var category in _toolsByCategory.Values)
            {
                foreach (var entry in category)
                {
                    if (entry.Tool is IDisposable d) d.Dispose();
                }
            }
            
            _toolsByCategory.Clear();
            _registered.Clear();
        }
    }
}