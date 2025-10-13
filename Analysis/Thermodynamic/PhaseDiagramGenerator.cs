// GeoscientistToolkit/Business/Thermodynamics/PhaseDiagramGenerator.cs

using System.Collections.Concurrent;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.Util;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Data;

namespace GeoscientistToolkit.Business.Thermodynamics;

/// <summary>
/// Generates various phase diagrams from thermodynamic calculations.
/// Supports binary/ternary, P-T, energy, and composition diagrams.
/// </summary>
public class PhaseDiagramGenerator
{
    private readonly ThermodynamicSolver _solver;
    private readonly CompoundLibrary _compoundLibrary;
    private readonly ReactionGenerator _reactionGenerator;
    private readonly ActivityCoefficientCalculator _activityCalculator;

    public PhaseDiagramGenerator()
    {
        _solver = new ThermodynamicSolver();
        _compoundLibrary = CompoundLibrary.Instance;
        _reactionGenerator = new ReactionGenerator(_compoundLibrary);
        _activityCalculator = new ActivityCoefficientCalculator();
    }

    #region Binary Phase Diagrams

    /// <summary>
    /// Generate a binary phase diagram for two components.
    /// </summary>
    public BinaryPhaseDiagramData GenerateBinaryDiagram(
        string component1,
        string component2,
        double temperature_K,
        double pressure_bar,
        int gridPoints = 50)
    {
        var data = new BinaryPhaseDiagramData
        {
            Component1 = component1,
            Component2 = component2,
            Temperature_K = temperature_K,
            Pressure_bar = pressure_bar
        };

        var results = new ConcurrentBag<BinaryPhaseDiagramPoint>();

        // Parallel calculation for efficiency
        Parallel.For(0, gridPoints, i =>
        {
            var x1 = (double)i / (gridPoints - 1);
            var x2 = 1.0 - x1;

            var state = CreateBinaryState(component1, component2, x1, x2, temperature_K, pressure_bar);
            var equilibrium = _solver.SolveEquilibrium(state);

            var point = new BinaryPhaseDiagramPoint
            {
                X_Component1 = x1,
                X_Component2 = x2,
                PhasesPresent = DeterminePhases(equilibrium),
                IonicStrength = equilibrium.IonicStrength_molkg,
                pH = equilibrium.pH
            };

            // Calculate saturation indices for all minerals
            var saturationIndices = _solver.CalculateSaturationIndices(equilibrium);
            point.SaturationIndices = saturationIndices;

            // Find precipitating phases
            point.PrecipitatingMinerals = saturationIndices
                .Where(si => si.Value > 0.01)
                .Select(si => si.Key)
                .ToList();

            results.Add(point);
        });

        data.Points = results.OrderBy(p => p.X_Component1).ToList();
        data.PhaseRegions = IdentifyPhaseRegions(data.Points);

        return data;
    }

    private List<string> DeterminePhases(ThermodynamicState state)
    {
        var phases = new HashSet<string>();

        foreach (var (species, moles) in state.SpeciesMoles)
        {
            if (moles < 1e-9) continue;

            var compound = _compoundLibrary.Find(species);
            if (compound != null)
            {
                phases.Add(compound.Phase.ToString());
                
                // Add specific mineral names if solid
                if (compound.Phase == CompoundPhase.Solid)
                {
                    phases.Add(species);
                }
            }
        }

        return phases.ToList();
    }

    private List<PhaseRegion> IdentifyPhaseRegions(List<BinaryPhaseDiagramPoint> points)
    {
        var regions = new List<PhaseRegion>();
        
        // Group consecutive points with the same phase assemblage
        var currentRegion = new PhaseRegion
        {
            StartComposition = 0,
            Phases = points.FirstOrDefault()?.PhasesPresent ?? new List<string>()
        };

        for (int i = 1; i < points.Count; i++)
        {
            var currentPhases = string.Join(",", points[i].PhasesPresent.OrderBy(p => p));
            var previousPhases = string.Join(",", points[i-1].PhasesPresent.OrderBy(p => p));

            if (currentPhases != previousPhases)
            {
                // Phase boundary detected
                currentRegion.EndComposition = points[i-1].X_Component1;
                regions.Add(currentRegion);

                currentRegion = new PhaseRegion
                {
                    StartComposition = points[i].X_Component1,
                    Phases = points[i].PhasesPresent
                };
            }
        }

        // Add the last region
        currentRegion.EndComposition = 1.0;
        regions.Add(currentRegion);

        return regions;
    }

