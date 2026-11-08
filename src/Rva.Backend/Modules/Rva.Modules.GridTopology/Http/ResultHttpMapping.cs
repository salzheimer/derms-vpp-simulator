using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Rva.Modules.GridTopology.Contracts;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Http;

/// <summary>
/// Expected failures as RFC 9457 problem+json, with the error's code in a <c>code</c> member. The host adds the
/// <c>traceId</c>. Module-local because modules cannot reference Rva.Host.
/// </summary>
internal static class ResultHttpMapping
{
    public static int StatusCodeOf(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.Rule => StatusCodes.Status422UnprocessableEntity,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// The status for <paramref name="error"/>: by its kind, except that corrupt stored topology
    /// (<see cref="GridTopologyErrors.ConstraintPathIncomplete"/>) is the server's fault, never the client's, so 500.
    /// Its message names asset codes, levels, and ids only, and it was already logged at Error where it was found.
    /// </summary>
    public static int StatusCodeOf(Error error) =>
        error.Code == GridTopologyErrors.ConstraintPathIncomplete
            ? StatusCodes.Status500InternalServerError
            : StatusCodeOf(error.Kind);

    public static ProblemHttpResult ToProblem(this Error error) =>
        TypedResults.Problem(
            detail: error.Message,
            statusCode: StatusCodeOf(error),
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
