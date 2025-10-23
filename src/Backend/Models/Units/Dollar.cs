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

    public static explicit operator decimal(Dollar dollar) => dollar._value;

    public static implicit operator Dollar(decimal value) => new(value);

    // Safe arithmetic operators (no division, no multiplication)
    public static Dollar operator +(Dollar left, Dollar right) => new(left._value + right._value);

    public static Dollar operator -(Dollar left, Dollar right) => new(left._value - right._value);

    // Comparison operators
    public static bool operator ==(Dollar left, Dollar right) => left._value == right._value;

    public static bool operator !=(Dollar left, Dollar right) => left._value != right._value;

    public static bool operator <(Dollar left, Dollar right) => left._value < right._value;

    public static bool operator <=(Dollar left, Dollar right) => left._value <= right._value;

    public static bool operator >(Dollar left, Dollar right) => left._value > right._value;

    public static bool operator >=(Dollar left, Dollar right) => left._value >= right._value;

    // Explicit access to value for when you really need it
    public decimal Value => _value;

    public override int GetHashCode() => _value.GetHashCode();

    public override bool Equals(object? obj) => obj is Dollar other && _value == other._value;

    public override string ToString() => _value.ToString("F2", CultureInfo.InvariantCulture);
}
