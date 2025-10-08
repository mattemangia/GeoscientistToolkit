// GeoscientistToolkit/UI/Tools/ParticleSeparatorTool.cs

using GeoscientistToolkit.Analysis.ParticleSeparator;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Interfaces;

namespace GeoscientistToolkit.UI.Tools;

/// <summary>
///     UI bridge for the Particle Separator tool.
///     This class connects the core analysis UI to the main application's tool panel.
/// </summary>
public class ParticleSeparatorTool : IDatasetTools, IDisposable
{
    private readonly ParticleSeparatorUI _separatorUI;

    public ParticleSeparatorTool()
    {
        _separatorUI = new ParticleSeparatorUI();
    }

    /// <summary>
    ///     Draws the UI panel for the Particle Separator.
    /// </summary>
    /// <param name="dataset">The target CtImageStackDataset.</param>
    public void Draw(Dataset dataset)
    {
        if (dataset is CtImageStackDataset ctDataset) _separatorUI.DrawPanel(ctDataset);
    }

    public void Dispose()
    {
        _separatorUI?.Dispose();
    }
}