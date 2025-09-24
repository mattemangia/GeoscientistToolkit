// GeoscientistToolkit/UI/Tools/CompositeTool.cs
using System;
using System.Collections.Generic;
using GeoscientistToolkit.Analysis.Filtering;
using GeoscientistToolkit.Analysis.Pnm;
using GeoscientistToolkit.Analysis.RemoveSmallIslands;
using GeoscientistToolkit.Analysis.Transform;
using GeoscientistToolkit.Analysis.RockCoreExtractor;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;
using System.Numerics;

namespace GeoscientistToolkit.UI.Tools
{
    /// <summary>
    /// Categorized tool panel for CT Image Stack datasets.
    /// Uses a two-level navigation system to keep UI readable and well-organized.
    /// </summary>
    public class CtImageStackCompositeTool : IDatasetTools, IDisposable
    {
        // Tool categories with their associated tools
        private enum ToolCategory
        {
            DataPreparation,
            Analysis,
            Export
        }

        private class ToolEntry
        {
            public string Name { get; set; }
            public string Icon { get; set; }
            public string Description { get; set; }
            public IDatasetTools Tool { get; set; }
            public ToolCategory Category { get; set; }
        }

        // Registration tracking
        private readonly HashSet<(CtImageStackDataset ds, object toolKey)> _registered = new();
        
        private CtImageStackDataset _lastDataset;
        private ToolCategory _selectedCategory = ToolCategory.DataPreparation;
        private int _selectedToolIndex = 0;
        private bool _disposed;

        // All tools organized by category
        private readonly Dictionary<ToolCategory, List<ToolEntry>> _toolsByCategory;
        private readonly Dictionary<ToolCategory, string> _categoryNames;
        private readonly Dictionary<ToolCategory, string> _categoryIcons;

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
                { ToolCategory.DataPreparation, "Data Preparation" },
                { ToolCategory.Analysis, "Analysis & Processing" },
                { ToolCategory.Export, "Export & Generation" }
            };

            _categoryIcons = new Dictionary<ToolCategory, string>
            {
                { ToolCategory.DataPreparation, "🔧" },
                { ToolCategory.Analysis, "🔬" },
                { ToolCategory.Export, "📦" }
            };