    #endregion

    #region Ternary Phase Diagrams

    /// <summary>
    /// Generate a ternary phase diagram for three components.
    /// </summary>
    public TernaryPhaseDiagramData GenerateTernaryDiagram(
        string component1,
        string component2,
        string component3,
        double temperature_K,
        double pressure_bar,
        int gridResolution = 25)
    {
        var data = new TernaryPhaseDiagramData
        {
            Component1 = component1,
            Component2 = component2,
            Component3 = component3,
            Temperature_K = temperature_K,
            Pressure_bar = pressure_bar
        };

        var results = new ConcurrentBag<TernaryPhaseDiagramPoint>();

        // Generate triangular grid points
        Parallel.For(0, gridResolution + 1, i =>
        {
            for (int j = 0; j <= gridResolution - i; j++)
            {
                var k = gridResolution - i - j;
                
                var x1 = (double)i / gridResolution;
                var x2 = (double)j / gridResolution;
                var x3 = (double)k / gridResolution;

                if (Math.Abs(x1 + x2 + x3 - 1.0) > 1e-9) continue;

                var state = CreateTernaryState(
                    component1, component2, component3,
                    x1, x2, x3,
                    temperature_K, pressure_bar);

                var equilibrium = _solver.SolveEquilibrium(state);

                var point = new TernaryPhaseDiagramPoint
                {
                    X_Component1 = x1,
                    X_Component2 = x2,
                    X_Component3 = x3,
                    PhasesPresent = DeterminePhases(equilibrium),
                    IonicStrength = equilibrium.IonicStrength_molkg,
                    pH = equilibrium.pH
                };

                var saturationIndices = _solver.CalculateSaturationIndices(equilibrium);
                point.PrecipitatingMinerals = saturationIndices
                    .Where(si => si.Value > 0.01)
                    .Select(si => si.Key)
                    .ToList();

                results.Add(point);
            }
        });

        data.Points = results.ToList();
        data.PhaseBoundaries = CalculatePhaseBoundaries(data.Points, gridResolution);

        return data;
    }
    
    /// <summary>
    /// COMPLETE IMPLEMENTATION: Identify phase boundaries using a grid traversal algorithm.
    /// This method treats the calculated points as a triangular grid and finds the edges
    /// between adjacent points that have different phase assemblages.
    /// </summary>
    private List<PhaseBoundary> CalculatePhaseBoundaries(
        List<TernaryPhaseDiagramPoint> points,
        int gridResolution)
    {
        var boundaries = new List<PhaseBoundary>();
        if (points.Count == 0) return boundaries;

        // 1. Create a dictionary for fast lookup of points by their grid indices (i, j).
        var pointGrid = new Dictionary<(int, int), TernaryPhaseDiagramPoint>();
        foreach (var p in points)
        {
            int i = (int)Math.Round(p.X_Component1 * gridResolution);
            int j = (int)Math.Round(p.X_Component2 * gridResolution);
            pointGrid[(i, j)] = p;
        }

        // 2. Iterate through the grid and check neighbors for phase changes.
        for (int i = 0; i <= gridResolution; i++)
        {
            for (int j = 0; j <= gridResolution - i; j++)
            {
                if (!pointGrid.TryGetValue((i, j), out var currentPoint)) continue;

                // Check "right" neighbor (along constant component 2 axis)
                if (pointGrid.TryGetValue((i + 1, j), out var rightNeighbor))
                {
                    if (!ArePhasesEqual(currentPoint.PhasesPresent, rightNeighbor.PhasesPresent))
                    {
                        boundaries.Add(new PhaseBoundary
                        {
                            Point1 = new[] { currentPoint.X_Component1, currentPoint.X_Component2, currentPoint.X_Component3 },
                            Point2 = new[] { rightNeighbor.X_Component1, rightNeighbor.X_Component2, rightNeighbor.X_Component3 },
                            Phase1 = currentPoint.PhasesPresent,
                            Phase2 = rightNeighbor.PhasesPresent
                        });
                    }
                }

                // Check "up" neighbor (along constant component 1 axis)
                if (pointGrid.TryGetValue((i, j + 1), out var upNeighbor))
                {
                    if (!ArePhasesEqual(currentPoint.PhasesPresent, upNeighbor.PhasesPresent))
                    {
                        boundaries.Add(new PhaseBoundary
                        {
                            Point1 = new[] { currentPoint.X_Component1, currentPoint.X_Component2, currentPoint.X_Component3 },
                            Point2 = new[] { upNeighbor.X_Component1, upNeighbor.X_Component2, upNeighbor.X_Component3 },
                            Phase1 = currentPoint.PhasesPresent,
                            Phase2 = upNeighbor.PhasesPresent
                        });
                    }
                }
            }
        }
        return boundaries;
    }


