// GeoscientistToolkit/Analysis/Geothermal/EnhancedHVACCalculator.cs
//
// ================================================================================================
// REFERENCES (APA Format):
// ================================================================================================
// Enhanced HVAC and heat pump performance calculations based on:
//
// ASHRAE. (2020). ASHRAE Handbook—HVAC Systems and Equipment. American Society of Heating,
//     Refrigerating and Air-Conditioning Engineers.
//
// Klein, S. A., et al. (2017). EES: Engineering Equation Solver. F-Chart Software.
//
// Kavanaugh, S. P., & Rafferty, K. (2014). Geothermal Heating and Cooling: Design of
//     Ground-Source Heat Pump Systems. ASHRAE.
//
// Yang, H., Cui, P., & Fang, Z. (2010). Vertical-borehole ground-coupled heat pumps: A review
//     of models and systems. Applied Energy, 87(1), 16-27.
//     https://doi.org/10.1016/j.apenergy.2009.04.038
//
// Self, S. J., Reddy, B. V., & Rosen, M. A. (2013). Geothermal heat pump systems: Status review
//     and comparison with other heating options. Applied Energy, 101, 341-348.
//     https://doi.org/10.1016/j.apenergy.2012.01.048
//
// ================================================================================================

using System;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.Geothermal;

/// <summary>
/// Enhanced HVAC and heat pump performance calculator
/// Includes realistic COP models, part-load performance, and seasonal efficiency
/// </summary>
public class EnhancedHVACCalculator
{
    private readonly HVACOptions _options;

    // Performance tracking
    private double _totalHeatDelivered;
    private double _totalWorkInput;
    private double _totalHeatingHours;
    private double _totalCoolingHours;

    public EnhancedHVACCalculator(HVACOptions options)
    {
        _options = options;
        Reset();
    }

    public void Reset()
    {
        _totalHeatDelivered = 0.0;
        _totalWorkInput = 0.0;
        _totalHeatingHours = 0.0;
        _totalCoolingHours = 0.0;
    }

    /// <summary>
    /// Calculate instantaneous COP for heating mode using Carnot efficiency correlation
    /// </summary>
    public float CalculateHeatingCOP(float sourceTemp, float sinkTemp, float partLoadRatio)
    {
        // Carnot COP: COP_Carnot = T_sink / (T_sink - T_source)
        float T_source_K = sourceTemp + 273.15f;
        float T_sink_K = sinkTemp + 273.15f;

        if (T_sink_K <= T_source_K)
        {
            Logger.LogWarning($"Invalid temperatures for heating: source={sourceTemp}°C, sink={sinkTemp}°C");
            return 1.0f;
        }

        float COP_Carnot = T_sink_K / (T_sink_K - T_source_K);

        // Actual COP is a fraction of Carnot efficiency
        // Typical heat pumps achieve 40-50% of Carnot COP
        float eta_carnot = _options.CarnotEfficiency;
        float COP_ideal = COP_Carnot * eta_carnot;

        // Apply part-load degradation
        float COP = COP_ideal * GetPartLoadFactor(partLoadRatio);

        // Apply temperature-dependent correction
        COP *= GetTemperatureCorrectionFactor(sourceTemp, sinkTemp, true);

        // Clamp to reasonable range
        return Math.Max(1.0f, Math.Min(_options.MaxCOP, COP));
    }

    /// <summary>
    /// Calculate instantaneous COP for cooling mode
    /// </summary>
    public float CalculateCoolingCOP(float sourceTemp, float sinkTemp, float partLoadRatio)
    {
        // For cooling (EER): COP_cooling = T_source / (T_sink - T_source)
        float T_source_K = sourceTemp + 273.15f;
        float T_sink_K = sinkTemp + 273.15f;

        if (T_sink_K <= T_source_K)
        {
            Logger.LogWarning($"Invalid temperatures for cooling: source={sourceTemp}°C, sink={sinkTemp}°C");
            return 1.0f;
        }

        float EER_Carnot = T_source_K / (T_sink_K - T_source_K);

        float eta_carnot = _options.CarnotEfficiency * 0.9f; // Cooling typically less efficient
        float EER_ideal = EER_Carnot * eta_carnot;

        // Apply part-load degradation
        float EER = EER_ideal * GetPartLoadFactor(partLoadRatio);

        // Apply temperature-dependent correction
        EER *= GetTemperatureCorrectionFactor(sourceTemp, sinkTemp, false);

        return Math.Max(1.0f, Math.Min(_options.MaxCOP, EER));
    }

