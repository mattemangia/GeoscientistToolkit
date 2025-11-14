using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Numerics;

namespace GeoscientistToolkit.Analysis.Seismology
{
    /// <summary>
    /// Fracture type classification
    /// </summary>
    public enum FractureType
    {
        Tensile,        // Mode I - opening
        ShearMode2,     // Mode II - in-plane shear
        ShearMode3,     // Mode III - out-of-plane shear
        Mixed           // Combination
    }

    /// <summary>
    /// Fracture state at a location
    /// </summary>
    public struct FractureState
    {
        public double X, Y, Z;                    // Position
        public double Latitude, Longitude, Depth; // Geographic coordinates
        public bool IsFractured;                  // Is fracture initiated?
        public FractureType Type;                 // Fracture mode
        public double StressIntensityFactor;      // K_I, K_II, or K_III (MPa·√m)
        public double FractureToughness;          // Critical SIF (MPa·√m)
        public double CoulombStress;              // Coulomb failure stress (MPa)
        public double FrictionCoefficient;        // Static friction
        public double NormalStress;               // Normal stress on fault (MPa)
        public double ShearStress;                // Shear stress on fault (MPa)
        public double ApertureChange;             // Change in fracture opening (μm)
        public Vector3 FractureOrientation;       // Normal to fracture plane
    }

    /// <summary>
    /// Analyzes stress-induced fracturing and fault reactivation
    /// Based on linear elastic fracture mechanics (LEFM) and Coulomb failure criterion
    /// </summary>
    public class FractureAnalyzer
    {
        private readonly int _nx, _ny, _nz;
        private readonly double _dx, _dy, _dz;

        // Stress tensor components (MPa)
        private double[,,] _sigmaXX, _sigmaYY, _sigmaZZ;
        private double[,,] _sigmaXY, _sigmaXZ, _sigmaYZ;

        // Material properties
        private double[,,] _youngsModulus;  // GPa
        private double[,,] _poissonsRatio;
        private double[,,] _fractureToughness; // MPa·√m

        // Fracture state
        private bool[,,] _isFractured;
        private double[,,] _fractureAperture; // μm

        public FractureAnalyzer(int nx, int ny, int nz, double dx, double dy, double dz)
        {
            _nx = nx;
            _ny = ny;
            _nz = nz;
            _dx = dx;
            _dy = dy;
            _dz = dz;

            // Initialize arrays
            _sigmaXX = new double[nx, ny, nz];
            _sigmaYY = new double[nx, ny, nz];
            _sigmaZZ = new double[nx, ny, nz];
            _sigmaXY = new double[nx, ny, nz];
            _sigmaXZ = new double[nx, ny, nz];
            _sigmaYZ = new double[nx, ny, nz];

            _youngsModulus = new double[nx, ny, nz];
            _poissonsRatio = new double[nx, ny, nz];
            _fractureToughness = new double[nx, ny, nz];

            _isFractured = new bool[nx, ny, nz];
            _fractureAperture = new double[nx, ny, nz];
        }

        /// <summary>
        /// Initialize material properties from crustal model
        /// </summary>
        public void InitializeMaterialProperties(CrustalModel crustalModel,
            double minLat, double maxLat, double minLon, double maxLon)
        {
            Parallel.For(0, _nx, i =>
            {
                double lat = minLat + (i / (double)(_nx - 1)) * (maxLat - minLat);

                for (int j = 0; j < _ny; j++)
                {
                    double lon = minLon + (j / (double)(_ny - 1)) * (maxLon - minLon);

                    for (int k = 0; k < _nz; k++)
                    {
                        double depth = k * _dz;

                        var crustalType = crustalModel.GetCrustalType(lat, lon);
                        var (_, layer, _) = crustalType.GetLayerAtDepth(depth);

                        // Calculate elastic moduli
                        double mu = layer.GetMu();  // GPa
                        double nu = layer.GetPoissonsRatio();
                        double E = 2.0 * mu * (1.0 + nu);

                        _youngsModulus[i, j, k] = E;
                        _poissonsRatio[i, j, k] = nu;

                        // Fracture toughness depends on rock type
                        // Typical values: sediments 0.1-0.5, crystalline 1-3 MPa·√m
                        _fractureToughness[i, j, k] = EstimateFractureToughness(layer);
                    }
                }
            });
        }

