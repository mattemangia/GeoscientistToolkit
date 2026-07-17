// GAIA/Business/Vector2JsonConverter.cs

using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GAIA.Business;

public class Vector2JsonConverter : JsonConverter<Vector2>
{
    public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();

        float x = 0, y = 0;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return new Vector2(x, y);

            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var propertyName = reader.GetString();
                reader.Read();

                // Matched case-insensitively: a camel-cased naming policy never reaches a custom
                // converter's Write, so files can legitimately carry either spelling.
                if (string.Equals(propertyName, "X", StringComparison.OrdinalIgnoreCase))
                    x = reader.GetSingle();
                else if (string.Equals(propertyName, "Y", StringComparison.OrdinalIgnoreCase))
                    y = reader.GetSingle();
            }
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("X", value.X);
        writer.WriteNumber("Y", value.Y);
        writer.WriteEndObject();
    }
}
