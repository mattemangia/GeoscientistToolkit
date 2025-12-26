// GeoscientistToolkit/Analysis/SlopeStability/SlopeStability2DDataset.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Borehole;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// Dataset for 2D slope stability analysis from geological cross-sections.
    /// Integrates with geology profiles, seismic lines, and borehole correlations.
    /// </summary>
    public class SlopeStability2DDataset : Dataset, ISerializableDataset
    {
        #region Properties

        // Source data
        public string SourceProfileID { get; set; }            // ID of correlation profile
        public string SourceGeologyDatasetPath { get; set; }   // Path to TwoDGeologyDataset
        public GeologicalSection GeologicalSection { get; set; }  // 2D geological section

        // 2D Geometry data
        public List<Block2D> Blocks { get; set; }
        public List<JointSet> JointSets { get; set; }
        public List<SlopeStabilityMaterial> Materials { get; set; }

        // Simulation configuration
        public SlopeStabilityParameters Parameters { get; set; }

        // 2D-specific parameters
        public float SectionThickness { get; set; }  // Depth into section (for mass calculation)
        public float VerticalExaggeration { get; set; }

        // Results
        public SlopeStability2DResults Results { get; set; }
        public bool HasResults { get; set; }

        // Block generation settings
        public BlockGeneration2DSettings BlockGenSettings { get; set; }

        #endregion

        #region Constructor

        public SlopeStability2DDataset() : base("2D Slope Stability Analysis", string.Empty)
        {
            Type = DatasetType.SlopeStability;
            Blocks = new List<Block2D>();
            JointSets = new List<JointSet>();
            Materials = new List<SlopeStabilityMaterial>();
            Parameters = new SlopeStabilityParameters();
            Results = null;
            HasResults = false;
            SourceProfileID = "";
            SourceGeologyDatasetPath = "";
            SectionThickness = 1.0f;  // 1m default
            VerticalExaggeration = 1.0f;
            BlockGenSettings = new BlockGeneration2DSettings();

            // Add default material
            Materials.Add(SlopeStabilityMaterial.CreatePreset(MaterialPreset.Granite));
        }

        public SlopeStability2DDataset(string name, string filePath) : base(name, filePath)
        {
            Type = DatasetType.SlopeStability;
            Blocks = new List<Block2D>();
            JointSets = new List<JointSet>();
            Materials = new List<SlopeStabilityMaterial>();
            Parameters = new SlopeStabilityParameters();
            Results = null;
            HasResults = false;
            SourceProfileID = "";
            SourceGeologyDatasetPath = "";
            SectionThickness = 1.0f;
            VerticalExaggeration = 1.0f;
            BlockGenSettings = new BlockGeneration2DSettings();

            Materials.Add(SlopeStabilityMaterial.CreatePreset(MaterialPreset.Granite));
        }

        #endregion

        #region Dataset Interface

        public override void Load()
        {
            if (!File.Exists(FilePath))
                throw new FileNotFoundException($"2D slope stability file not found: {FilePath}");

            var dto = JsonSerializer.Deserialize<SlopeStability2DDatasetDTO>(
                File.ReadAllText(FilePath));

            if (dto == null)
                throw new InvalidDataException("Failed to deserialize 2D slope stability data");

            FromDTO(dto);
        }

        public override void Unload()
        {
            Blocks?.Clear();
            JointSets?.Clear();
            Materials?.Clear();
            Results = null;
            HasResults = false;
            GeologicalSection = null;
        }

        public override long GetSizeInBytes()
        {
            long size = 0;

            // Blocks
            foreach (var block in Blocks)
            {
                size += block.Vertices.Count * 8; // Vector2 = 8 bytes
                size += 150; // Approximate overhead per block
            }

            // Joint sets
            size += JointSets.Count * 100;

            // Materials
            size += Materials.Count * 100;

            // Results
            if (HasResults && Results != null)
            {
                size += Results.BlockResults.Count * 80;
                size += Results.ContactResults.Count * 60;
                if (Results.HasTimeHistory)
                {
                    size += Results.TimeHistory.Count * Blocks.Count * 30;
                }
            }

            // Geological section
            if (GeologicalSection != null)
            {
                size += GeologicalSection.BoreholeColumns.Count * 500;
                size += GeologicalSection.FormationBoundaries.Count * 200;
            }

            return size;
        }

        #endregion

        #region Serialization

        public object ToSerializableObject()
        {
            return ToDTO();
        }

        public void Save(string path)
        {
            var dto = ToDTO();
            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(path, json);
            FilePath = path;
        }

        public SlopeStability2DDatasetDTO ToDTO()
        {
            return new SlopeStability2DDatasetDTO
            {
                TypeName = nameof(SlopeStability2DDataset),
                Name = this.Name,
                FilePath = this.FilePath,
                SourceProfileID = this.SourceProfileID,
                SourceGeologyDatasetPath = this.SourceGeologyDatasetPath,
                GeologicalSection = this.GeologicalSection,
                Blocks = this.Blocks,
                JointSets = this.JointSets,
                Materials = this.Materials,
                Parameters = this.Parameters,
                SectionThickness = this.SectionThickness,
                VerticalExaggeration = this.VerticalExaggeration,
                Results = this.Results,
                HasResults = this.HasResults,
                BlockGenSettings = this.BlockGenSettings
            };
        }

        public void FromDTO(SlopeStability2DDatasetDTO dto)
        {
            this.Name = dto.Name;
            this.FilePath = dto.FilePath;
            this.SourceProfileID = dto.SourceProfileID ?? "";
            this.SourceGeologyDatasetPath = dto.SourceGeologyDatasetPath ?? "";
            this.GeologicalSection = dto.GeologicalSection;
            this.Blocks = dto.Blocks ?? new List<Block2D>();
            this.JointSets = dto.JointSets ?? new List<JointSet>();
            this.Materials = dto.Materials ?? new List<SlopeStabilityMaterial>();
            this.Parameters = dto.Parameters ?? new SlopeStabilityParameters();
            this.SectionThickness = dto.SectionThickness;
            this.VerticalExaggeration = dto.VerticalExaggeration;
            this.Results = dto.Results;
            this.HasResults = dto.HasResults;
            this.BlockGenSettings = dto.BlockGenSettings ?? new BlockGeneration2DSettings();
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Gets a material by ID.
        /// </summary>
        public SlopeStabilityMaterial GetMaterial(int materialId)
        {
            return Materials.Find(m => m.Id == materialId);
        }

        /// <summary>
        /// Gets a joint set by ID.
        /// </summary>
        public JointSet GetJointSet(int jointSetId)
        {
            return JointSets.Find(j => j.Id == jointSetId);
        }

        /// <summary>
        /// Gets a block by ID.
        /// </summary>
        public Block2D GetBlock(int blockId)
        {
            return Blocks.Find(b => b.Id == blockId);
        }

        /// <summary>
        /// Import geological section from a correlation profile.
        /// </summary>
        public void ImportFromGeologicalSection(GeologicalSection section, float thickness = 1.0f)
        {
            this.GeologicalSection = section;
            this.SectionThickness = thickness;
            this.Name = $"2D Slope Stability - {section.ProfileName}";
            this.VerticalExaggeration = section.VerticalExaggeration;
        }

        #endregion
    }

    /// <summary>
    /// DTO for JSON serialization of 2D slope stability dataset.
    /// </summary>
    public class SlopeStability2DDatasetDTO : DatasetDTO
    {
        public string SourceProfileID { get; set; }
        public string SourceGeologyDatasetPath { get; set; }
        public GeologicalSection GeologicalSection { get; set; }
        public List<Block2D> Blocks { get; set; }
        public List<JointSet> JointSets { get; set; }
        public List<SlopeStabilityMaterial> Materials { get; set; }
        public SlopeStabilityParameters Parameters { get; set; }
        public float SectionThickness { get; set; }
        public float VerticalExaggeration { get; set; }
        public SlopeStability2DResults Results { get; set; }
        public bool HasResults { get; set; }
        public BlockGeneration2DSettings BlockGenSettings { get; set; }
    }

    /// <summary>
    /// Settings for 2D block generation from geological section.
    /// </summary>
    public class BlockGeneration2DSettings
    {
        public float MinimumBlockArea { get; set; }      // m²
        public float MaximumBlockArea { get; set; }      // m²
        public bool RemoveSmallBlocks { get; set; }
        public bool RemoveLargeBlocks { get; set; }
        public bool UseFormationBoundaries { get; set; }  // Use geological contacts as boundaries
        public bool UseTopography { get; set; }           // Use topography as upper boundary
        public float JointSpacingVariation { get; set; }  // Randomness in joint spacing (0-1)

        public BlockGeneration2DSettings()
        {
            MinimumBlockArea = 0.1f;  // 0.1 m²
            MaximumBlockArea = 100.0f; // 100 m²
            RemoveSmallBlocks = true;
            RemoveLargeBlocks = false;
            UseFormationBoundaries = true;
            UseTopography = true;
            JointSpacingVariation = 0.2f;
        }
    }

    /// <summary>
    /// Results from 2D slope stability simulation.
    /// </summary>
    public class SlopeStability2DResults
    {
        public List<Block2DResult> BlockResults { get; set; }
        public List<Contact2DResult> ContactResults { get; set; }
        public List<TimeStep2D> TimeHistory { get; set; }
        public bool HasTimeHistory { get; set; }

        public float TotalSimulationTime { get; set; }
        public int TotalIterations { get; set; }
        public bool Converged { get; set; }
        public float FinalKineticEnergy { get; set; }
        public float MaxDisplacement { get; set; }

        public DateTime SimulationDate { get; set; }
        public string Notes { get; set; }

        public SlopeStability2DResults()
        {
            BlockResults = new List<Block2DResult>();
            ContactResults = new List<Contact2DResult>();
            TimeHistory = new List<TimeStep2D>();
            HasTimeHistory = false;
            SimulationDate = DateTime.Now;
            Notes = "";
        }
    }

    /// <summary>
    /// Result data for a single 2D block.
    /// </summary>
    public class Block2DResult
    {
        public int BlockId { get; set; }
        public Vector2 FinalPosition { get; set; }
        public Vector2 TotalDisplacement { get; set; }
        public float MaxDisplacement { get; set; }
        public float FinalRotation { get; set; }
        public Vector2 FinalVelocity { get; set; }
        public bool HasFailed { get; set; }
        public float SafetyFactor { get; set; }
        public float MaxStress { get; set; }  // von Mises or principal stress
    }

    /// <summary>
    /// Result data for a contact between 2D blocks.
    /// </summary>
    public class Contact2DResult
    {
        public int Block1Id { get; set; }
        public int Block2Id { get; set; }
        public Vector2 ContactPoint { get; set; }
        public Vector2 ContactNormal { get; set; }
        public float NormalForce { get; set; }
        public float ShearForce { get; set; }
        public bool IsSliding { get; set; }
    }

    /// <summary>
    /// Time step snapshot for 2D simulation.
    /// </summary>
    public class TimeStep2D
    {
        public float Time { get; set; }
        public List<(int blockId, Vector2 position, float rotation)> BlockStates { get; set; }
        public float TotalKineticEnergy { get; set; }

        public TimeStep2D()
        {
            BlockStates = new List<(int, Vector2, float)>();
        }
    }
}
