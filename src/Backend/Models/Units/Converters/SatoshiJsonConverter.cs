using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoBot.Models.Units.Converters;

public class SatoshiJsonConverter : JsonConverter<Satoshi>
{
    public override Satoshi Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return new Satoshi(reader.GetInt64());
    }

    public override void Write(Utf8JsonWriter writer, Satoshi value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue((long)value);
    }
}
