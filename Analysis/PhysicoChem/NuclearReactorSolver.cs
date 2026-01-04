using System;
using System.Collections.Generic;
using GeoscientistToolkit.Data.PhysicoChem;

namespace GeoscientistToolkit.Analysis.PhysicoChem
{
    /// <summary>
    /// Nuclear reactor physics solver implementing:
    /// - Two-group neutron diffusion equation
    /// - Point kinetics with six delayed neutron groups
    /// - Xenon-135 and Samarium-149 poison dynamics
    /// - Thermal-hydraulic feedback
    /// - Control rod reactivity
    ///
    /// References:
    /// - Duderstadt & Hamilton, "Nuclear Reactor Analysis" (1976)
    /// - Stacey, "Nuclear Reactor Physics" (2007)
    /// - Keepin, "Physics of Nuclear Kinetics" (1965)
    /// </summary>
    public class NuclearReactorSolver
    {
        private readonly NuclearReactorParameters _params;
        private NuclearReactorState _state;
        private readonly List<NuclearReactorState> _history = new();

        // Grid dimensions
        private int _nx, _ny, _nz;
        private double _dx, _dy, _dz;

        // Field arrays (two energy groups)
        private double[,,] _fluxFast = null!;    // Group 1 (fast)
        private double[,,] _fluxThermal = null!; // Group 2 (thermal)
        private double[,,] _powerDensity = null!;
        private double[,,] _fuelTemp = null!;
        private double[,,] _coolantTemp = null!;

        // Delayed neutron precursors (6 groups, spatially dependent)
        private double[,,,] _precursors = null!;

        // Physical constants
        private const double EnergyPerFission = 200.0; // MeV
        private const double MeVToJoule = 1.602e-13;
        private const double AvogadroNumber = 6.022e23;

        // Xenon-135 constants (from Keepin, 1965)
        private const double LambdaXe = 2.09e-5;  // Xe-135 decay constant (1/s)
        private const double LambdaI = 2.87e-5;   // I-135 decay constant (1/s)
        private const double GammaXe = 0.003;     // Xe-135 direct fission yield
        private const double GammaI = 0.061;      // I-135 fission yield
        private const double SigmaXe = 2.65e-18;  // Xe-135 absorption cross section (cm²)

        // Samarium-149 constants
        private const double LambdaPm = 3.63e-6;  // Pm-149 decay constant (1/s)
        private const double GammaPm = 0.0113;    // Pm-149 fission yield
        private const double SigmaSm = 4.1e-20;   // Sm-149 absorption cross section (cm²)

        public NuclearReactorSolver(NuclearReactorParameters parameters)
        {
            _params = parameters;
            _state = new NuclearReactorState();
            InitializeGrid();
        }

        /// <summary>
        /// Initialize computational grid based on core geometry
        /// </summary>
        private void InitializeGrid()
        {
            // Discretization
            _nx = _params.RadialRings * 2 + 1;
            _ny = _params.RadialRings * 2 + 1;
            _nz = _params.AxialNodes;

            _dx = _params.CoreDiameter / (_nx - 1);
            _dy = _params.CoreDiameter / (_ny - 1);
            _dz = _params.CoreHeight / (_nz - 1);

            // Allocate arrays
            _fluxFast = new double[_nx, _ny, _nz];
            _fluxThermal = new double[_nx, _ny, _nz];
            _powerDensity = new double[_nx, _ny, _nz];
            _fuelTemp = new double[_nx, _ny, _nz];
            _coolantTemp = new double[_nx, _ny, _nz];
            _precursors = new double[6, _nx, _ny, _nz];

            // Initialize with guess solution (cosine shape)
            InitializeFluxDistribution();
            InitializeTemperatureDistribution();
        }

        /// <summary>
        /// Initialize neutron flux with fundamental mode shape
        /// </summary>
        private void InitializeFluxDistribution()
        {
            double r0 = _params.CoreDiameter / 2;
            double h = _params.CoreHeight;
            double phi0 = _params.Neutronics.NeutronFluxThermal;

            for (int i = 0; i < _nx; i++)
            {
                for (int j = 0; j < _ny; j++)
                {
                    double x = (i - _nx / 2.0) * _dx;
                    double y = (j - _ny / 2.0) * _dy;
                    double r = Math.Sqrt(x * x + y * y);

                    // Radial J0 Bessel function approximation
                    double radialShape = r < r0 ? Math.Cos(2.405 * r / (r0 + 0.71 * 0.4)) : 0;
                    radialShape = Math.Max(0, radialShape);

                    for (int k = 0; k < _nz; k++)
                    {
                        double z = k * _dz;
                        // Axial cosine shape
                        double axialShape = Math.Cos(Math.PI * (z - h / 2) / (h + 2 * 0.71 * 0.4));
                        axialShape = Math.Max(0, axialShape);

                        _fluxThermal[i, j, k] = phi0 * radialShape * axialShape;
                        _fluxFast[i, j, k] = _fluxThermal[i, j, k] *
                            (_params.Neutronics.NeutronFluxFast / _params.Neutronics.NeutronFluxThermal);
                    }
                }
            }
        }

