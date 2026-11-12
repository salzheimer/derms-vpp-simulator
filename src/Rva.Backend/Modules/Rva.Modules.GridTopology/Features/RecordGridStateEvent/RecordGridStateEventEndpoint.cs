using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Rva.Modules.GridTopology.Features.RecordGridStateEvent;

/// <summary><c>POST /api/topology/grid-state-events</c>: records a change in a node's or an asset's grid state.</summary>
internal static class RecordGridStateEventEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/grid-state-events", HandleAsync).WithName("RecordGridStateEvent");

    private static Task<IResult> HandleAsync(
        RecordGridStateEventRequest request, RecordGridStateEventHandler handler, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
