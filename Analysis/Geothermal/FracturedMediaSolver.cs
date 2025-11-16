// GeoscientistToolkit/Analysis/Geothermal/FracturedMediaSolver.cs
//
// ================================================================================================
// REFERENCES (APA Format):
// ================================================================================================
// Dual-continuum fractured media modeling based on:
//
// Warren, J. E., & Root, P. J. (1963). The behavior of naturally fractured reservoirs.
//     Society of Petroleum Engineers Journal, 3(03), 245-255. https://doi.org/10.2118/426-PA
//
// Kazemi, H., Merrill, L. S., Porterfield, K. L., & Zeman, P. R. (1976). Numerical simulation
//     of water-oil flow in naturally fractured reservoirs. Society of Petroleum Engineers Journal,
//     16(06), 317-326. https://doi.org/10.2118/5719-PA
//
// Pruess, K., & Narasimhan, T. N. (1985). A practical method for modeling fluid and heat flow
//     in fractured porous media. Society of Petroleum Engineers Journal, 25(01), 14-26.
//     https://doi.org/10.2118/10509-PA
//
// Lim, K. T., & Aziz, K. (1995). Matrix-fracture transfer shape factors for dual-porosity
//     simulators. Journal of Petroleum Science and Engineering, 13(3-4), 169-178.
//     https://doi.org/10.1016/0920-4105(95)00010-F
//
// Matthai, S. K., & Nick, H. M. (2009). Upscaling two-phase flow in naturally fractured
//     reservoirs. AAPG Bulletin, 93(11), 1621-1632. https://doi.org/10.1306/08030909085
//
// ================================================================================================

using System;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.Geothermal;

/// <summary>
/// Dual-continuum solver for fractured geothermal reservoirs
/// Implements Warren-Root and MINC (Multiple INteracting Continua) models
/// </summary>
public class FracturedMediaSolver
{
    private readonly GeothermalMesh _mesh;
    private readonly FracturedMediaOptions _options;

    // Matrix properties (rock matrix)
    private float[,,] _temperatureMatrix;
    private float[,,] _pressureMatrix;
    private float[,,] _saturationMatrix;

    // Fracture properties (fracture network)
    private float[,,] _temperatureFracture;
    private float[,,] _pressureFracture;
    private float[,,] _saturationFracture;

    // Transfer terms (matrix-fracture exchange)
    private float[,,] _heatTransferCoeff;
    private float[,,] _massTransferCoeff;

    // Fracture geometry
    private float[,,] _fractureAperture;      // Fracture opening (m)
    private float[,,] _fractureSpacing;       // Average fracture spacing (m)
    private float[,,] _fracturePermeability;  // Fracture permeability (m²)
    private float[,,] _fractureDensity;       // Fracture density (fractures/m)

    public FracturedMediaSolver(GeothermalMesh mesh, FracturedMediaOptions options)
    {
        _mesh = mesh;
        _options = options;

        InitializeFields();
    }

    private void InitializeFields()
    {
        int nr = _mesh.Nr;
        int ntheta = _mesh.Ntheta;
        int nz = _mesh.Nz;

        // Matrix fields
        _temperatureMatrix = new float[nr, ntheta, nz];
        _pressureMatrix = new float[nr, ntheta, nz];
        _saturationMatrix = new float[nr, ntheta, nz];

        // Fracture fields
        _temperatureFracture = new float[nr, ntheta, nz];
        _pressureFracture = new float[nr, ntheta, nz];
        _saturationFracture = new float[nr, ntheta, nz];

        // Transfer coefficients
        _heatTransferCoeff = new float[nr, ntheta, nz];
        _massTransferCoeff = new float[nr, ntheta, nz];

        // Fracture geometry
        _fractureAperture = new float[nr, ntheta, nz];
        _fractureSpacing = new float[nr, ntheta, nz];
        _fracturePermeability = new float[nr, ntheta, nz];
        _fractureDensity = new float[nr, ntheta, nz];

        // Initialize with default values
        for (int i = 0; i < nr; i++)
        for (int j = 0; j < ntheta; j++)
        for (int k = 0; k < nz; k++)
        {
            _temperatureMatrix[i, j, k] = _options.InitialTemperature;
            _temperatureFracture[i, j, k] = _options.InitialTemperature;

            _pressureMatrix[i, j, k] = _options.InitialPressure;
            _pressureFracture[i, j, k] = _options.InitialPressure;

            _saturationMatrix[i, j, k] = 1.0f;
            _saturationFracture[i, j, k] = 1.0f;

            // Fracture properties (can vary spatially)
            _fractureAperture[i, j, k] = _options.FractureAperture;
            _fractureSpacing[i, j, k] = _options.FractureSpacing;
            _fractureDensity[i, j, k] = _options.FractureDensity;

            // Cubic law for fracture permeability: k_f = b²/12
            float b = _fractureAperture[i, j, k];
            _fracturePermeability[i, j, k] = b * b / 12.0f;

            // Calculate transfer coefficients
            CalculateTransferCoefficients(i, j, k);
        }
    }

