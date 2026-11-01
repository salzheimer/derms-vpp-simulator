namespace Rva.Modules.GridTopology.Tests.Persistence;

/// <summary>D18: the utility time zone must be an IANA id. The host-level check (A-29) is in the integration tests.</summary>
public class GridTopologyOptionsTests
{
    private static readonly GridTopologyOptionsValidator Validator = new();

    [Fact]
    public void IANA_time_zone_is_valid() =>
        Assert.True(Validator.Validate(null, new GridTopologyOptions { UtilityTimeZone = "America/Los_Angeles" }).Succeeded);

    [Theory]
    [InlineData("", "GridTopology:UtilityTimeZone is required.")]
    [InlineData("Mars/Olympus_Mons", "GridTopology:UtilityTimeZone must be an IANA time zone id")]
    public void Missing_or_unknown_time_zone_is_rejected_naming_the_key(string value, string expected)
    {
        var result = Validator.Validate(null, new GridTopologyOptions { UtilityTimeZone = value });

        Assert.True(result.Failed);
        Assert.StartsWith(expected, result.FailureMessage, StringComparison.Ordinal);
    }
}
