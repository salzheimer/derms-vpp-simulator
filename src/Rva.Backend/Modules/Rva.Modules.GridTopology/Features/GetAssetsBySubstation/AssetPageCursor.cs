using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Rva.Modules.GridTopology.Features.GetAssetsBySubstation;

/// <summary>
/// Where the next page of a substation's assets starts: the listing (substation id), the topology version every page
/// reads, and the last asset id returned. Opaque to callers: base64url of <c>v1:&lt;substation&gt;:&lt;asOf μs since
/// the Unix epoch&gt;:&lt;last asset id&gt;</c>. The time is kept in microseconds, PostgreSQL's precision.
/// </summary>
internal sealed record AssetPageCursor(long SubstationId, long AsOfMicros, long AfterAssetId)
{
    private const string Version = "v1";

    public DateTimeOffset AsOf => DateTimeOffset.UnixEpoch.AddTicks(AsOfMicros * TimeSpan.TicksPerMicrosecond);

    public static long ToMicros(DateTimeOffset at) => (at.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / TimeSpan.TicksPerMicrosecond;

    public string Encode() =>
        Base64Url.EncodeToString(Encoding.ASCII.GetBytes(string.Join(':', Version,
            SubstationId.ToString(CultureInfo.InvariantCulture),
            AsOfMicros.ToString(CultureInfo.InvariantCulture),
            AfterAssetId.ToString(CultureInfo.InvariantCulture))));

    /// <summary>The cursor <paramref name="value"/> encodes, or null when it is not one this module issued.</summary>
    public static AssetPageCursor? TryDecode(string value)
    {
        string text;
        try
        {
            text = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(value));
        }
        catch (FormatException)
        {
            return null;
        }

        var parts = text.Split(':');
        return parts.Length == 4 && parts[0] == Version
            && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var substationId)
            && long.TryParse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var asOfMicros)
            && long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var afterAssetId)
            && asOfMicros >= ToMicros(DateTimeOffset.MinValue.ToUniversalTime()) && asOfMicros <= ToMicros(DateTimeOffset.MaxValue.ToUniversalTime())
                ? new AssetPageCursor(substationId, asOfMicros, afterAssetId)
                : null;
    }
}
