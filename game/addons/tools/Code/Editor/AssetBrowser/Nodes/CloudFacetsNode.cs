namespace Editor.AssetBrowsing.Nodes;

/// <summary>
/// A collapsible group node for a single facet dimension (e.g. "Rating", "Size").
/// Children are FacetValueNodes that toggle multi-select filters via CloudLocations.ActiveFilters.
/// </summary>
class FacetGroupNode : TreeNode.SmallHeader
{
	readonly Package.Facet _facet;

	public FacetGroupNode( Package.Facet facet ) : base( "label", facet.Title )
	{
		_facet = facet;
	}

	protected override void BuildChildren()
	{
		Clear();

		foreach ( var entry in _facet.Entries )
		{
			if ( entry.Name is "game" or "library" ) continue;
			AddItem( new FacetValueNode( entry, _facet.Name ) );
		}
	}
}
