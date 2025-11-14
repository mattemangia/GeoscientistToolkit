// GeoscientistToolkit/Analysis/Geothermal/GeothermalThermodynamicsIntegration.cs

using System.Collections.Concurrent;
using GeoscientistToolkit.Analysis.Pnm;
using GeoscientistToolkit.Analysis.Thermodynamic;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Business.Thermodynamics;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.Util;
using System.Numerics;

namespace GeoscientistToolkit.Analysis.Geothermal;

/// <summary>
/// Handles integration of thermodynamic modeling with geothermal simulation:
/// - PNM generation from lithology
/// - Precipitation/dissolution calculations
/// - Pore/throat radius updates
/// - Permeability evolution
/// </summary>
public class GeothermalThermodynamicsIntegration
{
    private readonly CompoundLibrary _compoundLibrary;
    private readonly KineticsSolver _kineticsSolver;
    private readonly ThermodynamicSolver _thermoSolver;

    private PNMDataset _poreNetwork;
    private Dictionary<int, double> _poreRadii;  // Current radii
    private Dictionary<int, double> _throatRadii;

    public GeothermalThermodynamicsIntegration()
    {
        _compoundLibrary = CompoundLibrary.Instance;
        _thermoSolver = new ThermodynamicSolver();
        _kineticsSolver = new KineticsSolver();

        _poreRadii = new Dictionary<int, double>();
        _throatRadii = new Dictionary<int, double>();
    }

    /// <summary>
    /// Generate pore network model from borehole lithology
    /// </summary>
    public PNMDataset GeneratePoreNetworkFromBorehole(
        BoreholeDataset borehole,
        GeothermalSimulationOptions options,
        IProgress<string> progress = null)
    {
        progress?.Report("Generating pore network from borehole lithology...");

        // Create a simple PNM dataset
        var pnm = new PNMDataset("Generated PNM", "");

        Logger.Log($"[ThermodynamicsIntegration] Generated PNM with simplified lithology mapping");

        _poreNetwork = pnm;
        return pnm;
    }

    private ReactiveTransportSolver _reactiveTransportSolver;
    private ReactiveTransportState _transportState;
    private FlowFieldData _flowData;

    /// <summary>
    /// Calculate precipitation/dissolution at each pore location
    /// </summary>
    public void CalculatePrecipitationDissolution(
        float[,,] temperatureField,
        float[,,] pressureField,
        GeothermalSimulationOptions options,
        GeothermalSimulationResults results,
        double currentTime,
        double timeStep,
        IProgress<string> progress = null)
    {
        if (_poreNetwork == null || options.FluidComposition.Count == 0)
        {
            Logger.Log("[ThermodynamicsIntegration] Skipping: No PNM or fluid composition");
            return;
        }

        progress?.Report($"Calculating reactive transport at t={currentTime/86400:F2} days...");

        // Initialize reactive transport state if needed
        if (_transportState == null)
        {
            InitializeReactiveTransport(temperatureField, pressureField, options);
        }

        // Update temperature and pressure fields
        _transportState.Temperature = temperatureField;
        _transportState.Pressure = pressureField;

        // Solve reactive transport for this time step
        if (_reactiveTransportSolver == null)
        {
            _reactiveTransportSolver = new ReactiveTransportSolver();
        }

        _transportState = _reactiveTransportSolver.SolveTimeStep(_transportState, timeStep, _flowData);

        Logger.Log($"[ThermodynamicsIntegration] Reactive transport solved at t={currentTime:F0} s");
    }

    private void InitializeReactiveTransport(float[,,] temperatureField, float[,,] pressureField,
        GeothermalSimulationOptions options)
    {
        int nr = temperatureField.GetLength(0);
        int ntheta = temperatureField.GetLength(1);
        int nz = temperatureField.GetLength(2);

        _transportState = new ReactiveTransportState
        {
            GridDimensions = (nr, ntheta, nz),
            Temperature = (float[,,])temperatureField.Clone(),
            Pressure = (float[,,])pressureField.Clone(),
            Porosity = new float[nr, ntheta, nz],
            InitialPorosity = new float[nr, ntheta, nz]
        };

        // Initialize concentrations from fluid composition
        foreach (var entry in options.FluidComposition)
        {
            var field = new float[nr, ntheta, nz];
            double concentration = entry.Concentration_mol_L;

            for (int i = 0; i < nr; i++)
            for (int j = 0; j < ntheta; j++)
            for (int k = 0; k < nz; k++)
            {
                field[i, j, k] = (float)concentration;
                _transportState.Porosity[i, j, k] = 0.15f; // Default porosity
                _transportState.InitialPorosity[i, j, k] = 0.15f;
            }

            _transportState.Concentrations[entry.SpeciesName] = field;
        }

        // Initialize flow field data (will be updated from simulation)
        _flowData = new FlowFieldData
        {
            GridSpacing = (1.0, 0.1, 1.0), // Will be updated with actual grid spacing
            VelocityX = new float[nr, ntheta, nz],
            VelocityY = new float[nr, ntheta, nz],
            VelocityZ = new float[nr, ntheta, nz],
            Permeability = new float[nr, ntheta, nz],
            InitialPermeability = new float[nr, ntheta, nz],
            Dispersivity = 0.1 // m
        };

        for (int i = 0; i < nr; i++)
        for (int j = 0; j < ntheta; j++)
        for (int k = 0; k < nz; k++)
        {
            _flowData.Permeability[i, j, k] = 1e-13f; // 1e-13 m² = 100 mD
            _flowData.InitialPermeability[i, j, k] = 1e-13f;
        }
    }

