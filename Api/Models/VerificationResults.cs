namespace GeoscientistToolkit.Api;

/// <summary>
///     Result from the geomechanics triaxial compression verification case.
/// </summary>
public record GeomechanicsTriaxialResult(
    float ExpectedPeakStrengthMPa,
    float SimulatedPeakStrengthMPa,
    float ErrorPercent,
    bool Passed);

/// <summary>
///     Result from the seismic PREM wave propagation verification case.
/// </summary>
public record SeismicPremResult(
    double ExpectedArrivalSeconds,
    double SimulatedArrivalSeconds,
    double ErrorPercent,
    bool Passed);

/// <summary>
///     Result from the slope stability gravity drop verification case.
/// </summary>
public record SlopeGravityDropResult(
    float ExpectedDropMeters,
    float SimulatedDropMeters,
    float ErrorMeters,
    bool Passed);

/// <summary>
///     Result from the slope stability sliding block verification case.
/// </summary>
public record SlopeSlidingResult(
    float ExpectedDistanceMeters,
    float SimulatedDistanceMeters,
    float ErrorMeters,
    bool Passed);

/// <summary>
///     Result from the thermodynamic water saturation pressure verification case.
/// </summary>
public record WaterSaturationResult(
    double ExpectedPressurePa,
    double SimulatedPressurePa,
    double ErrorPa,
    bool Passed);

/// <summary>
///     Result from the PNM permeability verification case.
/// </summary>
public record PnmPermeabilityResult(
    float SimulatedDarcyMilliDarcy,
    bool FlowDetected);

/// <summary>
///     Result from the acoustic speed verification case.
/// </summary>
public record AcousticSpeedResult(
    float ExpectedVelocityMetersPerSecond,
    float SimulatedVelocityMetersPerSecond,
    float ErrorMetersPerSecond,
    bool Passed);

/// <summary>
///     Result from the heat transfer verification case.
/// </summary>
public record HeatTransferResult(
    float TemperatureAtX1C,
    float TemperatureAtX5C,
    bool Passed);

/// <summary>
///     Result from the hydrology flow accumulation verification case.
/// </summary>
public record HydrologyFlowResult(
    int CenterAccumulation,
    bool Passed);

/// <summary>
///     Result from the geothermal borehole verification case.
/// </summary>
public record GeothermalBoreholeResult(
    double OutletTemperatureC,
    bool Passed);

/// <summary>
///     Result from the deep geothermal coaxial verification case.
/// </summary>
public record GeothermalCoaxialResult(
    double OutletTemperatureC,
    bool Passed);
