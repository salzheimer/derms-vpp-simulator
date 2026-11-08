using System.Globalization;
using System.Text.RegularExpressions;
using Rva.Modules.GridTopology.Contracts;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Features.Reads;

/// <summary>The <c>asOf</c> argument every read takes: checked by the handlers, parsed by the endpoints.</summary>
internal static partial class AsOf
{
    /// <summary>
    /// A Validation error when <paramref name="asOf"/> is later than now on <paramref name="time"/>: the topology's
    /// future is unknown, and answering with the current version would claim otherwise. Null when it is acceptable.
    /// </summary>
    public static Error? CheckNotInFuture(DateTimeOffset? asOf, TimeProvider time) =>
        asOf is { } at && at > time.GetUtcNow()
            ? Error.Validation(GridTopologyErrors.AsOfInFuture, $"asOf {Format(at)} is in the future.")
            : null;

    /// <summary>
    /// Parses the <c>asOf</c> query parameter: absent or empty means current; otherwise an ISO 8601 date and time with a
    /// UTC offset (<c>Z</c> or <c>±hh:mm</c>), ex. <c>2026-11-07T08:00:00Z</c>. A time without an offset is ambiguous
    /// across the utility's time zone and UTC, so it is rejected rather than guessed.
    /// </summary>
    public static Result<DateTimeOffset?> Parse(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return Result.Success<DateTimeOffset?>(null);

        if (!Iso8601WithOffset().IsMatch(value)
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return Error.Validation(GridTopologyErrors.AsOfInvalid,
                "asOf must be an ISO 8601 date and time with a UTC offset, ex. 2026-11-07T08:00:00Z or 2026-11-07T00:00:00-08:00.");

        return Result.Success<DateTimeOffset?>(parsed);
    }

    public static string Format(DateTimeOffset at) => at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex Iso8601WithOffset();
}