    private bool ArePhasesEqual(List<string> phases1, List<string> phases2)
    {
        if (phases1.Count != phases2.Count) return false;
        var set1 = new HashSet<string>(phases1);
        return set1.SetEquals(phases2);
    }

    #endregion

    #region P-T Phase Diagrams

    /// <summary>
    /// Generate a pressure-temperature phase diagram for a given composition.
    /// </summary>
    public PTPhaseDiagramData GeneratePTDiagram(
        Dictionary<string, double> composition,
        double minT_K,
        double maxT_K,
        double minP_bar,
        double maxP_bar,
        int gridPoints = 30)
    {
        var data = new PTPhaseDiagramData
        {
            Composition = composition,
            MinTemperature = minT_K,
            MaxTemperature = maxT_K,
            MinPressure = minP_bar,
            MaxPressure = maxP_bar
        };

        var results = new ConcurrentBag<PTPhaseDiagramPoint>();

        Parallel.For(0, gridPoints, i =>
        {
            for (int j = 0; j < gridPoints; j++)
            {
                var T = minT_K + (maxT_K - minT_K) * i / (gridPoints - 1);
                var P = minP_bar + (maxP_bar - minP_bar) * j / (gridPoints - 1);

                var state = CreateStateFromComposition(composition, T, P);
                var equilibrium = _solver.SolveEquilibrium(state);

                var point = new PTPhaseDiagramPoint
                {
                    Temperature_K = T,
                    Pressure_bar = P,
                    PhasesPresent = DeterminePhases(equilibrium),
                    DominantPhase = GetDominantPhase(equilibrium)
                };

                // Calculate volume change
                point.MolarVolume = CalculateMolarVolume(equilibrium);

                results.Add(point);
            }
        });

        data.Points = results.OrderBy(p => p.Temperature_K).ThenBy(p => p.Pressure_bar).ToList();
        data.PhaseTransitionCurves = IdentifyPhaseTransitions(data.Points, gridPoints);

        return data;
    }

    private List<PhaseTransitionCurve> IdentifyPhaseTransitions(
        List<PTPhaseDiagramPoint> points,
        int gridSize)
    {
        var curves = new List<PhaseTransitionCurve>();
        
        // Find contiguous curves where phase changes occur
        for (int i = 1; i < points.Count; i++)
        {
            if (!ArePhasesEqual(points[i-1].PhasesPresent, points[i].PhasesPresent))
            {
                curves.Add(new PhaseTransitionCurve
                {
                    Temperature = points[i].Temperature_K,
                    Pressure = points[i].Pressure_bar,
                    PhaseBefore = points[i-1].DominantPhase,
                    PhaseAfter = points[i].DominantPhase,
                    Type = ClassifyTransition(points[i-1].DominantPhase, points[i].DominantPhase)
                });
            }
        }

        return curves;
    }

    private string ClassifyTransition(string phase1, string phase2)
    {
        if ((phase1 == "Solid" && phase2 == "Liquid") || 
            (phase1 == "Liquid" && phase2 == "Solid"))
            return "Melting/Freezing";
        
        if ((phase1 == "Liquid" && phase2 == "Gas") || 
            (phase1 == "Gas" && phase2 == "Liquid"))
            return "Vaporization/Condensation";
        
        if ((phase1 == "Solid" && phase2 == "Gas") || 
            (phase1 == "Gas" && phase2 == "Solid"))
            return "Sublimation/Deposition";
        
        return "Phase Transition";
    }

