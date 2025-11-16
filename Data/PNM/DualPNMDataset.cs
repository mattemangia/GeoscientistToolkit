// GeoscientistToolkit/Data/PNM/DualPNMDataset.cs

using System.Numerics;
using System.Text.Json;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Pnm;

/// <summary>
/// Represents a micropore network associated with a specific macro-pore.
/// Based on the dual porosity approach from FOUBERT, DE BOEVER et al.
/// </summary>
public class MicroPoreNetwork
{
    public int MacroPoreID { get; set; }
    public List<Pore> MicroPores { get; set; } = new();
    public List<Throat> MicroThroats { get; set; } = new();

    // Microporosity properties
    public float MicroPorosity { get; set; }
    public float MicroPermeability { get; set; } // mD
    public float MicroSurfaceArea { get; set; } // µm²
    public float MicroVolume { get; set; } // µm³

    // Scale information from SEM
    public float SEMPixelSize { get; set; } // µm/pixel
    public string SEMImagePath { get; set; }
    public Vector2 SEMImagePosition { get; set; } // Position in macro-pore where SEM was taken
}

/// <summary>
/// Coupling information between macro and micro networks.
/// Defines how micro-porosity is distributed within macro-pores.
/// </summary>
public class DualPNMCoupling
{
    // Which macro-pores contain micro-porosity
    public Dictionary<int, MicroPoreNetwork> MacroToMicroMap { get; set; } = new();

    // Total micro-porosity fraction
    public float TotalMicroPorosity { get; set; }

    // Effective properties considering both scales
    public float EffectiveMacroPermeability { get; set; } // mD
    public float EffectiveMicroPermeability { get; set; } // mD
    public float CombinedPermeability { get; set; } // mD (parallel/series combination)

    // Coupling mode
    public DualPorosityCouplingMode CouplingMode { get; set; } = DualPorosityCouplingMode.Parallel;
}

/// <summary>
/// Defines how macro and micro networks are coupled.
/// </summary>
public enum DualPorosityCouplingMode
{
    /// <summary>
    /// Macropores and micropores conduct in parallel (separate flow paths)
    /// </summary>
    Parallel,

    /// <summary>
    /// Micropores are embedded within macropores (series flow)
    /// </summary>
    Series,

    /// <summary>
    /// Advanced coupling with explicit mass transfer between scales
    /// </summary>
    MassTransfer
}

/// <summary>
/// Dual Pore Network Model (DPNM) dataset.
/// Combines macro-scale PNM (from CT) with micro-scale PNM (from SEM).
/// Based on research from FOUBERT, DE BOEVER et al. on carbonate rocks.
///
/// References:
/// - De Boever et al. (2012): "Quantification and prediction of the 3D pore network evolution in carbonate reservoir rocks"
/// - Bauer et al. (2012): "Improving the estimations of petrophysical transport behavior of carbonate rocks using a dual pore network approach"
/// </summary>
public class DualPNMDataset : PNMDataset
{
    public DualPNMDataset(string name, string filePath) : base(name, filePath)
    {
        Type = DatasetType.DualPNM;
    }

    // ===== Macro-scale PNM (inherited from PNMDataset) =====
    // Pores, Throats, VoxelSize, etc. represent the macro-network from CT

    // ===== Micro-scale PNM =====

    /// <summary>
    /// Collection of all micro-pore networks (one per macro-pore that has micro-porosity)
    /// </summary>
    public List<MicroPoreNetwork> MicroNetworks { get; set; } = new();

    /// <summary>
    /// Coupling information between macro and micro scales
    /// </summary>
    public DualPNMCoupling Coupling { get; set; } = new();

    // ===== Source data tracking =====

    /// <summary>
    /// Path to the CT dataset used for macro-PNM generation
    /// </summary>
    public string CTDatasetPath { get; set; }

    /// <summary>
    /// Paths to SEM images used for micro-PNM generation
    /// </summary>
    public List<string> SEMImagePaths { get; set; } = new();

    // ===== Statistics =====

