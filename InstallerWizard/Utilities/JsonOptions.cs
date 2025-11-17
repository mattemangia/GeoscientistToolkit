using System.Text.Json;
using System.Text.Json.Serialization;

namespace GeoscientistToolkit.Installer.Utilities;

internal static class JsonOptions
{
    public static readonly Lazy<JsonSerializerOptions> Value = new(() =>
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    });
}
