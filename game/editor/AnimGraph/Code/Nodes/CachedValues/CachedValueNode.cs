using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Base behavior for values cached on entry to, or exit from, an active graph branch.
/// <br/><br/><b>Important:</b>&#160;Concrete Cached Value nodes document the fallback value used by each data type.
/// </summary>
public abstract class CachedValueNode : Node
{
	[Property]
	public CachedValueMode Mode { get; set; } = CachedValueMode.OnEntry;

	[JsonIgnore, Hide, Browsable( false )]
	public bool CacheOnInit => Mode == CachedValueMode.OnEntry;
}