    /// <summary>
    /// Part-load performance factor (accounts for cycling losses)
    /// Based on ASHRAE part-load curve
    /// </summary>
    private float GetPartLoadFactor(float partLoadRatio)
    {
        // PLF = 1 - Cd * (1 - PLR)
        // where Cd = degradation coefficient (typically 0.15-0.25)
        float PLR = Math.Max(0.0f, Math.Min(1.0f, partLoadRatio));

        if (PLR < _options.MinimumPLR)
        {
            // Below minimum PLR, unit cycles on/off with significant degradation
            PLR = _options.MinimumPLR;
        }

        float Cd = _options.CyclingDegradationCoeff;
        float PLF = 1.0f - Cd * (1.0f - PLR);

        return Math.Max(0.5f, PLF); // Minimum 50% efficiency
    }

    /// <summary>
    /// Temperature-dependent correction factor
    /// Accounts for compressor efficiency variation with operating conditions
    /// </summary>
    private float GetTemperatureCorrectionFactor(float sourceTemp, float sinkTemp, bool heatingMode)
    {
        // Calculate temperature lift
        float deltaT = Math.Abs(sinkTemp - sourceTemp);

        // Reference conditions
        float deltaT_ref = heatingMode ? 30.0f : 25.0f; // °C

        // Correction factor decreases with larger temperature lift
        // Using quadratic relationship
        float ratio = deltaT / deltaT_ref;
        float correctionFactor = 1.0f - _options.TempCorrectionCoeff * (ratio - 1.0f) * (ratio - 1.0f);

        return Math.Max(0.7f, Math.Min(1.2f, correctionFactor));
    }

    /// <summary>
    /// Calculate required compressor power
    /// </summary>
    public float CalculateCompressorPower(float heatLoad, float COP)
    {
        if (COP <= 0.0f) return 0.0f;

        // W_comp = Q_load / COP
        float power = heatLoad / COP;

        // Add auxiliary power (pumps, fans, controls)
        power += _options.AuxiliaryPower;

        return power;
    }

    /// <summary>
    /// Calculate entering water temperature (EWT) to heat pump
    /// </summary>
    public float CalculateEnteringWaterTemp(float outletTemp, float heatExtracted, float massFlowRate)
    {
        if (massFlowRate <= 0.0f) return outletTemp;

        // Q = m * cp * (T_in - T_out)
        // T_in = T_out + Q / (m * cp)
        float cp = 4186.0f; // J/kg/K for water
        float deltaT = heatExtracted / (massFlowRate * cp);

        return outletTemp + deltaT;
    }

    /// <summary>
    /// Update seasonal performance metrics
    /// </summary>
    public void UpdateSeasonalMetrics(float heatDelivered, float workInput, float dt, bool heatingMode)
    {
        _totalHeatDelivered += heatDelivered * dt;
        _totalWorkInput += workInput * dt;

        if (heatingMode)
            _totalHeatingHours += dt / 3600.0;
        else
            _totalCoolingHours += dt / 3600.0;
    }

    /// <summary>
    /// Calculate Seasonal Performance Factor (SPF) - European standard
    /// </summary>
    public float CalculateSPF()
    {
        if (_totalWorkInput <= 0.0)
            return 0.0f;

        // SPF = Total heat delivered / Total work input (including auxiliaries)
        float SPF = (float)(_totalHeatDelivered / _totalWorkInput);

        return SPF;
    }

    /// <summary>
    /// Calculate Seasonal Coefficient of Performance (SCOP) - heating only
    /// </summary>
    public float CalculateSCOP()
    {
        if (_totalWorkInput <= 0.0 || _totalHeatingHours <= 0.0)
            return 0.0f;

        // SCOP considers heating season only
        float SCOP = (float)(_totalHeatDelivered / _totalWorkInput);

        return SCOP;
    }

    /// <summary>
    /// Calculate Seasonal Energy Efficiency Ratio (SEER) - cooling only
    /// </summary>
    public float CalculateSEER()
    {
        if (_totalWorkInput <= 0.0 || _totalCoolingHours <= 0.0)
            return 0.0f;

        // SEER in BTU/Wh (US standard)
        double heat_BTU = _totalHeatDelivered * 3.412; // J to BTU
        double work_Wh = _totalWorkInput / 3600.0;     // J to Wh

        float SEER = (float)(heat_BTU / work_Wh);

        return SEER;
    }

