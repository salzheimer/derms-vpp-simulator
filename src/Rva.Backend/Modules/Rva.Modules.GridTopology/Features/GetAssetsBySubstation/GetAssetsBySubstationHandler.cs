using Microsoft.EntityFrameworkCore;
using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Contracts.Snapshots;
using Rva.Modules.GridTopology.Features.Reads;
using Rva.Modules.GridTopology.Persistence;
using Rva.SharedKernel.Identifiers;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Features.GetAssetsBySubstation;

/// <summary>
/// <see cref="IGridTopologyQueries.GetAssetsBySubstationAsync"/>: every asset under a substation, a page at a time in
/// asset id order. Contract only, no endpoint. Every page reads the topology as of one time, pinned by the first page
/// (to the database clock when the caller gives none) and carried in the cursor, so paging through a changing topology
/// neither skips nor repeats an asset.
/// </summary>
internal sealed class GetAssetsBySubstationHandler(GridTopologyDbContext db, AssetReader reader, TimeProvider time)
{
    public async Task<Result<AssetPage>> HandleAsync(
        string substationCode, DateTimeOffset? asOf, string? cursor, int pageSize, CancellationToken cancellationToken)
    {
        if (pageSize is < 1 or > IGridTopologyQueries.MaxAssetPageSize)
            return Error.Validation(GridTopologyErrors.PageSizeInvalid, $"pageSize must be 1 to {IGridTopologyQueries.MaxAssetPageSize}.");
        AssetPageCursor? after = null;
        if (cursor is not null)
        {
            // A pinned time came from the database clock, which may run slightly ahead of this one: not checked again.
            after = AssetPageCursor.TryDecode(cursor);
            if (after is null || (asOf is { } given && AssetPageCursor.ToMicros(given) != after.AsOfMicros))
                return CursorInvalid();
        }
        else if (AsOf.CheckNotInFuture(asOf, time) is { } invalid)
        {
            return invalid;
        }

        var at = after?.AsOf ?? asOf ?? await AsOfReads.DatabaseNowAsync(db, cancellationToken);

        var substation = await AsOfReads.Substations(db, at).Where(s => s.Code == substationCode)
            .Select(s => (SubstationId?)s.Id).SingleOrDefaultAsync(cancellationToken);
        if (substation is not { } substationId)
            return Error.NotFound(GridTopologyErrors.SubstationNotFound, $"No substation has code {substationCode} as of {AsOf.Format(at)}.");
        if (after is not null && after.SubstationId != substationId.Value)
            return CursorInvalid();

        // One page and one row more, to know whether another page follows. The subtree is resolved as of the pinned time
        // at every level, so an asset is listed under the substation its path led to then.
        var ids = await AssetIdsUnderAsync(substationId.Value, at, after?.AfterAssetId ?? 0, pageSize + 1, cancellationToken);
        var hasMore = ids.Count > pageSize;
        var pageIds = ids.Take(pageSize).Select(id => new AssetId(id)).ToList();

        var read = await reader.ReadAsync(assets => assets.Where(a => pageIds.Contains(a.Id)), at, cancellationToken);
        if (read.IsFailure)
            return read.Error;

        var page = read.Value.Select(r => r.Asset).OrderBy(a => a.Id.Value).ToList();
        var next = hasMore
            ? new AssetPageCursor(substationId.Value, AssetPageCursor.ToMicros(at), pageIds[^1].Value).Encode()
            : null;
        return new AssetPage(page, at, next);
    }

    private Task<List<long>> AssetIdsUnderAsync(long substationId, DateTimeOffset at, long afterAssetId, int limit, CancellationToken cancellationToken)
    {
        var point = at.ToUniversalTime();
        return db.Database.SqlQuery<long>($"""
            WITH feeders AS (
                SELECT feeder_id FROM topology.feeders_as_of({point}) WHERE substation_id = {substationId}),
            transformers AS (
                SELECT neighborhood_transformer_id FROM topology.neighborhood_transformers_as_of({point})
                WHERE feeder_id IN (SELECT feeder_id FROM feeders)),
            premises AS (
                SELECT premise_id FROM topology.premises_as_of({point})
                WHERE neighborhood_transformer_id IN (SELECT neighborhood_transformer_id FROM transformers)
                   OR feeder_id IN (SELECT feeder_id FROM feeders))
            SELECT a.asset_id AS "Value"
            FROM topology.assets_as_of({point}) a
            WHERE (a.substation_id = {substationId}
                   OR a.feeder_id IN (SELECT feeder_id FROM feeders)
                   OR a.neighborhood_transformer_id IN (SELECT neighborhood_transformer_id FROM transformers)
                   OR a.premise_id IN (SELECT premise_id FROM premises))
              AND a.asset_id > {afterAssetId}
            ORDER BY a.asset_id
            LIMIT {limit}
            """).ToListAsync(cancellationToken);
    }

    private static Error CursorInvalid() =>
        Error.Validation(GridTopologyErrors.PageCursorInvalid,
            "The cursor is not one this listing issued. Pass the NextCursor of the previous page, with the same substation and asOf.");
}
