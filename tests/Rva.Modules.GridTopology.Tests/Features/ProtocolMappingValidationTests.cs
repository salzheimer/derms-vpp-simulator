using Microsoft.EntityFrameworkCore;
using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Features.ProtocolMappings;
using Rva.Modules.GridTopology.Persistence;
using Rva.SharedKernel.ReferenceCodes;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Tests.Features;

/// <summary>
/// B-11: a protocol mapping query without a standard, or with a blank resource category or asset type code, is a
/// Validation error before any database call. A blank or whitespace standard cannot be constructed at all: the shared
/// kernel's <see cref="ProtocolStandard"/> rejects it, so the handlers see only <c>default(ProtocolStandard)</c>.
/// </summary>
public class ProtocolMappingValidationTests
{
    // Nothing listens here; opening a connection would throw, so a passing test proves no database call was made.
    private const string NoDatabase = "Host=127.0.0.1;Port=1;Database=none;Username=none;Timeout=1";

    private static readonly ProtocolStandard OpenAdr3 = new("OpenADR_3.0");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_or_whitespace_standard_cannot_be_constructed(string value) =>
        Assert.Throws<ArgumentException>(() => new ProtocolStandard(value));

    [Fact]
    public async Task Resolving_without_a_standard_is_a_validation_error()
    {
        await using var db = CreateContext();

        var result = await new ResolveTechnologyTypeHandler(db).HandleAsync(default, "Storage", "Battery", null, TestContext.Current.CancellationToken);

        AssertInvalid(result);
    }

    [Theory]
    [InlineData("", "Battery")]
    [InlineData("   ", "Battery")]
    [InlineData("Storage", "")]
    [InlineData("Storage", "\t")]
    public async Task Resolving_with_a_blank_category_or_type_code_is_a_validation_error(string category, string typeCode)
    {
        await using var db = CreateContext();

        var result = await new ResolveTechnologyTypeHandler(db).HandleAsync(OpenAdr3, category, typeCode, null, TestContext.Current.CancellationToken);

        AssertInvalid(result);
    }

    [Fact]
    public async Task Listing_mappings_without_a_standard_is_a_validation_error()
    {
        await using var db = CreateContext();

        var result = await new GetProtocolMappingsHandler(db).HandleAsync(default, null, TestContext.Current.CancellationToken);

        AssertInvalid(result);
    }

    private static GridTopologyDbContext CreateContext() =>
        new(((DbContextOptionsBuilder<GridTopologyDbContext>)GridTopologyDbContext.Configure(new DbContextOptionsBuilder<GridTopologyDbContext>(), NoDatabase)).Options);

    private static void AssertInvalid(Result result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal((ErrorKind.Validation, GridTopologyErrors.ProtocolMappingQueryInvalid), (result.Error.Kind, result.Error.Code));
    }
}
