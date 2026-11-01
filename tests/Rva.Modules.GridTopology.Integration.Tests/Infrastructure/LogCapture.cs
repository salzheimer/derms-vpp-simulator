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

    public IReadOnlyList<LogEvent> Events => events.ToArray();

    public IReadOnlyList<LogEvent> Errors => Events.Where(e => e.Level >= LogEventLevel.Error).ToList();

    public void Emit(LogEvent logEvent) => events.Enqueue(logEvent);
}
