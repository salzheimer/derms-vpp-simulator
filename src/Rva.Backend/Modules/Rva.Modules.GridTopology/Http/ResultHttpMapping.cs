using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
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

    public static ProblemHttpResult ToProblem(this Error error) =>
        TypedResults.Problem(
            detail: error.Message,
            statusCode: StatusCodeOf(error.Kind),
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
