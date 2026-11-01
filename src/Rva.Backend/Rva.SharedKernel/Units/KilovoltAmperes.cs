namespace Rva.SharedKernel.Units;

/// <summary>Apparent power in kilovolt-amperes. Any finite value is allowed; NaN and infinity are rejected.</summary>
public readonly record struct KilovoltAmperes : IComparable<KilovoltAmperes>
{
    public KilovoltAmperes(double value)
    {
        UnitGuard.Finite(value);
        Value = value;
    }

    public double Value { get; }

    public static explicit operator double(KilovoltAmperes quantity) => quantity.Value;

    public int CompareTo(KilovoltAmperes other) => Value.CompareTo(other.Value);

    public static bool operator <(KilovoltAmperes left, KilovoltAmperes right) => left.Value < right.Value;
    public static bool operator >(KilovoltAmperes left, KilovoltAmperes right) => left.Value > right.Value;
    public static bool operator <=(KilovoltAmperes left, KilovoltAmperes right) => left.Value <= right.Value;
    public static bool operator >=(KilovoltAmperes left, KilovoltAmperes right) => left.Value >= right.Value;

    public override string ToString() => $"{Value.ToString(System.Globalization.CultureInfo.InvariantCulture)} kVA";
}
