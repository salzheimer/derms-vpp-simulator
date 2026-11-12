using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rva.Modules.GridTopology.Http;

namespace Rva.Modules.GridTopology.Features.RecordGridStateEvent;

/// <summary><c>POST /api/topology/grid-state-events</c>: records a change in a node's or an asset's grid state.</summary>
internal static class RecordGridStateEventEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/grid-state-events", HandleAsync).WithName("RecordGridStateEvent");

    /// <summary>201 Created with no body once the event is stored; problem+json otherwise.</summary>
    private static async Task<IResult> HandleAsync(
        RecordGridStateEventRequest request, RecordGridStateEventHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? TypedResults.Created() : result.Error.ToProblem();
    }
}
