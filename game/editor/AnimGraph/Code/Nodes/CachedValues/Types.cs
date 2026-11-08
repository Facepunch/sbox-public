namespace Editor.AnimGraph.Nodes;

/// <summary>
/// When the cached value gets recorded.
/// </summary>
public enum CachedValueMode
{
	/// <summary>Cache the value when the node starts.</summary>
	OnEntry,

	/// <summary>Keep passing the live value through, caching it when the node stops.</summary>
	OnExit,
}
