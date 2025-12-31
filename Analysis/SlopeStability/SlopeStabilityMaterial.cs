using System;
using System.Numerics;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// Material properties for slope stability analysis.
    /// Represents intact rock or soil properties for blocks.
    /// </summary>
    public class SlopeStabilityMaterial
    {
        // Identification
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        // Physical properties
        public float Density { get; set; }              // kg/m³

        // Elastic properties (for block deformation if needed)
        public float YoungModulus { get; set; }         // Pa
        public float PoissonRatio { get; set; }         // dimensionless
        public float Restitution { get; set; }          // Coeff. of Restitution (0-1)

        // Strength properties (Mohr-Coulomb)
        public float Cohesion { get; set; }             // Pa
        public float FrictionAngle { get; set; }        // degrees
        public float TensileStrength { get; set; }      // Pa
        public float CompressiveStrength { get; set; }  // Pa (UCS)

        // Dilatancy
        public float DilationAngle { get; set; }        // degrees

        // Permeability (for fluid flow if implemented)
        public float Permeability { get; set; }         // m²
        public float Porosity { get; set; }             // dimensionless (0-1)

        // Weathering/degradation
        public float WeatheringFactor { get; set; }     // 0-1, affects strength

        // Constitutive model
        public ConstitutiveModel ConstitutiveModel { get; set; }

        // Visualization
        public Vector4 Color { get; set; }

        public SlopeStabilityMaterial()
        {
            Name = "Rock";
            Description = "";
            Density = 2700.0f;              // Typical rock: 2700 kg/m³
            YoungModulus = 50e9f;           // 50 GPa
            PoissonRatio = 0.25f;
            Restitution = 0.5f;             // Default semi-elastic
            Cohesion = 1e6f;                // 1 MPa
            FrictionAngle = 35.0f;
            TensileStrength = 0.5e6f;       // 0.5 MPa
            CompressiveStrength = 100e6f;   // 100 MPa
            DilationAngle = 5.0f;
            Permeability = 1e-15f;          // Low permeability
            Porosity = 0.05f;               // 5%
            WeatheringFactor = 1.0f;        // No weathering
            Color = new Vector4(0.6f, 0.6f, 0.6f, 1.0f);

            // Initialize constitutive model with material properties
            ConstitutiveModel = new ConstitutiveModel
            {
                ModelType = ConstitutiveModelType.Elastic,
                FailureCriterion = FailureCriterionType.MohrCoulomb,
                YoungModulus = YoungModulus,
                PoissonRatio = PoissonRatio,
                Cohesion = Cohesion,
                FrictionAngle = FrictionAngle,
                TensileStrength = TensileStrength,
                DilationAngle = DilationAngle
            };
            ConstitutiveModel.UpdateDerivedProperties();
        }

        /// <summary>
        /// Creates a preset material for common rock types.
        /// </summary>
        public static SlopeStabilityMaterial CreatePreset(MaterialPreset preset)
        {
            var material = new SlopeStabilityMaterial();

            switch (preset)
            {
                case MaterialPreset.Granite:
                    material.Name = "Granite";
                    material.Density = 2700f;
                    material.YoungModulus = 70e9f;
                    material.PoissonRatio = 0.25f;
                    material.Cohesion = 15e6f;
                    material.FrictionAngle = 50.0f;
                    material.TensileStrength = 7e6f;
                    material.CompressiveStrength = 200e6f;
                    material.Color = new Vector4(0.8f, 0.7f, 0.7f, 1.0f);
                    break;

                case MaterialPreset.Limestone:
                    material.Name = "Limestone";
                    material.Density = 2600f;
                    material.YoungModulus = 50e9f;
                    material.PoissonRatio = 0.28f;
                    material.Cohesion = 10e6f;
                    material.FrictionAngle = 40.0f;
                    material.TensileStrength = 4e6f;
                    material.CompressiveStrength = 120e6f;
                    material.Color = new Vector4(0.9f, 0.9f, 0.8f, 1.0f);
                    break;

                case MaterialPreset.Sandstone:
                    material.Name = "Sandstone";
                    material.Density = 2300f;
                    material.YoungModulus = 20e9f;
                    material.PoissonRatio = 0.30f;
                    material.Cohesion = 5e6f;
                    material.FrictionAngle = 35.0f;
                    material.TensileStrength = 2e6f;
                    material.CompressiveStrength = 80e6f;
                    material.Porosity = 0.15f;
                    material.Color = new Vector4(0.9f, 0.8f, 0.6f, 1.0f);
                    break;

                case MaterialPreset.Shale:
                    material.Name = "Shale";
                    material.Density = 2400f;
                    material.YoungModulus = 15e9f;
                    material.PoissonRatio = 0.32f;
                    material.Cohesion = 3e6f;
                    material.FrictionAngle = 25.0f;
                    material.TensileStrength = 1e6f;
                    material.CompressiveStrength = 50e6f;
                    material.Color = new Vector4(0.5f, 0.5f, 0.5f, 1.0f);
                    break;

                case MaterialPreset.Clay:
                    material.Name = "Clay";
                    material.Density = 2000f;
                    material.YoungModulus = 5e6f;
                    material.PoissonRatio = 0.35f;
                    material.Cohesion = 20e3f;
                    material.FrictionAngle = 18.0f;
                    material.TensileStrength = 0f;
                    material.CompressiveStrength = 200e3f;
                    material.Porosity = 0.40f;
                    material.Color = new Vector4(0.6f, 0.4f, 0.3f, 1.0f);
                    break;

                case MaterialPreset.Sand:
                    material.Name = "Sand";
                    material.Density = 1800f;
                    material.YoungModulus = 30e6f;
                    material.PoissonRatio = 0.30f;
                    material.Cohesion = 0f;
                    material.FrictionAngle = 30.0f;
                    material.TensileStrength = 0f;
                    material.CompressiveStrength = 0f;
                    material.Porosity = 0.35f;
                    material.Color = new Vector4(0.9f, 0.85f, 0.6f, 1.0f);
                    break;

                case MaterialPreset.WeatheredRock:
                    material.Name = "Weathered Rock";
                    material.Density = 2400f;
                    material.YoungModulus = 10e9f;
                    material.PoissonRatio = 0.28f;
                    material.Cohesion = 2e6f;
                    material.FrictionAngle = 30.0f;
                    material.TensileStrength = 0.5e6f;
                    material.CompressiveStrength = 40e6f;
                    material.WeatheringFactor = 0.5f;
                    material.Color = new Vector4(0.7f, 0.6f, 0.5f, 1.0f);
                    break;

                case MaterialPreset.Basalt:
                    material.Name = "Basalt";
                    material.Density = 2900f;
                    material.YoungModulus = 80e9f;
                    material.PoissonRatio = 0.25f;
                    material.Cohesion = 20e6f;
                    material.FrictionAngle = 55.0f;
                    material.TensileStrength = 10e6f;
                    material.CompressiveStrength = 250e6f;
                    material.Color = new Vector4(0.3f, 0.3f, 0.3f, 1.0f);
                    break;
            }

            return material;
        }

        /// <summary>
        /// Gets the effective properties accounting for weathering.
        /// </summary>
        public (float cohesion, float friction, float tensile) GetEffectiveStrength()
        {
            float factor = WeatheringFactor;
            return (
                Cohesion * factor,
                FrictionAngle,  // Friction angle typically doesn't change much with weathering
                TensileStrength * factor
            );
        }
    }

    /// <summary>
    /// Preset material types for quick setup.
    /// </summary>
    public enum MaterialPreset
    {
        Granite,
        Limestone,
        Sandstone,
        Shale,
        Clay,
        Sand,
        WeatheredRock,
        Basalt
    }
}
