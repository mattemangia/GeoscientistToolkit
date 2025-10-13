// GeoscientistToolkit/Analysis/Pnm/AbsolutePermeability.cs - Production Version with Confining Pressure

using System.Diagnostics;
using System.Numerics;
using System.Text;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.Util;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Analysis.Pnm;

public sealed class PermeabilityOptions
{
    public PNMDataset Dataset { get; set; }
    public FlowAxis Axis { get; set; } = FlowAxis.Z;
    public float FluidViscosity { get; set; } = 1.0f; // in cP
    public bool CorrectForTortuosity { get; set; } = true;
    public bool UseGpu { get; set; } = false;
    public bool CalculateDarcy { get; set; }
    public bool CalculateNavierStokes { get; set; }
    public bool CalculateLatticeBoltzmann { get; set; }

    public float InletPressure { get; set; } = 1.0f; // Pa
    public float OutletPressure { get; set; } = 0.0f; // Pa

    // Confining pressure effects
    public bool UseConfiningPressure { get; set; } = false;
    public float ConfiningPressure { get; set; } = 0.0f; // MPa
    public float PoreCompressibility { get; set; } = 0.015f; // 1/MPa (typical sandstone)
    public float ThroatCompressibility { get; set; } = 0.025f; // 1/MPa (throats more compressible)
    public float CriticalPressure { get; set; } = 100.0f; // MPa (pressure at which pores close)
}

public sealed class PermeabilityResults
{
    public float DarcyUncorrected { get; set; }
    public float DarcyCorrected { get; set; }
    public float NavierStokesUncorrected { get; set; }
    public float NavierStokesCorrected { get; set; }
    public float LatticeBoltzmannUncorrected { get; set; }
    public float LatticeBoltzmannCorrected { get; set; }
    public float Tortuosity { get; set; }

    public float UsedViscosity { get; set; } // cP
    public float UsedPressureDrop { get; set; } // Pa
    public float ModelLength { get; set; } // m
    public float CrossSectionalArea { get; set; } // m²
    public float TotalFlowRate { get; set; } // m³/s
    public float VoxelSize { get; set; } // μm
    public string FlowAxis { get; set; }
    public int PoreCount { get; set; }
    public int ThroatCount { get; set; }

    // Confining pressure info
    public float AppliedConfiningPressure { get; set; } // MPa
    public float EffectivePoreReduction { get; set; } // %
    public float EffectiveThroatReduction { get; set; } // %
    public int ClosedThroats { get; set; }
}

// Flow data for visualization
public sealed class FlowData
{
    public Dictionary<int, float> PorePressures { get; set; } = new();
    public Dictionary<int, float> ThroatFlowRates { get; set; } = new();
}

// Stress-dependent parameters for each pore/throat
internal struct StressDependentGeometry
{
    public float[] PoreRadii; // Adjusted pore radii
    public float[] ThroatRadii; // Adjusted throat radii
    public bool[] ThroatOpen; // Whether throat is open
    public float PoreReduction; // Average reduction factor
    public float ThroatReduction; // Average reduction factor
    public int ClosedThroats; // Number of closed throats
}

public static class AbsolutePermeability
{
    private static readonly PermeabilityResults _lastResults = new();
    private static readonly FlowData _lastFlowData = new();

    public static PermeabilityResults GetLastResults()
    {
        return _lastResults;
    }

    public static FlowData GetLastFlowData()
    {
        return _lastFlowData;
    }

    public static void Calculate(PermeabilityOptions options)
    {
        var pnm = options.Dataset;
        if (pnm.Pores.Count == 0 || pnm.Throats.Count == 0)
        {
            Logger.LogWarning("[Permeability] PNM is empty, cannot calculate permeability.");
            return;
        }

        if (pnm.VoxelSize <= 0 || pnm.VoxelSize > 1000)
        {
            Logger.LogWarning($"[Permeability] Suspicious voxel size: {pnm.VoxelSize} μm. Using default 1.0 μm");
            pnm.VoxelSize = 1.0f;
        }

        var pixelSize_m = pnm.VoxelSize * 1e-6f; // μm to meters
        Logger.Log($"[Permeability] Voxel size: {pnm.VoxelSize} μm = {pixelSize_m * 1e6} μm");
        Logger.Log($"[Permeability] Pressure: Inlet={options.InletPressure} Pa, Outlet={options.OutletPressure} Pa");
        Logger.Log($"[Permeability] Viscosity: {options.FluidViscosity} cP");

        if (options.UseConfiningPressure)
        {
            Logger.Log($"[Permeability] Confining pressure: {options.ConfiningPressure} MPa");
            Logger.Log($"[Permeability] Pore compressibility: {options.PoreCompressibility} 1/MPa");
            Logger.Log($"[Permeability] Throat compressibility: {options.ThroatCompressibility} 1/MPa");
        }

        // Calculate tortuosity if needed
        if (pnm.Tortuosity <= 0 || pnm.Tortuosity == 1.0f)
        {
            Logger.Log("[Permeability] Calculating geometric tortuosity...");
            pnm.Tortuosity = CalculateGeometricTortuosity(pnm, options.Axis);
            Logger.Log($"[Permeability] Tortuosity = {pnm.Tortuosity:F3}");
        }

        // Apply stress-dependent geometry modifications if confining pressure is used
        var stressGeometry = ApplyConfiningPressureEffects(pnm, options);

        _lastResults.Tortuosity = pnm.Tortuosity;
        _lastResults.UsedViscosity = options.FluidViscosity;
        _lastResults.UsedPressureDrop = Math.Abs(options.InletPressure - options.OutletPressure);
        _lastResults.VoxelSize = pnm.VoxelSize;
        _lastResults.FlowAxis = options.Axis.ToString();
        _lastResults.PoreCount = pnm.Pores.Count;
        _lastResults.ThroatCount = pnm.Throats.Count;
        _lastResults.AppliedConfiningPressure = options.UseConfiningPressure ? options.ConfiningPressure : 0;
        _lastResults.EffectivePoreReduction = stressGeometry.PoreReduction * 100;
        _lastResults.EffectiveThroatReduction = stressGeometry.ThroatReduction * 100;
        _lastResults.ClosedThroats = stressGeometry.ClosedThroats;

        // Clear previous flow data
        _lastFlowData.PorePressures.Clear();
        _lastFlowData.ThroatFlowRates.Clear();

        var tau2 = pnm.Tortuosity * pnm.Tortuosity;

        if (options.CalculateDarcy)
        {
            var darcyUncorrected = RunEngine(options, "Darcy", stressGeometry);
            pnm.DarcyPermeability = darcyUncorrected;

            _lastResults.DarcyUncorrected = darcyUncorrected;
            _lastResults.DarcyCorrected = darcyUncorrected / tau2;

            Logger.Log("[Permeability] Darcy permeability:");
            Logger.Log($"  Uncorrected: {darcyUncorrected:E3} mD ({darcyUncorrected / 1000:F3} D)");
            Logger.Log($"  τ²-corrected: {_lastResults.DarcyCorrected:E3} mD");
        }

        if (options.CalculateNavierStokes)
        {
            var nsUncorrected = RunEngine(options, "NavierStokes", stressGeometry);
            pnm.NavierStokesPermeability = nsUncorrected;

            _lastResults.NavierStokesUncorrected = nsUncorrected;
            _lastResults.NavierStokesCorrected = nsUncorrected / tau2;

            Logger.Log("[Permeability] Navier-Stokes permeability:");
            Logger.Log($"  Uncorrected: {nsUncorrected:E3} mD");
            Logger.Log($"  τ²-corrected: {_lastResults.NavierStokesCorrected:E3} mD");
        }

        if (options.CalculateLatticeBoltzmann)
        {
            var lbmUncorrected = RunEngine(options, "LatticeBoltzmann", stressGeometry);
            pnm.LatticeBoltzmannPermeability = lbmUncorrected;

            _lastResults.LatticeBoltzmannUncorrected = lbmUncorrected;
            _lastResults.LatticeBoltzmannCorrected = lbmUncorrected / tau2;

            Logger.Log("[Permeability] Lattice-Boltzmann permeability:");
            Logger.Log($"  Uncorrected: {lbmUncorrected:E3} mD");
            Logger.Log($"  τ²-corrected: {_lastResults.LatticeBoltzmannCorrected:E3} mD");
        }

        ValidateAndWarnResults();

        if (options.UseConfiningPressure)
            Logger.Log($"[Permeability] Stress effects: {stressGeometry.ClosedThroats} throats closed, " +
                       $"avg pore reduction {stressGeometry.PoreReduction:P1}, " +
                       $"avg throat reduction {stressGeometry.ThroatReduction:P1}");

        Logger.Log("[Permeability] Calculations complete.");
        Logger.Log($"[Permeability] Flow data contains {_lastFlowData.PorePressures.Count} pore pressures");
    }

