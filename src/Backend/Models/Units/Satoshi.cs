namespace AutoBot.Models.Units;

public readonly struct Satoshi
{
    private readonly long _value;

    public Satoshi(long value) => _value = value;

    public static implicit operator long(Satoshi satoshi) => satoshi._value;

    public static implicit operator Satoshi(long value) => new(value);

    public override string ToString() => _value.ToString();
}
