// GeoscientistToolkit/AddIns/CtSimulation/ICtSimulationIntegration.cs

using GeoscientistToolkit.Data.CtImageStack;

namespace GeoscientistToolkit.AddIns.CtSimulation
{
    /// <summary>
    /// Interface for simulation integration - allows viewer to work without concrete implementations
    /// </summary>
    public interface ICtSimulationIntegration : IDisposable
    {
        event Action<object> OnResultAvailable;
        void DrawPanel();
        byte[] GetOverlayData(int viewIndex);
        void ProcessDataset(CtImageStackDataset dataset);
    }

    /// <summary>
    /// Factory that creates integration if add-ins are available
    /// </summary>
    public static class SimulationIntegrationFactory
    {
        public static ICtSimulationIntegration CreateIntegration(CtCombinedViewer viewer, CtImageStackDataset dataset)
        {
            // Try to find implementation via reflection or service locator
            var integrationType = Type.GetType("GeoscientistToolkit.AddIns.Development.SimulationIntegration");
            
            if (integrationType != null)
            {
                return Activator.CreateInstance(integrationType, viewer, dataset) as ICtSimulationIntegration;
            }

            // Return null if no implementation found - viewer still works
            return null;
        }
    }
}