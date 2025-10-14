// GeoscientistToolkit/Data/Pnm/PNMDataset.cs

using System.Data;
using System.Numerics;
using System.Text;
using System.Text.Json;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.Util;

// Added for ISerializableDataset

namespace GeoscientistToolkit.Data.Pnm;

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

/// <summary>
///     Filtering criteria for visualisation and exports
///     (all ranges are inclusive when specified).
/// </summary>
public sealed class PoreFilterCriteria
{
    public float? MinPoreRadius { get; set; }
    public float? MaxPoreRadius { get; set; }
    public float? MinPoreArea { get; set; }
    public float? MaxPoreArea { get; set; }
    public float? MinPoreVolumeVox { get; set; }
    public float? MaxPoreVolumeVox { get; set; }
    public float? MinPoreVolumePhys { get; set; }
    public float? MaxPoreVolumePhys { get; set; }
    public int? MinConnections { get; set; }
    public int? MaxConnections { get; set; }

    // Optional throat filter:
    public float? MinThroatRadius { get; set; }
    public float? MaxThroatRadius { get; set; }

    public bool HasAnyConstraint()
    {
        return MinPoreRadius.HasValue || MaxPoreRadius.HasValue ||
               MinPoreArea.HasValue || MaxPoreArea.HasValue ||
               MinPoreVolumeVox.HasValue || MaxPoreVolumeVox.HasValue ||
               MinPoreVolumePhys.HasValue || MaxPoreVolumePhys.HasValue ||
               MinConnections.HasValue || MaxConnections.HasValue ||
               MinThroatRadius.HasValue || MaxThroatRadius.HasValue;
    }
}

public class PNMDataset : Dataset, ISerializableDataset
{
    // --- Data (original, immutable source) ---
    private readonly List<Pore> _poresOriginal = new();
    private readonly List<Throat> _throatsOriginal = new();

    public PNMDataset(string name, string filePath) : base(name, filePath)
    {
        Type = DatasetType.PNM;
    }

    // --- Physical/network properties ---
    public float VoxelSize { get; set; } // µm
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public int ImageDepth { get; set; }
    public float Tortuosity { get; set; }
    public float DarcyPermeability { get; set; } // mD
    public float NavierStokesPermeability { get; set; } // mD
    public float LatticeBoltzmannPermeability { get; set; } // mD
    
    // --- NEW: Diffusivity Properties ---
    public float BulkDiffusivity { get; set; } // m²/s
    public float EffectiveDiffusivity { get; set; } // m²/s
    public float FormationFactor { get; set; }
    public float TransportTortuosity { get; set; }

    /// <summary> Visible pores after filtering. </summary>
    public List<Pore> Pores { get; private set; } = new();

    /// <summary> Visible throats after filtering. (Always consistent with current Pores.)</summary>
    public List<Throat> Throats { get; private set; } = new();

    // --- Min/Max values for visualization scaling based on *visible* data ---
    public float MinPoreRadius { get; private set; }
    public float MaxPoreRadius { get; private set; }
    public float MinThroatRadius { get; private set; }
    public float MaxThroatRadius { get; private set; }

    // Active filter (null => no filter)
    public PoreFilterCriteria ActiveFilter { get; private set; }

    /// <summary>
    ///     Creates a data transfer object (DTO) for serialization.
    /// </summary>
    public object ToSerializableObject()
    {
        return new PNMDatasetDTO
        {
            TypeName = nameof(PNMDataset),
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
            // --- NEW: Save diffusivity results ---
            BulkDiffusivity = BulkDiffusivity,
            EffectiveDiffusivity = EffectiveDiffusivity,
            FormationFactor = FormationFactor,
            TransportTortuosity = TransportTortuosity,
            Pores = _poresOriginal.Select(p => new PoreDTO
            {
                ID = p.ID,
                Position = p.Position,
                Area = p.Area,
                VolumeVoxels = p.VolumeVoxels,
                VolumePhysical = p.VolumePhysical,
                Connections = p.Connections,
                Radius = p.Radius
            }).ToList(),
            Throats = _throatsOriginal.Select(t => new ThroatDTO
            {
                ID = t.ID,
                Pore1ID = t.Pore1ID,
                Pore2ID = t.Pore2ID,
                Radius = t.Radius
            }).ToList()
        };
    }

