using Microsoft.Extensions.Options;

namespace Rva.Modules.GridTopology;

/// <summary>The <c>GridTopology</c> configuration section.</summary>
internal sealed class GridTopologyOptions
{
    public const string SectionName = "GridTopology";

    /// <summary>
    /// IANA id of the utility's time zone, ex. America/Los_Angeles. "Not in the future" checks on in-service and
    /// decommission dates use the local date there (D18).
    /// </summary>
    public string UtilityTimeZone { get; init; } = "";

    /// <summary>
    /// The most nodes and assets one substation tree response may hold; a larger subtree is a 400. Optional; the default
    /// covers a large distribution substation.
    /// </summary>
    public int MaxSubstationTreeSize { get; init; } = DefaultMaxSubstationTreeSize;

    public const int DefaultMaxSubstationTreeSize = 20_000;
}

/// <summary>Fails startup, naming the key, when the utility time zone is missing or not an IANA id.</summary>
internal sealed class GridTopologyOptionsValidator : IValidateOptions<GridTopologyOptions>
{
    public static readonly string UtilityTimeZoneKey = $"{GridTopologyOptions.SectionName}:{nameof(GridTopologyOptions.UtilityTimeZone)}";

    public ValidateOptionsResult Validate(string? name, GridTopologyOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.UtilityTimeZone))
            return ValidateOptionsResult.Fail($"{UtilityTimeZoneKey} is required.");

        // HasIanaId rejects Windows ids, which some platforms would otherwise convert and accept.
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(options.UtilityTimeZone, out var zone) || !zone.HasIanaId)
            return ValidateOptionsResult.Fail($"{UtilityTimeZoneKey} must be an IANA time zone id, ex. America/Los_Angeles (got '{options.UtilityTimeZone}').");

        if (options.MaxSubstationTreeSize < 1)
            return ValidateOptionsResult.Fail($"{GridTopologyOptions.SectionName}:{nameof(GridTopologyOptions.MaxSubstationTreeSize)} must be at least 1.");

        return ValidateOptionsResult.Success;
    }
}
