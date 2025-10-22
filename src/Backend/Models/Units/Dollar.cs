using System.Globalization;
using System.Text.Json.Serialization;
using AutoBot.Models.Units.Converters;

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
