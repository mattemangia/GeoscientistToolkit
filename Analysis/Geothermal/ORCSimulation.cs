using System;
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using GeoscientistToolkit.Business;

namespace GeoscientistToolkit.Analysis.Geothermal
{
    /// <summary>
    /// Organic Rankine Cycle (ORC) simulation for geothermal power generation
    /// Supports SIMD acceleration for batch calculations
    /// Uses ORCFluidLibrary for comprehensive working fluid properties
    /// </summary>
    public class ORCSimulation
    {
        #region Working Fluid Properties

        private ORCFluid _currentFluid;

        #endregion

        #region Configuration

        private ORCConfiguration _config;
        public ORCConfiguration Config
        {
            get => _config;
            set
            {
                _config = value;
                if (_config != null)
                {
                    // Load fluid from library
                    _currentFluid = ORCFluidLibrary.Instance.GetFluidByName(_config.FluidName)
                                 ?? ORCFluidLibrary.Instance.GetFluidByName("R245fa"); // Default fallback
                }
            }
        }
        public bool UseSIMD { get; set; } = true;

        #endregion

        #region ORC State Points

        /// <summary>
        /// Thermodynamic state of working fluid
        /// </summary>
        public struct ORCState
        {
            public float Temperature; // K
            public float Pressure; // Pa
            public float Enthalpy; // J/kg
            public float Entropy; // J/(kg·K)
            public float Quality; // Vapor quality (0=liquid, 1=vapor)
            public bool IsValid;
        }

        /// <summary>
        /// ORC cycle results for a single time step
        /// </summary>
        public struct ORCCycleResults
        {
            public ORCState State1_PumpInlet; // Saturated liquid from condenser
            public ORCState State2_PumpOutlet; // High pressure liquid
            public ORCState State3_TurbineInlet; // Superheated vapor from evaporator
            public ORCState State4_TurbineOutlet; // Low pressure vapor to condenser

            public float MassFlowRate; // kg/s
            public float TurbineWork; // W
            public float PumpWork; // W
            public float NetPower; // W
            public float HeatInput; // W (from geothermal fluid)
            public float HeatRejected; // W (to cooling)
            public float ThermalEfficiency; // dimensionless
            public float ExergyEfficiency; // dimensionless
            public float SpecificPower; // W/(kg/s)

