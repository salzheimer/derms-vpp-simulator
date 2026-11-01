using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Features.ProtocolMappings;
using Rva.Modules.GridTopology.Persistence;

namespace Rva.Modules.GridTopology;

/// <summary>
/// The GridTopology module's only public type: Rva.Host calls these to register the module's services and endpoints.
/// </summary>
public static class GridTopologyModule
{
    /// <summary>
    /// Registers the module. Nothing here opens a database connection, so the host starts without one (D16); the
    /// readiness check reports it instead.
    /// </summary>
    public static IServiceCollection AddGridTopologyModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GridTopologyOptions>().Bind(configuration.GetSection(GridTopologyOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<GridTopologyOptions>, GridTopologyOptionsValidator>();

        services.AddScoped<ChangeContextAccessor>();
        services.AddScoped<ChangeContextInterceptor>();
        // The connection string is read when a context is created, after the host has validated it.
        services.AddDbContext<GridTopologyDbContext>((provider, options) =>
            GridTopologyDbContext.Configure(options, configuration.GetConnectionString("Core") ?? "")
                .AddInterceptors(provider.GetRequiredService<ChangeContextInterceptor>()));
        services.AddScoped<UnitOfWork>();
        services.AddSingleton<LookupCatalog>();

        services.AddScoped<ResolveTechnologyTypeHandler>();
        services.AddScoped<GetProtocolMappingsHandler>();
        services.AddScoped<IGridTopologyQueries, GridTopologyQueries>();

        services.AddHealthChecks().AddCheck<ReadinessCheck>(ReadinessCheck.Name, tags: [ReadinessTag], timeout: TimeSpan.FromSeconds(5));
        return services;
    }

    /// <summary>Maps the module's routes under <c>/api/topology</c>. The feature endpoints arrive with plan p1-s01c to p1-s01g.</summary>
    public static IEndpointRouteBuilder MapGridTopologyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/api/topology");
        return endpoints;
    }

    /// <summary>Health check tag the host serves at /health/ready.</summary>
    private const string ReadinessTag = "ready";
}
