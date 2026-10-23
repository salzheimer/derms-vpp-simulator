using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Rva.Host.Tests;

/// <summary>
/// Builds the real host in the Development environment (so appsettings.Development.json supplies valid settings),
/// with optional configuration overrides layered on top.
/// </summary>
internal static class HostFactory
{
    public static WebApplicationFactory<Program> Create(
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IWebHostBuilder>? configure = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            if (settings is not null)
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
            configure?.Invoke(builder);
        });
}
