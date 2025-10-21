namespace AutoBot.Models.Units;

public readonly struct Dollar
{
    private readonly decimal _value;

    public Dollar(decimal value) => _value = value;

    public static implicit operator decimal(Dollar dollar) => dollar._value;

    public static implicit operator Dollar(decimal value) => new(value);

    public override string ToString() => _value.ToString("F2");
}
