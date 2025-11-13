// GeoscientistToolkit/Analysis/Geothermal/GeothermalThermodynamicsIntegration.cs

using System.Collections.Concurrent;
using GeoscientistToolkit.Analysis.PNM;
using GeoscientistToolkit.Analysis.Thermodynamic;
using GeoscientistToolkit.Business.Thermodynamics;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.Util;

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
    private readonly ReactionGenerator _reactionGenerator;

    private PNMDataset _poreNetwork;
    private Dictionary<int, double> _poreRadii;  // Current radii
    private Dictionary<int, double> _throatRadii;

    public GeothermalThermodynamicsIntegration()
    {
        _compoundLibrary = CompoundLibrary.Instance;
        _thermoSolver = new ThermodynamicSolver();
        _reactionGenerator = new ReactionGenerator(_compoundLibrary);
        _kineticsSolver = new KineticsSolver(_compoundLibrary);

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
        progress?.Report("Generating 3D pore network model from borehole lithology...");

        // For now, create a simplified PNM based on lithology properties
        // In a full implementation, this would use the mesh generator to create
        // a 3D representation and then extract the pore network

        var pnmGenerator = new PNMGenerator();
        var pnm = new PNMDataset($"PNM_{borehole.WellName}");

        // Create pores for each lithology unit
        int poreId = 0;
        var layerPorosities = options.LayerPorosities;
        var layerPermeabilities = options.LayerPermeabilities;

        foreach (var unit in borehole.LithologyUnits)
        {
            var unitName = !string.IsNullOrEmpty(unit.Name) ? unit.Name : unit.RockType ?? "Unknown";
            var porosity = layerPorosities.GetValueOrDefault(unitName, 0.1);
            var permeability = layerPermeabilities.GetValueOrDefault(unitName, 1e-14);

            // Estimate pore radius from permeability using Kozeny-Carman
            // k = (phi^3 / (180 * (1-phi)^2)) * d^2
            // Solving for d (pore diameter):
            var d_pore = Math.Sqrt(180 * permeability * Math.Pow(1 - porosity, 2) / Math.Pow(porosity, 3));
            var radius_pore = d_pore / 2.0;

            // Create pores spaced along the depth of this unit
            var depthSpacing = Math.Max(1.0, unit.DepthRange / 10.0);  // 10 pores per unit
            var numPores = (int)(unit.DepthRange / depthSpacing);

            for (int i = 0; i < numPores; i++)
            {
                var depth = unit.TopDepth + i * depthSpacing;
                var pore = new Pore
                {
                    ID = poreId++,
                    Position = new System.Numerics.Vector3(0, 0, (float)depth),
                    Radius = radius_pore,
                    VolumePhysical = (4.0 / 3.0) * Math.PI * Math.Pow(radius_pore, 3),
                    Lithology = unitName
                };

                pnm.Pores.Add(pore);
                _poreRadii[pore.ID] = pore.Radius;

                // Connect to previous pore (create throats)
                if (i > 0 && pnm.Pores.Count > 1)
                {
                    var prevPore = pnm.Pores[pnm.Pores.Count - 2];
                    var throat = new Throat
                    {
                        ID = pnm.Throats.Count,
                        Pore1ID = prevPore.ID,
                        Pore2ID = pore.ID,
                        Radius = radius_pore * 0.5  // Throats are typically smaller
                    };

                    pnm.Throats.Add(throat);
                    _throatRadii[throat.ID] = throat.Radius;

                    // Update connections
                    if (!prevPore.Connections.Contains(pore.ID))
                        prevPore.Connections.Add(pore.ID);
                    if (!pore.Connections.Contains(prevPore.ID))
                        pore.Connections.Add(prevPore.ID);
                }
            }
        }

        _poreNetwork = pnm;
        progress?.Report($"Generated PNM with {pnm.Pores.Count} pores and {pnm.Throats.Count} throats");
        Logger.Log($"[ThermoIntegration] Generated PNM: {pnm.Pores.Count} pores, {pnm.Throats.Count} throats");

        return pnm;
    }

    /// <summary>
    /// Calculate thermodynamic equilibrium and precipitation/dissolution at current time step
    /// </summary>
    public ThermodynamicsResults CalculateThermodynamicsAtTimeStep(
        double currentTime,
        float[,,] temperatureField,
        float[,,] pressureField,
        GeothermalSimulationOptions options,
        GeothermalMesh mesh)
    {
        var results = new ThermodynamicsResults();

        if (_poreNetwork == null || _poreNetwork.Pores.Count == 0)
        {
            Logger.LogWarning("[ThermoIntegration] No pore network available for thermodynamic calculations");
            return results;
        }

        // Initialize precipitation fields
        int nr = temperatureField.GetLength(0);
        int ntheta = temperatureField.GetLength(1);
        int nz = temperatureField.GetLength(2);

        // Process each pore in parallel
        var porePrecipitation = new ConcurrentDictionary<int, Dictionary<string, double>>();

        Parallel.ForEach(_poreNetwork.Pores, pore =>
        {
            // Map pore position to grid indices
            var gridPos = MapPoreToGrid(pore, mesh, nr, ntheta, nz);
            if (gridPos == null) return;

            var (r_idx, theta_idx, z_idx) = gridPos.Value;

            // Get local conditions
            var temperature_K = temperatureField[r_idx, theta_idx, z_idx];
            var pressure_Pa = pressureField[r_idx, theta_idx, z_idx];
            var pressure_bar = pressure_Pa / 1e5;

            // Create thermodynamic state
            var state = CreateThermodynamicState(options.FluidComposition, temperature_K, pressure_bar);

            // Solve equilibrium
            var equilibratedState = _thermoSolver.SolveEquilibrium(state);

            // Calculate saturation indices
            var saturationIndices = _thermoSolver.CalculateSaturationIndices(equilibratedState);

            // Calculate precipitation/dissolution rates
            var precipRates = new Dictionary<string, double>();

            foreach (var (reactionName, SI) in saturationIndices)
            {
                var mineralName = reactionName.Replace(" dissolution", "").Trim();
                var mineral = _compoundLibrary.Find(mineralName);

                if (mineral != null && mineral.Phase == CompoundPhase.Solid)
                {
                    // Use kinetics solver to get rate
                    var surfaceArea = 4.0 * Math.PI * pore.Radius * pore.Radius;  // m²
                    var dt = options.ThermodynamicTimeStep;

                    var rate = _kineticsSolver.CalculateReactionRate(
                        mineral,
                        SI,
                        temperature_K,
                        surfaceArea,
                        equilibratedState.IonicStrength_molkg
                    );

                    // Positive rate = precipitation, negative = dissolution
                    var molesPrecipitated = rate * surfaceArea * dt;  // mol

                    if (Math.Abs(molesPrecipitated) > 1e-12)
                    {
                        precipRates[mineralName] = molesPrecipitated;
                    }
                }
            }

            if (precipRates.Count > 0)
            {
                porePrecipitation[pore.ID] = precipRates;
            }
        });

        // Update pore and throat radii based on precipitation
        UpdatePoreRadii(porePrecipitation, options.ThermodynamicTimeStep);

        // Calculate permeability change
        var permeabilityRatio = CalculatePermeabilityRatio();
        results.PermeabilityRatio = permeabilityRatio;
        results.PorePrecipitation = porePrecipitation.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        Logger.Log($"[ThermoIntegration] t={currentTime:F0}s: Permeability ratio = {permeabilityRatio:F4}");

        return results;
    }

    private (int r, int theta, int z)? MapPoreToGrid(Pore pore, GeothermalMesh mesh, int nr, int ntheta, int nz)
    {
        // Simple mapping: assume pore is at center of borehole (r=0)
        // and map depth (z) to vertical grid

        var depth = pore.Position.Z;
        var z_idx = (int)((depth / mesh.TotalDepth) * nz);
        z_idx = Math.Clamp(z_idx, 0, nz - 1);

        // Center of borehole
        var r_idx = 0;
        var theta_idx = 0;

        return (r_idx, theta_idx, z_idx);
    }

    private ThermodynamicState CreateThermodynamicState(
        List<FluidCompositionEntry> composition,
        double temperature_K,
        double pressure_bar)
    {
        var state = new ThermodynamicState
        {
            Temperature_K = temperature_K,
            Pressure_bar = pressure_bar,
            Volume_L = 1.0
        };

        // Add fluid composition
        foreach (var entry in composition)
        {
            var compound = _compoundLibrary.Find(entry.SpeciesName);
            if (compound != null)
            {
                var moles = entry.Concentration_mol_L * state.Volume_L;
                state.SpeciesMoles[compound.Name] = moles;

                // Add to elemental composition
                var compFormula = _reactionGenerator.ParseChemicalFormula(compound.ChemicalFormula);
                foreach (var (element, stoich) in compFormula)
                {
                    state.ElementalComposition[element] =
                        state.ElementalComposition.GetValueOrDefault(element, 0) + moles * stoich;
                }
            }
            else
            {
                Logger.LogWarning($"[ThermoIntegration] Species '{entry.SpeciesName}' not found in compound library");
            }
        }

        return state;
    }

    private void UpdatePoreRadii(ConcurrentDictionary<int, Dictionary<string, double>> porePrecipitation, double dt)
    {
        foreach (var (poreId, precipRates) in porePrecipitation)
        {
            if (!_poreRadii.ContainsKey(poreId)) continue;

            var currentRadius = _poreRadii[poreId];
            var deltaVolume = 0.0;

            foreach (var (mineralName, molesPrecip) in precipRates)
            {
                var mineral = _compoundLibrary.Find(mineralName);
                if (mineral?.MolarVolume_cm3_mol != null)
                {
                    // Convert molar volume from cm³/mol to m³/mol
                    var molarVolume_m3 = mineral.MolarVolume_cm3_mol.Value * 1e-6;
                    deltaVolume += molesPrecip * molarVolume_m3;
                }
            }

            // Update radius: V = 4/3 * π * r³
            // ΔV = 4π * r² * Δr  (for small changes)
            var deltaRadius = deltaVolume / (4.0 * Math.PI * currentRadius * currentRadius);
            var newRadius = Math.Max(1e-9, currentRadius - deltaRadius);  // Precipitation reduces radius

            _poreRadii[poreId] = newRadius;

            // Update the PNM dataset
            var pore = _poreNetwork.Pores.FirstOrDefault(p => p.ID == poreId);
            if (pore != null)
            {
                pore.Radius = newRadius;
            }
        }

        // Update throat radii proportionally
        UpdateThroatRadii();
    }

    private void UpdateThroatRadii()
    {
        foreach (var throat in _poreNetwork.Throats)
        {
            var pore1 = _poreNetwork.Pores.FirstOrDefault(p => p.ID == throat.Pore1ID);
            var pore2 = _poreNetwork.Pores.FirstOrDefault(p => p.ID == throat.Pore2ID);

            if (pore1 != null && pore2 != null)
            {
                // Throat radius is minimum of connected pores
                var newThroatRadius = Math.Min(pore1.Radius, pore2.Radius) * 0.5;
                _throatRadii[throat.ID] = newThroatRadius;
                throat.Radius = newThroatRadius;
            }
        }
    }

    private double CalculatePermeabilityRatio()
    {
        // Use Kozeny-Carman relation: k ∝ r²
        // Permeability ratio = (r_new / r_initial)²

        if (_poreRadii.Count == 0) return 1.0;

        var initialPore = _poreNetwork.Pores.FirstOrDefault();
        if (initialPore == null) return 1.0;

        // Average radius ratio
        double sumRadiusRatio = 0.0;
        int count = 0;

        foreach (var pore in _poreNetwork.Pores)
        {
            if (_poreRadii.TryGetValue(pore.ID, out var currentRadius))
            {
                var initialRadius = pore.Radius;  // This should be stored separately in production
                var ratio = currentRadius / Math.Max(1e-12, initialRadius);
                sumRadiusRatio += ratio * ratio;  // k ∝ r²
                count++;
            }
        }

        return count > 0 ? sumRadiusRatio / count : 1.0;
    }

    public PNMDataset GetPoreNetwork() => _poreNetwork;

    public Dictionary<int, double> GetPoreRadii() => new Dictionary<int, double>(_poreRadii);

    public Dictionary<int, double> GetThroatRadii() => new Dictionary<int, double>(_throatRadii);
}

public class ThermodynamicsResults
{
    public double PermeabilityRatio { get; set; } = 1.0;
    public Dictionary<int, Dictionary<string, double>> PorePrecipitation { get; set; } = new();
}
