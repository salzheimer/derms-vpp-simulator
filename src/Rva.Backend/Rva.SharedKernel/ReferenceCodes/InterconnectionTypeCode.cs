namespace Rva.SharedKernel.ReferenceCodes;

/// <summary>
/// The code of an interconnection type (<c>topology.interconnection_types.internal_code</c>): <c>btm</c>, <c>ftm</c>, or <c>atm</c>. A lookup-table code, not an enum, so adding a row needs no code change; the owning module
/// validates it against its table. Compared ordinally.
/// </summary>
public readonly record struct InterconnectionTypeCode
{
    public InterconnectionTypeCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