        /// <summary>
        /// Initialize temperature distribution
        /// </summary>
        private void InitializeTemperatureDistribution()
        {
            double tIn = _params.Coolant.InletTemperature;
            double tOut = _params.Coolant.OutletTemperature;

            for (int i = 0; i < _nx; i++)
            {
                for (int j = 0; j < _ny; j++)
                {
                    for (int k = 0; k < _nz; k++)
                    {
                        double axialFraction = (double)k / (_nz - 1);
                        _coolantTemp[i, j, k] = tIn + (tOut - tIn) * axialFraction;
                        _fuelTemp[i, j, k] = _coolantTemp[i, j, k] + 200; // Rough estimate
                    }
                }
            }
        }

        /// <summary>
        /// Solve steady-state criticality problem using power iteration
        /// </summary>
        public double SolveCriticality(int maxIterations = 500, double tolerance = 1e-6)
        {
            double keff = 1.0;
            double keffOld = 1.0;
            double[,,] fissionSource = new double[_nx, _ny, _nz];
            double[,,] fluxThermalNew = new double[_nx, _ny, _nz];
            double[,,] fluxFastNew = new double[_nx, _ny, _nz];

            // Cross sections
            double sigmaA1 = _params.Neutronics.SigmaAbsorption1;
            double sigmaA2 = _params.Neutronics.SigmaAbsorption2;
            double sigmaF1 = _params.Neutronics.SigmaFission1;
            double sigmaF2 = _params.Neutronics.SigmaFission2;
            double sigmaS12 = _params.Neutronics.SigmaScatter12;
            double D1 = _params.Neutronics.DiffusionCoeff1;
            double D2 = _params.Neutronics.DiffusionCoeff2;
            double nu = _params.Neutronics.AverageNeutronsPerFission;

            for (int iter = 0; iter < maxIterations; iter++)
            {
                // Calculate fission source
                double totalFission = 0;
                for (int i = 0; i < _nx; i++)
                {
                    for (int j = 0; j < _ny; j++)
                    {
                        for (int k = 0; k < _nz; k++)
                        {
                            fissionSource[i, j, k] = nu * (sigmaF1 * _fluxFast[i, j, k] +
                                                           sigmaF2 * _fluxThermal[i, j, k]);
                            totalFission += fissionSource[i, j, k];
                        }
                    }
                }

                // Solve fast group: -D1∇²φ1 + (Σa1 + Σs12)φ1 = χ1*S/keff
                SolveDiffusionGroup(fluxFastNew, _fluxFast, fissionSource, D1, sigmaA1 + sigmaS12,
                    1.0 / keff, 1.0); // χ1 ≈ 1 for fast fission neutrons

                // Solve thermal group: -D2∇²φ2 + Σa2*φ2 = Σs12*φ1
                double[,,] thermalSource = new double[_nx, _ny, _nz];
                for (int i = 0; i < _nx; i++)
                    for (int j = 0; j < _ny; j++)
                        for (int k = 0; k < _nz; k++)
                            thermalSource[i, j, k] = sigmaS12 * fluxFastNew[i, j, k];

                SolveDiffusionGroup(fluxThermalNew, _fluxThermal, thermalSource, D2, sigmaA2, 1.0, 1.0);

                // Calculate new keff
                double newTotalFission = 0;
                for (int i = 0; i < _nx; i++)
                {
                    for (int j = 0; j < _ny; j++)
                    {
                        for (int k = 0; k < _nz; k++)
                        {
                            newTotalFission += nu * (sigmaF1 * fluxFastNew[i, j, k] +
                                                     sigmaF2 * fluxThermalNew[i, j, k]);
                        }
                    }
                }

                keffOld = keff;
                keff *= newTotalFission / totalFission;

                // Update fluxes
                Array.Copy(fluxFastNew, _fluxFast, _fluxFast.Length);
                Array.Copy(fluxThermalNew, _fluxThermal, _fluxThermal.Length);

                // Normalize flux
                NormalizeFlux();

                // Check convergence
                if (Math.Abs(keff - keffOld) < tolerance)
                {
                    break;
                }
            }

            _params.Neutronics.Keff = keff;
            _state.Keff = keff;
            _state.ReactivityPcm = (keff - 1) / keff * 1e5;

            UpdatePowerDistribution();
            return keff;
        }

