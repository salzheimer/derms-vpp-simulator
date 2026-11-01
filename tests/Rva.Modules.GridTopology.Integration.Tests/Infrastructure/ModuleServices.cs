using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Rva.Modules.GridTopology.Integration.Tests.Infrastructure;

/// <summary>
/// The GridTopology module's services on their own, without the host: handler and contract level tests. Logs are
/// captured in <see cref="Logs"/>.
/// </summary>
internal sealed class ModuleServices : IAsyncDisposable
{
    private ModuleServices(ServiceProvider provider, LogCapture logs)
    {
        Provider = provider;
        Logs = logs;
    }

    public ServiceProvider Provider { get; }
    public LogCapture Logs { get; }

    public static ModuleServices Create(string connectionString, Action<IServiceCollection>? configure = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Core"] = connectionString,
                ["GridTopology:UtilityTimeZone"] = "America/Los_Angeles",
            })
            .Build();

        var logs = new LogCapture();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(logs).SetMinimumLevel(LogLevel.Debug));
        services.AddGridTopologyModule(configuration);
        configure?.Invoke(services);
        return new ModuleServices(services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }), logs);
    }

    public AsyncServiceScope CreateScope() => Provider.CreateAsyncScope();

    public ValueTask DisposeAsync() => Provider.DisposeAsync();
}
