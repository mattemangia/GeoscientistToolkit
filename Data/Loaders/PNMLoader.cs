// GeoscientistToolkit/Data/Loaders/PNMLoader.cs
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders
{
    public class PNMLoader : IDataLoader
    {
        public string Name => "Pore Network Model";
        public string Description => "Import a Pore Network Model from a JSON file";
        public bool CanImport => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);
        public string ValidationMessage => CanImport ? null : "Please select a valid PNM JSON file.";

        public string FilePath { get; set; } = "";

        public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
        {
            return await Task.Run(() =>
            {
                try
                {
                    progressReporter?.Report((0.1f, "Reading PNM file..."));
                    string jsonString = File.ReadAllText(FilePath);

                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var dto = JsonSerializer.Deserialize<PNMDatasetDTO>(jsonString, options);

                    if (dto == null)
                    {
                        throw new InvalidDataException("Failed to deserialize PNM file.");
                    }

                    progressReporter?.Report((0.5f, "Creating dataset..."));

                    var dataset = new PNMDataset(Path.GetFileNameWithoutExtension(FilePath), FilePath)
                    {
                        VoxelSize = dto.VoxelSize,
                        Tortuosity = dto.Tortuosity,
                        DarcyPermeability = dto.DarcyPermeability,
                        NavierStokesPermeability = dto.NavierStokesPermeability,
                        LatticeBoltzmannPermeability = dto.LatticeBoltzmannPermeability,
                        Pores = dto.Pores.Select(p => new Pore
                        {
                            ID = p.ID,
                            Position = p.Position,
                            Area = p.Area,
                            VolumeVoxels = p.VolumeVoxels,
                            VolumePhysical = p.VolumePhysical,
                            Connections = p.Connections,
                            Radius = p.Radius
                        }).ToList(),
                        Throats = dto.Throats.Select(t => new Throat
                        {
                            ID = t.ID,
                            Pore1ID = t.Pore1ID,
                            Pore2ID = t.Pore2ID,
                            Radius = t.Radius
                        }).ToList()
                    };

                    dataset.Load(); // This calculates bounds

                    progressReporter?.Report((1.0f, "PNM dataset loaded successfully."));
                    return dataset;
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[PNMLoader] Failed to load PNM file: {ex.Message}");
                    throw new Exception("Failed to load or parse the PNM file.", ex);
                }
            });
        }

        public void Reset()
        {
            FilePath = "";
        }
    }
}