using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoBot.Models.Units;

[JsonConverter(typeof(DollarJsonConverter))]
public readonly struct Dollar
{
    private readonly decimal _value;

    public Dollar(decimal value) => _value = value;

    public static implicit operator decimal(Dollar dollar) => dollar._value;

    public static implicit operator Dollar(decimal value) => new(value);

    public override string ToString() => _value.ToString("F2", CultureInfo.InvariantCulture);
}

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