    #endregion

    #region Energy Diagrams

    /// <summary>
    /// Generate a Gibbs energy diagram as a function of composition.
    /// </summary>
    public EnergyDiagramData GenerateEnergyDiagram(
        string component1,
        string component2,
        double temperature_K,
        double pressure_bar,
        int points = 100)
    {
        var data = new EnergyDiagramData
        {
            Component1 = component1,
            Component2 = component2,
            Temperature_K = temperature_K,
            Pressure_bar = pressure_bar
        };

        var results = new List<EnergyDiagramPoint>();

        for (int i = 0; i < points; i++)
        {
            var x1 = (double)i / (points - 1);
            var x2 = 1.0 - x1;

            var state = CreateBinaryState(component1, component2, x1, x2, temperature_K, pressure_bar);
            var equilibrium = _solver.SolveEquilibrium(state);

            var point = new EnergyDiagramPoint
            {
                X_Component1 = x1,
                GibbsEnergy = CalculateTotalGibbsEnergy(equilibrium),
                Enthalpy = CalculateTotalEnthalpy(equilibrium),
                Entropy = CalculateTotalEntropy(equilibrium),
                ChemicalPotential1 = CalculateChemicalPotential(equilibrium, component1),
                ChemicalPotential2 = CalculateChemicalPotential(equilibrium, component2)
            };

            results.Add(point);
        }

        data.Points = results;
        data.MinimumEnergy = results.Min(p => p.GibbsEnergy);
        data.EquilibriumComposition = results.First(p => p.GibbsEnergy == data.MinimumEnergy).X_Component1;

        return data;
    }

    private double CalculateTotalGibbsEnergy(ThermodynamicState state)
    {
        double totalG = 0;
        
        foreach (var (species, moles) in state.SpeciesMoles)
        {
            if (moles < 1e-9) continue;
            
            var compound = _compoundLibrary.Find(species);
            if (compound?.GibbsFreeEnergyFormation_kJ_mol != null)
            {
                var G0 = compound.GibbsFreeEnergyFormation_kJ_mol.Value * 1000; // J/mol
                var activity = state.Activities.GetValueOrDefault(species, 1.0);
                var mu = G0 + 8.314 * state.Temperature_K * Math.Log(Math.Max(activity, 1e-30));
                totalG += moles * mu;
            }
        }
        
        return totalG;
    }

    private double CalculateTotalEnthalpy(ThermodynamicState state)
    {
        double totalH = 0;
        
        foreach (var (species, moles) in state.SpeciesMoles)
        {
            if (moles < 1e-9) continue;
            
            var compound = _compoundLibrary.Find(species);
            if (compound?.EnthalpyFormation_kJ_mol != null)
            {
                totalH += moles * compound.EnthalpyFormation_kJ_mol.Value * 1000; // J
            }
        }
        
        return totalH;
    }

    private double CalculateTotalEntropy(ThermodynamicState state)
    {
        double totalS = 0;
        
        foreach (var (species, moles) in state.SpeciesMoles)
        {
            if (moles < 1e-9) continue;
            
            var compound = _compoundLibrary.Find(species);
            if (compound?.Entropy_J_molK != null)
            {
                totalS += moles * compound.Entropy_J_molK.Value;
            }
        }
        
        return totalS;
    }

    private double CalculateChemicalPotential(ThermodynamicState state, string component)
    {
        var compound = _compoundLibrary.Find(component);
        if (compound == null) return 0;
        
        var G0 = compound.GibbsFreeEnergyFormation_kJ_mol ?? 0;
        var activity = state.Activities.GetValueOrDefault(component, 1.0);
        
        return G0 * 1000 + 8.314 * state.Temperature_K * Math.Log(Math.Max(activity, 1e-30));
    }

    private double CalculateMolarVolume(ThermodynamicState state)
    {
        double totalVolume = 0;
        double totalMoles = 0;
        
        foreach (var (species, moles) in state.SpeciesMoles)
        {
            if (moles < 1e-9) continue;
            
            var compound = _compoundLibrary.Find(species);
            if (compound?.MolarVolume_cm3_mol != null)
            {
                totalVolume += moles * compound.MolarVolume_cm3_mol.Value;
                totalMoles += moles;
            }
        }
        
        return totalMoles > 0 ? totalVolume / totalMoles : 0;
    }

