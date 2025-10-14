// GeoscientistToolkit/UI/Tools/GeomechanicalSimulationTool.cs

using GeoscientistToolkit.Analysis.Geomechanics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Interfaces;

namespace GeoscientistToolkit.UI.Tools;

/// <summary>
///     Tool for geomechanical stress/strain analysis with Mohr circle visualization
/// </summary>
public class GeomechanicalSimulationTool : IDatasetTools, IDisposable
{
    private readonly GeomechanicalSimulationUI _simulationUI;

    public GeomechanicalSimulationTool()
    {
        _simulationUI = new GeomechanicalSimulationUI();
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is CtImageStackDataset ctDataset)
            _simulationUI.DrawPanel(ctDataset);
    }

    public void Dispose()
    {
        _simulationUI?.Dispose();
    }
}