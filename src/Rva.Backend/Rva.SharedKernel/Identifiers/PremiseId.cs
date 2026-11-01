namespace Rva.SharedKernel.Identifiers;

/// <summary>
/// Identifies a premise (<c>topology.premises.premise_id</c>).
/// Any value is accepted: EF Core gives unsaved rows temporary (negative) keys.
/// </summary>
public readonly record struct PremiseId
{
    public PremiseId(long value) => Value = value;

    public long Value { get; }

    public static explicit operator long(PremiseId id) => id.Value;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
