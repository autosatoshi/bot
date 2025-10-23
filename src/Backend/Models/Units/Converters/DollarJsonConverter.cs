using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoBot.Models.Units.Converters;

public class DollarJsonConverter : JsonConverter<Dollar>
{
    public override Dollar Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return new Dollar(reader.GetDecimal());
    }

    public override void Write(Utf8JsonWriter writer, Dollar value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue((decimal)value);
    }
}
