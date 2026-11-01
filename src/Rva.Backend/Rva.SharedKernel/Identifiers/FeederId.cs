namespace Rva.SharedKernel.Identifiers;

/// <summary>
/// Identifies a feeder (<c>topology.feeders.feeder_id</c>).
/// Any value is accepted: EF Core gives unsaved rows temporary (negative) keys.
/// </summary>
public readonly record struct FeederId
{
    public FeederId(long value) => Value = value;

    public long Value { get; }

    public static explicit operator long(FeederId id) => id.Value;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
