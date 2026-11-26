using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyAdventure.Core.Numerics;

public sealed class BigDoubleJsonConverter : JsonConverter<BigDouble>
{
    public override bool HandleNull => true;

    public override BigDouble Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return BigDouble.TryParse(reader.GetString(), out var parsed) ? parsed : BigDouble.Zero;
            case JsonTokenType.Number:
                return new BigDouble(reader.GetDouble());
            case JsonTokenType.StartObject or JsonTokenType.StartArray:
                reader.Skip();
                return BigDouble.Zero;
            default:
                return BigDouble.Zero;
        }
    }

    public override void Write(Utf8JsonWriter writer, BigDouble value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToCanonicalString());
}
