// GAIA/UI/Tools/RemoveSmallIslandsTool.cs

using GAIA.Analysis.RemoveSmallIslands;
using GAIA.Data;
using GAIA.Data.CtImageStack;
using GAIA.UI.Interfaces;

namespace GAIA.UI.Tools;

/// <summary>
///     UI bridge for the Remove Small Islands tool.
/// </summary>
public class RemoveSmallIslandsTool : IDatasetTools, IDisposable
{
    private readonly RemoveSmallIslandsUI _islandsUI;

    public RemoveSmallIslandsTool()
    {
        _islandsUI = new RemoveSmallIslandsUI();
    }

    /// <summary>
    ///     Draws the UI panel for removing small islands.
    /// </summary>
    public void Draw(Dataset dataset)
    {
        if (dataset is CtImageStackDataset ctDataset) _islandsUI.DrawPanel(ctDataset);
    }

    public void Dispose()
    {
        _islandsUI?.Dispose();
    }
}