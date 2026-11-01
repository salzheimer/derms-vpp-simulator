namespace Rva.SharedKernel.Identifiers;

/// <summary>
/// Identifies a microgrid (<c>topology.microgrids.microgrid_id</c>).
/// Any value is accepted: EF Core gives unsaved rows temporary (negative) keys.
/// </summary>
public readonly record struct MicrogridId
{
    public MicrogridId(long value) => Value = value;

    public long Value { get; }

    public static explicit operator long(MicrogridId id) => id.Value;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