            public float GeothermalFluidInletTemp; // K
            public float GeothermalFluidOutletTemp; // K
            public float CoolingWaterInletTemp; // K
            public float CoolingWaterOutletTemp; // K
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Simulate ORC cycle for a single geothermal fluid temperature
        /// </summary>
        public ORCCycleResults SimulateCycle(float geofluidTempK, float geofluidMassFlowRate)
        {
            var config = Config ?? new ORCConfiguration();
            var results = new ORCCycleResults
            {
                GeothermalFluidInletTemp = geofluidTempK,
                CoolingWaterInletTemp = config.CondenserTemperature
            };

            // State 1: Condenser outlet (saturated liquid)
            results.State1_PumpInlet = CalculateSaturatedLiquid(config.CondenserTemperature);
            if (!results.State1_PumpInlet.IsValid) return results;

            // State 2: Pump outlet (high pressure liquid)
            results.State2_PumpOutlet = CalculatePumpOutlet(results.State1_PumpInlet, config.EvaporatorPressure, config.PumpEfficiency);
            if (!results.State2_PumpOutlet.IsValid) return results;

            // State 3: Evaporator outlet (superheated vapor)
            float maxEvapTemp = geofluidTempK - config.MinPinchPointTemperature;
            results.State3_TurbineInlet = CalculateSuperheatedVapor(config.EvaporatorPressure, maxEvapTemp, config.SuperheatDegrees);
            if (!results.State3_TurbineInlet.IsValid) return results;

            // State 4: Turbine outlet (low pressure vapor)
            results.State4_TurbineOutlet = CalculateTurbineOutlet(results.State3_TurbineInlet, results.State1_PumpInlet.Pressure, config.TurbineEfficiency);
            if (!results.State4_TurbineOutlet.IsValid) return results;

            // Calculate work and heat transfers
            float h1 = results.State1_PumpInlet.Enthalpy;
            float h2 = results.State2_PumpOutlet.Enthalpy;
            float h3 = results.State3_TurbineInlet.Enthalpy;
            float h4 = results.State4_TurbineOutlet.Enthalpy;

            // Determine ORC mass flow rate from heat exchanger effectiveness
            float maxHeatTransfer = geofluidMassFlowRate * config.GeothermalFluidCp * (geofluidTempK - config.CondenserTemperature);
            float orcHeatRequired = h3 - h2; // J/kg for ORC fluid
            results.MassFlowRate = Math.Min(
                maxHeatTransfer / orcHeatRequired,
                config.MaxORCMassFlowRate
            );

            // Power calculations
            results.PumpWork = results.MassFlowRate * (h2 - h1);
            results.TurbineWork = results.MassFlowRate * (h3 - h4);
            results.NetPower = results.TurbineWork - results.PumpWork;
            results.HeatInput = results.MassFlowRate * (h3 - h2);
            results.HeatRejected = results.MassFlowRate * (h4 - h1);
            results.ThermalEfficiency = results.NetPower / results.HeatInput;

            // Geothermal fluid outlet temperature
            results.GeothermalFluidOutletTemp = geofluidTempK - (results.HeatInput / (geofluidMassFlowRate * config.GeothermalFluidCp));

            // Cooling water outlet temperature
            float coolingWaterMassFlow = results.HeatRejected / (config.CoolingWaterCp * config.CondenserDeltaT);
            results.CoolingWaterOutletTemp = config.CondenserTemperature + config.CondenserDeltaT;

            // Specific power
            results.SpecificPower = results.NetPower / results.MassFlowRate;

            // Exergy efficiency (Carnot comparison)
            float carnotEfficiency = 1.0f - (config.CondenserTemperature / geofluidTempK);
            results.ExergyEfficiency = results.ThermalEfficiency / carnotEfficiency;

            return results;
        }

        /// <summary>
        /// Batch simulation using SIMD for multiple temperature points
        /// </summary>
        public ORCCycleResults[] SimulateCycleBatch(float[] geofluidTempsK, float geofluidMassFlowRate)
        {
            int n = geofluidTempsK.Length;
            var results = new ORCCycleResults[n];

            if (UseSIMD && Avx2.IsSupported && n >= 8)
            {
                SimulateCycleBatchSIMD(geofluidTempsK, geofluidMassFlowRate, results);
            }
            else
            {
                // Scalar fallback
                for (int i = 0; i < n; i++)
                {
                    results[i] = SimulateCycle(geofluidTempsK[i], geofluidMassFlowRate);
                }
            }

            return results;
        }

        #endregion

        #region SIMD Batch Processing

        private unsafe void SimulateCycleBatchSIMD(float[] temps, float massFlow, ORCCycleResults[] results)
        {
            int n = temps.Length;
            int vecSize = Vector256<float>.Count; // 8 floats
            int vecCount = n / vecSize;

            var config = Config ?? new ORCConfiguration();

            // Vectorized constants
            var vCondTemp = Vector256.Create(config.CondenserTemperature);
            var vEvapPress = Vector256.Create(config.EvaporatorPressure);
            var vPumpEff = Vector256.Create(config.PumpEfficiency);
            var vTurbEff = Vector256.Create(config.TurbineEfficiency);
            var vPinch = Vector256.Create(config.MinPinchPointTemperature);
            var vSuperheat = Vector256.Create(config.SuperheatDegrees);
            var vGeoMassFlow = Vector256.Create(massFlow);
            var vGeoCp = Vector256.Create(config.GeothermalFluidCp);

            fixed (float* pTemps = temps)
            {
                for (int v = 0; v < vecCount; v++)
                {
                    int baseIdx = v * vecSize;
                    var vGeoTemp = Avx.LoadVector256(pTemps + baseIdx);

                    // Process 8 cycles in parallel
                    ProcessCycleVectorAVX2(
                        vGeoTemp, vCondTemp, vEvapPress, vPumpEff, vTurbEff,
                        vPinch, vSuperheat, vGeoMassFlow, vGeoCp,
                        results, baseIdx
                    );
                }
            }

            // Handle remainder
            for (int i = vecCount * vecSize; i < n; i++)
            {
                results[i] = SimulateCycle(temps[i], massFlow);
            }
        }

