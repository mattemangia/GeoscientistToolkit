using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;

namespace GeoscientistToolkit.Api;

/// <summary>
///     Provides access to GeoScript command execution.
/// </summary>
public class GeoScriptApi
{
    private readonly GeoScriptEngine _engine = new();

    /// <summary>
    ///     Executes a GeoScript command or pipeline against the provided dataset.
    /// </summary>
    /// <param name="script">GeoScript command text (supports pipelines).</param>
    /// <param name="inputDataset">Primary input dataset.</param>
    /// <param name="contextDatasets">Optional datasets available in the script context.</param>
    public Task<Dataset> ExecuteAsync(
        string script,
        Dataset inputDataset,
        Dictionary<string, Dataset> contextDatasets = null)
    {
        return _engine.ExecuteAsync(script, inputDataset, contextDatasets);
    }
}