    /// <summary>
    /// Calculate building heat loss coefficient
    /// </summary>
    public float CalculateBuildingHeatLoss(float indoorTemp, float outdoorTemp)
    {
        // Q_loss = UA * (T_indoor - T_outdoor)
        float deltaT = indoorTemp - outdoorTemp;
        float heatLoss = _options.BuildingUAValue * deltaT;

        return Math.Max(0.0f, heatLoss);
    }

    /// <summary>
    /// Calculate required supply temperature for building
    /// </summary>
    public float CalculateSupplyTemperature(float outdoorTemp)
    {
        // Weather compensation curve
        // T_supply = T_base + k * (T_base - T_outdoor)
        float T_base = _options.DesignIndoorTemp;
        float T_outdoor = outdoorTemp;

        if (T_outdoor >= _options.HeatingCutoffTemp)
        {
            // No heating needed
            return T_base;
        }

        // Linear compensation
        float k = _options.WeatherCompensationSlope;
        float T_supply = _options.DesignSupplyTemp + k * (T_base - T_outdoor);

        // Clamp to design limits
        return Math.Max(_options.MinSupplyTemp, Math.Min(_options.MaxSupplyTemp, T_supply));
    }

    /// <summary>
    /// Calculate defrost energy penalty (for cold climate operation)
    /// </summary>
    public float CalculateDefrostPenalty(float outdoorTemp, float humidity)
    {
        // Defrost is needed when outdoor temp is between -10°C and 7°C with high humidity
        if (outdoorTemp < -10.0f || outdoorTemp > 7.0f)
            return 0.0f;

        if (humidity < 0.6f) // 60% RH threshold
            return 0.0f;

        // Defrost cycles reduce effective COP by 5-15%
        float penaltyFactor = 0.05f + 0.10f * (humidity - 0.6f) / 0.4f;

        return penaltyFactor;
    }

    public HVACPerformanceMetrics GetPerformanceMetrics()
    {
        return new HVACPerformanceMetrics
        {
            TotalHeatDelivered = _totalHeatDelivered,
            TotalWorkInput = _totalWorkInput,
            TotalHeatingHours = _totalHeatingHours,
            TotalCoolingHours = _totalCoolingHours,
            SPF = CalculateSPF(),
            SCOP = CalculateSCOP(),
            SEER = CalculateSEER()
        };
    }
}

/// <summary>
/// HVAC system options
/// </summary>
public class HVACOptions
{
    // Heat pump performance
    public float CarnotEfficiency { get; set; } = 0.45f;           // 45% of Carnot
    public float MaxCOP { get; set; } = 6.0f;                       // Maximum COP
    public float CyclingDegradationCoeff { get; set; } = 0.20f;    // Part-load degradation
    public float MinimumPLR { get; set; } = 0.25f;                  // Minimum part-load ratio
    public float TempCorrectionCoeff { get; set; } = 0.15f;         // Temperature correction

    // Auxiliary systems
    public float AuxiliaryPower { get; set; } = 500.0f;             // W (pumps, fans)

    // Building parameters
    public float BuildingUAValue { get; set; } = 300.0f;            // W/K (heat loss coefficient)
    public float DesignIndoorTemp { get; set; } = 20.0f;            // °C
    public float DesignSupplyTemp { get; set; } = 35.0f;            // °C (for radiant floor)
    public float MinSupplyTemp { get; set; } = 25.0f;               // °C
    public float MaxSupplyTemp { get; set; } = 55.0f;               // °C
    public float HeatingCutoffTemp { get; set; } = 15.0f;           // °C (no heating above this)

    // Weather compensation
    public float WeatherCompensationSlope { get; set; } = 1.5f;     // Supply temp slope
}

/// <summary>
/// HVAC performance metrics
/// </summary>
public class HVACPerformanceMetrics
{
    public double TotalHeatDelivered { get; set; }    // J
    public double TotalWorkInput { get; set; }        // J
    public double TotalHeatingHours { get; set; }     // hours
    public double TotalCoolingHours { get; set; }     // hours
    public float SPF { get; set; }                     // Seasonal Performance Factor
    public float SCOP { get; set; }                    // Seasonal COP (heating)
    public float SEER { get; set; }                    // Seasonal EER (cooling)
}
