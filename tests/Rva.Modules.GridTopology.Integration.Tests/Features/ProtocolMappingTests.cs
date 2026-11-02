using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure;
using Rva.SharedKernel.ReferenceCodes;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Integration.Tests.Features;

/// <summary>
/// B-03, B-04, B-10, and as-of reads: <see cref="IGridTopologyQueries.ResolveTechnologyTypeAsync"/> and
/// <see cref="IGridTopologyQueries.GetProtocolMappingsAsync"/> through the contract, on a database with the catalog
/// seed only (never the development topology, D12).
/// </summary>
public class ProtocolMappingTests(PostgresContainerFixture postgres)
{
    private static readonly ProtocolStandard OpenAdr3 = new("OpenADR_3.0");
    private static readonly ProtocolStandard Ieee2030_5 = new("IEEE_2030_5");

    [Fact]
    public async Task OpenADR_storage_battery_resolves_to_bess()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, module) = await CatalogDatabaseAsync(cancellationToken);
        await using var _ = module;
        await using var scope = module.CreateScope();

        var result = await Queries(scope).ResolveTechnologyTypeAsync(OpenAdr3, "Storage", "Battery", null, cancellationToken);

        Assert.Equal(new TechnologyTypeCode("bess"), result.Value);
    }

    [Fact]
    public async Task Mappings_for_a_standard_are_every_row_with_extra_attributes_intact()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (database, module) = await CatalogDatabaseAsync(cancellationToken);
        await using var _ = module;
        await using var scope = module.CreateScope();

        var mappings = (await Queries(scope).GetProtocolMappingsAsync(Ieee2030_5, null, cancellationToken)).Value;

        var expected = await ReadMappingsAsync(database, "IEEE_2030_5", cancellationToken);
        Assert.Equal(3, expected.Count);
        Assert.Equal(expected.Select(e => (e.Technology, e.Category, e.TypeCode)),
            mappings.Select(m => (m.Technology.Value, m.ResourceCategory, m.AssetTypeCode)));
        Assert.All(mappings, m => Assert.Equal(Ieee2030_5, m.Standard));
        // jsonb normalizes key order and spacing: compare the JSON values, not the text.
        Assert.All(mappings.Zip(expected), pair =>
        {
            Assert.NotNull(pair.First.ExtraAttributes);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(pair.Second.ExtraAttributes!), JsonNode.Parse(pair.First.ExtraAttributes!)),
                $"{pair.First.AssetTypeCode}: {pair.First.ExtraAttributes} != {pair.Second.ExtraAttributes}");
        });
    }

    [Fact]
    public async Task A_standard_with_no_mappings_returns_an_empty_list()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, module) = await CatalogDatabaseAsync(cancellationToken);
        await using var _ = module;
        await using var scope = module.CreateScope();

        var result = await Queries(scope).GetProtocolMappingsAsync(new ProtocolStandard("OpenADR_2.0b"), null, cancellationToken);

        Assert.Empty(result.Value);
    }

    [Theory]
    [InlineData("OpenADR_3.0", "Storage", "Flywheel")]       // unmapped type code
    [InlineData("OpenADR_3.0", "storage", "battery")]        // codes match exactly, not ignoring case
    [InlineData("SunSpec_Modbus", "Storage", "Battery")]     // unmapped standard
    public async Task An_unmapped_combination_is_not_found_never_a_default_technology(string standard, string category, string typeCode)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, module) = await CatalogDatabaseAsync(cancellationToken);
        await using var _ = module;
        await using var scope = module.CreateScope();

        var result = await Queries(scope).ResolveTechnologyTypeAsync(new ProtocolStandard(standard), category, typeCode, null, cancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal((ErrorKind.NotFound, GridTopologyErrors.ProtocolMappingNotFound), (result.Error.Kind, result.Error.Code));
    }

    [Fact]
    public async Task Resolving_as_of_an_earlier_time_uses_the_mapping_as_it_was_then()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (database, module) = await CatalogDatabaseAsync(cancellationToken);
        await using var _ = module;
        var before = await DatabaseNowAsync(database, cancellationToken);
        await database.ExecuteAsync("""
            UPDATE topology.protocol_mappings
            SET technology_type_id = (SELECT technology_type_id FROM topology.technology_types WHERE internal_code = 'evse')
            WHERE protocol_standard = 'OpenADR_3.0' AND resource_category = 'Storage' AND asset_type_code = 'Battery';
            """, cancellationToken);
        await using var scope = module.CreateScope();

        var then = await Queries(scope).ResolveTechnologyTypeAsync(OpenAdr3, "Storage", "Battery", before, cancellationToken);
        var now = await Queries(scope).ResolveTechnologyTypeAsync(OpenAdr3, "Storage", "Battery", null, cancellationToken);

        Assert.Equal(new TechnologyTypeCode("bess"), then.Value);
        Assert.Equal(new TechnologyTypeCode("evse"), now.Value);
    }

    [Fact]
    public async Task Mappings_as_of_an_earlier_time_leave_out_rows_added_since()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (database, module) = await CatalogDatabaseAsync(cancellationToken);
        await using var _ = module;
        var before = await DatabaseNowAsync(database, cancellationToken);
        await database.ExecuteAsync("""
            INSERT INTO topology.protocol_mappings (technology_type_id, protocol_standard, resource_category, asset_type_code)
            SELECT technology_type_id, 'IEEE_2030_5', 'DER', '81' FROM topology.technology_types WHERE internal_code = 'evse';
            """, cancellationToken);
        await using var scope = module.CreateScope();

        var then = await Queries(scope).GetProtocolMappingsAsync(Ieee2030_5, before, cancellationToken);
        var now = await Queries(scope).GetProtocolMappingsAsync(Ieee2030_5, null, cancellationToken);

        Assert.Equal(new[] { "4", "80", "82" }, then.Value.Select(m => m.AssetTypeCode));
        Assert.Equal(new[] { "4", "80", "81", "82" }, now.Value.Select(m => m.AssetTypeCode));
    }

    private static IGridTopologyQueries Queries(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IGridTopologyQueries>();

    private async Task<(TestDatabase Database, ModuleServices Module)> CatalogDatabaseAsync(CancellationToken cancellationToken)
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Empty, cancellationToken);
        await postgres.RunSqlFilesAsync(database, $"{PostgresContainerFixture.SeedDirectory}/catalog.sql");
        return (database, ModuleServices.Create(database.ConnectionString));
    }

    private static async Task<DateTimeOffset> DatabaseNowAsync(TestDatabase database, CancellationToken cancellationToken) =>
        new(await database.ScalarAsync<DateTime>("SELECT clock_timestamp()", cancellationToken));

    private static async Task<List<(string Technology, string Category, string TypeCode, string? ExtraAttributes)>> ReadMappingsAsync(
        TestDatabase database, string standard, CancellationToken cancellationToken)
    {
        var rows = new List<(string, string, string, string?)>();
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = new Npgsql.NpgsqlCommand("""
            SELECT t.internal_code, m.resource_category, m.asset_type_code, m.extra_attributes::text
            FROM topology.protocol_mappings m JOIN topology.technology_types t USING (technology_type_id)
            WHERE m.protocol_standard = $1
            ORDER BY m.resource_category, m.asset_type_code
            """, connection) { Parameters = { new() { Value = standard } } };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        return rows;
    }
}
