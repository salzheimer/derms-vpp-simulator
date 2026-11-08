using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Rva.Modules.GridTopology.Features.Reads;
using Rva.Modules.GridTopology.Http;

namespace Rva.Modules.GridTopology.Features.GetSubstationTree;

/// <summary><c>GET /api/topology/substations/{code}/tree?asOf=</c>.</summary>
internal static class GetSubstationTreeEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/substations/{code}/tree", HandleAsync).WithName("GetSubstationTree");

    private static async Task<Results<Ok<SubstationTreeResponse>, ProblemHttpResult>> HandleAsync(
        string code, string? asOf, GetSubstationTreeHandler handler, CancellationToken cancellationToken)
    {
        var at = AsOf.Parse(asOf);
        if (at.IsFailure)
            return at.Error.ToProblem();

        var result = await handler.HandleAsync(code, at.Value, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }
}
