// GeoscientistToolkit/Data/Loaders/IDataLoader.cs

namespace GeoscientistToolkit.Data.Loaders;

public interface IDataLoader
{
    string Name { get; }
    string Description { get; }
    bool CanImport { get; }
    string ValidationMessage { get; }

    Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter);
    void Reset();
}

public class LoaderProgress
{
    private readonly IProgress<(float progress, string message)> _reporter;

    public LoaderProgress(IProgress<(float progress, string message)> reporter)
    {
        _reporter = reporter;
    }

    public void Report(float progress, string message)
    {
        _reporter?.Report((progress, message));
    }
}