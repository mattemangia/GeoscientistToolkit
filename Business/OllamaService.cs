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

        // System prompt
        sb.AppendLine("You are an experienced geoscientist and technical report writer specializing in subsurface analysis, geothermal energy, and resource assessment.");
        sb.AppendLine();
        sb.AppendLine("TASK: Draft a comprehensive technical project report based on the datasets provided below.");
        sb.AppendLine();

        // Dataset inventory
        sb.AppendLine("=== AVAILABLE DATASETS ===");
        sb.AppendLine();

        foreach (var dataset in datasets)
        {
            sb.AppendLine($"Dataset: {dataset.Name}");
            sb.AppendLine($"Type: {dataset.Type}");

            if (!string.IsNullOrEmpty(dataset.Description))
                sb.AppendLine($"Description: {dataset.Description}");

            if (dataset.Metadata != null && dataset.Metadata.Count > 0)
            {
                sb.AppendLine("Properties:");
                foreach (var kvp in dataset.Metadata)
                {
                    sb.AppendLine($"  • {kvp.Key}: {kvp.Value}");
                }
            }
            sb.AppendLine();
        }

        // Report requirements
        sb.AppendLine("=== REPORT REQUIREMENTS ===");
        sb.AppendLine();
        sb.AppendLine("Your report must include the following sections:");
        sb.AppendLine();
        sb.AppendLine("1. EXECUTIVE SUMMARY");
        sb.AppendLine("   - Brief overview of the project and available data");
        sb.AppendLine("   - Key findings in 2-3 sentences");
        sb.AppendLine();
        sb.AppendLine("2. DATA INVENTORY");
        sb.AppendLine("   - Summarize the types and quality of data available");
        sb.AppendLine("   - Note any data gaps or limitations");
        sb.AppendLine();
        sb.AppendLine("3. TECHNICAL ANALYSIS");
        sb.AppendLine("   - Analyze relationships between different datasets");
        sb.AppendLine("   - Identify patterns, trends, or anomalies");
        sb.AppendLine("   - For simulation data (permeability, acoustic, geothermal): interpret physical implications");
        sb.AppendLine();
        sb.AppendLine("4. BOREHOLE & GEOTHERMAL ASSESSMENT (if applicable)");
        sb.AppendLine("   - If borehole data exists: evaluate depth, lithology, and geothermal potential");
        sb.AppendLine("   - Assess suitability for repurposing from oil & gas to geothermal energy production");
        sb.AppendLine("   - Specify potential geothermal technology types (e.g., direct use, binary cycle, enhanced geothermal systems)");
        sb.AppendLine("   - Consider temperature gradients, permeability, and reservoir characteristics");
        sb.AppendLine();
        sb.AppendLine("5. RECOMMENDATIONS");
        sb.AppendLine("   - Suggest further data collection or analysis");
        sb.AppendLine("   - Propose next steps for project development");
        sb.AppendLine("   - Highlight areas requiring expert review");
        sb.AppendLine();
        sb.AppendLine("FORMAT: Use clear headings, bullet points where appropriate, and professional technical language.");
        sb.AppendLine("LENGTH: Aim for a comprehensive report of 500-1000 words.");
        sb.AppendLine();
        sb.AppendLine("Begin drafting the report now:");

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