        /// <summary>
        /// Solve one-group diffusion equation using finite differences
        /// </summary>
        private void SolveDiffusionGroup(double[,,] fluxNew, double[,,] fluxOld,
            double[,,] source, double D, double sigmaR, double multiplier, double chi)
        {
            // Gauss-Seidel iteration with SOR
            double omega = 1.5; // Over-relaxation factor
            double dx2 = _dx * _dx;
            double dy2 = _dy * _dy;
            double dz2 = _dz * _dz;

            for (int sweep = 0; sweep < 50; sweep++)
            {
                for (int i = 1; i < _nx - 1; i++)
                {
                    for (int j = 1; j < _ny - 1; j++)
                    {
                        for (int k = 1; k < _nz - 1; k++)
                        {
                            // Check if inside cylindrical core
                            double x = (i - _nx / 2.0) * _dx;
                            double y = (j - _ny / 2.0) * _dy;
                            double r = Math.Sqrt(x * x + y * y);
                            if (r > _params.CoreDiameter / 2) continue;

                            double laplacian =
                                D * ((fluxNew[i + 1, j, k] + fluxNew[i - 1, j, k] - 2 * fluxOld[i, j, k]) / dx2 +
                                     (fluxNew[i, j + 1, k] + fluxNew[i, j - 1, k] - 2 * fluxOld[i, j, k]) / dy2 +
                                     (fluxNew[i, j, k + 1] + fluxNew[i, j, k - 1] - 2 * fluxOld[i, j, k]) / dz2);

                            double phiNew = (chi * multiplier * source[i, j, k] + laplacian) / sigmaR;
                            fluxNew[i, j, k] = fluxOld[i, j, k] + omega * (phiNew - fluxOld[i, j, k]);
                            fluxNew[i, j, k] = Math.Max(0, fluxNew[i, j, k]);
                        }
                    }
                }
            }

            // Apply zero-flux boundary conditions
            ApplyBoundaryConditions(fluxNew);
        }

        /// <summary>
        /// Apply boundary conditions (zero flux at extrapolated boundary)
        /// </summary>
        private void ApplyBoundaryConditions(double[,,] flux)
        {
            for (int i = 0; i < _nx; i++)
            {
                for (int j = 0; j < _ny; j++)
                {
                    flux[i, j, 0] = 0;
                    flux[i, j, _nz - 1] = 0;
                }
            }
            for (int i = 0; i < _nx; i++)
            {
                for (int k = 0; k < _nz; k++)
                {
                    flux[i, 0, k] = 0;
                    flux[i, _ny - 1, k] = 0;
                }
            }
            for (int j = 0; j < _ny; j++)
            {
                for (int k = 0; k < _nz; k++)
                {
                    flux[0, j, k] = 0;
                    flux[_nx - 1, j, k] = 0;
                }
            }
        }

        /// <summary>
        /// Normalize flux to desired power level
        /// </summary>
        private void NormalizeFlux()
        {
            double totalPower = 0;
            double sigmaF2 = _params.Neutronics.SigmaFission2;

            for (int i = 0; i < _nx; i++)
            {
                for (int j = 0; j < _ny; j++)
                {
                    for (int k = 0; k < _nz; k++)
                    {
                        totalPower += sigmaF2 * _fluxThermal[i, j, k] * EnergyPerFission * MeVToJoule;
                    }
                }
            }

            double dV = _dx * _dy * _dz * 1e6; // m³ to cm³
            totalPower *= dV; // Watts

            double targetPower = _params.ThermalPowerMW * 1e6; // Watts
            double normFactor = targetPower / (totalPower + 1e-30);

            for (int i = 0; i < _nx; i++)
            {
                for (int j = 0; j < _ny; j++)
                {
                    for (int k = 0; k < _nz; k++)
                    {
                        _fluxFast[i, j, k] *= normFactor;
                        _fluxThermal[i, j, k] *= normFactor;
                    }
                }
            }
        }

