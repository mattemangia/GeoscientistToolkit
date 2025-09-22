// GeoscientistToolkit/UI/Tools/AcousticSimulationTool.cs
using GeoscientistToolkit.Analysis.AcousticSimulation;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Interfaces;
using System;

namespace GeoscientistToolkit.UI.Tools
{
    /// <summary>
    /// A tool that hosts the Acoustic Simulation UI panel within the main application's tool section.
    /// This class acts as a bridge between the core UI and the analysis module.
    /// </summary>
    public class AcousticSimulationTool : IDatasetTools, IDisposable
    {
        private readonly AcousticSimulationUI _simulationUI;

        public AcousticSimulationTool()
        {
            _simulationUI = new AcousticSimulationUI();
        }

        /// <summary>
        /// Draws the UI for the Acoustic Simulation tool.
        /// </summary>
        /// <param name="dataset">The dataset to operate on, which must be a CtImageStackDataset.</param>
        public void Draw(Dataset dataset)
        {
            if (dataset is CtImageStackDataset ctDataset)
            {
                _simulationUI.DrawPanel(ctDataset);
            }
        }

        /// <summary>
        /// Disposes of the resources used by the simulation UI.
        /// </summary>
        public void Dispose()
        {
            _simulationUI?.Dispose();
        }
    }
}