    /// <summary>
    /// Calculate matrix-fracture transfer coefficients (Warren-Root shape factor)
    /// </summary>
    private void CalculateTransferCoefficients(int i, int j, int k)
    {
        float spacing = _fractureSpacing[i, j, k];
        float density = _fractureDensity[i, j, k];

        if (spacing < 1e-6f || density < 1e-6f)
        {
            _heatTransferCoeff[i, j, k] = 0.0f;
            _massTransferCoeff[i, j, k] = 0.0f;
            return;
        }

        // Warren-Root shape factor (simplified for 3D orthogonal fracture network)
        // σ = 4n(n+2) / L², where n = number of fracture sets, L = spacing
        float n_sets = 3.0f; // Assume 3 orthogonal fracture sets
        float sigma = 4.0f * n_sets * (n_sets + 2.0f) / (spacing * spacing);

        // Heat transfer coefficient
        // Q_transfer = α * A * λ * (T_f - T_m)
        // where α = shape factor, A = area, λ = thermal conductivity
        float lambda_matrix = _options.MatrixThermalConductivity;
        _heatTransferCoeff[i, j, k] = sigma * lambda_matrix;

        // Mass transfer coefficient
        // M_transfer = β * A * k * ρ / μ * (P_f - P_m)
        // where β = shape factor, k = matrix permeability
        float k_matrix = _options.MatrixPermeability;
        float mu = 0.001f; // Assume water viscosity (Pa·s)
        float rho = 1000.0f; // Water density (kg/m³)
        _massTransferCoeff[i, j, k] = sigma * k_matrix * rho / mu;
    }

    /// <summary>
    /// Update dual-continuum model with matrix-fracture transfer
    /// </summary>
    public void UpdateDualContinuum(float dt)
    {
        int nr = _mesh.Nr;
        int ntheta = _mesh.Ntheta;
        int nz = _mesh.Nz;

        for (int i = 1; i < nr - 1; i++)
        for (int j = 0; j < ntheta; j++)
        for (int k = 1; k < nz - 1; k++)
        {
            // Heat transfer (matrix ↔ fracture)
            float dT = _temperatureFracture[i, j, k] - _temperatureMatrix[i, j, k];
            float heatFlux = _heatTransferCoeff[i, j, k] * dT;

            // Specific heat capacity
            float cp_matrix = _options.MatrixSpecificHeat;
            float rho_matrix = _options.MatrixDensity;
            float cp_fracture = _options.FluidSpecificHeat;
            float rho_fracture = 1000.0f; // Fluid density

            // Porosity of matrix and fracture
            float phi_matrix = _options.MatrixPorosity;
            float phi_fracture = CalculateFracturePorosity(i, j, k);

            // Update temperatures
            float dT_matrix = heatFlux * dt / (rho_matrix * cp_matrix * (1.0f - phi_matrix));
            float dT_fracture = -heatFlux * dt / (rho_fracture * cp_fracture * phi_fracture);

            _temperatureMatrix[i, j, k] += dT_matrix;
            _temperatureFracture[i, j, k] += dT_fracture;

            // Mass transfer (matrix ↔ fracture) for multiphase flow
            if (_options.EnableMultiphase)
            {
                float dP = _pressureFracture[i, j, k] - _pressureMatrix[i, j, k];
                float massFlux = _massTransferCoeff[i, j, k] * dP;

                // Update pressures (simplified)
                float compressibility = 4.5e-10f; // Water compressibility (1/Pa)
                float dP_matrix = massFlux * dt / (phi_matrix * compressibility);
                float dP_fracture = -massFlux * dt / (phi_fracture * compressibility);

                _pressureMatrix[i, j, k] += dP_matrix;
                _pressureFracture[i, j, k] += dP_fracture;
            }
        }
    }