        /// <summary>
        /// Update power density distribution from flux
        /// </summary>
        private void UpdatePowerDistribution()
        {
            double sigmaF2 = _params.Neutronics.SigmaFission2;
            double sigmaF1 = _params.Neutronics.SigmaFission1;

            double maxPower = 0;
            double avgPower = 0;
            int count = 0;

            for (int i = 0; i < _nx; i++)
            {
                for (int j = 0; j < _ny; j++)
                {
                    for (int k = 0; k < _nz; k++)
                    {
                        // Power density in kW/L
                        double fissionRate = sigmaF1 * _fluxFast[i, j, k] + sigmaF2 * _fluxThermal[i, j, k];
                        _powerDensity[i, j, k] = fissionRate * EnergyPerFission * MeVToJoule * 1e-3;

                        if (_powerDensity[i, j, k] > 0)
                        {
                            maxPower = Math.Max(maxPower, _powerDensity[i, j, k]);
                            avgPower += _powerDensity[i, j, k];
                            count++;
                        }
                    }
                }
            }

            avgPower /= Math.Max(1, count);
            _params.Neutronics.RadialPeakingFactor = maxPower / (avgPower + 1e-30);

            _state.PowerDensity = _powerDensity;
        }

        /// <summary>
        /// Solve point kinetics equations with delayed neutrons
        /// dn/dt = (ρ - β)/Λ * n + Σ λi*Ci
        /// dCi/dt = βi/Λ * n - λi*Ci
        /// </summary>
        public void SolvePointKinetics(double dt, double externalReactivity = 0)
        {
            double rho = CalculateTotalReactivity() + externalReactivity;
            double beta = _params.Neutronics.DelayedNeutronFraction;
            double Lambda = _params.Neutronics.GenerationTime;
            double[] betaI = _params.Neutronics.DelayedFractions;
            double[] lambdaI = _params.Neutronics.DecayConstants;

            double n = _state.RelativePower;
            double[] C = _state.PrecursorConcentrations;

            // RK4 integration
            double[] k1n = new double[1], k2n = new double[1], k3n = new double[1], k4n = new double[1];
            double[][] k1C = new double[6][], k2C = new double[6][], k3C = new double[6][], k4C = new double[6][];

            for (int i = 0; i < 6; i++)
            {
                k1C[i] = new double[1];
                k2C[i] = new double[1];
                k3C[i] = new double[1];
                k4C[i] = new double[1];
            }

            // k1
            k1n[0] = dt * ((rho - beta) / Lambda * n + SumPrecursorDecay(C, lambdaI));
            for (int i = 0; i < 6; i++)
                k1C[i][0] = dt * (betaI[i] / Lambda * n - lambdaI[i] * C[i]);

            // k2
            double nTemp = n + 0.5 * k1n[0];
            double[] CTemp = new double[6];
            for (int i = 0; i < 6; i++) CTemp[i] = C[i] + 0.5 * k1C[i][0];
            k2n[0] = dt * ((rho - beta) / Lambda * nTemp + SumPrecursorDecay(CTemp, lambdaI));
            for (int i = 0; i < 6; i++)
                k2C[i][0] = dt * (betaI[i] / Lambda * nTemp - lambdaI[i] * CTemp[i]);

            // k3
            nTemp = n + 0.5 * k2n[0];
            for (int i = 0; i < 6; i++) CTemp[i] = C[i] + 0.5 * k2C[i][0];
            k3n[0] = dt * ((rho - beta) / Lambda * nTemp + SumPrecursorDecay(CTemp, lambdaI));
            for (int i = 0; i < 6; i++)
                k3C[i][0] = dt * (betaI[i] / Lambda * nTemp - lambdaI[i] * CTemp[i]);

            // k4
            nTemp = n + k3n[0];
            for (int i = 0; i < 6; i++) CTemp[i] = C[i] + k3C[i][0];
            k4n[0] = dt * ((rho - beta) / Lambda * nTemp + SumPrecursorDecay(CTemp, lambdaI));
            for (int i = 0; i < 6; i++)
                k4C[i][0] = dt * (betaI[i] / Lambda * nTemp - lambdaI[i] * CTemp[i]);

            // Update
            _state.RelativePower = n + (k1n[0] + 2 * k2n[0] + 2 * k3n[0] + k4n[0]) / 6;
            _state.RelativePower = Math.Max(0, _state.RelativePower);
            for (int i = 0; i < 6; i++)
            {
                _state.PrecursorConcentrations[i] = C[i] + (k1C[i][0] + 2 * k2C[i][0] + 2 * k3C[i][0] + k4C[i][0]) / 6;
                _state.PrecursorConcentrations[i] = Math.Max(0, _state.PrecursorConcentrations[i]);
            }

            _state.ThermalPowerMW = _state.RelativePower * _params.ThermalPowerMW;
            _state.ReactivityPcm = rho * 1e5;
            _state.PeriodSeconds = _params.Neutronics.CalculatePeriod(_state.ReactivityPcm);
            _state.Time += dt;
        }

