// GeoscientistToolkit/UI/Tools/CompositeTool.cs

using System.Numerics;
using GeoscientistToolkit.Analysis.AcousticSimulation;
using GeoscientistToolkit.Analysis.ImageAdjustment;
using GeoscientistToolkit.Analysis.MaterialManager;
using GeoscientistToolkit.Analysis.Materials;
using GeoscientistToolkit.Analysis.MaterialStatistics;
using GeoscientistToolkit.Analysis.NMR;
using GeoscientistToolkit.Analysis.Pnm;
using GeoscientistToolkit.Analysis.RockCoreExtractor;
using GeoscientistToolkit.Analysis.Transform;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Tools;

/// <summary>
///     Categorized tool panel for CT Image Stack datasets.
///     Uses a compact dropdown + tabs navigation to maximize usable space.
/// </summary>
public class CtImageStackCompositeTool : IDatasetTools, IDisposable
{
    private readonly Dictionary<ToolCategory, string> _categoryDescriptions;
    private readonly Dictionary<ToolCategory, string> _categoryNames;

    // Registration tracking
    private readonly HashSet<(CtImageStackDataset ds, object toolKey)> _registered = new();
    private readonly RockCoreExtractorTool _rockCoreTool;

    // All tools organized by category
    private readonly Dictionary<ToolCategory, List<ToolEntry>> _toolsByCategory;

    // Tool instances (stable references)
    private readonly TransformTool _transformTool;
    private bool _disposed;

