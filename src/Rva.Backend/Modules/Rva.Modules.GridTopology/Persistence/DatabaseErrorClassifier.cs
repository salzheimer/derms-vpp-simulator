using Microsoft.EntityFrameworkCore;
using Npgsql;
using Rva.Modules.GridTopology.Contracts;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Persistence;

/// <summary>
/// Decides which database failures are expected outcomes (returned as an <see cref="Error"/>) rather than faults.
/// Only races the domain cannot rule out in advance are expected: a concurrent change, two registrations of one code,
/// and the database clock disagreeing with the app clock (Q2). Any other constraint or trigger violation means the
/// database rejected something the domain accepted, a validation gap, and stays an exception.
/// </summary>
internal static class DatabaseErrorClassifier
{
    /// <summary>The unique constraint PostgreSQL names for the inline UNIQUE on topology.assets.asset_code.</summary>
    public const string AssetCodeUnique = "assets_asset_code_key";

    public const string GridStateEventNotFuture = "ck_grid_state_events_not_future";

    /// <summary>The expected error <paramref name="exception"/> stands for, or null when it is a fault.</summary>
    public static Error? ClassifyExpected(Exception exception)
    {
        if (exception is DbUpdateConcurrencyException)
            return Error.Conflict(GridTopologyErrors.ConcurrentChange, "The record was changed by someone else since it was read. Read it again and retry.");

        return FindPostgresException(exception) switch
        {
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: AssetCodeUnique } =>
                Error.Conflict(GridTopologyErrors.DuplicateCode, "An asset with this code already exists. Codes stay reserved after decommissioning."),
            { SqlState: PostgresErrorCodes.CheckViolation, ConstraintName: GridStateEventNotFuture } =>
                Error.Validation(GridTopologyErrors.OccurredInFuture, "occurredAt is later than the time the event was recorded."),
            _ => null,
        };
    }

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
                return postgres;
        }
        return null;
    }
}