    public int TotalMicroPoreCount => MicroNetworks.Sum(mn => mn.MicroPores.Count);
    public int TotalMicroThroatCount => MicroNetworks.Sum(mn => mn.MicroThroats.Count);
    public int MacroPoresWithMicroporosity => MicroNetworks.Count;

    /// <summary>
    /// Get micro-pore network for a specific macro-pore
    /// </summary>
    public MicroPoreNetwork GetMicroNetwork(int macroPoreID)
    {
        if (Coupling.MacroToMicroMap.TryGetValue(macroPoreID, out var microNet))
            return microNet;
        return null;
    }

    /// <summary>
    /// Add a micro-pore network for a specific macro-pore
    /// </summary>
    public void AddMicroNetwork(int macroPoreID, MicroPoreNetwork microNetwork)
    {
        microNetwork.MacroPoreID = macroPoreID;
        MicroNetworks.Add(microNetwork);
        Coupling.MacroToMicroMap[macroPoreID] = microNetwork;

        Logger.Log($"Added micro-network for macro-pore {macroPoreID}: {microNetwork.MicroPores.Count} micro-pores, {microNetwork.MicroThroats.Count} micro-throats");
    }

    /// <summary>
    /// Calculate effective properties considering both scales
    /// </summary>
    public void CalculateCombinedProperties()
    {
        if (MicroNetworks.Count == 0)
        {
            Logger.LogWarning("No micro-networks defined. Combined properties will match macro-scale only.");
            Coupling.CombinedPermeability = DarcyPermeability;
            return;
        }

        // Calculate total micro-porosity
        float totalMicroVolume = MicroNetworks.Sum(mn => mn.MicroVolume);
        float totalMacroVolume = Pores.Sum(p => p.VolumePhysical);
        Coupling.TotalMicroPorosity = totalMicroVolume / (totalMacroVolume + totalMicroVolume);

        // Calculate effective permeabilities based on coupling mode
        switch (Coupling.CouplingMode)
        {
            case DualPorosityCouplingMode.Parallel:
                // Parallel conductance: k_eff = k_macro + k_micro
                Coupling.EffectiveMacroPermeability = DarcyPermeability;
                Coupling.EffectiveMicroPermeability = MicroNetworks.Average(mn => mn.MicroPermeability);
                Coupling.CombinedPermeability = Coupling.EffectiveMacroPermeability +
                                               Coupling.EffectiveMicroPermeability * Coupling.TotalMicroPorosity;
                break;

            case DualPorosityCouplingMode.Series:
                // Series conductance: 1/k_eff = 1/k_macro + 1/k_micro
                Coupling.EffectiveMacroPermeability = DarcyPermeability;
                Coupling.EffectiveMicroPermeability = MicroNetworks.Average(mn => mn.MicroPermeability);

                if (Coupling.EffectiveMicroPermeability > 0)
                {
                    Coupling.CombinedPermeability = 1.0f / (
                        (1.0f - Coupling.TotalMicroPorosity) / Coupling.EffectiveMacroPermeability +
                        Coupling.TotalMicroPorosity / Coupling.EffectiveMicroPermeability
                    );
                }
                else
                {
                    Coupling.CombinedPermeability = Coupling.EffectiveMacroPermeability;
                }
                break;

            case DualPorosityCouplingMode.MassTransfer:
                // For mass transfer mode, use weighted average
                Coupling.EffectiveMacroPermeability = DarcyPermeability;
                Coupling.EffectiveMicroPermeability = MicroNetworks.Average(mn => mn.MicroPermeability);
                float alpha = Coupling.TotalMicroPorosity;
                Coupling.CombinedPermeability = (1 - alpha) * Coupling.EffectiveMacroPermeability +
                                               alpha * Coupling.EffectiveMicroPermeability;
                break;
        }

        Logger.Log($"Dual PNM Properties Calculated:");
        Logger.Log($"  Macro-permeability: {Coupling.EffectiveMacroPermeability:F3} mD");
        Logger.Log($"  Micro-permeability: {Coupling.EffectiveMicroPermeability:F3} mD");
        Logger.Log($"  Micro-porosity fraction: {Coupling.TotalMicroPorosity:F4}");
        Logger.Log($"  Combined permeability ({Coupling.CouplingMode}): {Coupling.CombinedPermeability:F3} mD");
    }

