using Rva.SharedKernel.Identifiers;
using Rva.SharedKernel.ReferenceCodes;
using Rva.SharedKernel.Units;

namespace Rva.Modules.GridTopology.Tests.SharedKernel;

/// <summary>A-02 (typed ids and units: equality, conversions) and A-13 (units reject NaN and infinity).</summary>
public class IdentifierAndUnitTests
{
    [Fact]
    public void Typed_ids_with_equal_values_are_equal()
    {
        Assert.Equal(new AssetId(7), new AssetId(7));
        Assert.NotEqual(new AssetId(7), new AssetId(8));
        Assert.Equal(7L, (long)new GridNodeId(7));
        Assert.Equal("7", new SubstationId(7).ToString());
    }

    [Fact]
    public void Different_id_types_are_not_interchangeable() =>
        Assert.False(new FeederId(7).Equals((object)new SubstationId(7)));

    [Fact]
    public void Kilowatts_keep_their_sign()
    {
        var charging = new Kilowatts(-7.6);

        Assert.Equal(-7.6, charging.Value);
        Assert.Equal(-7.6, (double)charging);
        Assert.True(charging < new Kilowatts(0));
        Assert.Equal(new Kilowatts(-7.6), charging);
    }

    [Fact]
    public void Units_compare_by_value()
    {
        Assert.Equal(new KilovoltAmperes(7.6), new KilovoltAmperes(7.6));
        Assert.True(new KilowattHours(20) > new KilowattHours(10));
        Assert.Equal(-1, new Kilowatts(1).CompareTo(new Kilowatts(2)));
        Assert.Equal("7.6 kVA", new KilovoltAmperes(7.6).ToString());
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Units_reject_NaN_and_infinity(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Kilowatts(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KilowattHours(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KilovoltAmperes(value));
    }

    [Fact]
    public void Reference_codes_wrap_a_non_blank_code_and_compare_ordinally()
    {
        Assert.Equal(new TechnologyTypeCode("bess"), new TechnologyTypeCode("bess"));
        Assert.NotEqual(new InterconnectionTypeCode("btm"), new InterconnectionTypeCode("BTM"));
        Assert.Throws<ArgumentException>(() => new ProtocolStandard("  "));
    }
}
