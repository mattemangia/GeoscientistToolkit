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

namespace GeoscientistToolkit.UI.Tools
{
    /// <summary>
    /// Tabbed shell for CT Image Stack tools.
    /// Ensures interactive tools (Transform, Rock Core) are registered with their overlay integrations.
    /// </summary>
    public class CtImageStackCompositeTool : IDatasetTools, IDisposable
    {
        // We need to register per-(dataset, tool instance). Use 'object' for tool key so adapters/inners work.
        private readonly HashSet<(CtImageStackDataset ds, object toolKey)> _registered = new();

        private readonly List<(string TabName, IDatasetTools Tool)> _tools;
        private CtImageStackDataset _lastDataset;
        private int _activeTabIndex;
        private bool _disposed;

        // --- Adapter so we can keep a RockCoreExtractorTool inside the tab list (it doesn't implement IDatasetTools) ---
        private sealed class RockCoreAdapter : IDatasetTools
        {
            public RockCoreExtractorTool Tool { get; }
            public RockCoreAdapter(RockCoreExtractorTool tool) => Tool = tool ?? throw new ArgumentNullException(nameof(tool));
            public void Draw(Dataset dataset)
            {
                if (dataset is CtImageStackDataset ct)
                {
                    // Keep it attached in case UI hasn't been opened earlier
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
            // Stable instances; avoid recreating on every frame.
            var transformTool = new TransformTool();
            var rockCoreTool  = new RockCoreExtractorTool();

            _tools = new List<(string, IDatasetTools)>
            {
                ("Segmentation",        new CtImageStackTools()),
                ("Filtering",           new FilterTool()),
                ("Transform",           transformTool),
                ("Rock Core",           new RockCoreAdapter(rockCoreTool)),
                ("Island Removal",      new RemoveSmallIslandsTool()),
                ("Particle Separation", new ParticleSeparatorTool()),
                ("Meshing",             new MeshExtractionTool()),
                ("PNM Generation",      new PNMGenerationTool()),
                ("Acoustic Sim",        new AcousticSimulationTool())
            };
        }

        public void Draw(Dataset dataset)
        {
            if (_disposed) return;

            if (dataset is not CtImageStackDataset ctDataset)
            {
                ImGui.TextDisabled("These tools are available for CT Image Stack datasets.");
                // If we switched away from CT, drop registrations
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
                // Ensure idempotence (e.g., after reload)
                RegisterAllForDataset(ctDataset);
            }

            if (ImGui.BeginTabBar("CT_ToolsTabBar"))
            {
                for (int i = 0; i < _tools.Count; i++)
                {
                    var (tabName, tool) = _tools[i];

                    // Use overload without 'open' ref param to avoid the discard compile error
                    if (ImGui.BeginTabItem(tabName))
                    {
                        _activeTabIndex = i;
                        tool.Draw(ctDataset);
                        ImGui.EndTabItem();
                    }
                }
                ImGui.EndTabBar();
            }
        }

        // ---- Registration wiring for interactive overlays ----

        private void RegisterAllForDataset(CtImageStackDataset ds)
        {
            if (ds == null) return;

            foreach (var (_, tool) in _tools)
            {
                // Determine the object we register with (inner tool for adapters)
                object keyTool = tool;
                if (tool is RockCoreAdapter rca) keyTool = rca.Tool;

                var key = (ds, keyTool);
                if (_registered.Contains(key)) continue;

                // Transform overlay integration (tool implements TransformTool)
                if (tool is TransformTool tTool)
                {
                    TransformIntegration.RegisterTool(ds, tTool); // safe to call multiple times in our impl
                    _registered.Add(key);
                }
                // Rock Core overlay integration (inner tool)
                else if (tool is RockCoreAdapter rcAdapter)
                {
                    RockCoreIntegration.RegisterTool(ds, rcAdapter.Tool); // safe repeated
                    _registered.Add(key);
                }
                else
                {
                    // Non-interactive tools: we still mark them to avoid reprocessing each frame
                    _registered.Add(key);
                }
            }
        }

        private void UnregisterAllForDataset(CtImageStackDataset ds)
        {
            if (ds == null) return;

            // Only integrations with static registries need explicit unregister
            TransformIntegration.UnregisterTool(ds);
            RockCoreIntegration.UnregisterTool(ds);

            // Clear registration markers for this dataset
            _registered.RemoveWhere(tuple => ReferenceEquals(tuple.ds, ds));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            UnregisterAllForDataset(_lastDataset);

            foreach (var (_, tool) in _tools)
            {
                if (tool is IDisposable d) d.Dispose();
            }
            _tools.Clear();
            _registered.Clear();
        }
    }
}