    private static StressDependentGeometry ApplyConfiningPressureEffects(PNMDataset pnm, PermeabilityOptions options)
{
    var result = new StressDependentGeometry
    {
        PoreRadii = new float[pnm.Pores.Count],
        ThroatRadii = new float[pnm.Throats.Count],
        ThroatOpen = new bool[pnm.Throats.Count]
    };

    if (!options.UseConfiningPressure || options.ConfiningPressure <= 0)
    {
        // No confining pressure - use original radii (IN MICROMETERS)
        for (var i = 0; i < pnm.Pores.Count; i++)
            result.PoreRadii[i] = pnm.Pores[i].Radius;  // Already in μm

        for (var i = 0; i < pnm.Throats.Count; i++)
        {
            result.ThroatRadii[i] = pnm.Throats[i].Radius;  // Already in μm
            result.ThroatOpen[i] = true;
        }

        result.PoreReduction = 0;
        result.ThroatReduction = 0;
        result.ClosedThroats = 0;
        return result;
    }

    // Rest of the function remains the same, just remember radii are in μm
    var P = options.ConfiningPressure;
    var Pc = options.CriticalPressure;
    var αp = options.PoreCompressibility;
    var αt = options.ThroatCompressibility;

    var minRadiusFactor = 0.01f;
    var closureThreshold = 0.05f;

    float poreSum = 0;
    float throatSum = 0;
    var closedCount = 0;

    // Apply pressure effects to pores
    for (var i = 0; i < pnm.Pores.Count; i++)
    {
        var r0 = pnm.Pores[i].Radius;  // In μm

        var reduction = MathF.Exp(-αp * P);
        reduction = Math.Max(reduction, minRadiusFactor);

        var sizeEffect = 1.0f + (1.0f - r0 / pnm.MaxPoreRadius) * 0.5f;
        reduction = MathF.Pow(reduction, sizeEffect);

        result.PoreRadii[i] = r0 * reduction;  // Still in μm
        poreSum += 1 - reduction;
    }

    // Apply pressure effects to throats
    for (var i = 0; i < pnm.Throats.Count; i++)
    {
        var r0 = pnm.Throats[i].Radius;  // In μm

        var reduction = MathF.Exp(-αt * P);
        var sizeEffect = 1.0f + (1.0f - r0 / pnm.MaxThroatRadius) * 1.0f;
        reduction = MathF.Pow(reduction, sizeEffect);

        if (reduction < closureThreshold)
        {
            result.ThroatRadii[i] = 0;
            result.ThroatOpen[i] = false;
            closedCount++;
            throatSum += 1.0f;
        }
        else
        {
            result.ThroatRadii[i] = r0 * Math.Max(reduction, minRadiusFactor);  // Still in μm
            result.ThroatOpen[i] = true;
            throatSum += 1 - reduction;
        }
    }

    result.PoreReduction = poreSum / Math.Max(1, pnm.Pores.Count);
    result.ThroatReduction = throatSum / Math.Max(1, pnm.Throats.Count);
    result.ClosedThroats = closedCount;

    return result;
}


    private static void ValidateAndWarnResults()
    {
        void CheckValue(string name, float value)
        {
            if (value > 100000) // > 100 Darcy
                Logger.LogWarning(
                    $"[Permeability] {name} = {value:E3} mD seems unreasonably high. Check input parameters.");
            else if (value < 0.001 && value > 0) // < 0.001 mD
                Logger.LogWarning(
                    $"[Permeability] {name} = {value:E3} mD seems unreasonably low. Check network connectivity.");
        }

        if (_lastResults.DarcyUncorrected > 0) CheckValue("Darcy", _lastResults.DarcyUncorrected);
        if (_lastResults.NavierStokesUncorrected > 0) CheckValue("Navier-Stokes", _lastResults.NavierStokesUncorrected);
        if (_lastResults.LatticeBoltzmannUncorrected > 0)
            CheckValue("Lattice-Boltzmann", _lastResults.LatticeBoltzmannUncorrected);
    }

