using System.Globalization;
using System.Text.Json.Serialization;
using AutoBot.Models.Units.Converters;

namespace AutoBot.Models.Units;

[JsonConverter(typeof(SatoshiJsonConverter))]
public readonly struct Satoshi
{
    private readonly long _value;

    public Satoshi(long value) => _value = value;

    // Remove implicit conversion to long to force explicit conversions
    // This prevents ALL accidental truncation scenarios
    public static explicit operator long(Satoshi satoshi) => satoshi._value;

    public static implicit operator Satoshi(long value) => new(value);

    // Safe arithmetic operators (no division)
    public static Satoshi operator +(Satoshi left, Satoshi right) => new(left._value + right._value);

    public static Satoshi operator -(Satoshi left, Satoshi right) => new(left._value - right._value);

    public static Satoshi operator *(Satoshi left, long right) => new(left._value * right);

    public static Satoshi operator *(long left, Satoshi right) => new(left * right._value);

    // Comparison operators
    public static bool operator ==(Satoshi left, Satoshi right) => left._value == right._value;

    public static bool operator !=(Satoshi left, Satoshi right) => left._value != right._value;

    public static bool operator <(Satoshi left, Satoshi right) => left._value < right._value;

    public static bool operator <=(Satoshi left, Satoshi right) => left._value <= right._value;

    public static bool operator >(Satoshi left, Satoshi right) => left._value > right._value;

    public static bool operator >=(Satoshi left, Satoshi right) => left._value >= right._value;

    // Explicit access to value for when you really need it
    public long Value => _value;

    public override int GetHashCode() => _value.GetHashCode();

    public override bool Equals(object? obj) => obj is Satoshi other && _value == other._value;

    public override string ToString() => _value.ToString(CultureInfo.InvariantCulture);
}
