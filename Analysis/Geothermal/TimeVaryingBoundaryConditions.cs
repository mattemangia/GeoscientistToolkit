// GeoscientistToolkit/Analysis/Geothermal/TimeVaryingBoundaryConditions.cs
//
// ================================================================================================
// Time-varying boundary conditions for realistic geothermal simulations
// Supports seasonal variations, load profiles, and dynamic injection/extraction
// ================================================================================================

using System;
using System.Collections.Generic;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.Geothermal;

/// <summary>
/// Manages time-varying boundary conditions for geothermal simulations
/// </summary>
public class TimeVaryingBoundaryConditions
{
    private readonly TimeVaryingBCOptions _options;

    // Time series data
    private List<(double time, float value)> _temperatureSeries;
    private List<(double time, float value)> _flowRateSeries;
    private List<(double time, float value)> _loadDemandSeries;

    // Current state
    private double _currentTime;

    public TimeVaryingBoundaryConditions(TimeVaryingBCOptions options)
    {
        _options = options;

        _temperatureSeries = new List<(double, float)>();
        _flowRateSeries = new List<(double, float)>();
        _loadDemandSeries = new List<(double, float)>();

        InitializeDefaultProfiles();
    }

    private void InitializeDefaultProfiles()
    {
        if (_options.UseSeasonalVariation)
        {
            GenerateSeasonalProfile();
        }

        if (_options.UseDailyVariation)
        {
            GenerateDailyLoadProfile();
        }
    }

    /// <summary>
    /// Generate seasonal temperature and load variation (annual cycle)
    /// </summary>
    private void GenerateSeasonalProfile()
    {
        const int daysPerYear = 365;
        const double secondsPerDay = 86400.0;

        for (int day = 0; day <= daysPerYear; day++)
        {
            double time = day * secondsPerDay;

            // Sinusoidal variation for outdoor temperature
            // Peak in summer (day 182 ≈ July 1), minimum in winter (day 1 ≈ Jan 1)
            float dayOfYear = day;
            float T_outdoor = _options.AverageOutdoorTemp +
                _options.SeasonalTempAmplitude * (float)Math.Sin(2.0 * Math.PI * (dayOfYear - 90.0) / 365.0);

            _temperatureSeries.Add((time, T_outdoor));

            // Heating load (high in winter, low in summer)
            float heatingLoad = _options.PeakHeatingLoad * Math.Max(0.0f,
                1.0f - (T_outdoor - _options.HeatingBaseTemp) / 20.0f);

            // Cooling load (high in summer, low in winter)
            float coolingLoad = _options.PeakCoolingLoad * Math.Max(0.0f,
                (T_outdoor - _options.CoolingBaseTemp) / 15.0f);

            // Total load
            float totalLoad = heatingLoad + coolingLoad;
            _loadDemandSeries.Add((time, totalLoad));
        }

        Logger.Log($"Generated seasonal profile: {daysPerYear} days, " +
            $"Temp range: {_options.AverageOutdoorTemp - _options.SeasonalTempAmplitude:F1}°C to " +
            $"{_options.AverageOutdoorTemp + _options.SeasonalTempAmplitude:F1}°C");
    }

    /// <summary>
    /// Generate daily load variation profile (24-hour cycle)
    /// </summary>
    private void GenerateDailyLoadProfile()
    {
        const int hoursPerDay = 24;
        const double secondsPerHour = 3600.0;

        // Typical daily load profile (commercial building)
        float[] hourlyMultipliers = new float[24]
        {
            0.3f, 0.3f, 0.3f, 0.3f, 0.3f, 0.4f,  // 0-5 AM: Low
            0.6f, 0.8f, 0.9f, 1.0f, 1.0f, 1.0f,  // 6-11 AM: Ramp up
            0.9f, 0.9f, 1.0f, 1.0f, 0.9f, 0.8f,  // 12-5 PM: Peak
            0.7f, 0.6f, 0.5f, 0.4f, 0.4f, 0.3f   // 6-11 PM: Ramp down
        };

        for (int hour = 0; hour < hoursPerDay; hour++)
        {
            double time = hour * secondsPerHour;
            float loadMultiplier = hourlyMultipliers[hour];

            // Note: This is a relative multiplier, will be applied to seasonal load
            _flowRateSeries.Add((time, loadMultiplier));
        }
    }

    /// <summary>
    /// Get inlet temperature at current simulation time
    /// </summary>
    public float GetInletTemperature(double currentTime)
    {
        _currentTime = currentTime;

        if (!_options.UseTimeVaryingInletTemp)
            return _options.BaseInletTemperature;

        // Interpolate from time series
        return InterpolateTimeSeries(_temperatureSeries, currentTime, _options.BaseInletTemperature);
    }