    private static float RunEngine(PermeabilityOptions options, string engine, StressDependentGeometry stressGeom)
    {
        var stopwatch = Stopwatch.StartNew();
        var pnm = options.Dataset;

        var pixelSize_m = pnm.VoxelSize * 1e-6f; // μm to m

        // Get boundary pores
        var (inletPores, outletPores, modelLength, crossSectionalArea) = GetBoundaryPores(pnm, options.Axis);

        if (inletPores.Count == 0 || outletPores.Count == 0)
        {
            Logger.LogWarning($"[Permeability] No inlet/outlet pores found for axis {options.Axis}.");
            return 0f;
        }

        _lastResults.ModelLength = modelLength;
        _lastResults.CrossSectionalArea = crossSectionalArea;

        Logger.Log($"[Permeability] Found {inletPores.Count} inlet and {outletPores.Count} outlet pores");
        Logger.Log($"[Permeability] Model: Length={modelLength * 1e6:F1} μm, Area={crossSectionalArea * 1e12:F3} μm²");

        // Build linear system with stress-modified geometry
        var (matrix, b) = BuildLinearSystemWithStress(pnm, engine, inletPores, outletPores,
            options.FluidViscosity, pixelSize_m, options.InletPressure, options.OutletPressure, stressGeom);

        // Check if system is solvable
        if (stressGeom.ClosedThroats == pnm.Throats.Count)
        {
            Logger.LogError(
                $"[Permeability] All throats are closed at {options.ConfiningPressure} MPa confining pressure.");
            return 0f;
        }

        // Solve for pore pressures
        var pressures = options.UseGpu && OpenCLContext.IsAvailable
            ? SolveWithGpu(matrix, b)
            : SolveWithCpu(matrix, b);

        if (pressures == null)
        {
            Logger.LogError($"[Permeability] Linear system solver failed for '{engine}'.");
            return 0f;
        }

        // Store pore pressures for visualization
        StorePorePressures(pnm, pressures);

        // Calculate total flow rate with stress-modified geometry
        var totalFlowRate = CalculateTotalFlowWithStorageAndStress(pnm, engine, pressures, inletPores,
            options.FluidViscosity, pixelSize_m, stressGeom);

        _lastResults.TotalFlowRate = totalFlowRate;

        Logger.Log($"[Permeability] Total flow rate Q = {totalFlowRate:E3} m³/s");

        // Calculate permeability using Darcy's law
        var viscosityPaS = options.FluidViscosity * 0.001f; // cP to Pa·s
        var pressureDrop = Math.Abs(options.InletPressure - options.OutletPressure);

        if (pressureDrop <= 0)
        {
            Logger.LogError("[Permeability] Invalid pressure drop (must be > 0)");
            return 0f;
        }

        var permeability_m2 = totalFlowRate * viscosityPaS * modelLength /
                              (crossSectionalArea * pressureDrop);

        // 1 Darcy = 9.869233e-13 m² => 1 m² = 1.01325e15 mD
        var permeability_mD = permeability_m2 * 1.01325e15f;

        stopwatch.Stop();
        Logger.Log($"[Permeability] {engine} calculation took {stopwatch.ElapsedMilliseconds}ms");
        Logger.Log($"[Permeability] K = {permeability_m2:E3} m² = {permeability_mD:E3} mD");

        return permeability_mD;
    }

    private static void StorePorePressures(PNMDataset pnm, float[] pressures)
    {
        _lastFlowData.PorePressures.Clear();

        foreach (var pore in pnm.Pores)
            if (pore.ID < pressures.Length)
                _lastFlowData.PorePressures[pore.ID] = pressures[pore.ID];
    }

    private static float CalculateTotalFlowWithStorageAndStress(PNMDataset pnm, string engine, float[] pressures,
        HashSet<int> inletPores, float viscosity_cP, float voxelSize_m, StressDependentGeometry stressGeom)
    {
        float totalFlow = 0;
        var poreMap = pnm.Pores.ToDictionary(p => p.ID);
        var viscosity_PaS = viscosity_cP * 0.001f;

        _lastFlowData.ThroatFlowRates.Clear();

        for (var throatIdx = 0; throatIdx < pnm.Throats.Count; throatIdx++)
        {
            var throat = pnm.Throats[throatIdx];

            // Skip closed throats
            if (!stressGeom.ThroatOpen[throatIdx])
                continue;

            var p1_inlet = inletPores.Contains(throat.Pore1ID);
            var p2_inlet = inletPores.Contains(throat.Pore2ID);

            if (!poreMap.TryGetValue(throat.Pore1ID, out var p1) ||
                !poreMap.TryGetValue(throat.Pore2ID, out var p2))
                continue;

            // CRITICAL FIX: Pore radii are ALREADY IN MICROMETERS, not voxels!
            // Convert from μm to m directly (multiply by 1e-6, NOT by voxelSize_m)
            var poreIndex1 = pnm.Pores.IndexOf(p1);
            var poreIndex2 = pnm.Pores.IndexOf(p2);
            var r_p1 = stressGeom.PoreRadii[poreIndex1] * 1e-6f;  // μm to m
            var r_p2 = stressGeom.PoreRadii[poreIndex2] * 1e-6f;  // μm to m
            var r_t = stressGeom.ThroatRadii[throatIdx] * 1e-6f;   // μm to m

            var conductance = CalculateConductanceWithStress(p1.Position, p2.Position,
                r_p1, r_p2, r_t, engine, voxelSize_m, viscosity_PaS);

            var deltaP = pressures[p1.ID] - pressures[p2.ID];
            var flowRate = conductance * deltaP;

            // Store throat flow rate for visualization
            _lastFlowData.ThroatFlowRates[throat.ID] = Math.Abs(flowRate);

            if (p1_inlet && !p2_inlet)
                totalFlow += Math.Max(0, flowRate);
            else if (!p1_inlet && p2_inlet) 
                totalFlow += Math.Max(0, -flowRate);
        }

        return totalFlow;
    }

    private static float CalculateGeometricTortuosity(PNMDataset pnm, FlowAxis axis)
    {
        if (pnm.Pores.Count == 0) return 1.0f;

        var (inletPores, outletPores, modelLength, _) = GetBoundaryPores(pnm, axis);

        if (inletPores.Count == 0 || outletPores.Count == 0) return 1.0f;

        // Build adjacency map with physical distances
        var adjacency = new Dictionary<int, List<(int neighbor, float distance)>>();
        var voxelSize_m = pnm.VoxelSize * 1e-6f;

        foreach (var throat in pnm.Throats)
        {
            var p1 = pnm.Pores.FirstOrDefault(p => p.ID == throat.Pore1ID);
            var p2 = pnm.Pores.FirstOrDefault(p => p.ID == throat.Pore2ID);

            if (p1 != null && p2 != null)
            {
                var dx = Math.Abs(p1.Position.X - p2.Position.X) * voxelSize_m;
                var dy = Math.Abs(p1.Position.Y - p2.Position.Y) * voxelSize_m;
                var dz = Math.Abs(p1.Position.Z - p2.Position.Z) * voxelSize_m;
                var dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

                if (!adjacency.ContainsKey(p1.ID)) adjacency[p1.ID] = new List<(int, float)>();
                if (!adjacency.ContainsKey(p2.ID)) adjacency[p2.ID] = new List<(int, float)>();

                adjacency[p1.ID].Add((p2.ID, dist));
                adjacency[p2.ID].Add((p1.ID, dist));
            }
        }

        // Find shortest paths
        float totalPathLength = 0;
        var pathCount = 0;

        foreach (var inlet in inletPores)
        {
            var distances = DijkstraShortestPath(adjacency, inlet, pnm.Pores.Max(p => p.ID) + 1);

            foreach (var outlet in outletPores)
                if (distances.ContainsKey(outlet) && distances[outlet] < float.MaxValue)
                {
                    totalPathLength += distances[outlet];
                    pathCount++;
                }
        }

        if (pathCount == 0) return 1.0f;

        var avgPathLength = totalPathLength / pathCount;
        var tortuosity = avgPathLength / modelLength;

        return Math.Max(1.0f, Math.Min(10.0f, tortuosity));
    }

