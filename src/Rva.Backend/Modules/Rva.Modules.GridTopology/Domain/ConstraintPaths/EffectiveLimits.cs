namespace Rva.Modules.GridTopology.Domain.ConstraintPaths;

/// <summary>
/// The per-asset limits along a constraint path (GT-04, GT-12): the minimum of the asset's own capability and every
/// site and microgrid limit that applies. A null limit means none is recorded at that level and is skipped; 0 is a
/// limit (D20). Both results are non-negative magnitudes in kW. Node ratings are shared by every asset below the node,
/// so dispatch allocates them; they are not folded in here.
/// </summary>
internal static class EffectiveLimits
{
    /// <param name="ratedPMaxKw">The asset's maximum power; positive = export.</param>
    /// <param name="assetExportLimitKw">The asset's per-system export limit.</param>
    /// <param name="siteExportLimitKw">The premise's export limit (0 = non-export).</param>
    /// <param name="microgridExportLimitKw">The microgrid's export limit at its PCC.</param>
    public static double Export(double ratedPMaxKw, double? assetExportLimitKw, double? siteExportLimitKw, double? microgridExportLimitKw) =>
        Minimum(Math.Max(0, ratedPMaxKw), assetExportLimitKw, siteExportLimitKw, microgridExportLimitKw);

    /// <param name="ratedPMinKw">The asset's minimum power; negative = import, so its magnitude is the charging capability.</param>
    /// <param name="siteServiceCapacityKva">The premise's service capacity, its import limit.</param>
    /// <param name="microgridImportLimitKw">The microgrid's import limit at its PCC.</param>
    public static double Import(double ratedPMinKw, double? siteServiceCapacityKva, double? microgridImportLimitKw) =>
        Minimum(Math.Max(0, -ratedPMinKw), siteServiceCapacityKva, microgridImportLimitKw);

    private static double Minimum(double capability, params double?[] limits)
    {
        var result = capability;
        foreach (var limit in limits)
        {
            if (limit is { } value && value < result)
                result = value;
        }
        return Math.Max(0, result);
    }
}
