using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Features.GetAssetsBySubstation;
using Rva.Modules.GridTopology.Features.Reads;

namespace Rva.Modules.GridTopology.Tests.Features;

/// <summary>The read arguments checked before any database call: <c>asOf</c> (C-22, C-23) and the page cursor (C-26).</summary>
public class ReadArgumentTests
{
    [Theory]
    [InlineData("2026-11-07T08:00:00Z", "2026-11-07T08:00:00+00:00")]
    [InlineData("2026-11-07T00:00:00-08:00", "2026-11-07T08:00:00+00:00")]
    [InlineData("2026-11-07T09:30+01:30", "2026-11-07T08:00:00+00:00")]
    [InlineData("2026-11-07T08:00:00.1234567Z", "2026-11-07T08:00:00.1234567+00:00")]
    public void AsOf_with_a_utc_offset_parses_to_the_instant(string value, string instant) =>
        Assert.Equal(DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture), AsOf.Parse(value).Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_absent_as_of_means_current(string? value) =>
        Assert.Null(AsOf.Parse(value).Value);

    [Theory]
    [InlineData("2026-11-07T08:00:00")]      // no offset
    [InlineData("2026-11-07")]
    [InlineData("2026-11-07 08:00:00Z")]     // not ISO 8601
    [InlineData("2026-02-30T08:00:00Z")]     // no such day
    [InlineData("11/07/2026 08:00 +00:00")]
    [InlineData("now")]
    public void A_malformed_as_of_is_a_validation_error(string value) =>
        Assert.Equal(GridTopologyErrors.AsOfInvalid, AsOf.Parse(value).Error.Code);

    [Fact]
    public void A_future_as_of_is_rejected_and_the_present_is_not()
    {
        var time = new FixedTime(new DateTimeOffset(2026, 11, 7, 8, 0, 0, TimeSpan.Zero));

        Assert.Null(AsOf.CheckNotInFuture(null, time));
        Assert.Null(AsOf.CheckNotInFuture(time.GetUtcNow(), time));
        Assert.Null(AsOf.CheckNotInFuture(time.GetUtcNow().AddDays(-1), time));
        Assert.Equal(GridTopologyErrors.AsOfInFuture, AsOf.CheckNotInFuture(time.GetUtcNow().AddTicks(10), time)?.Code);
    }

    [Fact]
    public void A_cursor_round_trips_with_its_time_at_microsecond_precision()
    {
        var at = new DateTimeOffset(2026, 11, 7, 8, 0, 0, TimeSpan.Zero).AddTicks(1234560);
        var cursor = new AssetPageCursor(42, AssetPageCursor.ToMicros(at), 9001);

        var decoded = AssetPageCursor.TryDecode(cursor.Encode());

        Assert.Equal(cursor, decoded);
        Assert.Equal(at, decoded!.AsOf);
    }

    [Theory]
    [InlineData("")]
    [InlineData("%%%")]
    [InlineData("djI6MToxOjE")]          // "v2:1:1:1": another version
    [InlineData("djE6YToxOjE")]          // "v1:a:1:1": not a number
    [InlineData("djE6MToxOjE6MQ")]       // "v1:1:1:1:1": too many parts
    public void A_cursor_this_module_did_not_issue_does_not_decode(string value) =>
        Assert.Null(AssetPageCursor.TryDecode(value));

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
