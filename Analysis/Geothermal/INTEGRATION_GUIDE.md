# Geothermal Simulation Enhancements - Integration Guide

This document provides step-by-step instructions for integrating the new geothermal simulation features into the main solve loop.

## Overview

Five new solver modules have been added:
1. **MultiphaseFlowSolver** - Water-Steam-CO2 with salinity
2. **AdaptiveMeshRefinement** - Dynamic grid refinement
3. **TimeVaryingBoundaryConditions** - Seasonal/daily variations
4. **EnhancedHVACCalculator** - Realistic COP calculations
5. **FracturedMediaSolver** - Dual-continuum model

All solvers are **already initialized** in the constructor based on option flags.

## Integration Points in Solve Loop

### Location: `GeothermalSimulationSolver.cs` - Main Time Step Loop

Find the main time stepping loop (typically around line 800-1200). Add the following calls at the appropriate locations:

---

## 1. Time-Varying Boundary Conditions

**When to call:** At the **START of each timestep** (before solving equations)

```csharp
// At beginning of timestep loop
double currentTime = CurrentSimulationTime;

if (_timeVaryingBC != null)
{
    // Get time-varying inlet temperature
    float inletTemp_C = _timeVaryingBC.GetInletTemperature(currentTime);
    _options.FluidInletTemperature = inletTemp_C + 273.15; // Convert to K

    // Get time-varying mass flow rate
    float massFlowRate = _timeVaryingBC.GetMassFlowRate(currentTime);
    _options.FluidMassFlowRate = massFlowRate;

    // Store history for results
    results.InletTemperatureHistory.Add((currentTime, inletTemp_C));
    results.ActualFlowRateHistory.Add((currentTime, massFlowRate));
    results.LoadDemandHistory.Add((currentTime, _timeVaryingBC.GetLoadDemand(currentTime)));
    results.OutdoorTemperatureHistory.Add((currentTime, _timeVaryingBC.GetOutdoorTemperature(currentTime)));
}
```

---

## 2. Multiphase Flow Solver

**When to call:** **AFTER pressure and temperature have been updated**, but **BEFORE final convergence check**

```csharp
// After solving heat and flow equations, update multiphase properties
if (_multiphaseSolver != null)
{
    // Update phase properties (density, viscosity, CO2 solubility)
    _multiphaseSolver.UpdatePhaseProperties(_pressure, _temperature, dt);

    // Update saturations (implicit)
    _multiphaseSolver.UpdateSaturations(_pressure, _temperature, dt);

    // Store results every N steps
    if (timestep % _options.SaveInterval == 0)
    {
        results.WaterSaturationField = _multiphaseSolver.GetWaterSaturation();
        results.GasSaturationField = _multiphaseSolver.GetGasSaturation();
        results.CO2SaturationField = _multiphaseSolver.GetCO2LiquidSaturation();
        results.BrineDensityField = _multiphaseSolver.GetBrineDensity();
        results.DissolvedCO2Field = _multiphaseSolver.GetDissolvedCO2();
        results.SalinityField = _multiphaseSolver.GetSalinity();
        results.RelativePermeabilityWaterField = _multiphaseSolver.GetRelPermWater();
        results.RelativePermeabilityGasField = _multiphaseSolver.GetRelPermGas();
        results.CapillaryPressureField = _multiphaseSolver.GetCapillaryPressure();
    }
}
```

---

## 3. Fractured Media Solver

**When to call:** **AFTER updating temperature and pressure fields**, in parallel with or after multiphase

```csharp
// Update fractured media dual-continuum model
if (_fracturedMediaSolver != null)
{
    // Set current fields
    _fracturedMediaSolver.SetMatrixTemperature(_temperature);
    _fracturedMediaSolver.SetFractureTemperature(_temperature); // Initially same
    _fracturedMediaSolver.SetMatrixPressure(_pressure);
    _fracturedMediaSolver.SetFracturePressure(_pressure);

    // Update matrix-fracture transfer
    _fracturedMediaSolver.UpdateDualContinuum(dt);

    // Get updated temperatures for next iteration
    var matrixTemp = _fracturedMediaSolver.GetMatrixTemperature();
    var fractureTemp = _fracturedMediaSolver.GetFractureTemperature();

    // Blend or use fracture temperature for flow calculations
    // (Fractures dominate flow in highly fractured media)
    // _temperature = fractureTemp;  // Option 1: Use fracture only
    // Or blend: _temperature = 0.5 * matrixTemp + 0.5 * fractureTemp;

    // Store results
    if (timestep % _options.SaveInterval == 0)
    {
        results.MatrixTemperatureField = _fracturedMediaSolver.GetMatrixTemperature();
        results.FractureTemperatureField = _fracturedMediaSolver.GetFractureTemperature();
        results.MatrixPressureField = _fracturedMediaSolver.GetMatrixPressure();
        results.FracturePressureField = _fracturedMediaSolver.GetFracturePressure();
        results.FractureApertureField = _fracturedMediaSolver.GetFractureAperture();
        results.FracturePermeabilityField = _fracturedMediaSolver.GetFracturePermeability();
    }
}
```

