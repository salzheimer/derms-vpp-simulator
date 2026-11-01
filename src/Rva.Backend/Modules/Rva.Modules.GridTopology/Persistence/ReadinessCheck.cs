using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Rva.Modules.GridTopology.Persistence;

/// <summary>
/// Readiness (tag "ready", D16): rva_core is reachable and every lookup code the module relies on is present. Reads the
/// database on every check rather than the cached catalog, so a missing row shows up at once. The description names
/// what is wrong but never carries exception text, which can hold connection details.
/// </summary>
internal sealed class ReadinessCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public const string Name = "grid-topology";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<LookupTable, IReadOnlyDictionary<string, int>> lookups;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            lookups = await LookupCatalog.ReadAllAsync(scope.ServiceProvider.GetRequiredService<GridTopologyDbContext>(), cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "rva_core is unreachable.", ex);
        }

        var missing = LookupCatalog.MissingKnownCodes(lookups);
        return missing.Count == 0
            ? HealthCheckResult.Healthy("rva_core is reachable and every lookup code is present.")
            : new HealthCheckResult(context.Registration.FailureStatus, "Missing lookup codes: " + string.Join(", ", missing) + ".");
    }
}
