using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Rva.Host.Health;

/// <summary>
/// /health is liveness only: the process is up and its settings are valid, so it runs no checks and never touches a
/// database. /health/ready runs the checks modules tag "ready" (databases, required reference data) and reports each
/// one's status and description, never exception text.
/// </summary>
internal static class HealthEndpoints
{
    public const string ReadinessTag = "ready";

    public static IEndpointRouteBuilder MapRvaHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadinessTag),
            ResponseWriter = WriteReport,
        });
        return endpoints;
    }

    private static Task WriteReport(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return JsonSerializer.SerializeAsync(context.Response.Body, new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString(), description = e.Value.Description }),
        }, cancellationToken: context.RequestAborted);
    }
}