            // Initialize tools by category
            _toolsByCategory = new Dictionary<ToolCategory, List<ToolEntry>>
            {
                {
                    ToolCategory.DataPreparation,
                    new List<ToolEntry>
                    {
                        new ToolEntry
                        {
                            Name = "Segmentation",
                            Icon = "✂️",
                            Description = "Material segmentation and labeling tools",
                            Tool = new CtImageStackTools(),
                            Category = ToolCategory.DataPreparation
                        },
                        new ToolEntry
                        {
                            Name = "Filtering",
                            Icon = "🎛️",
                            Description = "Advanced image filtering and noise reduction",
                            Tool = new FilterTool(),
                            Category = ToolCategory.DataPreparation
                        },
                        new ToolEntry
                        {
                            Name = "Transform",
                            Icon = "🔄",
                            Description = "Rotate, scale, crop, and resample datasets",
                            Tool = _transformTool,
                            Category = ToolCategory.DataPreparation
                        },
                        new ToolEntry
                        {
                            Name = "Rock Core",
                            Icon = "🪨",
                            Description = "Extract cylindrical core samples",
                            Tool = new RockCoreAdapter(_rockCoreTool),
                            Category = ToolCategory.DataPreparation
                        }
                    }
                },
                {
                    ToolCategory.Analysis,
                    new List<ToolEntry>
                    {
                        new ToolEntry
                        {
                            Name = "Island Removal",
                            Icon = "🏝️",
                            Description = "Remove small disconnected regions",
                            Tool = new RemoveSmallIslandsTool(),
                            Category = ToolCategory.Analysis
                        },
                        new ToolEntry
                        {
                            Name = "Particle Separator",
                            Icon = "⚛️",
                            Description = "Separate touching particles using watershed",
                            Tool = new ParticleSeparatorTool(),
                            Category = ToolCategory.Analysis
                        },
                        new ToolEntry
                        {
                            Name = "Acoustic Simulation",
                            Icon = "🔊",
                            Description = "Compute acoustic properties and wave velocities",
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
                            Icon = "🔺",
                            Description = "Generate 3D surface meshes from materials",
                            Tool = new MeshExtractionTool(),
                            Category = ToolCategory.Export
                        },
                        new ToolEntry
                        {
                            Name = "PNM Generation",
                            Icon = "🕸️",
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
            DrawCategorizedUI(ctDataset);
        }

        private void DrawCategorizedUI(CtImageStackDataset ctDataset)
        {
            // Option 1: Sidebar + Content Area
            float sidebarWidth = 200f;
            
            ImGui.BeginChild("ToolSidebar", new Vector2(sidebarWidth, 0), ImGuiChildFlags.Border);
            {
                ImGui.Text("Tool Categories");
                ImGui.Separator();
                
                foreach (var category in Enum.GetValues<ToolCategory>())
                {
                    string label = $"{_categoryIcons[category]} {_categoryNames[category]}";
                    bool isSelected = _selectedCategory == category;
                    
                    if (isSelected)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
                    }
                    
                    if (ImGui.Button(label, new Vector2(-1, 30)))
                    {
                        _selectedCategory = category;
                        _selectedToolIndex = 0; // Reset to first tool in category
                    }
                    
                    if (isSelected)
                    {
                        ImGui.PopStyleColor();
                    }
                    
                    // Show tool count
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(sidebarWidth - 30);
                    ImGui.TextDisabled($"({_toolsByCategory[category].Count})");
                    
                    ImGui.Spacing();
                }
            }
            ImGui.EndChild();
            
            ImGui.SameLine();
            
            // Content area with tabs for tools within the selected category
            ImGui.BeginChild("ToolContent", new Vector2(0, 0), ImGuiChildFlags.None);
            {
                var categoryTools = _toolsByCategory[_selectedCategory];
                
                // Category header
                ImGui.Text($"{_categoryIcons[_selectedCategory]} {_categoryNames[_selectedCategory]}");
                ImGui.Separator();
                ImGui.Spacing();
                
                if (categoryTools.Count > 0)
                {
                    // Sub-tabs for tools within this category (now more readable with fewer tabs)
                    if (ImGui.BeginTabBar($"CategoryTools_{_selectedCategory}"))
                    {
                        for (int i = 0; i < categoryTools.Count; i++)
                        {
                            var entry = categoryTools[i];
                            string tabLabel = $"{entry.Icon} {entry.Name}";
                            
                            if (ImGui.BeginTabItem(tabLabel))
                            {
                                _selectedToolIndex = i;
                                
                                // Tool description
                                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), entry.Description);
                                ImGui.Separator();
                                ImGui.Spacing();
                                
                                // Draw the actual tool UI
                                ImGui.BeginChild($"Tool_{entry.Name}", new Vector2(0, 0), ImGuiChildFlags.None);
                                entry.Tool.Draw(ctDataset);
                                ImGui.EndChild();
                                
                                ImGui.EndTabItem();
                            }
                        }
                        ImGui.EndTabBar();
                    }
                }
                else
                {
                    ImGui.TextDisabled("No tools available in this category.");
                }
            }
            ImGui.EndChild();
        }

        // Alternative layout option (uncomment to use instead)
        private void DrawCollapsibleUI(CtImageStackDataset ctDataset)
        {
            // Alternative: Collapsible sections instead of sidebar
            foreach (var category in Enum.GetValues<ToolCategory>())
            {
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(5, 5));
                
                string headerLabel = $"{_categoryIcons[category]} {_categoryNames[category]} ({_toolsByCategory[category].Count} tools)";
                if (ImGui.CollapsingHeader(headerLabel, ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.Indent();
                    
                    var categoryTools = _toolsByCategory[category];
                    if (categoryTools.Count > 0)
                    {
                        if (ImGui.BeginTabBar($"Tabs_{category}"))
                        {
                            foreach (var entry in categoryTools)
                            {
                                string tabLabel = $"{entry.Icon} {entry.Name}";
                                if (ImGui.BeginTabItem(tabLabel))
                                {
                                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), entry.Description);
                                    ImGui.Separator();
                                    ImGui.Spacing();
                                    
                                    entry.Tool.Draw(ctDataset);
                                    
                                    ImGui.EndTabItem();
                                }
                            }
                            ImGui.EndTabBar();
                        }
                    }
                    
                    ImGui.Unindent();
                }
                
                ImGui.PopStyleVar();
                ImGui.Spacing();
            }
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