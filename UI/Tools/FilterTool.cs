// GAIA/UI/Tools/FilterTool.cs

using GAIA.Analysis.Filtering;
using GAIA.Data;
using GAIA.Data.CtImageStack;
using GAIA.UI.Interfaces;

namespace GAIA.UI.Tools;

/// <summary>
///     UI bridge for the Advanced Filtering toolset.
/// </summary>
public class FilterTool : IDatasetTools, IDisposable
{
    private readonly FilterUI _filterUI;

    public FilterTool()
    {
        _filterUI = new FilterUI();
    }

    /// <summary>
    ///     Draws the UI panel for the advanced filters.
    /// </summary>
    public void Draw(Dataset dataset)
    {
        if (dataset is CtImageStackDataset ctDataset) _filterUI.DrawPanel(ctDataset);
    }

    public void Dispose()
    {
        _filterUI?.Dispose();
    }
}