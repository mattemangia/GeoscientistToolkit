// NodeEndpoint/GeomechanicsStubs.cs
// Stub implementations for fluid/geothermal methods (not available in headless server)

using GeoscientistToolkit.Analysis.Geomechanics;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.Geomechanics;

// Stub partial class to provide missing methods from GeomechanicalSimulatorCPU_FluidGeothermal.cs
// These are not implemented in the headless server as they require complex UI and data dependencies
public partial class GeomechanicalSimulatorCPU
{
    private void InitializeGeothermalAndFluid(object labels, object extent)
    {
        Logger.Log("WARNING: Geothermal/Fluid coupling is not available in NodeEndpoint (headless server).", Settings.LogLevel.Warning);
        Logger.Log("GeomechanicalParametersExtended properties (EnableGeothermal, EnableFluidInjection) are defined but implementation is excluded.", Settings.LogLevel.Warning);
        Logger.Log("For geothermal/fluid simulations, use the main GeoscientistToolkit application with full UI support.", Settings.LogLevel.Warning);
    }

    private void SimulateFluidInjectionAndFracturing()
    {
        // No-op stub - fluid injection not implemented in headless server
    }

    private void PopulateGeothermalAndFluidResults()
    {
        // No-op stub - fluid results not implemented in headless server
    }
}
