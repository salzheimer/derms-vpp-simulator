namespace Rva.SharedKernel.Units;

/// <summary>Energy in kilowatt-hours. Any finite value is allowed; NaN and infinity are rejected.</summary>
public readonly record struct KilowattHours : IComparable<KilowattHours>
{
    public KilowattHours(double value)
    {
        UnitGuard.Finite(value);
        Value = value;
    }

    public double Value { get; }

    public static explicit operator double(KilowattHours quantity) => quantity.Value;

    public int CompareTo(KilowattHours other) => Value.CompareTo(other.Value);

    public static bool operator <(KilowattHours left, KilowattHours right) => left.Value < right.Value;
    public static bool operator >(KilowattHours left, KilowattHours right) => left.Value > right.Value;
    public static bool operator <=(KilowattHours left, KilowattHours right) => left.Value <= right.Value;
    public static bool operator >=(KilowattHours left, KilowattHours right) => left.Value >= right.Value;

    public override string ToString() => $"{Value.ToString(System.Globalization.CultureInfo.InvariantCulture)} kWh";
}
