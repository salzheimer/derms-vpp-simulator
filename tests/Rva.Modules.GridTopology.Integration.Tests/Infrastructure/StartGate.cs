using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rva.Modules.GridTopology.Integration.Tests.Infrastructure;

/// <summary>
/// Makes a <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/> start deterministic when
/// startup fails. With minimal hosting the factory runs Program on its own thread, which goes on into app.Run() as soon
/// as the host is built; if ValidateOnStart then fails, app.Run() disposes the host's services, and the test thread,
/// still about to register on IHostApplicationLifetime, gets an ObjectDisposedException instead of the
/// OptionsValidationException (AI_HARNESS_PLAN.md section 7). The gate replaces IHostLifetime with one whose
/// WaitForStartAsync, which the host awaits before running the startup validators, waits until the test thread has
/// registered. A copy lives in each test project that starts the host, as there is no shared test utilities project;
/// the other is tests/Rva.Host.Tests/StartGate.cs.
/// </summary>
internal sealed class StartGate
{
    private Pass? pending;

    /// <summary>Registers the gated lifetime; call from ConfigureTestServices so it replaces the host's own.</summary>
    public void AddTo(IServiceCollection services) =>
        services.AddSingleton<IHostLifetime>(_ => new GatedLifetime(
            pending ?? throw new InvalidOperationException("The host was built outside StartGate.Start.")));

    /// <summary>
    /// Replaces WebApplicationFactory.CreateHost: builds and starts the host, letting Program's thread past
    /// WaitForStartAsync only once this thread has registered on the built host. Unlike the base method this does not
    /// call TryConfigureServerPort, which only applies with UseKestrel(port); these tests never use it.
    /// </summary>
    public IHost Start(IHostBuilder builder)
    {
        // A new pass per start: the factory builds again after a failed start, and an open pass would reopen the race.
        var pass = new Pass();
        pending = pass;
        IHost host;
        Task start;
        try
        {
            host = builder.Build();
            if (!pass.Claimed)
                throw new InvalidOperationException("The host did not use the gated IHostLifetime, so its start is not gated.");
            // DeferredHost.StartAsync registers on the host's services before its first await, so once it returns,
            // Program's thread may validate and, on failure, dispose those services.
            start = host.StartAsync();
        }
        finally
        {
            // Always open, so Program's thread is never left waiting.
            pending = null;
            pass.Open();
        }

        start.GetAwaiter().GetResult();
        return host;
    }

    private sealed class Pass
    {
        private readonly TaskCompletionSource opened = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Claimed { get; set; }

        public Task Opened => opened.Task;

        public void Open() => opened.TrySetResult();
    }

    private sealed class GatedLifetime : IHostLifetime
    {
        private readonly Pass pass;

        public GatedLifetime(Pass pass)
        {
            this.pass = pass;
            pass.Claimed = true;
        }

        public Task WaitForStartAsync(CancellationToken cancellationToken) => pass.Opened.WaitAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
