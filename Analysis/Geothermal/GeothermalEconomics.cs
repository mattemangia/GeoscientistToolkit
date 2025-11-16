using System;
using System.Collections.Generic;
using System.Linq;

namespace GeoscientistToolkit.Analysis.Geothermal
{
    /// <summary>
    /// Economic analysis for geothermal power generation with ORC
    /// Includes NPV, IRR, LCOE, and sensitivity analysis
    /// </summary>
    public class GeothermalEconomics
    {
        public EconomicParameters Parameters { get; set; }

        public GeothermalEconomics()
        {
            Parameters = new EconomicParameters();
        }

        #region Economic Analysis

        /// <summary>
        /// Calculate comprehensive economic metrics for a geothermal ORC project
        /// </summary>
        public EconomicResults CalculateEconomics(ORCSimulation.ORCCycleResults[] orcResults, int operatingYears = 30)
        {
            var results = new EconomicResults
            {
                ProjectLifetimeYears = operatingYears
            };

            // Calculate average annual power generation
            float avgNetPowerW = orcResults.Average(r => r.NetPower);
            float avgNetPowerMW = avgNetPowerW / 1e6f;

            // Annual energy production (MWh)
            float capacityFactor = Parameters.CapacityFactor;
            float annualEnergyMWh = avgNetPowerMW * 8760.0f * capacityFactor; // hours per year

            results.AverageNetPowerMW = avgNetPowerMW;
            results.AnnualEnergyProductionMWh = annualEnergyMWh;

            // Capital costs
            float drillingCosts = CalculateDrillingCosts();
            float powerPlantCosts = CalculatePowerPlantCosts(avgNetPowerMW);
            float infrastructureCosts = CalculateInfrastructureCosts(avgNetPowerMW);
            float totalCapex = drillingCosts + powerPlantCosts + infrastructureCosts;

            results.DrillingCostsMUSD = drillingCosts;
            results.PowerPlantCostsMUSD = powerPlantCosts;
            results.InfrastructureCostsMUSD = infrastructureCosts;
            results.TotalCapitalCostMUSD = totalCapex;

            // Operating costs
            float annualOpex = CalculateAnnualOperatingCosts(avgNetPowerMW, annualEnergyMWh);
            results.AnnualOperatingCostMUSD = annualOpex;

            // Revenue
            float annualRevenue = annualEnergyMWh * Parameters.ElectricityPrice;
            results.AnnualRevenueMUSD = annualRevenue;

            // Cash flow analysis
            var cashFlows = new List<float>();
            cashFlows.Add(-totalCapex); // Year 0: Capital investment

            for (int year = 1; year <= operatingYears; year++)
            {
                float degradationFactor = MathF.Pow(1.0f - Parameters.AnnualDegradation, year);
                float yearRevenue = annualRevenue * degradationFactor;
                float yearOpex = annualOpex * MathF.Pow(1.0f + Parameters.InflationRate, year - 1);
                float netCashFlow = yearRevenue - yearOpex;

                cashFlows.Add(netCashFlow);
            }

            results.CashFlows = cashFlows.ToArray();

            // NPV calculation
            results.NPV_MUSD = CalculateNPV(cashFlows.ToArray(), Parameters.DiscountRate);

            // IRR calculation
            results.IRR = CalculateIRR(cashFlows.ToArray());

            // Payback period
            results.PaybackPeriodYears = CalculatePaybackPeriod(cashFlows.ToArray());

            // LCOE (Levelized Cost of Electricity)
            results.LCOE_USDperMWh = CalculateLCOE(totalCapex, annualOpex, annualEnergyMWh, operatingYears);

            // Profitability metrics
            results.ROI = (results.NPV_MUSD / totalCapex) * 100.0f; // %
            results.BenefitCostRatio = CalculateBenefitCostRatio(cashFlows.ToArray(), Parameters.DiscountRate);

            // Sensitivity analysis
            results.SensitivityAnalysis = PerformSensitivityAnalysis(avgNetPowerMW, annualEnergyMWh, totalCapex, annualOpex, operatingYears);

            return results;
        }

        #endregion

        #region Cost Calculations

