using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Rva.Modules.Telemetry;

/// <summary>
/// The Telemetry module's only public type: Rva.Host calls these to register the module's services and endpoints.
/// </summary>
public static class TelemetryModule
{
    public static IServiceCollection AddTelemetryModule(this IServiceCollection services, IConfiguration configuration) => services;

    public static IEndpointRouteBuilder MapTelemetryEndpoints(this IEndpointRouteBuilder endpoints) => endpoints;
}
