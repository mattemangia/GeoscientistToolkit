// GeoscientistToolkit/UI/Tools/FilterTool.cs
using GeoscientistToolkit.Analysis.Filtering;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Interfaces;
using System;

namespace GeoscientistToolkit.UI.Tools
{
    /// <summary>
    /// UI bridge for the Advanced Filtering toolset.
    /// </summary>
    public class FilterTool : IDatasetTools, IDisposable
    {
        private readonly FilterUI _filterUI;

        public FilterTool()
        {
            _filterUI = new FilterUI();
        }

        /// <summary>
        /// Draws the UI panel for the advanced filters.
        /// </summary>
        public void Draw(Dataset dataset)
        {
            if (dataset is CtImageStackDataset ctDataset)
            {
                _filterUI.DrawPanel(ctDataset);
            }
        }

        public void Dispose()
        {
            _filterUI?.Dispose();
        }
    }
}