    /// <summary>
    /// Get mass flow rate at current simulation time
    /// </summary>
    public float GetMassFlowRate(double currentTime)
    {
        _currentTime = currentTime;

        if (!_options.UseTimeVaryingFlowRate)
            return _options.BaseMassFlowRate;

        // Get seasonal load demand
        float loadDemand = InterpolateTimeSeries(_loadDemandSeries, currentTime, _options.PeakHeatingLoad);

        // Get daily variation multiplier
        double timeOfDay = currentTime % 86400.0; // Seconds in a day
        float dailyMultiplier = InterpolateTimeSeries(_flowRateSeries, timeOfDay, 1.0f);

        // Combine seasonal and daily variations
        float flowRate = _options.BaseMassFlowRate * (loadDemand / _options.PeakHeatingLoad) * dailyMultiplier;

        // Apply min/max limits
        flowRate = Math.Max(_options.MinFlowRate, Math.Min(_options.MaxFlowRate, flowRate));

        return flowRate;
    }

    /// <summary>
    /// Get heat extraction/injection rate at current simulation time
    /// </summary>
    public float GetHeatExtractionRate(double currentTime)
    {
        _currentTime = currentTime;

        float loadDemand = InterpolateTimeSeries(_loadDemandSeries, currentTime, 0.0f);

        // Convert load demand to heat extraction rate
        // Q_extraction = Load / COP
        float COP = _options.EstimatedCOP;
        float heatExtraction = loadDemand / COP;

        return heatExtraction;
    }

    /// <summary>
    /// Linear interpolation of time series data
    /// </summary>
    private float InterpolateTimeSeries(List<(double time, float value)> series, double currentTime, float defaultValue)
    {
        if (series == null || series.Count == 0)
            return defaultValue;

        // Handle periodic boundary conditions (annual/daily cycles)
        double period = 0.0;
        if (series.Count > 1)
        {
            period = series[series.Count - 1].time;
            if (period > 0.0)
            {
                currentTime = currentTime % period;
            }
        }

        // Find surrounding data points
        for (int i = 0; i < series.Count - 1; i++)
        {
            if (currentTime >= series[i].time && currentTime <= series[i + 1].time)
            {
                // Linear interpolation
                double t1 = series[i].time;
                double t2 = series[i + 1].time;
                float v1 = series[i].value;
                float v2 = series[i + 1].value;

                if (Math.Abs(t2 - t1) < 1e-9)
                    return v1;

                float alpha = (float)((currentTime - t1) / (t2 - t1));
                return v1 + alpha * (v2 - v1);
            }
        }

        // If time is beyond series, return last value
        return series.Count > 0 ? series[series.Count - 1].value : defaultValue;
    }

    /// <summary>
    /// Add custom time series data
    /// </summary>
    public void AddTemperatureData(double time, float temperature)
    {
        _temperatureSeries.Add((time, temperature));
        _temperatureSeries.Sort((a, b) => a.time.CompareTo(b.time));
    }

    public void AddFlowRateData(double time, float flowRate)
    {
        _flowRateSeries.Add((time, flowRate));
        _flowRateSeries.Sort((a, b) => a.time.CompareTo(b.time));
    }

    public void AddLoadDemandData(double time, float load)
    {
        _loadDemandSeries.Add((time, load));
        _loadDemandSeries.Sort((a, b) => a.time.CompareTo(b.time));
    }

    /// <summary>
    /// Clear all time series data
    /// </summary>
    public void ClearAllData()
    {
        _temperatureSeries.Clear();
        _flowRateSeries.Clear();
        _loadDemandSeries.Clear();
    }

    /// <summary>
    /// Get current outdoor temperature (for display/analysis)
    /// </summary>
    public float GetOutdoorTemperature(double currentTime)
    {
        return InterpolateTimeSeries(_temperatureSeries, currentTime, _options.AverageOutdoorTemp);
    }

    /// <summary>
    /// Get current load demand (for display/analysis)
    /// </summary>
    public float GetLoadDemand(double currentTime)
    {
        return InterpolateTimeSeries(_loadDemandSeries, currentTime, 0.0f);
    }
}

/// <summary>
/// Options for time-varying boundary conditions
/// </summary>
public class TimeVaryingBCOptions
{
    // Enable/disable features
    public bool UseTimeVaryingInletTemp { get; set; } = true;
    public bool UseTimeVaryingFlowRate { get; set; } = true;
    public bool UseSeasonalVariation { get; set; } = true;
    public bool UseDailyVariation { get; set; } = true;

    // Base values (used when time-varying is disabled)
    public float BaseInletTemperature { get; set; } = 15.0f;    // °C
    public float BaseMassFlowRate { get; set; } = 1.0f;         // kg/s

    // Seasonal variation parameters
    public float AverageOutdoorTemp { get; set; } = 10.0f;      // °C
    public float SeasonalTempAmplitude { get; set; } = 15.0f;   // °C (±15°C variation)

    // Load parameters
    public float PeakHeatingLoad { get; set; } = 50000.0f;      // W (50 kW)
    public float PeakCoolingLoad { get; set; } = 30000.0f;      // W (30 kW)
    public float HeatingBaseTemp { get; set; } = 15.0f;         // °C (heating below this)
    public float CoolingBaseTemp { get; set; } = 20.0f;         // °C (cooling above this)

    // Flow rate limits
    public float MinFlowRate { get; set; } = 0.1f;              // kg/s
    public float MaxFlowRate { get; set; } = 5.0f;              // kg/s

    // Performance
    public float EstimatedCOP { get; set; } = 4.0f;             // Coefficient of Performance
}
