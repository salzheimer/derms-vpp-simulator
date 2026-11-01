using System.Data.Common;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Rva.Modules.GridTopology.Integration.Tests.Infrastructure.FaultInjection;

/// <summary>
/// FI-1 that can be switched off again: while <see cref="Unreachable"/> is set, opening a connection fails as an
/// unreachable server does (a transient NpgsqlException over a refused socket). Lets a test check that the host
/// recovers once the fault is removed (U4).
/// </summary>
internal sealed class DatabaseOutage : DbConnectionInterceptor
{
    public volatile bool Unreachable;

    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        ThrowIfUnreachable();
        return result;
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        ThrowIfUnreachable();
        return ValueTask.FromResult(result);
    }

    private void ThrowIfUnreachable()
    {
        if (Unreachable)
            throw new NpgsqlException("Failed to connect (test outage)", new SocketException((int)SocketError.ConnectionRefused));
    }
}

/// <summary>
/// Runs <see cref="OnCommand"/> just before the first command whose text matches <see cref="Matches"/>, ex. to cancel
/// the caller's token there (FI-7). Also records every command's text.
/// </summary>
internal sealed class CommandHook(Func<string, bool> matches, Action onCommand) : DbCommandInterceptor
{
    private int fired;

    public Func<string, bool> Matches { get; } = matches;
    public Action OnCommand { get; } = onCommand;
    public List<string> Commands { get; } = [];

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Fire(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Fire(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Fire(command);
        return ValueTask.FromResult(result);
    }

    private void Fire(DbCommand command)
    {
        lock (Commands)
            Commands.Add(command.CommandText);
        if (Matches(command.CommandText) && Interlocked.Exchange(ref fired, 1) == 0)
            OnCommand();
    }

    /// <summary>A hook that only records commands.</summary>
    public static CommandHook Recorder() => new(_ => false, () => { });
}
