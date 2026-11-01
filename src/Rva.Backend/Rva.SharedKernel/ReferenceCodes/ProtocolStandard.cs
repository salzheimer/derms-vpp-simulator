namespace Rva.SharedKernel.ReferenceCodes;

/// <summary>
/// The code of an external protocol standard and version (<c>topology.protocol_mappings.protocol_standard</c>), ex. <c>OpenADR_3.0</c>, <c>IEEE_2030_5</c>. A lookup-table code, not an enum, so adding a row needs no code change; the owning module
/// validates it against its table. Compared ordinally.
/// </summary>
public readonly record struct ProtocolStandard
{
    public ProtocolStandard(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