        /// <summary>
        /// Estimate fracture toughness from seismic velocities
        /// </summary>
        private double EstimateFractureToughness(CrustalLayer layer)
        {
            // Empirical correlation with P-wave velocity
            // Higher velocity = stronger, more crystalline = higher toughness
            double vp = layer.VpKmPerS;

            if (vp < 3.0) return 0.2;        // Soft sediments
            else if (vp < 4.5) return 0.5;   // Hard sediments
            else if (vp < 6.0) return 1.0;   // Upper crust
            else if (vp < 7.0) return 2.0;   // Middle/lower crust
            else return 2.5;                 // Crystalline basement/mantle
        }

        /// <summary>
        /// Update stress field from wave propagation
        /// </summary>
        public void UpdateStressFromWaves(WavePropagationEngine waveEngine, CrustalModel crustalModel,
            double minLat, double maxLat, double minLon, double maxLon)
        {
            Parallel.For(1, _nx - 1, i =>
            {
                double lat = minLat + (i / (double)(_nx - 1)) * (maxLat - minLat);

                for (int j = 1; j < _ny - 1; j++)
                {
                    double lon = minLon + (j / (double)(_ny - 1)) * (maxLon - minLon);

                    for (int k = 1; k < _nz - 1; k++)
                    {
                        double depth = k * _dz;

                        // Get displacement field
                        var wf = waveEngine.GetWaveFieldAt(i, j, k);

                        // Calculate strain from neighboring displacements
                        var wfXp = waveEngine.GetWaveFieldAt(i + 1, j, k);
                        var wfXm = waveEngine.GetWaveFieldAt(i - 1, j, k);
                        var wfYp = waveEngine.GetWaveFieldAt(i, j + 1, k);
                        var wfYm = waveEngine.GetWaveFieldAt(i, j - 1, k);
                        var wfZp = waveEngine.GetWaveFieldAt(i, j, k + 1);
                        var wfZm = waveEngine.GetWaveFieldAt(i, j, k - 1);

                        // Strain components (small strain theory)
                        double exx = (wfXp.DisplacementX - wfXm.DisplacementX) / (2.0 * _dx);
                        double eyy = (wfYp.DisplacementY - wfYm.DisplacementY) / (2.0 * _dy);
                        double ezz = (wfZp.DisplacementZ - wfZm.DisplacementZ) / (2.0 * _dz);

                        double exy = 0.5 * ((wfYp.DisplacementX - wfYm.DisplacementX) / (2.0 * _dy) +
                                            (wfXp.DisplacementY - wfXm.DisplacementY) / (2.0 * _dx));
                        double exz = 0.5 * ((wfZp.DisplacementX - wfZm.DisplacementX) / (2.0 * _dz) +
                                            (wfXp.DisplacementZ - wfXm.DisplacementZ) / (2.0 * _dx));
                        double eyz = 0.5 * ((wfZp.DisplacementY - wfZm.DisplacementY) / (2.0 * _dz) +
                                            (wfYp.DisplacementZ - wfYm.DisplacementZ) / (2.0 * _dy));

                        // Get material properties
                        var crustalType = crustalModel.GetCrustalType(lat, lon);
                        var (_, layer, _) = crustalType.GetLayerAtDepth(depth);

                        double lambda = layer.GetLambda() * 1000.0; // GPa to MPa
                        double mu = layer.GetMu() * 1000.0;         // GPa to MPa

                        // Add lithostatic stress
                        double rho = layer.DensityGPerCm3 * 1000.0; // kg/m^3
                        double g = 9.81; // m/s^2
                        double lithostaticStress = rho * g * depth * 1000.0 / 1e6; // MPa

                        // Calculate stress (linear elasticity)
                        double volumetricStrain = exx + eyy + ezz;

                        _sigmaXX[i, j, k] = lambda * volumetricStrain + 2.0 * mu * exx - lithostaticStress;
                        _sigmaYY[i, j, k] = lambda * volumetricStrain + 2.0 * mu * eyy - lithostaticStress;
                        _sigmaZZ[i, j, k] = lambda * volumetricStrain + 2.0 * mu * ezz - lithostaticStress;
                        _sigmaXY[i, j, k] = 2.0 * mu * exy;
                        _sigmaXZ[i, j, k] = 2.0 * mu * exz;
                        _sigmaYZ[i, j, k] = 2.0 * mu * eyz;
                    }
                }
            });
        }