    /// <summary>
    /// Creates a data transfer object (DTO) for serialization.
    /// Overrides base PNMDataset serialization to include dual PNM data.
    /// </summary>
    public new object ToSerializableObject()
    {
        return new DualPNMDatasetDTO
        {
            TypeName = nameof(DualPNMDataset),
            Name = Name,
            FilePath = FilePath,
            VoxelSize = VoxelSize,
            ImageWidth = ImageWidth,
            ImageHeight = ImageHeight,
            ImageDepth = ImageDepth,
            Tortuosity = Tortuosity,
            DarcyPermeability = DarcyPermeability,
            NavierStokesPermeability = NavierStokesPermeability,
            LatticeBoltzmannPermeability = LatticeBoltzmannPermeability,
            BulkDiffusivity = BulkDiffusivity,
            EffectiveDiffusivity = EffectiveDiffusivity,
            FormationFactor = FormationFactor,
            TransportTortuosity = TransportTortuosity,
            Pores = Pores.Select(p => new PoreDTO
            {
                ID = p.ID,
                Position = p.Position,
                Area = p.Area,
                VolumeVoxels = p.VolumeVoxels,
                VolumePhysical = p.VolumePhysical,
                Connections = p.Connections,
                Radius = p.Radius
            }).ToList(),
            Throats = Throats.Select(t => new ThroatDTO
            {
                ID = t.ID,
                Pore1ID = t.Pore1ID,
                Pore2ID = t.Pore2ID,
                Radius = t.Radius
            }).ToList(),
            MicroNetworks = MicroNetworks.Select(mn => new MicroPoreNetworkDTO
            {
                MacroPoreID = mn.MacroPoreID,
                MicroPorosity = mn.MicroPorosity,
                MicroPermeability = mn.MicroPermeability,
                MicroSurfaceArea = mn.MicroSurfaceArea,
                MicroVolume = mn.MicroVolume,
                SEMPixelSize = mn.SEMPixelSize,
                SEMImagePath = mn.SEMImagePath,
                SEMImagePosition = mn.SEMImagePosition,
                MicroPores = mn.MicroPores.Select(p => new PoreDTO
                {
                    ID = p.ID,
                    Position = p.Position,
                    Area = p.Area,
                    VolumeVoxels = p.VolumeVoxels,
                    VolumePhysical = p.VolumePhysical,
                    Connections = p.Connections,
                    Radius = p.Radius
                }).ToList(),
                MicroThroats = mn.MicroThroats.Select(t => new ThroatDTO
                {
                    ID = t.ID,
                    Pore1ID = t.Pore1ID,
                    Pore2ID = t.Pore2ID,
                    Radius = t.Radius
                }).ToList()
            }).ToList(),
            Coupling = new DualPNMCouplingDTO
            {
                TotalMicroPorosity = Coupling.TotalMicroPorosity,
                EffectiveMacroPermeability = Coupling.EffectiveMacroPermeability,
                EffectiveMicroPermeability = Coupling.EffectiveMicroPermeability,
                CombinedPermeability = Coupling.CombinedPermeability,
                CouplingMode = Coupling.CouplingMode.ToString()
            },
            CTDatasetPath = CTDatasetPath,
            SEMImagePaths = new List<string>(SEMImagePaths)
        };
    }