    private static Dictionary<int, float> DijkstraShortestPath(
        Dictionary<int, List<(int neighbor, float distance)>> adjacency, int start, int maxId)
    {
        var distances = new Dictionary<int, float>();
        var visited = new HashSet<int>();
        var pq = new PriorityQueue<int, float>();

        distances[start] = 0;
        pq.Enqueue(start, 0);

        while (pq.Count > 0)
        {
            pq.TryDequeue(out var current, out var currentDist);

            if (visited.Contains(current)) continue;
            visited.Add(current);

            if (!adjacency.ContainsKey(current)) continue;

            foreach (var (neighbor, edgeWeight) in adjacency[current])
            {
                if (visited.Contains(neighbor)) continue;

                var newDist = currentDist + edgeWeight;

                if (!distances.ContainsKey(neighbor) || newDist < distances[neighbor])
                {
                    distances[neighbor] = newDist;
                    pq.Enqueue(neighbor, newDist);
                }
            }
        }

        return distances;
    }
    
   private static (HashSet<int> inlets, HashSet<int> outlets, float L, float A)
    GetBoundaryPores(PNMDataset pnm, FlowAxis axis)
{
    if (pnm.Pores.Count == 0)
        return (new HashSet<int>(), new HashSet<int>(), 0, 0);

    var voxelSize_m = pnm.VoxelSize * 1e-6f;
    
    // CRITICAL: Find the actual extent of PORES, not the image!
    float minPos = float.MaxValue, maxPos = float.MinValue;
    float minCross1 = float.MaxValue, maxCross1 = float.MinValue;
    float minCross2 = float.MaxValue, maxCross2 = float.MinValue;
    
    foreach (var pore in pnm.Pores)
    {
        Vector3 pos = pore.Position;
        
        switch (axis)
        {
            case FlowAxis.X:
                if (pos.X < minPos) minPos = pos.X;
                if (pos.X > maxPos) maxPos = pos.X;
                if (pos.Y < minCross1) minCross1 = pos.Y;
                if (pos.Y > maxCross1) maxCross1 = pos.Y;
                if (pos.Z < minCross2) minCross2 = pos.Z;
                if (pos.Z > maxCross2) maxCross2 = pos.Z;
                break;
            case FlowAxis.Y:
                if (pos.Y < minPos) minPos = pos.Y;
                if (pos.Y > maxPos) maxPos = pos.Y;
                if (pos.X < minCross1) minCross1 = pos.X;
                if (pos.X > maxCross1) maxCross1 = pos.X;
                if (pos.Z < minCross2) minCross2 = pos.Z;
                if (pos.Z > maxCross2) maxCross2 = pos.Z;
                break;
            case FlowAxis.Z:
                if (pos.Z < minPos) minPos = pos.Z;
                if (pos.Z > maxPos) maxPos = pos.Z;
                if (pos.X < minCross1) minCross1 = pos.X;
                if (pos.X > maxCross1) maxCross1 = pos.X;
                if (pos.Y < minCross2) minCross2 = pos.Y;
                if (pos.Y > maxCross2) maxCross2 = pos.Y;
                break;
        }
    }
    
    // Calculate physical dimensions based on PORE extent
    float L = (maxPos - minPos) * voxelSize_m;
    float A = (maxCross1 - minCross1) * (maxCross2 - minCross2) * voxelSize_m * voxelSize_m;
    
    // Adaptive tolerance based on pore density
    float axisLength = maxPos - minPos;
    float tolerance = Math.Max(5.0f, axisLength * 0.1f); // 10% of material length
    
    var inlets = new HashSet<int>();
    var outlets = new HashSet<int>();
    
    // First pass: try with initial tolerance
    foreach (var pore in pnm.Pores)
    {
        float pos = axis switch
        {
            FlowAxis.X => pore.Position.X,
            FlowAxis.Y => pore.Position.Y,
            _ => pore.Position.Z
        };
        
        if (pos <= minPos + tolerance) inlets.Add(pore.ID);
        if (pos >= maxPos - tolerance) outlets.Add(pore.ID);
    }
    
    // If too few boundary pores, progressively expand tolerance
    int minRequired = Math.Max(5, pnm.Pores.Count / 50); // At least 5 or 2% of pores
    
    while ((inlets.Count < minRequired || outlets.Count < minRequired) && tolerance < axisLength * 0.3f)
    {
        tolerance *= 1.5f; // Increase tolerance by 50%
        inlets.Clear();
        outlets.Clear();
        
        foreach (var pore in pnm.Pores)
        {
            float pos = axis switch
            {
                FlowAxis.X => pore.Position.X,
                FlowAxis.Y => pore.Position.Y,
                _ => pore.Position.Z
            };
            
            if (pos <= minPos + tolerance) inlets.Add(pore.ID);
            if (pos >= maxPos - tolerance) outlets.Add(pore.ID);
        }
        
        Logger.Log($"[Boundary Detection] Expanded tolerance to {tolerance:F1} voxels");
    }

    Logger.Log($"[Boundary Detection] Pore extent along {axis}: {minPos:F1} to {maxPos:F1} voxels");
    Logger.Log($"[Boundary Detection] Physical L={L * 1e6:F1} µm, A={A * 1e12:F3} µm²");
    Logger.Log($"[Boundary Detection] Found {inlets.Count} inlet pores, {outlets.Count} outlet pores");

    // Final check - ensure we have at least some boundary pores
    if (inlets.Count == 0 || outlets.Count == 0)
    {
        Logger.LogWarning("[Boundary Detection] Failed to find boundary pores. Using extremes.");
        
        // Sort pores by position and take extremes
        var sortedPores = pnm.Pores.OrderBy(p => axis switch
        {
            FlowAxis.X => p.Position.X,
            FlowAxis.Y => p.Position.Y,
            _ => p.Position.Z
        }).ToList();
        
        int takeCount = Math.Max(5, sortedPores.Count / 10);
        
        for (int i = 0; i < takeCount && i < sortedPores.Count; i++)
            inlets.Add(sortedPores[i].ID);
        
        for (int i = sortedPores.Count - takeCount; i < sortedPores.Count && i >= 0; i++)
            outlets.Add(sortedPores[i].ID);
    }

    return (inlets, outlets, L, A);
}
private static float EstimateAveragePoreSpacing(PNMDataset pnm, FlowAxis axis)
{
    if (pnm.Pores.Count < 2) return 10.0f; // Default
    
    // Sample pore positions along the flow axis
    var positions = axis switch
    {
        FlowAxis.X => pnm.Pores.Select(p => p.Position.X).OrderBy(x => x).ToList(),
        FlowAxis.Y => pnm.Pores.Select(p => p.Position.Y).OrderBy(y => y).ToList(),
        _ => pnm.Pores.Select(p => p.Position.Z).OrderBy(z => z).ToList()
    };
    
    if (positions.Count < 2) return 10.0f;
    
    // Calculate average spacing between consecutive pores
    float totalSpacing = 0;
    int count = 0;
    
    for (int i = 1; i < positions.Count; i++)
    {
        var spacing = positions[i] - positions[i - 1];
        if (spacing > 0.1f) // Ignore duplicates
        {
            totalSpacing += spacing;
            count++;
        }
    }
    
    return count > 0 ? totalSpacing / count : 10.0f;
}
    private static (SparseMatrix, float[]) BuildLinearSystemWithStress(PNMDataset pnm, string engine,
    HashSet<int> inlets, HashSet<int> outlets, float viscosity_cP, float voxelSize_m,
    float inletPressure, float outletPressure, StressDependentGeometry stressGeom)
{
    var maxId = pnm.Pores.Max(p => p.ID);
    var poreMap = pnm.Pores.ToDictionary(p => p.ID);
    var matrix = new SparseMatrix(maxId + 1);
    var b = new float[maxId + 1];

    var viscosity_PaS = viscosity_cP * 0.001f;

    // Build conductance matrix with stress-modified geometry
    for (var throatIdx = 0; throatIdx < pnm.Throats.Count; throatIdx++)
    {
        var throat = pnm.Throats[throatIdx];

        // Skip closed throats
        if (!stressGeom.ThroatOpen[throatIdx])
            continue;

        if (!poreMap.TryGetValue(throat.Pore1ID, out var p1) ||
            !poreMap.TryGetValue(throat.Pore2ID, out var p2))
            continue;

        // CRITICAL FIX: Radii are ALREADY IN MICROMETERS!
        var poreIndex1 = pnm.Pores.IndexOf(p1);
        var poreIndex2 = pnm.Pores.IndexOf(p2);
        var r_p1 = stressGeom.PoreRadii[poreIndex1] * 1e-6f;  // μm to m
        var r_p2 = stressGeom.PoreRadii[poreIndex2] * 1e-6f;  // μm to m
        var r_t = stressGeom.ThroatRadii[throatIdx] * 1e-6f;   // μm to m

        var conductance = CalculateConductanceWithStress(p1.Position, p2.Position,
            r_p1, r_p2, r_t, engine, voxelSize_m, viscosity_PaS);

        // Add to system matrix (off-diagonal negative, diagonal positive)
        matrix.Add(p1.ID, p1.ID, conductance);
        matrix.Add(p2.ID, p2.ID, conductance);
        matrix.Add(p1.ID, p2.ID, -conductance);
        matrix.Add(p2.ID, p1.ID, -conductance);
    }

    // Apply boundary conditions (Dirichlet)
    foreach (var id in inlets)
    {
        matrix.ClearRow(id);
        matrix.Set(id, id, 1.0f);
        b[id] = inletPressure;
    }

    foreach (var id in outlets)
    {
        matrix.ClearRow(id);
        matrix.Set(id, id, 1.0f);
        b[id] = outletPressure;
    }

    return (matrix, b);
}

