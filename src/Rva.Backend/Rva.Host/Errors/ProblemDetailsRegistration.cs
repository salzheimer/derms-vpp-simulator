using System.Data.Common;
using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;

namespace Rva.Host.Errors;

/// <summary>
/// Every error response is RFC 9457 problem+json with a <c>code</c> and a <c>traceId</c>. Unexpected exceptions become
/// 500, or 503 when the database is unreachable or timed out, and carry no exception text, SQL, or stack trace.
/// </summary>
internal static partial class ProblemDetailsRegistration
{
    public const string UnexpectedErrorCode = "Rva.UnexpectedError";
    public const string DatabaseUnavailableCode = "Rva.DatabaseUnavailable";

    public static IServiceCollection AddRvaProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            problem.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
            if (!problem.Extensions.ContainsKey("code"))
                problem.Extensions["code"] = CodeFor(problem.Status ?? context.HttpContext.Response.StatusCode);
        });
        services.AddExceptionHandler<TransientDatabaseExceptionHandler>();
        return services;
    }

    /// <summary>A code for responses no module gave one, ex. an unknown route or an unhandled exception.</summary>
    private static string CodeFor(int status) => status switch
    {
        StatusCodes.Status404NotFound => "Rva.NotFound",
        StatusCodes.Status405MethodNotAllowed => "Rva.MethodNotAllowed",
        StatusCodes.Status500InternalServerError => UnexpectedErrorCode,
        StatusCodes.Status503ServiceUnavailable => DatabaseUnavailableCode,
        _ => $"Rva.Http{status}",
    };

    /// <summary>
    /// The exception, or one it wraps, is a database fault worth retrying later: unreachable, dropped connection, or
    /// timeout. Uses only base class library types, so the host needs no provider knowledge.
    /// </summary>
    public static bool IsTransientDatabaseFault(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException { IsTransient: true } or TimeoutException)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 503 for transient database faults. Since .NET 10 the exception handler middleware does not log exceptions an
    /// <see cref="IExceptionHandler"/> handles, so this logs the one Error entry. Everything else falls through to the
    /// middleware's 500, which logs it.
    /// </summary>
    private sealed partial class TransientDatabaseExceptionHandler(
        IProblemDetailsService problemDetails, ILogger<TransientDatabaseExceptionHandler> logger) : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            if (!IsTransientDatabaseFault(exception))
                return false;

            LogDatabaseUnavailable(exception, httpContext.Request.Method, httpContext.Request.Path);
            httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return await problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails =
                {
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Title = "The database is unavailable. Try again later.",
                    Extensions = { ["code"] = DatabaseUnavailableCode },
                },
            });
        }

        [LoggerMessage(Level = LogLevel.Error, Message = "Database unavailable while handling {Method} {Path}")]
        private partial void LogDatabaseUnavailable(Exception exception, string method, string path);
    }
}
