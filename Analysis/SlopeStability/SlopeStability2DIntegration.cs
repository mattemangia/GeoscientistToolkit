// GeoscientistToolkit/Analysis/SlopeStability/SlopeStability2DIntegration.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.TwoDGeology;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// Integration utilities for creating 2D slope stability analyses from geological sections.
    /// </summary>
    public static class SlopeStability2DIntegration
    {
        /// <summary>
        /// Create a 2D slope stability dataset from a geological section.
        /// </summary>
        public static SlopeStability2DDataset CreateFromGeologicalSection(
            GeologicalSection section,
            string datasetName,
            float sectionThickness = 1.0f)
        {
            var dataset = new SlopeStability2DDataset
            {
                Name = datasetName,
                SectionThickness = sectionThickness
            };

            dataset.ImportFromGeologicalSection(section, sectionThickness);

            // Create default materials from lithologies
            var materialMap = CreateMaterialsFromLithologies(section, dataset);

            Logger.Log($"[SlopeStability2DIntegration] Created 2D dataset from section '{section.ProfileName}'");
            Logger.Log($"  Created {dataset.Materials.Count} materials from lithology types");

            return dataset;
        }

        /// <summary>
        /// Create a 2D slope stability dataset from a TwoDGeologyDataset.
        /// </summary>
        public static SlopeStability2DDataset CreateFromTwoDGeologyDataset(
            TwoDGeologyDataset geologyDataset,
            string datasetName,
            float sectionThickness = 1.0f)
        {
            if (geologyDataset.ProfileData == null)
            {
                Logger.LogError("[SlopeStability2DIntegration] TwoDGeologyDataset has no profile data");
                return null;
            }

            // Convert CrossSection to GeologicalSection
            var section = ConvertCrossSectionToGeologicalSection(geologyDataset.ProfileData);

            var dataset = CreateFromGeologicalSection(section, datasetName, sectionThickness);
            dataset.SourceGeologyDatasetPath = geologyDataset.FilePath;

            return dataset;
        }

        /// <summary>
        /// Create a 2D slope stability dataset from a borehole correlation profile.
        /// </summary>
        public static SlopeStability2DDataset CreateFromCorrelationProfile(
            CorrelationProfile profile,
            Dictionary<string, BoreholeDataset> boreholes,
            Dictionary<string, BoreholeHeader> headers,
            List<LithologyCorrelation> correlations,
            string datasetName,
            float sectionThickness = 1.0f,
            float verticalExaggeration = 1.0f)
        {
            // Generate geological section from correlation profile
            var section = GeologicalSectionGenerator.GenerateSection(
                profile, boreholes, headers, correlations, null, verticalExaggeration);

            var dataset = CreateFromGeologicalSection(section, datasetName, sectionThickness);
            dataset.SourceProfileID = profile.ID;

            Logger.Log($"[SlopeStability2DIntegration] Created 2D dataset from correlation profile '{profile.Name}'");
            Logger.Log($"  Profile length: {section.TotalLength:F1}m");
            Logger.Log($"  Borehole columns: {section.BoreholeColumns.Count}");

            return dataset;
        }

        /// <summary>
        /// Convert CrossSection to GeologicalSection format.
        /// </summary>
        private static GeologicalSection ConvertCrossSectionToGeologicalSection(
            GeoscientistToolkit.Business.GIS.GeologicalMapping.CrossSectionGenerator.CrossSection crossSection)
        {
            var section = new GeologicalSection
            {
                ProfileName = crossSection.Profile?.Name ?? "Cross Section",
                TotalLength = crossSection.Profile?.TotalDistance ?? 1000f,
                MinElevation = crossSection.Profile?.MinElevation ?? -100f,
                MaxElevation = crossSection.Profile?.MaxElevation ?? 100f,
                VerticalExaggeration = crossSection.VerticalExaggeration,
                BoreholeColumns = new List<SectionBoreholeColumn>(),
                FormationBoundaries = new List<SectionFormationBoundary>(),
                CorrelationLines = new List<SectionCorrelationLine>()
            };

            // Convert formations to boundaries
            if (crossSection.Formations != null)
            {
                foreach (var formation in crossSection.Formations)
                {
                    var boundary = new SectionFormationBoundary
                    {
                        HorizonName = formation.Name ?? "Formation",
                        LithologyType = formation.LithologyType ?? "Unknown",
                        Color = formation.Color,
                        Points = new List<Vector2>()
                    };

                    // Convert top boundary points
                    if (formation.TopBoundary != null)
                    {
                        boundary.Points = formation.TopBoundary.Select(p => new Vector2(p.X, p.Y)).ToList();
                    }

                    section.FormationBoundaries.Add(boundary);
                }
            }

            return section;
        }

        /// <summary>
        /// Create materials from lithology types in the section.
        /// </summary>
        private static Dictionary<string, int> CreateMaterialsFromLithologies(
            GeologicalSection section,
            SlopeStability2DDataset dataset)
        {
            var materialMap = new Dictionary<string, int>();
            int materialId = 0;

            // Collect unique lithology types
            var lithologyTypes = new HashSet<string>();

            foreach (var column in section.BoreholeColumns)
            {
                foreach (var unit in column.LithologyUnits)
                {
                    if (!string.IsNullOrEmpty(unit.LithologyType))
                        lithologyTypes.Add(unit.LithologyType);
                }
            }

            foreach (var boundary in section.FormationBoundaries)
            {
                if (!string.IsNullOrEmpty(boundary.LithologyType))
                    lithologyTypes.Add(boundary.LithologyType);
            }

            // Create materials for each lithology type
            foreach (var lithType in lithologyTypes)
            {
                var material = CreateMaterialFromLithologyType(lithType, materialId);
                dataset.Materials.Add(material);
                materialMap[lithType] = materialId;
                materialId++;
            }

            // Ensure at least one default material exists
            if (dataset.Materials.Count == 0)
            {
                dataset.Materials.Add(SlopeStabilityMaterial.CreatePreset(MaterialPreset.Sandstone));
                materialMap["default"] = 0;
            }

            return materialMap;
        }

        /// <summary>
        /// Create a material from a lithology type name.
        /// Attempts to match to appropriate material presets.
        /// </summary>
        private static SlopeStabilityMaterial CreateMaterialFromLithologyType(string lithologyType, int id)
        {
            // Try to match lithology type to material preset
            var lowerType = lithologyType.ToLowerInvariant();

            MaterialPreset preset = MaterialPreset.Sandstone; // Default

            if (lowerType.Contains("granite") || lowerType.Contains("igneous"))
                preset = MaterialPreset.Granite;
            else if (lowerType.Contains("limestone") || lowerType.Contains("carbonate"))
                preset = MaterialPreset.Limestone;
            else if (lowerType.Contains("sandstone") || lowerType.Contains("sand"))
                preset = MaterialPreset.Sandstone;
            else if (lowerType.Contains("shale") || lowerType.Contains("mudstone"))
                preset = MaterialPreset.Shale;
            else if (lowerType.Contains("clay") || lowerType.Contains("argillite"))
                preset = MaterialPreset.Clay;
            else if (lowerType.Contains("basalt") || lowerType.Contains("volcanic"))
                preset = MaterialPreset.Basalt;
            else if (lowerType.Contains("weather"))
                preset = MaterialPreset.WeatheredRock;

            var material = SlopeStabilityMaterial.CreatePreset(preset);
            material.Id = id;
            material.Name = lithologyType;

            return material;
        }

        /// <summary>
        /// Generate blocks from the dataset using default settings.
        /// </summary>
        public static void GenerateBlocks(
            SlopeStability2DDataset dataset,
            List<JointSet> jointSets = null)
        {
            if (dataset.GeologicalSection == null)
            {
                Logger.LogError("[SlopeStability2DIntegration] No geological section in dataset");
                return;
            }

            // Use provided joint sets or dataset joint sets
            var joints = jointSets ?? dataset.JointSets;

            if (joints.Count == 0)
            {
                Logger.LogWarning("[SlopeStability2DIntegration] No joint sets defined. Creating default joint sets.");
                joints = CreateDefaultJointSets();
                dataset.JointSets = joints;
            }

            // Generate blocks
            var blocks = BlockGenerator2D.GenerateBlocks(
                dataset.GeologicalSection,
                joints,
                dataset.BlockGenSettings);

            dataset.Blocks = blocks;

            Logger.Log($"[SlopeStability2DIntegration] Generated {blocks.Count} blocks");
        }

        /// <summary>
        /// Create default joint sets for initial testing.
        /// </summary>
        private static List<JointSet> CreateDefaultJointSets()
        {
            var jointSets = new List<JointSet>
            {
                // Bedding planes (sub-horizontal)
                new JointSet
                {
                    Id = 0,
                    Name = "Bedding",
                    Dip = 10f,
                    DipDirection = 90f,
                    Spacing = 2.0f,
                    NormalStiffness = 1e7f,
                    ShearStiffness = 1e6f,
                    FrictionAngle = 30f,
                    Cohesion = 10000f,
                    TensileStrength = 5000f
                },
                // Joint set 1 (steep)
                new JointSet
                {
                    Id = 1,
                    Name = "Joint Set 1",
                    Dip = 70f,
                    DipDirection = 270f,
                    Spacing = 3.0f,
                    NormalStiffness = 1e7f,
                    ShearStiffness = 1e6f,
                    FrictionAngle = 25f,
                    Cohesion = 5000f,
                    TensileStrength = 2000f
                }
            };

            return jointSets;
        }

        /// <summary>
        /// Run simulation with default parameters.
        /// </summary>
        public static SlopeStability2DResults RunSimulation(
            SlopeStability2DDataset dataset,
            Action<float> progressCallback = null)
        {
            if (dataset.Blocks == null || dataset.Blocks.Count == 0)
            {
                Logger.LogError("[SlopeStability2DIntegration] No blocks in dataset. Generate blocks first.");
                return null;
            }

            // Create and run simulator
            var simulator = new SlopeStability2DSimulator(dataset);
            var results = simulator.Run(progressCallback);

            // Store results
            dataset.Results = results;
            dataset.HasResults = true;

            return results;
        }
    }
}
