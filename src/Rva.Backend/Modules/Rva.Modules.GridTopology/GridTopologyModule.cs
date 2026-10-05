using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Rva.Modules.GridTopology;

/// <summary>
/// The GridTopology module's only public type: Rva.Host calls these to register the module's services and endpoints.
/// </summary>
public static class GridTopologyModule
{
    public static IServiceCollection AddGridTopologyModule(this IServiceCollection services, IConfiguration configuration) => services;

    public static IEndpointRouteBuilder MapGridTopologyEndpoints(this IEndpointRouteBuilder endpoints) => endpoints;
}
