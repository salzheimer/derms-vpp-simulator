using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Rva.Modules.Simulation;

/// <summary>
/// The Simulation module's only public type: Rva.Host calls these to register the module's services and endpoints.
/// </summary>
public static class SimulationModule
{
    public static IServiceCollection AddSimulationModule(this IServiceCollection services, IConfiguration configuration) => services;

    public static IEndpointRouteBuilder MapSimulationEndpoints(this IEndpointRouteBuilder endpoints) => endpoints;
}