        private unsafe void ProcessCycleVectorAVX2(
            Vector256<float> vGeoTemp,
            Vector256<float> vCondTemp,
            Vector256<float> vEvapPress,
            Vector256<float> vPumpEff,
            Vector256<float> vTurbEff,
            Vector256<float> vPinch,
            Vector256<float> vSuperheat,
            Vector256<float> vGeoMassFlow,
            Vector256<float> vGeoCp,
            ORCCycleResults[] results,
            int baseIdx)
        {
            // Calculate max evaporator temperature
            var vMaxEvapTemp = Avx.Subtract(vGeoTemp, vPinch);

            // State 1: Saturated liquid properties (vectorized)
            var (vH1, vS1, vP1) = CalculateSaturatedLiquidVec(vCondTemp);

            // State 2: Pump outlet
            var vDeltaH_pump = Avx.Divide(
                Avx.Multiply(
                    Avx.Subtract(vEvapPress, vP1),
                    Vector256.Create(1.0f / 1200.0f) // Approximate liquid density for R245fa
                ),
                vPumpEff
            );
            var vH2 = Avx.Add(vH1, vDeltaH_pump);

            // State 3: Superheated vapor at evaporator outlet
            var vTurbInTemp = Avx.Subtract(vMaxEvapTemp, vSuperheat);
            var (vH3, vS3) = CalculateSuperheatedVaporVec(vEvapPress, vTurbInTemp);

            // State 4: Turbine outlet (isentropic expansion with efficiency)
            var vH4s = CalculateEnthalpyFromEntropyVec(vS3, vP1); // Isentropic
            var vH4 = Avx.Subtract(vH3, Avx.Multiply(vTurbEff, Avx.Subtract(vH3, vH4s))); // Actual

            // Work and power calculations
            var vPumpWork = Avx.Subtract(vH2, vH1);
            var vTurbWork = Avx.Subtract(vH3, vH4);
            var vHeatInput = Avx.Subtract(vH3, vH2);

            // ORC mass flow rate determination
            var vMaxHeat = Avx.Multiply(vGeoMassFlow, Avx.Multiply(vGeoCp, Avx.Subtract(vGeoTemp, vCondTemp)));
            var vORCMassFlow = Avx.Min(
                Avx.Divide(vMaxHeat, vHeatInput),
                Vector256.Create(Config?.MaxORCMassFlowRate ?? 100.0f)
            );

            var vNetPower = Avx.Multiply(vORCMassFlow, Avx.Subtract(vTurbWork, vPumpWork));
            var vEfficiency = Avx.Divide(Avx.Subtract(vTurbWork, vPumpWork), vHeatInput);

            // Store results (extract from vectors)
            Span<float> netPower = stackalloc float[8];
            Span<float> efficiency = stackalloc float[8];
            Span<float> orcMassFlow = stackalloc float[8];
            Span<float> h1 = stackalloc float[8];
            Span<float> h2 = stackalloc float[8];
            Span<float> h3 = stackalloc float[8];
            Span<float> h4 = stackalloc float[8];

            fixed (float* pNetPower = netPower)
            fixed (float* pEfficiency = efficiency)
            fixed (float* pOrcMassFlow = orcMassFlow)
            fixed (float* pH1 = h1)
            fixed (float* pH2 = h2)
            fixed (float* pH3 = h3)
            fixed (float* pH4 = h4)
            {
                Avx.Store(pNetPower, vNetPower);
                Avx.Store(pEfficiency, vEfficiency);
                Avx.Store(pOrcMassFlow, vORCMassFlow);
                Avx.Store(pH1, vH1);
                Avx.Store(pH2, vH2);
                Avx.Store(pH3, vH3);
                Avx.Store(pH4, vH4);
            }

            for (int i = 0; i < 8; i++)
            {
                results[baseIdx + i].NetPower = netPower[i];
                results[baseIdx + i].ThermalEfficiency = efficiency[i];
                results[baseIdx + i].MassFlowRate = orcMassFlow[i];
                results[baseIdx + i].TurbineWork = orcMassFlow[i] * (h3[i] - h4[i]);
                results[baseIdx + i].PumpWork = orcMassFlow[i] * (h2[i] - h1[i]);
                results[baseIdx + i].HeatInput = orcMassFlow[i] * (h3[i] - h2[i]);
            }
        }

