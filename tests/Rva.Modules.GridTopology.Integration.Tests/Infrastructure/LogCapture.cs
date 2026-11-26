using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;

namespace Rva.Modules.GridTopology.Integration.Tests.Infrastructure;

/// <summary>Captures Microsoft.Extensions.Logging entries for tests that run the module without the host.</summary>
internal sealed class LogCapture : ILoggerProvider
{
    public sealed record Entry(LogLevel Level, string Category, string Message, Exception? Exception);

    private readonly ConcurrentQueue<Entry> entries = new();

    public IReadOnlyList<Entry> Entries => entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose() { }

    private sealed class Logger(LogCapture owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            owner.entries.Enqueue(new Entry(logLevel, category, formatter(state, exception), exception));
    }
}

/// <summary>
/// Captures the host's Serilog events. Registered as an <see cref="ILogEventSink"/> service, which Program.cs picks up
/// through ReadFrom.Services.
/// </summary>
internal sealed class SerilogCapture : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> events = new();
    private readonly List<(Func<LogEvent, bool> Match, TaskCompletionSource<LogEvent> Seen)> waiters = [];
    private readonly Lock sync = new();

    public IReadOnlyList<LogEvent> Events => events.ToArray();

    public IReadOnlyList<LogEvent> Errors => Events.Where(e => e.Level >= LogEventLevel.Error).ToList();

    public void Emit(LogEvent logEvent)
    {
        lock (sync)
        {
            events.Enqueue(logEvent);
            foreach (var waiter in waiters.Where(w => w.Match(logEvent)).ToList())
            {
                waiters.Remove(waiter);
                waiter.Seen.TrySetResult(logEvent);
            }
        }
    }

    /// <summary>
    /// The first event matching <paramref name="match"/>, waiting for it if needed. For events written after the
    /// response reaches the client, such as the request summary, which the outermost middleware writes last.
    /// </summary>
    public Task<LogEvent> WaitForAsync(Func<LogEvent, bool> match, TimeSpan timeout, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            var logged = events.FirstOrDefault(match);
            if (logged is not null)
                return Task.FromResult(logged);
            var seen = new TaskCompletionSource<LogEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
            waiters.Add((match, seen));
            return seen.Task.WaitAsync(timeout, cancellationToken);
        }
    }
}
