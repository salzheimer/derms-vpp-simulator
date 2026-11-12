using System.Globalization;
using System.Text.RegularExpressions;
using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Persistence;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Features.Reads;

/// <summary>The <c>asOf</c> argument every read takes: checked by the handlers, parsed by the endpoints.</summary>
internal static partial class AsOf
{
    /// <summary>
    /// A Validation error when <paramref name="asOf"/> is in the future: the topology's future is unknown, and answering
    /// with the current version would claim otherwise. Null when it is acceptable.
    /// <para>
    /// The database clock stamps every version and is the one callers' times come from (ex. a simulation run's
    /// <c>topology_as_of</c>), and it may run slightly ahead of this process's clock (Q2). So a time up to the app
    /// clock is accepted at once, and a later one only if the database clock has reached it.
    /// </para>
    /// </summary>
    public static async Task<Error?> CheckNotInFutureAsync(
        DateTimeOffset? asOf, TimeProvider time, GridTopologyDbContext db, CancellationToken cancellationToken)
    {
        if (asOf is not { } at || !IsAfter(at, time.GetUtcNow()))
            return null;
        return IsAfter(at, await AsOfReads.DatabaseNowAsync(db, cancellationToken))
            ? Error.Validation(GridTopologyErrors.AsOfInFuture, $"asOf {Format(at)} is in the future.")
            : null;
    }

    public static bool IsAfter(DateTimeOffset asOf, DateTimeOffset now) => asOf > now;

    /// <summary>
    /// Parses the <c>asOf</c> query parameter: absent or empty means current; otherwise an ISO 8601 date and time with a
    /// UTC offset (<c>Z</c> or <c>±hh:mm</c>), ex. <c>2026-11-07T08:00:00Z</c>. A time without an offset is ambiguous
    /// across the utility's time zone and UTC, so it is rejected rather than guessed.
    /// </summary>
    public static Result<DateTimeOffset?> Parse(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return Result.Success<DateTimeOffset?>(null);

        if (!TryParseInstant(value, out var parsed))
            return Error.Validation(GridTopologyErrors.AsOfInvalid,
                "asOf must be an ISO 8601 date and time with a UTC offset, ex. 2026-11-07T08:00:00Z or 2026-11-07T00:00:00-08:00.");

        return Result.Success<DateTimeOffset?>(parsed);
    }

    /// <summary>
    /// Parses an ISO 8601 date and time with a UTC offset (<c>Z</c> or <c>±hh:mm</c>), the form every time the module
    /// accepts from a caller takes. A time without an offset is rejected rather than guessed.
    /// </summary>
    public static bool TryParseInstant(string? value, out DateTimeOffset instant)
    {
        instant = default;
        return value is not null
            && Iso8601WithOffset().IsMatch(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out instant);
    }

    public static string Format(DateTimeOffset at) => at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex Iso8601WithOffset();
}
