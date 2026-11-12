using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rva.Modules.GridTopology.Http;
using Rva.Modules.GridTopology.Persistence;
using Rva.SharedKernel.Results;
using Rva.SharedKernel.Units;

namespace Rva.Modules.GridTopology.Integration.Tests.Infrastructure;

/// <summary>
/// Test-only endpoints that drive module internals through the real host pipeline, for the failure tests that need a
/// request before the feature endpoints exist (plan p1-s01c onward). Added after the application's own pipeline, so
/// they run inside its exception handler exactly as a module endpoint would.
/// </summary>
internal sealed class TestEndpoints : IStartupFilter
{
    public const string Throw = "/_test/throw";
    public const string Read = "/_test/read";
    public const string Lookup = "/_test/lookup-wind";
    public const string UpdateAsset = "/_test/update-asset";
    public const string BreakAssetRating = "/_test/break-asset-rating";
    public const string TamperGridStateEvent = "/_test/tamper-grid-state-event";

    /// <summary>Text in the exception <see cref="Throw"/> raises; must never reach a response.</summary>
    public const string SecretDetail = "SECRET-DETAIL-7f3a";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);

        app.Map(Throw, branch => branch.Run(_ => throw new InvalidOperationException($"Unexpected failure {SecretDetail}")));

        app.Map(Read, branch => branch.Run(async context =>
        {
            var db = context.RequestServices.GetRequiredService<GridTopologyDbContext>();
            var count = await db.Substations.CountAsync(context.RequestAborted);
            await context.Response.WriteAsync(count.ToString(System.Globalization.CultureInfo.InvariantCulture), context.RequestAborted);
        }));

        app.Map(Lookup, branch => branch.Run(async context =>
        {
            var id = await context.RequestServices.GetRequiredService<LookupCatalog>().IdOfAsync(LookupTable.TechnologyTypes, "wind", context.RequestAborted);
            await context.Response.WriteAsync(id.ToString(System.Globalization.CultureInfo.InvariantCulture), context.RequestAborted);
        }));

        // A valid change to fixture A's asset (p_max 7.6 -> 7.0 kW).
        app.Map(UpdateAsset, branch => branch.Run(context => ChangeFixtureAssetAsync(context, "test: update asset",
            (db, asset) => db.Entry(asset).Property(a => a.RatedPMax).CurrentValue = new Kilowatts(7.0))));

        // FI-5: a write the domain would never allow (rated kVA below the asset's power), so only the database rejects it.
        app.Map(BreakAssetRating, branch => branch.Run(context => ChangeFixtureAssetAsync(context, "test: kVA below power",
            (db, asset) => db.Entry(asset).Property(a => a.RatedKva).CurrentValue = new KilovoltAmperes(1))));

        // FI-5 on the append-only grid state events (D-20): edit the earliest event's reason through the unit of work,
        // a change the module offers no path for, so only the database's append-only trigger rejects it.
        app.Map(TamperGridStateEvent, branch => branch.Run(async context =>
        {
            var db = context.RequestServices.GetRequiredService<GridTopologyDbContext>();
            var result = await context.RequestServices.GetRequiredService<UnitOfWork>().ExecuteAsync("test: tamper grid state event", async cancellationToken =>
            {
                var gridStateEvent = await db.GridStateEvents.OrderBy(e => e.Id).FirstAsync(cancellationToken);
                db.Entry(gridStateEvent).Property(e => e.Reason).CurrentValue = "tampered";
                return Result.Success();
            }, context.RequestAborted);

            if (result.IsFailure)
                await result.Error.ToProblem().ExecuteAsync(context);
            else
                context.Response.StatusCode = StatusCodes.Status204NoContent;
        }));
    };

    private static async Task ChangeFixtureAssetAsync(HttpContext context, string reason,
        Action<GridTopologyDbContext, Domain.Assets.Asset> change)
    {
        var db = context.RequestServices.GetRequiredService<GridTopologyDbContext>();
        var result = await context.RequestServices.GetRequiredService<UnitOfWork>().ExecuteAsync(reason, async cancellationToken =>
        {
            var asset = await db.Assets.SingleAsync(a => a.Code == FixtureCodes.FixtureAAsset, cancellationToken);
            change(db, asset);
            return Result.Success();
        }, context.RequestAborted);

        if (result.IsFailure)
            await result.Error.ToProblem().ExecuteAsync(context);
        else
            context.Response.StatusCode = StatusCodes.Status204NoContent;
    }
}

/// <summary>Natural codes from the pgTAP fixtures (infrastructure/postgres/tests/fixtures).</summary>
internal static class FixtureCodes
{
    public const string FixtureAAsset = "FXA-ASSET";
    public const string FixtureAPremise = "FXA-PREM";
}