    private static float CalculateConductanceWithStress(Vector3 pos1, Vector3 pos2,
    float r_p1, float r_p2, float r_t, string engine, float voxelSize_m, float viscosity_PaS)
{
    // r_p1, r_p2, r_t are already in meters
    
    // Physical distance between pore centers
    var dx = Math.Abs(pos1.X - pos2.X) * voxelSize_m;
    var dy = Math.Abs(pos1.Y - pos2.Y) * voxelSize_m;
    var dz = Math.Abs(pos1.Z - pos2.Z) * voxelSize_m;
    var length = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

    if (length < 1e-12f) length = 1e-12f;

    // Check for closed throat
    if (r_t <= 0) return 0;

    // CRITICAL: Shape factor should be between 0.5-0.7 for real rocks
    // Too low gives unrealistic permeabilities
    const float SHAPE_FACTOR = 0.6f; // Adjusted for more realistic values
    
    // Hagen-Poiseuille conductance: g = (π*r^4) / (8*μ*L)
    switch (engine)
    {
        case "Darcy":
            return SHAPE_FACTOR * (float)(Math.PI * Math.Pow(r_t, 4)) / (8 * viscosity_PaS * length);

        case "NavierStokes":
            // Include entrance effects
            var velocity = 1e-4f; // m/s
            var density = 1000f; // kg/m³
            var Re_throat = density * velocity * 2 * r_t / viscosity_PaS;
            
            var entranceLength = Math.Min(0.06f * Re_throat * r_t, 0.1f * length);
            var constrictionFactor = 1.0f + 0.3f * MathF.Pow(r_t / Math.Max(r_p1, r_p2), 2);
            
            var effectiveLength = (length + entranceLength) * constrictionFactor;
            return SHAPE_FACTOR * (float)(Math.PI * Math.Pow(r_t, 4)) / (8 * viscosity_PaS * effectiveLength);

        case "LatticeBoltzmann":
            // Include pore body resistance
            var l_p1 = r_p1 * 0.5f;
            var l_p2 = r_p2 * 0.5f;
            var l_t = Math.Max(1e-12f, length - l_p1 - l_p2);

            var g_p1 = SHAPE_FACTOR * (float)(Math.PI * Math.Pow(r_p1, 4)) / (8 * viscosity_PaS * Math.Max(l_p1, 1e-12f));
            var g_p2 = SHAPE_FACTOR * (float)(Math.PI * Math.Pow(r_p2, 4)) / (8 * viscosity_PaS * Math.Max(l_p2, 1e-12f));
            var g_t = SHAPE_FACTOR * (float)(Math.PI * Math.Pow(r_t, 4)) / (8 * viscosity_PaS * l_t);

            // Junction losses (reduced for more realistic values)
            var junctionLoss1 = 0.3f * MathF.Pow(Math.Max(0, 1 - r_t / r_p1), 2);
            var junctionLoss2 = 0.3f * MathF.Pow(Math.Max(0, 1 - r_t / r_p2), 2);

            var totalResistance = 1 / g_p1 * (1 + junctionLoss1) +
                                  1 / g_t +
                                  1 / g_p2 * (1 + junctionLoss2);

            return 1 / totalResistance;

        default:
            return SHAPE_FACTOR * (float)(Math.PI * Math.Pow(r_t, 4)) / (8 * viscosity_PaS * length);
    }
}
    // --- CPU CONJUGATE GRADIENT SOLVER ---