    /// <summary>
    /// Imports data from a DTO
    /// </summary>
    public void ImportFromDTO(DualPNMDatasetDTO dto)
    {
        if (dto == null) return;

        // Import base PNM data
        VoxelSize = dto.VoxelSize;
        ImageWidth = dto.ImageWidth;
        ImageHeight = dto.ImageHeight;
        ImageDepth = dto.ImageDepth;
        Tortuosity = dto.Tortuosity;
        DarcyPermeability = dto.DarcyPermeability;
        NavierStokesPermeability = dto.NavierStokesPermeability;
        LatticeBoltzmannPermeability = dto.LatticeBoltzmannPermeability;
        BulkDiffusivity = dto.BulkDiffusivity;
        EffectiveDiffusivity = dto.EffectiveDiffusivity;
        FormationFactor = dto.FormationFactor;
        TransportTortuosity = dto.TransportTortuosity;

        // Import macro pores and throats (access private fields via reflection)
        var poresField = typeof(PNMDataset).GetField("_poresOriginal",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var throatsField = typeof(PNMDataset).GetField("_throatsOriginal",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (poresField != null && dto.Pores != null)
        {
            var poresList = poresField.GetValue(this) as List<Pore>;
            poresList?.Clear();
            poresList?.AddRange(dto.Pores.Select(p => new Pore
            {
                ID = p.ID,
                Position = p.Position,
                Area = p.Area,
                VolumeVoxels = p.VolumeVoxels,
                VolumePhysical = p.VolumePhysical,
                Connections = p.Connections,
                Radius = p.Radius
            }));
        }

        if (throatsField != null && dto.Throats != null)
        {
            var throatsList = throatsField.GetValue(this) as List<Throat>;
            throatsList?.Clear();
            throatsList?.AddRange(dto.Throats.Select(t => new Throat
            {
                ID = t.ID,
                Pore1ID = t.Pore1ID,
                Pore2ID = t.Pore2ID,
                Radius = t.Radius
            }));
        }

        // Import micro networks
        MicroNetworks.Clear();
        if (dto.MicroNetworks != null)
        {
            foreach (var mnDto in dto.MicroNetworks)
            {
                var microNet = new MicroPoreNetwork
                {
                    MacroPoreID = mnDto.MacroPoreID,
                    MicroPorosity = mnDto.MicroPorosity,
                    MicroPermeability = mnDto.MicroPermeability,
                    MicroSurfaceArea = mnDto.MicroSurfaceArea,
                    MicroVolume = mnDto.MicroVolume,
                    SEMPixelSize = mnDto.SEMPixelSize,
                    SEMImagePath = mnDto.SEMImagePath,
                    SEMImagePosition = mnDto.SEMImagePosition,
                    MicroPores = mnDto.MicroPores?.Select(p => new Pore
                    {
                        ID = p.ID,
                        Position = p.Position,
                        Area = p.Area,
                        VolumeVoxels = p.VolumeVoxels,
                        VolumePhysical = p.VolumePhysical,
                        Connections = p.Connections,
                        Radius = p.Radius
                    }).ToList() ?? new List<Pore>(),
                    MicroThroats = mnDto.MicroThroats?.Select(t => new Throat
                    {
                        ID = t.ID,
                        Pore1ID = t.Pore1ID,
                        Pore2ID = t.Pore2ID,
                        Radius = t.Radius
                    }).ToList() ?? new List<Throat>()
                };

                MicroNetworks.Add(microNet);
                Coupling.MacroToMicroMap[microNet.MacroPoreID] = microNet;
            }
        }

        // Import coupling data
        if (dto.Coupling != null)
        {
            Coupling.TotalMicroPorosity = dto.Coupling.TotalMicroPorosity;
            Coupling.EffectiveMacroPermeability = dto.Coupling.EffectiveMacroPermeability;
            Coupling.EffectiveMicroPermeability = dto.Coupling.EffectiveMicroPermeability;
            Coupling.CombinedPermeability = dto.Coupling.CombinedPermeability;

            if (Enum.TryParse<DualPorosityCouplingMode>(dto.Coupling.CouplingMode, out var mode))
            {
                Coupling.CouplingMode = mode;
            }
        }

        // Import paths
        CTDatasetPath = dto.CTDatasetPath;
        SEMImagePaths = dto.SEMImagePaths != null ? new List<string>(dto.SEMImagePaths) : new List<string>();

        // Apply filter to update visible pores/throats
        ApplyFilter(null);

        Logger.Log($"Imported Dual PNM: {Pores.Count} macro-pores, {TotalMicroPoreCount} micro-pores");
    }

    /// <summary>
    /// Export dual PNM to JSON with both macro and micro networks
    /// </summary>
    public void ExportToJson(string outputPath)
    {
        var dualData = new
        {
            DatasetType = "DualPNM",
            MacroNetwork = ToSerializableObject(),
            MicroNetworks = MicroNetworks.Select(mn => new
            {
                mn.MacroPoreID,
                mn.MicroPorosity,
                mn.MicroPermeability,
                mn.MicroSurfaceArea,
                mn.MicroVolume,
                mn.SEMPixelSize,
                mn.SEMImagePath,
                mn.SEMImagePosition,
                MicroPores = mn.MicroPores.Select(p => new
                {
                    p.ID,
                    p.Position,
                    p.Area,
                    p.VolumeVoxels,
                    p.VolumePhysical,
                    p.Connections,
                    p.Radius
                }).ToList(),
                MicroThroats = mn.MicroThroats.Select(t => new
                {
                    t.ID,
                    t.Pore1ID,
                    t.Pore2ID,
                    t.Radius
                }).ToList()
            }).ToList(),
            Coupling = new
            {
                Coupling.TotalMicroPorosity,
                Coupling.EffectiveMacroPermeability,
                Coupling.EffectiveMicroPermeability,
                Coupling.CombinedPermeability,
                CouplingMode = Coupling.CouplingMode.ToString()
            },
            CTDatasetPath,
            SEMImagePaths,
            Statistics = new
            {
                MacroPores = Pores.Count,
                MacroThroats = Throats.Count,
                MicroPores = TotalMicroPoreCount,
                MicroThroats = TotalMicroThroatCount,
                MacroPoresWithMicroporosity
            }
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(dualData, options);
        File.WriteAllText(outputPath, json);

        Logger.Log($"Exported Dual PNM to: {outputPath}");
        Logger.Log($"  Macro-pores: {Pores.Count}, Micro-pores: {TotalMicroPoreCount}");
        Logger.Log($"  Macro-throats: {Throats.Count}, Micro-throats: {TotalMicroThroatCount}");
    }

    /// <summary>
    /// Export statistics comparing macro and micro scales
    /// </summary>
    public string GetStatisticsReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== DUAL PORE NETWORK MODEL STATISTICS ===");
        sb.AppendLine();

        sb.AppendLine("MACRO-SCALE NETWORK (from CT):");
        sb.AppendLine($"  Pores: {Pores.Count}");
        sb.AppendLine($"  Throats: {Throats.Count}");
        sb.AppendLine($"  Voxel size: {VoxelSize:F3} µm");
        sb.AppendLine($"  Image dimensions: {ImageWidth} x {ImageHeight} x {ImageDepth}");
        sb.AppendLine($"  Permeability: {DarcyPermeability:F3} mD");
        sb.AppendLine($"  Tortuosity: {Tortuosity:F3}");
        sb.AppendLine();

        sb.AppendLine("MICRO-SCALE NETWORKS (from SEM):");
        sb.AppendLine($"  Macro-pores with micro-porosity: {MacroPoresWithMicroporosity}");
        sb.AppendLine($"  Total micro-pores: {TotalMicroPoreCount}");
        sb.AppendLine($"  Total micro-throats: {TotalMicroThroatCount}");
        sb.AppendLine($"  SEM images used: {SEMImagePaths.Count}");

        if (MicroNetworks.Count > 0)
        {
            float avgMicroPores = (float)TotalMicroPoreCount / MicroNetworks.Count;
            float avgMicroThroats = (float)TotalMicroThroatCount / MicroNetworks.Count;
            sb.AppendLine($"  Avg micro-pores per macro-pore: {avgMicroPores:F1}");
            sb.AppendLine($"  Avg micro-throats per macro-pore: {avgMicroThroats:F1}");
        }
        sb.AppendLine();

        sb.AppendLine("DUAL POROSITY COUPLING:");
        sb.AppendLine($"  Coupling mode: {Coupling.CouplingMode}");
        sb.AppendLine($"  Micro-porosity fraction: {Coupling.TotalMicroPorosity:F4}");
        sb.AppendLine($"  Macro-permeability: {Coupling.EffectiveMacroPermeability:F3} mD");
        sb.AppendLine($"  Micro-permeability: {Coupling.EffectiveMicroPermeability:F3} mD");
        sb.AppendLine($"  Combined permeability: {Coupling.CombinedPermeability:F3} mD");
        sb.AppendLine();

        return sb.ToString();
    }
}