        #endregion

        #region Property Calculations

        private ORCState CalculateSaturatedLiquid(float temperature)
        {
            float pressure = CalculateSaturationPressure(temperature);
            return new ORCState
            {
                Temperature = temperature,
                Pressure = pressure,
                Enthalpy = CalculateEnthalpy(temperature, 0.0f),
                Entropy = CalculateEntropy(temperature, 0.0f),
                Quality = 0.0f,
                IsValid = temperature < _currentFluid.CriticalTemperature_K
            };
        }

        private ORCState CalculateSuperheatedVapor(float pressure, float temperature, float superheat)
        {
            float actualTemp = Math.Min(temperature, _currentFluid.CriticalTemperature_K - 10.0f);
            return new ORCState
            {
                Temperature = actualTemp,
                Pressure = pressure,
                Enthalpy = CalculateEnthalpy(actualTemp, 1.0f) + superheat * 1000f, // Add superheat correction
                Entropy = CalculateEntropy(actualTemp, 1.0f) + superheat * 2.0f,
                Quality = 1.0f,
                IsValid = actualTemp > CalculateSaturationTemperature(pressure)
            };
        }

        private ORCState CalculatePumpOutlet(ORCState inlet, float outletPressure, float efficiency)
        {
            // Incompressible liquid assumption
            float densityLiquid = _currentFluid.LiquidDensity_kgm3;
            float deltaH = (outletPressure - inlet.Pressure) / (densityLiquid * efficiency);

            float liquidCp = _currentFluid.LiquidHeatCapacity_JkgK > 0 ? _currentFluid.LiquidHeatCapacity_JkgK : 1400.0f;
            return new ORCState
            {
                Temperature = inlet.Temperature + deltaH / liquidCp, // Approximate heating
                Pressure = outletPressure,
                Enthalpy = inlet.Enthalpy + deltaH,
                Entropy = inlet.Entropy + deltaH / inlet.Temperature,
                Quality = 0.0f,
                IsValid = true
            };
        }

        private ORCState CalculateTurbineOutlet(ORCState inlet, float outletPressure, float efficiency)
        {
            // Isentropic expansion
            float Ts_out = CalculateSaturationTemperature(outletPressure);
            float hs = CalculateEnthalpy(Ts_out, 1.0f);

            // Actual expansion
            float h_actual = inlet.Enthalpy - efficiency * (inlet.Enthalpy - hs);

            return new ORCState
            {
                Temperature = Ts_out,
                Pressure = outletPressure,
                Enthalpy = h_actual,
                Entropy = inlet.Entropy, // Approximate
                Quality = 0.95f, // Mostly vapor
                IsValid = true
            };
        }

        // Property correlations using current fluid from library
        private float CalculateSaturationPressure(float temperature)
        {
            // Antoine equation: log10(P[Pa]) = A - B/(T[K] + C)
            var coeffs = _currentFluid.AntoineCoefficients_A_B_C;
            float A = coeffs[0], B = coeffs[1], C = coeffs[2];
            float log10P = A - B / (temperature + C);
            return MathF.Pow(10.0f, log10P); // Pa
        }

        private float CalculateSaturationTemperature(float pressure)
        {
            // Inverse Antoine: T = B/(A - log10(P)) - C
            var coeffs = _currentFluid.AntoineCoefficients_A_B_C;
            float A = coeffs[0], B = coeffs[1], C = coeffs[2];
            return B / (A - MathF.Log10(pressure)) - C;
        }

        private float CalculateEnthalpy(float temperature, float quality)
        {
            var hLiqCoeff = _currentFluid.LiquidEnthalpyCoeff_A_B_C_D;
            var hVapCoeff = _currentFluid.VaporEnthalpyCoeff_A_B_C_D;

            float hLiq = EvaluatePolynomial(temperature, hLiqCoeff);
            float hVap = EvaluatePolynomial(temperature, hVapCoeff);
            return hLiq + quality * (hVap - hLiq);
        }

