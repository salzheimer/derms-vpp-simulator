using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Rva.Modules.GridTopology.Contracts.Snapshots;
using Rva.Modules.GridTopology.Features.Reads;
using Rva.Modules.GridTopology.Http;

namespace Rva.Modules.GridTopology.Features.GetAsset;

/// <summary><c>GET /api/topology/assets/{code}?asOf=</c>: one asset by its natural code.</summary>
internal static class GetAssetEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/assets/{code}", HandleAsync).WithName("GetAsset");

    private static async Task<Results<Ok<GetAssetResponse>, ProblemHttpResult>> HandleAsync(
        string code, string? asOf, GetAssetHandler handler, CancellationToken cancellationToken)
    {
        var at = AsOf.Parse(asOf);
        if (at.IsFailure)
            return at.Error.ToProblem();

        var result = await handler.HandleAsync(code, at.Value, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(GetAssetResponse.From(result.Value)) : result.Error.ToProblem();
    }
}

/// <summary>The asset as JSON: the <see cref="AssetSnapshot"/> fields with plain numbers and strings.</summary>
internal sealed record GetAssetResponse(
    long Id,
    string Code,
    GetAssetResponse.Node Connection,
    long SubstationId,
    string SubstationCode,
    string InterconnectionType,
    double RatedPMinKw,
    double RatedPMaxKw,
    double RatedKva,
    double? ExportLimitKw,
    GetAssetResponse.SiteLimits? Site,
    GetAssetResponse.MicrogridLimits? Microgrid,
    string? Makeup,
    double? EnergyCapacityKwh,
    string? ControlManufacturer,
    string LifecycleState,
    string ParticipationState,
    DateTimeOffset ValidFrom)
{
    internal sealed record Node(long GridNodeId, string Level, string Code);

    internal sealed record SiteLimits(Node Premise, string CustomerSegment, double? ServiceCapacityKva, double? ExportLimitKw, bool CanIsland);

    internal sealed record MicrogridLimits(Node Microgrid, double? ImportLimitKw, double? ExportLimitKw, bool CanIsland);

    public static GetAssetResponse From(AssetSnapshot a) => new(
        a.Id.Value,
        a.Code,
        From(a.Connection),
        a.SubstationId.Value,
        a.SubstationCode,
        a.InterconnectionType.Value,
        a.RatedPMinKw.Value,
        a.RatedPMaxKw.Value,
        a.RatedKva.Value,
        a.ExportLimitKw?.Value,
        a.Site is { } s ? new SiteLimits(From(s.Premise), s.CustomerSegment, s.ServiceCapacityKva?.Value, s.ExportLimitKw?.Value, s.CanIsland) : null,
        a.Microgrid is { } m ? new MicrogridLimits(From(m.Microgrid), m.ImportLimitKw?.Value, m.ExportLimitKw?.Value, m.CanIsland) : null,
        a.Makeup,
        a.EnergyCapacityKwh?.Value,
        a.ControlManufacturer,
        a.LifecycleState,
        a.ParticipationState,
        a.ValidFrom);

    private static Node From(GridNodeRef node) => new(node.GridNodeId.Value, node.Level, node.Code);
}
