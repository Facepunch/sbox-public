namespace Editor.AssetBrowsing.Nodes;

/// <summary>
/// A tree node representing a package type filter (e.g. "Models") within an optional parent
/// context (e.g. a collection or organisation). Shows an asset count and navigates on click.
/// Facet filtering for this type is handled by the cloud browser's filter bar, not by this node's children.
/// </summary>
class CloudTypeFilterNode : AssetFilterNode
{
	static readonly (string Name, string Label, string Icon)[] KnownTypes =
	[
		("model",    "Models",    "chair"),
		("material", "Materials", "broken_image"),
		("map",      "Maps",      "map"),
		("sound",    "Sound",     "volume_up"),
		("prefab",   "Prefabs",   "ballot"),
		("sprite",   "Sprites",   "image"),
		("decal",    "Decals",    "layers"),
	];

	public CloudTypeFilterNode( string typeName, string typeLabel, string typeIcon, string parentFilter )
		: base( typeIcon, typeLabel, BuildFilter( parentFilter, typeName ) )
	{
		_ = LoadCountAsync();
	}

	static string BuildFilter( string parent, string type ) =>
		string.IsNullOrWhiteSpace( parent ) ? $"type:{type}" : $"{parent.Trim()} type:{type}";

	public static IEnumerable<CloudTypeFilterNode> CreateForParent( string parentFilter )
	{
		foreach ( var (name, label, icon) in KnownTypes )
			yield return new CloudTypeFilterNode( name, label, icon, parentFilter );
	}

	async Task LoadCountAsync()
	{
		var result = await Package.FindAsync( $"{Filter} sort:popular", 1, 0 );
		Count = result.TotalCount;
		Dirty();
	}
}