        private float CalculateEntropy(float temperature, float quality)
        {
            var sLiqCoeff = _currentFluid.LiquidEntropyCoeff_A_B_C_D;
            var sVapCoeff = _currentFluid.VaporEntropyCoeff_A_B_C_D;

            float sLiq = EvaluatePolynomial(temperature, sLiqCoeff);
            float sVap = EvaluatePolynomial(temperature, sVapCoeff);
            return sLiq + quality * (sVap - sLiq);
        }

        private float EvaluatePolynomial(float x, float[] coeffs)
        {
            float result = coeffs[0];
            float xPow = x;
            for (int i = 1; i < coeffs.Length && i < 4; i++)
            {
                result += coeffs[i] * xPow;
                xPow *= x;
            }
            return result;
        }

        #endregion

        #region Vectorized Property Calculations

        private (Vector256<float> h, Vector256<float> s, Vector256<float> p) CalculateSaturatedLiquidVec(Vector256<float> vTemp)
        {
            var vH = EvaluatePolynomialVec(vTemp, _currentFluid.LiquidEnthalpyCoeff_A_B_C_D);
            var vS = EvaluatePolynomialVec(vTemp, _currentFluid.LiquidEntropyCoeff_A_B_C_D);
            var vP = CalculateSaturationPressureVec(vTemp);
            return (vH, vS, vP);
        }

        private (Vector256<float> h, Vector256<float> s) CalculateSuperheatedVaporVec(Vector256<float> vPress, Vector256<float> vTemp)
        {
            var vH = EvaluatePolynomialVec(vTemp, _currentFluid.VaporEnthalpyCoeff_A_B_C_D);
            var vS = EvaluatePolynomialVec(vTemp, _currentFluid.VaporEntropyCoeff_A_B_C_D);
            return (vH, vS);
        }

        private Vector256<float> CalculateEnthalpyFromEntropyVec(Vector256<float> vEntropy, Vector256<float> vPress)
        {
            // Simplified: assume isentropic corresponds to saturation at outlet pressure
            var vTemp = CalculateSaturationTemperatureVec(vPress);
            return EvaluatePolynomialVec(vTemp, _currentFluid.VaporEnthalpyCoeff_A_B_C_D);
        }

        private Vector256<float> CalculateSaturationPressureVec(Vector256<float> vTemp)
        {
            // Antoine equation: log10(P[Pa]) = A - B/(T[K] + C)
            var coeffs = _currentFluid.AntoineCoefficients_A_B_C;
            var vA = Vector256.Create(coeffs[0]);
            var vB = Vector256.Create(coeffs[1]);
            var vC = Vector256.Create(coeffs[2]);

            var vDenom = Avx.Add(vTemp, vC);
            var vLog10P = Avx.Subtract(vA, Avx.Divide(vB, vDenom));

            // Convert from log10 to actual pressure: P = 10^log10P
            // Use exp approximation: 10^x = exp(x * ln(10))
            var vLn10 = Vector256.Create(2.302585f);
            var vExp = Avx.Multiply(vLog10P, vLn10);
            return ExpApproxVec(vExp);
        }

        private Vector256<float> CalculateSaturationTemperatureVec(Vector256<float> vPress)
        {
            // Inverse Antoine: T = B/(A - log10(P)) - C
            var coeffs = _currentFluid.AntoineCoefficients_A_B_C;
            var vA = Vector256.Create(coeffs[0]);
            var vB = Vector256.Create(coeffs[1]);
            var vC = Vector256.Create(coeffs[2]);

            var vLog10P = Log10ApproxVec(vPress);
            var vDenom = Avx.Subtract(vA, vLog10P);
            return Avx.Subtract(Avx.Divide(vB, vDenom), vC);
        }

