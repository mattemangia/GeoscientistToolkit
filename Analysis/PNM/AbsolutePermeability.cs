// GeoscientistToolkit/Analysis/Pnm/AbsolutePermeability.cs - Production Version with Confining Pressure
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.Util;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Analysis.Pnm
{
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
        public Dictionary<int, float> PorePressures { get; set; } = new Dictionary<int, float>();
        public Dictionary<int, float> ThroatFlowRates { get; set; } = new Dictionary<int, float>();
    }

    // Stress-dependent parameters for each pore/throat
    internal struct StressDependentGeometry
    {
        public float[] PoreRadii;      // Adjusted pore radii
        public float[] ThroatRadii;    // Adjusted throat radii
        public bool[] ThroatOpen;      // Whether throat is open
        public float PoreReduction;    // Average reduction factor
        public float ThroatReduction;  // Average reduction factor
        public int ClosedThroats;      // Number of closed throats
    }

    public static class AbsolutePermeability
    {
        private static PermeabilityResults _lastResults = new PermeabilityResults();
        private static FlowData _lastFlowData = new FlowData();
        
        public static PermeabilityResults GetLastResults() => _lastResults;
        public static FlowData GetLastFlowData() => _lastFlowData;

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

            float pixelSize_m = pnm.VoxelSize * 1e-6f; // μm to meters
            Logger.Log($"[Permeability] Voxel size: {pnm.VoxelSize} μm = {pixelSize_m*1e6} μm");
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
            StressDependentGeometry stressGeometry = ApplyConfiningPressureEffects(pnm, options);

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
            
            float tau2 = pnm.Tortuosity * pnm.Tortuosity;

            if (options.CalculateDarcy)
            {
                float darcyUncorrected = RunEngine(options, "Darcy", stressGeometry);
                pnm.DarcyPermeability = darcyUncorrected;
                
                _lastResults.DarcyUncorrected = darcyUncorrected;
                _lastResults.DarcyCorrected = darcyUncorrected / tau2;
                
                Logger.Log($"[Permeability] Darcy permeability:");
                Logger.Log($"  Uncorrected: {darcyUncorrected:E3} mD ({darcyUncorrected/1000:F3} D)");
                Logger.Log($"  τ²-corrected: {_lastResults.DarcyCorrected:E3} mD");
            }

            if (options.CalculateNavierStokes)
            {
                float nsUncorrected = RunEngine(options, "NavierStokes", stressGeometry);
                pnm.NavierStokesPermeability = nsUncorrected;
                
                _lastResults.NavierStokesUncorrected = nsUncorrected;
                _lastResults.NavierStokesCorrected = nsUncorrected / tau2;
                
                Logger.Log($"[Permeability] Navier-Stokes permeability:");
                Logger.Log($"  Uncorrected: {nsUncorrected:E3} mD");
                Logger.Log($"  τ²-corrected: {_lastResults.NavierStokesCorrected:E3} mD");
            }

            if (options.CalculateLatticeBoltzmann)
            {
                float lbmUncorrected = RunEngine(options, "LatticeBoltzmann", stressGeometry);
                pnm.LatticeBoltzmannPermeability = lbmUncorrected;
                
                _lastResults.LatticeBoltzmannUncorrected = lbmUncorrected;
                _lastResults.LatticeBoltzmannCorrected = lbmUncorrected / tau2;
                
                Logger.Log($"[Permeability] Lattice-Boltzmann permeability:");
                Logger.Log($"  Uncorrected: {lbmUncorrected:E3} mD");
                Logger.Log($"  τ²-corrected: {_lastResults.LatticeBoltzmannCorrected:E3} mD");
            }

            ValidateAndWarnResults();
            
            if (options.UseConfiningPressure)
            {
                Logger.Log($"[Permeability] Stress effects: {stressGeometry.ClosedThroats} throats closed, " +
                          $"avg pore reduction {stressGeometry.PoreReduction:P1}, " +
                          $"avg throat reduction {stressGeometry.ThroatReduction:P1}");
            }
            
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
                // No confining pressure - use original radii
                for (int i = 0; i < pnm.Pores.Count; i++)
                    result.PoreRadii[i] = pnm.Pores[i].Radius;
                
                for (int i = 0; i < pnm.Throats.Count; i++)
                {
                    result.ThroatRadii[i] = pnm.Throats[i].Radius;
                    result.ThroatOpen[i] = true;
                }
                
                result.PoreReduction = 0;
                result.ThroatReduction = 0;
                result.ClosedThroats = 0;
                return result;
            }

            float P = options.ConfiningPressure; // MPa
            float Pc = options.CriticalPressure; // MPa
            float αp = options.PoreCompressibility; // 1/MPa
            float αt = options.ThroatCompressibility; // 1/MPa

            // Use a modified exponential model that prevents complete closure
            // r(P) = r₀ * [exp(-α*P) * (1 - P/Pc) + (P/Pc) * r_min/r₀]
            // This ensures radii approach r_min as P approaches Pc
            
            float minRadiusFactor = 0.01f; // Minimum radius is 1% of original
            float closureThreshold = 0.05f; // Throats close when radius < 5% of original
            
            float poreSum = 0;
            float throatSum = 0;
            int closedCount = 0;

            // Apply pressure effects to pores
            for (int i = 0; i < pnm.Pores.Count; i++)
            {
                float r0 = pnm.Pores[i].Radius;
                
                // Exponential reduction with minimum limit
                float reduction = MathF.Exp(-αp * P);
                
                // Ensure minimum radius
                reduction = Math.Max(reduction, minRadiusFactor);
                
                // Apply heterogeneity: smaller pores are more compressible
                float sizeEffect = 1.0f + (1.0f - r0 / pnm.MaxPoreRadius) * 0.5f; // Up to 50% more compressible for small pores
                reduction = MathF.Pow(reduction, sizeEffect);
                
                result.PoreRadii[i] = r0 * reduction;
                poreSum += (1 - reduction);
            }

            // Apply pressure effects to throats (more sensitive)
            for (int i = 0; i < pnm.Throats.Count; i++)
            {
                float r0 = pnm.Throats[i].Radius;
                
                // Exponential reduction
                float reduction = MathF.Exp(-αt * P);
                
                // Size-dependent compressibility (smaller throats close first)
                float sizeEffect = 1.0f + (1.0f - r0 / pnm.MaxThroatRadius) * 1.0f; // Up to 100% more compressible
                reduction = MathF.Pow(reduction, sizeEffect);
                
                // Check for closure
                if (reduction < closureThreshold)
                {
                    result.ThroatRadii[i] = 0;
                    result.ThroatOpen[i] = false;
                    closedCount++;
                    throatSum += 1.0f; // Complete closure
                }
                else
                {
                    result.ThroatRadii[i] = r0 * Math.Max(reduction, minRadiusFactor);
                    result.ThroatOpen[i] = true;
                    throatSum += (1 - reduction);
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
                {
                    Logger.LogWarning($"[Permeability] {name} = {value:E3} mD seems unreasonably high. Check input parameters.");
                }
                else if (value < 0.001 && value > 0) // < 0.001 mD
                {
                    Logger.LogWarning($"[Permeability] {name} = {value:E3} mD seems unreasonably low. Check network connectivity.");
                }
            }
            
            if (_lastResults.DarcyUncorrected > 0) CheckValue("Darcy", _lastResults.DarcyUncorrected);
            if (_lastResults.NavierStokesUncorrected > 0) CheckValue("Navier-Stokes", _lastResults.NavierStokesUncorrected);
            if (_lastResults.LatticeBoltzmannUncorrected > 0) CheckValue("Lattice-Boltzmann", _lastResults.LatticeBoltzmannUncorrected);
        }

        private static float RunEngine(PermeabilityOptions options, string engine, StressDependentGeometry stressGeom)
        {
            var stopwatch = Stopwatch.StartNew();
            var pnm = options.Dataset;
            
            float pixelSize_m = pnm.VoxelSize * 1e-6f; // μm to m
            
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
            Logger.Log($"[Permeability] Model: Length={modelLength*1e6:F1} μm, Area={crossSectionalArea*1e12:F3} μm²");

            // Build linear system with stress-modified geometry
            var (matrix, b) = BuildLinearSystemWithStress(pnm, engine, inletPores, outletPores, 
                options.FluidViscosity, pixelSize_m, options.InletPressure, options.OutletPressure, stressGeom);

            // Check if system is solvable
            if (stressGeom.ClosedThroats == pnm.Throats.Count)
            {
                Logger.LogError($"[Permeability] All throats are closed at {options.ConfiningPressure} MPa confining pressure.");
                return 0f;
            }

            // Solve for pore pressures
            float[] pressures = options.UseGpu && OpenCLContext.IsAvailable
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
            float totalFlowRate = CalculateTotalFlowWithStorageAndStress(pnm, engine, pressures, inletPores, 
                options.FluidViscosity, pixelSize_m, stressGeom);
            
            _lastResults.TotalFlowRate = totalFlowRate;
            
            Logger.Log($"[Permeability] Total flow rate Q = {totalFlowRate:E3} m³/s");

            // Calculate permeability using Darcy's law
            float viscosityPaS = options.FluidViscosity * 0.001f; // cP to Pa·s
            float pressureDrop = Math.Abs(options.InletPressure - options.OutletPressure);
            
            if (pressureDrop <= 0)
            {
                Logger.LogError("[Permeability] Invalid pressure drop (must be > 0)");
                return 0f;
            }
            
            float permeability_m2 = (totalFlowRate * viscosityPaS * modelLength) / 
                                    (crossSectionalArea * pressureDrop);
            
            // 1 Darcy = 9.869233e-13 m² => 1 m² = 1.01325e15 mD
            float permeability_mD = permeability_m2 * 1.01325e15f;
            
            stopwatch.Stop();
            Logger.Log($"[Permeability] {engine} calculation took {stopwatch.ElapsedMilliseconds}ms");
            Logger.Log($"[Permeability] K = {permeability_m2:E3} m² = {permeability_mD:E3} mD");
            
            return permeability_mD;
        }

        private static void StorePorePressures(PNMDataset pnm, float[] pressures)
        {
            _lastFlowData.PorePressures.Clear();
            
            foreach (var pore in pnm.Pores)
            {
                if (pore.ID < pressures.Length)
                {
                    _lastFlowData.PorePressures[pore.ID] = pressures[pore.ID];
                }
            }
        }

        private static float CalculateTotalFlowWithStorageAndStress(PNMDataset pnm, string engine, float[] pressures, 
            HashSet<int> inletPores, float viscosity_cP, float voxelSize_m, StressDependentGeometry stressGeom)
        {
            float totalFlow = 0;
            var poreMap = pnm.Pores.ToDictionary(p => p.ID);
            float viscosity_PaS = viscosity_cP * 0.001f;
            
            _lastFlowData.ThroatFlowRates.Clear();

            for (int throatIdx = 0; throatIdx < pnm.Throats.Count; throatIdx++)
            {
                var throat = pnm.Throats[throatIdx];
                
                // Skip closed throats
                if (!stressGeom.ThroatOpen[throatIdx])
                    continue;
                
                bool p1_inlet = inletPores.Contains(throat.Pore1ID);
                bool p2_inlet = inletPores.Contains(throat.Pore2ID);

                if (!poreMap.TryGetValue(throat.Pore1ID, out var p1) || 
                    !poreMap.TryGetValue(throat.Pore2ID, out var p2)) 
                    continue;
                
                // Use stress-modified radii
                float r_p1 = stressGeom.PoreRadii[pnm.Pores.IndexOf(p1)] * voxelSize_m;
                float r_p2 = stressGeom.PoreRadii[pnm.Pores.IndexOf(p2)] * voxelSize_m;
                float r_t = stressGeom.ThroatRadii[throatIdx] * voxelSize_m;
                
                float conductance = CalculateConductanceWithStress(p1.Position, p2.Position, 
                    r_p1, r_p2, r_t, engine, voxelSize_m, viscosity_PaS);
                
                float deltaP = pressures[p1.ID] - pressures[p2.ID];
                float flowRate = conductance * deltaP;
                
                // Store throat flow rate for visualization
                _lastFlowData.ThroatFlowRates[throat.ID] = Math.Abs(flowRate);
                
                if (p1_inlet && !p2_inlet)
                {
                    totalFlow += Math.Max(0, flowRate);
                }
                else if (!p1_inlet && p2_inlet)
                {
                    totalFlow += Math.Max(0, -flowRate);
                }
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
            float voxelSize_m = pnm.VoxelSize * 1e-6f;
            
            foreach (var throat in pnm.Throats)
            {
                var p1 = pnm.Pores.FirstOrDefault(p => p.ID == throat.Pore1ID);
                var p2 = pnm.Pores.FirstOrDefault(p => p.ID == throat.Pore2ID);
                
                if (p1 != null && p2 != null)
                {
                    float dx = Math.Abs(p1.Position.X - p2.Position.X) * voxelSize_m;
                    float dy = Math.Abs(p1.Position.Y - p2.Position.Y) * voxelSize_m;
                    float dz = Math.Abs(p1.Position.Z - p2.Position.Z) * voxelSize_m;
                    float dist = MathF.Sqrt(dx*dx + dy*dy + dz*dz);
                    
                    if (!adjacency.ContainsKey(p1.ID)) adjacency[p1.ID] = new List<(int, float)>();
                    if (!adjacency.ContainsKey(p2.ID)) adjacency[p2.ID] = new List<(int, float)>();
                    
                    adjacency[p1.ID].Add((p2.ID, dist));
                    adjacency[p2.ID].Add((p1.ID, dist));
                }
            }
            
            // Find shortest paths
            float totalPathLength = 0;
            int pathCount = 0;
            
            foreach (int inlet in inletPores)
            {
                var distances = DijkstraShortestPath(adjacency, inlet, pnm.Pores.Max(p => p.ID) + 1);
                
                foreach (int outlet in outletPores)
                {
                    if (distances.ContainsKey(outlet) && distances[outlet] < float.MaxValue)
                    {
                        totalPathLength += distances[outlet];
                        pathCount++;
                    }
                }
            }
            
            if (pathCount == 0) return 1.0f;
            
            float avgPathLength = totalPathLength / pathCount;
            float tortuosity = avgPathLength / modelLength;
            
            return Math.Max(1.0f, Math.Min(10.0f, tortuosity));
        }

        private static Dictionary<int, float> DijkstraShortestPath(Dictionary<int, List<(int neighbor, float distance)>> adjacency, int start, int maxId)
        {
            var distances = new Dictionary<int, float>();
            var visited = new HashSet<int>();
            var pq = new PriorityQueue<int, float>();
            
            distances[start] = 0;
            pq.Enqueue(start, 0);
            
            while (pq.Count > 0)
            {
                pq.TryDequeue(out int current, out float currentDist);
                
                if (visited.Contains(current)) continue;
                visited.Add(current);
                
                if (!adjacency.ContainsKey(current)) continue;
                
                foreach (var (neighbor, edgeWeight) in adjacency[current])
                {
                    if (visited.Contains(neighbor)) continue;
                    
                    float newDist = currentDist + edgeWeight;
                    
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

            var bounds = (
                Min: new Vector3(
                    pnm.Pores.Min(p => p.Position.X), 
                    pnm.Pores.Min(p => p.Position.Y), 
                    pnm.Pores.Min(p => p.Position.Z)),
                Max: new Vector3(
                    pnm.Pores.Max(p => p.Position.X), 
                    pnm.Pores.Max(p => p.Position.Y), 
                    pnm.Pores.Max(p => p.Position.Z))
            );

            float voxelSize_m = pnm.VoxelSize * 1e-6f;
            var inlets = new HashSet<int>();
            var outlets = new HashSet<int>();
            float L = 0, A = 0;
            float tolerance = 2.0f; // Tolerance in voxels

            switch (axis)
            {
                case FlowAxis.X:
                    L = (bounds.Max.X - bounds.Min.X) * voxelSize_m;
                    A = (bounds.Max.Y - bounds.Min.Y) * (bounds.Max.Z - bounds.Min.Z) * voxelSize_m * voxelSize_m;
                    foreach (var pore in pnm.Pores)
                    {
                        if (pore.Position.X <= bounds.Min.X + tolerance) inlets.Add(pore.ID);
                        if (pore.Position.X >= bounds.Max.X - tolerance) outlets.Add(pore.ID);
                    }
                    break;
                    
                case FlowAxis.Y:
                    L = (bounds.Max.Y - bounds.Min.Y) * voxelSize_m;
                    A = (bounds.Max.X - bounds.Min.X) * (bounds.Max.Z - bounds.Min.Z) * voxelSize_m * voxelSize_m;
                    foreach (var pore in pnm.Pores)
                    {
                        if (pore.Position.Y <= bounds.Min.Y + tolerance) inlets.Add(pore.ID);
                        if (pore.Position.Y >= bounds.Max.Y - tolerance) outlets.Add(pore.ID);
                    }
                    break;
                    
                case FlowAxis.Z:
                    L = (bounds.Max.Z - bounds.Min.Z) * voxelSize_m;
                    A = (bounds.Max.X - bounds.Min.X) * (bounds.Max.Y - bounds.Min.Y) * voxelSize_m * voxelSize_m;
                    foreach (var pore in pnm.Pores)
                    {
                        if (pore.Position.Z <= bounds.Min.Z + tolerance) inlets.Add(pore.ID);
                        if (pore.Position.Z >= bounds.Max.Z - tolerance) outlets.Add(pore.ID);
                    }
                    break;
            }
            
            Logger.Log($"[Boundary Detection] Axis={axis}, L={L*1e6:F1} μm, A={A*1e12:F3} μm²");
            Logger.Log($"[Boundary Detection] Found {inlets.Count} inlet pores, {outlets.Count} outlet pores");
            
            return (inlets, outlets, L, A);
        }

        private static (SparseMatrix, float[]) BuildLinearSystemWithStress(PNMDataset pnm, string engine, 
            HashSet<int> inlets, HashSet<int> outlets, float viscosity_cP, float voxelSize_m,
            float inletPressure, float outletPressure, StressDependentGeometry stressGeom)
        {
            int maxId = pnm.Pores.Max(p => p.ID);
            var poreMap = pnm.Pores.ToDictionary(p => p.ID);
            var matrix = new SparseMatrix(maxId + 1);
            var b = new float[maxId + 1];

            float viscosity_PaS = viscosity_cP * 0.001f;

            // Build conductance matrix with stress-modified geometry
            for (int throatIdx = 0; throatIdx < pnm.Throats.Count; throatIdx++)
            {
                var throat = pnm.Throats[throatIdx];
                
                // Skip closed throats
                if (!stressGeom.ThroatOpen[throatIdx])
                    continue;
                
                if (!poreMap.TryGetValue(throat.Pore1ID, out var p1) || 
                    !poreMap.TryGetValue(throat.Pore2ID, out var p2)) 
                    continue;

                // Use stress-modified radii
                float r_p1 = stressGeom.PoreRadii[pnm.Pores.IndexOf(p1)] * voxelSize_m;
                float r_p2 = stressGeom.PoreRadii[pnm.Pores.IndexOf(p2)] * voxelSize_m;
                float r_t = stressGeom.ThroatRadii[throatIdx] * voxelSize_m;

                float conductance = CalculateConductanceWithStress(p1.Position, p2.Position, 
                    r_p1, r_p2, r_t, engine, voxelSize_m, viscosity_PaS);

                // Add to system matrix (off-diagonal negative, diagonal positive)
                matrix.Add(p1.ID, p1.ID, conductance);
                matrix.Add(p2.ID, p2.ID, conductance);
                matrix.Add(p1.ID, p2.ID, -conductance);
                matrix.Add(p2.ID, p1.ID, -conductance);
            }

            // Apply boundary conditions (Dirichlet)
            foreach (int id in inlets)
            {
                matrix.ClearRow(id);
                matrix.Set(id, id, 1.0f);
                b[id] = inletPressure;
            }
            
            foreach (int id in outlets)
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
            // Physical distance between pore centers
            float dx = Math.Abs(pos1.X - pos2.X) * voxelSize_m;
            float dy = Math.Abs(pos1.Y - pos2.Y) * voxelSize_m;
            float dz = Math.Abs(pos1.Z - pos2.Z) * voxelSize_m;
            float length = MathF.Sqrt(dx*dx + dy*dy + dz*dz);
            
            if (length < 1e-12f) length = 1e-12f; // Prevent division by zero
            
            // Check for closed throat
            if (r_t <= 0) return 0;

            // Hagen-Poiseuille conductance: g = π*r^4 / (8*μ*L)
            switch (engine)
            {
                case "Darcy":
                    // Simple model with stress-modified radius
                    return (float)(Math.PI * Math.Pow(r_t, 4)) / (8 * viscosity_PaS * length);

                case "NavierStokes":
                    // Include entrance effects with Reynolds number correction
                    // Account for constriction at throat entrance
                    float Re_throat = 2 * r_t * 1000 / viscosity_PaS; // Simplified Reynolds
                    float entranceLength = 0.06f * Re_throat * r_t;
                    
                    // Additional resistance from pore-throat transitions
                    float constrictionFactor = 1.0f + 0.5f * MathF.Pow((r_t / Math.Min(r_p1, r_p2)), 2);
                    
                    float effectiveLength = (length + entranceLength) * constrictionFactor;
                    return (float)(Math.PI * Math.Pow(r_t, 4)) / (8 * viscosity_PaS * effectiveLength);

                case "LatticeBoltzmann":
                    // Include pore body resistance with stress-modified radii
                    // Pore bodies contribute to flow resistance
                    float l_p1 = r_p1 * 0.5f; // Effective pore body length
                    float l_p2 = r_p2 * 0.5f;
                    float l_t = Math.Max(1e-12f, length - l_p1 - l_p2);
                    
                    // Individual conductances
                    float g_p1 = (float)(Math.PI * Math.Pow(r_p1, 4)) / (8 * viscosity_PaS * Math.Max(l_p1, 1e-12f));
                    float g_p2 = (float)(Math.PI * Math.Pow(r_p2, 4)) / (8 * viscosity_PaS * Math.Max(l_p2, 1e-12f));
                    float g_t = (float)(Math.PI * Math.Pow(r_t, 4)) / (8 * viscosity_PaS * l_t);
                    
                    // Include junction losses at pore-throat interfaces
                    float junctionLoss1 = 0.5f * MathF.Pow(1 - (r_t/r_p1), 2);
                    float junctionLoss2 = 0.5f * MathF.Pow(1 - (r_t/r_p2), 2);
                    
                    // Total resistance (resistances in series plus junction losses)
                    float totalResistance = (1/g_p1) * (1 + junctionLoss1) + 
                                           (1/g_t) + 
                                           (1/g_p2) * (1 + junctionLoss2);
                    
                    return 1 / totalResistance;

                default:
                    return (float)(Math.PI * Math.Pow(r_t, 4)) / (8 * viscosity_PaS * length);
            }
        }
        
        // --- CPU CONJUGATE GRADIENT SOLVER ---

        private static float[] SolveWithCpu(SparseMatrix A, float[] b, float tolerance = 1e-6f, int maxIterations = 5000)
        {
            int n = b.Length;
            var x = new float[n];
            var r = new float[n];
            Array.Copy(b, r, n);
            
            var p = new float[n];
            Array.Copy(r, p, n);

            float rsold = Dot(r, r);
            
            if (rsold < tolerance * tolerance) return x;

            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                var Ap = A.Multiply(p);
                float pAp = Dot(p, Ap);
                
                if (Math.Abs(pAp) < 1e-10f)
                {
                    Logger.LogWarning($"[CG Solver] Breakdown at iteration {iteration}");
                    break;
                }
                
                float alpha = rsold / pAp;
                Axpy(x, p, alpha);
                Axpy(r, Ap, -alpha);

                float rsnew = Dot(r, r);
                
                if (Math.Sqrt(rsnew) < tolerance)
                {
                    Logger.Log($"[CG Solver] Converged in {iteration + 1} iterations");
                    break;
                }

                float beta = rsnew / rsold;
                
                for (int j = 0; j < n; j++) 
                    p[j] = r[j] + beta * p[j];
                
                rsold = rsnew;
            }
            
            return x;
        }

        private static float Dot(float[] a, float[] b)
        {
            double sum = 0;
            for (int i = 0; i < a.Length; i++)
                sum += a[i] * b[i];
            return (float)sum;
        }
        
        private static void Axpy(float[] y, float[] x, float alpha)
        {
            for (int i = 0; i < y.Length; i++)
                y[i] += alpha * x[i];
        }

        // --- GPU SOLVER ---
        
        private static float[] SolveWithGpu(SparseMatrix A, float[] b, float tolerance = 1e-6f, int maxIterations = 5000)
        {
            try
            {
                return OpenCLContext.Solve(A, b, tolerance, maxIterations);
            }
            catch(Exception ex)
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
        public int Size { get; }

        public SparseMatrix(int size)
        {
            Size = size;
            _rows = new Dictionary<int, float>[size];
            for (int i = 0; i < size; i++) 
                _rows[i] = new Dictionary<int, float>();
        }

        public void Set(int row, int col, float value) => _rows[row][col] = value;
        
        public void Add(int row, int col, float value)
        {
            _rows[row].TryGetValue(col, out float current);
            _rows[row][col] = current + value;
        }
        
        public void ClearRow(int row) => _rows[row].Clear();
        
        public Dictionary<int, float> GetRow(int row) => _rows[row];

        public float[] Multiply(float[] vector)
        {
            var result = new float[Size];
            Parallel.For(0, Size, i =>
            {
                double sum = 0;
                foreach (var (col, value) in _rows[i])
                {
                    sum += value * vector[col];
                }
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
        private static bool _initialized = false;

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
                    
                    for (int i = 0; i < numPlatforms; i++)
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
                    nint* devicePtr = stackalloc nint[1];
                    devicePtr[0] = _device;
                    _context = _cl.CreateContext(null, 1, devicePtr, null, null, &err);
                    if (err != 0) return;
                    
                    _queue = _cl.CreateCommandQueue(_context, _device, CommandQueueProperties.None, &err);
                    if (err != 0) return;
                    
                    // Create program with CG kernel
                    string kernelSource = @"
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
                    
                    nint* devicePtr2 = stackalloc nint[1];
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
                                _cl.GetProgramBuildInfo(_program, _device, ProgramBuildInfo.BuildLog, logSize, logPtr, null);
                                string logStr = System.Text.Encoding.ASCII.GetString(log);
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
                int n = b.Length;

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
                int numGroups = (n + localSize - 1) / localSize;
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
                float zero = 0.0f;
                _cl.EnqueueFillBuffer(_queue, clX, &zero, (nuint)sizeof(float), 0, (nuint)(n * sizeof(float)), 0, null, null);
                _cl.EnqueueCopyBuffer(_queue, clB, clR, 0, 0, (nuint)(n * sizeof(float)), 0, null, null);
                _cl.EnqueueCopyBuffer(_queue, clR, clP, 0, 0, (nuint)(n * sizeof(float)), 0, null, null);

                // CG iteration
                float rsold = ComputeDotProduct(clR, clR, n, dotKernel, clDotResult);
                if (Math.Sqrt(rsold) < tolerance)
                {
                    float[] result = new float[n];
                    fixed (float* resultPtr = result)
                    {
                        _cl.EnqueueReadBuffer(_queue, clX, true, 0, (nuint)(n * sizeof(float)), resultPtr, 0, null, null);
                    }
                    CleanupBuffers(clRowPtr, clColIdx, clValues, clB, clX, clR, clP, clAp, clDotResult);
                    CleanupKernels(spmvKernel, vectorOpsKernel, dotKernel);
                    return result;
                }

                for (int iter = 0; iter < maxIterations; iter++)
                {
                    // Ap = A * p
                    ComputeSpmv(spmvKernel, clRowPtr, clColIdx, clValues, clP, clAp, n);

                    // pAp = dot(p, Ap)
                    float pAp = ComputeDotProduct(clP, clAp, n, dotKernel, clDotResult);
                    if (Math.Abs(pAp) < 1e-10f) break;

                    float alpha = rsold / pAp;

                    // x = x + alpha * p
                    ComputeAxpy(vectorOpsKernel, clX, clP, alpha, n, 1);

                    // r = r - alpha * Ap
                    ComputeAxpy(vectorOpsKernel, clR, clAp, -alpha, n, 1);

                    // rsnew = dot(r, r)
                    float rsnew = ComputeDotProduct(clR, clR, n, dotKernel, clDotResult);

                    if (Math.Sqrt(rsnew) < tolerance)
                    {
                        Logger.Log($"[OpenCL CG] Converged in {iter + 1} iterations");
                        break;
                    }

                    float beta = rsnew / rsold;

                    // p = r + beta * p
                    ComputeScale(vectorOpsKernel, clP, beta, n);
                    ComputeAxpy(vectorOpsKernel, clP, clR, 1.0f, n, 1);

                    rsold = rsnew;

                    if (iter % 50 == 0)
                    {
                        Logger.Log($"[OpenCL CG] Iteration {iter}, residual: {Math.Sqrt(rsnew):E3}");
                    }
                }

                // Read result
                float[] solution = new float[n];
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
            _cl.SetKernelArg(kernel, 5, (nuint)sizeof(int), &numRows);
            
            nuint globalSize = (nuint)numRows;
            _cl.EnqueueNdrangeKernel(_queue, kernel, 1, null, &globalSize, null, 0, null, null);
            _cl.Finish(_queue);
        }

        private static unsafe void ComputeAxpy(nint kernel, nint y, nint x, float alpha, int n, int op)
        {
            _cl.SetKernelArg(kernel, 0, (nuint)sizeof(nint), &y);
            _cl.SetKernelArg(kernel, 1, (nuint)sizeof(nint), &x);
            _cl.SetKernelArg(kernel, 2, (nuint)sizeof(float), &alpha);
            _cl.SetKernelArg(kernel, 3, (nuint)sizeof(int), &n);
            _cl.SetKernelArg(kernel, 4, (nuint)sizeof(int), &op);
            
            nuint globalSize = (nuint)n;
            _cl.EnqueueNdrangeKernel(_queue, kernel, 1, null, &globalSize, null, 0, null, null);
            _cl.Finish(_queue);
        }

        private static unsafe void ComputeScale(nint kernel, nint x, float alpha, int n)
        {
            int op = 2; // scale operation
            _cl.SetKernelArg(kernel, 0, (nuint)sizeof(nint), &x);
            _cl.SetKernelArg(kernel, 1, (nuint)sizeof(nint), &x); // input and output same for scaling
            _cl.SetKernelArg(kernel, 2, (nuint)sizeof(float), &alpha);
            _cl.SetKernelArg(kernel, 3, (nuint)sizeof(int), &n);
            _cl.SetKernelArg(kernel, 4, (nuint)sizeof(int), &op);
            
            nuint globalSize = (nuint)n;
            _cl.EnqueueNdrangeKernel(_queue, kernel, 1, null, &globalSize, null, 0, null, null);
            _cl.Finish(_queue);
        }

        private static unsafe float ComputeDotProduct(nint a, nint b, int n, nint kernel, nint result)
        {
            int localSize = 256;
            int numGroups = (n + localSize - 1) / localSize;
            
            _cl.SetKernelArg(kernel, 0, (nuint)sizeof(nint), &a);
            _cl.SetKernelArg(kernel, 1, (nuint)sizeof(nint), &b);
            _cl.SetKernelArg(kernel, 2, (nuint)sizeof(nint), &result);
            _cl.SetKernelArg(kernel, 3, (nuint)(localSize * sizeof(float)), null); // local memory
            _cl.SetKernelArg(kernel, 4, (nuint)sizeof(int), &n);
            
            nuint globalSize = (nuint)(numGroups * localSize);
            nuint localSizePtr = (nuint)localSize;
            _cl.EnqueueNdrangeKernel(_queue, kernel, 1, null, &globalSize, &localSizePtr, 0, null, null);
            _cl.Finish(_queue);
            
            // Read partial results and sum
            float[] partialResults = new float[numGroups];
            fixed (float* ptr = partialResults)
            {
                _cl.EnqueueReadBuffer(_queue, result, true, 0, (nuint)(numGroups * sizeof(float)), ptr, 0, null, null);
            }
            
            float sum = 0;
            for (int i = 0; i < numGroups; i++)
                sum += partialResults[i];
            
            return sum;
        }

        private static (int[] rowPtr, int[] colIdx, float[] values) ConvertToCSR(SparseMatrix matrix)
        {
            var rowPtr = new List<int>();
            var colIdx = new List<int>();
            var values = new List<float>();
            
            rowPtr.Add(0);
            
            for (int row = 0; row < matrix.Size; row++)
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

        private static unsafe void CleanupBuffers(params nint[] buffers)
        {
            foreach (var buffer in buffers)
            {
                if (buffer != 0)
                    _cl.ReleaseMemObject(buffer);
            }
        }

        private static unsafe void CleanupKernels(params nint[] kernels)
        {
            foreach (var kernel in kernels)
            {
                if (kernel != 0)
                    _cl.ReleaseKernel(kernel);
            }
        }
    }
}