    private static float[] SolveWithCpu(SparseMatrix A, float[] b, float tolerance = 1e-6f, int maxIterations = 5000)
    {
        var n = b.Length;
        var x = new float[n];
        var r = new float[n];
        Array.Copy(b, r, n);

        var p = new float[n];
        Array.Copy(r, p, n);

        var rsold = Dot(r, r);

        if (rsold < tolerance * tolerance) return x;

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var Ap = A.Multiply(p);
            var pAp = Dot(p, Ap);

            if (Math.Abs(pAp) < 1e-10f)
            {
                Logger.LogWarning($"[CG Solver] Breakdown at iteration {iteration}");
                break;
            }

            var alpha = rsold / pAp;
            Axpy(x, p, alpha);
            Axpy(r, Ap, -alpha);

            var rsnew = Dot(r, r);

            if (Math.Sqrt(rsnew) < tolerance)
            {
                Logger.Log($"[CG Solver] Converged in {iteration + 1} iterations");
                break;
            }

            var beta = rsnew / rsold;

            for (var j = 0; j < n; j++)
                p[j] = r[j] + beta * p[j];

            rsold = rsnew;
        }

        return x;
    }

    private static float Dot(float[] a, float[] b)
    {
        double sum = 0;
        for (var i = 0; i < a.Length; i++)
            sum += a[i] * b[i];
        return (float)sum;
    }

    private static void Axpy(float[] y, float[] x, float alpha)
    {
        for (var i = 0; i < y.Length; i++)
            y[i] += alpha * x[i];
    }

    // --- GPU SOLVER ---

    private static float[] SolveWithGpu(SparseMatrix A, float[] b, float tolerance = 1e-6f, int maxIterations = 5000)
    {
        try
        {
            return OpenCLContext.Solve(A, b, tolerance, maxIterations);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[GPU Solver] Failed: {ex.Message}. Falling back to CPU.");
            return SolveWithCpu(A, b, tolerance, maxIterations);
        }
    }
}

// --- SPARSE MATRIX CLASS ---

public class SparseMatrix
{
    private readonly Dictionary<int, float>[] _rows;

    public SparseMatrix(int size)
    {
        Size = size;
        _rows = new Dictionary<int, float>[size];
        for (var i = 0; i < size; i++)
            _rows[i] = new Dictionary<int, float>();
    }

    public int Size { get; }

    public void Set(int row, int col, float value)
    {
        _rows[row][col] = value;
    }

    public void Add(int row, int col, float value)
    {
        _rows[row].TryGetValue(col, out var current);
        _rows[row][col] = current + value;
    }

    public void ClearRow(int row)
    {
        _rows[row].Clear();
    }

    public Dictionary<int, float> GetRow(int row)
    {
        return _rows[row];
    }

    public float[] Multiply(float[] vector)
    {
        var result = new float[Size];
        Parallel.For(0, Size, i =>
        {
            double sum = 0;
            foreach (var (col, value) in _rows[i]) sum += value * vector[col];
            result[i] = (float)sum;
        });
        return result;
    }
}

// --- OPENCL CONTEXT (unchanged from original) ---

internal static class OpenCLContext
{
    private static readonly Lazy<bool> _isAvailable = new(CheckAvailability);
    private static CL _cl;
    private static nint _context;
    private static nint _device;
    private static nint _queue;
    private static nint _program;
    private static bool _initialized;

    public static bool IsAvailable
    {
        get
        {
            EnsureInitialized();
            return _isAvailable.Value && _initialized;
        }
    }