        /// <summary>
        /// Check fracture initiation using Griffith criterion
        /// </summary>
        public void CheckFractureInitiation()
        {
            Parallel.For(0, _nx, i =>
            {
                for (int j = 0; j < _ny; j++)
                {
                    for (int k = 0; k < _nz; k++)
                    {
                        if (_isFractured[i, j, k]) continue;

                        // Calculate principal stresses
                        var (sigma1, sigma2, sigma3) = CalculatePrincipalStresses(i, j, k);

                        // Maximum tensile stress criterion
                        double maxTensileStress = Math.Max(Math.Max(sigma1, sigma2), sigma3);

                        // Tensile strength (rough estimate from toughness)
                        double Kic = _fractureToughness[i, j, k];
                        double a_crack = 0.001; // Assumed initial crack size (m)
                        double tensileStrength = Kic / Math.Sqrt(Math.PI * a_crack);

                        // Check if tensile failure occurs
                        if (maxTensileStress > tensileStrength)
                        {
                            _isFractured[i, j, k] = true;

                            // Calculate initial aperture (Sneddon's solution)
                            double E = _youngsModulus[i, j, k] * 1000.0; // GPa to MPa
                            double nu = _poissonsRatio[i, j, k];
                            double G = E / (2.0 * (1.0 + nu));

                            _fractureAperture[i, j, k] = maxTensileStress * a_crack / G * 1e6; // to μm
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Calculate principal stresses and directions
        /// </summary>
        private (double sigma1, double sigma2, double sigma3) CalculatePrincipalStresses(int i, int j, int k)
        {
            // Stress tensor
            double sxx = _sigmaXX[i, j, k];
            double syy = _sigmaYY[i, j, k];
            double szz = _sigmaZZ[i, j, k];
            double sxy = _sigmaXY[i, j, k];
            double sxz = _sigmaXZ[i, j, k];
            double syz = _sigmaYZ[i, j, k];

            // Invariants
            double I1 = sxx + syy + szz;
            double I2 = sxx * syy + syy * szz + szz * sxx - sxy * sxy - sxz * sxz - syz * syz;
            double I3 = sxx * syy * szz + 2.0 * sxy * sxz * syz - sxx * syz * syz - syy * sxz * sxz - szz * sxy * sxy;

            // Solve cubic equation for eigenvalues (principal stresses)
            // Simplified using Cardano's formula
            double q = (3.0 * I2 - I1 * I1) / 9.0;
            double r = (9.0 * I1 * I2 - 27.0 * I3 - 2.0 * I1 * I1 * I1) / 54.0;

            double theta = Math.Acos(r / Math.Sqrt(-q * q * q));

            double sigma1 = I1 / 3.0 + 2.0 * Math.Sqrt(-q) * Math.Cos(theta / 3.0);
            double sigma2 = I1 / 3.0 + 2.0 * Math.Sqrt(-q) * Math.Cos((theta + 2.0 * Math.PI) / 3.0);
            double sigma3 = I1 / 3.0 + 2.0 * Math.Sqrt(-q) * Math.Cos((theta + 4.0 * Math.PI) / 3.0);

            return (sigma1, sigma2, sigma3);
        }

        /// <summary>
        /// Calculate Coulomb failure stress on a fault plane
        /// </summary>
        public double CalculateCoulombStress(
            int i, int j, int k,
            double strikeRad, double dipRad, double rakeRad,
            double frictionCoeff = 0.6)
        {
            // Fault plane normal and slip direction
            double nx = Math.Sin(dipRad) * Math.Sin(strikeRad);
            double ny = Math.Sin(dipRad) * Math.Cos(strikeRad);
            double nz = -Math.Cos(dipRad);

            double sx = Math.Cos(rakeRad) * Math.Cos(strikeRad) + Math.Sin(rakeRad) * Math.Cos(dipRad) * Math.Sin(strikeRad);
            double sy = -Math.Cos(rakeRad) * Math.Sin(strikeRad) + Math.Sin(rakeRad) * Math.Cos(dipRad) * Math.Cos(strikeRad);
            double sz = Math.Sin(rakeRad) * Math.Sin(dipRad);

            // Stress components
            double sxx = _sigmaXX[i, j, k];
            double syy = _sigmaYY[i, j, k];
            double szz = _sigmaZZ[i, j, k];
            double sxy = _sigmaXY[i, j, k];
            double sxz = _sigmaXZ[i, j, k];
            double syz = _sigmaYZ[i, j, k];

            // Normal stress on fault plane
            double normalStress = sxx * nx * nx + syy * ny * ny + szz * nz * nz +
                                 2.0 * (sxy * nx * ny + sxz * nx * nz + syz * ny * nz);

            // Shear stress in slip direction
            double shearStress = (sxx * nx * sx + sxy * (nx * sy + ny * sx) + sxz * (nx * sz + nz * sx) +
                                 syy * ny * sy + syz * (ny * sz + nz * sy) + szz * nz * sz);

            // Coulomb failure function
            // CFS = τ - μ(σn - p)  where p is pore pressure (assumed 0 here)
            double coulombStress = shearStress - frictionCoeff * normalStress;

            return coulombStress;
        }

        /// <summary>
        /// Generate fracture state map
        /// </summary>
        public FractureState[,,] GenerateFractureMap(
            double minLat, double maxLat,
            double minLon, double maxLon,
            double strikeRad, double dipRad, double rakeRad)
        {
            var fractureMap = new FractureState[_nx, _ny, _nz];

            Parallel.For(0, _nx, i =>
            {
                double lat = minLat + (i / (double)(_nx - 1)) * (maxLat - minLat);

                for (int j = 0; j < _ny; j++)
                {
                    double lon = minLon + (j / (double)(_ny - 1)) * (maxLon - minLon);

                    for (int k = 0; k < _nz; k++)
                    {
                        double depth = k * _dz;

                        var (sigma1, sigma2, sigma3) = CalculatePrincipalStresses(i, j, k);
                        double coulombStress = CalculateCoulombStress(i, j, k, strikeRad, dipRad, rakeRad);

                        fractureMap[i, j, k] = new FractureState
                        {
                            X = i * _dx,
                            Y = j * _dy,
                            Z = k * _dz,
                            Latitude = lat,
                            Longitude = lon,
                            Depth = depth,
                            IsFractured = _isFractured[i, j, k],
                            Type = DetermineFractureType(sigma1, sigma2, sigma3),
                            StressIntensityFactor = _fractureToughness[i, j, k],
                            FractureToughness = _fractureToughness[i, j, k],
                            CoulombStress = coulombStress,
                            FrictionCoefficient = 0.6,
                            NormalStress = sigma3, // Minimum principal stress (most compressive)
                            ShearStress = Math.Abs(sigma1 - sigma3) / 2.0,
                            ApertureChange = _fractureAperture[i, j, k],
                            FractureOrientation = new Vector3(0, 0, 1) // Simplified
                        };
                    }
                }
            });

            return fractureMap;
        }

        /// <summary>
        /// Determine fracture type from principal stresses
        /// </summary>
        private FractureType DetermineFractureType(double sigma1, double sigma2, double sigma3)
        {
            // If maximum principal stress is tensile
            if (sigma1 > 0)
                return FractureType.Tensile;

            // Otherwise shear failure
            double tau_max = (sigma1 - sigma3) / 2.0;
            double sigma_mean = (sigma1 + sigma3) / 2.0;

            // Simple classification based on stress state
            if (Math.Abs(sigma2 - sigma_mean) < Math.Abs(tau_max) * 0.1)
                return FractureType.ShearMode2;
            else
                return FractureType.ShearMode3;
        }

        /// <summary>
        /// Get fracture density map (for visualization)
        /// </summary>
        public double[,] GetFractureDensityMap()
        {
            var density = new double[_nx, _ny];

            Parallel.For(0, _nx, i =>
            {
                for (int j = 0; j < _ny; j++)
                {
                    int fracCount = 0;
                    for (int k = 0; k < _nz; k++)
                    {
                        if (_isFractured[i, j, k])
                            fracCount++;
                    }
                    density[i, j] = fracCount / (double)_nz;
                }
            });

            return density;
        }
    }
}
