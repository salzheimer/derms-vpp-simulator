using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Rva.Host.Tests;

/// <summary>
/// Builds the real host in the Development environment (so appsettings.Development.json supplies valid settings),
/// with optional configuration overrides layered on top. Its start is gated (<see cref="StartGate"/>), so a startup
/// failure always surfaces as the failure itself.
/// </summary>
internal static class HostFactory
{
    public static WebApplicationFactory<Program> Create(
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IWebHostBuilder>? configure = null) =>
        new GatedFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            if (settings is not null)
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
            configure?.Invoke(builder);
        });

    // WithWebHostBuilder's factory delegates ConfigureWebHost and CreateHost to this one.
    private sealed class GatedFactory : WebApplicationFactory<Program>
    {
        private readonly StartGate gate = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureTestServices(gate.AddTo);

        protected override IHost CreateHost(IHostBuilder builder) => gate.Start(builder);
    }
}
