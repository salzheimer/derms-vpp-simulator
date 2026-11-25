using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog.Core;

namespace Rva.Modules.GridTopology.Integration.Tests.Infrastructure;

/// <summary>
/// The real host in memory, in the Production environment (so error responses are what a client would get; Development
/// would show the exception page), with every required setting supplied in memory and the Core connection string
/// pointing at the test's database. Adds the test-only endpoints of <see cref="TestEndpoints"/> and captures Serilog
/// events in <see cref="Logs"/>. Its start is gated (<see cref="StartGate"/>), so a startup failure always surfaces as
/// the failure itself.
/// </summary>
internal sealed class TopologyHostFactory(
    string coreConnectionString,
    IReadOnlyDictionary<string, string?>? settings = null,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
{
    private readonly StartGate gate = new();

    public SerilogCapture Logs { get; } = new();

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        TestDatabase.ReleaseConnections(coreConnectionString);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Rva:Deployment:UtilityCode"] = "test",
                ["Rva:Deployment:RedisKeyPrefix"] = "rva:test:",
                ["Rva:Deployment:MqttTopicRoot"] = "rva/test",
                ["ConnectionStrings:Core"] = coreConnectionString,
                ["ConnectionStrings:Telemetry"] = "Host=127.0.0.1;Port=1;Database=unused",
                ["ConnectionStrings:Redis"] = "127.0.0.1:1",
            });
            if (settings is not null)
                config.AddInMemoryCollection(settings);
        });
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILogEventSink>(Logs);
            services.AddSingleton<IStartupFilter, TestEndpoints>();
            configureServices?.Invoke(services);
            gate.AddTo(services);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder) => gate.Start(builder);
}
