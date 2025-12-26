// GeoscientistToolkit/Business/QuaternionJsonConverter.cs

using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

public class QuaternionJsonConverter : JsonConverter<Quaternion>
{
    public override Quaternion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException();

        float x = 0, y = 0, z = 0, w = 1;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return new Quaternion(x, y, z, w);

            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var propertyName = reader.GetString();
                reader.Read();
                switch (propertyName)
                {
                    case "X": x = reader.GetSingle(); break;
                    case "Y": y = reader.GetSingle(); break;
                    case "Z": z = reader.GetSingle(); break;
                    case "W": w = reader.GetSingle(); break;
                }
            }
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, Quaternion value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("X", value.X);
        writer.WriteNumber("Y", value.Y);
        writer.WriteNumber("Z", value.Z);
        writer.WriteNumber("W", value.W);
        writer.WriteEndObject();
    }
}
