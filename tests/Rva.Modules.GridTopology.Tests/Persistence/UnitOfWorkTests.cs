using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Persistence;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Tests.Persistence;

/// <summary>A-10: a change without a valid reason fails validation before any database call.</summary>
public class UnitOfWorkTests
{
    // Nothing listens here; opening a connection would throw, so a passing test proves no database call was made.
    private const string NoDatabase = "Host=127.0.0.1;Port=1;Database=none;Username=none;Timeout=1";

    private static UnitOfWork CreateUnitOfWork(out GridTopologyDbContext db)
    {
        var options = (DbContextOptionsBuilder<GridTopologyDbContext>)GridTopologyDbContext.Configure(new DbContextOptionsBuilder<GridTopologyDbContext>(), NoDatabase);
        db = new GridTopologyDbContext(options.Options);
        return new UnitOfWork(db, new ChangeContextAccessor(), NullLogger<UnitOfWork>.Instance);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Missing_change_reason_is_a_validation_error(string reason)
    {
        var unitOfWork = CreateUnitOfWork(out var db);
        await using var context = db;
        var workRan = false;

        var result = await unitOfWork.ExecuteAsync(reason, _ => { workRan = true; return Task.FromResult(Result.Success()); },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal((ErrorKind.Validation, GridTopologyErrors.ChangeReasonInvalid), (result.Error.Kind, result.Error.Code));
        Assert.False(workRan);
    }

    [Fact]
    public async Task Change_reason_longer_than_the_history_column_is_a_validation_error()
    {
        var unitOfWork = CreateUnitOfWork(out var db);
        await using var context = db;

        var result = await unitOfWork.ExecuteAsync(new string('x', UnitOfWork.MaxChangeReasonLength + 1),
            _ => Task.FromResult(Result.Success()), TestContext.Current.CancellationToken);

        Assert.Equal(GridTopologyErrors.ChangeReasonInvalid, result.Error.Code);
    }

    [Fact]
    public async Task SaveChanges_outside_a_unit_of_work_throws()
    {
        CreateUnitOfWork(out var db);
        await using var context = db;

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }
}
