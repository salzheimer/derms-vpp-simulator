namespace Rva.SharedKernel.Identifiers;

/// <summary>
/// Identifies a neighborhood transformer (<c>topology.neighborhood_transformers.neighborhood_transformer_id</c>).
/// Any value is accepted: EF Core gives unsaved rows temporary (negative) keys.
/// </summary>
public readonly record struct TransformerId
{
    public TransformerId(long value) => Value = value;

    public long Value { get; }

    public static explicit operator long(TransformerId id) => id.Value;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
