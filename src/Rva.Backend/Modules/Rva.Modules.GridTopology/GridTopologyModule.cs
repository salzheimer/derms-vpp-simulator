using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Features.GetAsset;
using Rva.Modules.GridTopology.Features.GetAssetsBySubstation;
using Rva.Modules.GridTopology.Features.GetConstraintPath;
using Rva.Modules.GridTopology.Features.GetGridState;
using Rva.Modules.GridTopology.Features.GetSubstationTree;
using Rva.Modules.GridTopology.Features.ProtocolMappings;
using Rva.Modules.GridTopology.Features.Reads;
using Rva.Modules.GridTopology.Features.RecordGridStateEvent;
using Rva.Modules.GridTopology.Features.ResolveNode;
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

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AssetReader>();
        services.AddScoped<ResolveTechnologyTypeHandler>();
        services.AddScoped<GetProtocolMappingsHandler>();
        services.AddScoped<GetAssetHandler>();
        services.AddScoped<GetAssetsBySubstationHandler>();
        services.AddScoped<GetConstraintPathHandler>();
        services.AddScoped<ResolveNodeHandler>();
        services.AddScoped<GetSubstationTreeHandler>();
        services.AddScoped<GetGridStateHandler>();
        services.AddScoped<RecordGridStateEventHandler>();
        services.AddScoped<IGridTopologyQueries, GridTopologyQueries>();

        services.AddHealthChecks().AddCheck<ReadinessCheck>(ReadinessCheck.Name, tags: [ReadinessTag], timeout: TimeSpan.FromSeconds(5));
        return services;
    }

    /// <summary>
    /// Maps the module's routes under <c>/api/topology</c>. External callers name assets and nodes by natural code.
    /// </summary>
    public static IEndpointRouteBuilder MapGridTopologyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/topology");
        GetAssetEndpoint.Map(group);
        GetSubstationTreeEndpoint.Map(group);
        RecordGridStateEventEndpoint.Map(group);
        return endpoints;
    }

    /// <summary>Health check tag the host serves at /health/ready.</summary>
    private const string ReadinessTag = "ready";
}
