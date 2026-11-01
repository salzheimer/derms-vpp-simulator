namespace Rva.SharedKernel.ReferenceCodes;

/// <summary>
/// The code of a technology type (<c>topology.technology_types.internal_code</c>), ex. <c>bess</c>, <c>solar_pv</c>. A lookup-table code, not an enum, so adding a row needs no code change; the owning module
/// validates it against its table. Compared ordinally.
/// </summary>
public readonly record struct TechnologyTypeCode
{
    public TechnologyTypeCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
