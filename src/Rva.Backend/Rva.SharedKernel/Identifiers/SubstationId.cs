namespace Rva.SharedKernel.Identifiers;

/// <summary>
/// Identifies a substation (<c>topology.substations.substation_id</c>).
/// Any value is accepted: EF Core gives unsaved rows temporary (negative) keys.
/// </summary>
public readonly record struct SubstationId
{
    public SubstationId(long value) => Value = value;

    public long Value { get; }

    public static explicit operator long(SubstationId id) => id.Value;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