    private static bool CheckAvailability()
    {
        try
        {
            _cl = CL.GetApi();
            unsafe
            {
                uint numPlatforms;
                _cl.GetPlatformIDs(0, null, &numPlatforms);
                return numPlatforms > 0;
            }
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;

        try
        {
            _cl = CL.GetApi();

            unsafe
            {
                // Get platforms
                uint numPlatforms;
                _cl.GetPlatformIDs(0, null, &numPlatforms);
                if (numPlatforms == 0) return;

                var platforms = stackalloc nint[(int)numPlatforms];
                _cl.GetPlatformIDs(numPlatforms, platforms, null);

                // Find a device (prefer GPU)
                nint selectedPlatform = 0;
                _device = 0;

                for (var i = 0; i < numPlatforms; i++)
                {
                    uint numDevices;
                    _cl.GetDeviceIDs(platforms[i], DeviceType.Gpu, 0, null, &numDevices);

                    if (numDevices == 0)
                        _cl.GetDeviceIDs(platforms[i], DeviceType.Cpu, 0, null, &numDevices);

                    if (numDevices > 0)
                    {
                        selectedPlatform = platforms[i];
                        var devices = stackalloc nint[(int)numDevices];
                        _cl.GetDeviceIDs(selectedPlatform, DeviceType.All, numDevices, devices, null);
                        _device = devices[0];
                        break;
                    }
                }

                if (_device == 0) return;

                // Create context and command queue
                int err;
                var devicePtr = stackalloc nint[1];
                devicePtr[0] = _device;
                _context = _cl.CreateContext(null, 1, devicePtr, null, null, &err);
                if (err != 0) return;

                _queue = _cl.CreateCommandQueue(_context, _device, CommandQueueProperties.None, &err);
                if (err != 0) return;

                // Create program with CG kernel
                var kernelSource = @"
// --- spmv_csr: y = A * x (CSR) ---
__kernel void spmv_csr(__global const int* rowPtr,
                       __global const int* colIdx,
                       __global const float* values,
                       __global const float* x,
                       __global float* y,
                       const int numRows)
{
    int row = get_global_id(0);
    if (row >= numRows) return;

    int start = rowPtr[row];
    int end   = rowPtr[row + 1];

    float sum = 0.0f;
    for (int jj = start; jj < end; ++jj) {
        sum += values[jj] * x[colIdx[jj]];
    }
    y[row] = sum;
}

// --- vector_ops: axpy / scale ---
// op == 1: y = y + alpha * x
// op == 2: y = alpha * y  (x is ignored; host passes x=y)
__kernel void vector_ops(__global float* y,
                         __global const float* x,
                         const float alpha,
                         const int n,
                         const int op)
{
    int i = get_global_id(0);
    if (i >= n) return;

    if (op == 1) {
        y[i] = y[i] + alpha * x[i];
    } else if (op == 2) {
        y[i] = alpha * y[i];
    }
}

// --- dot_product with local reduction ---
// result length must be >= number of work-groups
__kernel void dot_product(__global const float* a,
                          __global const float* b,
                          __global float* result,
                          __local float* scratch,
                          const int n)
{
    int global_id = get_global_id(0);
    int local_id  = get_local_id(0);
    int group_sz  = get_local_size(0);

    float acc = 0.0f;
    // grid-stride loop
    while (global_id < n) {
        acc += a[global_id] * b[global_id];
        global_id += get_global_size(0);
    }

    scratch[local_id] = acc;
    barrier(CLK_LOCAL_MEM_FENCE);

    for (int offset = group_sz >> 1; offset > 0; offset >>= 1) {
        if (local_id < offset) {
            scratch[local_id] += scratch[local_id + offset];
        }
        barrier(CLK_LOCAL_MEM_FENCE);
    }

    if (local_id == 0) {
        result[get_group_id(0)] = scratch[0];
    }
}
";

                var sourcePtr = kernelSource;
                var sourceLen = (nuint)kernelSource.Length;
                _program = _cl.CreateProgramWithSource(_context, 1, new[] { sourcePtr }, in sourceLen, &err);
                if (err != 0) return;

                var devicePtr2 = stackalloc nint[1];
                devicePtr2[0] = _device;
                err = _cl.BuildProgram(_program, 1, devicePtr2, string.Empty, null, null);

                if (err != 0)
                {
                    // Get build log
                    nuint logSize;
                    _cl.GetProgramBuildInfo(_program, _device, ProgramBuildInfo.BuildLog, 0, null, &logSize);
                    if (logSize > 0)
                    {
                        var log = new byte[logSize];
                        fixed (byte* logPtr = log)
                        {
                            _cl.GetProgramBuildInfo(_program, _device, ProgramBuildInfo.BuildLog, logSize, logPtr,
                                null);
                            var logStr = Encoding.ASCII.GetString(log);
                            Logger.LogError($"[OpenCL] Build failed: {logStr}");
                        }
                    }

                    return;
                }

                _initialized = true;
                Logger.Log("[OpenCL] Context initialized successfully");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[OpenCL] Initialization failed: {ex.Message}");
            _initialized = false;
        }
    }

    public static float[] Solve(SparseMatrix A, float[] b, float tolerance, int maxIterations)
    {
        EnsureInitialized();
        if (!_initialized) throw new InvalidOperationException("OpenCL not initialized");

        unsafe
        {
            var n = b.Length;

            // Convert sparse matrix to CSR format
            var (rowPtr, colIdx, values) = ConvertToCSR(A);

            // Create OpenCL buffers
            int err;
            nint clRowPtr, clColIdx, clValues, clB, clX, clR, clP, clAp, clDotResult;

            // Pin arrays and create buffers
            fixed (int* rowPtrPtr = rowPtr)
            fixed (int* colIdxPtr = colIdx)
            fixed (float* valuesPtr = values)
            fixed (float* bPtr = b)
            {
                clRowPtr = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                    (nuint)(rowPtr.Length * sizeof(int)), rowPtrPtr, &err);
                if (err != 0) throw new Exception($"Failed to create rowPtr buffer: {err}");

                clColIdx = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                    (nuint)(colIdx.Length * sizeof(int)), colIdxPtr, &err);
                if (err != 0) throw new Exception($"Failed to create colIdx buffer: {err}");

                clValues = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                    (nuint)(values.Length * sizeof(float)), valuesPtr, &err);
                if (err != 0) throw new Exception($"Failed to create values buffer: {err}");

                clB = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                    (nuint)(b.Length * sizeof(float)), bPtr, &err);
                if (err != 0) throw new Exception($"Failed to create b buffer: {err}");
            }

            clX = _cl.CreateBuffer(_context, MemFlags.ReadWrite, (nuint)(n * sizeof(float)), null, &err);
            if (err != 0) throw new Exception($"Failed to create x buffer: {err}");

            clR = _cl.CreateBuffer(_context, MemFlags.ReadWrite, (nuint)(n * sizeof(float)), null, &err);
            if (err != 0) throw new Exception($"Failed to create r buffer: {err}");

            clP = _cl.CreateBuffer(_context, MemFlags.ReadWrite, (nuint)(n * sizeof(float)), null, &err);
            if (err != 0) throw new Exception($"Failed to create p buffer: {err}");

            clAp = _cl.CreateBuffer(_context, MemFlags.ReadWrite, (nuint)(n * sizeof(float)), null, &err);
            if (err != 0) throw new Exception($"Failed to create Ap buffer: {err}");

            // IMPORTANT FIX: the partial-results buffer must be sized to the number of work-groups
            const int localSize = 256;
            var numGroups = (n + localSize - 1) / localSize;
            if (numGroups < 1) numGroups = 1;