    /// <summary>
    ///     Fired whenever visible data changes (apply/clear filter, or programmatic edits).
    ///     PNMViewer subscribes to this via ProjectManager's DatasetDataChanged signal.
    /// </summary>
    public event Action DataChanged;

    public override long GetSizeInBytes()
    {
        // If saved to disk, report file size
        if (!string.IsNullOrEmpty(FilePath) && File.Exists(FilePath))
            return new FileInfo(FilePath).Length;

        // Otherwise estimate size from in-memory data
        long estimatedSize = 0;

        // Estimate based on data structure sizes
        // Each Pore: ID(4) + Position(12) + Area(4) + VolumeVoxels(4) + VolumePhysical(4) + Connections(4) + Radius(4) = 36 bytes
        estimatedSize += Pores.Count * 36;

        // Each Throat: ID(4) + Pore1ID(4) + Pore2ID(4) + Radius(4) = 16 bytes
        estimatedSize += Throats.Count * 16;

        // Add some overhead for properties and metadata
        estimatedSize += 1024; // Properties, metadata, etc.

        return estimatedSize;
    }

    public override void Load()
    {
        // If a file exists, load from it
        if (!string.IsNullOrEmpty(FilePath) && File.Exists(FilePath))
            try
            {
                var jsonString = File.ReadAllText(FilePath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var dto = JsonSerializer.Deserialize<PNMDatasetDTO>(jsonString, options);

                if (dto != null)
                {
                    ImportFromDTO(dto);
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[PNMDataset] Failed to load from file: {ex.Message}");
            }

        // If no file or loading failed, just ensure bounds are calculated
        CalculateBounds();
    }

    public override void Unload()
    {
        _poresOriginal.Clear();
        _throatsOriginal.Clear();
        Pores.Clear();
        Throats.Clear();
    }

    /// <summary>
    ///     Call once after raw data is assigned to Pores/Throats from a loader,
    ///     to populate originals and set visible lists = originals (no filter).
    /// </summary>
    public void InitializeFromCurrentLists()
    {
        _poresOriginal.Clear();
        _poresOriginal.AddRange(Pores);
        _throatsOriginal.Clear();
        _throatsOriginal.AddRange(Throats);
        // ensure visible lists reference copies (avoid aliasing)
        Pores = _poresOriginal.ToList();
        Throats = _throatsOriginal.ToList();
        ActiveFilter = null;
        CalculateBounds();

        // Mark as not missing since we have data
        IsMissing = false;
    }

    /// <summary>
    ///     Replace current visible lists with originals (no filter).
    /// </summary>
    public void ClearFilter()
    {
        if (_poresOriginal.Count == 0 && _throatsOriginal.Count == 0)
            return;

        ActiveFilter = null;
        Pores = _poresOriginal.ToList();
        Throats = _throatsOriginal.ToList();
        CalculateBounds();
        RaiseDataChanged();
    }

    /// <summary>
    ///     Apply a filter. Visible lists are rebuilt from the originals.
    /// </summary>
    public void ApplyFilter(PoreFilterCriteria criteria)
    {
        if (criteria == null || !criteria.HasAnyConstraint())
        {
            ClearFilter();
            return;
        }

        ActiveFilter = criteria;

        // Filter pores
        IEnumerable<Pore> pores = _poresOriginal;

        if (criteria.MinPoreRadius.HasValue) pores = pores.Where(p => p.Radius >= criteria.MinPoreRadius.Value);
        if (criteria.MaxPoreRadius.HasValue) pores = pores.Where(p => p.Radius <= criteria.MaxPoreRadius.Value);

        if (criteria.MinPoreArea.HasValue) pores = pores.Where(p => p.Area >= criteria.MinPoreArea.Value);
        if (criteria.MaxPoreArea.HasValue) pores = pores.Where(p => p.Area <= criteria.MaxPoreArea.Value);

        if (criteria.MinPoreVolumeVox.HasValue)
            pores = pores.Where(p => p.VolumeVoxels >= criteria.MinPoreVolumeVox.Value);
        if (criteria.MaxPoreVolumeVox.HasValue)
            pores = pores.Where(p => p.VolumeVoxels <= criteria.MaxPoreVolumeVox.Value);

        if (criteria.MinPoreVolumePhys.HasValue)
            pores = pores.Where(p => p.VolumePhysical >= criteria.MinPoreVolumePhys.Value);
        if (criteria.MaxPoreVolumePhys.HasValue)
            pores = pores.Where(p => p.VolumePhysical <= criteria.MaxPoreVolumePhys.Value);

        if (criteria.MinConnections.HasValue) pores = pores.Where(p => p.Connections >= criteria.MinConnections.Value);
        if (criteria.MaxConnections.HasValue) pores = pores.Where(p => p.Connections <= criteria.MaxConnections.Value);

        var visiblePores = pores.ToList();
        var visibleIds = new HashSet<int>(visiblePores.Select(p => p.ID));

        // Filter throats: keep those connecting two visible pores, then apply throat radius range if set
        var throats = _throatsOriginal.Where(t => visibleIds.Contains(t.Pore1ID) && visibleIds.Contains(t.Pore2ID));
        if (criteria.MinThroatRadius.HasValue) throats = throats.Where(t => t.Radius >= criteria.MinThroatRadius.Value);
        if (criteria.MaxThroatRadius.HasValue) throats = throats.Where(t => t.Radius <= criteria.MaxThroatRadius.Value);

        Pores = visiblePores;
        Throats = throats.ToList();

        CalculateBounds();
        RaiseDataChanged();
    }

    private void RaiseDataChanged()
    {
        // Notify project and direct subscribers
        ProjectManager.Instance?.NotifyDatasetDataChanged(this);
        DataChanged?.Invoke();
    }

    /// <summary>Re-calc min/max for *visible* subsets.</summary>
    public void CalculateBounds()
    {
        if (Pores != null && Pores.Any())
        {
            MinPoreRadius = Pores.Min(p => p.Radius);
            MaxPoreRadius = Pores.Max(p => p.Radius);
        }
        else
        {
            MinPoreRadius = 0;
            MaxPoreRadius = 1; // Default range to prevent division by zero
        }

        if (Throats != null && Throats.Any())
        {
            MinThroatRadius = Throats.Min(t => t.Radius);
            MaxThroatRadius = Throats.Max(t => t.Radius);
        }
        else
        {
            MinThroatRadius = 0;
            MaxThroatRadius = 1; // Default range to prevent division by zero
        }
    }

    public void ExportToJson(string path)
    {
        var dto = new PNMDatasetDTO
        {
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
            }).ToList()
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        var jsonString = JsonSerializer.Serialize(dto, options);
        File.WriteAllText(path, jsonString);

        // Update the FilePath after successful export
        FilePath = path;
    }

    // -------------------- TABLE BUILDERS & CSV EXPORT --------------------

    public TableDataset BuildPoresTableDataset(string datasetName = null, bool includePhysicalUnits = true)
    {
        var table = new DataTable(datasetName ?? $"{Name}_Pores");
        table.Columns.Add("PoreID", typeof(int));
        table.Columns.Add("X", typeof(float));
        table.Columns.Add("Y", typeof(float));
        table.Columns.Add("Z", typeof(float));
        table.Columns.Add("Radius_vox", typeof(float));
        table.Columns.Add("Area_vox2", typeof(float));
        table.Columns.Add("Volume_vox3", typeof(float));
        table.Columns.Add("Connections", typeof(int));

        if (includePhysicalUnits)
        {
            table.Columns.Add("Radius_um", typeof(double));
            table.Columns.Add("Area_um2", typeof(double));
            table.Columns.Add("Volume_um3", typeof(double));
        }

        foreach (var p in Pores)
        {
            var row = table.NewRow();
            row["PoreID"] = p.ID;
            row["X"] = p.Position.X;
            row["Y"] = p.Position.Y;
            row["Z"] = p.Position.Z;
            row["Radius_vox"] = p.Radius;
            row["Area_vox2"] = p.Area;
            row["Volume_vox3"] = p.VolumeVoxels;
            row["Connections"] = p.Connections;

            if (includePhysicalUnits)
            {
                double r_um = p.Radius * VoxelSize;
                double a_um2 = p.Area * VoxelSize * VoxelSize;
                double v_um3 = p.VolumePhysical;
                if (v_um3 == 0) v_um3 = p.VolumeVoxels * Math.Pow(VoxelSize, 3);

                row["Radius_um"] = r_um;
                row["Area_um2"] = a_um2;
                row["Volume_um3"] = v_um3;
            }

            table.Rows.Add(row);
        }

        return new TableDataset(table.TableName, table);
    }

    public TableDataset BuildThroatsTableDataset(string datasetName = null, bool includePhysicalUnits = true)
    {
        var table = new DataTable(datasetName ?? $"{Name}_Throats");
        table.Columns.Add("ThroatID", typeof(int));
        table.Columns.Add("Pore1ID", typeof(int));
        table.Columns.Add("Pore2ID", typeof(int));
        table.Columns.Add("Radius_vox", typeof(float));

        if (includePhysicalUnits)
            table.Columns.Add("Radius_um", typeof(double));

        foreach (var t in Throats)
        {
            var row = table.NewRow();
            row["ThroatID"] = t.ID;
            row["Pore1ID"] = t.Pore1ID;
            row["Pore2ID"] = t.Pore2ID;
            row["Radius_vox"] = t.Radius;
            if (includePhysicalUnits)
                row["Radius_um"] = t.Radius * VoxelSize;
            table.Rows.Add(row);
        }

        return new TableDataset(table.TableName, table);
    }

    public void ExportPoresCsv(string path, bool includeHeaders = true, bool includePhysicalUnits = true)
    {
        using var w = new StreamWriter(path, false, Encoding.UTF8);
        var headers = new List<string>
            { "PoreID", "X", "Y", "Z", "Radius_vox", "Area_vox2", "Volume_vox3", "Connections" };
        if (includePhysicalUnits) headers.AddRange(new[] { "Radius_um", "Area_um2", "Volume_um3" });

        if (includeHeaders) w.WriteLine(string.Join(",", headers));

        foreach (var p in Pores)
        {
            var vals = new List<string>
            {
                p.ID.ToString(),
                p.Position.X.ToString("G9"),
                p.Position.Y.ToString("G9"),
                p.Position.Z.ToString("G9"),
                p.Radius.ToString("G9"),
                p.Area.ToString("G9"),
                p.VolumeVoxels.ToString("G9"),
                p.Connections.ToString()
            };

            if (includePhysicalUnits)
            {
                double r_um = p.Radius * VoxelSize;
                double a_um2 = p.Area * VoxelSize * VoxelSize;
                double v_um3 = p.VolumePhysical;
                if (v_um3 == 0) v_um3 = p.VolumeVoxels * Math.Pow(VoxelSize, 3);

                vals.Add(r_um.ToString("G9"));
                vals.Add(a_um2.ToString("G9"));
                vals.Add(v_um3.ToString("G9"));
            }

            w.WriteLine(string.Join(",", vals.Select(EscapeCsv)));
        }
    }

    public void ExportThroatsCsv(string path, bool includeHeaders = true, bool includePhysicalUnits = true)
    {
        using var w = new StreamWriter(path, false, Encoding.UTF8);
        var headers = new List<string> { "ThroatID", "Pore1ID", "Pore2ID", "Radius_vox" };
        if (includePhysicalUnits) headers.Add("Radius_um");
        if (includeHeaders) w.WriteLine(string.Join(",", headers));

        foreach (var t in Throats)
        {
            var vals = new List<string>
            {
                t.ID.ToString(),
                t.Pore1ID.ToString(),
                t.Pore2ID.ToString(),
                t.Radius.ToString("G9")
            };
            if (includePhysicalUnits)
                vals.Add((t.Radius * VoxelSize).ToString("G9"));

            w.WriteLine(string.Join(",", vals.Select(EscapeCsv)));
        }
    }

    private static string EscapeCsv(string s)
    {
        if (s == null) return "";
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    // -------------------- DTO IMPORT/EXPORT --------------------

    public void ImportFromDTO(PNMDatasetDTO dto)
    {
        VoxelSize = dto.VoxelSize;
        ImageWidth = dto.ImageWidth;
        ImageHeight = dto.ImageHeight;
        ImageDepth = dto.ImageDepth;
        Tortuosity = dto.Tortuosity;
        DarcyPermeability = dto.DarcyPermeability;
        NavierStokesPermeability = dto.NavierStokesPermeability;
        LatticeBoltzmannPermeability = dto.LatticeBoltzmannPermeability;

        // --- NEW: Load diffusivity results ---
        BulkDiffusivity = dto.BulkDiffusivity;
        EffectiveDiffusivity = dto.EffectiveDiffusivity;
        FormationFactor = dto.FormationFactor;
        TransportTortuosity = dto.TransportTortuosity;


        // Fill visible first:
        Pores = dto.Pores?.Select(p => new Pore
        {
            ID = p.ID,
            Position = p.Position,
            Area = p.Area,
            VolumeVoxels = p.VolumeVoxels,
            VolumePhysical = p.VolumePhysical,
            Connections = p.Connections,
            Radius = p.Radius
        }).ToList() ?? new List<Pore>();

        Throats = dto.Throats?.Select(t => new Throat
        {
            ID = t.ID,
            Pore1ID = t.Pore1ID,
            Pore2ID = t.Pore2ID,
            Radius = t.Radius
        }).ToList() ?? new List<Throat>();

        InitializeFromCurrentLists();
    }

    public void ExportToJson(string path, bool exportOriginal = false, bool indented = true)
    {
        var poresToWrite = exportOriginal ? _poresOriginal : Pores;
        var throatsToWrite = exportOriginal ? _throatsOriginal : Throats;

        var dto = new PNMDatasetDTO
        {
            Name = Name,
            FilePath = FilePath,
            VoxelSize = VoxelSize,
            Tortuosity = Tortuosity,
            DarcyPermeability = DarcyPermeability,
            NavierStokesPermeability = NavierStokesPermeability,
            LatticeBoltzmannPermeability = LatticeBoltzmannPermeability,
            Pores = poresToWrite.Select(p => new PoreDTO
            {
                ID = p.ID, Position = p.Position, Area = p.Area,
                VolumeVoxels = p.VolumeVoxels, VolumePhysical = p.VolumePhysical,
                Connections = p.Connections, Radius = p.Radius
            }).ToList(),
            Throats = throatsToWrite.Select(t => new ThroatDTO
            {
                ID = t.ID, Pore1ID = t.Pore1ID, Pore2ID = t.Pore2ID, Radius = t.Radius
            }).ToList()
        };
        var options = new JsonSerializerOptions { WriteIndented = indented };
        File.WriteAllText(path, JsonSerializer.Serialize(dto, options));

        // Update the FilePath after successful export
        FilePath = path;
    }
}