// GeoscientistToolkit/UI/Tools/CompositeTool.cs
using System;
using System.Collections.Generic;
using GeoscientistToolkit.Analysis.AcousticSimulation;
using GeoscientistToolkit.Analysis.Filtering;
using GeoscientistToolkit.Analysis.ParticleSeparator;
using GeoscientistToolkit.Analysis.RemoveSmallIslands;
using GeoscientistToolkit.Analysis.Transform;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Tools; // Added
using ImGuiNET;

namespace GeoscientistToolkit.UI.Tools
{
    /// <summary>
    /// A composite tool that groups multiple processing and analysis tools for CT Image Stacks.
    /// </summary>
    public class CtImageStackCompositeTool : IDatasetTools, IDisposable
    {
        private readonly List<(string name, IDatasetTools tool)> _tools;

        public CtImageStackCompositeTool()
        {
            _tools = new List<(string name, IDatasetTools tool)>
            {
                // Processing Tools
                ("Segmentation", new CtImageStackTools()),
                ("Advanced Filtering", new FilterTool()),
                ("Transform Dataset", new TransformTool()), 
                
                // --- Analysis Separator ---
                ("--- Object & Particle Analysis ---", new SeparatorTool()),
                ("Particle Separation", new ParticleSeparatorTool()),
                ("Island Removal", new RemoveSmallIslandsTool()),
                ("Mesh Extraction", new MeshExtractionTool()),

                // --- Simulation Separator ---
                ("--- Physical Simulation ---", new SeparatorTool()),
                ("Acoustic Simulation", new AcousticSimulationTool())
            };
        }
        
        public void Draw(Dataset dataset)
        {
            if (dataset is not CtImageStackDataset ctDataset)
            {
                ImGui.TextDisabled("These tools are only available for editable CT Image Stacks.");
                return;
            }

            foreach (var (name, tool) in _tools)
            {
                if (tool is SeparatorTool separator)
                {
                    separator.Draw(name);
                    continue;
                }

                if (ImGui.CollapsingHeader(name, ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.Indent();
                    tool.Draw(ctDataset);
                    ImGui.Unindent();
                    ImGui.Spacing();
                }
            }
        }

        public void Dispose()
        {
            foreach (var (_, tool) in _tools)
            {
                if (tool is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
        
        /// <summary>
        /// A dummy tool used to render a named separator in the UI.
        /// </summary>
        private class SeparatorTool : IDatasetTools
        {
            public void Draw(Dataset dataset) { /* Does nothing */ }
            public void Draw(string label)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.TextDisabled(label);
                ImGui.Separator();
                ImGui.Spacing();
            }
        }
    }
}