            clDotResult = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
                (nuint)(numGroups * sizeof(float)), null, &err);
            if (err != 0) throw new Exception($"Failed to create dot result buffer: {err}");

            // Create kernels
            var spmvKernel = _cl.CreateKernel(_program, "spmv_csr", &err);
            if (err != 0) throw new Exception($"Failed to create spmv kernel: {err}");

            var vectorOpsKernel = _cl.CreateKernel(_program, "vector_ops", &err);
            if (err != 0) throw new Exception($"Failed to create vector_ops kernel: {err}");

            var dotKernel = _cl.CreateKernel(_program, "dot_product", &err);
            if (err != 0) throw new Exception($"Failed to create dot kernel: {err}");

            // Initialize x = 0, r = b, p = r
            var zero = 0.0f;
            _cl.EnqueueFillBuffer(_queue, clX, &zero, sizeof(float), 0, (nuint)(n * sizeof(float)), 0, null, null);
            _cl.EnqueueCopyBuffer(_queue, clB, clR, 0, 0, (nuint)(n * sizeof(float)), 0, null, null);
            _cl.EnqueueCopyBuffer(_queue, clR, clP, 0, 0, (nuint)(n * sizeof(float)), 0, null, null);

            // CG iteration
            var rsold = ComputeDotProduct(clR, clR, n, dotKernel, clDotResult);
            if (Math.Sqrt(rsold) < tolerance)
            {
                var result = new float[n];
                fixed (float* resultPtr = result)
                {
                    _cl.EnqueueReadBuffer(_queue, clX, true, 0, (nuint)(n * sizeof(float)), resultPtr, 0, null, null);
                }

                CleanupBuffers(clRowPtr, clColIdx, clValues, clB, clX, clR, clP, clAp, clDotResult);
                CleanupKernels(spmvKernel, vectorOpsKernel, dotKernel);
                return result;
            }

            for (var iter = 0; iter < maxIterations; iter++)
            {
                // Ap = A * p
                ComputeSpmv(spmvKernel, clRowPtr, clColIdx, clValues, clP, clAp, n);

                // pAp = dot(p, Ap)
                var pAp = ComputeDotProduct(clP, clAp, n, dotKernel, clDotResult);
                if (Math.Abs(pAp) < 1e-10f) break;

                var alpha = rsold / pAp;

                // x = x + alpha * p
                ComputeAxpy(vectorOpsKernel, clX, clP, alpha, n, 1);

                // r = r - alpha * Ap
                ComputeAxpy(vectorOpsKernel, clR, clAp, -alpha, n, 1);

                // rsnew = dot(r, r)
                var rsnew = ComputeDotProduct(clR, clR, n, dotKernel, clDotResult);

                if (Math.Sqrt(rsnew) < tolerance)
                {
                    Logger.Log($"[OpenCL CG] Converged in {iter + 1} iterations");
                    break;
                }

                var beta = rsnew / rsold;

                // p = r + beta * p
                ComputeScale(vectorOpsKernel, clP, beta, n);
                ComputeAxpy(vectorOpsKernel, clP, clR, 1.0f, n, 1);

                rsold = rsnew;

                if (iter % 50 == 0) Logger.Log($"[OpenCL CG] Iteration {iter}, residual: {Math.Sqrt(rsnew):E3}");
            }

            // Read result
            var solution = new float[n];
            fixed (float* solutionPtr = solution)
            {
                _cl.EnqueueReadBuffer(_queue, clX, true, 0, (nuint)(n * sizeof(float)), solutionPtr, 0, null, null);
            }

            // Cleanup
            CleanupBuffers(clRowPtr, clColIdx, clValues, clB, clX, clR, clP, clAp, clDotResult);
            CleanupKernels(spmvKernel, vectorOpsKernel, dotKernel);

            return solution;
        }
    }

    private static unsafe void ComputeSpmv(nint kernel, nint rowPtr, nint colIdx, nint values,
        nint x, nint y, int numRows)
    {
        _cl.SetKernelArg(kernel, 0, (nuint)sizeof(nint), &rowPtr);
        _cl.SetKernelArg(kernel, 1, (nuint)sizeof(nint), &colIdx);
        _cl.SetKernelArg(kernel, 2, (nuint)sizeof(nint), &values);
        _cl.SetKernelArg(kernel, 3, (nuint)sizeof(nint), &x);
        _cl.SetKernelArg(kernel, 4, (nuint)sizeof(nint), &y);
        _cl.SetKernelArg(kernel, 5, sizeof(int), &numRows);

        var globalSize = (nuint)numRows;
        _cl.EnqueueNdrangeKernel(_queue, kernel, 1, null, &globalSize, null, 0, null, null);
        _cl.Finish(_queue);
    }

    private static unsafe void ComputeAxpy(nint kernel, nint y, nint x, float alpha, int n, int op)
    {
        _cl.SetKernelArg(kernel, 0, (nuint)sizeof(nint), &y);
        _cl.SetKernelArg(kernel, 1, (nuint)sizeof(nint), &x);
        _cl.SetKernelArg(kernel, 2, sizeof(float), &alpha);
        _cl.SetKernelArg(kernel, 3, sizeof(int), &n);
        _cl.SetKernelArg(kernel, 4, sizeof(int), &op);

        var globalSize = (nuint)n;
        _cl.EnqueueNdrangeKernel(_queue, kernel, 1, null, &globalSize, null, 0, null, null);
        _cl.Finish(_queue);
    }

    private static unsafe void ComputeScale(nint kernel, nint x, float alpha, int n)
    {
        var op = 2; // scale operation
        _cl.SetKernelArg(kernel, 0, (nuint)sizeof(nint), &x);
        _cl.SetKernelArg(kernel, 1, (nuint)sizeof(nint), &x); // input and output same for scaling
        _cl.SetKernelArg(kernel, 2, sizeof(float), &alpha);
        _cl.SetKernelArg(kernel, 3, sizeof(int), &n);
        _cl.SetKernelArg(kernel, 4, sizeof(int), &op);

        var globalSize = (nuint)n;
        _cl.EnqueueNdrangeKernel(_queue, kernel, 1, null, &globalSize, null, 0, null, null);
        _cl.Finish(_queue);
    }

    private static unsafe float ComputeDotProduct(nint a, nint b, int n, nint kernel, nint result)
    {
        var localSize = 256;
        var numGroups = (n + localSize - 1) / localSize;

        _cl.SetKernelArg(kernel, 0, (nuint)sizeof(nint), &a);
        _cl.SetKernelArg(kernel, 1, (nuint)sizeof(nint), &b);
        _cl.SetKernelArg(kernel, 2, (nuint)sizeof(nint), &result);
        _cl.SetKernelArg(kernel, 3, (nuint)(localSize * sizeof(float)), null); // local memory
        _cl.SetKernelArg(kernel, 4, sizeof(int), &n);

        var globalSize = (nuint)(numGroups * localSize);
        var localSizePtr = (nuint)localSize;
        _cl.EnqueueNdrangeKernel(_queue, kernel, 1, null, &globalSize, &localSizePtr, 0, null, null);
        _cl.Finish(_queue);

        // Read partial results and sum
        var partialResults = new float[numGroups];
        fixed (float* ptr = partialResults)
        {
            _cl.EnqueueReadBuffer(_queue, result, true, 0, (nuint)(numGroups * sizeof(float)), ptr, 0, null, null);
        }

        float sum = 0;
        for (var i = 0; i < numGroups; i++)
            sum += partialResults[i];

        return sum;
    }

    private static (int[] rowPtr, int[] colIdx, float[] values) ConvertToCSR(SparseMatrix matrix)
    {
        var rowPtr = new List<int>();
        var colIdx = new List<int>();
        var values = new List<float>();

        rowPtr.Add(0);

        for (var row = 0; row < matrix.Size; row++)
        {
            var rowData = matrix.GetRow(row);
            var sortedCols = rowData.OrderBy(kvp => kvp.Key);

            foreach (var kvp in sortedCols)
            {
                colIdx.Add(kvp.Key);
                values.Add(kvp.Value);
            }

            rowPtr.Add(values.Count);
        }

        return (rowPtr.ToArray(), colIdx.ToArray(), values.ToArray());
    }

    private static void CleanupBuffers(params nint[] buffers)
    {
        foreach (var buffer in buffers)
            if (buffer != 0)
                _cl.ReleaseMemObject(buffer);
    }

    private static void CleanupKernels(params nint[] kernels)
    {
        foreach (var kernel in kernels)
            if (kernel != 0)
                _cl.ReleaseKernel(kernel);
    }
}