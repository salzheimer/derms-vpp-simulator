namespace Rva.Modules.GridTopology.Contracts.Snapshots;

/// <summary>One page of the assets under a substation, ordered by asset id.</summary>
/// <param name="AsOf">
/// The topology version every page of this listing reads. When the first page is requested without one, it is the
/// database time of that request, carried in <paramref name="NextCursor"/>, so later pages neither gain nor lose assets
/// changed while paging.
/// </param>
/// <param name="NextCursor">Pass to get the next page; null on the last page.</param>
public sealed record AssetPage(IReadOnlyList<AssetSnapshot> Assets, DateTimeOffset AsOf, string? NextCursor);
