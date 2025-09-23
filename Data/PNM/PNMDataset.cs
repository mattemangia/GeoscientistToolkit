// GeoscientistToolkit/Data/Pnm/PNMDataset.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using GeoscientistToolkit.Business;

namespace GeoscientistToolkit.Data.Pnm
{
    public class Pore
    {
        public int ID { get; set; }
        public Vector3 Position { get; set; }
        public float Area { get; set; }
        public float VolumeVoxels { get; set; }
        public float VolumePhysical { get; set; }
        public int Connections { get; set; }
        public float Radius { get; set; }
    }

    public class Throat
    {
        public int ID { get; set; }
        public int Pore1ID { get; set; }
        public int Pore2ID { get; set; }
        public float Radius { get; set; }
    }

    public class PNMDataset : Dataset
    {
        // --- Properties ---
        public float VoxelSize { get; set; } // in µm
        public float Tortuosity { get; set; }
        public float DarcyPermeability { get; set; } // in mD
        public float NavierStokesPermeability { get; set; } // in mD
        public float LatticeBoltzmannPermeability { get; set; } // in mD

        public List<Pore> Pores { get; set; } = new List<Pore>();
        public List<Throat> Throats { get; set; } = new List<Throat>();

        // --- Min/Max values for visualization scaling ---
        public float MinPoreRadius { get; private set; }
        public float MaxPoreRadius { get; private set; }
        public float MinThroatRadius { get; private set; }
        public float MaxThroatRadius { get; private set; }

        public PNMDataset(string name, string filePath) : base(name, filePath)
        {
            Type = DatasetType.PNM;
        }

        public override long GetSizeInBytes()
        {
            if (File.Exists(FilePath))
            {
                return new FileInfo(FilePath).Length;
            }
            return 0;
        }

        public override void Load()
        {
            // Data is loaded via PNMLoader, this method is for post-load processing
            CalculateBounds();
        }

        public override void Unload()
        {
            // Data is held in memory, clear lists to free it
            Pores.Clear();
            Throats.Clear();
        }

        /// <summary>
        /// Calculates min/max values for various properties for scaling in the viewer.
        /// </summary>
        public void CalculateBounds()
        {
            if (Pores.Any())
            {
                MinPoreRadius = Pores.Min(p => p.Radius);
                MaxPoreRadius = Pores.Max(p => p.Radius);
            }
            if (Throats.Any())
            {
                MinThroatRadius = Throats.Min(t => t.Radius);
                MaxThroatRadius = Throats.Max(t => t.Radius);
            }
        }

        public void ExportToJson(string path)
        {
            var dto = new PNMDatasetDTO
            {
                Name = this.Name,
                FilePath = this.FilePath,
                VoxelSize = this.VoxelSize,
                Tortuosity = this.Tortuosity,
                DarcyPermeability = this.DarcyPermeability,
                NavierStokesPermeability = this.NavierStokesPermeability,
                LatticeBoltzmannPermeability = this.LatticeBoltzmannPermeability,
                Pores = this.Pores.Select(p => new PoreDTO
                {
                    ID = p.ID,
                    Position = p.Position,
                    Area = p.Area,
                    VolumeVoxels = p.VolumeVoxels,
                    VolumePhysical = p.VolumePhysical,
                    Connections = p.Connections,
                    Radius = p.Radius
                }).ToList(),
                Throats = this.Throats.Select(t => new ThroatDTO
                {
                    ID = t.ID,
                    Pore1ID = t.Pore1ID,
                    Pore2ID = t.Pore2ID,
                    Radius = t.Radius
                }).ToList()
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            string jsonString = JsonSerializer.Serialize(dto, options);
            File.WriteAllText(path, jsonString);
        }
    }
}