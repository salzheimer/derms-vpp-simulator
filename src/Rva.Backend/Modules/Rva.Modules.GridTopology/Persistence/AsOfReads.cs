using Microsoft.EntityFrameworkCore;
using Rva.Modules.GridTopology.Domain.Assets;
using Rva.Modules.GridTopology.Domain.Catalog;
using Rva.Modules.GridTopology.Domain.Hierarchy;

namespace Rva.Modules.GridTopology.Persistence;

/// <summary>
/// The topology tables as they are now, or as they were at a point in time through the <c>&lt;table&gt;_as_of</c>
/// functions. Every read in the module goes through here, and always untracked.
/// <list type="bullet">
/// <item>Never use navigations or <c>Include</c> on these queries: EF joins the main tables for them, so related rows
/// would come back current, not as of the time. Read each table through its own method and join by id.</item>
/// <item>The <c>_as_of</c> functions return the table's row type, which has no <c>xmin</c>. Tables mapped with a row
/// version get a placeholder <c>xmin</c> of 0, harmless because as-of rows are never saved.</item>
/// </list>
/// </summary>
internal static class AsOfReads
{
    public static IQueryable<Asset> Assets(GridTopologyDbContext db, DateTimeOffset? asOf) =>
        asOf is { } at
            ? db.Assets.FromSql($"SELECT *, '0'::xid AS xmin FROM topology.assets_as_of({at.ToUniversalTime()})").AsNoTracking()
            : db.Assets.AsNoTracking();

    public static IQueryable<AssetComponent> AssetComponents(GridTopologyDbContext db, DateTimeOffset? asOf) =>
        asOf is { } at
            ? db.AssetComponents.FromSql($"SELECT *, '0'::xid AS xmin FROM topology.asset_components_as_of({at.ToUniversalTime()})").AsNoTracking()
            : db.AssetComponents.AsNoTracking();

    public static IQueryable<Substation> Substations(GridTopologyDbContext db, DateTimeOffset? asOf) =>
        asOf is { } at
            ? db.Substations.FromSql($"SELECT * FROM topology.substations_as_of({at.ToUniversalTime()})").AsNoTracking()
            : db.Substations.AsNoTracking();

    public static IQueryable<Feeder> Feeders(GridTopologyDbContext db, DateTimeOffset? asOf) =>
        asOf is { } at
            ? db.Feeders.FromSql($"SELECT * FROM topology.feeders_as_of({at.ToUniversalTime()})").AsNoTracking()
            : db.Feeders.AsNoTracking();

    public static IQueryable<NeighborhoodTransformer> Transformers(GridTopologyDbContext db, DateTimeOffset? asOf) =>
        asOf is { } at
            ? db.NeighborhoodTransformers.FromSql($"SELECT * FROM topology.neighborhood_transformers_as_of({at.ToUniversalTime()})").AsNoTracking()
            : db.NeighborhoodTransformers.AsNoTracking();

    public static IQueryable<Premise> Premises(GridTopologyDbContext db, DateTimeOffset? asOf) =>
        asOf is { } at
            ? db.Premises.FromSql($"SELECT * FROM topology.premises_as_of({at.ToUniversalTime()})").AsNoTracking()
            : db.Premises.AsNoTracking();

    public static IQueryable<Microgrid> Microgrids(GridTopologyDbContext db, DateTimeOffset? asOf) =>
        asOf is { } at
            ? db.Microgrids.FromSql($"SELECT * FROM topology.microgrids_as_of({at.ToUniversalTime()})").AsNoTracking()
            : db.Microgrids.AsNoTracking();

    public static IQueryable<HardwareProfile> HardwareProfiles(GridTopologyDbContext db, DateTimeOffset? asOf) =>
        asOf is { } at
            ? db.HardwareProfiles.FromSql($"SELECT * FROM topology.hardware_profiles_as_of({at.ToUniversalTime()})").AsNoTracking()
            : db.HardwareProfiles.AsNoTracking();

    public static IQueryable<ProtocolMapping> ProtocolMappings(GridTopologyDbContext db, DateTimeOffset? asOf) =>
        asOf is { } at
            ? db.ProtocolMappings.FromSql($"SELECT * FROM topology.protocol_mappings_as_of({at.ToUniversalTime()})").AsNoTracking()
            : db.ProtocolMappings.AsNoTracking();

    public static IQueryable<TechnologyType> TechnologyTypes(GridTopologyDbContext db, DateTimeOffset? asOf) =>
        asOf is { } at
            ? db.TechnologyTypes.FromSql($"SELECT * FROM topology.technology_types_as_of({at.ToUniversalTime()})").AsNoTracking()
            : db.TechnologyTypes.AsNoTracking();

    /// <summary>The database's clock, which stamps every version: the time "now" means for a pinned read.</summary>
    public static async Task<DateTimeOffset> DatabaseNowAsync(GridTopologyDbContext db, CancellationToken cancellationToken) =>
        new(DateTime.SpecifyKind(
            await db.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(cancellationToken),
            DateTimeKind.Utc));
}