---

## 4. Adaptive Mesh Refinement

**When to call:** **Every N timesteps** (e.g., every 10 steps), **BEFORE solving equations**

```csharp
// Adaptive mesh refinement every N steps
if (_amrSolver != null && timestep % _options.RefinementInterval == 0)
{
    // Analyze fields and determine refinement
    float[,,] saturation = _multiphaseSolver?.GetWaterSaturation(); // May be null
    _amrSolver.AnalyzeRefinement(_temperature, _pressure, saturation, currentTime);

    // Apply refinement/coarsening
    _amrSolver.ApplyRefinement();

    // Get effective grid spacing for numerical methods
    // (Can be used to adjust CFL condition, diffusion coefficients, etc.)
    var refinementLevels = _amrSolver.GetRefinementLevels();

    // Store results
    results.RefinementLevelField = refinementLevels;
    results.TemperatureGradientField = _amrSolver.GetTemperatureGradient();
    results.RefinementHistory.Add((currentTime, /* count refined */, /* count coarsened */));
}
```

---

## 5. Enhanced HVAC Calculator

**When to call:** **AFTER heat extraction rate is calculated**, typically at end of timestep

```csharp
// Calculate enhanced HVAC performance
if (_hvacCalculator != null)
{
    // Get outlet temperature from heat exchanger
    float outletTemp_C = fluidTempUp[nz-1] - 273.15f; // Convert to Celsius
    float inletTemp_C = (float)(_options.FluidInletTemperature - 273.15);

    // Calculate entering water temperature to heat pump
    float heatExtracted = /* calculated heat extraction rate (W) */;
    float massFlowRate = (float)_options.FluidMassFlowRate;
    float enteringWaterTemp = _hvacCalculator.CalculateEnteringWaterTemp(
        outletTemp_C, heatExtracted, massFlowRate);

    // Get outdoor temperature for COP calculation
    float outdoorTemp = _timeVaryingBC?.GetOutdoorTemperature(currentTime) ?? 10.0f;

    // Calculate supply temperature needed
    float supplyTemp = _hvacCalculator.CalculateSupplyTemperature(outdoorTemp);

    // Calculate instantaneous COP (heating mode)
    float loadDemand = _timeVaryingBC?.GetLoadDemand(currentTime) ?? _options.PeakHeatingLoad;
    float partLoadRatio = loadDemand / _options.PeakHeatingLoad;

    float instantCOP = _hvacCalculator.CalculateHeatingCOP(
        enteringWaterTemp,  // Source temperature
        supplyTemp,          // Sink temperature
        partLoadRatio        // Part-load ratio
    );

    // Calculate compressor power
    float compressorPower = _hvacCalculator.CalculateCompressorPower(loadDemand, instantCOP);

    // Update seasonal metrics
    _hvacCalculator.UpdateSeasonalMetrics(loadDemand, compressorPower, dt, true);

    // Store history
    results.InstantaneousCOP.Add((currentTime, instantCOP));
    results.PartLoadRatioHistory.Add((currentTime, partLoadRatio));
    results.CompressorPowerHistory.Add((currentTime, compressorPower));
    results.EnteringWaterTemperatureHistory.Add((currentTime, enteringWaterTemp));
    results.SupplyTemperatureHistory.Add((currentTime, supplyTemp));
}
```

---

## 6. Final Results Collection

**When to call:** **At the END of simulation**, in the results finalization section

```csharp
// Collect final HVAC metrics
if (_hvacCalculator != null)
{
    var hvacMetrics = _hvacCalculator.GetPerformanceMetrics();
    results.SeasonalPerformanceFactor = hvacMetrics.SPF;
    results.SeasonalCOPHeating = hvacMetrics.SCOP;
    results.SeasonalEER = hvacMetrics.SEER;
    results.TotalHeatDelivered = hvacMetrics.TotalHeatDelivered;
    results.TotalWorkInput = hvacMetrics.TotalWorkInput;
    results.TotalHeatingHours = hvacMetrics.TotalHeatingHours;
    results.TotalCoolingHours = hvacMetrics.TotalCoolingHours;
}

// Collect final AMR statistics
if (_amrSolver != null)
{
    results.TotalRefinementOperations = /* count from history */;
    results.TotalCoarseningOperations = /* count from history */;
}

// Collect final multiphase fields
if (_multiphaseSolver != null)
{
    results.WaterSaturationField = _multiphaseSolver.GetWaterSaturation();
    results.GasSaturationField = _multiphaseSolver.GetGasSaturation();
    results.CO2SaturationField = _multiphaseSolver.GetCO2LiquidSaturation();
    // ... etc
}

// Collect final fractured media results
if (_fracturedMediaSolver != null)
{
    results.MatrixTemperatureField = _fracturedMediaSolver.GetMatrixTemperature();
    results.FractureTemperatureField = _fracturedMediaSolver.GetFractureTemperature();
    // Calculate average temp difference
    // results.AverageMatrixFractureTempDifference = ...
}
```

