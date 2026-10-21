using System.Threading.Channels;
using Rva.SharedKernel.IntegrationEvents;

namespace Rva.Host.IntegrationEvents;

internal static class IntegrationEventsRegistration
{
    /// <summary>Registers the in-process publisher and its dispatcher. Handlers are registered by the consuming modules.</summary>
    public static IServiceCollection AddIntegrationEvents(this IServiceCollection services)
    {
        // One reader (the dispatcher) keeps events in publish order.
        services.AddSingleton(Channel.CreateUnbounded<IIntegrationEvent>(new UnboundedChannelOptions { SingleReader = true }));
        services.AddSingleton<IIntegrationEventPublisher, ChannelIntegrationEventPublisher>();
        services.AddHostedService<IntegrationEventDispatcher>();
        return services;
    }
}
