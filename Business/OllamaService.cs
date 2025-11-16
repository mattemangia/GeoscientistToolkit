// GeoscientistToolkit/Business/OllamaService.cs

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoscientistToolkit.Settings;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business;

/// <summary>
///     Service for interacting with Ollama LLM API
/// </summary>
public class OllamaService
{
    private static OllamaService _instance;
    private static readonly object _lock = new();
    private readonly HttpClient _httpClient;

    private OllamaService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "GeoscientistToolkit/1.0");
    }

    public static OllamaService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                        _instance = new OllamaService();
                }
            }
            return _instance;
        }
    }

    /// <summary>
    ///     Test connection to Ollama server
    /// </summary>
    public async Task<bool> TestConnectionAsync(string baseUrl, int timeoutSeconds = 10)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            var response = await _httpClient.GetAsync($"{baseUrl}/api/tags", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to connect to Ollama: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Get list of available models from Ollama
    /// </summary>
    public async Task<List<string>> GetAvailableModelsAsync(string baseUrl, int timeoutSeconds = 10)
    {
        var models = new List<string>();

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            var response = await _httpClient.GetAsync($"{baseUrl}/api/tags", cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                Logger.LogError($"Failed to fetch models from Ollama: {response.StatusCode}");
                return models;
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<OllamaModelsResponse>(json);

            if (result?.Models != null)
            {
                models = result.Models.Select(m => m.Name).ToList();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to fetch models from Ollama: {ex.Message}");
        }

        return models;
    }

    /// <summary>
    ///     Generate a report using Ollama
    /// </summary>
    public async Task<string> GenerateReportAsync(string prompt, OllamaSettings settings)
    {
        if (!settings.Enabled)
        {
            Logger.LogError("Ollama is not enabled in settings");
            return null;
        }

        if (string.IsNullOrEmpty(settings.SelectedModel))
        {
            Logger.LogError("No Ollama model selected");
            return null;
        }

        try
        {
            var requestBody = new OllamaGenerateRequest
            {
                Model = settings.SelectedModel,
                Prompt = prompt,
                Stream = false,
                Options = new OllamaOptions
                {
                    Temperature = settings.Temperature,
                    NumPredict = settings.MaxTokens
                }
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds));
            var response = await _httpClient.PostAsync($"{settings.BaseUrl}/api/generate", content, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                Logger.LogError($"Ollama request failed: {response.StatusCode}");
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<OllamaGenerateResponse>(responseJson);

            return result?.Response;
        }
        catch (TaskCanceledException)
        {
            Logger.LogError("Ollama request timed out");
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to generate report with Ollama: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    ///     Generate a project report based on dataset information
    /// </summary>
    public async Task<string> GenerateProjectReportAsync(List<DatasetInfo> datasets, OllamaSettings settings)
    {
        var prompt = BuildReportPrompt(datasets);
        return await GenerateReportAsync(prompt, settings);
    }

    private string BuildReportPrompt(List<DatasetInfo> datasets)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a geoscientist expert. Based on the following datasets, generate a comprehensive project report.");
        sb.AppendLine("The report should analyze the data, provide insights, and make recommendations.");
        sb.AppendLine();
        sb.AppendLine("Available datasets:");
        sb.AppendLine();

        foreach (var dataset in datasets)
        {
            sb.AppendLine($"- Dataset: {dataset.Name}");
            sb.AppendLine($"  Type: {dataset.Type}");

            if (!string.IsNullOrEmpty(dataset.Description))
                sb.AppendLine($"  Description: {dataset.Description}");

            if (dataset.Metadata != null && dataset.Metadata.Count > 0)
            {
                sb.AppendLine("  Metadata:");
                foreach (var kvp in dataset.Metadata)
                {
                    sb.AppendLine($"    {kvp.Key}: {kvp.Value}");
                }
            }
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("Please provide:");
        sb.AppendLine("1. A summary of the available data");
        sb.AppendLine("2. Analysis of the datasets and their interrelationships");
        sb.AppendLine("3. Key findings and observations");
        sb.AppendLine("4. If borehole data is present, assess suitability for repurposing (e.g., oil & gas to geothermal energy)");
        sb.AppendLine("5. Recommendations for further analysis or actions");
        sb.AppendLine();
        sb.AppendLine("Format the report in a professional, structured manner with clear sections and headings.");

        return sb.ToString();
    }
}

/// <summary>
///     Information about a dataset for report generation
/// </summary>
public class DatasetInfo
{
    public string Name { get; set; }
    public string Type { get; set; }
    public string Description { get; set; }
    public Dictionary<string, string> Metadata { get; set; }
}

// Ollama API request/response models
internal class OllamaModelsResponse
{
    [JsonPropertyName("models")]
    public List<OllamaModel> Models { get; set; }
}

internal class OllamaModel
{
    [JsonPropertyName("name")]
    public string Name { get; set; }
}

internal class OllamaGenerateRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; }

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [JsonPropertyName("options")]
    public OllamaOptions Options { get; set; }
}

internal class OllamaOptions
{
    [JsonPropertyName("temperature")]
    public float Temperature { get; set; }

    [JsonPropertyName("num_predict")]
    public int NumPredict { get; set; }
}

internal class OllamaGenerateResponse
{
    [JsonPropertyName("response")]
    public string Response { get; set; }

    [JsonPropertyName("done")]
    public bool Done { get; set; }
}