---

## Execution Order Summary

Within each timestep:

```
1. START OF TIMESTEP
   └─> TimeVaryingBC: Update inlet temp, flow rate

2. SOLVE HEAT EQUATION
   └─> (Existing heat solver code)

3. SOLVE FLOW EQUATION
   └─> (Existing flow solver code)

4. UPDATE MULTIPHASE (if enabled)
   └─> MultiphaseFlowSolver: Update properties & saturations

5. UPDATE FRACTURED MEDIA (if enabled)
   └─> FracturedMediaSolver: Matrix-fracture transfer

6. CHECK ADAPTIVE REFINEMENT (every N steps)
   └─> AMR: Analyze gradients, refine/coarsen

7. CALCULATE HVAC PERFORMANCE
   └─> EnhancedHVAC: COP, compressor power, seasonal metrics

8. CONVERGENCE CHECK & STORE RESULTS
   └─> (Existing convergence check)

9. NEXT TIMESTEP
```

---

## Feature Toggle Reference

All features are controlled by options in `GeothermalSimulationOptions`:

```csharp
// Enable/disable features
options.EnableMultiphaseFlow = true;
options.EnableAdaptiveMeshRefinement = true;
options.EnableTimeVaryingBC = true;
options.EnableEnhancedHVAC = true;
options.EnableDualContinuumFractures = true;

// Configure multiphase
options.MultiphaseFluidType = MultiphaseFluidType.WaterCO2;
options.InitialSalinity = 0.035f; // 3.5% seawater

// Configure AMR
options.MaxRefinementLevel = 2;
options.TemperatureGradientThreshold = 5.0f; // K/m
options.RefinementInterval = 10; // Every 10 timesteps

// Configure time-varying BC
options.UseSeasonalVariation = true;
options.UseDailyVariation = true;
options.PeakHeatingLoad = 50000.0f; // W

// Configure HVAC
options.CarnotEfficiency = 0.45f; // 45% of Carnot
options.BuildingUAValue = 300.0f; // W/K

// Configure fractured media
options.FractureSpacing = 1.0f; // m
options.FractureDensity = 3.0f; // fractures/m
```

---

## Performance Notes

1. **Multiphase solver**: Uses OpenCL if `options.UseGPU = true`. Automatically falls back to CPU.

2. **AMR**: Only analyze every N timesteps (default: 10) to avoid overhead.

3. **Time-varying BC**: Very lightweight, no performance impact.

4. **HVAC**: Analytical calculations, negligible overhead.

5. **Fractured media**: Dual-continuum adds ~30% computational cost. Only enable if truly needed.

---

## Visualization Integration

The new result fields can be visualized using existing 3D viewers:

- **Saturation fields**: Display as 3D scalar fields (like temperature)
- **Refinement levels**: Display as colored zones (0=blue, 1=green, 2=yellow, 3=red)
- **COP history**: Plot as 2D time series chart
- **Matrix vs Fracture**: Display side-by-side or as difference field

Add visualization code in `GeothermalVisualization3D.cs` and `GeothermalSimulationTools.cs`.

---

## Testing Recommendations

1. **Test individually**: Enable one feature at a time to verify each works
2. **Start with time-varying BC + HVAC**: Easiest to verify (check COP plots)
3. **Add multiphase**: Verify saturations sum to 1.0
4. **Add AMR**: Check refinement near boreholes
5. **Add fractured media last**: Most complex interaction

---

## Common Issues and Solutions

**Issue**: Multiphase solver crashes
- **Solution**: Check pressure/temperature ranges. CO2 properties fail below 0°C or above 800°C.

**Issue**: AMR causes instability
- **Solution**: Reduce `MaxRefinementLevel` to 1 or increase `RefinementInterval` to 20.

**Issue**: COP values unrealistic (>10 or <1)
- **Solution**: Check temperature differences. Very small ΔT causes numerical issues.

**Issue**: Fractured media diverges
- **Solution**: Reduce timestep. Matrix-fracture transfer is stiff for large apertures.

---

## Questions?

For implementation assistance, refer to:
- Individual solver files for detailed API documentation
- OpenCL kernels in `MultiphaseCLSolver.cs` for GPU implementation
- Scientific references in each file header for physics validation