        private Vector256<float> EvaluatePolynomialVec(Vector256<float> vX, float[] coeffs)
        {
            var vResult = Vector256.Create(coeffs[0]);
            var vXPow = vX;

            for (int i = 1; i < coeffs.Length; i++)
            {
                vResult = Avx.Add(vResult, Avx.Multiply(Vector256.Create(coeffs[i]), vXPow));
                vXPow = Avx.Multiply(vXPow, vX);
            }

            return vResult;
        }

        private Vector256<float> ExpApproxVec(Vector256<float> vX)
        {
            // Taylor series: exp(x) ≈ 1 + x + x²/2 + x³/6 + x⁴/24 + x⁵/120
            var v1 = Vector256.Create(1.0f);
            var vX2 = Avx.Multiply(vX, vX);
            var vX3 = Avx.Multiply(vX2, vX);
            var vX4 = Avx.Multiply(vX3, vX);
            var vX5 = Avx.Multiply(vX4, vX);

            var vResult = v1;
            vResult = Avx.Add(vResult, vX);
            vResult = Avx.Add(vResult, Avx.Multiply(vX2, Vector256.Create(0.5f)));
            vResult = Avx.Add(vResult, Avx.Multiply(vX3, Vector256.Create(1.0f / 6.0f)));
            vResult = Avx.Add(vResult, Avx.Multiply(vX4, Vector256.Create(1.0f / 24.0f)));
            vResult = Avx.Add(vResult, Avx.Multiply(vX5, Vector256.Create(1.0f / 120.0f)));

            return vResult;
        }

        private Vector256<float> LogApproxVec(Vector256<float> vX)
        {
            // Natural log approximation: ln(x) ≈ (x-1) - (x-1)²/2 + (x-1)³/3 for x near 1
            // For better range, use ln(x) = ln(m * 2^e) = ln(m) + e*ln(2)
            // Simplified: just use (x-1) for now (should use proper range reduction)
            var v1 = Vector256.Create(1.0f);
            var vXm1 = Avx.Subtract(vX, v1);
            var vXm1_2 = Avx.Multiply(vXm1, vXm1);
            var vXm1_3 = Avx.Multiply(vXm1_2, vXm1);

            var vResult = vXm1;
            vResult = Avx.Subtract(vResult, Avx.Multiply(vXm1_2, Vector256.Create(0.5f)));
            vResult = Avx.Add(vResult, Avx.Multiply(vXm1_3, Vector256.Create(1.0f / 3.0f)));

            return vResult;
        }

        private Vector256<float> Log10ApproxVec(Vector256<float> vX)
        {
            // log10(x) = ln(x) / ln(10)
            var vLnX = LogApproxVec(vX);
            var vLn10 = Vector256.Create(2.302585f);
            return Avx.Divide(vLnX, vLn10);
        }

        #endregion
    }

    #region Configuration

    /// <summary>
    /// Configuration parameters for ORC simulation
    /// </summary>
    public class ORCConfiguration
    {
        // Cycle pressures
        public float EvaporatorPressure { get; set; } = 1.5e6f; // Pa (15 bar)
        public float CondenserPressure { get; set; } = 2.0e5f; // Pa (2 bar)

        // Temperatures
        public float CondenserTemperature { get; set; } = 303.15f; // K (30°C)
        public float SuperheatDegrees { get; set; } = 5.0f; // K
        public float MinPinchPointTemperature { get; set; } = 10.0f; // K

        // Component efficiencies
        public float TurbineEfficiency { get; set; } = 0.85f; // 85%
        public float PumpEfficiency { get; set; } = 0.75f; // 75%
        public float GeneratorEfficiency { get; set; } = 0.95f; // 95%

        // Heat exchanger parameters
        public float EvaporatorEffectiveness { get; set; } = 0.85f;
        public float CondenserEffectiveness { get; set; } = 0.90f;
        public float CondenserDeltaT { get; set; } = 5.0f; // K

        // Flow limits
        public float MaxORCMassFlowRate { get; set; } = 100.0f; // kg/s

        // Fluid properties
        public float GeothermalFluidCp { get; set; } = 4180.0f; // J/(kg·K) - water
        public float CoolingWaterCp { get; set; } = 4180.0f; // J/(kg·K)

        // Working fluid selection (from ORCFluidLibrary)
        public string FluidName { get; set; } = "R245fa";
    }

    #endregion
}