    #endregion

    #region Helper Methods

    private ThermodynamicState CreateBinaryState(
        string comp1, string comp2, 
        double x1, double x2,
        double T, double P)
    {
        var state = new ThermodynamicState
        {
            Temperature_K = T,
            Pressure_bar = P,
            Volume_L = 1.0
        };

        AddComponentToState(state, comp1, x1);
        AddComponentToState(state, comp2, x2);
        
        return state;
    }

    private ThermodynamicState CreateTernaryState(
        string comp1, string comp2, string comp3,
        double x1, double x2, double x3,
        double T, double P)
    {
        var state = new ThermodynamicState
        {
            Temperature_K = T,
            Pressure_bar = P,
            Volume_L = 1.0
        };

        AddComponentToState(state, comp1, x1);
        AddComponentToState(state, comp2, x2);
        AddComponentToState(state, comp3, x3);
        
        return state;
    }

    private ThermodynamicState CreateStateFromComposition(
        Dictionary<string, double> composition,
        double T, double P)
    {
        var state = new ThermodynamicState
        {
            Temperature_K = T,
            Pressure_bar = P,
            Volume_L = 1.0
        };

        foreach (var (component, moles) in composition)
        {
            AddComponentToState(state, component, moles);
        }
        
        return state;
    }

    private void AddComponentToState(ThermodynamicState state, string componentName, double moles)
    {
        var compound = _compoundLibrary.Find(componentName);
        if (compound == null) return;
        
        state.SpeciesMoles[componentName] = moles;
        
        var composition = _reactionGenerator.ParseChemicalFormula(compound.ChemicalFormula);
        foreach (var (element, stoich) in composition)
        {
            state.ElementalComposition[element] = 
                state.ElementalComposition.GetValueOrDefault(element, 0) + moles * stoich;
        }
    }

    private string GetDominantPhase(ThermodynamicState state)
    {
        var phaseMoles = new Dictionary<CompoundPhase, double>();
        
        foreach (var (species, moles) in state.SpeciesMoles)
        {
            var compound = _compoundLibrary.Find(species);
            if (compound != null)
            {
                phaseMoles[compound.Phase] = phaseMoles.GetValueOrDefault(compound.Phase, 0) + moles;
            }
        }
        
        return phaseMoles.OrderByDescending(p => p.Value).FirstOrDefault().Key.ToString();
    }

    #endregion

    #region Export Methods

    /// <summary>
    /// Export phase diagram data to a DataTable for visualization or analysis.
    /// </summary>
    public DataTable ExportBinaryDiagramToTable(BinaryPhaseDiagramData data)
    {
        var table = new DataTable("BinaryPhaseDiagram");
        
        table.Columns.Add("X_" + data.Component1, typeof(double));
        table.Columns.Add("X_" + data.Component2, typeof(double));
        table.Columns.Add("Phases", typeof(string));
        table.Columns.Add("pH", typeof(double));
        table.Columns.Add("IonicStrength", typeof(double));
        table.Columns.Add("Precipitates", typeof(string));
        
        foreach (var point in data.Points)
        {
            var row = table.NewRow();
            row["X_" + data.Component1] = point.X_Component1;
            row["X_" + data.Component2] = point.X_Component2;
            row["Phases"] = string.Join(", ", point.PhasesPresent);
            row["pH"] = point.pH;
            row["IonicStrength"] = point.IonicStrength;
            row["Precipitates"] = string.Join(", ", point.PrecipitatingMinerals);
            table.Rows.Add(row);
        }
        
        return table;
    }

