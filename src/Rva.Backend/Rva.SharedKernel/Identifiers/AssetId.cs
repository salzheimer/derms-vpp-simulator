namespace Rva.SharedKernel.Identifiers;

/// <summary>
/// Identifies an asset (<c>topology.assets.asset_id</c>). Other modules refer to assets by it or by asset code.
/// Any value is accepted: EF Core gives unsaved rows temporary (negative) keys.
/// </summary>
public readonly record struct AssetId
{
    public AssetId(long value) => Value = value;

    public long Value { get; }

    public static explicit operator long(AssetId id) => id.Value;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
