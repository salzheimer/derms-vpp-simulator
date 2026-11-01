using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rva.Modules.GridTopology.Persistence;

/// <summary>
/// Sets the transaction-local rva.actor, rva.actor_type, and rva.change_reason as soon as a transaction starts, so every
/// statement in it is attributed. Values are passed as parameters, never interpolated. The actor is cast to uuid here,
/// so a malformed actor fails the transaction before anything is written. Transactions started outside a unit of work
/// (reads) get no settings.
/// </summary>
internal sealed class ChangeContextInterceptor(ChangeContextAccessor accessor) : DbTransactionInterceptor
{
    private const string Sql =
        "SELECT set_config('rva.actor', CAST(@actor AS uuid)::text, true), " +
        "set_config('rva.actor_type', @actor_type, true), " +
        "set_config('rva.change_reason', @change_reason, true)";

    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        using var command = CreateCommand(connection, result);
        command?.ExecuteNonQuery();
        return result;
    }

    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
        await using var command = CreateCommand(connection, result);
        if (command is not null)
            await command.ExecuteNonQueryAsync(cancellationToken);
        return result;
    }

    private DbCommand? CreateCommand(DbConnection connection, DbTransaction transaction)
    {
        if (accessor.Current is not { } context)
            return null;

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = Sql;
        AddParameter(command, "actor", context.Actor);
        AddParameter(command, "actor_type", context.ActorType);
        AddParameter(command, "change_reason", context.Reason);
        return command;
    }

    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
