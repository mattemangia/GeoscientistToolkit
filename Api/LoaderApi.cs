using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Loaders;

namespace GeoscientistToolkit.Api;

/// <summary>
///     Provides access to dataset loaders used by the toolkit.
/// </summary>
public class LoaderApi
{
    /// <summary>
    ///     Returns a catalog of loader factories keyed by a stable identifier.
    /// </summary>
    public IReadOnlyDictionary<string, Func<IDataLoader>> GetLoaderFactories()
    {
        return new Dictionary<string, Func<IDataLoader>>
        {
            ["acoustic_volume"] = () => new AcousticVolumeLoader(),
            ["las"] = () => new LASLoader(),
            ["mesh_3d"] = () => new Mesh3DLoader(),
            ["pnm"] = () => new PNMLoader(),
            ["seismic"] = () => new SeismicLoader(),
            ["seismic_cube"] = () => new SeismicCubeLoader(),
            ["subsurface_gis"] = () => new SubsurfaceGISLoader(),
            ["table"] = () => new TableLoader(),
            ["text"] = () => new TextLoader(),
            ["tough2"] = () => new Tough2Loader(),
            ["ct_stack"] = () => new CTStackLoaderWrapper(),
            ["labeled_volume"] = () => new LabeledVolumeLoaderWrapper(),
            ["segmentation"] = () => new SegmentationLoader(),
            ["slope_stability_results"] = () => new SlopeStabilityResultsBinaryLoader(),
            ["two_d_geology"] = () => new TwoDGeologyLoader()
        };
    }

    /// <summary>
    ///     Executes a configured loader instance.
    /// </summary>
    public Task<Dataset> LoadAsync(
        IDataLoader loader,
        IProgress<(float progress, string message)> progress = null)
    {
        if (loader == null) throw new ArgumentNullException(nameof(loader));
        return loader.LoadAsync(progress);
    }

    /// <summary>
    ///     Loads an acoustic volume dataset from a directory.
    /// </summary>
    public Task<Dataset> LoadAcousticVolumeAsync(
        string directoryPath,
        IProgress<(float progress, string message)> progress = null)
    {
        var loader = new AcousticVolumeLoader { DirectoryPath = directoryPath };
        return loader.LoadAsync(progress);
    }

    /// <summary>
    ///     Loads a LAS log dataset from a file.
    /// </summary>
    public Task<Dataset> LoadLasAsync(
        string filePath,
        IProgress<(float progress, string message)> progress = null)
    {
        var loader = new LASLoader { FilePath = filePath };
        return loader.LoadAsync(progress);
    }

    /// <summary>
    ///     Loads a 3D mesh dataset from a file.
    /// </summary>
    public Task<Dataset> LoadMesh3DAsync(
        string filePath,
        IProgress<(float progress, string message)> progress = null)
    {
        var loader = new Mesh3DLoader { ModelPath = filePath };
        return loader.LoadAsync(progress);
    }

    /// <summary>
    ///     Loads a PNM dataset from a file.
    /// </summary>
    public Task<Dataset> LoadPnmAsync(
        string filePath,
        IProgress<(float progress, string message)> progress = null)
    {
        var loader = new PNMLoader { FilePath = filePath };
        return loader.LoadAsync(progress);
    }

    /// <summary>
    ///     Loads a SEG-Y seismic dataset from a file.
    /// </summary>
    public Task<Dataset> LoadSeismicAsync(
        string filePath,
        IProgress<(float progress, string message)> progress = null)
    {
        var loader = new SeismicLoader { FilePath = filePath };
        return loader.LoadAsync(progress);
    }

    /// <summary>
    ///     Loads a seismic cube dataset from a .seiscube file.
    /// </summary>
    public Task<Dataset> LoadSeismicCubeAsync(
        string filePath,
        IProgress<(float progress, string message)> progress = null)
    {
        var loader = new SeismicCubeLoader { FilePath = filePath };
        return loader.LoadAsync(progress);
    }

    /// <summary>
    ///     Loads a subsurface GIS dataset from a file.
    /// </summary>
    public Task<Dataset> LoadSubsurfaceGisAsync(
        string filePath,
        IProgress<(float progress, string message)> progress = null)
    {
        var loader = new SubsurfaceGISLoader { FilePath = filePath };
        return loader.LoadAsync(progress);
    }

    /// <summary>
    ///     Loads a table dataset from a file.
    /// </summary>
    public Task<Dataset> LoadTableAsync(
        string filePath,
        IProgress<(float progress, string message)> progress = null)
    {
        var loader = new TableLoader { FilePath = filePath };
        return loader.LoadAsync(progress);
    }

    /// <summary>
    ///     Loads a text dataset from a file.
    /// </summary>
    public Task<Dataset> LoadTextAsync(
        string filePath,
        IProgress<(float progress, string message)> progress = null)
    {
        var loader = new TextLoader { TextPath = filePath };
        return loader.LoadAsync(progress);
    }

    /// <summary>
    ///     Loads a TOUGH2 dataset from a file.
    /// </summary>
    public Task<Dataset> LoadTough2Async(
        string filePath,
        IProgress<(float progress, string message)> progress = null)
    {
        var loader = new Tough2Loader { FilePath = filePath };
        return loader.LoadAsync(progress);
    }
}
