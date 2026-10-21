using System.Collections.Concurrent;
using System.Threading.Channels;
using Rva.SharedKernel.IntegrationEvents;

namespace Rva.Host.IntegrationEvents;

/// <summary>
/// Reads the event channel and calls every <see cref="IIntegrationEventHandler{TEvent}"/> registered for each
/// event's runtime type, one DI scope per event. A failing handler is logged and skipped so it cannot stop
/// delivery to other handlers or take down the host.
/// </summary>
internal sealed class IntegrationEventDispatcher(
    Channel<IIntegrationEvent> channel,
    IServiceScopeFactory scopeFactory,
    ILogger<IntegrationEventDispatcher> logger) : BackgroundService
{
    private delegate Task HandlerInvoker(object handler, IIntegrationEvent integrationEvent, CancellationToken cancellationToken);

    private static readonly ConcurrentDictionary<Type, (Type HandlerType, HandlerInvoker Invoke)> HandlerTypes = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var integrationEvent in channel.Reader.ReadAllAsync(stoppingToken))
        {
            await DispatchAsync(integrationEvent, stoppingToken);
        }
    }

    private async Task DispatchAsync(IIntegrationEvent integrationEvent, CancellationToken stoppingToken)
    {
        var eventType = integrationEvent.GetType();
        var (handlerType, invoke) = HandlerTypes.GetOrAdd(eventType, CreateInvoker);

        await using var scope = scopeFactory.CreateAsyncScope();
        foreach (var handler in scope.ServiceProvider.GetServices(handlerType).OfType<object>())
        {
            try
            {
                await invoke(handler, integrationEvent, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Integration event handler {Handler} failed for {EventType}", handler.GetType().FullName, eventType.FullName);
            }
        }
    }

    private static (Type, HandlerInvoker) CreateInvoker(Type eventType)
    {
        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
        var invoker = typeof(IntegrationEventDispatcher)
            .GetMethod(nameof(InvokeHandler), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(eventType)
            .CreateDelegate<HandlerInvoker>();
        return (handlerType, invoker);
    }

    private static Task InvokeHandler<TEvent>(object handler, IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent =>
        ((IIntegrationEventHandler<TEvent>)handler).HandleAsync((TEvent)integrationEvent, cancellationToken);
}