        private float CalculateDrillingCosts()
        {
            // Drilling cost model: cost per meter * depth + fixed costs per well
            float costPerMeter = Parameters.DrillingCostPerMeter; // $/m
            float depthM = Parameters.WellDepthMeters;
            float numWells = Parameters.NumberOfWells;
            float fixedCostPerWell = Parameters.FixedCostPerWell; // $

            // Non-linear depth factor (deeper = more expensive per meter)
            float depthFactor = 1.0f + (depthM / 1000.0f) * 0.15f;

            float totalDrillingCost = numWells * (costPerMeter * depthM * depthFactor + fixedCostPerWell);

            return totalDrillingCost / 1e6f; // Convert to MUSD
        }

        private float CalculatePowerPlantCosts(float powerMW)
        {
            // ORC power plant cost model (economy of scale)
            // Typical: $2000-4000 per kW for small plants, $1500-2500 for larger
            float baseSpecificCost = Parameters.PowerPlantSpecificCost; // $/kW
            float powerKW = powerMW * 1000.0f;

            // Economy of scale factor
            float scaleFactor = MathF.Pow(powerKW / 1000.0f, -0.15f); // Larger plants cheaper per kW
            scaleFactor = Math.Max(0.7f, Math.Min(1.3f, scaleFactor)); // Clamp

            float powerPlantCost = powerKW * baseSpecificCost * scaleFactor;

            // Add heat exchangers, pumps, cooling system
            float auxiliaryCost = powerPlantCost * 0.25f;

            return (powerPlantCost + auxiliaryCost) / 1e6f; // MUSD
        }

        private float CalculateInfrastructureCosts(float powerMW)
        {
            // Grid connection, pipelines, buildings, site preparation
            float gridConnectionCost = Parameters.GridConnectionCost * powerMW; // $
            float pipelineCost = Parameters.PipelineCostPerMeter * Parameters.PipelineLengthMeters; // $
            float sitePrepCost = Parameters.SitePreparationCost; // $
            float buildingsCost = 2.0e6f; // Control room, maintenance facility

            return (gridConnectionCost + pipelineCost + sitePrepCost + buildingsCost) / 1e6f; // MUSD
        }

        private float CalculateAnnualOperatingCosts(float powerMW, float annualEnergyMWh)
        {
            // O&M costs
            float fixedOM = Parameters.FixedOMCostPerMW * powerMW; // $/year
            float variableOM = Parameters.VariableOMCostPerMWh * annualEnergyMWh; // $/year

            // Labor costs
            float laborCost = Parameters.AnnualLaborCost;

            // Insurance, taxes, royalties
            float insurance = Parameters.InsuranceRate * powerMW * 1e6f; // % of plant value
            float propertyTax = Parameters.PropertyTaxRate * powerMW * 1e6f;

            // Geothermal royalties (if applicable)
            float royalties = Parameters.GeothermalRoyaltyRate * annualEnergyMWh * Parameters.ElectricityPrice;

            float totalOpex = fixedOM + variableOM + laborCost + insurance + propertyTax + royalties;

            return totalOpex / 1e6f; // MUSD
        }

        #endregion

        #region Financial Metrics

        private float CalculateNPV(float[] cashFlows, float discountRate)
        {
            float npv = 0.0f;
            for (int year = 0; year < cashFlows.Length; year++)
            {
                npv += cashFlows[year] / MathF.Pow(1.0f + discountRate, year);
            }
            return npv;
        }

        private float CalculateIRR(float[] cashFlows)
        {
            // Newton-Raphson method to find IRR (rate where NPV = 0)
            float irr = 0.1f; // Initial guess: 10%
            const int maxIterations = 100;
            const float tolerance = 1e-6f;

            for (int iter = 0; iter < maxIterations; iter++)
            {
                float npv = 0.0f;
                float dnpv = 0.0f; // Derivative

                for (int year = 0; year < cashFlows.Length; year++)
                {
                    float discountFactor = MathF.Pow(1.0f + irr, year);
                    npv += cashFlows[year] / discountFactor;
                    dnpv -= year * cashFlows[year] / (discountFactor * (1.0f + irr));
                }

                if (MathF.Abs(npv) < tolerance)
                {
                    return irr * 100.0f; // Return as percentage
                }

                // Newton-Raphson update
                irr = irr - npv / dnpv;

                // Bounds check
                if (irr < -0.99f || irr > 10.0f)
                {
                    return float.NaN; // IRR doesn't exist or is unreasonable
                }
            }

            return float.NaN; // Did not converge
        }