    /// <summary>
    /// Create an OxyPlot model for visualization.
    /// </summary>
    public PlotModel CreateBinaryDiagramPlot(BinaryPhaseDiagramData data)
    {
        var model = new PlotModel
        {
            Title = $"{data.Component1}-{data.Component2} Phase Diagram at {data.Temperature_K:F0}K, {data.Pressure_bar:F1} bar"
        };

        // Add axes
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = $"Mole Fraction {data.Component1}",
            Minimum = 0,
            Maximum = 1
        });
        
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "pH",
            Minimum = 0,
            Maximum = 14
        });

        // Add phase regions as area series
        foreach (var region in data.PhaseRegions)
        {
            var series = new AreaSeries
            {
                Title = string.Join("+", region.Phases.Distinct()),
                StrokeThickness = 2
            };
            
            // Add points for the region
            var regionPoints = data.Points
                .Where(p => p.X_Component1 >= region.StartComposition && 
                           p.X_Component1 <= region.EndComposition)
                .ToList();
                
            foreach (var point in regionPoints)
            {
                series.Points.Add(new DataPoint(point.X_Component1, point.pH));
            }
            
            model.Series.Add(series);
        }

        return model;
    }

    #endregion
}

#region Data Classes

public class BinaryPhaseDiagramData
{
    public string Component1 { get; set; }
    public string Component2 { get; set; }
    public double Temperature_K { get; set; }
    public double Pressure_bar { get; set; }
    public List<BinaryPhaseDiagramPoint> Points { get; set; } = new();
    public List<PhaseRegion> PhaseRegions { get; set; } = new();
}

public class BinaryPhaseDiagramPoint
{
    public double X_Component1 { get; set; }
    public double X_Component2 { get; set; }
    public List<string> PhasesPresent { get; set; } = new();
    public List<string> PrecipitatingMinerals { get; set; } = new();
    public Dictionary<string, double> SaturationIndices { get; set; } = new();
    public double IonicStrength { get; set; }
    public double pH { get; set; }
}

public class PhaseRegion
{
    public double StartComposition { get; set; }
    public double EndComposition { get; set; }
    public List<string> Phases { get; set; } = new();
}

public class TernaryPhaseDiagramData
{
    public string Component1 { get; set; }
    public string Component2 { get; set; }
    public string Component3 { get; set; }
    public double Temperature_K { get; set; }
    public double Pressure_bar { get; set; }
    public List<TernaryPhaseDiagramPoint> Points { get; set; } = new();
    public List<PhaseBoundary> PhaseBoundaries { get; set; } = new();
}

public class TernaryPhaseDiagramPoint
{
    public double X_Component1 { get; set; }
    public double X_Component2 { get; set; }
    public double X_Component3 { get; set; }
    public List<string> PhasesPresent { get; set; } = new();
    public List<string> PrecipitatingMinerals { get; set; } = new();
    public double IonicStrength { get; set; }
    public double pH { get; set; }
}

public class PhaseBoundary
{
    public double[] Point1 { get; set; }
    public double[] Point2 { get; set; }
    public List<string> Phase1 { get; set; }
    public List<string> Phase2 { get; set; }
}

public class PTPhaseDiagramData
{
    public Dictionary<string, double> Composition { get; set; }
    public double MinTemperature { get; set; }
    public double MaxTemperature { get; set; }
    public double MinPressure { get; set; }
    public double MaxPressure { get; set; }
    public List<PTPhaseDiagramPoint> Points { get; set; } = new();
    public List<PhaseTransitionCurve> PhaseTransitionCurves { get; set; } = new();
}

public class PTPhaseDiagramPoint
{
    public double Temperature_K { get; set; }
    public double Pressure_bar { get; set; }
    public List<string> PhasesPresent { get; set; } = new();
    public string DominantPhase { get; set; }
    public double MolarVolume { get; set; }
}

public class PhaseTransitionCurve
{
    public double Temperature { get; set; }
    public double Pressure { get; set; }
    public string PhaseBefore { get; set; }
    public string PhaseAfter { get; set; }
    public string Type { get; set; }
}

public class EnergyDiagramData
{
    public string Component1 { get; set; }
    public string Component2 { get; set; }
    public double Temperature_K { get; set; }
    public double Pressure_bar { get; set; }
    public List<EnergyDiagramPoint> Points { get; set; } = new();
    public double MinimumEnergy { get; set; }
    public double EquilibriumComposition { get; set; }
}

public class EnergyDiagramPoint
{
    public double X_Component1 { get; set; }
    public double GibbsEnergy { get; set; }
    public double Enthalpy { get; set; }
    public double Entropy { get; set; }
    public double ChemicalPotential1 { get; set; }
    public double ChemicalPotential2 { get; set; }
}

#endregion