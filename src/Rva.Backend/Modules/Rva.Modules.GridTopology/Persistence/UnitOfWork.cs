using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rva.Modules.GridTopology.Contracts;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Persistence;

/// <summary>
/// The only way GridTopology saves changes. Runs a command in one transaction that starts with its change context
/// (system actor and the given reason, D17), saves, and commits, so a change is written with its history and outbox
/// rows or not at all. Expected database outcomes (<see cref="DatabaseErrorClassifier"/>) come back as failures; any
/// other exception rolls back and propagates.
/// </summary>
internal sealed partial class UnitOfWork(GridTopologyDbContext db, ChangeContextAccessor changeContext, ILogger<UnitOfWork> logger)
{
    /// <summary>Length of the history tables' change_reason column.</summary>
    public const int MaxChangeReasonLength = 255;

    /// <summary>
    /// Runs <paramref name="work"/>, which loads and changes entities through the context, then saves and commits.
    /// A failure returned by <paramref name="work"/> rolls back and is returned unchanged.
    /// </summary>
    public async Task<Result> ExecuteAsync(string changeReason, Func<CancellationToken, Task<Result>> work, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(changeReason) || changeReason.Length > MaxChangeReasonLength)
            return Error.Validation(GridTopologyErrors.ChangeReasonInvalid, $"A change needs a reason of 1 to {MaxChangeReasonLength} characters.");

        changeContext.Current = ChangeContext.System(changeReason);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var result = await work(cancellationToken);
            if (result.IsFailure)
            {
                db.ChangeTracker.Clear();
                return result;   // disposing the transaction rolls it back
            }

            db.SaveAllowed = true;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            finally
            {
                db.SaveAllowed = false;
            }

            await transaction.CommitAsync(cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (DatabaseErrorClassifier.ClassifyExpected(ex) is { } error)
        {
            db.ChangeTracker.Clear();
            LogExpectedFailure(error.Code, changeReason);
            return error;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            db.ChangeTracker.Clear();
            LogCancelled(changeReason);
            throw;
        }
        finally
        {
            changeContext.Current = null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "GridTopology change '{ChangeReason}' was rejected: {ErrorCode}")]
    private partial void LogExpectedFailure(string errorCode, string changeReason);

    [LoggerMessage(Level = LogLevel.Information, Message = "GridTopology change '{ChangeReason}' was cancelled by the caller; nothing was committed")]
    private partial void LogCancelled(string changeReason);
}
