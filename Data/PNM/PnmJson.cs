// GAIA/Data/Pnm/PnmJson.cs

using System.Text.Json;
using System.Text.Json.Serialization;
using GAIA.Business;

namespace GAIA.Data.Pnm;

/// <summary>
///     Serializer options shared by every stand-alone PNM JSON read and write.
///     Vector3 exposes X/Y/Z as fields, which System.Text.Json skips unless a converter is
///     registered: without it pore positions are written as "{}" and read back as (0,0,0),
///     collapsing the whole network onto the origin. ProjectSerializer registers the same
///     converter for project files, so both paths must stay in agreement.
/// </summary>
public static class PnmJson
{
    public static readonly JsonSerializerOptions Indented = Create(null);

    /// <summary> Used by the dual-network export, whose on-disk schema is camel-cased. </summary>
    public static readonly JsonSerializerOptions CamelCase = Create(JsonNamingPolicy.CamelCase);

    private static JsonSerializerOptions Create(JsonNamingPolicy namingPolicy)
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = namingPolicy,
            PropertyNameCaseInsensitive = true,
            // A degenerate or tiny network can leave a derived quantity (a permeability, a tortuosity)
            // non-finite. Persist it as a named literal instead of throwing: losing the whole file to
            // one NaN would be a worse failure than recording the NaN.
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters = { new Vector3JsonConverter(), new Vector2JsonConverter() }
        };
    }
}