        private float CalculatePaybackPeriod(float[] cashFlows)
        {
            float cumulativeCashFlow = cashFlows[0]; // Initial investment (negative)

            for (int year = 1; year < cashFlows.Length; year++)
            {
                cumulativeCashFlow += cashFlows[year];

                if (cumulativeCashFlow >= 0.0f)
                {
                    // Interpolate to find exact payback time
                    float previousCumulative = cumulativeCashFlow - cashFlows[year];
                    float fraction = -previousCumulative / cashFlows[year];
                    return year - 1 + fraction;
                }
            }

            return float.PositiveInfinity; // Never pays back
        }

        private float CalculateLCOE(float capex, float annualOpex, float annualEnergyMWh, int years)
        {
            // LCOE = (Sum of discounted costs) / (Sum of discounted energy production)
            float discountRate = Parameters.DiscountRate;

            float discountedCosts = capex; // Year 0
            float discountedEnergy = 0.0f;

            for (int year = 1; year <= years; year++)
            {
                float discountFactor = MathF.Pow(1.0f + discountRate, year);
                float degradationFactor = MathF.Pow(1.0f - Parameters.AnnualDegradation, year);

                discountedCosts += annualOpex / discountFactor;
                discountedEnergy += (annualEnergyMWh * degradationFactor) / discountFactor;
            }

            return discountedCosts / discountedEnergy * 1e6f; // USD/MWh
        }

        private float CalculateBenefitCostRatio(float[] cashFlows, float discountRate)
        {
            float discountedBenefits = 0.0f;
            float discountedCosts = -cashFlows[0]; // Initial investment

            for (int year = 1; year < cashFlows.Length; year++)
            {
                float discountFactor = MathF.Pow(1.0f + discountRate, year);
                if (cashFlows[year] > 0)
                    discountedBenefits += cashFlows[year] / discountFactor;
                else
                    discountedCosts += -cashFlows[year] / discountFactor;
            }

            return discountedBenefits / discountedCosts;
        }

        #endregion

        #region Sensitivity Analysis

        private SensitivityResults PerformSensitivityAnalysis(float powerMW, float energyMWh, float capex, float opex, int years)
        {
            var sensitivity = new SensitivityResults();

            // Vary electricity price (-30% to +30%)
            sensitivity.ElectricityPriceVariation = VariyParameter(
                -0.3f, 0.3f, 7,
                factor => {
                    float newPrice = Parameters.ElectricityPrice * (1.0f + factor);
                    return CalculateNPVForPrice(newPrice, energyMWh, capex, opex, years);
                }
            );

            // Vary capital cost (-20% to +50%)
            sensitivity.CapitalCostVariation = VariyParameter(
                -0.2f, 0.5f, 8,
                factor => {
                    float newCapex = capex * (1.0f + factor);
                    return CalculateNPVForCapex(newCapex, opex, energyMWh, years);
                }
            );

            // Vary capacity factor (-15% to +10%)
            sensitivity.CapacityFactorVariation = VariyParameter(
                -0.15f, 0.10f, 6,
                factor => {
                    float newEnergy = energyMWh * (1.0f + factor);
                    return CalculateNPVForEnergy(newEnergy, capex, opex, years);
                }
            );

            // Vary discount rate (3% to 12%)
            sensitivity.DiscountRateVariation = new List<(float parameter, float npv)>();
            float annualRevenue = energyMWh * Parameters.ElectricityPrice;
            for (float rate = 0.03f; rate <= 0.12f; rate += 0.01f)
            {
                var cashFlows = BuildCashFlows(capex, opex, annualRevenue, years);
                float npv = CalculateNPV(cashFlows, rate);
                sensitivity.DiscountRateVariation.Add((rate * 100.0f, npv));
            }

            return sensitivity;
        }

        private List<(float factor, float npv)> VariyParameter(float minFactor, float maxFactor, int steps, Func<float, float> npvCalculator)
        {
            var results = new List<(float factor, float npv)>();
            float stepSize = (maxFactor - minFactor) / (steps - 1);

            for (int i = 0; i < steps; i++)
            {
                float factor = minFactor + i * stepSize;
                float npv = npvCalculator(factor);
                results.Add((factor * 100.0f, npv)); // Factor as percentage
            }

            return results;
        }

        private float CalculateNPVForPrice(float electricityPrice, float energyMWh, float capex, float opex, int years)
        {
            float annualRevenue = energyMWh * electricityPrice;
            var cashFlows = BuildCashFlows(capex, opex, annualRevenue, years);
            return CalculateNPV(cashFlows, Parameters.DiscountRate);
        }

