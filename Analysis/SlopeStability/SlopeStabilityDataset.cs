using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GeoscientistToolkit.Data;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// Dataset for slope stability analysis projects.
    /// Contains blocks, joint sets, materials, parameters, and results.
    /// </summary>
    public class SlopeStabilityDataset : Dataset, ISerializableDataset
    {
        // Geometry data
        public List<Block> Blocks { get; set; }
        public List<JointSet> JointSets { get; set; }
        public List<SlopeStabilityMaterial> Materials { get; set; }

        // Simulation configuration
        public SlopeStabilityParameters Parameters { get; set; }

        // Results
        public SlopeStabilityResults Results { get; set; }
        public bool HasResults { get; set; }

        // Original mesh (if imported from Mesh3D)
        public string SourceMeshPath { get; set; }

        // Block generation settings
        public BlockGenerationSettings BlockGenSettings { get; set; }

        public SlopeStabilityDataset()
        {
            Type = DatasetType.SlopeStability;
            Name = "Slope Stability Analysis";
            Blocks = new List<Block>();
            JointSets = new List<JointSet>();
            Materials = new List<SlopeStabilityMaterial>();
            Parameters = new SlopeStabilityParameters();
            Results = null;
            HasResults = false;
            SourceMeshPath = "";
            BlockGenSettings = new BlockGenerationSettings();

            // Add default material
            Materials.Add(SlopeStabilityMaterial.CreatePreset(MaterialPreset.Granite));
        }

        public override void Load()
        {
            if (!File.Exists(FilePath))
                throw new FileNotFoundException($"Slope stability file not found: {FilePath}");

            var dto = JsonSerializer.Deserialize<SlopeStabilityDatasetDTO>(
                File.ReadAllText(FilePath));

            if (dto == null)
                throw new InvalidDataException("Failed to deserialize slope stability data");

            // Deserialize DTO to dataset
            FromDTO(dto);
        }

        public override void Unload()
        {
            Blocks?.Clear();
            JointSets?.Clear();
            Materials?.Clear();
            Results = null;
            HasResults = false;
        }

        public override long GetSizeInBytes()
        {
            long size = 0;

            // Blocks
            foreach (var block in Blocks)
            {
                size += block.Vertices.Count * 12; // Vector3 = 12 bytes
                size += block.Faces.Count * 4 * 4; // Assume quad faces
                size += 200; // Approximate overhead per block
            }

            // Joint sets
            size += JointSets.Count * 100;

            // Materials
            size += Materials.Count * 100;

            // Results
            if (HasResults && Results != null)
            {
                size += Results.BlockResults.Count * 100;
                size += Results.ContactResults.Count * 80;
                if (Results.HasTimeHistory)
                {
                    size += Results.TimeHistory.Count * Blocks.Count * 40;
                }
            }

            return size;
        }

        public void Save(string path)
        {
            var dto = ToDTO();
            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(path, json);
        }

        public SlopeStabilityDatasetDTO ToDTO()
        {
            return new SlopeStabilityDatasetDTO
            {
                Name = this.Name,
                FilePath = this.FilePath,
                Blocks = this.Blocks,
                JointSets = this.JointSets,
                Materials = this.Materials,
                Parameters = this.Parameters,
                Results = this.Results,
                HasResults = this.HasResults,
                SourceMeshPath = this.SourceMeshPath,
                BlockGenSettings = this.BlockGenSettings
            };
        }

        public void FromDTO(SlopeStabilityDatasetDTO dto)
        {
            this.Name = dto.Name;
            this.FilePath = dto.FilePath;
            this.Blocks = dto.Blocks ?? new List<Block>();
            this.JointSets = dto.JointSets ?? new List<JointSet>();
            this.Materials = dto.Materials ?? new List<SlopeStabilityMaterial>();
            this.Parameters = dto.Parameters ?? new SlopeStabilityParameters();
            this.Results = dto.Results;
            this.HasResults = dto.HasResults;
            this.SourceMeshPath = dto.SourceMeshPath ?? "";
            this.BlockGenSettings = dto.BlockGenSettings ?? new BlockGenerationSettings();
        }

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
        public Block GetBlock(int blockId)
        {
            return Blocks.Find(b => b.Id == blockId);
        }
    }

    /// <summary>
    /// DTO for JSON serialization.
    /// </summary>
    public class SlopeStabilityDatasetDTO
    {
        public string Name { get; set; }
        public string FilePath { get; set; }
        public List<Block> Blocks { get; set; }
        public List<JointSet> JointSets { get; set; }
        public List<SlopeStabilityMaterial> Materials { get; set; }
        public SlopeStabilityParameters Parameters { get; set; }
        public SlopeStabilityResults Results { get; set; }
        public bool HasResults { get; set; }
        public string SourceMeshPath { get; set; }
        public BlockGenerationSettings BlockGenSettings { get; set; }
    }

    /// <summary>
    /// Settings for block generation algorithm.
    /// </summary>
    public class BlockGenerationSettings
    {
        public float TargetBlockSize { get; set; }         // meters
        public float BlockSizeTolerance { get; set; }      // ±%
        public bool UseVoronoiSeeds { get; set; }
        public int NumVoronoiSeeds { get; set; }
        public bool RespectMeshBoundaries { get; set; }
        public float MinimumBlockVolume { get; set; }      // m³
        public float MaximumBlockVolume { get; set; }      // m³
        public bool RemoveSmallBlocks { get; set; }
        public bool RemoveLargeBlocks { get; set; }
        public bool MergeSliverBlocks { get; set; }

        public BlockGenerationSettings()
        {
            TargetBlockSize = 1.0f;
            BlockSizeTolerance = 0.5f;
            UseVoronoiSeeds = false;
            NumVoronoiSeeds = 100;
            RespectMeshBoundaries = true;
            MinimumBlockVolume = 0.001f;  // 1 liter
            MaximumBlockVolume = 1000.0f; // 1000 m³
            RemoveSmallBlocks = true;
            RemoveLargeBlocks = false;
            MergeSliverBlocks = true;
        }
    }
}
