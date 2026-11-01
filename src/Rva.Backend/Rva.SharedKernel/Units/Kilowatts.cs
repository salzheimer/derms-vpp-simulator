namespace Rva.SharedKernel.Units;

/// <summary>Real power in kilowatts. Signed: positive = discharging or export, negative = charging or import. Any finite value is allowed; NaN and infinity are rejected.</summary>
public readonly record struct Kilowatts : IComparable<Kilowatts>
{
    public Kilowatts(double value)
    {
        UnitGuard.Finite(value);
        Value = value;
    }

    public double Value { get; }

    public static explicit operator double(Kilowatts quantity) => quantity.Value;

    public int CompareTo(Kilowatts other) => Value.CompareTo(other.Value);

    public static bool operator <(Kilowatts left, Kilowatts right) => left.Value < right.Value;
    public static bool operator >(Kilowatts left, Kilowatts right) => left.Value > right.Value;
    public static bool operator <=(Kilowatts left, Kilowatts right) => left.Value <= right.Value;
    public static bool operator >=(Kilowatts left, Kilowatts right) => left.Value >= right.Value;

    public override string ToString() => $"{Value.ToString(System.Globalization.CultureInfo.InvariantCulture)} kW";
}