        private float CalculateNPVForCapex(float capex, float opex, float energyMWh, int years)
        {
            float annualRevenue = energyMWh * Parameters.ElectricityPrice;
            var cashFlows = BuildCashFlows(capex, opex, annualRevenue, years);
            return CalculateNPV(cashFlows, Parameters.DiscountRate);
        }

        private float CalculateNPVForEnergy(float energyMWh, float capex, float opex, int years)
        {
            float annualRevenue = energyMWh * Parameters.ElectricityPrice;
            var cashFlows = BuildCashFlows(capex, opex, annualRevenue, years);
            return CalculateNPV(cashFlows, Parameters.DiscountRate);
        }

        private float[] BuildCashFlows(float capex, float opex, float annualRevenue, int years)
        {
            var cashFlows = new float[years + 1];
            cashFlows[0] = -capex;

            for (int year = 1; year <= years; year++)
            {
                float degradationFactor = MathF.Pow(1.0f - Parameters.AnnualDegradation, year);
                float yearRevenue = annualRevenue * degradationFactor;
                float yearOpex = opex * MathF.Pow(1.0f + Parameters.InflationRate, year - 1);
                cashFlows[year] = yearRevenue - yearOpex;
            }

            return cashFlows;
        }

        #endregion
    }

    #region Data Structures

    public class EconomicParameters
    {
        // Project parameters
        public int NumberOfWells { get; set; } = 2; // Production + injection
        public float WellDepthMeters { get; set; } = 2000.0f;
        public float CapacityFactor { get; set; } = 0.90f; // 90% uptime

        // Cost parameters
        public float DrillingCostPerMeter { get; set; } = 1500.0f; // $/m
        public float FixedCostPerWell { get; set; } = 2.0e6f; // $ (mobilization, etc.)
        public float PowerPlantSpecificCost { get; set; } = 3000.0f; // $/kW
        public float GridConnectionCost { get; set; } = 500000.0f; // $/MW
        public float PipelineCostPerMeter { get; set; } = 500.0f; // $/m
        public float PipelineLengthMeters { get; set; } = 1000.0f;
        public float SitePreparationCost { get; set; } = 1.0e6f; // $

        // Operating costs
        public float FixedOMCostPerMW { get; set; } = 120000.0f; // $/MW/year
        public float VariableOMCostPerMWh { get; set; } = 5.0f; // $/MWh
        public float AnnualLaborCost { get; set; } = 500000.0f; // $/year
        public float InsuranceRate { get; set; } = 0.005f; // 0.5% of plant value
        public float PropertyTaxRate { get; set; } = 0.01f; // 1% of plant value
        public float GeothermalRoyaltyRate { get; set; } = 0.05f; // 5% of revenue

        // Revenue parameters
        public float ElectricityPrice { get; set; } = 80.0f; // $/MWh (wholesale)

        // Financial parameters
        public float DiscountRate { get; set; } = 0.08f; // 8% WACC
        public float InflationRate { get; set; } = 0.02f; // 2% annual
        public float AnnualDegradation { get; set; } = 0.005f; // 0.5% per year (reservoir cooling)
    }

    public class EconomicResults
    {
        public int ProjectLifetimeYears { get; set; }

        // Technical results
        public float AverageNetPowerMW { get; set; }
        public float AnnualEnergyProductionMWh { get; set; }

        // Cost breakdown
        public float DrillingCostsMUSD { get; set; }
        public float PowerPlantCostsMUSD { get; set; }
        public float InfrastructureCostsMUSD { get; set; }
        public float TotalCapitalCostMUSD { get; set; }
        public float AnnualOperatingCostMUSD { get; set; }

        // Revenue
        public float AnnualRevenueMUSD { get; set; }

        // Cash flows
        public float[] CashFlows { get; set; }

        // Financial metrics
        public float NPV_MUSD { get; set; }
        public float IRR { get; set; } // %
        public float PaybackPeriodYears { get; set; }
        public float LCOE_USDperMWh { get; set; }
        public float ROI { get; set; } // %
        public float BenefitCostRatio { get; set; }

        // Sensitivity analysis
        public SensitivityResults SensitivityAnalysis { get; set; }
    }

    public class SensitivityResults
    {
        public List<(float factorPercent, float npv)> ElectricityPriceVariation { get; set; }
        public List<(float factorPercent, float npv)> CapitalCostVariation { get; set; }
        public List<(float factorPercent, float npv)> CapacityFactorVariation { get; set; }
        public List<(float discountRatePercent, float npv)> DiscountRateVariation { get; set; }
    }

    #endregion
}
