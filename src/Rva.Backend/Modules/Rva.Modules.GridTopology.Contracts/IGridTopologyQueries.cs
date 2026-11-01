namespace Rva.Modules.GridTopology.Contracts;

/// <summary>
/// Synchronous, in-process reads of grid topology for other modules. Every method returns <c>Task&lt;Result&lt;T&gt;&gt;</c>,
/// takes an optional <c>asOf</c> and a <c>CancellationToken</c>, and is served from rva_core, never from a cache.
/// The methods are added by the read features (plan p1-s01b to p1-s01g).
/// </summary>
public interface IGridTopologyQueries;