    private CtImageStackDataset _lastDataset;
    private ToolCategory _selectedCategory = ToolCategory.Segmentation; // Default to segmentation
    private int _selectedToolIndex;

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
            { ToolCategory.PhysicalProperties, "Physical Properties" },
            { ToolCategory.Analysis, "Analysis" },
            { ToolCategory.Export, "Export" }
        };

        _categoryDescriptions = new Dictionary<ToolCategory, string>
        {
            { ToolCategory.Preprocessing, "Data preparation and enhancement" },
            { ToolCategory.Segmentation, "Material identification and labeling" },
            { ToolCategory.PhysicalProperties, "Assign density and other physical material properties" },
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
                    new()
                    {
                        Name = "Brightness/Contrast",
                        Description = "Adjust brightness and contrast with live preview",
                        Tool = new BrightnessContrastTool(),
                        Category = ToolCategory.Preprocessing
                    },
                    new()
                    {
                        Name = "Filtering",
                        Description = "Advanced image filtering, noise reduction, and enhancement",
                        Tool = new FilterTool(),
                        Category = ToolCategory.Preprocessing
                    },
                    new()
                    {
                        Name = "Transform",
                        Description = "Rotate, scale, crop, and resample datasets",
                        Tool = _transformTool,
                        Category = ToolCategory.Preprocessing
                    },
                    new()
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
                    new()
                    {
                        Name = "Material Manager",
                        Description = "Create and manage materials for segmentation",
                        Tool = new MaterialManagerTool(),
                        Category = ToolCategory.Segmentation
                    },
                    new()
                    {
                        Name = "Segmentation",
                        Description = "Material segmentation using thresholding and interactive tools",
                        Tool = new CtImageStackTools(),
                        Category = ToolCategory.Segmentation
                    },
                    new()
                    {
                        Name = "Island Removal",
                        Description = "Remove small disconnected regions from segmented materials",
                        Tool = new RemoveSmallIslandsTool(),
                        Category = ToolCategory.Segmentation
                    },
                    new()
                    {
                        Name = "Texture Classification",
                        Description = "Machine learning based texture classification with GPU acceleration",
                        Tool = new TextureClassificationTool(),
                        Category = ToolCategory.Segmentation
                    },
                    new()
                    {
                        Name = "Particle Separator",
                        Description = "Separate touching particles using watershed algorithms",
                        Tool = new ParticleSeparatorTool(),
                        Category = ToolCategory.Segmentation
                    }
                }
            },
            {
                ToolCategory.PhysicalProperties,
                new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Property Assignment",
                        Description = "Assign physical properties to materials from a library.",
                        Tool = new PhysicalMaterialAssignmentTool(),
                        Category = ToolCategory.PhysicalProperties
                    },
                    new()
                    {
                        Name = "Density Calibration",
                        Description = "Calibrate material densities from grayscale values using ROIs.",
                        Tool = new DensityCalibrationTool(),
                        Category = ToolCategory.PhysicalProperties
                    }
                }
            },
            {
                ToolCategory.Analysis,
                new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Material Statistics",
                        Description = "Analyze material volumes, distributions, and export statistics",
                        Tool = new MaterialStatisticsTool(),
                        Category = ToolCategory.Analysis
                    },
                    new()
                    {
                        Name = "Acoustic Simulation",
                        Description = "Compute acoustic properties and elastic wave velocities",
                        Tool = new AcousticSimulationTool(),
                        Category = ToolCategory.Analysis
                    },
                    new()
                    {
                        Name = "NMR Simulation",
                        Description = "Simulate NMR T2 response for porosity and fluid analysis",
                        Tool = new NMRAnalysisTool(),
                        Category = ToolCategory.Analysis
                    }
                }
            },
            {
                ToolCategory.Export,
                new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Mesh Extraction",
                        Description = "Generate 3D surface meshes from segmented materials",
                        Tool = new MeshExtractionTool(),
                        Category = ToolCategory.Export
                    },
                    new()
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        UnregisterAllForDataset(_lastDataset);

        foreach (var category in _toolsByCategory.Values)
        foreach (var entry in category)
            if (entry.Tool is IDisposable d)
                d.Dispose();

        _toolsByCategory.Clear();
        _registered.Clear();
    }

    private void DrawCompactUI(CtImageStackDataset ctDataset)
    {
        // Compact category selector as dropdown
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8, 4));
        ImGui.Text("Category:");
        ImGui.SameLine();

        var currentCategoryName = _categoryNames[_selectedCategory];
        var categoryTools = _toolsByCategory[_selectedCategory];
        var preview = $"{currentCategoryName} ({categoryTools.Count})";

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.BeginCombo("##CategorySelector", preview))
        {
            foreach (var category in Enum.GetValues<ToolCategory>())
            {
                var tools = _toolsByCategory[category];
                var isSelected = _selectedCategory == category;
                var label = $"{_categoryNames[category]} ({tools.Count} tools)";

                if (ImGui.Selectable(label, isSelected))
                {
                    _selectedCategory = category;
                    _selectedToolIndex = 0;
                }

                // Tooltip with description
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(_categoryDescriptions[category]);
            }

            ImGui.EndCombo();
        }

        ImGui.PopStyleVar();

        // Category description
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), _categoryDescriptions[_selectedCategory]);
        ImGui.Separator();
        ImGui.Spacing();

        // Get the currently active tool
        var activeToolEntry = categoryTools.Count > 0 && _selectedToolIndex < categoryTools.Count
            ? categoryTools[_selectedToolIndex]
            : null;

        // If the active tool is NOT the acoustic simulation, ensure the transducer placement state is stopped.
        // The acoustic tool's own Draw() method will re-enable it when it becomes active.
        if (activeToolEntry?.Tool is not AcousticSimulationTool) AcousticIntegration.StopPlacement();

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
                ImGuiWindowFlags.HorizontalScrollbar);
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
                for (var i = 0; i < categoryTools.Count; i++)
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
                            ImGuiWindowFlags.HorizontalScrollbar);
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
        var count = 0;
        foreach (var tools in _toolsByCategory.Values) count += tools.Count;
        return count;
    }

    // ---- Registration wiring for interactive overlays ----

    private void RegisterAllForDataset(CtImageStackDataset ds)
    {
        if (ds == null) return;

        foreach (var category in _toolsByCategory.Values)
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

    private void UnregisterAllForDataset(CtImageStackDataset ds)
    {
        if (ds == null) return;

        TransformIntegration.UnregisterTool(ds);
        RockCoreIntegration.UnregisterTool(ds);
        _registered.RemoveWhere(tuple => ReferenceEquals(tuple.ds, ds));
    }

    // Tool categories
    private enum ToolCategory
    {
        Preprocessing,
        Segmentation,
        PhysicalProperties,
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

    // --- Adapter for RockCoreExtractorTool ---
    private sealed class RockCoreAdapter : IDatasetTools
    {
        public RockCoreAdapter(RockCoreExtractorTool tool)
        {
            Tool = tool ?? throw new ArgumentNullException(nameof(tool));
        }

        public RockCoreExtractorTool Tool { get; }

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
}