        private double SumPrecursorDecay(double[] C, double[] lambda)
        {
            double sum = 0;
            for (int i = 0; i < 6; i++)
                sum += lambda[i] * C[i];
            return sum;
        }

        /// <summary>
        /// Calculate total reactivity including all feedback effects
        /// </summary>
        public double CalculateTotalReactivity()
        {
            double rho = 0;

            // Control rod reactivity
            foreach (var bank in _params.ControlRodBanks)
            {
                rho += bank.GetReactivityContribution() / 1e5;
            }

            // Boron reactivity (-10 pcm/ppm typical)
            rho -= 10 * _params.BoronConcentrationPPM / 1e5;

            // Doppler (fuel temperature) feedback
            double avgFuelTemp = CalculateAverageFuelTemp();
            double dopplerCoeff = -2.5e-5; // per °C (typical PWR)
            rho += dopplerCoeff * (avgFuelTemp - 900); // Reference temp 900°C

            // Moderator temperature feedback
            double avgModTemp = CalculateAverageCoolantTemp();
            double modTempCoeff = -2e-4; // per °C at BOL (negative)
            rho += modTempCoeff * (avgModTemp - 300);

            // Xenon poisoning
            rho += CalculateXenonReactivity();

            return rho;
        }

        /// <summary>
        /// Solve Xenon-135 dynamics
        /// dI/dt = γI*Σf*φ - λI*I
        /// dXe/dt = γXe*Σf*φ + λI*I - λXe*Xe - σXe*φ*Xe
        /// </summary>
        public void SolveXenonDynamics(double dt)
        {
            double sigmaF = _params.Neutronics.SigmaFission2;
            double phi = _params.Neutronics.NeutronFluxThermal * _state.RelativePower;

            double I = _state.IodineConcentration;
            double Xe = _state.XenonConcentration;

            // Simple Euler integration (could be improved)
            double dIdt = GammaI * sigmaF * phi - LambdaI * I;
            double dXedt = GammaXe * sigmaF * phi + LambdaI * I - LambdaXe * Xe - SigmaXe * phi * Xe;

            _state.IodineConcentration = Math.Max(0, I + dIdt * dt);
            _state.XenonConcentration = Math.Max(0, Xe + dXedt * dt);
        }

        /// <summary>
        /// Calculate xenon reactivity worth
        /// </summary>
        private double CalculateXenonReactivity()
        {
            double phi = _params.Neutronics.NeutronFluxThermal * _state.RelativePower;
            double Xe = _state.XenonConcentration;

            // Δρ = -σXe * Xe / Σa
            double sigmaA = _params.Neutronics.SigmaAbsorption2;
            return -SigmaXe * Xe / (sigmaA + 1e-30);
        }

        /// <summary>
        /// Update thermal-hydraulics (simplified single-channel model)
        /// </summary>
        public void UpdateThermalHydraulics()
        {
            double power = _state.ThermalPowerMW * 1e6; // W
            double massFlow = _params.Coolant.MassFlowRate;
            double cp = _params.Coolant.GetSpecificHeat();
            double tIn = _params.Coolant.InletTemperature;

            // Coolant temperature rise
            double dT = power / (massFlow * cp);
            _params.Coolant.OutletTemperature = tIn + dT;

            // Update 3D temperature distribution
            for (int i = 0; i < _nx; i++)
            {
                for (int j = 0; j < _ny; j++)
                {
                    for (int k = 0; k < _nz; k++)
                    {
                        double axialFrac = (double)k / (_nz - 1);
                        _coolantTemp[i, j, k] = tIn + dT * axialFrac;

                        // Fuel temperature from power density and heat transfer
                        double q = _powerDensity[i, j, k] * 1e3; // W/L
                        double hCoeff = _params.ThermalHydraulics.HeatTransferCoeff;
                        double kFuel = _params.ThermalHydraulics.FuelThermalConductivity;

                        // Simplified: T_fuel = T_coolant + q/(h*A) + q*r²/(4k)
                        double dTconv = q / (hCoeff + 1e-30) * 0.01; // Simplified
                        double dTcond = q / (4 * Math.PI * kFuel + 1e-30) * 0.001;
                        _fuelTemp[i, j, k] = _coolantTemp[i, j, k] + dTconv + dTcond;
                    }
                }
            }

            _state.FuelTemperature = _fuelTemp;
            _state.CoolantTemperature = _coolantTemp;
            _state.PeakFuelTemp = CalculatePeakFuelTemp();
            _state.PeakCladTemp = CalculatePeakCladTemp();
        }