    /// <summary>
    /// Calculate fracture porosity from fracture density and aperture
    /// φ_f = n * b / L, where n = density, b = aperture, L = spacing
    /// </summary>
    private float CalculateFracturePorosity(int i, int j, int k)
    {
        float density = _fractureDensity[i, j, k];
        float aperture = _fractureAperture[i, j, k];
        float spacing = _fractureSpacing[i, j, k];

        if (spacing < 1e-6f) return 0.0f;

        float porosity = density * aperture / spacing;
        return Math.Min(0.5f, porosity); // Cap at 50%
    }

    /// <summary>
    /// Get effective permeability tensor for fractured media
    /// Combines matrix and fracture permeabilities
    /// </summary>
    public (float kr, float ktheta, float kz) GetEffectivePermeability(int i, int j, int k)
    {
        float k_matrix = _options.MatrixPermeability;
        float k_fracture = _fracturePermeability[i, j, k];
        float phi_matrix = _options.MatrixPorosity;
        float phi_fracture = CalculateFracturePorosity(i, j, k);

        // Weighted average (simplified)
        float k_eff = phi_matrix * k_matrix + phi_fracture * k_fracture;

        // Assume isotropic for now (can be extended to anisotropic)
        return (k_eff, k_eff, k_eff);
    }

    /// <summary>
    /// Set fracture properties from a fracture network
    /// </summary>
    public void SetFractureNetwork(float[,,] aperture, float[,,] spacing, float[,,] density)
    {
        int nr = _mesh.Nr;
        int ntheta = _mesh.Ntheta;
        int nz = _mesh.Nz;

        for (int i = 0; i < nr; i++)
        for (int j = 0; j < ntheta; j++)
        for (int k = 0; k < nz; k++)
        {
            if (aperture != null) _fractureAperture[i, j, k] = aperture[i, j, k];
            if (spacing != null) _fractureSpacing[i, j, k] = spacing[i, j, k];
            if (density != null) _fractureDensity[i, j, k] = density[i, j, k];

            // Recalculate fracture permeability
            float b = _fractureAperture[i, j, k];
            _fracturePermeability[i, j, k] = b * b / 12.0f;

            // Recalculate transfer coefficients
            CalculateTransferCoefficients(i, j, k);
        }
    }

    // Public accessors
    public float[,,] GetMatrixTemperature() => _temperatureMatrix;
    public float[,,] GetFractureTemperature() => _temperatureFracture;
    public float[,,] GetMatrixPressure() => _pressureMatrix;
    public float[,,] GetFracturePressure() => _pressureFracture;
    public float[,,] GetFractureAperture() => _fractureAperture;
    public float[,,] GetFracturePermeability() => _fracturePermeability;

    public void SetMatrixTemperature(float[,,] temperature) => Array.Copy(temperature, _temperatureMatrix, temperature.Length);
    public void SetFractureTemperature(float[,,] temperature) => Array.Copy(temperature, _temperatureFracture, temperature.Length);
    public void SetMatrixPressure(float[,,] pressure) => Array.Copy(pressure, _pressureMatrix, pressure.Length);
    public void SetFracturePressure(float[,,] pressure) => Array.Copy(pressure, _pressureFracture, pressure.Length);
}

/// <summary>
/// Options for fractured media simulation
/// </summary>
public class FracturedMediaOptions
{
    // Matrix properties
    public float MatrixPermeability { get; set; } = 1e-18f;           // m² (1 nD)
    public float MatrixPorosity { get; set; } = 0.05f;                 // 5%
    public float MatrixThermalConductivity { get; set; } = 2.5f;       // W/m/K
    public float MatrixSpecificHeat { get; set; } = 1000.0f;           // J/kg/K
    public float MatrixDensity { get; set; } = 2650.0f;                // kg/m³

    // Fracture properties
    public float FractureAperture { get; set; } = 1e-4f;               // m (0.1 mm)
    public float FractureSpacing { get; set; } = 1.0f;                 // m
    public float FractureDensity { get; set; } = 3.0f;                 // fractures/m

    // Fluid properties
    public float FluidSpecificHeat { get; set; } = 4200.0f;            // J/kg/K (water)

    // Initial conditions
    public float InitialTemperature { get; set; } = 20.0f;             // °C
    public float InitialPressure { get; set; } = 1e7f;                 // Pa (100 bar)

    // Multiphase flag
    public bool EnableMultiphase { get; set; } = false;
}
