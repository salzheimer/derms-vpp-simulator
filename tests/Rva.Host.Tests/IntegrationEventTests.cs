using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Rva.SharedKernel.IntegrationEvents;

namespace Rva.Host.Tests;

/// <summary>
/// The in-process publisher delivers events to handlers registered by consumers, through the shared-kernel
/// contracts only. No module publishes an event yet, so the event and handlers are defined here and registered the
/// way a consuming module's Add&lt;Area&gt;Module would; that modules cannot reference each other's implementation is
/// enforced separately by the architecture tests.
/// </summary>
public class IntegrationEventTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    internal sealed record AssetRegistered(string AssetCode) : IIntegrationEvent;

    internal sealed record OtherEvent : IIntegrationEvent;

    /// <summary>Completes once it has seen the expected number of events; collects them in arrival order.</summary>
    internal sealed class Inbox(int expected)
    {
        private readonly List<string> received = [];
        private readonly TaskCompletionSource<IReadOnlyList<string>> done = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<string>> Received => done.Task;

        public void Add(string item)
        {
            lock (received)
            {
                received.Add(item);
                if (received.Count == expected) done.TrySetResult(received.ToList());
            }
        }
    }

    private sealed class RecordingHandler(Inbox inbox) : IIntegrationEventHandler<AssetRegistered>
    {
        public Task HandleAsync(AssetRegistered integrationEvent, CancellationToken cancellationToken)
        {
            inbox.Add($"recording:{integrationEvent.AssetCode}");
            return Task.CompletedTask;
        }
    }

    private sealed class FailingHandler : IIntegrationEventHandler<AssetRegistered>
    {
        public Task HandleAsync(AssetRegistered integrationEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("handler failure");
    }

    private sealed class OtherEventHandler(Inbox inbox) : IIntegrationEventHandler<OtherEvent>
    {
        public Task HandleAsync(OtherEvent integrationEvent, CancellationToken cancellationToken)
        {
            inbox.Add("other");
            return Task.CompletedTask;
        }
    }

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> HostWith(Inbox inbox, Action<IServiceCollection> registerHandlers) =>
        HostFactory.Create(configure: builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(inbox);
            registerHandlers(services);
        }));

    [Fact]
    public async Task Published_event_reaches_the_registered_handler()
    {
        var inbox = new Inbox(expected: 1);
        await using var factory = HostWith(inbox, s => s.AddScoped<IIntegrationEventHandler<AssetRegistered>, RecordingHandler>());
        using var client = factory.CreateClient();

        await factory.Services.GetRequiredService<IIntegrationEventPublisher>().PublishAsync(new AssetRegistered("BAT-001"), TestContext.Current.CancellationToken);

        Assert.Equal(["recording:BAT-001"], await inbox.Received.WaitAsync(Timeout, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Events_are_delivered_in_publish_order_and_only_to_handlers_of_their_type()
    {
        var inbox = new Inbox(expected: 3);
        await using var factory = HostWith(inbox, s =>
        {
            s.AddScoped<IIntegrationEventHandler<AssetRegistered>, RecordingHandler>();
            s.AddScoped<IIntegrationEventHandler<OtherEvent>, OtherEventHandler>();
        });
        using var client = factory.CreateClient();
        var publisher = factory.Services.GetRequiredService<IIntegrationEventPublisher>();

        await publisher.PublishAsync(new AssetRegistered("BAT-001"), TestContext.Current.CancellationToken);
        await publisher.PublishAsync(new OtherEvent(), TestContext.Current.CancellationToken);
        await publisher.PublishAsync(new AssetRegistered("BAT-002"), TestContext.Current.CancellationToken);

        Assert.Equal(["recording:BAT-001", "other", "recording:BAT-002"], await inbox.Received.WaitAsync(Timeout, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_failing_handler_does_not_stop_other_handlers_or_later_events()
    {
        var inbox = new Inbox(expected: 2);
        await using var factory = HostWith(inbox, s =>
        {
            s.AddScoped<IIntegrationEventHandler<AssetRegistered>, FailingHandler>();
            s.AddScoped<IIntegrationEventHandler<AssetRegistered>, RecordingHandler>();
        });
        using var client = factory.CreateClient();
        var publisher = factory.Services.GetRequiredService<IIntegrationEventPublisher>();

        await publisher.PublishAsync(new AssetRegistered("BAT-001"), TestContext.Current.CancellationToken);
        await publisher.PublishAsync(new AssetRegistered("BAT-002"), TestContext.Current.CancellationToken);

        Assert.Equal(["recording:BAT-001", "recording:BAT-002"], await inbox.Received.WaitAsync(Timeout, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Event_without_handlers_is_dropped_without_affecting_delivery()
    {
        var inbox = new Inbox(expected: 1);
        await using var factory = HostWith(inbox, s => s.AddScoped<IIntegrationEventHandler<AssetRegistered>, RecordingHandler>());
        using var client = factory.CreateClient();
        var publisher = factory.Services.GetRequiredService<IIntegrationEventPublisher>();

        await publisher.PublishAsync(new OtherEvent(), TestContext.Current.CancellationToken);
        await publisher.PublishAsync(new AssetRegistered("BAT-001"), TestContext.Current.CancellationToken);

        Assert.Equal(["recording:BAT-001"], await inbox.Received.WaitAsync(Timeout, TestContext.Current.CancellationToken));
    }
}