        private double CalculateAverageFuelTemp()
        {
            double sum = 0;
            int count = 0;
            for (int i = 0; i < _nx; i++)
                for (int j = 0; j < _ny; j++)
                    for (int k = 0; k < _nz; k++)
                        if (_fuelTemp[i, j, k] > 0) { sum += _fuelTemp[i, j, k]; count++; }
            return count > 0 ? sum / count : 400;
        }

        private double CalculateAverageCoolantTemp()
        {
            return (_params.Coolant.InletTemperature + _params.Coolant.OutletTemperature) / 2;
        }

        private double CalculatePeakFuelTemp()
        {
            double max = 0;
            for (int i = 0; i < _nx; i++)
                for (int j = 0; j < _ny; j++)
                    for (int k = 0; k < _nz; k++)
                        max = Math.Max(max, _fuelTemp[i, j, k]);
            return max;
        }

        private double CalculatePeakCladTemp()
        {
            // Clad is between fuel and coolant
            double max = 0;
            for (int i = 0; i < _nx; i++)
                for (int j = 0; j < _ny; j++)
                    for (int k = 0; k < _nz; k++)
                        max = Math.Max(max, (_fuelTemp[i, j, k] + _coolantTemp[i, j, k]) / 2);
            return max;
        }

        /// <summary>
        /// Perform SCRAM (emergency shutdown)
        /// </summary>
        public void PerformSCRAM()
        {
            foreach (var bank in _params.ControlRodBanks)
            {
                bank.InsertionFraction = 1.0; // Fully insert all rods
            }
            _params.Safety.IsScramActive = true;
        }

        /// <summary>
        /// Run transient simulation
        /// </summary>
        public List<NuclearReactorState> RunTransient(double endTime, double dt,
            Func<double, double>? reactivityProfile = null)
        {
            _history.Clear();
            _state.Time = 0;
            _state.RelativePower = 1.0; // Start at full power

            // Initialize precursors to equilibrium
            InitializeEquilibriumPrecursors();

            while (_state.Time < endTime)
            {
                double externalRho = reactivityProfile?.Invoke(_state.Time) ?? 0;

                SolvePointKinetics(dt, externalRho);
                SolveXenonDynamics(dt);
                UpdateThermalHydraulics();

                // Safety check
                if (_params.Safety.CheckSafetyLimits(
                    _state.RelativePower * 100,
                    _state.PeriodSeconds,
                    _state.PeakCladTemp,
                    _params.Coolant.Pressure))
                {
                    PerformSCRAM();
                }

                _history.Add(_state.Clone());
            }

            return _history;
        }

        private void InitializeEquilibriumPrecursors()
        {
            double n = _state.RelativePower;
            double Lambda = _params.Neutronics.GenerationTime;
            double[] betaI = _params.Neutronics.DelayedFractions;
            double[] lambdaI = _params.Neutronics.DecayConstants;

            for (int i = 0; i < 6; i++)
            {
                _state.PrecursorConcentrations[i] = betaI[i] * n / (lambdaI[i] * Lambda);
            }
        }

        /// <summary>
        /// Get current reactor state
        /// </summary>
        public NuclearReactorState GetState() => _state.Clone();

        /// <summary>
        /// Get neutron flux distribution
        /// </summary>
        public (double[,,] fast, double[,,] thermal) GetFluxDistribution()
        {
            return ((double[,,])_fluxFast.Clone(), (double[,,])_fluxThermal.Clone());
        }

        /// <summary>
        /// Get power density distribution
        /// </summary>
        public double[,,] GetPowerDistribution() => (double[,,])_powerDensity.Clone();
    }
}
