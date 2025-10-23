using System.Globalization;
using System.Text.Json.Serialization;
using AutoBot.Models.Units.Converters;

namespace AutoBot.Models.Units;

[JsonConverter(typeof(DollarJsonConverter))]
public readonly struct Dollar
{
    private readonly decimal _value;

    public Dollar(decimal value)
    {
        if ((value * 100) % 1 != 0)
        {
            throw new ArgumentException($"Dollar amounts can only have up to 2 decimal places. Got: {value}");
        }

        _value = value;
    }

    public static implicit operator decimal(Dollar dollar) => dollar._value;

    public static implicit operator Dollar(decimal value) => new(value);

    // Explicit access to value for when you really need it
    public decimal Value => _value;

    public override string ToString() => _value.ToString("F2", CultureInfo.InvariantCulture);
}