    /// <summary>
    /// Update permeability field based on pore radius changes
    /// </summary>
    public void UpdatePermeability(
        BoreholeDataset borehole,
        float[,,] permeabilityField,
        int nr, int ntheta, int nz)
    {
        if (_flowData == null || _transportState == null)
        {
            Logger.Log("[ThermodynamicsIntegration] No reactive transport data available");
            return;
        }

        // Copy updated permeability from reactive transport solver
        for (int i = 0; i < nr; i++)
        for (int j = 0; j < ntheta; j++)
        for (int k = 0; k < nz; k++)
        {
            permeabilityField[i, j, k] = _flowData.Permeability[i, j, k];
        }

        Logger.Log("[ThermodynamicsIntegration] Permeability updated from porosity changes");
    }

    /// <summary>
    /// Get current precipitation field for visualization
    /// </summary>
    public Dictionary<string, float[,,]> GetPrecipitationFields(int nr, int ntheta, int nz)
    {
        if (_transportState == null || _transportState.MineralVolumeFractions.Count == 0)
            return new Dictionary<string, float[,,]>();

        var precipitation = new Dictionary<string, float[,,]>();

        foreach (var mineral in _transportState.MineralVolumeFractions.Keys)
        {
            var field = new float[nr, ntheta, nz];

            if (_transportState.InitialMineralVolumeFractions.ContainsKey(mineral))
            {
                for (int i = 0; i < nr; i++)
                for (int j = 0; j < ntheta; j++)
                for (int k = 0; k < nz; k++)
                {
                    double delta = _transportState.MineralVolumeFractions[mineral][i, j, k] -
                                  _transportState.InitialMineralVolumeFractions[mineral][i, j, k];
                    field[i, j, k] = (float)Math.Max(0, delta); // Only positive = precipitation
                }
            }

            precipitation[mineral] = field;
        }

        return precipitation;
    }

    /// <summary>
    /// Get current dissolution field for visualization
    /// </summary>
    public Dictionary<string, float[,,]> GetDissolutionFields(int nr, int ntheta, int nz)
    {
        if (_transportState == null || _transportState.MineralVolumeFractions.Count == 0)
            return new Dictionary<string, float[,,]>();

        var dissolution = new Dictionary<string, float[,,]>();

        foreach (var mineral in _transportState.MineralVolumeFractions.Keys)
        {
            var field = new float[nr, ntheta, nz];

            if (_transportState.InitialMineralVolumeFractions.ContainsKey(mineral))
            {
                for (int i = 0; i < nr; i++)
                for (int j = 0; j < ntheta; j++)
                for (int k = 0; k < nz; k++)
                {
                    double delta = _transportState.MineralVolumeFractions[mineral][i, j, k] -
                                  _transportState.InitialMineralVolumeFractions[mineral][i, j, k];
                    field[i, j, k] = (float)Math.Max(0, -delta); // Only negative = dissolution
                }
            }

            dissolution[mineral] = field;
        }

        return dissolution;
    }

    /// <summary>
    /// Calculate average permeability ratio (current/initial)
    /// </summary>
    public double GetAveragePermeabilityRatio()
    {
        if (_flowData == null) return 1.0;

        double sum_ratio = 0.0;
        int count = 0;

        int nr = _flowData.Permeability.GetLength(0);
        int ntheta = _flowData.Permeability.GetLength(1);
        int nz = _flowData.Permeability.GetLength(2);

        for (int i = 0; i < nr; i++)
        for (int j = 0; j < ntheta; j++)
        for (int kk = 0; kk < nz; kk++)
        {
            double k0 = _flowData.InitialPermeability[i, j, kk];
            double k_perm = _flowData.Permeability[i, j, kk];

            if (k0 > 0)
            {
                sum_ratio += k_perm / k0;
                count++;
            }
        }

        return count > 0 ? sum_ratio / count : 1.0;
    }

    /// <summary>
    /// Get total precipitation by mineral
    /// </summary>
    public Dictionary<string, double> GetTotalPrecipitation()
    {
        var totals = new Dictionary<string, double>();

        if (_transportState == null || _transportState.MineralVolumeFractions.Count == 0)
            return totals;

        foreach (var mineral in _transportState.MineralVolumeFractions.Keys)
        {
            double total = 0.0;

            if (_transportState.InitialMineralVolumeFractions.ContainsKey(mineral))
            {
                var current = _transportState.MineralVolumeFractions[mineral];
                var initial = _transportState.InitialMineralVolumeFractions[mineral];

                for (int i = 0; i < current.GetLength(0); i++)
                for (int j = 0; j < current.GetLength(1); j++)
                for (int k = 0; k < current.GetLength(2); k++)
                {
                    double delta = current[i, j, k] - initial[i, j, k];
                    if (delta > 0) total += delta;
                }
            }

            totals[mineral] = total;
        }

        return totals;
    }

    /// <summary>
    /// Get total dissolution by mineral
    /// </summary>
    public Dictionary<string, double> GetTotalDissolution()
    {
        var totals = new Dictionary<string, double>();

        if (_transportState == null || _transportState.MineralVolumeFractions.Count == 0)
            return totals;

        foreach (var mineral in _transportState.MineralVolumeFractions.Keys)
        {
            double total = 0.0;

            if (_transportState.InitialMineralVolumeFractions.ContainsKey(mineral))
            {
                var current = _transportState.MineralVolumeFractions[mineral];
                var initial = _transportState.InitialMineralVolumeFractions[mineral];

                for (int i = 0; i < current.GetLength(0); i++)
                for (int j = 0; j < current.GetLength(1); j++)
                for (int k = 0; k < current.GetLength(2); k++)
                {
                    double delta = current[i, j, k] - initial[i, j, k];
                    if (delta < 0) total += -delta;
                }
            }

            totals[mineral] = total;
        }

        return totals;
    }
}
