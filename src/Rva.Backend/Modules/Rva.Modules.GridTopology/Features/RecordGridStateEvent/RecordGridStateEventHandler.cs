using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Features.RecordGridStateEvent;

/// <summary>
/// Records one grid state event on a node or an asset (GT-17), with the reason as its change context (D11). Asset
/// events may only be <c>disconnected</c> or <c>connected_energized</c> (D10); <c>islanded</c> only on a node that can
/// island (GT-17).
/// </summary>
internal sealed class RecordGridStateEventHandler
{
    public Task<Result> HandleAsync(RecordGridStateEventRequest request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
